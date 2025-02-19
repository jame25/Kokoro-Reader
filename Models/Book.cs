using CommunityToolkit.Mvvm.ComponentModel;

namespace KokoroReader.Models
{
    public partial class Book : ObservableObject
    {
        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string author = string.Empty;

        [ObservableProperty]
        private string filePath = string.Empty;

        [ObservableProperty]
        private double readingProgress;

        [ObservableProperty]
        private DateTime lastRead = DateTime.Now;

        [ObservableProperty]
        private List<Chapter> chapters = new();

        [ObservableProperty]
        private int currentChapterIndex;

        public Chapter? CurrentChapter => Chapters.Count > 0 ? Chapters[CurrentChapterIndex] : null;
    }
} 
