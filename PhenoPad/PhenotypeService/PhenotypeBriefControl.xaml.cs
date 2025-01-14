﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using PhenoPad.PhenotypeService;
using Windows.UI;
using PhenoPad.Styles;
using System.Diagnostics;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace PhenoPad.CustomControl
{
    public sealed partial class PhenotypeBriefControl : UserControl
    {
        public String phenotypeName
        {
            get { return (String)GetValue(phenotypeNameProperty); }
            set
            {
                phenotypeNameTextBlock.Text = value;
                SetValue(phenotypeNameProperty, value);
            }
        }
        public String phenotypeId
        {
            get { return (String)GetValue(phenotypeIdProperty); }
            set
            {
                SetValue(phenotypeIdProperty, value);
            }
        }
        public int phenotypeState
        {
            get { return (int)GetValue(phenotypeStateProperty); }
            set
            {
                setPhenotypeState(value);
                SetValue(phenotypeStateProperty, value);
            }
        }
        public Visibility DeleteBtnShowing = Visibility.Visible;
        public SourceType sourceType
        {
            get { return (SourceType)GetValue(sourceTypeProperty); }
            set
            {
                SetValue(sourceTypeProperty, value);
            }
        }

        public PresentPosition presentPosition
        {
            get { return (PresentPosition)GetValue(presentPositionProperty); }
            set
            {
                SetValue(presentPositionProperty, value);
            }
        }



        public static readonly DependencyProperty phenotypeNameProperty = DependencyProperty.Register(
          "phenotypeName",
          typeof(String),
          typeof(TextBlock),
          new PropertyMetadata(null)
        );

        public static readonly DependencyProperty phenotypeIdProperty = DependencyProperty.Register(
          "phenotypeId",
          typeof(String),
          typeof(TextBlock),
          new PropertyMetadata(null)
        );
        public static readonly DependencyProperty phenotypeStateProperty = DependencyProperty.Register(
          "phenotypeState",
          typeof(int),
          typeof(TextBlock),
          new PropertyMetadata(null)
        );
        public static readonly DependencyProperty sourceTypeProperty = DependencyProperty.Register(
          "sourceType",
          typeof(SourceType),
          typeof(TextBlock),
          new PropertyMetadata(null)
        );
        public static readonly DependencyProperty presentPositionProperty = DependencyProperty.Register(
         "presentPosition",
         typeof(PresentPosition),
         typeof(TextBlock),
         new PropertyMetadata(null)
       );


        private int localState;

        public PhenotypeBriefControl()
        {
            this.InitializeComponent();
            //DeletePhenotypeSB.Begin();
        }

        public void initByPhenotype(Phenotype p)
        {
            phenotypeName = p.name;
            phenotypeId = p.hpId;
            phenotypeState = p.state;
            sourceType = p.sourceType;
            setPhenotypeState(phenotypeState);
        }


        // Add a phenotype
        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            setPhenotypeState(1);
            PhenotypeManager.getSharedPhenotypeManager().addPhenotype(new Phenotype(phenotypeId, phenotypeName, 1, MainPage.Current.curPageIndex), sourceType);
        }
        
        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            localState = -1;
            PhenotypeManager.getSharedPhenotypeManager().removeByIdAsync(phenotypeId, SourceType.Notes);
            this.Visibility = Visibility.Collapsed;

            if (presentPosition == PresentPosition.Inline)
            {
            }
        }
        

        private void YSwitchBtn_Click(object sender, RoutedEventArgs e)
        {
            setPhenotypeState(1);
            Debug.WriteLine(((Button)sender).Parent.GetType().ToString());
            PhenotypeManager.getSharedPhenotypeManager().updatePhenoStateById(phenotypeId, 1, sourceType);

        }
        private void NSwitchBtn_Click(object sender, RoutedEventArgs e)
        {
            setPhenotypeState(0);
            PhenotypeManager.getSharedPhenotypeManager().updatePhenoStateById(phenotypeId, 0, sourceType);
        }

        private void setPhenotypeState(int state)
        {
            //default state: user has not done anything
            if (state == -1)
            {
                NameGrid.Background = new SolidColorBrush(Colors.LightGray);
                phenotypeNameTextBlock.Foreground = new SolidColorBrush(Colors.Black);
                phenotypeNameTextBlock.Text = phenotypeName;
                //NameCrossLine.Visibility = Visibility.Collapsed;
            }
            //user selects the phenotype as Y
            else if (state == 1)
            {
                //NameGrid.Background = Application.Current.Resources["Button_Background"] as SolidColorBrush;
                //phenotypeNameTextBlock.Foreground = Application.Current.Resources["WORD_DARK"] as SolidColorBrush;
                //phenotypeNameTextBlock.Foreground = new SolidColorBrush(Colors.Black);
                phenotypeNameTextBlock.Foreground = new SolidColorBrush(Colors.White);
                phenotypeNameTextBlock.Text = phenotypeName;
                NameGrid.Background = new SolidColorBrush(Colors.CornflowerBlue);
                //NameCrossLine.Visibility = Visibility.Collapsed;
            }
            //user selects the phenotype as N
            else
            {
                //NameGrid.Background = new SolidColorBrush(Colors.LightCoral);
                phenotypeNameTextBlock.Foreground = new SolidColorBrush(Colors.White);
                NameGrid.Background = new SolidColorBrush(Colors.PaleVioletRed);
                phenotypeNameTextBlock.Text = "No " + phenotypeName;
                //NameCrossLine.Visibility = Visibility.Visible;
            }
            
        }

        private void phenotypeNameTextBlock_Tapped(object sender, RoutedEventArgs e)
        {

            switch (localState)
            {
                case -1:
                    localState = 1;
                        setPhenotypeState(1);
                    PhenotypeManager.getSharedPhenotypeManager().addPhenotype(new Phenotype(phenotypeId, phenotypeName, 1,MainPage.Current.curPageIndex), sourceType);
                    break;
                case 0:
                    localState = 1;
                    //if (presentPosition == PresentPosition.Inline)
                        setPhenotypeState(1);
                    PhenotypeManager.getSharedPhenotypeManager().updatePhenoStateById(phenotypeId, 1, sourceType);
                    break;
                case 1:
                    localState = 0;
                    //if (presentPosition == PresentPosition.Inline)
                        setPhenotypeState(0);
                    PhenotypeManager.getSharedPhenotypeManager().updatePhenoStateById(phenotypeId, 0, sourceType);
                    break;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            localState = phenotypeState;
            //changes display style for view mode
            if (MainPage.Current == null || MainPage.Current.curPage == null) {
                DeleteBtn.Visibility = Visibility.Collapsed;
                Grid.SetColumn(phenotypeNameTextBlock,0);
                Grid.SetColumnSpan(phenotypeNameBtn, 2);
                Grid.SetColumnSpan(phenotypeGrid, 2);
                phenotypeNameBtn.Click -= phenotypeNameTextBlock_Tapped;
            }
        }
    }
}
