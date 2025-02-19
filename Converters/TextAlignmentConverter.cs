using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KokoroReader.Converters
{
    public class TextAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Models.TextAlignment modelAlignment)
            {
                return modelAlignment switch
                {
                    Models.TextAlignment.Left => TextAlignment.Left,
                    Models.TextAlignment.Center => TextAlignment.Center,
                    Models.TextAlignment.Right => TextAlignment.Right,
                    Models.TextAlignment.Justify => TextAlignment.Justify,
                    _ => TextAlignment.Left
                };
            }
            return TextAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TextAlignment wpfAlignment)
            {
                return wpfAlignment switch
                {
                    TextAlignment.Left => Models.TextAlignment.Left,
                    TextAlignment.Center => Models.TextAlignment.Center,
                    TextAlignment.Right => Models.TextAlignment.Right,
                    TextAlignment.Justify => Models.TextAlignment.Justify,
                    _ => Models.TextAlignment.Left
                };
            }
            return Models.TextAlignment.Left;
        }
    }
} 