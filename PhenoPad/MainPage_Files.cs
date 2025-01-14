﻿using System;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using PhenoPad.PhenotypeService;
using System.Collections.Generic;
using Windows.UI.Popups;
using System.Threading.Tasks;
using PhenoPad.SpeechService;
using Windows.UI;
using System.Diagnostics;
using PhenoPad.CustomControl;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;
using Microsoft.Graphics.Canvas;
using PhenoPad.FileService;
using System.ComponentModel;
using System.Threading;
using PhenoPad.LogService;
using Windows.Storage;

namespace PhenoPad
{
    //This partial class of MainPage mainly contains logic methods for file I/O such as save / load
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        // Using a semaphore to ensure only one thread is accessing resources,
        // its purpose is to avoid concurrent accesses during the saving process
        private SemaphoreSlim savingSemaphoreSlim = new SemaphoreSlim(1);
        public bool loadFromDisk = false;
        string prefix = "transcriptions_";

        // Stores all TextMessages in the notebook, is the ItemSource for SpeechPage.ChatView
        public List<TextMessage> conversations;

        //=============================================================================================

        /// <summary>
        /// Initializes the new notebook and creates a locally saved file for it.
        /// </summary>
        public async void InitializeNotebook()
        {
            LoadingPopup.IsOpen = true;
            MetroLogger.getSharedLogger().Info("Initialize a new notebook.");
            PhenoMana.clearCache();
            conversations = new List<TextMessage>(); // Stores all TextMessages in the notebook,
                                                     // items in this list are displayed in SpeechPage's
                                                     // speech bubbles.
            SpeechPage.Current.updateChat();

            // Tries to create a file structure for the new notebook.
            {
                notebookId = FileManager.getSharedFileManager().createNotebookId();
                FileManager.getSharedFileManager().currentNoteboookId = notebookId;
                bool result = await FileManager.getSharedFileManager().CreateNotebook(notebookId);
                SpeechManager.getSharedSpeechManager().setAudioIndex(0);
                if (!result)
                    NotifyUser("Failed to create file structure, notes may not be saved.", NotifyType.ErrorMessage, 2);
                else
                    notebookObject = await FileManager.getSharedFileManager().GetNotebookObjectFromXML(notebookId);

                if (notebookObject != null)
                    noteNameTextBox.Text = notebookObject.name;
            }

            notePages = new List<NotePageControl>();
            pageIndexButtons = new List<Button>();

            NotePageControl aPage = new NotePageControl(notebookId,"0");
            notePages.Add(aPage);
            inkCanvas = aPage.inkCan;
            MainPageInkBar.TargetInkCanvas = inkCanvas;
            curPage = aPage;
            curPageIndex = 0;
            PageHost.Content = curPage;
            setPageIndexText(0);

            currentMode = WritingMode;
            modeTextBlock.Text = WritingMode;
            bool cur_mic = ConfigService.ConfigService.getConfigService().IfUseExternalMicrophone();
            if (cur_mic == false)
                SurfaceMicRadioButton_Checked();
            else
                ExterMicRadioButton_Checked();
            // create file sturcture for this page
            await FileManager.getSharedFileManager().CreateNotePage(notebookObject, curPageIndex.ToString());
            OperationLogger.getOpLogger().SetCurrentNoteID(notebookId);

            ExpandButton.Visibility = Visibility.Collapsed;
            await Task.Delay(TimeSpan.FromSeconds(3));
            LoadingPopup.IsOpen = false;
            curPage.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Initializes the notebook by loading from pre-existing local save file.
        /// </summary>
        public async void InitializeNotebookFromDisk()
        {

            LoadingPopup.IsOpen = true;
            MetroLogger.getSharedLogger().Info("Initializing notebook from disk ...");
            PhenoMana.clearCache();
            try
            {
                // if notebook file exists, continues with loading...
                notebookObject = await FileManager.getSharedFileManager().GetNotebookObjectFromXML(notebookId);

                if (notebookObject == null) {
                    Debug.WriteLine("notebook Object is null");
                }

                // gets all stored pages and notebook object from the disk
                List<string> pageIds = await FileManager.getSharedFileManager().GetPageIdsByNotebook(notebookId);

                bool cur_mic = ConfigService.ConfigService.getConfigService().IfUseExternalMicrophone();
                if (cur_mic == false)
                { 
                    SurfaceMicRadioButton_Checked();
                }
                else
                { 
                    ExterMicRadioButton_Checked();
                }
                noteNameTextBox.Text = notebookObject.name;

                // Get the stored conversation transcripts and audio names from XML meta if they exist
                conversations =  await FileManager.getSharedFileManager().GetSavedTranscriptsFromXML(notebookId);
                if (conversations != null)
                    Current.conversations = conversations;
                else
                    Current.conversations = new List<TextMessage>();
                List<string> audioNames = await FileManager.getSharedFileManager().GetSavedAudioNamesFromXML(notebookId);
                if (audioNames != null)
                    this.SavedAudios = audioNames;
                Debug.WriteLine("mainpage audionames null" + audioNames == null);

                SpeechPage.Current.updateChat();
                // Gets all saved phenotypes from XML meta
                List<Phenotype> phenos = await FileManager.getSharedFileManager().GetSavedPhenotypeObjectsFromXML(notebookId);
                if (phenos != null && phenos.Count > 0)
                { 
                    PhenotypeManager.getSharedPhenotypeManager().addPhenotypesFromFile(phenos);
                }

                // Gets all phenotype candidates from XML meta
                bool init_analyze = false;
                List<Phenotype> phenocand = await FileManager.getSharedFileManager().GetSavedPhenotypeObjectsFromXML(notebookId,NoteFileType.PhenotypeCandidates);
                if (phenocand != null && phenocand.Count > 0)
                {
                    PhenotypeManager.getSharedPhenotypeManager().addPhenotypeCandidateFromFile(phenocand);
                }
                else if (phenocand == null)
                {
                    init_analyze = true;
                }

                // Process loading note pages one by one
                notePages = new List<NotePageControl>();
                pageIndexButtons = new List<Button>();
                bool has_EHR = false;
                //
                for (int i = 0; i < pageIds.Count; ++i)
                {
                    NotePageControl aPage = new NotePageControl(notebookObject.name, i.ToString());
                    notePages.Add(aPage);
                    aPage.pageId = pageIds[i];
                    aPage.notebookId = notebookId;

                    string text = await FileManager.getSharedFileManager().LoadNoteText(notebookId, pageIds[i]);
                    aPage.setTextNoteEditBox(text);
                    // Check if there's an EHR file in the page
                    StorageFile ehr = await FileManager.getSharedFileManager().GetNoteFileNotCreate(notebookId, i.ToString(), NoteFileType.EHR);
                    if (ehr != null)
                    {
                        has_EHR = true;
                        await aPage.SwitchToEHR(ehr);
                    }

                    // Load strokes and parse strokes to line to dictionary
                    bool result = await FileManager.getSharedFileManager().LoadNotePageStroke(notebookId, pageIds[i], aPage);
                    aPage.InitAnalyzeStrokes();
                    
                    // Load image/drawing addins
                    List<ImageAndAnnotation> imageAndAnno = await FileManager.getSharedFileManager().GetImgageAndAnnotationObjectFromXML(notebookId, pageIds[i]);
                    if (imageAndAnno == null) {
                    }
                    else
                    {
                        // loop to add actual add-in to canvas but hides it depending on its inDock value
                        foreach (var ia in imageAndAnno)
                        { 
                            aPage.loadAddInControl(ia);
                        }
                    }

                    List<RecognizedPhrases> recogPhrases = await FileManager.getSharedFileManager().GetRecognizedPhraseFromXML(notebookId, pageIds[i]);
                    if (recogPhrases != null && recogPhrases.Count > 0)
                        aPage.loadRecognizedPhrases(recogPhrases);

                    // If no saved phenotype candidates, initial analyze on each page 
                    if (init_analyze)
                    {
                        aPage.initialAnalyze();
                    }
                    else
                    {
                        aPage.initialAnalyzeNoPhenotype();
                    }
                }

                curPage = notePages[0];
                curPageIndex = 0;
                PageHost.Content = curPage;
                setPageIndexText(curPageIndex);

                // Shows add-in icons into side bar
                var addins = await curPage.GetAllAddInObjects();
                showAddIn(addins);

                // Set initial page to first page and auto-start analyzing strokes
                if (!has_EHR)
                {
                    // initializing for regular note page
                    inkCanvas = notePages[0].inkCan;
                }
                else {
                    // current implementation assumes if there's ehr, it must be on first page
                    inkCanvas = notePages[0].ehrPage.annotations;
                }
                OperationLogger.getOpLogger().SetCurrentNoteID(notebookId);
                var count = PhenoMana.ShowPhenoCandAtPage(curPageIndex);
                if (count <= 0)
                    CloseCandidate();

                MainPageInkBar.TargetInkCanvas = inkCanvas;
                await Task.Delay(TimeSpan.FromSeconds(2));
                LoadingPopup.IsOpen = false;
                curPage.Visibility = Visibility.Visible;
            }
            catch (NullReferenceException ne)
            {
                // Note: NullReferenceException is very likely to happen 
                //       when things aren't saved properlly during debugging 
                //       state due to force quit
                MetroLogger.getSharedLogger().Error( ne + ne.Message );
                await PromptRemakeNote(notebookId);
                return;
            }
            catch (Exception e)
            {
                MetroLogger.getSharedLogger().Error($"Failed to Initialize Notebook From Disk:{e}:{e.Message}");
            }
        }

        /// <summary>
        /// Initializes the EHR Text file from a picked .txt file
        /// </summary>
        public async void InitializeEHRNote(StorageFile file)
        {
            PhenoMana.clearCache();

            // If user cancels choosing a file or file is not valid, just create a new notebook
            if (file == null) {
                NotifyUser("No EHR file, please paste EHR text",NotifyType.StatusMessage,2);
            }

            // Tries to create a file structure for the new notebook.
            {
                notebookId = FileManager.getSharedFileManager().createNotebookId();
                FileManager.getSharedFileManager().currentNoteboookId = notebookId;
                bool result = await FileManager.getSharedFileManager().CreateNotebook(notebookId);
                SpeechManager.getSharedSpeechManager().setAudioIndex(0);
                if (!result)
                    NotifyUser("Failed to create file structure, notes may not be saved.", NotifyType.ErrorMessage, 2);
                else
                    notebookObject = await FileManager.getSharedFileManager().GetNotebookObjectFromXML(notebookId);

                if (notebookObject != null)
                    noteNameTextBox.Text = notebookObject.name;
            }

            notePages = new List<NotePageControl>();
            pageIndexButtons = new List<Button>();

            // Initializs audio microphone service
            bool cur_mic = ConfigService.ConfigService.getConfigService().IfUseExternalMicrophone();
            if (cur_mic == false)
            { 
                SurfaceMicRadioButton_Checked();
            }
            else
            { 
                ExterMicRadioButton_Checked();
            }

            NotePageControl aPage = new NotePageControl(notebookId, "0");
            notePages.Add(aPage);
            curPage = aPage;
            curPageIndex = 0;
            PageHost.Content = curPage;
            setPageIndexText(0);
            // create file sturcture for this page
            await FileManager.getSharedFileManager().CreateNotePage(notebookObject, curPageIndex.ToString());

            await curPage.SwitchToEHR(file);
            inkCanvas = curPage.ehrPage.annotations;
            MainPageInkBar.TargetInkCanvas = inkCanvas;
            OperationLogger.getOpLogger().SetCurrentNoteID(notebookId);
            var addins = await curPage.GetAllAddInObjects();
            showAddIn(addins);

            currentMode = WritingMode;
            modeTextBlock.Text = WritingMode;
            AbbreviationON_Checked(null, null);
            await Task.Delay(TimeSpan.FromSeconds(3));
            LoadingPopup.IsOpen = false;
            curPage.Visibility = Visibility.Visible;
        }

        public async Task PromptRemakeNote(string notebookId)
        {
            var messageDialog = new MessageDialog("This Notebook seems to be corrupted and cannot be loaded, please recreate a new note.");
            messageDialog.Title = "Error";
            messageDialog.Commands.Add(new UICommand("OK") { Id = 0 });
            messageDialog.DefaultCommandIndex = 0;

            await messageDialog.ShowAsync();

            await FileManager.getSharedFileManager().DeleteNotebookById(notebookId);
            Frame.Navigate(typeof(PageOverview));
        }


        /// <summary>
        /// Save everything to disk, include: 
        /// handwritten strokes, typing words, photos and annotations, drawing, collected phenotypes
        /// </summary>
        public async Task<bool> saveNoteToDisk()
        {
            
            await savingSemaphoreSlim.WaitAsync(); // locks semaphore before accessing
            bool pgResult = true;
            bool flag = false;
            bool result2 = false;
            try
            {
                // Save note pages one by one
                foreach (var page in notePages)
                {
                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                        CoreDispatcherPriority.Normal, async () => {
                            flag = await page.SaveToDisk();
                            if (!flag)
                            {
                                MetroLogger.getSharedLogger().Error($"Page {page.pageId} failed to save.");
                                pgResult = false;
                            }
                        }
                    );
                }
                
                await Current.SaveCurrentConversationsToDisk(); // save the transcripts from past speeches;

                result2 = await FileManager.getSharedFileManager().saveCollectedPhenotypesToFile(notebookId);
                result2 &= await FileManager.getSharedFileManager().SaveAudioNamesToXML(notebookId, SavedAudios);

                if (! (pgResult && result2))
                    MetroLogger.getSharedLogger().Info($"Some parts of notebook {notebookId} failed to save.");
            }
            catch (NullReferenceException)
            {
                //This exception may be encountered when attemping to click close during main page and there's no
                //valid notebook id provided. Technically nothing needs to be done here              
            }
            catch (Exception ex)
            {
                MetroLogger.getSharedLogger().Error("Failed to save notebook: " + ex + ex.Message);
            }
            finally
            {
                savingSemaphoreSlim.Release();               
            }
            return pgResult && result2;
        }


        /// <summary>
        /// Saves current added phenotypes to local file
        /// </summary>
        public async Task<bool> AutoSavePhenotypes()
        {
            bool complete = await FileManager.getSharedFileManager().saveCollectedPhenotypesToFile(notebookId);
            if (!complete)
            { 
                MetroLogger.getSharedLogger().Error("Failed to auto-save collected phenotypes.");
            }
            return complete;
        }

        /// <summary>
        /// Load everything from disk, include: 
        /// handwritten strokes, typing words, photos and annotations, drawing, collected phenotypes.
        /// </summary>
        private async Task<bool> loadNoteFromDisk()
        {
            bool isSuccessful = true;
            bool result;

            // Save all note pages
            for (int i = 0; i < notePages.Count; ++i)
            {
                result = await FileManager.getSharedFileManager().SaveNotePageStrokes(notebookId, i.ToString(), notePages[i]); // save handwritten strokes of the page
            }

            // Collected phenotypes
            result = await FileManager.getSharedFileManager().saveCollectedPhenotypesToFile(notebookId);
            if (result)
                Debug.WriteLine("Successfully save collected phenotypes.");
            else
            {
                Debug.WriteLine("Failed to save collected phenotypes.");
                isSuccessful = false;
            }

            return isSuccessful;
        }

        /// <summary>
        /// Gets all strokers from the inkCanvas and saves the strokes as .gif file to user selected folder.
        /// Return 1 if success, 0 if failed and 2 if canceled.
        /// </summary>
        private async Task<int> SaveStroketoDiskAsGif()
        {
            // Get all strokes on the InkCanvas.
            IReadOnlyList<InkStroke> currentStrokes = inkCanvas.InkPresenter.StrokeContainer.GetStrokes();

            // Strokes present on ink canvas.
            if (currentStrokes.Count > 0)
            {
                // Let users choose their ink file using a file picker.
                // Initialize the picker.
                FileSavePicker savePicker = new FileSavePicker();
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("GIF with embedded ISF", new List<string>() { ".gif" });
                savePicker.DefaultFileExtension = ".gif";
                savePicker.SuggestedFileName = "InkSample";

                // Show the file picker.
                Windows.Storage.StorageFile file = await savePicker.PickSaveFileAsync();
                // When chosen, picker returns a reference to the selected file.
                if (file != null)
                {
                    // Prevent updates to the file until updates are finalized with call to CompleteUpdatesAsync.
                    Windows.Storage.CachedFileManager.DeferUpdates(file);

                    IRandomAccessStream stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);

                    // Write the ink strokes to the output stream.
                    using (IOutputStream outputStream = stream.GetOutputStreamAt(0))
                    {
                        await inkCanvas.InkPresenter.StrokeContainer.SaveAsync(outputStream);
                        await outputStream.FlushAsync();
                    }
                    stream.Dispose();

                    // Finalize write so other apps can update file.
                    Windows.Storage.Provider.FileUpdateStatus status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                    if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                    {
                        return 1;
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
            // User selects Cancel and picker returns null.
            return 2;
        }

        /// <summary>
        /// Promts user to select an .gif file to load saved strokes.
        /// </summary>
        private async Task<bool> loadStrokefromGif()
        {
            // Let users choose their ink file using a file picker.
            // Initialize the picker.
            FileOpenPicker openPicker = new FileOpenPicker();
            openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".gif");

            // Show the file picker.
            StorageFile file = await openPicker.PickSingleFileAsync();

            // User selects a file and picker returns a reference to the selected file.
            if (file != null)
            {
                IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);

                // Read from file.
                using (var inputStream = stream.GetInputStreamAt(0))
                {
                    await inkCanvas.InkPresenter.StrokeContainer.LoadAsync(inputStream);
                    curPage.initialAnalyze();
                    stream.Dispose();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Saves the strokes into an image format of {gif,jog,tif,png} to disk. Return 1 if success, 0 if failed and 2 if canceled.
        /// </summary>
        private async Task<int> saveImageToDisk()
        {
            // Get all strokes on the InkCanvas.
            IReadOnlyList<InkStroke> currentStrokes = inkCanvas.InkPresenter.StrokeContainer.GetStrokes();

            // Strokes present on ink canvas.
            if (currentStrokes.Count > 0)
            {
                CanvasDevice device = CanvasDevice.GetSharedDevice();
                CanvasRenderTarget renderTarget = new CanvasRenderTarget(device, (int)curPage.PAGE_WIDTH, (int)curPage.PAGE_HEIGHT, 96);
                using (var ds = renderTarget.CreateDrawingSession())
                {
                    ds.Clear(Colors.White);
                    ds.DrawInk(currentStrokes);
                }

                // Let users choose their ink file using a file picker.
                // Initialize the picker.
                FileSavePicker savePicker = new FileSavePicker();
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Images", new List<string>() { ".gif", ".jpg", ".tif", ".png" });
                savePicker.DefaultFileExtension = ".jpg";
                savePicker.SuggestedFileName = "InkImage";

                // Show the file picker.
                StorageFile file = await savePicker.PickSaveFileAsync();

                // When chosen, picker returns a reference to the selected file.
                if (file != null)
                {
                    // Prevent updates to the file until updates are finalized with call to CompleteUpdatesAsync.
                    CachedFileManager.DeferUpdates(file);
                    // Open a file stream for writing.
                    IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                    // Write the ink strokes to the output stream.
                    using (IOutputStream outputStream = stream.GetOutputStreamAt(0))
                    {
                        await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Jpeg, 1f);
                    }
                    stream.Dispose();

                    // Finalize write so other apps can update file.
                    Windows.Storage.Provider.FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);

                    if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                    {
                        return 1;
                    }
                    else
                    {
                        return 0;
                    }
                }
                // User selects Cancel and picker returns null.
                return 2;
            }
            return 2;
        }

        /// <summary>
        /// Promts user to select an image file type {gif,png,jpg,tif} and tries to load into note.
        /// </summary>
        private async Task<bool> loadImagefromDisk()
        {
            // Let users choose their ink file using a file picker.
            // Initialize the picker.
            FileOpenPicker openPicker = new FileOpenPicker();
            openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".gif");
            openPicker.FileTypeFilter.Add(".png");
            openPicker.FileTypeFilter.Add(".jpg");
            openPicker.FileTypeFilter.Add(".tif");

            // Show the file picker.
            StorageFile file = await openPicker.PickSingleFileAsync();

            // User selects a file and picker returns a reference to the selected file.
            if (file != null)
            {
                IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);

                // Read from file.
                using (var inputStream = stream.GetInputStreamAt(0))
                {
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                    SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                    SoftwareBitmap softwareBitmapBGR8 = SoftwareBitmap.Convert(softwareBitmap,
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);
                    SoftwareBitmapSource bitmapSource = new SoftwareBitmapSource();
                    await bitmapSource.SetBitmapAsync(softwareBitmapBGR8);
                    curPage.AddImageControl("FIXME", bitmapSource);
                }
                stream.Dispose();
                return true;
            }
            else // User selects Cancel and picker returns null.
            {
                return false;
            }
        }

        /// <summary>
        /// Updates audio related metadata in the saved notebook metadata.
        /// </summary>
        /// <remarks>
        /// Note that this function updates the notebook meta file, not AudioMeta, which stores 
        /// the name of all audios created by a notebook. 
        /// Currently the only audio related data in "meta" is audioCount.
        /// </remarks>
        public async void updateAudioMeta()
        {
            // Set audio count as the current conversation index
            notebookObject.audioCount = SpeechManager.getSharedSpeechManager().getAudioCount();
            await FileManager.getSharedFileManager().SaveToMetaFile(notebookObject);
        }

        /// <summary>
        /// Gets saved transcripts from disk and updates the conversations history panel on the Speech Page.
        /// </summary>
        public async void updatePastConversation()
        {  
            // load content of saved transcripts into MainPage.conversations
            conversations = await FileManager.getSharedFileManager().GetSavedTranscriptsFromXML(notebookId);
            // display the loaded transcripts to Speech Page and update audio drop-down list
            SpeechPage.Current.updateChat();
        }
    }
}
