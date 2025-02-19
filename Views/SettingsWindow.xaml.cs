using System;
using System.Windows;
using KokoroReader.Models;
using KokoroReader.ViewModels;

namespace KokoroReader.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel viewModel;

        public SettingsWindow(Settings settings)
        {
            InitializeComponent();
            viewModel = new SettingsViewModel(settings, () => settings.Save());
            DataContext = viewModel;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 
