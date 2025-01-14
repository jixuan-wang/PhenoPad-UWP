﻿using PhenoPad.LogService;
using PhenoPad.PhenotypeService;
using PhenoPad.SpeechService;
using PhenoPad.WebSocketService;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.ApplicationModel.Core;
using Windows.Globalization;
using Windows.Media.Playback;
using Windows.Media.SpeechRecognition;
using Windows.Networking.Sockets;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using PhenoPad.FileService;
using Windows.UI.Xaml.Input;
using Windows.Foundation;
using System.Linq;

namespace PhenoPad
{
    // This is a partial class of Mainpage that contains methods regarding to video/audio functions including speech engine.
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        #region properties

        private bool _videoOn = false;
        public bool VideoOn
        {
            get
            {
                return this._videoOn;
            }

            set
            {
                if (value != this._videoOn)
                {
                    this._videoOn = value;
                    NotifyPropertyChanged("VideoOn");
                }
            }
        } //automation properties
        public string BLUETOOTHSERVICEURI = "";
        private bool _audioOn;
        public bool AudioOn
        {
            get
            {
                return this._audioOn;
            }

            set
            {
                if (value != this._audioOn)
                {
                    this._audioOn = value;
                    NotifyPropertyChanged("AudioOn");
                }
            }
        } //automation properties
        public bool bluetoonOn;


        public BluetoothService.BluetoothService bluetoothService = null;
        public UIWebSocketClient uiClinet = null;
        private int doctor = 0;
        public bool speechEngineRunning = false;
        public DispatcherTimer audioTimer = new DispatcherTimer();
        public SemaphoreSlim InitBTConnectionSemaphore;
        public SpeechManager speechManager = SpeechManager.getSharedSpeechManager();

        private SpeechRecognizer speechRecognizer; // The speech recognizer used throughout this sample.

        private PhenotypeManager PhenoMana = PhenotypeManager.getSharedPhenotypeManager();

        /// <summary>
        /// Keeps track of existing text that we've accepted in 
        /// ContinuousRecognitionSession_ResultGenerated(),
        /// so that we can combine it and Hypothesized results
        /// to show in-progress dictation mid-sentence.
        /// </summary>
        private StringBuilder dictatedTextBuilder;

        /// <summary>
        /// This HResult represents the scenario where a user is
        /// prompted to allow in-app speech, but declines. This
        /// should only happen on a Phone device, where speech is
        /// enabled for the entire device, not per-app.
        /// </summary>
        private static uint HResultPrivacyStatementDeclined = 0x80045509;

        /// <summary>
        /// Keep track of whether the continuous recognizer is currently 
        /// running, so it can be cleaned up appropriately.
        /// </summary>
        private bool isListening; 

        // For requesting server audio playbacks
        [XmlArray("Audios")]
        [XmlArrayItem("name")]
        public List<string> SavedAudios;
        SemaphoreSlim playbackSem;
        private bool isReading; // flag for reading audio stream
        private DispatcherTimer readTimer;
        private StreamWebSocket playbackStreamSocket;
        private CancellationTokenSource cancelSource;
        private CancellationToken token;
        private List<byte> audioBuffer;
        public static string SERVER_ADDR = "137.135.117.253"; //TODO: this address is outdated
        public static string SERVER_PORT = "8080";

        private int RETRIES = 3;
        private int RETRY_DELAY = 1000; // in milliseconds

        #endregion

        //======================================== START OF METHODS =======================================/

        /// <summary>
        /// Updates the bluetooth video camera stream status (turns video streaming ON/OFF).
        /// </summary>
        /// <param name="desiredStatus">Boolean value. True for ON, False for OFF.</param>
        /// <remarks>
        /// If Bluetooth connected, turn on video streaming by sending "camera start" to Raspberry Pi if desiredStatus is True;
        /// else turn off video streaming by sending "camera stop" to Raspberry Pi.
        /// </remarks>
        private async Task videoStreamStatusUpdateAsync(bool desiredStatus)
        {
            if (this.bluetoothService == null)
            {
                NotifyUser("Could not reach Bluetooth device, try to connect again",
                                   NotifyType.ErrorMessage, 2);
                this.VideoOn = false;
                this.StreamButton.IsChecked = false;
                return;
            }
            Debug.WriteLine("Sending message");

            if (desiredStatus)
            {
                await this.bluetoothService.sendBluetoothMessage("camera start");
            }
            else
            {
                await this.bluetoothService.sendBluetoothMessage("camera stop");
            }
            Debug.WriteLine("Setting status value to " + (this.bluetoothService.initialized && desiredStatus).ToString());
            this.VideoOn = this.bluetoothService.initialized && desiredStatus;
        }

        /// <summary>
        /// Changes chat view display when there's changes to the contents.
        /// </summary>
        /// <remarks>
        /// Subscribed to ChatListView.ContainerContentChanging event. Displays text message on the chat view container.
        /// - if message is not final:
        ///     Display message on the right (by setting the horizontal alignment of the item container to the right)
        /// - if message is final:
        ///     Display message on the right if speaker is doctor (currently set to 0), otherwise display on the left. 
        ///     Update chat view's layout and scroll the list to bring the last item (latest message) into view.
        /// </remarks>
        private async void OnChatViewContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            TextMessage message = (TextMessage)args.Item;

            if (message.IsNotFinal)
            {
                args.ItemContainer.HorizontalAlignment = HorizontalAlignment.Right;
            }
            else
            {
                args.ItemContainer.HorizontalAlignment = (message.Speaker == doctor) ? HorizontalAlignment.Right : HorizontalAlignment.Left;

                // Need this dispatcher in-order to avoid threading errors
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                    chatView.UpdateLayout();
                    chatView.ScrollIntoView(chatView.Items[chatView.Items.Count - 1]);
                });
            }
        }

        /// <summary>
        /// Sets up and runs video stream preview.
        /// </summary>
        /// <remarks>
        /// Is called in a preview-button-clicked event.
        /// When called:
        /// - Sets microphone options button status based on local setting.
        /// - Fetches and sets RPI address (which is not used anywhere).
        /// - Initializes videoStreamWebSocket and subscribe event handler functions to handle video stream socket events.  
        /// </remarks>
        private async void PreviewMultiMedia()
        {
            // Initialize microphone choice
            if (ConfigService.ConfigService.getConfigService().IfUseExternalMicrophone())
                ExternalMicRadioBtn.IsChecked = true;
            else
                SurfaceMicRadioBtn.IsChecked = true;

            // Acquire Raspberry Pi's IP address.
            string RPI_IP_ADDRESS = BluetoothService.BluetoothService.getBluetoothService().GetPiIP();

            // Initialize video stream websocket.
            this.videoStreamWebSocket = new Windows.Networking.Sockets.MessageWebSocket();

            // In this example, we send/receive a string, so we need to set the MessageType to Utf8.
            this.videoStreamWebSocket.Control.MessageType = Windows.Networking.Sockets.SocketMessageType.Utf8;
            this.videoStreamWebSocket.Closed += WebSocket_Closed;
            this.videoStreamWebSocket.MessageReceived += WebSocket_MessageReceived;

            // Connect and send command to Raspberry Pi.
            try
            {
                videoStreamCancellationToken = videoCancellationSource.Token;
                Task connectTask = this.videoStreamWebSocket.ConnectAsync(new Uri("ws://" + RPI_IP_ADDRESS + ":8000/websocket")).AsTask(); // NOTE: should the socket be hardcoded here?
                await connectTask.ContinueWith(_ => this.SendMessageUsingMessageWebSocketAsync("read_camera"));
            }
            catch (Exception ex)
            {
                Windows.Web.WebErrorStatus webErrorStatus = Windows.Networking.Sockets.WebSocketError.GetStatus(ex.GetBaseException().HResult);
            }
        }

        #region BLUETOOTH RELATED

        /// <summary>
        /// Handles Device Exceptions received from Raspberry Pi, 
        /// </summary>
        /// <remarks>
        /// Restarts bluetooth connection only (because automatically re-connecting causes errros). Stops speech service before closing bluetooth.
        /// </remarks>
        public async void RestartBTOnException()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => 
            {
                // Stops speech service first if it is running.
                if (speechEngineRunning)
                    await SpeechManager.getSharedSpeechManager().StopASRResults();

                bool result = bluetoothService.CloseConnection();

                if (result)
                {
                    SetBTUIOnInit(bluetoothService.initialized);
                    Debug.WriteLine("Bluetooth Connection disconnected");
                }
                else
                {
                    LogService.MetroLogger.getSharedLogger().Error("Bluetooth Connection failed to disconnect.");
                }
            });

            await Task.Delay(TimeSpan.FromSeconds(5));
            var success = await changeSpeechEngineState_BT();
            if (success)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    var result = await StartAudioAfterBluetooth();
                });
            }

            if (!success)
            {
                LogService.MetroLogger.getSharedLogger().Error("Bluetooth Connection failed to reconnect.");
            }
        }

        /// <summary>
        /// Runs when "Toggle Bluetooth" Button is clicked.
        /// </summary>
        /// <remarks>
        /// Subscribed to the BTConnectBtn(Toggle Bluetooth) clicked event.
        /// </remarks>
        private async void BTConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = true;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => {
                result = await changeSpeechEngineState_BT();
            });
        }

        /// <summary>
        /// Changes state of Bluetooth connection (called when "Toggle Bluetooth" Button is clicked).
        /// </summary>
        /// <returns>true if toggle state changed successfully, false otherwise</returns>
        /// <remarks>
        /// Called at the BTConnectBtn(Toggle Bluetooth) clicked event. 
        /// If Bluetooth is connected, also stops ASR service.
        /// </remarks>
        public async Task<bool> changeSpeechEngineState_BT()
        {
            bool result = true;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => {
                // If Bluetooth is not connected, connect.
                if (!bluetoonOn)
                {
                    try
                    {
                        bluetoothService = BluetoothService.BluetoothService.getBluetoothService();
                        await bluetoothService.Initialize();
                        SetBTUIOnInit(bluetoothService.initialized);
                        result = bluetoothService.initialized;
                    }
                    catch (Exception e)
                    {
                        LogService.MetroLogger.getSharedLogger().Error($"Failed to turn on BT: {e.Message}");
                        result = false;
                    }
                }
                // If Bluetooth connected, stop speech service first, then close Bluetooth connection.
                else
                {
                    try
                    {
                        if (speechEngineRunning)
                            await SpeechManager.getSharedSpeechManager().StopASRResults();
                        Debug.WriteLine("stopped ASR RESULT \n");

                        result = bluetoothService.CloseConnection();
                        if (result)
                        {
                            SetBTUIOnInit(bluetoothService.initialized);
                            Debug.WriteLine("Bluetooth Connection disconnected");
                            result = true;
                        }
                    }
                    catch (Exception e)
                    {
                        LogService.MetroLogger.getSharedLogger().Error($"Failed to turn off BT: {e.Message}");
                    }
                    result = false;
                }
            });
            return result;
        }

        /// <summary>
        /// Sends "start audio" command to Raspberry Pi to start recording and connects to the ASR server to receive ASR results.
        /// </summary>
        /// <returns>true if successfully sent command and connected to server, false otherwise</returns>
        /// <remarks>
        /// Called when choosing "external audio" option (i.e. using Raspberry Pi for audio).
        /// </remarks>
        public async Task<bool> StartAudioAfterBluetooth()
        {
            var success = false;
            DisableAudioButton();

            if (speechEngineRunning == false)
            {
                string uri = ASRAddrInput.Text.Trim();
                speechManager.CreateNewAudioName();
                string audioName = speechManager.GetAudioNameForServer();
                Debug.WriteLine($"start BT Audio, addr = {uri}");
                success = await bluetoothService.sendBluetoothMessage($"audio start manager_id=666 server_uri={uri} audiofile_name={audioName}"); //TODO: manager_id should not be hardcoded
                if (!success)
                {
                    LogService.MetroLogger.getSharedLogger().Error("failed to send audio start message to raspi");
                    return false;
                }
                success &= await SpeechManager.getSharedSpeechManager().ReceiveASRResults();
            }
            return success;
         }

        /// <summary>
        /// Set status of UI element parameters related to Bluetooth.
        /// </summary>
        /// <param name="initialized">Variable representing the state of the Bluetooth connection</param>
        /// <remarks>
        /// Assigns the value of *initialized* to the parameters. 
        /// Affected UI elements:
        ///     Video Button;
        ///     Audio button;
        ///     Shutter Button.
        /// </remarks>
        public void SetBTUIOnInit(bool initialized)
        {
            StreamButton.IsEnabled = initialized;
            shutterButton.IsEnabled = initialized;
            audioButton.IsEnabled = initialized;
            bluetoothicon.Visibility = initialized ? Visibility.Visible : Visibility.Collapsed;
            bluetoonOn = initialized;
        }

        #endregion

        #region Windows Speech Recognizer

        /// <summary>
        /// Initializes Speech Recognizer and sets up recognizer constraints.
        /// </summary>
        /// <param name="recognizerLanguage">Language to use for the speech recognizer.</param>
        /// <returns>Awaitable task.</returns>
        private async Task InitializeRecognizer(Language recognizerLanguage)
        {
            // Clean up prior to re-initializing.
            if (speechRecognizer != null)
            {
                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;
                speechRecognizer.ContinuousRecognitionSession.Completed -= ContinuousRecognitionSession_Completed;
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= ContinuousRecognitionSession_ResultGenerated;
                speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;

                this.speechRecognizer.Dispose();
                this.speechRecognizer = null;
            }

            this.speechRecognizer = new SpeechRecognizer(recognizerLanguage);

            // Provide feedback to the user about the state of the recognizer. 
            // This can be used to provide visual feedback in the form of an audio
            // indicator to help the user understand whether they're being heard.
            speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;

            // Apply the dictation topic constraint to optimize for dictated freeform speech.
            var dictationConstraint = new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation");
            speechRecognizer.Constraints.Add(dictationConstraint);
            SpeechRecognitionCompilationResult result = await speechRecognizer.CompileConstraintsAsync();

            if (result.Status != SpeechRecognitionResultStatus.Success)
            {
                Console.WriteLine("Grammar Compilation Failed: " + result.Status.ToString());
            }

            // Subscribe handler functions to handle continuous recognition events. 
            // - Completed is called when various error states occur. 
            // - ResultGenerated is called when some recognized phrases occur, or the garbage rule is hit. 
            // - HypothesisGenerated fires during recognition, and allows us to provide incremental feedback based on what the user's currently saying.
            speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;
            speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;
            speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;
        }

        /// <summary>
        /// Handles events where errors occur, such as the microphone becoming unavailable.
        /// </summary>
        /// <param name="sender">The continuous recognition session.</param>
        /// <param name="args">The result state of the recognition session.</param>
        /// <remarks>
        /// Subscribes to the event where a continuous recognition session ends, in the case in the event of an error.
        /// </remarks>
        private async void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
        {
            if (args.Status != SpeechRecognitionResultStatus.Success)
            {
                // If TimeoutExceeded occurs, the user has been silent for too long. We can use this to 
                // cancel recognition if the user in dictation mode and walks away from their device, etc.
                // In a global-command type scenario, this timeout won't apply automatically.
                // With dictation (no grammar in place) modes, the default timeout is 20 seconds.
                if (args.Status == SpeechRecognitionResultStatus.TimeoutExceeded)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        Console.WriteLine("Automatic Time Out of Dictation");
                        isListening = false;
                    });
                }
                else
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        Console.WriteLine("Continuous Recognition Completed: " + args.Status.ToString());
                        isListening = false;
                    });
                }
            }
        }

        /// <summary>
        /// While the user is speaking, update the textbox with partial speech 
        /// recognition results  for user feedback.
        /// </summary>
        /// <param name="sender">The recognizer which generated the hypothesis.</param>
        /// <param name="args">The recognition result fragment.</param>
        /// <remarks>
        /// Handler function that handles events where a recognition result fragment is 
        /// returned by the dictation session.
        /// </remarks>
        private async void SpeechRecognizer_HypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            string hypothesis = args.Hypothesis.Text;

            // Update the textbox with the confirmed text and the hypothesis combined.
            string textboxContent = dictatedTextBuilder.ToString() + " " + hypothesis + " ...";
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
            });
        }

        /// <summary>
        /// Update textbox content when a speech recognition result is generated.
        /// </summary>
        /// <param name="sender">The Recognition session which generated this result.</param>
        /// <param name="args">The complete recognition result.</param>
        /// <remarks>
        /// Handler function that handles events where a speech recognition result is generated.
        /// Check for high to medium confidence, and then append the string to the end of the stringbuffer,
        /// and replace the content of the textbox with the string buffer, to remove any hypothesis text 
        /// that may be present.
        /// </remarks>
        private async void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            // We may choose to discard content that has low confidence, as that could 
            // indicate that we're picking up noise via the microphone, or someone could 
            // be talking out of earshot.
            if (args.Result.Confidence == SpeechRecognitionConfidence.Medium ||
                args.Result.Confidence == SpeechRecognitionConfidence.High)
            {
                dictatedTextBuilder.Append(args.Result.Text + " ");

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                });
            }
            else
            {
                // In some scenarios, a developer may choose to ignore giving the user 
                // feedback in this case, if speech is not the primary input mechanism 
                // for the application. Here, just discard the hypothesis text.
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    string discardedText = args.Result.Text;
                    if (!string.IsNullOrEmpty(discardedText))
                    {
                        discardedText = discardedText.Length <= 25 ? discardedText : (discardedText.Substring(0, 25) + "...");
                        Console.WriteLine("Discarded due to low/rejected Confidence: " + discardedText);
                    }
                });
            }
        }

        /// <summary>
        /// Notify the user when the recognizer is receiving their voice input.
        /// </summary>
        /// <param name="sender">The recognizer that is currently running.</param>
        /// <param name="args">The current state of the recognizer.</param>
        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => 
            {
                this.NotifyUser(args.State.ToString(), NotifyType.StatusMessage, 1); //TODO: this is probably a bad way to do this, although this code is not used.
                Console.WriteLine(args.State.ToString());
            });
        }

        #endregion

        /// <summary>
        /// Runs a funtion until successful excecution or maximum number of attempts reached.
        /// </summary>
        /// <param name="retries">Maximum number of attempts to run function.</param>
        /// <param name="f">The target function which returns a boolean value that represents success/failure.</param>
        /// <returns>true if function successfully excecuted, false otherwise</returns>
        private async Task<bool> RunFuncWithRetries (int retries, Task<bool> f)
        {
            bool success = await f;
            int retryCount = retries;
            
            // retry if task failed 
            while(retryCount > 0 && !success)
            {
                // wait 1 second before retrying
                await Task.Delay(RETRY_DELAY);
                retryCount--;
                success = await f;
            }
            return success;
        }

        /// <summary>
        /// Invoked when user presses the Microphone button on sidebar, starts/stops speech service.
        /// </summary>
        private async void AudioStreamButton_Clicked(object sender = null, RoutedEventArgs e = null)
        {
            //Temporarily disables audio button for avoiding frequent requests
            DisableAudioButton();

            // if using external microphone (Raspberry Pi)
            if (ConfigService.ConfigService.getConfigService().IfUseExternalMicrophone())
            {
                // Notify the user if Bluetooth is not connected.
                if (!bluetoonOn)
                {
                    NotifyUser("Bluetooth is not connected, cannot use External Microphone.", NotifyType.ErrorMessage, 2);
                    ReEnableAudioButton();
                }
                else
                {
                    // If ASR is already on, turn off ASR.
                    if (speechEngineRunning)
                    {
                        var success = await RunFuncWithRetries(RETRIES, SpeechManager.getSharedSpeechManager().StopASRResults());
                    }
                    // If Bluetooth is connected and ASR isn't already on, start ASR.
                    else
                    {
                        var success = await RunFuncWithRetries(RETRIES, StartAudioAfterBluetooth());
                    }

                }
            }
            // If using internal microphone.
            else
            {
                var success = await RunFuncWithRetries(RETRIES, changeSpeechEngineState());
                ReEnableAudioButton();
            }
        }

        /// <summary>
        /// Starts/stops speech service when using internal/plug-in microphones.
        /// </summary>
        /// <returns>true if successfully start/stop audion, false otherwise</returns>
        /// <remarks>
        /// Called when choosing the "interal mic" option (i.e. not using the Raspberry Pi for audio).
        /// If speech service is not running, start speech; otherwise, end speech.
        /// </remarks>
        public async Task<bool> changeSpeechEngineState()
        {
            try
            {
                var success = true;

                if (speechEngineRunning == false)
                {
                    NotifyUser("Starting Audio using internal microphone ...", NotifyType.StatusMessage, 7);
                    success = await SpeechManager.getSharedSpeechManager().StartAudio();
                }
                else
                {
                    NotifyUser("Ending Audio ...", NotifyType.StatusMessage, 2);
                    success = await SpeechManager.getSharedSpeechManager().EndAudio(notebookId);
                }
                return success;
            }
            catch (Exception ex)
            {
                LogService.MetroLogger.getSharedLogger().Error("Failed to start/stop audio: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Updates mainpage UI elements and notifies user when new speech session starts.
        /// </summary>
        public void onAudioStarted(object sender = null, object e = null)
        {
            audioTimer.Stop(); //TODO: What does audioTimer do exactly? audioTimer.start() is called only during demos.
            speechEngineRunning = true;
            NotifyUser("Audio service started.", NotifyType.StatusMessage, 1);
            LogService.MetroLogger.getSharedLogger().Info("Audio started.");
            ReEnableAudioButton();
        }

        /// <summary>
        /// Updates vairable values and notifies the user when speech service ends.
        /// </summary>
        public void onAudioEnded()
        {
            speechEngineRunning = false;
            NotifyUser("Audio service ended.", NotifyType.StatusMessage, 1);
            LogService.MetroLogger.getSharedLogger().Info("Audio stopped.");
            ReEnableAudioButton();
        }

        /// <summary>
        /// Makes the audio button un-interactable.
        /// </summary>
        public void DisableAudioButton()
        {
            audioButton.IsEnabled = false;
            audioStatusText.Text = "...";
        }

        /// <summary>
        /// Re-enables the audio button and provide user with visual feedback of speech service's current state through text.
        /// </summary>
        public async void ReEnableAudioButton(object sender = null, object e = null)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => 
            {
                await Task.Delay(TimeSpan.FromSeconds(7));
                audioButton.IsEnabled = true;
                if (speechEngineRunning)
                {
                    audioStatusText.Text = "ON";
                    audioButton.IsChecked = true;
                }
                else
                {
                    audioStatusText.Text = "OFF";
                    audioButton.IsChecked = false;
                }
            });
        }

        /// <summary>
        /// Stops speech service.
        /// </summary>
        /// <remarks>
        /// Stops speech service by calling AudioStreamButton_Clicked.
        /// </remarks>
        public async Task KillAudioService()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.High, async () => 
            {
                if (speechEngineRunning)
                {
                    // close all audio services before navigating
                    Debug.WriteLine("Killing audio service");
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => 
                    {
                        AudioStreamButton_Clicked();
                    });
                    await Task.Delay(TimeSpan.FromSeconds(7));
                }
                else
                {
                    Debug.WriteLine("Cannot kill audio service: audio service not running");
                }
            });
            return;
        }

        /// <summary>
        /// Add the name of a newly created audio file to the saved list of audio file names.
        /// </summary>
        /// <param name="name">the name of the new audio file</param>
        /// <remarks>
        /// Called when a new audio file is available (e.g. when a new speech session is completed).
        /// Add the new audio's name to the AudioMeta xml file to keep track of audio files created
        /// from this note.
        /// </remarks>
        public async void SaveNewAudioName(string name)
        {
            this.SavedAudios.Add(name);
            await FileService.FileManager.getSharedFileManager().SaveAudioNamesToXML(notebookId, SavedAudios);
            speechManager.ClearCurAudioName();
        }


        /// <summary>
        /// Downloads an audio segment from the speech server and plays the audio.
        /// </summary>
        /// <param name="audioFileName">the name of the saved audio file</param>
        /// <param name="start">the starting timestamp of the audio segment</param>
        /// <param name="end">the ending timestamp of the audio segment</param>
        public async void PlayMedia(string audioFileName,double start = 0, double end = 0)
        {
            // Don't process requests with invalid intervals
            if (start >= end)
            {
                return;
            }
            if (playbackSem.CurrentCount == 0)
            {
                Debug.WriteLine("semaphore currently inuse, will abort");
                return;
            }
            await playbackSem.WaitAsync();
            playbackStreamSocket = new StreamWebSocket();
            try
            {
                string audioName = $"666_{notebookId}_{audioFileName} {start} {end}";
                String serverAddr = ASRAddrInput.Text.Trim().Split(':')[0];
                String serverPort = ASRAddrInput.Text.Trim().Split(':')[1];
                Uri serverUri = new Uri("ws://" + serverAddr + ":" + serverPort + "/client/ws/file_request");
                Task connectTask = playbackStreamSocket.ConnectAsync(serverUri).AsTask();

                await connectTask;
                if (connectTask.Exception != null)
                {
                    MetroLogger.getSharedLogger().Error("connectTask.Exception:" + connectTask.Exception.Message);
                    throw new Exception(connectTask.Exception.Message);
                }

                // Send the requested audio name to server through buffer
                var bytes = Encoding.UTF8.GetBytes(audioName);
                await playbackStreamSocket.OutputStream.WriteAsync(bytes.AsBuffer());

                // Receive audio from server
                uint length = 1000000; // Leave a large buffer
                audioBuffer = new List<Byte>();
                isReading = true;
                cancelSource = new CancellationTokenSource();
                token = cancelSource.Token;
                while (isReading)
                {
                    readTimer.Start();
                    IBuffer op = await playbackStreamSocket.InputStream.ReadAsync(new Windows.Storage.Streams.Buffer(length), length, InputStreamOptions.Partial).AsTask(token);
                    if (op.Length > 0)
                        audioBuffer.AddRange(op.ToArray());
                    readTimer.Stop();
                }
            }
            catch (TaskCanceledException)
            {
                // Finished receiving audio. Play the audio received from server.
                readTimer.Stop();
                Debug.WriteLine("------------------END RECEIVING" + audioBuffer.Count + "----------------");
                MemoryStream mem = new MemoryStream(audioBuffer.ToArray());
                MediaPlayer player = new MediaPlayer();
                player.SetStreamSource(mem.AsRandomAccessStream());
                player.Play();
                Debug.WriteLine("done");
            }
            catch (Exception ex)
            {
                LogService.MetroLogger.getSharedLogger().Error("playback:" + ex.Message);
                Current.NotifyUser("Failed to get audio from server, please try again later", NotifyType.ErrorMessage, 1);
            }
            playbackStreamSocket.Dispose();
            playbackStreamSocket = null;
            playbackSem.Release();
        }

        /// <summary>
        /// Download audio from the speech server and save to a local save file.
        /// </summary>
        /// <param name="audioName">the name of the audio file</param>
        /// <returns>(bool)true if download and save successful, (bool)false otherwise</returns>
        public async Task<bool> GetRemoteAudioAndSave(string audioName)
        {
            // Don't process requests with invalid intervals
            if (playbackSem.CurrentCount == 0)
            {
                return false;
            }

            await playbackSem.WaitAsync();
            playbackStreamSocket = new StreamWebSocket();
            try
            {
                string audioserverName = $"666_{notebookId}_" + audioName;
                Uri serverUri = new Uri("ws://" + SERVER_ADDR + ":" + SERVER_PORT + "/client/ws/file_request");
                Task connectTask = playbackStreamSocket.ConnectAsync(serverUri).AsTask();

                await connectTask;
                if (connectTask.Exception != null)
                {
                    MetroLogger.getSharedLogger().Error("connectTask.Exception:" + connectTask.Exception.Message);
                    throw new Exception(connectTask.Exception.Message);
                }

                // Send the requested audio name to server through buffer
                var bytes = Encoding.UTF8.GetBytes(audioserverName);
                await playbackStreamSocket.OutputStream.WriteAsync(bytes.AsBuffer());

                uint length = 1000000; // Leave a large buffer
                audioBuffer = new List<Byte>();
                isReading = true;
                cancelSource = new CancellationTokenSource();
                token = cancelSource.Token;
                while (isReading)
                {
                    readTimer.Start();
                    IBuffer op = await playbackStreamSocket.InputStream.ReadAsync(new Windows.Storage.Streams.Buffer(length), length, InputStreamOptions.Partial).AsTask(token);
                    if (op.Length > 0)
                        audioBuffer.AddRange(op.ToArray());
                    readTimer.Stop();
                }
            }
            catch (TaskCanceledException)
            {
                // saves the audio received from server
                readTimer.Stop();
                Debug.WriteLine("------------------END RECEIVING " + audioBuffer.Count + "----------------");
                if (audioBuffer.Count == 0)
                    throw new Exception("No bytes in audio buffer");
                bool success = await FileManager.getSharedFileManager().SaveByteAudioToFile(notebookId, audioName, audioBuffer);
                if (!success)
                    throw new Exception("Failed to save remote audio locally");
            }
            catch (Exception ex)
            {
                LogService.MetroLogger.getSharedLogger().Error("playback:" + ex.Message);
                Current.NotifyUser("Failed to get audio from server, please try again later", NotifyType.ErrorMessage, 1);
                return false;
            }
            finally {
                playbackStreamSocket.Dispose();
                playbackStreamSocket = null;
                playbackSem.Release();
            }
            return true;
        }

        /// <summary>
        /// Subscribed to readTimer.Tick event
        /// </summary>
        private void EndAudioStream(object sender = null, object e = null)
        {
            isReading = false;
            cancelSource.Cancel();
        }

        /// <summary>
        /// handling when user double taps on the speech bubble in the realtime conversation grid
        /// </summary>
        private void SpeechBubble_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {

            TextBlock tb = ((TextBlock)sender);
            string body = tb.Text;

            //Gets the position of note content grid for pop up alignment
            var element_Visual_Relative = NoteGrid.TransformToVisual(this);
            Point pos_grid = element_Visual_Relative.TransformPoint(new Point(0, 0));

            var element_Visual_Relative2 = tb.TransformToVisual(this);
            Point pos_bubble = element_Visual_Relative2.TransformPoint(new Point(0, 0));

            TextMessage tm = speechManager.speechInterpreter.GetTextMessage(body);

            if (tm != null && tm.phenotypesInText.Count > 0)
            {
                PhenoInSpeechListView.ItemsSource = tm.phenotypesInText;
                showingPhenoSpeech = tm.phenotypesInText; //TODO: what is this?
                Canvas.SetLeft(PhenotypePopup, pos_grid.X);
                Canvas.SetTop(PhenotypePopup, pos_bubble.Y - 50);
                PhenotypePopup.Visibility = Visibility.Visible;
                UpdateLayout();
            }
        }

        /// <summary>
        /// Saves ASR transcripts to local save file if there are ASR results
        /// </summary>
        public async Task SaveCurrentConversationsToDisk()
        {
            if ( speechManager.speechInterpreter.CurrentConversationHasContent() || Current.conversations.Count > 0 )
            {  
                try
                {
                    string fpath = FileManager.getSharedFileManager().GetNoteFilePath(
                        FileManager.getSharedFileManager().currentNoteboookId, "", NoteFileType.Transcriptions, "transcripts");
                    
                    var result = await FileManager.getSharedFileManager().SaveObjectSerilization(fpath, conversations, typeof(List<TextMessage>));
                    Debug.WriteLine($"transcripts saved to {fpath}, result = {result}");
                }
                catch (Exception e)
                {
                    MetroLogger.getSharedLogger().Error("Failed to save current conversations transcriptions into disk: " + e.Message);
                }
            }
        }
    }
}
