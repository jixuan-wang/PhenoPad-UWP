﻿using PhenoPad.CustomControl;
using PhenoPad.FileService;
using PhenoPad.PhenotypeService;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace PhenoPad.LogService
{
    //So far only tracking the abbreviation,
    //Can add more types for different operations that
    //we want to track
    public enum OperationType
    {
        Abbreviation,
        Alternative,
        Phenotype,
        Stroke,
        Speech,
        ASR,
        HWR,
        ADDIN
    }
    /// <summary>
    /// A class for logging note taking operations used for statistical purposes
    /// </summary>
    class OperationLogger
    {
        //https://github.com/serilog/serilog/wiki/Structured-Data
      
        public static OperationLogger logger;
        public static List<string> CacheLogs;
        public static string CurrentNotebook;
        private DispatcherTimer FlushTimer;
        // this is for removing duplicate HWR logs
        //TODO: this name can be misleading, consider changing it
        private string lastHWRLog;

        public OperationLogger()
        {
        }

        /// <summary>
        /// Returns an operation logger object.
        /// </summary>
        /// <returns>an instance of the operation logger class</returns>
        /// <remarks>
        /// Creates a new instance if there isn't one.
        /// </remarks>
        public static OperationLogger getOpLogger()
        {
            if (logger == null) {
                logger = new OperationLogger();
                logger.InitializeLogFile();
            }
            return logger;
        }

        /// <summary>
        /// Creates an universal log file in local folder with flag to overwirte existing file or not, by default overwrite flag is set to false
        /// In future can pass in type parameter to separate files
        /// for different types of operations
        /// </summary>
        public void InitializeLogFile(bool overwrite = false) {
            CacheLogs = new List<string>();
            FlushTimer = new DispatcherTimer();
            FlushTimer.Tick += FlushLogToDisk;
            //triggers flush log every second
            FlushTimer.Interval = TimeSpan.FromSeconds(1);
            CurrentNotebook = null;
            lastHWRLog = "";
            string time = GetTimeStamp();
        }


        public void SetCurrentNoteID(string notebookId)
        {
            CurrentNotebook = notebookId;
        }

        public List<string> GetPhenotypeNames(List<Phenotype> phenos)
        {
            List<string> names = new List<string>();
            foreach (Phenotype p in phenos)
            {
                names.Add(p.name);
            }
            return names;

        }

        /// <summary>
        /// Logs an operation.
        /// </summary>
        /// <remarks>
        /// args can contain between 0-6 arguments depending on the type of the operation
        /// </remarks>
        public async void Log(OperationType opType, params string[] args)
        {
            // Logs an operation based on its type with varying number of arguments
            string log = "";
            switch (opType)
            {
                case OperationType.Stroke:
                    // args format= (args0: strokeID, args1: strokeaStarttime, args2: strokeDuration, args3: lineIndex, args4: pageID)
                    Debug.Assert(args.Count() == 5);
                    log = $"{GetTimeStamp()}|Stroke| {args[0]} | {args[1]} | {args[2]} | {args[3]} | {args[4]}";
                    break;
                case OperationType.HWR:
                    // args format= ( args0:recognized text, args1: {keyword:Phenotype}) 
                    Debug.Assert(args.Count() == 2);
                    log = $"{GetTimeStamp()}|HWR| {args[0]} | {args[1]}";
                    break;
                case OperationType.ADDIN:
                    // args format= ( args0:name, args1: {line #}, args2: {page #}) 
                    Debug.Assert(args.Count() == 3);
                    log = $"{GetTimeStamp()}|ADDIN| {args[0]} | {args[1]} | {args[2]}";
                    break;
                case OperationType.Speech:
                    // args format = ( args0:transcript, args1: {keyword:Phenotype}) 
                    Debug.Assert(args.Count() == 2);
                    log = $"{GetTimeStamp()}|Speech| {args[0]} | {args[1]}";
                    break;
                case OperationType.ASR:
                    Debug.Assert(args.Count() == 1);
                    log = $"{GetTimeStamp()}|ASR| {args[0]} ";
                    break;
                case OperationType.Phenotype:
                    // args format= (args0:source, args1:{added/deleted/Y/N}, args2:{keyword:Phenotype} )
                    Debug.Assert(args.Count() == 3);
                    log = $"{GetTimeStamp()}|Phenotype| {args[0]} | {args[1]} | {args[2]}";
                    break;
                case OperationType.Abbreviation:
                    // args format= (string: context sentence, string:shortTerm, string:DefaultExtendedForm, string:selectedExtendedForm, string:selectedRank, string: list of candidates )
                    Debug.Assert(args.Count() == 6);
                    log = $"{GetTimeStamp()}|Abbreviation| {args[0]} | {args[1]}, {args[2]} | {args[3]} | {args[4]} | {args[5]}";
                    break;
                case OperationType.Alternative:
                    // args format= (args0:original text, args1:selected/user input text, args2:candidate rank(-1 if user inputs) )
                    Debug.Assert(args.Count() == 3);
                    log = $"{GetTimeStamp()}|Alternative| {args[0]} | {args[1]} | {args[2]}";
                    break;
            }
            // only adds the log if it's got different content from the previous log 
            if (!CheckIfSameLog(log))
            {
                CacheLogs.Add(log);
                lastHWRLog = log;
            }

            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    if (!FlushTimer.IsEnabled)
                        FlushTimer.Start();
                });     
        }

        /// <summary>
        /// Checks if a new log is the same as the last log.
        /// </summary>
        /// <param name="log">the current log</param>
        /// <returns>true if it is the new log is the same as the last log false otherwise.</returns>
        private bool CheckIfSameLog(string log)
        {
            if (lastHWRLog == string.Empty)
                return false;
            
            var lastLog = lastHWRLog.Split('|');
            var curLog = log.Split('|');
            for (int i = 1; i < lastLog.Count(); i++)
            {
                if (lastLog[i] != curLog[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns the string representation of current local time.
        /// </summary>
        /// <returns>The string representation of current local time (precise to seconds).</returns>
        private string GetTimeStamp()
        {
            DateTime now = DateTime.Now;
            return now.ToString();
        }

        /// <summary>
        /// Clears all logs saved in cache.
        /// </summary>
        public void ClearAllLogs()
        {
            CacheLogs.Clear();
        }

        /// <summary>
        /// Flushes the logs in current program to local disk
        /// </summary>
        public async void FlushLogToDisk(object sender = null, object e = null)
        {

            FlushTimer.Stop();
            if (CacheLogs.Count > 0)
            {
                bool done = await FileManager.getSharedFileManager().AppendLogToFile(CacheLogs, CurrentNotebook);
                if (done)
                    CacheLogs.Clear();
                else
                    MetroLogger.getSharedLogger().Error($"{this.GetType().ToString()},failed to flush cachelogs, will try to flush in the next flushing interval");
            }
        }      

        internal void Log(OperationType type, string recognized,Dictionary<string, Phenotype> annoResult)
        {
            string pairs = "";
            foreach (var item in annoResult)
            {
                pairs += $"{item.Key}:{item.Value.name},";
            }
            pairs = pairs.TrimEnd(',');
            Log(type, recognized, pairs);
        }

        public string ParseCandidateList(List<string> str)
        {
            string parsed = "[";
            foreach (string s in str)
            {
                parsed += s + ",";
            }
            parsed = parsed.TrimEnd(',') + "]";
            return parsed;
        }


        /// <summary>
        /// parses a log line to operationitem to be displayed in view mode,
        /// currently not all logs will be displayed
        /// </summary>
        public async Task<List<NoteLineViewControl>> ParseOperationItems(Notebook notebook, List<TextMessage>conversations)
        {
            List<NoteLineViewControl> opitems = new List<NoteLineViewControl>();

            // Parsing information from log file
            List<string> logs = await FileManager.getSharedFileManager().GetLogStrings(notebook.id);

            if (logs != null)
            {
                // Gets all stored pages and notebook object from the disk
                List<string> pageIds = await FileManager.getSharedFileManager().GetPageIdsByNotebook(notebook.id);
                List<Phenotype> savedPhenotypes = await FileManager.getSharedFileManager().GetSavedPhenotypeObjectsFromXML(notebook.id);
                List<InkStroke> allstrokes = new List<InkStroke>();
                List<RecognizedPhrases> recogPhrases = new List<RecognizedPhrases>();
                List<ImageAndAnnotation> imageAndAnno = new List<ImageAndAnnotation>();
                for (int i = 0; i < pageIds.Count; i++)
                {
                    InkCanvas tempCanvas = new InkCanvas();
                    await FileManager.getSharedFileManager().LoadNotePageStroke(notebook.id, i.ToString(), null, tempCanvas);
                    var strokes = tempCanvas.InkPresenter.StrokeContainer.GetStrokes();
                    foreach (var s in strokes)
                    {
                        s.Selected = true;
                        allstrokes.Add(s.Clone());
                    }
                    recogPhrases.AddRange(await FileManager.getSharedFileManager().GetRecognizedPhraseFromXML(notebook.id, pageIds[i]));
                    var ia = await FileManager.getSharedFileManager().GetImgageAndAnnotationObjectFromXML(notebook.id, pageIds[i]);
                    imageAndAnno.AddRange(ia);

                }

                // Selectively parse useful log for display
                foreach (string line in logs)
                {
                    List<string> segment = line.Split('|').ToList();
                    DateTime time;
                    DateTime.TryParse(segment[0].Trim(), out time);
                    int lineNum;

                    // Note: currently only interested in stroke and phenotypes
                    switch (segment[1])
                    {

                        case ("Stroke"):
                            int id;
                            Int32.TryParse(segment[2].Trim(), out id);
                            DateTimeOffset sStartTime;
                            DateTimeOffset.TryParse(segment[3].Trim(), out sStartTime);
                            TimeSpan duration;
                            TimeSpan.TryParse(segment[4].Trim(), out duration);
                            Int32.TryParse(segment[5].Trim(), out lineNum);
                            NoteLineViewControl sameLine = opitems.Where(x => x.type == "Stroke" && lineNum == x.keyLine).FirstOrDefault();
                            List<InkStroke> s = allstrokes.Where(x => NotePageControl.getLineNumByRect(x.BoundingRect)== lineNum).ToList();
                            if (sameLine == null)
                            {
                                var phrase = recogPhrases.Where(x => x.line_index == lineNum).ToList();

                                // Skip lines with erased phrases
                                if(phrase.Count == 0)
                                {
                                    continue;
                                }

                                
                                NoteLineViewControl newLine = new NoteLineViewControl(time, lineNum, "Stroke", phrase, s, notebook);
                                Int32.TryParse(segment[6].Trim(), out newLine.pageID);

                                newLine.UpdateUILayout();

                                newLine.strokeCanvas.InkPresenter.IsInputEnabled = false;
                                newLine.keyLine = lineNum;
                                
                                if (s != null)
                                {
                                    newLine.strokeCanvas.InkPresenter.StrokeContainer.AddStrokes(s);
                                    foreach (var ss in newLine.strokeCanvas.InkPresenter.StrokeContainer.GetStrokes())
                                        ss.Selected = true;
                                    newLine.strokes = s;
                                }
                                Debug.WriteLine("stroke, added new line");
                                opitems.Add(newLine);
                            }
                            else { 
                                sameLine.keyTime = sameLine.keyTime > time? sameLine.keyTime: time;
                                sameLine.UpdateUILayout();
                                Debug.WriteLine("stroke line already existing, updated timestamp");
                            }

                            break;

                        case ("ADDIN"):
                            string name = segment[2].Trim();
                            Debug.WriteLine(name);
                            Int32.TryParse(segment[3].Trim(), out lineNum);
                            NoteLineViewControl newline = new NoteLineViewControl(time, lineNum, "ADDIN");
                            Int32.TryParse(segment[4].Trim(), out newline.pageID);
                            var ia = imageAndAnno.Where(x => x.name == name).FirstOrDefault();
                            Debug.WriteLine(ia == null);
                            if (ia != null) {
                                newline.setAddin(ia);
                                newline.UpdateUILayout();
                                opitems.Add(newline);
                            }
                            break;
                    }
                }
                //TODO
                // 1. add word blocks for lines           
                foreach (var l in opitems.Where(x => x.type == "Stroke"))
                {
                    l.LoadPhenotypes(savedPhenotypes);
                    var rect = l.strokeCanvas.InkPresenter.StrokeContainer.BoundingRect;
                    l.strokeCanvas.InkPresenter.StrokeContainer.MoveSelected(new Point(-rect.X, -rect.Y));
                    l.strokeCanvas.Height = rect.Height;
                    l.strokeCanvas.Width = rect.Width;
                }
                // parsing transcripts
                foreach (var t in conversations)
                {
                    NoteLineViewControl line = opitems.Where(x=> x.keyTime >= t.DisplayTime.Add(TimeSpan.FromSeconds(1)*-1) && x.keyTime <= t.DisplayTime.Add(TimeSpan.FromSeconds(1))).FirstOrDefault();
                    if (line == null)
                    {
                        line = new NoteLineViewControl(t.DisplayTime, -1, "SPEECH");
                    }
                    line.SetChatList(conversations.Where(x => x.Body == t.Body).ToList());
                    int numLoaded = await line.LoadPhenotypes(savedPhenotypes);
                    line.UpdateUILayout();
                    if (numLoaded > 0)
                    {
                        Debug.WriteLine("count greater than 1");
                        opitems.Add(line);
                    }

                }
                Debug.WriteLine("done setting");
            }            
            return opitems;
        }
    }

    /// <summary>
    /// A class that contains useful information of a logged operation
    /// </summary>
    class OperationItem
    {
        public string notebookID;
        public string pageID;
        public string type;
        public DateTime timestamp;
        public DateTime timeEnd;//for strokes
        

        // Attributes for stroke
        // Note: two ways of arranging strokes:by lines recognized using HWR or timestamp (display whenever there's a gap of time)
        public List<uint> strokeID;
        public int lineID; // probably need this for line ordering

        // Attributes for HWR/Speech
        public string context;
        public Phenotype phenotype;//list of UI elements for display

        // Attributes for phenotypes
        public string source;

        public OperationItem()
        {
        }

        public OperationItem(string notebookID, string pageID, string type, DateTime time)
        {
            this.notebookID = notebookID;
            this.pageID = pageID;
            this.type = type;
            strokeID = new List<uint>();
            timestamp = time;
            context = null;
            source = null;
            phenotype = null;
        }
    }
}
