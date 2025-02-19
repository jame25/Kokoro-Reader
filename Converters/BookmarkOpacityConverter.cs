using System;
using System.Windows.Data;
using System.Globalization;

namespace KokoroReader.Converters
{
    public class BookmarkOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isBookmarked)
            {
                return isBookmarked ? 1.0 : 0.3;
            }
            return 0.3;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 
