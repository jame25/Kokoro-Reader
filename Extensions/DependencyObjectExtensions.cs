using System.Windows;
using System.Windows.Media;

namespace KokoroReader.Extensions
{
    public static class DependencyObjectExtensions
    {
        public static T? FindDescendant<T>(this DependencyObject parent) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T found)
                    return found;
                    
                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                    return descendant;
            }
            
            return null;
        }
    }
} 
