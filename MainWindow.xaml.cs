using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using KokoroReader.ViewModels;
using KokoroReader.Models;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace KokoroReader
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel;
        private bool isClosing;
        private const int WM_CONTEXTMENU = 0x007B;
        private const uint TPM_LEFTBUTTON = 0x0000;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_RETURNCMD = 0x0100;
        private IntPtr hwnd;

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public MainWindow()
        {
            InitializeComponent();
            
            // Load settings first
            var settings = Settings.Load();
            viewModel = new MainViewModel(settings);
            DataContext = viewModel;

            // Apply window position from settings
            if (settings.HasSavedPosition)
            {
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
                Width = settings.WindowWidth;
                Height = settings.WindowHeight;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // Hook up window message handler
            SourceInitialized += MainWindow_SourceInitialized;
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(new HwndSourceHook(WndProc));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CONTEXTMENU)
            {
                POINT cursorPos;
                if (GetCursorPos(out cursorPos))
                {
                    // Show system menu at cursor position
                    IntPtr hMenu = GetSystemMenu(hwnd, false);
                    if (hMenu != IntPtr.Zero)
                    {
                        TrackPopupMenu(hMenu, TPM_LEFTBUTTON | TPM_RIGHTBUTTON, cursorPos.X, cursorPos.Y, 0, hwnd, IntPtr.Zero);
                        handled = true;
                    }
                }
            }
            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                // Get the element that was clicked
                var source = e.OriginalSource as DependencyObject;

                // For visual elements, only prevent dragging for interactive controls
                while (source != null)
                {
                    // Check for non-visual elements that should still prevent dragging
                    if (source is FlowDocument || 
                        source is TextElement || 
                        source is Button || 
                        source is ScrollBar || 
                        source is Slider || 
                        source is TextBox ||
                        source is ComboBox ||
                        source is ListBox ||
                        source is TreeView ||
                        source is MenuItem)
                    {
                        return;
                    }

                    // Try to get the parent, handling non-visual elements
                    try
                    {
                        source = VisualTreeHelper.GetParent(source);
                    }
                    catch (InvalidOperationException)
                    {
                        // If the element is not a Visual, try to get its logical parent
                        source = LogicalTreeHelper.GetParent(source);
                    }
                }

                // If we get here and the mouse button is still pressed, it's safe to drag
                if (Mouse.LeftButton == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Up:
                    System.Diagnostics.Debug.WriteLine("[MainWindow] Left/Up arrow key pressed");
                    _ = viewModel.PreviousPageCommand.ExecuteAsync(null);
                    break;
                case Key.Right:
                case Key.Down:
                    System.Diagnostics.Debug.WriteLine("[MainWindow] Right/Down arrow key pressed");
                    _ = viewModel.NextPageCommand.ExecuteAsync(null);
                    break;
                case Key.Add:
                case Key.OemPlus:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        viewModel.IncreaseFontSizeCommand.Execute(null);
                    }
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        viewModel.DecreaseFontSizeCommand.Execute(null);
                    }
                    break;
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!isClosing && WindowState != WindowState.Minimized)
            {
                viewModel.HandleWindowSizeChanged(e.NewSize.Width, e.NewSize.Height);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            isClosing = true;
            
            // Save window position and size
            if (WindowState == WindowState.Maximized)
            {
                // Use RestoreBounds for maximized window to save the restored position
                viewModel.Settings.UpdateWindowMetrics(
                    RestoreBounds.Left,
                    RestoreBounds.Top,
                    RestoreBounds.Width,
                    RestoreBounds.Height);
            }
            else
            {
                viewModel.Settings.UpdateWindowMetrics(
                    Left,
                    Top,
                    Width,
                    Height);
            }

            viewModel.Dispose();
        }

        private void ContentViewer_ContentOverflow(object sender, double remainingHeight)
        {
            // When content overflows, notify the ViewModel to handle pagination
            if (remainingHeight > 0)
            {
                viewModel.HandleContentOverflow(remainingHeight);
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Prevent mouse wheel scrolling
            e.Handled = true;
        }
    }
} 