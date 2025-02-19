using System;
using System.IO;
using System.Text.Json;

namespace KokoroReader.Models
{
    public class Bookmark
    {
        public string BookPath { get; set; } = string.Empty;
        public int ChapterIndex { get; set; }
        public int PageIndex { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public Bookmark()
        {
            CreatedAt = DateTime.Now;
            Name = $"Bookmark {CreatedAt:yyyy-MM-dd HH:mm:ss}";
            Description = string.Empty;
        }

        public Bookmark(string bookPath, int chapterIndex, int pageIndex) : this()
        {
            BookPath = bookPath;
            ChapterIndex = chapterIndex;
            PageIndex = pageIndex;
        }

        public string GetBookmarkFileName()
        {
            // Create a safe filename from the bookmark name
            var safeName = string.Join("_", Name.Split(Path.GetInvalidFileNameChars()));
            return $"{safeName}_{CreatedAt:yyyyMMddHHmmss}.json";
        }

        public string GetBookFolderName()
        {
            // Create a safe folder name from the book path
            var bookName = Path.GetFileNameWithoutExtension(BookPath);
            return string.Join("_", bookName.Split(Path.GetInvalidFileNameChars()));
        }

        public void Save(string baseBookmarksPath)
        {
            try
            {
                var bookFolder = Path.Combine(baseBookmarksPath, GetBookFolderName());
                Directory.CreateDirectory(bookFolder);

                var bookmarkPath = Path.Combine(bookFolder, GetBookmarkFileName());
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(bookmarkPath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save bookmark: {ex.Message}", ex);
            }
        }

        public static Bookmark Load(string bookmarkPath)
        {
            try
            {
                var json = File.ReadAllText(bookmarkPath);
                return JsonSerializer.Deserialize<Bookmark>(json) ?? throw new Exception("Failed to deserialize bookmark");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load bookmark: {ex.Message}", ex);
            }
        }

        public void Delete(string baseBookmarksPath)
        {
            try
            {
                var bookFolder = Path.Combine(baseBookmarksPath, GetBookFolderName());
                var bookmarkPath = Path.Combine(bookFolder, GetBookmarkFileName());
                
                if (File.Exists(bookmarkPath))
                {
                    File.Delete(bookmarkPath);
                }

                // Remove book folder if empty
                if (Directory.Exists(bookFolder) && !Directory.EnumerateFileSystemEntries(bookFolder).Any())
                {
                    Directory.Delete(bookFolder);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete bookmark: {ex.Message}", ex);
            }
        }

        // Override equals to compare bookmarks
        public override bool Equals(object? obj)
        {
            if (obj is not Bookmark other) return false;
            return BookPath == other.BookPath && 
                   ChapterIndex == other.ChapterIndex && 
                   PageIndex == other.PageIndex;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(BookPath, ChapterIndex, PageIndex);
        }
    }
} 