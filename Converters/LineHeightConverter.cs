using System;
using System.Globalization;
using System.Windows.Data;

namespace KokoroReader.Converters
{
    public class LineHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double fontSize)
            {
                // Return 1.8 times the font size for comfortable reading
                return fontSize * 1.8;
            }
            return 20.0; // Default line height
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 