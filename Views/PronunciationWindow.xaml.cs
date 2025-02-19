using System;
using System.Collections.Generic;
using System.Windows;
using KokoroReader.Models;
using NLog;
using System.Windows.Controls;

namespace KokoroReader.Views
{
    public partial class PronunciationWindow : Window
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        private readonly Settings settings;
        private readonly Action onSettingsChanged;

        public PronunciationWindow(Settings settings, Action onSettingsChanged)
        {
            InitializeComponent();
            this.settings = settings;
            this.onSettingsChanged = onSettingsChanged;

            // Load pronunciations into the DataGrid
            LoadPronunciations();
        }

        private void LoadPronunciations()
        {
            var pronunciations = new List<PronunciationEntry>();
            foreach (var kvp in settings.PronunciationDictionary)
            {
                pronunciations.Add(new PronunciationEntry { Word = kvp.Key, Pronunciation = kvp.Value });
            }
            PronunciationsGrid.ItemsSource = pronunciations;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Update settings with new pronunciations
            var pronunciations = new Dictionary<string, string>();
            foreach (var entry in (List<PronunciationEntry>)PronunciationsGrid.ItemsSource)
            {
                if (!string.IsNullOrWhiteSpace(entry.Word) && !string.IsNullOrWhiteSpace(entry.Pronunciation))
                {
                    pronunciations[entry.Word] = entry.Pronunciation;
                }
            }

            settings.PronunciationDictionary = pronunciations;
            settings.SavePronunciations(); // Save to pronunciation.dict file
            onSettingsChanged?.Invoke();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var pronunciations = (List<PronunciationEntry>)PronunciationsGrid.ItemsSource;
            pronunciations.Add(new PronunciationEntry { Word = "", Pronunciation = "" });
            PronunciationsGrid.Items.Refresh();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (PronunciationsGrid.SelectedItem is PronunciationEntry selectedEntry)
            {
                var pronunciations = (List<PronunciationEntry>)PronunciationsGrid.ItemsSource;
                pronunciations.Remove(selectedEntry);
                PronunciationsGrid.Items.Refresh();
            }
        }
    }

    public class PronunciationEntry
    {
        public string Word { get; set; } = "";
        public string Pronunciation { get; set; } = "";
    }
} 