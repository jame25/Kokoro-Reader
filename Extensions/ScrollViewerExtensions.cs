using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Interop;

namespace KokoroReader.Extensions
{
    public static class ScrollViewerExtensions
    {
        public static void AnimateScroll(this ScrollViewer scrollViewer, Point targetPoint, TimeSpan duration)
        {
            var startOffset = scrollViewer.VerticalOffset;
            var targetOffset = targetPoint.Y;
            var animationStartTime = DateTime.Now;

            // Get the actual refresh rate of the display
            var refreshRate = 60.0; // Default to 60Hz
            if (Window.GetWindow(scrollViewer) is Window window)
            {
                var dpiScale = VisualTreeHelper.GetDpi(window);
                var dpi = Math.Max(dpiScale.DpiScaleX, dpiScale.DpiScaleY) * 96.0;
                
                // Estimate refresh rate based on DPI
                // Most high DPI displays (4K, etc.) support at least 120Hz
                if (dpi >= 192.0) // >= 200% scaling
                {
                    refreshRate = 120.0;
                }
                else if (dpi >= 144.0) // >= 150% scaling
                {
                    refreshRate = 90.0;
                }
            }

            // Calculate the interval based on refresh rate
            var interval = TimeSpan.FromMilliseconds(1000.0 / refreshRate);
            
            var timer = new DispatcherTimer
            {
                Interval = interval // This will be ~8.33ms for 120Hz, ~16.67ms for 60Hz
            };

            timer.Tick += (s, e) =>
            {
                var progress = (DateTime.Now - animationStartTime).TotalMilliseconds / duration.TotalMilliseconds;
                if (progress >= 1)
                {
                    scrollViewer.ScrollToVerticalOffset(targetOffset);
                    timer.Stop();
                    return;
                }

                // Use cubic easing for smooth animation
                progress = EaseInOutCubic(progress);
                var newOffset = startOffset + (targetOffset - startOffset) * progress;
                scrollViewer.ScrollToVerticalOffset(newOffset);
            };

            timer.Start();
        }

        private static double EaseInOutCubic(double t)
        {
            return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }
    }
} 