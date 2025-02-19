using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KokoroReader.Models
{
    public partial class Chapter : ObservableObject
    {
        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string content = string.Empty;

        [ObservableProperty]
        private string filePath = string.Empty;

        [ObservableProperty]
        private int order;

        [ObservableProperty]
        private List<KokoroReader.Models.Page> pages = new();

        [ObservableProperty]
        private int currentPageIndex;

        public KokoroReader.Models.Page? CurrentPage => Pages.Count > 0 ? Pages[CurrentPageIndex] : null;

        public async Task<string> GetContentAsync()
        {
            // For now, just return the content property
            // This can be enhanced later to load content from file if needed
            return Content;
        }
    }
} 