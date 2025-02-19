using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using KokoroReader.Models;
using System.IO;
using KokoroSharp;

namespace KokoroReader.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly Settings settings;
        private readonly Action onSettingsChanged;

        public SettingsViewModel(Settings settings, Action onSettingsChanged)
        {
            this.settings = settings;
            this.onSettingsChanged = onSettingsChanged;

            // Initialize available fonts
            AvailableFonts = System.Windows.Media.Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(f => f)
                .ToList();

            // Initialize available themes
            AvailableThemes = Enum.GetValues(typeof(Theme))
                .Cast<Theme>()
                .ToList();

            // Initialize available voices
            try
            {
                var voicesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices");
                if (Directory.Exists(voicesPath))
                {
                    KokoroSharp.KokoroVoiceManager.LoadVoicesFromPath(voicesPath);
                    AvailableVoices = KokoroSharp.KokoroVoiceManager.Voices
                        .Select(v => v.Name)
                        .OrderBy(name => name)
                        .ToList();
                }
                else
                {
                    AvailableVoices = new List<string> { "af_heart" }; // Fallback to default voice
                    MessageBox.Show($"Voices directory not found at: {voicesPath}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                AvailableVoices = new List<string> { "af_heart" }; // Fallback to default voice
                MessageBox.Show($"Error loading voices: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Initialize commands
            SetTextAlignmentCommand = new RelayCommand<Models.TextAlignment>(ExecuteSetTextAlignment);
        }

        public List<string> AvailableFonts { get; }
        public List<Theme> AvailableThemes { get; }
        public List<string> AvailableVoices { get; }

        public string SelectedFont
        {
            get => settings.FontFamilyName;
            set
            {
                if (settings.FontFamilyName != value)
                {
                    settings.FontFamilyName = value;
                    OnPropertyChanged();
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public Theme SelectedTheme
        {
            get => settings.Theme;
            set
            {
                if (settings.Theme != value)
                {
                    settings.Theme = value;
                    OnPropertyChanged();
                    settings.Save(); // Save immediately when theme changes
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public string SelectedVoice
        {
            get => settings.VoiceName;
            set
            {
                if (settings.VoiceName != value)
                {
                    settings.VoiceName = value;
                    OnPropertyChanged();
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public double FontSize
        {
            get => settings.FontSize;
            set
            {
                if (settings.FontSize != value)
                {
                    settings.FontSize = value;
                    OnPropertyChanged();
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public string FontFamilyName
        {
            get => settings.FontFamilyName;
            set
            {
                if (settings.FontFamilyName != value)
                {
                    settings.FontFamilyName = value;
                    OnPropertyChanged();
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public Models.TextAlignment TextAlignment
        {
            get => settings.TextAlignment;
            set
            {
                if (settings.TextAlignment != value)
                {
                    settings.TextAlignment = value;
                    OnPropertyChanged();
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public double LineHeight
        {
            get => settings.LineHeight;
            set
            {
                if (settings.LineHeight != value)
                {
                    settings.LineHeight = value;
                    OnPropertyChanged();
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public double VoiceSpeed
        {
            get => settings.VoiceSpeed;
            set
            {
                if (settings.VoiceSpeed != value)
                {
                    settings.VoiceSpeed = value;
                    OnPropertyChanged();
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public Dictionary<string, string> PronunciationDictionary
        {
            get => settings.PronunciationDictionary;
            set
            {
                if (settings.PronunciationDictionary != value)
                {
                    settings.PronunciationDictionary = value;
                    OnPropertyChanged();
                    onSettingsChanged?.Invoke();
                }
            }
        }

        public IRelayCommand<Models.TextAlignment> SetTextAlignmentCommand { get; }

        private void ExecuteSetTextAlignment(Models.TextAlignment alignment)
        {
            TextAlignment = alignment;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 
