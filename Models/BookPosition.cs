using System;

namespace KokoroReader.Models
{
    public class BookPosition
    {
        public string BookPath { get; set; }
        public int ChapterIndex { get; set; }
        public int PageIndex { get; set; }
        public DateTime LastAccessed { get; set; }

        public BookPosition()
        {
            BookPath = string.Empty;
            ChapterIndex = 0;
            PageIndex = 0;
            LastAccessed = DateTime.Now;
        }

        public BookPosition(string bookPath, int chapterIndex, int pageIndex)
        {
            BookPath = bookPath;
            ChapterIndex = chapterIndex;
            PageIndex = pageIndex;
            LastAccessed = DateTime.Now;
        }
    }
} 