using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using System.Windows;
using System.Windows.Media;
using System.ComponentModel;

namespace KokoroReader.Models
{
    public enum Theme
    {
        Light,
        Dark,
        Sepia
    }

    public enum TextAlignment
    {
        Left,
        Center,
        Right,
        Justify
    }

    public class Settings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "settings.conf");

        private static readonly string BookmarksPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "bookmarks"
        );

        private static readonly string PronunciationDictPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "pronunciation.dict"
        );

        private ILogger? logger;

        public void SetLogger(ILogger logger)
        {
            this.logger = logger;
        }

        private void LogInfo(string message)
        {
            if (LoggingEnabled && logger != null)
            {
                logger.Info(message);
            }
        }

        private void LogError(Exception ex, string message)
        {
            if (LoggingEnabled && logger != null)
            {
                logger.Error(ex, message);
            }
        }

        // Event for theme changes
        public event Action<Theme>? ThemeChanged;

        // Window settings
        public double WindowWidth { get; set; } = 800; // Default width
        public double WindowHeight { get; set; } = 921; // Default height
        public double WindowLeft { get; set; } = 2237; // Default left position
        public double WindowTop { get; set; } = 346; // Default top position
        [JsonIgnore] // This property should not be serialized
        public bool HasSavedPosition => !double.IsNaN(WindowLeft) && !double.IsNaN(WindowTop);

        // Book settings
        private string? lastBookPath;
        public string? LastBookPath
        {
            get => lastBookPath;
            set
            {
                if (lastBookPath != value)
                {
                    lastBookPath = value;
                    OnPropertyChanged(nameof(LastBookPath));
                }
            }
        }

        public int LastChapterIndex { get; set; }
        private double fontSize = 16.0;
        public double FontSize
        {
            get => fontSize;
            set
            {
                if (fontSize != value)
                {
                    fontSize = value;
                    OnPropertyChanged(nameof(FontSize));
                }
            }
        }

        private string fontFamilyName = "Segoe UI";
        public string FontFamilyName
        {
            get => fontFamilyName;
            set
            {
                if (fontFamilyName != value)
                {
                    fontFamilyName = value;
                    OnPropertyChanged(nameof(FontFamilyName));
                }
            }
        }

        public double LineHeight { get; set; } = 1.6;
        private TextAlignment textAlignment = TextAlignment.Justify;
        public TextAlignment TextAlignment
        {
            get => textAlignment;
            set
            {
                if (textAlignment != value)
                {
                    textAlignment = value;
                    OnPropertyChanged(nameof(TextAlignment));
                }
            }
        }

        // Theme settings
        private Theme theme = Theme.Sepia;
        public Theme Theme
        {
            get => theme;
            set
            {
                if (theme != value)
                {
                    theme = value;
                    ThemeChanged?.Invoke(value);
                }
            }
        }

        // Voice settings
        public double VoiceSpeed { get; set; } = 1.0;
        public string VoiceName { get; set; } = "af_heart"; // Default voice model

        // Logging settings
        public bool LoggingEnabled { get; set; } = false; // Disabled by default

        [JsonIgnore] // This property should not be serialized
        public List<Bookmark> Bookmarks { get; private set; } = new();

        // Pronunciation dictionary
        [JsonIgnore] // This property should not be serialized to settings.conf
        [JsonPropertyName("pronunciations")]
        public Dictionary<string, string> PronunciationDictionary { get; set; } = new();
        [JsonIgnore] // This property should not be serialized to settings.conf
        public Dictionary<string, string> Pronunciations => PronunciationDictionary; // Backwards compatibility alias

        public void LoadBookmarks()
        {
            try
            {
                LogInfo("Starting to load bookmarks");
                Bookmarks.Clear();
                if (!Directory.Exists(BookmarksPath))
                {
                    Directory.CreateDirectory(BookmarksPath);
                    LogInfo("Created bookmarks directory as it did not exist");
                    return;
                }

                // Iterate through all book folders
                var bookFolders = Directory.GetDirectories(BookmarksPath);
                LogInfo($"Found {bookFolders.Length} book folders");

                foreach (var bookFolder in bookFolders)
                {
                    // Get all bookmark files in this book folder
                    var bookmarkFiles = Directory.GetFiles(bookFolder, "*.json");
                    LogInfo($"Found {bookmarkFiles.Length} bookmark files in folder: {Path.GetFileName(bookFolder)}");

                    foreach (var bookmarkFile in bookmarkFiles)
                    {
                        try
                        {
                            var bookmark = Bookmark.Load(bookmarkFile);
                            // Convert to filename only for consistency
                            bookmark.BookPath = Path.GetFileName(bookmark.BookPath);
                            
                            // Look for the book in both the application directory and absolute path
                            var bookFileName = bookmark.BookPath;
                            var localBookPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, bookFileName);
                            var lastBookDir = LastBookPath != null ? Path.GetDirectoryName(LastBookPath) : null;
                            var lastBookDirPath = lastBookDir != null ? Path.Combine(lastBookDir, bookFileName) : null;
                            var exists = File.Exists(localBookPath) || File.Exists(bookmark.BookPath) || 
                                       (lastBookDirPath != null && File.Exists(lastBookDirPath));

                            if (exists)
                            {
                                Bookmarks.Add(bookmark);
                                LogInfo($"Loaded bookmark for {bookmark.BookPath} at Chapter {bookmark.ChapterIndex}, Page {bookmark.PageIndex}");
                            }
                            else
                            {
                                LogInfo($"Skipping bookmark as book no longer exists: {bookmark.BookPath}");
                                // Keep the bookmark file in case the book is moved back
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError(ex, $"Error loading bookmark file: {bookmarkFile}");
                        }
                    }
                }
                LogInfo($"Successfully loaded {Bookmarks.Count} bookmarks in total");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error loading bookmarks");
            }
        }

        public string ApplyPronunciations(string text)
        {
            foreach (var kvp in PronunciationDictionary)
            {
                text = text.Replace(kvp.Key, kvp.Value);
            }
            return text;
        }

        public static Settings Load()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                Settings settings;
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
                else
                {
                    settings = new Settings();
                    // Set default book path to user guide for new installations
                    string userGuidePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_guide.epub");
                    if (File.Exists(userGuidePath))
                    {
                        settings.LastBookPath = userGuidePath;
                    }
                    settings.LoggingEnabled = false; // Ensure logging is disabled by default
                    settings.Save(); // Save default settings immediately
                }

                // Create bookmarks directory if it doesn't exist
                if (!Directory.Exists(BookmarksPath))
                {
                    Directory.CreateDirectory(BookmarksPath);
                }

                // Load bookmarks
                settings.LoadBookmarks();

                // Load pronunciations
                settings.LoadPronunciations();

                return settings;
            }
            catch (Exception)
            {
                // Return default settings without logging the error since logging might not be initialized
                return new Settings();
            }
        }

        public void LoadPronunciations()
        {
            try
            {
                if (File.Exists(PronunciationDictPath))
                {
                    var lines = File.ReadAllLines(PronunciationDictPath);
                    PronunciationDictionary.Clear();
                    foreach (var line in lines)
                    {
                        // Skip empty lines and comments
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                            continue;

                        var parts = line.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var word = parts[0].Trim();
                            var pronunciation = parts[1].Trim();
                            if (!string.IsNullOrEmpty(word) && !string.IsNullOrEmpty(pronunciation))
                            {
                                PronunciationDictionary[word] = pronunciation;
                            }
                        }
                    }
                    LogInfo($"Loaded {PronunciationDictionary.Count} pronunciations from dictionary file");
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "Error loading pronunciation dictionary");
            }
        }

        public void SavePronunciations()
        {
            try
            {
                var lines = new List<string>
                {
                    "# Pronunciation Dictionary",
                    "# Format: word=pronunciation",
                    "# Example: LHC=Large Hadron Collider",
                    ""
                };

                lines.AddRange(PronunciationDictionary
                    .OrderBy(p => p.Key)
                    .Select(p => $"{p.Key}={p.Value}"));

                File.WriteAllLines(PronunciationDictPath, lines);
                LogInfo($"Saved {PronunciationDictionary.Count} pronunciations to dictionary file");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error saving pronunciation dictionary");
            }
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };
                
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
                LogInfo("Settings saved successfully");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error saving settings");
                // Consider showing a message to the user here
            }
        }

        public void SaveOnExit()
        {
            try
            {
                // Ensure window dimensions are valid before saving
                if (WindowWidth <= 0) WindowWidth = 800;
                if (WindowHeight <= 0) WindowHeight = 921;
                
                Save();
            }
            catch
            {
                // Ignore errors on exit
            }
        }

        // Helper method to update window metrics
        public void UpdateWindowMetrics(double left, double top, double width, double height)
        {
            WindowLeft = left;
            WindowTop = top;
            WindowWidth = width;
            WindowHeight = height;
            Save(); // Save immediately when window metrics are updated
        }

        public void AddBookmark(Bookmark bookmark)
        {
            try
            {
                // Always store just the filename for consistency
                bookmark.BookPath = Path.GetFileName(bookmark.BookPath);
                LogInfo($"Storing bookmark with path: {bookmark.BookPath}");

                // Save the bookmark using the base bookmarks path
                bookmark.Save(BookmarksPath);

                // Add to in-memory collection
                Bookmarks.Add(bookmark);
                LogInfo($"Added bookmark for {bookmark.BookPath} at Chapter {bookmark.ChapterIndex}, Page {bookmark.PageIndex}");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error adding bookmark");
            }
        }

        public void RemoveBookmark(Bookmark bookmark)
        {
            try
            {
                // Remove from in-memory collection
                Bookmarks.RemoveAll(b => 
                    b.BookPath == bookmark.BookPath && 
                    b.ChapterIndex == bookmark.ChapterIndex && 
                    b.PageIndex == bookmark.PageIndex);

                // Delete the bookmark file
                bookmark.Delete(BookmarksPath);

                LogInfo($"Removed bookmark for {bookmark.BookPath} at Chapter {bookmark.ChapterIndex}, Page {bookmark.PageIndex}");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error removing bookmark");
            }
        }

        public bool HasBookmark(Bookmark bookmark)
        {
            var bookFileName = Path.GetFileName(bookmark.BookPath);
            return Bookmarks.Any(b => 
                Path.GetFileName(b.BookPath) == bookFileName && 
                b.ChapterIndex == bookmark.ChapterIndex && 
                b.PageIndex == bookmark.PageIndex);
        }

        public IEnumerable<Bookmark> GetBookmarksForBook(string bookPath)
        {
            var bookFileName = Path.GetFileName(bookPath);
            return Bookmarks.Where(b => Path.GetFileName(b.BookPath) == bookFileName)
                .OrderBy(b => b.ChapterIndex)
                .ThenBy(b => b.PageIndex);
        }

        public void AddPronunciation(string word, string pronunciation)
        {
            word = word.Trim();
            pronunciation = pronunciation.Trim();
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(pronunciation))
                return;

            PronunciationDictionary[word] = pronunciation;
            LogInfo($"Added pronunciation: {word} -> {pronunciation}");
            SavePronunciations();
        }

        public void RemovePronunciation(string word)
        {
            if (PronunciationDictionary.Remove(word))
            {
                LogInfo($"Removed pronunciation for: {word}");
                SavePronunciations();
            }
        }
    }
} 