﻿using PhenoPad.FileService;
using PhenoPad.PhenotypeService;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace PhenoPad.CustomControl
{
    public sealed partial class NoteLineViewControl : UserControl
    {
        public DateTime keyTime;
        public int keyLine;
        public string type;
        public int pageID;
        public double canvasLeft;

        public List<InkStroke> strokes;
        public AddInControl addin;
        public string text;
        public List<WordBlockControl> HWRs;

        public List<Phenotype> phenotypes;
        public List<TextMessage> chats;

        // test field
        public Notebook notebook;


        public NoteLineViewControl() { }
            
        // remove extra parameter for stroke
        public NoteLineViewControl(DateTime time, int line, string type, List<RecognizedPhrases> phrase=null, List<InkStroke> strokes = null, Notebook notebook = null)
        {
            this.InitializeComponent();
            keyTime = time;
            keyLine = line;
            this.type = type;
            phenotypes = new List<Phenotype>();
            //Debug.WriteLine("Phraseeeeee FIX THIS", phrase.ToString());

            // take out
            this.strokes = strokes;
            this.notebook = notebook;
            // COMMENT OUT
            // also need to check count to make sure to not add phrases that have been erased
            HWRs = new List<WordBlockControl>();
            if (phrase != null && phrase.Count != 0)
                AddRecogPhrases(phrase);

   
        }

        public void UpdateUILayout() {
            KeyTime.Text = keyTime.ToString();
            KeyLine.Text = $"Page {pageID + 1}  \nLine {keyLine}";
            if (addin != null)
            {
                AddInIcon.Child = addin;
                addin.HorizontalAlignment = HorizontalAlignment.Center;
                addin.VerticalAlignment = VerticalAlignment.Center;
                strokeGrid.Visibility = Visibility.Collapsed;
                addinGrid.Visibility = Visibility.Visible;
            }
            UpdateLayout();
        }

        public void SetChatList(List<TextMessage> texts) {

            Debug.WriteLine(texts.Count + "-----");
            this.chats = texts;
            this.text = this.chats.FirstOrDefault().Body;
            chatItem.ItemsSource = chats;
            chatItem.UpdateLayout();
            UpdateLayout();
        }

        public void AddRecogPhrases(List<RecognizedPhrases> recogPhrase) {

            List<WordBlockControl> words = new List<WordBlockControl>();
            int line_index = recogPhrase.FirstOrDefault().line_index;
            foreach (var ph in recogPhrase) {
                List<string> candidates = ph.candidate_list;

                // remove dupe candidates
                candidates = candidates.Distinct().ToList();

                //2019/06/18-> remember to change null back to strokes that match condition!
                //List<InkStroke> strokes = new List<InkStroke>();
                //foreach (var s in inkCan.InkPresenter.StrokeContainer.GetStrokes())
                //{
                //if (ph.strokes.Contains(s.StrokeStartedTime.Value.DateTime))
                //strokes.Add(s);
                //}

                // matching stroke test
                List<InkStroke> matchingStrokes = new List<InkStroke>();

                foreach(var s in strokes)
                {
                    if (ph.strokes.Contains(s.StrokeStartedTime.Value.DateTime))
                    {
                        matchingStrokes.Add(s);
                    }
                }

                WordBlockControl wb = new WordBlockControl(ph.line_index, ph.word_index, ph.current, candidates, matchingStrokes, notebook, notebook.notePages.ElementAt(Int32.Parse(ph.pageId)), ph.pageId, recogPhrase);
                // might need to replace strokes with something else later?
                //WordBlockControl wb = new WordBlockControl(ph.line_index, ph.word_index, ph.current, candidates, this.strokes);
                wb.WordBlock.FontSize = 16;
                wb.corrected = ph.is_corrected;
                //wb.WordBlock.RenderTransform = new TranslateTransform { X = 10, Y = 20 };
                words.Add(wb);

                // add to handwriting recognition
                HWRs.Add(wb);
            }
            //this handles the case when there's only one line of note on the page
            if (words.Count > 0)
            {
                NotePhraseControl npc = new NotePhraseControl(line_index, words);
                //npc.PhraseCanvas.InkPresenter.StrokeContainer.MoveSelected(new Point(-rect.X, -rect.Y));

                text = npc.GetString().Trim();
                //text = "Test";
                //recogPhraseStack.RenderTransform = new TranslateTransform { X = -500 };

                recogPhraseStack.Children.Add(npc);
                //recogPhraseStack.RenderTransform = new TranslateTransform { X = -500 };
                //UpdateLayout();
                npc.UpdateLayout();
            }

        }

        public void setAddin(ImageAndAnnotation ia) {
            AddInControl canvasAddIn = new AddInControl(ia.name, ia.notebookId, ia.pageId, ia.widthOrigin, ia.heightOrigin);
            canvasAddIn.addinType = ia.addinType;
            canvasAddIn.Height = ia.height;
            canvasAddIn.Width = ia.width;
            canvasAddIn.widthOrigin = ia.widthOrigin;
            canvasAddIn.heightOrigin = ia.heightOrigin;
            canvasAddIn.inkCan.Height = ia.heightOrigin;
            canvasAddIn.inkCan.Width = ia.widthOrigin;
            canvasAddIn.canvasLeft = ia.canvasLeft;
            canvasAddIn.canvasTop = ia.canvasTop;
            canvasAddIn.dragTransform.X = ia.transX;
            canvasAddIn.dragTransform.Y = ia.transY;
            canvasAddIn.commentID = ia.commentID;
            canvasAddIn.commentslideX = ia.slideX;
            canvasAddIn.commentslideY = ia.slideY;
            canvasAddIn.inkRatio = ia.inkRatio;
            canvasAddIn.inDock = false;
            canvasAddIn.viewFactor.ScaleX = ia.zoomFactorX;
            canvasAddIn.viewFactor.ScaleY = ia.zoomFactorY;
            canvasAddIn.InitializeFromDisk(onlyView: true);
            this.addin = canvasAddIn;
            UpdateLayout();
        }

        public void ChatBubble_Click(object sender, RoutedEventArgs args) {
            Debug.WriteLine($"clicked = {this.text}");
            TextMessage message = chats.FirstOrDefault();
            NoteViewPage.Current.ShowAllChatAt(sender,message);
            //NoteViewPage.Current.PlayMedia(message.start, message.end);
        }

        public async Task<int> LoadPhenotypes(List<Phenotype> savedPhenotypes) {
            Debug.WriteLine($"loading...text={text}");
            Dictionary<string, Phenotype> annoResult = await PhenotypeManager.getSharedPhenotypeManager().annotateByNCRAsync(text);
            if (annoResult == null)
                return 0;

            var annoPhenos = annoResult.Values.ToList();
            foreach (var p in annoPhenos) {
                var saved = savedPhenotypes.Where(x => x.name == p.name).FirstOrDefault();
                if (saved != null)
                    phenotypes.Add(saved);
                else
                    phenotypes.Add(p);                       
            }

            if (phenotypes.Count > 0) {
                PhenoListView.ItemsSource = phenotypes;
                PhenoListView.UpdateLayout();
            }
            return phenotypes.Count;


        }


    }
}
