using System;
using System.Globalization;
using System.Windows.Data;

namespace KokoroReader.Converters
{
    public class ContentWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double windowWidth)
            {
                // Calculate optimal reading width (about 66 characters per line for optimal readability)
                // Assuming average character width is about 0.5em at base font size
                double maxWidth = Math.Min(windowWidth * 0.8, 900); // Cap at 900px for very wide screens
                return maxWidth;
            }
            return 800; // Default width
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 