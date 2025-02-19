using System.Globalization;
using System.Windows.Data;

namespace KokoroReader.Converters
{
    public class FontSizeToZoomConverter : IValueConverter
    {
        private const double BaseSize = 16.0; // Base font size

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double fontSize)
            {
                return fontSize / BaseSize * 100.0; // Convert to percentage
            }
            return 100.0; // Default zoom
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double zoom)
            {
                return zoom / 100.0 * BaseSize;
            }
            return BaseSize;
        }
    }
} 