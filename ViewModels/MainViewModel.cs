using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KokoroReader.Models;
using KokoroReader.Controls;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using VersOne.Epub;
using VersOne.Epub.Schema;
using VersOne.Epub.Options;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Threading;
using KokoroReader.Extensions;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Input;
using System.Linq;
using System.ComponentModel;
using HtmlAgilityPack;
using System.Diagnostics;
using NLog;

namespace KokoroReader.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // Cache regex patterns for better performance
        private static readonly Regex ScriptRegex = new(@"<script[^>]*>.*?</script>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex StyleRegex = new(@"<style[^>]*>.*?</style>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex EventRegex = new(@"(javascript|onerror|onload|onclick|onmouseover):[^""']*[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex XmlnsRegex = new(@"xmlns(?::\w+)?=""[^""]*""", RegexOptions.Compiled);
        private static readonly Regex RomanNumeralRegex = new(@"\b(?:Chapter\s+)(M{0,4}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3}))\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private static readonly Dictionary<string, int> RomanNumeralValues = new()
        {
            { "M", 1000 },
            { "CM", 900 },
            { "D", 500 },
            { "CD", 400 },
            { "C", 100 },
            { "XC", 90 },
            { "L", 50 },
            { "XL", 40 },
            { "X", 10 },
            { "IX", 9 },
            { "V", 5 },
            { "IV", 4 },
            { "I", 1 }
        };

        private EpubBookRef? currentEpubBook;
        private readonly Dictionary<string, EpubLocalTextContentFileRef> epubChapterMap = new();
        private Theme currentTheme;
        private bool disposedValue;
        private readonly Settings settings;
        private readonly BookPositionManager positionManager;
        private KokoroTTS? tts;
        private KokoroVoice? currentVoice;
        private SynthesisHandle? currentSpeech;
        private bool isSpeaking;
        private bool isTTSInitialized;
        private bool _isCurrentPageBookmarked;
        private bool _hasBookmarksInCurrentBook;
        private double _bookmarkProximity;
        private bool isHandlingManualNavigation;
        private bool isAutoPageChange;
        private ScrollViewer? _contentScrollViewer;

        [ObservableProperty]
        private ObservableCollection<Book> recentBooks = new();

        [ObservableProperty]
        private Book? selectedBook;

        [ObservableProperty]
        private Book? currentBook;

        [ObservableProperty]
        private string currentContent = string.Empty;

        [ObservableProperty]
        private double currentProgress;

        [ObservableProperty]
        private double fontSize = 16.0;

        [ObservableProperty]
        private bool isTextToSpeechEnabled;

        [ObservableProperty]
        private double bookProgress;

        [ObservableProperty]
        private string pageNumberText = string.Empty;

        public ScrollViewer? ContentScrollViewer
        {
            get => _contentScrollViewer;
            set => SetProperty(ref _contentScrollViewer, value);
        }

        public bool IsCurrentPageBookmarked
        {
            get => _isCurrentPageBookmarked;
            private set => SetProperty(ref _isCurrentPageBookmarked, value);
        }

        public bool HasBookmarksInCurrentBook
        {
            get => _hasBookmarksInCurrentBook;
            private set => SetProperty(ref _hasBookmarksInCurrentBook, value);
        }

        public double BookmarkProximity
        {
            get => _bookmarkProximity;
            private set
            {
                if (_bookmarkProximity != value)
                {
                    Debug.WriteLine($"[BookmarkProximity] Value changing from {_bookmarkProximity} to {value}");
                    _bookmarkProximity = value;
                    OnPropertyChanged(nameof(BookmarkProximity));
                }
            }
        }

        public ICommand ToggleBookmarkCommand { get; }
        public ICommand NavigateToBookmarkCommand { get; }
        public ICommand OpenBookCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenPronunciationCommand { get; }
        public ICommand CloseCommand { get; }

        public Settings Settings => settings;

        private void OnFontSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.FontFamilyName) || 
                e.PropertyName == nameof(Settings.FontSize) ||
                e.PropertyName == nameof(Settings.TextAlignment))
            {
                if (e.PropertyName == nameof(Settings.FontSize))
                {
                    FontSize = settings.FontSize;
                }
                _ = UpdateContent();
            }
            else if (e.PropertyName == nameof(Settings.VoiceName))
            {
                try
                {
                    Debug.WriteLine($"[OnFontSettingsChanged] Voice name changed to: {settings.VoiceName}");
                    
                    // Stop any current speech and clear the current voice
                    StopSpeaking();
                    currentVoice = null;
                    
                    var voicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices", $"{settings.VoiceName}.npy");
                    Debug.WriteLine($"[OnFontSettingsChanged] Looking for voice file at: {voicePath}");
                    
                    if (File.Exists(voicePath))
                    {
                        Debug.WriteLine("[OnFontSettingsChanged] Voice file found, loading voice");
                        currentVoice = KokoroVoice.FromPath(voicePath);
                        Debug.WriteLine($"[OnFontSettingsChanged] Successfully loaded voice: {settings.VoiceName}");
                        
                        // If TTS is enabled, start speaking with new voice
                        if (IsTextToSpeechEnabled && CurrentBook?.CurrentChapter != null)
                        {
                            Debug.WriteLine("[OnFontSettingsChanged] TTS is enabled, speaking current page with new voice");
                            Application.Current.Dispatcher.BeginInvoke(() => SpeakCurrentPage());
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[OnFontSettingsChanged] Voice file not found at: {voicePath}");
                        MessageBox.Show($"Voice file not found at {voicePath}. Please ensure the voice file exists.", 
                            "Voice Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                        
                        // Revert to default voice if available
                        var defaultVoicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices", "af_heart.npy");
                        if (File.Exists(defaultVoicePath))
                        {
                            Debug.WriteLine("[OnFontSettingsChanged] Loading default voice");
                            currentVoice = KokoroVoice.FromPath(defaultVoicePath);
                            settings.VoiceName = "af_heart"; // Update settings to match the fallback
                            Debug.WriteLine("[OnFontSettingsChanged] Updated settings to use default voice");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OnFontSettingsChanged] Error changing voice: {ex}");
                    MessageBox.Show($"Failed to change voice: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (e.PropertyName == nameof(Settings.VoiceSpeed))
            {
                UpdateVoiceSpeed(settings.VoiceSpeed);
            }
        }

        public MainViewModel(Settings settings)
        {
            Debug.WriteLine("Initializing MainViewModel");
            this.settings = settings;
            this.positionManager = new BookPositionManager();
            currentTheme = settings.Theme;
            try
            {
                FontSize = settings.FontSize > 0 ? settings.FontSize : 16;
                
                settings.LoadBookmarks();
                settings.PropertyChanged += OnFontSettingsChanged;
                ApplyTheme(currentTheme);

                settings.ThemeChanged += theme => {
                    currentTheme = theme;
                    ApplyTheme(theme);
                };

                // Check if settings.conf exists
                bool settingsExist = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.conf"));
                
                if (!settingsExist)
                {
                    // If no settings exist, try to open the user guide
                    string userGuidePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_guide.epub");
                    if (File.Exists(userGuidePath))
                    {
                        settings.LastBookPath = userGuidePath;
                        _ = LoadLastBookAsync();
                    }
                }
                else if (!string.IsNullOrEmpty(settings.LastBookPath) && File.Exists(settings.LastBookPath))
                {
                    // If settings exist and last book path is valid, load the last book
                    _ = LoadLastBookAsync();
                }

                // Create commands with debug logging
                Debug.WriteLine("Creating commands");
                
                ToggleBookmarkCommand = new RelayCommand(
                    () => {
                        Debug.WriteLine($"ToggleBookmark executed. CurrentBook: {CurrentBook != null}, HasCurrentChapter: {CurrentBook?.CurrentChapter != null}");
                        ToggleBookmark();
                    },
                    () => {
                        var canExecute = CurrentBook != null;
                        Debug.WriteLine($"ToggleBookmark CanExecute: {canExecute}");
                        return canExecute;
                    });

                NavigateToBookmarkCommand = new RelayCommand(
                    () => {
                        Debug.WriteLine($"[Command] NavigateToBookmark executed. HasBookmarks: {HasBookmarksInCurrentBook}, CurrentBook: {CurrentBook != null}");
                        Logger.Info($"[Command] NavigateToBookmark executed. HasBookmarks: {HasBookmarksInCurrentBook}, CurrentBook: {CurrentBook != null}");
                        NavigateToBookmark();
                    },
                    () => {
                        var canExecute = HasBookmarksInCurrentBook;
                        Debug.WriteLine($"[Command] NavigateToBookmark CanExecute: {canExecute}, HasBookmarks: {HasBookmarksInCurrentBook}, CurrentBook: {CurrentBook != null}");
                        Logger.Info($"[Command] NavigateToBookmark CanExecute: {canExecute}, HasBookmarks: {HasBookmarksInCurrentBook}, CurrentBook: {CurrentBook != null}");
                        return canExecute;
                    });

                OpenBookCommand = new AsyncRelayCommand(OpenBook);

                // Add Settings command
                OpenSettingsCommand = new RelayCommand(
                    () => {
                        Debug.WriteLine("OpenSettings executed");
                        OpenSettings();
                    });

                // Add Pronunciation command
                OpenPronunciationCommand = new RelayCommand(
                    () => {
                        Debug.WriteLine("OpenPronunciation executed");
                        OpenPronunciation();
                    });

                // Add Close command
                CloseCommand = new RelayCommand(
                    () => {
                        Debug.WriteLine("Close command executed");
                        Application.Current.MainWindow.Close();
                    });

                InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                Debug.WriteLine("MainViewModel initialization completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Critical error in MainViewModel initialization: {ex}");
                MessageBox.Show($"Critical error initializing the application: {ex.Message}\n\nThe application will now close.",
                    "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown(1);
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                LoadRecentBooks();
                if (CurrentBook != null)
                {
                    UpdateBookmarkStatus();
                }
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private void LoadRecentBooks()
        {
            // Implementation without logging
        }

        private async Task OpenBook()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "All supported files (*.epub;*.txt)|*.epub;*.txt|EPUB files (*.epub)|*.epub|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Open Book"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var book = new Book { FilePath = dialog.FileName };

                    if (Path.GetExtension(dialog.FileName).ToLower() == ".txt")
                    {
                        await LoadTextBookAsync(book);
                    }
                    else
                    {
                        await LoadBookAsync(book);
                    }
                    
                    if (!RecentBooks.Any(b => b.FilePath == book.FilePath))
                    {
                        RecentBooks.Add(book);
                    }
                    
                    CurrentBook = book;
                    SelectedBook = book;
                    settings.LastBookPath = book.FilePath;
                    settings.Save();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening book: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task LoadTextBookAsync(Book book)
        {
            try
            {
                book.Title = Path.GetFileNameWithoutExtension(book.FilePath);
                book.Author = "Unknown Author";
                book.Chapters = new List<Chapter>();

                var content = await File.ReadAllTextAsync(book.FilePath);
                
                var chapter = new Chapter
                {
                    Title = "Content",
                    FilePath = book.FilePath,
                    Order = 0
                };
                book.Chapters.Add(chapter);
                book.CurrentChapterIndex = 0;

                var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                double pageHeight = 800;
                double pageWidth = 600;

                if (Application.Current.MainWindow is Window mainWindow)
                {
                    pageHeight = mainWindow.ActualHeight - 64;
                    pageWidth = mainWindow.ActualWidth - 64;
                }

                // Convert TextAlignment using the converter
                var converter = new Converters.TextAlignmentConverter();
                var wpfTextAlignment = (System.Windows.TextAlignment)converter.Convert(settings.TextAlignment, typeof(System.Windows.TextAlignment), null, System.Globalization.CultureInfo.CurrentCulture);

                chapter.Pages = KokoroReader.Models.Page.CreatePages(paragraphs, pageHeight, FontSize, 1.6, wpfTextAlignment, settings.FontFamilyName);
                chapter.CurrentPageIndex = 0;

                await UpdateContent();
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [RelayCommand]
        private async Task LoadBookAsync(Book book)
        {
            try
            {
                if (book == null)
                {
                    return;
                }

                // Store current book for later comparison
                var previousBook = CurrentBook;
                CurrentBook = book;

                // Reset state
                CurrentContent = string.Empty;
                currentEpubBook = null;
                epubChapterMap.Clear();

                if (Path.GetExtension(book.FilePath).ToLower() == ".txt")
                {
                    await LoadTextBookAsync(book);
                }
                else if (Path.GetExtension(book.FilePath).ToLower() == ".epub")
                {
                    if (!IsValidEpub(book.FilePath))
                    {
                        throw new InvalidOperationException("The file is not a valid EPUB file.");
                    }

                    currentEpubBook = await EpubReader.OpenBookAsync(book.FilePath);
                    if (currentEpubBook == null)
                    {
                        throw new InvalidOperationException("Failed to open the EPUB file.");
                    }

                    var chapters = await LoadChapters(currentEpubBook);
                    if (!chapters.Any())
                    {
                        throw new InvalidOperationException("No readable content found in the EPUB file.");
                    }

                    book.Chapters = chapters;
                    book.CurrentChapterIndex = 0;
                    await LoadChapterContent(book.Chapters[0]);
                }

                // Update bookmark status after book is loaded
                UpdateBookmarkStatus();
                
                // Force command state update
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    CommandManager.InvalidateRequerySuggested();
                    Logger.Info($"[LoadBookAsync] Forced command state update. HasBookmarks: {HasBookmarksInCurrentBook}, CurrentBook: {CurrentBook != null}");
                });

                // Save settings if this is a different book
                if (previousBook?.FilePath != book.FilePath)
                {
                    settings.LastBookPath = book.FilePath;
                    settings.Save();
                }
            }
            catch (Exception ex)
            {
                var message = GetFriendlyErrorMessage(ex);
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Logger.Error(ex, "Error loading book");
            }
        }

        private bool IsValidEpub(string filePath)
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);
                
                // Check for required epub files
                var hasContainer = archive.Entries.Any(e => e.FullName.EndsWith("META-INF/container.xml", StringComparison.OrdinalIgnoreCase));
                var hasMimetype = archive.Entries.Any(e => e.Name.Equals("mimetype", StringComparison.OrdinalIgnoreCase));
                
                if (!hasContainer || !hasMimetype)
                {
                    return false;
                }

                // Try to read the mimetype to verify it's an epub
                var mimetypeEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("mimetype", StringComparison.OrdinalIgnoreCase));
                if (mimetypeEntry != null)
                {
                    using var reader = new StreamReader(mimetypeEntry.Open());
                    var mimetype = reader.ReadToEnd().Trim();
                    return mimetype == "application/epub+zip";
                }

                return false;
            }
            catch (InvalidDataException)
            {
                // This indicates an unsupported compression method
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking epub validity: {ex}");
                return false;
            }
        }

        private string GetFriendlyErrorMessage(Exception ex)
        {
            if (ex is InvalidDataException)
            {
                return "This EPUB file uses an unsupported compression method. Please try converting it to a standard EPUB format.";
            }
            else if (ex is System.IO.IOException ioEx)
            {
                return "Unable to access the file. Make sure it's not in use by another program and you have permission to access it.";
            }
            else if (ex.Message.Contains("compression"))
            {
                return "This EPUB file uses an unsupported compression method. Please try converting it to a standard format using Calibre or another EPUB tool.";
            }
            else if (ex.Message.Contains("corrupted") || ex.Message.Contains("invalid"))
            {
                return "The EPUB file appears to be corrupted or invalid. Please verify the file integrity.";
            }

            // If we can't determine a specific error, return a more detailed message
            return $"Error loading book: {ex.Message}\n\nIf this is a compression issue, try converting the EPUB to a standard format using Calibre or another EPUB tool.";
        }

        private async Task<List<Chapter>> LoadChapters(EpubBookRef epubBook)
        {
            try
            {
                var chapters = new List<Chapter>();
                var navItems = await epubBook.GetNavigationAsync();

                foreach (var navItem in navItems)
                {
                    if (navItem.NestedItems != null && navItem.NestedItems.Any())
                    {
                        foreach (var nestedItem in navItem.NestedItems)
                        {
                            if (nestedItem.Link?.ContentFilePath != null)
                            {
                                var contentFileRef = epubBook.Content.Html.Local.FirstOrDefault(f => f.FilePath.EndsWith(nestedItem.Link.ContentFilePath));
                                if (contentFileRef != null)
                                {
                                    var content = await contentFileRef.ReadContentAsTextAsync();
                                    var chapter = new Chapter
                                    {
                                        Title = nestedItem.Title,
                                        Content = content,
                                        FilePath = nestedItem.Link.ContentFilePath
                                    };
                                    chapters.Add(chapter);
                                }
                            }
                        }
                    }
                    else if (navItem.Link?.ContentFilePath != null)
                    {
                        var contentFileRef = epubBook.Content.Html.Local.FirstOrDefault(f => f.FilePath.EndsWith(navItem.Link.ContentFilePath));
                        if (contentFileRef != null)
                        {
                            var content = await contentFileRef.ReadContentAsTextAsync();
                            var chapter = new Chapter
                            {
                                Title = navItem.Title,
                                Content = content,
                                FilePath = navItem.Link.ContentFilePath
                            };
                            chapters.Add(chapter);
                        }
                    }
                }

                // If no chapters were found through navigation, try to load all HTML files as chapters
                if (!chapters.Any())
                {
                    foreach (var htmlFile in epubBook.Content.Html.Local)
                    {
                        var content = await htmlFile.ReadContentAsTextAsync();
                        var chapter = new Chapter
                        {
                            Title = Path.GetFileNameWithoutExtension(htmlFile.FilePath),
                            Content = content,
                            FilePath = htmlFile.FilePath
                        };
                        chapters.Add(chapter);
                    }
                }

                return chapters;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading chapters: {ex}");
                throw;
            }
        }

        private async Task LoadChapterContent(Chapter? chapter, int? targetPageIndex = null)
        {
            try
            {
                if (chapter == null)
                {
                    return;
                }

                // Load content in background
                await Task.Run(async () =>
                {
                    string content = await chapter.GetContentAsync();
                    if (string.IsNullOrEmpty(content))
                    {
                        return;
                    }

                    // Process HTML in background
                    var cleanedContent = await Task.Run(() =>
                    {
                        string cleaned = content;
                        cleaned = ScriptRegex.Replace(cleaned, "");
                        cleaned = StyleRegex.Replace(cleaned, "");
                        cleaned = EventRegex.Replace(cleaned, "");
                        cleaned = System.Net.WebUtility.HtmlDecode(cleaned);
                        return cleaned;
                    });

                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(cleanedContent);

                    var paragraphs = new List<string>();
                    var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//body") ?? htmlDoc.DocumentNode;
                    var processedContent = new HashSet<string>();

                    // Process paragraphs in background
                    await Task.Run(() =>
                    {
                        // Get all text-containing elements
                        var textNodes = bodyNode.DescendantsAndSelf()
                            .Where(n => n.NodeType == HtmlNodeType.Element &&
                                      !string.IsNullOrWhiteSpace(n.InnerText) &&
                                      (n.Name == "p" || n.Name == "div" ||
                                       n.Name == "h1" || n.Name == "h2" || n.Name == "h3" ||
                                       n.Name == "h4" || n.Name == "h5" || n.Name == "h6" ||
                                       n.Name == "span"))
                            .ToList();

                        string currentParagraph = "";
                        bool isCollectingParagraph = false;

                        foreach (var node in textNodes)
                        {
                            // Skip empty nodes or nodes that only contain whitespace/punctuation
                            if (string.IsNullOrWhiteSpace(node.InnerText) || 
                                node.InnerText.Trim() == "&nbsp;" ||
                                (node.InnerText.Trim().Length == 1 && char.IsPunctuation(node.InnerText.Trim()[0])))
                            {
                                continue;
                            }

                            // Handle headings as separate paragraphs
                            if (node.Name.StartsWith("h") && node.Name.Length == 2)
                            {
                                if (isCollectingParagraph)
                                {
                                    if (!string.IsNullOrWhiteSpace(currentParagraph))
                                    {
                                        paragraphs.Add(currentParagraph);
                                    }
                                    currentParagraph = "";
                                    isCollectingParagraph = false;
                                }
                                paragraphs.Add(node.OuterHtml);
                                continue;
                            }

                            // For paragraphs and divs, treat them as potential paragraph breaks
                            if (node.Name == "p" || node.Name == "div")
                            {
                                if (isCollectingParagraph)
                                {
                                    if (!string.IsNullOrWhiteSpace(currentParagraph))
                                    {
                                        paragraphs.Add(currentParagraph);
                                    }
                                    currentParagraph = "";
                                }
                                
                                string text = node.InnerHtml.Trim();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    // Check if this text is just a continuation (e.g., standalone punctuation)
                                    if (text.Length == 1 && char.IsPunctuation(text[0]))
                                    {
                                        if (paragraphs.Count > 0)
                                        {
                                            paragraphs[paragraphs.Count - 1] = paragraphs[paragraphs.Count - 1].TrimEnd() + text;
                                        }
                                    }
                                    else
                                    {
                                        currentParagraph = $"<p>{text}</p>";
                                        isCollectingParagraph = true;
                                    }
                                }
                            }
                            // For spans and other inline elements, append to current paragraph
                            else
                            {
                                string text = node.InnerHtml.Trim();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    if (!isCollectingParagraph)
                                    {
                                        currentParagraph = "<p>";
                                        isCollectingParagraph = true;
                                    }
                                    currentParagraph += text;
                                }
                            }
                        }

                        // Add the last paragraph if there is one
                        if (isCollectingParagraph && !string.IsNullOrWhiteSpace(currentParagraph))
                        {
                            if (!currentParagraph.EndsWith("</p>"))
                            {
                                currentParagraph += "</p>";
                            }
                            paragraphs.Add(currentParagraph);
                        }

                        // Clean up the paragraphs
                        paragraphs = paragraphs
                            .Select(p => WhitespaceRegex.Replace(p, " "))
                            .Select(p => XmlnsRegex.Replace(p, ""))
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .Distinct()
                            .ToList();
                    });

                    // Update UI on dispatcher thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        double pageHeight = Application.Current.MainWindow.ActualHeight;
                        pageHeight -= 100;
                        pageHeight = Math.Max(pageHeight, 400);

                        var converter = new Converters.TextAlignmentConverter();
                        var wpfTextAlignment = (System.Windows.TextAlignment)converter.Convert(
                            settings.TextAlignment,
                            typeof(System.Windows.TextAlignment),
                            null,
                            System.Globalization.CultureInfo.CurrentCulture);

                        chapter.Pages = KokoroReader.Models.Page.CreatePages(
                            paragraphs,
                            pageHeight,
                            FontSize,
                            1.6,
                            wpfTextAlignment,
                            settings.FontFamilyName);

                        int pageIndex = targetPageIndex ?? FindFirstNonEmptyPage(chapter);
                        if (pageIndex >= chapter.Pages.Count)
                        {
                            pageIndex = chapter.Pages.Count - 1;
                        }
                        if (pageIndex < 0)
                        {
                            pageIndex = 0;
                        }
                        
                        chapter.CurrentPageIndex = pageIndex;

                        if (chapter.Pages.Count > 0 && pageIndex >= 0 && pageIndex < chapter.Pages.Count)
                        {
                            string pageContent = chapter.Pages[pageIndex].Content;
                            UpdateContent(pageContent);
                            UpdateBookProgress();
                            UpdatePageNumber();
                            
                            // Save the current position
                            if (CurrentBook != null)
                            {
                                _ = positionManager.SavePosition(
                                    CurrentBook.FilePath,
                                    CurrentBook.CurrentChapterIndex,
                                    pageIndex);
                                    
                                // Also update settings
                                settings.LastBookPath = CurrentBook.FilePath;
                                settings.LastChapterIndex = CurrentBook.CurrentChapterIndex;
                                settings.Save();

                                // Update bookmark status after loading content
                                UpdateBookmarkStatus();
                            }
                        }
                        else
                        {
                            UpdateContent(string.Empty);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading chapter content: {ex}");
                throw;
            }
        }

        private int FindFirstNonEmptyPage(Chapter chapter)
        {
            if (chapter?.Pages == null || chapter.Pages.Count == 0)
                return 0;

            for (int i = 0; i < chapter.Pages.Count; i++)
            {
                if (!chapter.Pages[i].IsEmpty())
                    return i;
            }
            return 0;
        }

        private async Task UpdateContent()
        {
            try
            {
                if (CurrentBook?.CurrentChapter == null)
                {
                    CurrentContent = string.Empty;
                    return;
                }

                string content = await CurrentBook.CurrentChapter.GetContentAsync();
                if (string.IsNullOrEmpty(content))
                {
                    CurrentContent = string.Empty;
                    return;
                }

                UpdateContent(content);
                UpdateBookProgress();

                if (IsTextToSpeechEnabled)
                {
                    SpeakCurrentPage();
                }
            }
            catch (Exception ex)
            {
                UpdateContent(string.Empty);
            }
        }

        private void UpdateContent(string content)
        {
            try
            {
                if (string.IsNullOrEmpty(content))
                {
                    CurrentContent = string.Empty;
                    return;
                }

                // Prevent duplicate content
                if (content == CurrentContent)
                {
                    return;
                }

                string cleanedContent = content;
                cleanedContent = ScriptRegex.Replace(cleanedContent, "");
                cleanedContent = StyleRegex.Replace(cleanedContent, "");
                cleanedContent = EventRegex.Replace(cleanedContent, "");
                cleanedContent = System.Net.WebUtility.HtmlDecode(cleanedContent);

                CurrentContent = cleanedContent;
                
                if (ContentScrollViewer != null)
                {
                    ContentScrollViewer.ScrollToTop();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating content: {ex}");
            }
        }

        private void UpdateBookProgress()
        {
            try
            {
                if (CurrentBook?.Chapters == null || !CurrentBook.Chapters.Any())
                {
                    BookProgress = 0;
                    return;
                }

                double totalLength = CurrentBook.Chapters.Sum(c => c.Content.Length);
                if (totalLength == 0)
                {
                    BookProgress = 0;
                    return;
                }

                double progress = 0;
                for (int i = 0; i < CurrentBook.CurrentChapterIndex; i++)
                {
                    progress += (CurrentBook.Chapters[i].Content.Length / totalLength) * 100;
                }

                if (CurrentBook.CurrentChapter?.Pages != null && CurrentBook.CurrentChapter.Pages.Count > 0)
                {
                    double chapterWeight = (CurrentBook.CurrentChapter.Content.Length / totalLength) * 100;
                    double currentPageProgress = (double)CurrentBook.CurrentChapter.CurrentPageIndex / CurrentBook.CurrentChapter.Pages.Count;
                    progress += chapterWeight * currentPageProgress;
                }

                BookProgress = Math.Min(100, Math.Max(0, progress));
            }
            catch (Exception ex)
            {
                BookProgress = 0;
            }
        }

        public void HandleContentOverflow(double remainingHeight)
        {
            if (isHandlingManualNavigation)
            {
                return;
            }

            if (CurrentBook == null || CurrentBook.CurrentChapter == null || 
                CurrentBook.CurrentChapter.CurrentPageIndex >= CurrentBook.CurrentChapter.Pages.Count - 1)
            {
                return;
            }

            if (remainingHeight > FontSize * 2)
            {
                _ = NextPageCommand.ExecuteAsync(null);
            }
        }

        partial void OnSelectedBookChanged(Book? oldValue, Book? newValue)
        {
            if (newValue != null)
            {
                _ = LoadBookAsync(newValue);
            }
        }

        [RelayCommand]
        private async Task NextPage()
        {
            try
            {
                isHandlingManualNavigation = !isAutoPageChange;
                if (!isAutoPageChange) // Only stop speaking if this is not an auto page change
                {
                    StopSpeaking();
                    IsTextToSpeechEnabled = false; // Also disable TTS for manual navigation
                }
                
                if (CurrentBook?.CurrentChapter == null)
                {
                    return;
                }

                var currentChapter = CurrentBook.CurrentChapter;
                bool navigated = false;

                // If we have pages and not at the last page of current chapter
                if (currentChapter.Pages.Count > 0 && currentChapter.CurrentPageIndex < currentChapter.Pages.Count - 1)
                {
                    // Find next non-empty page
                    int nextPageIndex = currentChapter.CurrentPageIndex + 1;
                    while (nextPageIndex < currentChapter.Pages.Count && 
                           currentChapter.Pages[nextPageIndex].IsEmpty())
                    {
                        nextPageIndex++;
                    }

                    if (nextPageIndex < currentChapter.Pages.Count)
                    {
                        currentChapter.CurrentPageIndex = nextPageIndex;
                        UpdateContent(currentChapter.Pages[nextPageIndex].Content);
                        UpdateBookProgress();
                        UpdatePageNumber();
                        navigated = true;
                        
                        // Save position after navigation
                        await positionManager.SavePosition(
                            CurrentBook.FilePath,
                            CurrentBook.CurrentChapterIndex,
                            nextPageIndex);

                        // Update bookmark status after navigation
                        UpdateBookmarkStatus();
                    }
                }

                // If we couldn't navigate to next page in current chapter, try next chapter
                if (!navigated && CurrentBook.CurrentChapterIndex < CurrentBook.Chapters.Count - 1)
                {
                    CurrentBook.CurrentChapterIndex++;
                    await LoadChapterContent(CurrentBook.CurrentChapter);
                    
                    if (CurrentBook.CurrentChapter?.Pages.Count > 0)
                    {
                        // Find first non-empty page in new chapter
                        int firstNonEmptyPage = 0;
                        while (firstNonEmptyPage < CurrentBook.CurrentChapter.Pages.Count && 
                               CurrentBook.CurrentChapter.Pages[firstNonEmptyPage].IsEmpty())
                        {
                            firstNonEmptyPage++;
                        }

                        if (firstNonEmptyPage < CurrentBook.CurrentChapter.Pages.Count)
                        {
                            CurrentBook.CurrentChapter.CurrentPageIndex = firstNonEmptyPage;
                            UpdateContent(CurrentBook.CurrentChapter.Pages[firstNonEmptyPage].Content);
                            navigated = true;
                            
                            // Save position after navigation
                            await positionManager.SavePosition(
                                CurrentBook.FilePath,
                                CurrentBook.CurrentChapterIndex,
                                firstNonEmptyPage);

                            // Update bookmark status after navigation
                            UpdateBookmarkStatus();
                        }
                    }
                    
                    if (navigated)
                    {
                        UpdateBookProgress();
                        UpdatePageNumber();
                    }
                }

                // If we couldn't navigate at all, we're at the end of the book
                if (!navigated && IsTextToSpeechEnabled)
                {
                    IsTextToSpeechEnabled = false;
                }
            }
            catch (Exception ex)
            {
                // Ignore errors during page navigation
            }
            finally
            {
                isHandlingManualNavigation = false;
            }
        }

        [RelayCommand]
        private async Task PreviousPage()
        {
            try
            {
                isHandlingManualNavigation = true;
                StopSpeaking();
                
                if (CurrentBook?.CurrentChapter == null)
                {
                    return;
                }

                var currentChapter = CurrentBook.CurrentChapter;

                // If we have pages and not at the first page of current chapter
                if (currentChapter.Pages.Count > 0 && currentChapter.CurrentPageIndex > 0)
                {
                    // Find previous non-empty page
                    int previousPageIndex = currentChapter.CurrentPageIndex - 1;
                    while (previousPageIndex >= 0 && 
                           currentChapter.Pages[previousPageIndex].IsEmpty())
                    {
                        previousPageIndex--;
                    }

                    if (previousPageIndex >= 0)
                    {
                        currentChapter.CurrentPageIndex = previousPageIndex;
                        UpdateContent(currentChapter.Pages[previousPageIndex].Content);
                        UpdateBookProgress();
                        UpdatePageNumber();
                        
                        // Save position after navigation
                        await positionManager.SavePosition(
                            CurrentBook.FilePath,
                            CurrentBook.CurrentChapterIndex,
                            previousPageIndex);

                        // Update bookmark status after navigation
                        UpdateBookmarkStatus();
                        return;
                    }
                }

                // If at the first page of current chapter, move to previous chapter
                if (CurrentBook.CurrentChapterIndex > 0)
                {
                    CurrentBook.CurrentChapterIndex--;
                    await LoadChapterContent(CurrentBook.CurrentChapter);
                    
                    // Find last non-empty page in previous chapter
                    if (CurrentBook.CurrentChapter?.Pages.Count > 0)
                    {
                        int lastNonEmptyPage = CurrentBook.CurrentChapter.Pages.Count - 1;
                        while (lastNonEmptyPage >= 0 && 
                               CurrentBook.CurrentChapter.Pages[lastNonEmptyPage].IsEmpty())
                        {
                            lastNonEmptyPage--;
                        }

                        if (lastNonEmptyPage >= 0)
                        {
                            CurrentBook.CurrentChapter.CurrentPageIndex = lastNonEmptyPage;
                            UpdateContent(CurrentBook.CurrentChapter.Pages[lastNonEmptyPage].Content);
                            
                            // Save position after navigation
                            await positionManager.SavePosition(
                                CurrentBook.FilePath,
                                CurrentBook.CurrentChapterIndex,
                                lastNonEmptyPage);

                            // Update bookmark status after navigation
                            UpdateBookmarkStatus();
                        }
                    }
                    
                    UpdateBookProgress();
                    UpdatePageNumber();
                }
            }
            catch (Exception ex)
            {
                // Ignore errors during navigation
            }
            finally
            {
                isHandlingManualNavigation = false;
            }
        }

        private void UpdateProgress(ScrollViewer scrollViewer)
        {
            if (scrollViewer.ScrollableHeight > 0)
            {
                BookProgress = (scrollViewer.VerticalOffset / scrollViewer.ScrollableHeight) * 100;
            }
        }

        [RelayCommand]
        private void IncreaseFontSize()
        {
            if (FontSize < 32)
            {
                FontSize += 2;
                settings.FontSize = FontSize;
            }
        }

        [RelayCommand]
        private void DecreaseFontSize()
        {
            if (FontSize > 8)
            {
                FontSize -= 2;
                settings.FontSize = FontSize;
            }
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            try
            {
                currentTheme = currentTheme switch
                {
                    Theme.Light => Theme.Sepia,
                    Theme.Sepia => Theme.Dark,
                    Theme.Dark => Theme.Light,
                    _ => Theme.Light
                };
                ApplyTheme(currentTheme);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error changing theme: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public class DynamicBrushManager : DependencyObject
        {
            private static readonly DynamicBrushManager Instance = new();
            private static readonly Dictionary<string, WeakReference<SolidColorBrush>> _brushCache = new();

            public static readonly DependencyProperty TextColorProperty =
                DependencyProperty.Register(nameof(TextColor), typeof(Color), typeof(DynamicBrushManager),
                    new PropertyMetadata(Colors.Black, OnColorPropertyChanged));
            
            public static readonly DependencyProperty BackgroundColorProperty =
                DependencyProperty.Register(nameof(BackgroundColor), typeof(Color), typeof(DynamicBrushManager),
                    new PropertyMetadata(Colors.White, OnColorPropertyChanged));
            
            public static readonly DependencyProperty SidebarColorProperty =
                DependencyProperty.Register(nameof(SidebarColor), typeof(Color), typeof(DynamicBrushManager),
                    new PropertyMetadata(Colors.White, OnColorPropertyChanged));
            
            public static readonly DependencyProperty AccentColorProperty =
                DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(DynamicBrushManager),
                    new PropertyMetadata(Colors.Purple, OnColorPropertyChanged));
            
            public static readonly DependencyProperty BorderColorProperty =
                DependencyProperty.Register(nameof(BorderColor), typeof(Color), typeof(DynamicBrushManager),
                    new PropertyMetadata(Colors.Gray, OnColorPropertyChanged));
            
            public static readonly DependencyProperty SecondaryTextColorProperty =
                DependencyProperty.Register(nameof(SecondaryTextColor), typeof(Color), typeof(DynamicBrushManager),
                    new PropertyMetadata(Colors.Gray, OnColorPropertyChanged));

            public Color TextColor
            {
                get => (Color)GetValue(TextColorProperty);
                set => SetValue(TextColorProperty, value);
            }

            public Color BackgroundColor
            {
                get => (Color)GetValue(BackgroundColorProperty);
                set => SetValue(BackgroundColorProperty, value);
            }

            public Color SidebarColor
            {
                get => (Color)GetValue(SidebarColorProperty);
                set => SetValue(SidebarColorProperty, value);
            }

            public Color AccentColor
            {
                get => (Color)GetValue(AccentColorProperty);
                set => SetValue(AccentColorProperty, value);
            }

            public Color BorderColor
            {
                get => (Color)GetValue(BorderColorProperty);
                set => SetValue(BorderColorProperty, value);
            }

            public Color SecondaryTextColor
            {
                get => (Color)GetValue(SecondaryTextColorProperty);
                set => SetValue(SecondaryTextColorProperty, value);
            }

            private static void OnColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                var color = (Color)e.NewValue;
                var propertyName = e.Property.Name;
                var brushName = propertyName.Replace("Color", "Brush");
                
                // Create a new brush and update the resource
                var brush = new SolidColorBrush(color);
                Application.Current.Resources[brushName] = brush;
            }

            public static SolidColorBrush GetBrush(string name)
            {
                var brushName = name.EndsWith("Brush") ? name : name + "Brush";

                // Get the color from the appropriate property
                var color = name.ToLower() switch
                {
                    "text" => Instance.TextColor,
                    "background" => Instance.BackgroundColor,
                    "sidebar" => Instance.SidebarColor,
                    "accent" => Instance.AccentColor,
                    "border" => Instance.BorderColor,
                    "secondarytext" => Instance.SecondaryTextColor,
                    _ => Colors.Black
                };

                // Create a new brush and bind its color to the property
                var brush = new SolidColorBrush(color);
                var binding = new Binding($"{name}Color")
                {
                    Source = Instance,
                    Mode = BindingMode.OneWay
                };
                
                BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, binding);
                return brush;
            }

            public static void SetThemeColors(Theme theme)
            {
                try
                {
                    switch (theme)
                    {
                        case Theme.Light:
                            Instance.BackgroundColor = Colors.White;
                            Instance.SidebarColor = Color.FromRgb(245, 245, 245);
                            Instance.TextColor = Colors.Black;
                            Instance.AccentColor = Color.FromRgb(103, 58, 183);
                            Instance.BorderColor = Color.FromRgb(221, 221, 221);
                            Instance.SecondaryTextColor = Color.FromRgb(102, 102, 102);
                            break;
                        case Theme.Sepia:
                            Instance.BackgroundColor = Color.FromRgb(249, 241, 228);
                            Instance.SidebarColor = Color.FromRgb(244, 236, 223);
                            Instance.TextColor = Color.FromRgb(93, 71, 49);
                            Instance.AccentColor = Color.FromRgb(149, 117, 205);
                            Instance.BorderColor = Color.FromRgb(221, 213, 200);
                            Instance.SecondaryTextColor = Color.FromRgb(143, 121, 99);
                            break;
                        case Theme.Dark:
                            Instance.BackgroundColor = Color.FromRgb(30, 30, 30);
                            Instance.SidebarColor = Color.FromRgb(45, 45, 45);
                            Instance.TextColor = Colors.White;
                            Instance.AccentColor = Color.FromRgb(149, 117, 205);
                            Instance.BorderColor = Color.FromRgb(70, 70, 70);
                            Instance.SecondaryTextColor = Color.FromRgb(170, 170, 170);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Ignore errors during theme color setting
                }
            }

            public static DynamicBrushManager GetInstance() => Instance;
        }

        private void ApplyTheme(Theme theme)
        {
            try
            {
                var resources = Application.Current.Resources;
                DynamicBrushManager.SetThemeColors(theme);
                resources["BrushManager"] = DynamicBrushManager.GetInstance();

                foreach (var name in new[] { "Text", "Background", "Sidebar", "Accent", "Border", "SecondaryText" })
                {
                    try
                    {
                        var brush = DynamicBrushManager.GetBrush(name);
                        resources[$"{name}Brush"] = brush;
                    }
                    catch (Exception ex)
                    {
                        // Ignore errors during brush creation
                    }
                }

                resources["FontSizeToZoomConverter"] = new KokoroReader.Converters.FontSizeToZoomConverter();
                resources["LineHeightConverter"] = new KokoroReader.Converters.LineHeightConverter();
                
                if (CurrentBook?.CurrentChapter != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var currentContent = CurrentContent;
                        CurrentContent = string.Empty;
                        
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            CurrentContent = currentContent;
                            _ = UpdateContent();
                        }), DispatcherPriority.Render);
                    }), DispatcherPriority.Render);
                }
            }
            catch (Exception ex)
            {
                // Ignore errors during theme application
            }
        }

        private void InitializeTTS()
        {
            if (isTTSInitialized)
            {
                Debug.WriteLine("[InitializeTTS] TTS already initialized, returning");
                return;
            }

            try
            {
                Debug.WriteLine("[InitializeTTS] Starting TTS initialization");
                var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kokoro.onnx");
                Debug.WriteLine($"[InitializeTTS] Looking for model at: {modelPath}");

                if (!File.Exists(modelPath))
                {
                    Debug.WriteLine("[InitializeTTS] Model file not found");
                    MessageBox.Show($"TTS model file not found at {modelPath}. Please ensure the kokoro.onnx file is present in the application directory.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Debug.WriteLine("[InitializeTTS] Creating KokoroTTS instance");
                tts = new KokoroTTS(modelPath);
                tts.NicifyAudio = true;

                Debug.WriteLine($"[InitializeTTS] Current voice name from settings: {settings.VoiceName}");
                var voicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices", $"{settings.VoiceName}.npy");
                Debug.WriteLine($"[InitializeTTS] Looking for voice file at: {voicePath}");

                if (File.Exists(voicePath))
                {
                    Debug.WriteLine("[InitializeTTS] Loading selected voice");
                    currentVoice = KokoroVoice.FromPath(voicePath);
                    Debug.WriteLine($"[InitializeTTS] Successfully loaded voice: {settings.VoiceName}");
                }
                else
                {
                    Debug.WriteLine("[InitializeTTS] Selected voice not found, trying fallback");
                    var defaultVoicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices", "af_heart.npy");
                    Debug.WriteLine($"[InitializeTTS] Looking for default voice at: {defaultVoicePath}");
                    
                    if (File.Exists(defaultVoicePath))
                    {
                        Debug.WriteLine("[InitializeTTS] Loading default voice");
                        currentVoice = KokoroVoice.FromPath(defaultVoicePath);
                        settings.VoiceName = "af_heart"; // Update settings to match the fallback
                        Debug.WriteLine("[InitializeTTS] Updated settings to use default voice");
                    }
                    else
                    {
                        Debug.WriteLine("[InitializeTTS] No voices found");
                        MessageBox.Show($"Voice file not found at {voicePath}. Please ensure the voice files are present in the voices directory.", 
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                isTTSInitialized = true;
                Debug.WriteLine("[InitializeTTS] TTS initialization completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InitializeTTS] Error initializing TTS: {ex}");
                MessageBox.Show($"Failed to initialize TTS: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateVoiceSpeed(double newSpeed)
        {
            try
            {
                if (!isTTSInitialized || tts == null || !isSpeaking || currentSpeech == null)
                {
                    return;
                }

                var pipelineConfig = new KokoroTTSPipelineConfig { Speed = (float)newSpeed };
                
                var content = CurrentBook?.CurrentChapter?.CurrentPage?.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return;
                }

                content = settings.ApplyPronunciations(content);

                var previousSpeech = currentSpeech;
                currentSpeech = tts.SpeakFast(content, currentVoice, pipelineConfig);

                currentSpeech.OnSpeechCompleted += (packet) =>
                {
                    isSpeaking = false;
                    
                    if (IsTextToSpeechEnabled)
                    {
                        Application.Current.Dispatcher.BeginInvoke(() => 
                        {
                            isAutoPageChange = true;
                            try
                            {
                                NextPageCommand.Execute(null);
                                if (IsTextToSpeechEnabled && !isHandlingManualNavigation)
                                {
                                    SpeakCurrentPage();
                                }
                            }
                            finally
                            {
                                isAutoPageChange = false;
                            }
                        });
                    }
                };

                currentSpeech.OnSpeechCanceled += (packet) =>
                {
                    isSpeaking = false;
                };
            }
            catch (Exception ex)
            {
                // Ignore errors during voice speed update
            }
        }

        private string ConvertRomanNumerals(string text)
        {
            return RomanNumeralRegex.Replace(text, match =>
            {
                string fullMatch = match.Groups[0].Value;
                string romanNumeral = match.Groups[1].Value.ToUpper();
                
                // Skip conversion if the Roman numeral part is empty
                if (string.IsNullOrEmpty(romanNumeral))
                {
                    return fullMatch;
                }
                
                int result = 0;
                int i = 0;
                
                while (i < romanNumeral.Length)
                {
                    if (i + 1 < romanNumeral.Length)
                    {
                        string doubleSymbol = romanNumeral.Substring(i, 2);
                        if (RomanNumeralValues.ContainsKey(doubleSymbol))
                        {
                            result += RomanNumeralValues[doubleSymbol];
                            i += 2;
                            continue;
                        }
                    }
                    
                    string singleSymbol = romanNumeral.Substring(i, 1);
                    if (RomanNumeralValues.ContainsKey(singleSymbol))
                    {
                        result += RomanNumeralValues[singleSymbol];
                    }
                    i++;
                }
                
                // Only attempt replacement if we found a valid Roman numeral
                return result > 0 ? fullMatch.Replace(romanNumeral, result.ToString()) : fullMatch;
            });
        }

        private void SpeakCurrentPage()
        {
            try
            {
                if (!IsTextToSpeechEnabled || tts == null || currentVoice == null || 
                    CurrentBook?.CurrentChapter?.CurrentPage == null)
                {
                    return;
                }

                StopSpeaking(); // Always stop current speech before starting new one
                
                string content;
                var currentPage = CurrentBook.CurrentChapter.CurrentPage;
                
                // First try to get content directly from the page
                if (currentPage != null && !string.IsNullOrWhiteSpace(currentPage.Content))
                {
                    content = currentPage.Content;
                    // Clean up any HTML tags
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"<[^>]+>", string.Empty);
                    content = System.Net.WebUtility.HtmlDecode(content);
                }
                else
                {
                    // Fallback to getting content from the viewer
                    var contentViewer = Application.Current.MainWindow?.FindName("ContentViewer") as BookContentViewer;
                    if (contentViewer == null)
                    {
                        return;
                    }

                    content = contentViewer.GetCleanTextContent();
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return;
                }
                
                content = ConvertRomanNumerals(content);
                content = settings.ApplyPronunciations(content);
                
                var pipelineConfig = new KokoroTTSPipelineConfig { Speed = (float)settings.VoiceSpeed };
                currentSpeech = tts.SpeakFast(content, currentVoice, pipelineConfig);
                isSpeaking = true;

                currentSpeech.OnSpeechCompleted += (packet) =>
                {
                    isSpeaking = false;
                    
                    if (IsTextToSpeechEnabled)
                    {
                        Application.Current.Dispatcher.BeginInvoke(() => 
                        {
                            isAutoPageChange = true;
                            try
                            {
                                NextPageCommand.Execute(null);
                                if (IsTextToSpeechEnabled && !isHandlingManualNavigation)
                                {
                                    SpeakCurrentPage();
                                }
                            }
                            finally
                            {
                                isAutoPageChange = false;
                            }
                        });
                    }
                };

                currentSpeech.OnSpeechCanceled += (packet) =>
                {
                    isSpeaking = false;
                };
            }
            catch (Exception ex)
            {
                isSpeaking = false;
                Logger.Error(ex, "Error in SpeakCurrentPage");
            }
        }

        public void StopSpeaking()
        {
            try
            {
                if (isSpeaking && tts != null && !isAutoPageChange)
                {
                    tts.StopPlayback();
                    isSpeaking = false;
                }
            }
            catch (Exception ex)
            {
                // Ignore errors during speech stopping
            }
        }

        [RelayCommand]
        private void ToggleTextToSpeech()
        {
            try
            {
                if (!isTTSInitialized)
                {
                    InitializeTTS();
                }

                if (tts == null || currentVoice == null)
                {
                    return;
                }

                IsTextToSpeechEnabled = !IsTextToSpeechEnabled;

                if (IsTextToSpeechEnabled)
                {
                    SpeakCurrentPage();
                }
                else
                {
                    StopSpeaking();
                }
            }
            catch (Exception ex)
            {
                // Ignore errors during text-to-speech toggling
            }
        }

        public void HandleWindowSizeChanged(double width, double height)
        {
            try
            {
                if (CurrentBook?.CurrentChapter == null)
                {
                    return;
                }

                int currentPageIndex = CurrentBook.CurrentChapter.CurrentPageIndex;
                _ = LoadChapterContent(CurrentBook.CurrentChapter, currentPageIndex);
            }
            catch (Exception ex)
            {
                // Ignore errors during window size change handling
            }
        }

        private void UpdateBookmarkStatus()
        {
            try
            {
                Logger.Info("[UpdateBookmarkStatus] Starting bookmark status update");
                Debug.WriteLine("[UpdateBookmarkStatus] Starting bookmark status update");
                
                if (CurrentBook == null)
                {
                    IsCurrentPageBookmarked = false;
                    HasBookmarksInCurrentBook = false;
                    BookmarkProximity = 0;
                    Logger.Info("[UpdateBookmarkStatus] No current book, status reset");
                    Debug.WriteLine("[UpdateBookmarkStatus] No current book, status reset");
                    return;
                }

                var currentBookmark = new Bookmark(
                    Path.GetFileName(CurrentBook.FilePath),
                    CurrentBook.CurrentChapterIndex,
                    CurrentBook.CurrentChapter?.CurrentPageIndex ?? 0);

                Logger.Info($"[UpdateBookmarkStatus] Current location - Chapter: {currentBookmark.ChapterIndex}, Page: {currentBookmark.PageIndex}");
                Debug.WriteLine($"[UpdateBookmarkStatus] Current location - Chapter: {currentBookmark.ChapterIndex}, Page: {currentBookmark.PageIndex}");

                // First check if current page is bookmarked
                IsCurrentPageBookmarked = settings.HasBookmark(currentBookmark);
                Logger.Info($"[UpdateBookmarkStatus] IsCurrentPageBookmarked: {IsCurrentPageBookmarked}");
                Debug.WriteLine($"[UpdateBookmarkStatus] IsCurrentPageBookmarked: {IsCurrentPageBookmarked}");

                // Check if there are any bookmarks in the current book
                var bookmarks = settings.GetBookmarksForBook(Path.GetFileName(CurrentBook.FilePath)).ToList();
                HasBookmarksInCurrentBook = bookmarks.Any();
                
                Logger.Info($"[UpdateBookmarkStatus] HasBookmarksInCurrentBook: {HasBookmarksInCurrentBook}");
                Debug.WriteLine($"[UpdateBookmarkStatus] HasBookmarksInCurrentBook: {HasBookmarksInCurrentBook}");
                Logger.Info($"[UpdateBookmarkStatus] Total bookmarks in current book: {bookmarks.Count}");
                Debug.WriteLine($"[UpdateBookmarkStatus] Total bookmarks in current book: {bookmarks.Count}");

                foreach (var bm in bookmarks)
                {
                    Logger.Info($"[UpdateBookmarkStatus] Found bookmark at Chapter {bm.ChapterIndex}, Page {bm.PageIndex}");
                    Debug.WriteLine($"[UpdateBookmarkStatus] Found bookmark at Chapter {bm.ChapterIndex}, Page {bm.PageIndex}");
                }
                
                // Force command state update
                CommandManager.InvalidateRequerySuggested();
                Logger.Info("[UpdateBookmarkStatus] Invalidated command state");
                Debug.WriteLine("[UpdateBookmarkStatus] Invalidated command state");
                
                if (bookmarks.Any())
                {
                    // Find closest bookmark for proximity indicator
                    var closestBookmark = bookmarks
                        .OrderBy(b =>
                        {
                            var chapterDiff = Math.Abs(b.ChapterIndex - currentBookmark.ChapterIndex);
                            var pageDiff = Math.Abs(b.PageIndex - currentBookmark.PageIndex);
                            return (chapterDiff * 1000) + pageDiff;
                        })
                        .First();

                    var chapterDiff = Math.Abs(closestBookmark.ChapterIndex - currentBookmark.ChapterIndex);
                    var pageDiff = Math.Abs(closestBookmark.PageIndex - currentBookmark.PageIndex);
                    
                    Logger.Info($"[UpdateBookmarkStatus] Closest bookmark - Chapter diff: {chapterDiff}, Page diff: {pageDiff}");
                    Debug.WriteLine($"[UpdateBookmarkStatus] Closest bookmark - Chapter diff: {chapterDiff}, Page diff: {pageDiff}");

                    if (chapterDiff == 0)
                    {
                        // Same chapter, proximity based on page difference
                        BookmarkProximity = Math.Max(0, 1.0 - (pageDiff * 0.2));
                        Logger.Info($"[UpdateBookmarkStatus] Same chapter, setting proximity to {BookmarkProximity:F2}");
                        Debug.WriteLine($"[UpdateBookmarkStatus] Same chapter, setting proximity to {BookmarkProximity:F2}");
                    }
                    else
                    {
                        // Different chapter, lower proximity
                        BookmarkProximity = Math.Max(0, 0.5 - (chapterDiff * 0.1));
                        Logger.Info($"[UpdateBookmarkStatus] Different chapter, setting proximity to {BookmarkProximity:F2}");
                        Debug.WriteLine($"[UpdateBookmarkStatus] Different chapter, setting proximity to {BookmarkProximity:F2}");
                    }
                }
                else
                {
                    BookmarkProximity = 0;
                }

                Logger.Info($"[UpdateBookmarkStatus] Final values - IsBookmarked: {IsCurrentPageBookmarked}, HasBookmarks: {HasBookmarksInCurrentBook}, Proximity: {BookmarkProximity:F2}");
                Debug.WriteLine($"[UpdateBookmarkStatus] Final values - IsBookmarked: {IsCurrentPageBookmarked}, HasBookmarks: {HasBookmarksInCurrentBook}, Proximity: {BookmarkProximity:F2}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[UpdateBookmarkStatus] Error updating bookmark status");
                Debug.WriteLine($"[UpdateBookmarkStatus] Error: {ex}");
            }
        }

        private void ToggleBookmark()
        {
            try
            {
                Debug.WriteLine("ToggleBookmark called");
                if (CurrentBook == null || CurrentBook.CurrentChapter == null)
                {
                    Debug.WriteLine("ToggleBookmark: No current book or chapter");
                    return;
                }

                var bookmark = new Bookmark(
                    Path.GetFileName(CurrentBook.FilePath),
                    CurrentBook.CurrentChapterIndex,
                    CurrentBook.CurrentChapter.CurrentPageIndex);

                Debug.WriteLine($"Current bookmark state - IsBookmarked: {IsCurrentPageBookmarked}, HasBookmarks: {HasBookmarksInCurrentBook}");

                if (IsCurrentPageBookmarked)
                {
                    settings.RemoveBookmark(bookmark);
                    Debug.WriteLine("Bookmark removed");
                }
                else
                {
                    settings.AddBookmark(bookmark);
                    Debug.WriteLine("Bookmark added");
                }

                UpdateBookmarkStatus();
                
                // Force UI update
                OnPropertyChanged(nameof(IsCurrentPageBookmarked));
                OnPropertyChanged(nameof(HasBookmarksInCurrentBook));
                
                // Force command state update
                CommandManager.InvalidateRequerySuggested();
                
                Debug.WriteLine($"Updated bookmark state - IsBookmarked: {IsCurrentPageBookmarked}, HasBookmarks: {HasBookmarksInCurrentBook}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ToggleBookmark: {ex}");
            }
        }

        public void NavigateToBookmark()
        {
            try
            {
                Debug.WriteLine("[NavigateToBookmark] Method called");
                Logger.Info("[NavigateToBookmark] Starting bookmark navigation");
                
                if (CurrentBook == null || CurrentBook.CurrentChapter == null)
                {
                    Logger.Warn("[NavigateToBookmark] No current book or chapter selected");
                    return;
                }

                var bookFileName = Path.GetFileName(CurrentBook.FilePath);
                Logger.Info($"[NavigateToBookmark] Current book: {bookFileName}");
                Logger.Info($"[NavigateToBookmark] Current chapter: {CurrentBook.CurrentChapterIndex}");
                Logger.Info($"[NavigateToBookmark] Current page: {CurrentBook.CurrentChapter.CurrentPageIndex}");

                var bookmarks = settings.GetBookmarksForBook(bookFileName).ToList();
                Logger.Info($"[NavigateToBookmark] Found {bookmarks.Count} bookmarks for current book");
                foreach (var bm in bookmarks)
                {
                    Logger.Info($"[NavigateToBookmark] Available bookmark - Chapter: {bm.ChapterIndex}, Page: {bm.PageIndex}");
                }

                if (!bookmarks.Any())
                {
                    Logger.Warn("[NavigateToBookmark] No bookmarks found for current book");
                    return;
                }

                // Find the closest bookmark based on chapter and page distance
                var currentChapter = CurrentBook.CurrentChapterIndex;
                var currentPage = CurrentBook.CurrentChapter.CurrentPageIndex;

                Logger.Info($"[NavigateToBookmark] Finding closest bookmark to Chapter: {currentChapter}, Page: {currentPage}");

                var closestBookmark = bookmarks
                    .OrderBy(b =>
                    {
                        var chapterDiff = Math.Abs(b.ChapterIndex - currentChapter);
                        var pageDiff = Math.Abs(b.PageIndex - currentPage);
                        var score = (chapterDiff * 1000) + pageDiff;
                        Logger.Info($"[NavigateToBookmark] Bookmark at Ch{b.ChapterIndex}P{b.PageIndex} has score: {score}");
                        return score;
                    })
                    .First();

                Logger.Info($"[NavigateToBookmark] Found closest bookmark - Chapter: {closestBookmark.ChapterIndex}, Page: {closestBookmark.PageIndex}");

                // If we're already at this bookmark, find the next one
                if (closestBookmark.ChapterIndex == currentChapter && closestBookmark.PageIndex == currentPage)
                {
                    Logger.Info("[NavigateToBookmark] Already at closest bookmark, finding next one");
                    closestBookmark = bookmarks
                        .Where(b => b.ChapterIndex > currentChapter || (b.ChapterIndex == currentChapter && b.PageIndex > currentPage))
                        .OrderBy(b => b.ChapterIndex)
                        .ThenBy(b => b.PageIndex)
                        .FirstOrDefault()
                        ?? bookmarks.First(); // Wrap around to first bookmark if no next one exists
                    
                    Logger.Info($"[NavigateToBookmark] Selected next bookmark - Chapter: {closestBookmark.ChapterIndex}, Page: {closestBookmark.PageIndex}");
                }

                // Validate chapter index
                if (closestBookmark.ChapterIndex < 0 || closestBookmark.ChapterIndex >= CurrentBook.Chapters.Count)
                {
                    Logger.Error($"[NavigateToBookmark] Invalid chapter index: {closestBookmark.ChapterIndex}");
                    return;
                }

                // Navigate to the bookmark
                if (closestBookmark.ChapterIndex != currentChapter)
                {
                    Logger.Info($"[NavigateToBookmark] Navigating to different chapter: {closestBookmark.ChapterIndex}");
                    CurrentBook.CurrentChapterIndex = closestBookmark.ChapterIndex;
                    _ = LoadChapterContent(CurrentBook.CurrentChapter, closestBookmark.PageIndex)
                        .ContinueWith(t =>
                        {
                            if (t.IsCompleted && !t.IsFaulted)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    Logger.Info("[NavigateToBookmark] Chapter loaded, updating content");
                                    _ = UpdateContent();
                                    UpdateBookmarkStatus();
                                });
                            }
                            else if (t.IsFaulted)
                            {
                                Logger.Error(t.Exception, "[NavigateToBookmark] Failed to load chapter content");
                            }
                        });
                }
                else if (closestBookmark.PageIndex != currentPage)
                {
                    Logger.Info($"[NavigateToBookmark] Navigating to different page in same chapter: {closestBookmark.PageIndex}");
                    CurrentBook.CurrentChapter.CurrentPageIndex = closestBookmark.PageIndex;
                    _ = UpdateContent();
                    UpdateBookmarkStatus();
                }
                else
                {
                    Logger.Warn("[NavigateToBookmark] Already at target bookmark location");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[NavigateToBookmark] Error during bookmark navigation");
            }
        }

        private async Task LoadLastBookAsync()
        {
            try
            {
                var book = new Book
                {
                    FilePath = settings.LastBookPath
                };

                if (Path.GetExtension(settings.LastBookPath).ToLower() == ".txt")
                {
                    await LoadTextBookAsync(book);
                }
                else
                {
                    await LoadBookAsync(book);
                }

                if (settings.LastChapterIndex >= 0 && settings.LastChapterIndex < book.Chapters.Count)
                {
                    book.CurrentChapterIndex = settings.LastChapterIndex;
                    await LoadChapterContent(book.CurrentChapter);
                }

                CurrentBook = book;
                SelectedBook = book;
                UpdateBookProgress();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading last book: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int GetTotalPageCount()
        {
            if (CurrentBook?.Chapters == null)
                return 0;
            
            return CurrentBook.Chapters.Sum(chapter => chapter.Pages.Count);
        }
        
        private int GetCurrentOverallPageNumber()
        {
            if (CurrentBook?.Chapters == null || CurrentBook.CurrentChapter == null)
                return 0;
            
            int pageCount = 0;
            for (int i = 0; i < CurrentBook.CurrentChapterIndex; i++)
            {
                pageCount += CurrentBook.Chapters[i].Pages.Count;
            }
            
            pageCount += CurrentBook.CurrentChapter.CurrentPageIndex + 1;
            return pageCount;
        }

        private void UpdatePageNumber()
        {
            if (CurrentBook?.CurrentChapter == null || CurrentBook.Chapters.Count == 0)
            {
                PageNumberText = string.Empty;
                return;
            }

            int currentPage = GetCurrentOverallPageNumber();
            PageNumberText = $"- {currentPage} -";
        }

        protected virtual async void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        StopSpeaking();
                        tts?.Dispose();

                        settings.LastBookPath = CurrentBook?.FilePath;
                        settings.LastChapterIndex = CurrentBook?.CurrentChapterIndex ?? 0;
                        settings.Theme = currentTheme;
                        settings.FontSize = FontSize;
                        
                        await Task.Delay(100);
                        settings.SaveOnExit();
                    }
                    catch
                    {
                        // Ignore errors during disposal
                    }
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void OpenSettings()
        {
            try
            {
                Debug.WriteLine("Opening Settings window");
                var settingsWindow = new Views.SettingsWindow(settings)
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                
                Debug.WriteLine("Showing Settings window");
                settingsWindow.ShowDialog();
                Debug.WriteLine("Settings window closed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening settings window: {ex}");
                MessageBox.Show($"Error opening settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenPronunciation()
        {
            try
            {
                Debug.WriteLine("Opening Pronunciation window");
                var pronunciationWindow = new Views.PronunciationWindow(
                    settings,
                    () => {
                        Debug.WriteLine("Pronunciation settings changed, updating content");
                        _ = UpdateContent();
                    })
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                
                Debug.WriteLine("Showing Pronunciation window");
                pronunciationWindow.ShowDialog();
                Debug.WriteLine("Pronunciation window closed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening pronunciation window: {ex}");
                MessageBox.Show($"Error opening pronunciation dictionary: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 