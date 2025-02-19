using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Input;
using System.Xml;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using NLog;
using System.Windows.Navigation;
using System.Text;
using HtmlAgilityPack;
using System.Collections.Generic;

namespace KokoroReader.Controls
{
    public class BookContentViewer : FlowDocumentScrollViewer
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        private FlowDocument document;
        private ScrollViewer? scrollViewer;
        private bool isDragging;
        private Point lastMousePosition;

        public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
            nameof(Content), typeof(string), typeof(BookContentViewer),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnContentChanged));

        public static readonly DependencyProperty FontFamilyNameProperty = DependencyProperty.Register(
            nameof(FontFamilyName), typeof(string), typeof(BookContentViewer),
            new FrameworkPropertyMetadata("Segoe UI", FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

        public static readonly DependencyProperty TextSizeProperty = DependencyProperty.Register(
            nameof(TextSize), typeof(double), typeof(BookContentViewer),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

        public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
            nameof(LineHeight), typeof(double), typeof(BookContentViewer),
            new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

        public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
            nameof(TextAlignment), typeof(TextAlignment), typeof(BookContentViewer),
            new FrameworkPropertyMetadata(TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

        public string Content
        {
            get => (string)GetValue(ContentProperty);
            set => SetValue(ContentProperty, value);
        }

        public string FontFamilyName
        {
            get => (string)GetValue(FontFamilyNameProperty);
            set => SetValue(FontFamilyNameProperty, value);
        }

        public double TextSize
        {
            get => (double)GetValue(TextSizeProperty);
            set => SetValue(TextSizeProperty, value);
        }

        public double LineHeight
        {
            get => (double)GetValue(LineHeightProperty);
            set => SetValue(LineHeightProperty, value);
        }

        public TextAlignment TextAlignment
        {
            get => (TextAlignment)GetValue(TextAlignmentProperty);
            set => SetValue(TextAlignmentProperty, value);
        }

        public event EventHandler<double> ContentOverflow;

        public BookContentViewer()
        {
            document = new FlowDocument();
            Document = document;
            
            // Configure viewer settings
            IsToolBarVisible = false;
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Zoom = 100;
            
            // Configure document defaults for better text flow
            document.PagePadding = new Thickness(0);
            document.ColumnWidth = 1000;
            document.PageWidth = 1000;
            document.LineHeight = 1.2;
            document.TextAlignment = TextAlignment.Left;
            document.IsOptimalParagraphEnabled = true;
            document.IsHyphenationEnabled = true;
            document.FlowDirection = FlowDirection.LeftToRight;
            document.IsColumnWidthFlexible = false;
            document.IsEnabled = true;
            document.Background = Brushes.Transparent;
            
            // Disable text selection and set cursor
            IsSelectionEnabled = false;
            SelectionBrush = Brushes.Transparent;
            Cursor = Cursors.Hand;
            Focusable = false;
            FocusVisualStyle = null;
            
            // Optimize mouse event handling
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseRightButtonDown += OnMouseRightButtonDown;
            
            // Enable smooth scrolling
            ScrollViewer.SetCanContentScroll(this, false);
            
            // Optimize document rendering
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            CacheMode = new BitmapCache { 
                EnableClearType = true, 
                SnapsToDevicePixels = true,
                RenderAtScale = 1.0 // Prevent blurry text during dragging
            };

            // Prevent flicker during dragging
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isDragging) return;

            var window = Window.GetWindow(this);
            if (window != null && e.GetPosition(window).Y <= window.ActualHeight - 100) // Don't drag from bottom area
            {
                isDragging = true;
                try
                {
                    window.DragMove();
                }
                finally
                {
                    isDragging = false;
                }
                e.Handled = true;
            }
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window?.ContextMenu != null)
            {
                // Get the mouse position relative to the screen
                var screenPosition = PointToScreen(e.GetPosition(this));
                
                // Convert screen position back to window coordinates
                var windowPosition = window.PointFromScreen(screenPosition);

                // Calculate available space in each direction
                double rightSpace = window.ActualWidth - windowPosition.X;
                double bottomSpace = window.ActualHeight - windowPosition.Y;

                // Get the context menu's dimensions (after temporarily showing it off-screen)
                window.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
                window.ContextMenu.HorizontalOffset = -10000; // Off-screen
                window.ContextMenu.VerticalOffset = -10000;   // Off-screen
                window.ContextMenu.IsOpen = true;
                var menuWidth = window.ContextMenu.ActualWidth;
                var menuHeight = window.ContextMenu.ActualHeight;
                window.ContextMenu.IsOpen = false;

                // Adjust position if menu would go outside window bounds
                if (rightSpace < menuWidth)
                {
                    windowPosition.X = Math.Max(0, window.ActualWidth - menuWidth);
                }
                if (bottomSpace < menuHeight)
                {
                    windowPosition.Y = Math.Max(0, window.ActualHeight - menuHeight);
                }

                // Set the final context menu position
                window.ContextMenu.PlacementTarget = window;
                window.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                window.ContextMenu.HorizontalOffset = windowPosition.X;
                window.ContextMenu.VerticalOffset = windowPosition.Y;
                
                window.ContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged -= OnScrollChanged;
                scrollViewer.MouseLeftButtonDown -= OnMouseLeftButtonDown;
                scrollViewer.MouseRightButtonDown -= OnMouseRightButtonDown;
            }

            scrollViewer = Template.FindName("PART_ContentHost", this) as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += OnScrollChanged;
                scrollViewer.MouseLeftButtonDown += OnMouseLeftButtonDown;
                scrollViewer.MouseRightButtonDown += OnMouseRightButtonDown;
                
                // Configure ScrollViewer for better performance
                scrollViewer.CanContentScroll = false;
                scrollViewer.PanningMode = PanningMode.VerticalOnly;
                scrollViewer.PanningRatio = 0.1;
                scrollViewer.PanningDeceleration = 0.001;
                scrollViewer.Cursor = Cursors.Hand;
                scrollViewer.Focusable = false;
                scrollViewer.FocusVisualStyle = null;
                
                // Enable hardware acceleration
                scrollViewer.CacheMode = new BitmapCache { 
                    EnableClearType = true, 
                    SnapsToDevicePixels = true,
                    RenderAtScale = 1.0
                };
                
                // Prevent flicker during dragging
                RenderOptions.SetBitmapScalingMode(scrollViewer, BitmapScalingMode.HighQuality);
                RenderOptions.SetEdgeMode(scrollViewer, EdgeMode.Aliased);
                
                Logger.Info("Successfully configured ScrollViewer");
            }
            else
            {
                Logger.Warn("Could not find ScrollViewer in template");
            }
        }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BookContentViewer viewer)
            {
                viewer.UpdateContent();
            }
        }

        private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BookContentViewer viewer)
            {
                viewer.UpdateFontSettings();
            }
        }

        public string GetCleanTextContent()
        {
            try
            {
                var sb = new StringBuilder();
                
                // Process each block in the document
                foreach (var block in document.Blocks)
                {
                    // Handle different types of blocks
                    switch (block)
                    {
                        case Paragraph paragraph:
                            // Get text from paragraph
                            sb.AppendLine(new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim());
                            break;
                        case Section section:
                            // Process section blocks recursively
                            foreach (var sectionBlock in section.Blocks)
                            {
                                if (sectionBlock is Paragraph sectionParagraph)
                                {
                                    sb.AppendLine(new TextRange(sectionParagraph.ContentStart, sectionParagraph.ContentEnd).Text.Trim());
                                }
                            }
                            break;
                    }
                    
                    // Add extra newline between blocks for better speech separation
                    sb.AppendLine();
                }
                
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting clean text content");
                return string.Empty;
            }
        }

        private void UpdateContent()
        {
            try
            {
                // Clear all existing content
                document.Blocks.Clear();

                if (string.IsNullOrEmpty(Content))
                    return;

                // Log the raw content
                Logger.Info($"[UpdateContent] Raw content: {Content}");

                // First attempt: Try direct text node processing
                if (!Content.Contains("<") && !Content.Contains(">"))
                {
                    var paragraph = new Paragraph(new Run(Content.Trim()))
                    {
                        TextAlignment = TextAlignment,
                        LineHeight = TextSize * LineHeight,
                        Margin = new Thickness(0, 0, 0, TextSize * 0.5)
                    };
                    document.Blocks.Add(paragraph);
                    return;
                }

                // Second attempt: Clean and process as HTML
                string cleanedContent = CleanHtmlContent(Content);
                Logger.Info($"[UpdateContent] Cleaned content: {cleanedContent}");

                // Create HTML document
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(cleanedContent);

                // Get the body node, or use the document node if no body is found
                var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//body") ?? htmlDoc.DocumentNode;
                Logger.Info($"[UpdateContent] Body node HTML: {bodyNode.OuterHtml}");

                // Process each block element
                var blockNodes = bodyNode.ChildNodes.Where(n => 
                    n.NodeType == HtmlNodeType.Element || 
                    (n.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(n.InnerText.Trim()))).ToList();

                if (blockNodes.Count == 0)
                {
                    var text = bodyNode.InnerText.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        var paragraph = new Paragraph(new Run(text))
                        {
                            TextAlignment = TextAlignment,
                            LineHeight = TextSize * LineHeight,
                            Margin = new Thickness(0, 0, 0, TextSize * 0.5)
                        };
                        document.Blocks.Add(paragraph);
                        return;
                    }
                }

                foreach (var node in blockNodes)
                {
                    Block? block = null;

                    if (node.NodeType == HtmlNodeType.Text)
                    {
                        var text = node.InnerText.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            block = new Paragraph(new Run(text))
                            {
                                TextAlignment = TextAlignment,
                                LineHeight = TextSize * LineHeight,
                                Margin = new Thickness(0, 0, 0, TextSize * 0.5)
                            };
                        }
                    }
                    else
                    {
                        switch (node.Name.ToLower())
                        {
                            case "p":
                                block = CreateParagraph(node);
                                break;
                            case "h1":
                            case "h2":
                            case "h3":
                            case "h4":
                            case "h5":
                            case "h6":
                                block = CreateHeading(node);
                                break;
                            case "ul":
                            case "ol":
                                block = CreateList(node);
                                break;
                            case "div":
                                block = CreateSection(node);
                                break;
                            case "img":
                                block = CreateImage(node);
                                break;
                            default:
                                if (!string.IsNullOrWhiteSpace(node.InnerText))
                                {
                                    block = CreateParagraph(node);
                                }
                                break;
                        }
                    }

                    if (block != null)
                    {
                        document.Blocks.Add(block);
                    }
                }

                // If no blocks were added, try fallback processing
                if (document.Blocks.Count == 0)
                {
                    FallbackTextProcessing();
                }

                UpdateFontSettings();

                // Optimize document rendering after content update
                document.PageWidth = ActualWidth > 0 ? ActualWidth : 1000;
                document.ColumnWidth = document.PageWidth;
                IsSelectionEnabled = false;
                document.Focusable = false;

                // Force layout update
                UpdateLayout();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating content");
                FallbackTextProcessing();
            }
        }

        private string CleanHtmlContent(string content)
        {
            try
            {
                // First pass: Basic HTML cleanup
                var cleaned = content;

                // Remove script and style tags with their content
                cleaned = Regex.Replace(cleaned, @"<script[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                cleaned = Regex.Replace(cleaned, @"<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                // Remove comments
                cleaned = Regex.Replace(cleaned, @"<!--.*?-->", "", RegexOptions.Singleline);
                
                // Remove potentially dangerous attributes
                cleaned = Regex.Replace(cleaned, @"(javascript|onerror|onload|onclick|onmouseover):[^""']*[""']", "", RegexOptions.IgnoreCase);

                // Handle HTML entities and special characters
                cleaned = System.Net.WebUtility.HtmlDecode(cleaned);

                // Clean up whitespace while preserving structure
                cleaned = Regex.Replace(cleaned, @"[\r\n]+", "\n");  // Normalize line endings
                cleaned = Regex.Replace(cleaned, @"(?<!\n)\s+", " "); // Collapse multiple spaces
                cleaned = Regex.Replace(cleaned, @"\n\s*\n", "\n\n"); // Normalize paragraph breaks
                cleaned = cleaned.Trim();

                // Ensure proper list structure is preserved
                if (!cleaned.Contains("<p>") && !cleaned.Contains("<div>") && !cleaned.Contains("<h") && !cleaned.Contains("<ul") && !cleaned.Contains("<ol"))
                {
                    var lines = cleaned.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(l => l.Trim())
                                     .Where(l => !string.IsNullOrWhiteSpace(l));
                    
                    cleaned = string.Join("\n", lines.Select(l => $"<p>{l}</p>"));
                }
                
                return cleaned;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error cleaning HTML content");
                return content;
            }
        }

        private void FallbackTextProcessing()
        {
            try
            {
                Logger.Info("[FallbackTextProcessing] Starting fallback processing");
                document.Blocks.Clear();

                // First try to clean any HTML tags
                var cleanText = Regex.Replace(Content, @"<[^>]+>", string.Empty);
                cleanText = System.Net.WebUtility.HtmlDecode(cleanText);
                Logger.Info($"[FallbackTextProcessing] Cleaned text length: {cleanText.Length}");
                Logger.Debug($"[FallbackTextProcessing] Cleaned text preview: {cleanText.Substring(0, Math.Min(200, cleanText.Length))}");

                // Split content into paragraphs
                var paragraphs = Regex.Split(cleanText, @"(?:\r?\n){2,}|\u2028{2,}|\f")
                                    .Where(p => !string.IsNullOrWhiteSpace(p))
                                    .Select(p => p.Trim())
                                    .ToList();

                Logger.Info($"[FallbackTextProcessing] Found {paragraphs.Count} paragraphs");

                if (!paragraphs.Any())
                {
                    Logger.Warn("[FallbackTextProcessing] No paragraphs found, using entire content as one paragraph");
                    paragraphs = new List<string> { cleanText.Trim() };
                }

                foreach (var text in paragraphs)
                {
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    Logger.Debug($"[FallbackTextProcessing] Creating paragraph with text: {text.Substring(0, Math.Min(50, text.Length))}");
                    var paragraph = new Paragraph(new Run(text))
                    {
                        TextAlignment = TextAlignment,
                        LineHeight = TextSize * 1.2,
                        Margin = new Thickness(0, TextSize * 0.3, 0, TextSize * 0.3)
                    };
                    document.Blocks.Add(paragraph);
                }

                Logger.Info($"[FallbackTextProcessing] Created {document.Blocks.Count} paragraphs");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[FallbackTextProcessing] Error in fallback processing");
                
                // Last resort: just show the raw content as a single paragraph
                Logger.Info("[FallbackTextProcessing] Using last resort: raw content as single paragraph");
                var paragraph = new Paragraph(new Run(Content))
                {
                    TextAlignment = TextAlignment,
                    LineHeight = TextSize * 1.2,
                    Margin = new Thickness(0, TextSize * 0.3, 0, TextSize * 0.3)
                };
                document.Blocks.Add(paragraph);
            }
        }

        private Paragraph CreateParagraph(HtmlNode node)
        {
            try
            {
                Logger.Debug($"[CreateParagraph] Creating paragraph from node: {node.Name}, Text preview: {node.InnerText.Substring(0, Math.Min(50, node.InnerText.Length))}");
                
                var paragraph = new Paragraph()
                {
                    TextAlignment = TextAlignment,
                    LineHeight = TextSize * 1.2, // Reduced from previous value
                    Margin = new Thickness(0, TextSize * 0.3, 0, TextSize * 0.3) // Reduced margins
                };

                ProcessInlineContent(node, paragraph.Inlines);
                
                // Verify paragraph content
                var content = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                Logger.Debug($"[CreateParagraph] Created paragraph with content length: {content.Length}, Preview: {content.Substring(0, Math.Min(50, content.Length))}");
                
                return paragraph;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[CreateParagraph] Error creating paragraph");
                // Create a simple paragraph with the text content as fallback
                return new Paragraph(new Run(node.InnerText))
                {
                    TextAlignment = TextAlignment,
                    LineHeight = TextSize * 1.2,
                    Margin = new Thickness(0, TextSize * 0.3, 0, TextSize * 0.3)
                };
            }
        }

        private Section CreateSection(HtmlNode node)
        {
            try
            {
                Logger.Debug($"[CreateSection] Creating section from node: {node.Name}, Child nodes: {node.ChildNodes.Count}");
                var section = new Section();
                var blockCount = 0;
                
                foreach (var childNode in node.ChildNodes)
                {
                    if (childNode.NodeType == HtmlNodeType.Element)
                    {
                        Block? block = null;
                        Logger.Debug($"[CreateSection] Processing child node: {childNode.Name}, Text preview: {childNode.InnerText.Substring(0, Math.Min(50, childNode.InnerText.Length))}");
                        
                        switch (childNode.Name.ToLower())
                        {
                            case "p":
                                block = CreateParagraph(childNode);
                                break;
                            case "h1":
                            case "h2":
                            case "h3":
                            case "h4":
                            case "h5":
                            case "h6":
                                block = CreateHeading(childNode);
                                break;
                            case "div":
                                block = CreateSection(childNode);
                                break;
                            case "img":
                                block = CreateImage(childNode);
                                break;
                            default:
                                if (!string.IsNullOrWhiteSpace(childNode.InnerText))
                                {
                                    Logger.Debug($"[CreateSection] Creating paragraph for unknown element: {childNode.Name}");
                                    block = CreateParagraph(childNode);
                                }
                                break;
                        }
                        
                        if (block != null)
                        {
                            section.Blocks.Add(block);
                            blockCount++;
                            if (block is Paragraph para)
                            {
                                var content = new TextRange(para.ContentStart, para.ContentEnd).Text;
                                Logger.Debug($"[CreateSection] Added block {blockCount}, Content preview: {content.Substring(0, Math.Min(50, content.Length))}");
                            }
                        }
                    }
                    else if (childNode.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(childNode.InnerText.Trim()))
                    {
                        // Handle direct text nodes in sections
                        var text = childNode.InnerText.Trim();
                        Logger.Debug($"[CreateSection] Creating paragraph for direct text, Preview: {text.Substring(0, Math.Min(50, text.Length))}");
                        var paragraph = new Paragraph(new Run(text))
                        {
                            TextAlignment = TextAlignment,
                            LineHeight = TextSize * LineHeight,
                            Margin = new Thickness(0, 0, 0, TextSize * 0.5)
                        };
                        section.Blocks.Add(paragraph);
                        blockCount++;
                    }
                }

                Logger.Info($"[CreateSection] Created section with {blockCount} blocks");
                return section;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[CreateSection] Error creating section");
                // Create a simple section with the text content as fallback
                var section = new Section();
                section.Blocks.Add(new Paragraph(new Run(node.InnerText))
                {
                    TextAlignment = TextAlignment,
                    LineHeight = TextSize * LineHeight
                });
                return section;
            }
        }

        private BlockUIContainer CreateImage(HtmlNode node)
        {
            var image = new Image
            {
                MaxHeight = 800,
                MaxWidth = 600,
                Stretch = Stretch.Uniform
            };

            var src = node.GetAttributeValue("src", "");
            if (!string.IsNullOrEmpty(src))
            {
                try
                {
                    var uri = new Uri(src, UriKind.RelativeOrAbsolute);
                    image.Source = new BitmapImage(uri);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error loading image from {src}");
                }
            }

            return new BlockUIContainer(image);
        }

        private Paragraph CreateHeading(HtmlNode node)
        {
            var heading = new Paragraph()
            {
                TextAlignment = TextAlignment.Left,
                LineHeight = TextSize * 1.4, // Increased line height for headings
                FontWeight = FontWeights.Bold,
                FontSize = TextSize * GetHeadingScale(node.Name),
                // Add proper spacing around headings
                Margin = new Thickness(0, TextSize * 1.2, 0, TextSize * 0.6)
            };

            ProcessInlineContent(node, heading.Inlines);
            return heading;
        }

        private double GetHeadingScale(string headingTag)
        {
            return headingTag.ToLower() switch
            {
                "h1" => 2.0,
                "h2" => 1.5,  // Adjusted for better hierarchy
                "h3" => 1.3,  // Adjusted for better hierarchy
                "h4" => 1.2,
                "h5" => 1.1,
                "h6" => 1.0,
                _ => 1.0
            };
        }

        private Block CreateList(HtmlNode node)
        {
            try
            {
                Logger.Info($"[CreateList] ==================== START LIST CREATION ====================");
                Logger.Info($"[CreateList] Node type: {node.Name}, NodeType: {node.NodeType}");
                
                var section = new Section();
                var isOrdered = node.Name.ToLower() == "ol";
                var listMargin = new Thickness(TextSize * 2, TextSize * 0.3, 0, TextSize * 0.3);
                var itemCount = 1;

                foreach (var childNode in node.ChildNodes)
                {
                    if (childNode.Name.ToLower() == "li")
                    {
                        // Create paragraph for list item with proper styling
                        var item = new Paragraph
                        {
                            Margin = listMargin,
                            TextAlignment = TextAlignment,
                            LineHeight = TextSize * 1.4,
                            FontSize = TextSize,
                            FontFamily = new FontFamily(FontFamilyName)
                        };

                        // Add bullet or number with proper styling
                        var marker = new Run(isOrdered ? $"{itemCount}. " : "• ")
                        {
                            FontWeight = FontWeights.Normal,
                            FontSize = TextSize,
                            FontFamily = new FontFamily(FontFamilyName),
                            Foreground = (Brush)FindResource("TextBrush")
                        };
                        item.Inlines.Add(marker);

                        // Process the content with special handling for nested elements
                        if (childNode.HasChildNodes)
                        {
                            foreach (var contentNode in childNode.ChildNodes)
                            {
                                if (contentNode.NodeType == HtmlNodeType.Text)
                                {
                                    var text = contentNode.InnerText.Trim();
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        var run = new Run(text)
                                        {
                                            FontSize = TextSize,
                                            FontFamily = new FontFamily(FontFamilyName),
                                            Foreground = (Brush)FindResource("TextBrush")
                                        };
                                        item.Inlines.Add(run);
                                    }
                                }
                                else if (contentNode.NodeType == HtmlNodeType.Element)
                                {
                                    var inline = CreateInline(contentNode);
                                    if (inline != null)
                                    {
                                        // Ensure the inline element has proper styling
                                        if (inline is Span span)
                                        {
                                            span.Foreground = (Brush)FindResource("TextBrush");
                                            if (contentNode.GetAttributeValue("class", "") == "command")
                                            {
                                                span.FontFamily = new FontFamily("Consolas");
                                            }
                                            else if (contentNode.GetAttributeValue("class", "") == "highlight")
                                            {
                                                span.FontWeight = FontWeights.Bold;
                                                span.Foreground = (Brush)FindResource("AccentBrush");
                                            }
                                        }
                                        item.Inlines.Add(inline);
                                    }
                                }
                            }
                        }
                        else
                        {
                            var content = childNode.InnerText.Trim();
                            if (!string.IsNullOrEmpty(content))
                            {
                                var run = new Run(content)
                                {
                                    FontSize = TextSize,
                                    FontFamily = new FontFamily(FontFamilyName),
                                    Foreground = (Brush)FindResource("TextBrush")
                                };
                                item.Inlines.Add(run);
                            }
                        }

                        // Set the foreground color for the entire paragraph
                        item.Foreground = (Brush)FindResource("TextBrush");
                        section.Blocks.Add(item);
                        itemCount++;
                    }
                }

                if (itemCount == 1)
                {
                    return new Paragraph(new Run(node.InnerText))
                    {
                        TextAlignment = TextAlignment,
                        LineHeight = TextSize * 1.4,
                        Margin = new Thickness(0, TextSize * 0.3, 0, TextSize * 0.3),
                        FontSize = TextSize,
                        FontFamily = new FontFamily(FontFamilyName),
                        Foreground = (Brush)FindResource("TextBrush")
                    };
                }

                return section;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[CreateList] Error creating list");
                return new Paragraph(new Run(node.InnerText))
                {
                    TextAlignment = TextAlignment,
                    LineHeight = TextSize * 1.4,
                    Margin = new Thickness(0, TextSize * 0.3, 0, TextSize * 0.3),
                    FontSize = TextSize,
                    FontFamily = new FontFamily(FontFamilyName),
                    Foreground = (Brush)FindResource("TextBrush")
                };
            }
        }

        private void ProcessInlineContent(HtmlNode node, InlineCollection inlines)
        {
            try
            {
                if (!node.HasChildNodes)
                {
                    var directText = node.InnerText;
                    if (!string.IsNullOrEmpty(directText))
                    {
                        directText = Regex.Replace(directText, @"\s+", " ");
                        var run = new Run(directText)
                        {
                            FontSize = TextSize,
                            FontFamily = new FontFamily(FontFamilyName)
                        };
                        inlines.Add(run);
                    }
                    return;
                }

                bool lastWasText = false;
                foreach (var childNode in node.ChildNodes)
                {
                    if (childNode.NodeType == HtmlNodeType.Text)
                    {
                        var text = childNode.InnerText;
                        if (!string.IsNullOrEmpty(text))
                        {
                            text = Regex.Replace(text, @"\s{2,}", " ");
                            
                            if (!lastWasText)
                            {
                                text = text.TrimStart();
                            }
                            
                            if (childNode.NextSibling == null)
                            {
                                text = text.TrimEnd();
                            }

                            if (!string.IsNullOrEmpty(text))
                            {
                                var run = new Run(text)
                                {
                                    FontSize = TextSize,
                                    FontFamily = new FontFamily(FontFamilyName)
                                };
                                inlines.Add(run);
                                lastWasText = true;
                            }
                        }
                    }
                    else if (childNode.NodeType == HtmlNodeType.Element)
                    {
                        var inline = CreateInline(childNode);
                        if (inline != null)
                        {
                            inlines.Add(inline);
                            lastWasText = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error processing inline content");
                if (!string.IsNullOrWhiteSpace(node.InnerText))
                {
                    var text = node.InnerText;
                    text = Regex.Replace(text, @"\s+", " ").Trim();
                    var run = new Run(text)
                    {
                        FontSize = TextSize,
                        FontFamily = new FontFamily(FontFamilyName)
                    };
                    inlines.Add(run);
                }
            }
        }

        private Inline CreateInline(HtmlNode node)
        {
            try
            {
                switch (node.Name.ToLower())
                {
                    case "i":
                    case "em":
                        var italic = new Italic();
                        ProcessInlineContent(node, italic.Inlines);
                        
                        // Create a container for proper spacing
                        var italicContainer = new Span();
                        
                        // Add the italic text
                        italicContainer.Inlines.Add(italic);
                        
                        // Add space after if needed
                        if (node.NextSibling != null)
                        {
                            var nextText = node.NextSibling.InnerText;
                            if (!string.IsNullOrEmpty(nextText))
                            {
                                // Always add a space after italic text if there's more text following,
                                // unless it's followed by punctuation
                                var trimmedText = nextText.TrimStart();
                                if (!string.IsNullOrEmpty(trimmedText) && !char.IsPunctuation(trimmedText[0]))
                                {
                                    var space = new Run(" ")
                                    {
                                        FontSize = TextSize,
                                        FontFamily = new FontFamily(FontFamilyName)
                                    };
                                    italicContainer.Inlines.Add(space);
                                }
                            }
                        }
                        
                        return italicContainer.Inlines.Count > 1 ? italicContainer : italic;

                    case "strong":
                    case "b":
                        var bold = new Bold();
                        ProcessInlineContent(node, bold.Inlines);
                        return bold;

                    case "u":
                        var underline = new Underline();
                        ProcessInlineContent(node, underline.Inlines);
                        return underline;

                    case "br":
                        return new LineBreak();

                    case "#text":
                        var text = node.InnerText.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            return new Run(text);
                        }
                        return null;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error creating inline");
                return null;
            }
        }

        private void UpdateFontSettings()
        {
            try
            {
                Logger.Info($"Updating document settings - Font: {FontFamilyName}, Size: {TextSize}, Alignment: {TextAlignment}");
                
                // Update document-level settings
                document.FontFamily = new FontFamily(FontFamilyName);
                document.FontSize = TextSize;
                document.LineHeight = TextSize * 1.2;
                document.TextAlignment = TextAlignment;
                
                // Update all text elements recursively
                foreach (var block in document.Blocks.ToList())
                {
                    UpdateBlockFontSettings(block);
                }
                
                // Force layout update
                document.PageWidth = ActualWidth > 0 ? ActualWidth : 1000;
                document.ColumnWidth = document.PageWidth;
                IsSelectionEnabled = false;
                document.Focusable = false;
                
                // Force a complete refresh
                InvalidateVisual();
                UpdateLayout();
                
                // Ensure scroll position is maintained
                if (scrollViewer != null)
                {
                    var currentOffset = scrollViewer.VerticalOffset;
                    scrollViewer.ScrollToVerticalOffset(currentOffset);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating document settings");
            }
        }

        private void UpdateBlockFontSettings(Block block)
        {
            if (block is Paragraph para)
            {
                para.FontFamily = new FontFamily(FontFamilyName);
                para.FontSize = TextSize;
                para.LineHeight = TextSize * 1.2;
                para.Margin = new Thickness(0, TextSize * 0.3, 0, TextSize * 0.3);
                para.TextAlignment = TextAlignment;  // Ensure text alignment is applied to each paragraph
                
                // Update all inline elements
                foreach (var inline in para.Inlines.ToList())
                {
                    UpdateInlineFontSettings(inline);
                }
            }
            else if (block is Section section)
            {
                foreach (var childBlock in section.Blocks.ToList())
                {
                    UpdateBlockFontSettings(childBlock);
                }
            }
        }

        private void UpdateInlineFontSettings(Inline inline)
        {
            inline.FontFamily = new FontFamily(FontFamilyName);
            inline.FontSize = TextSize;
            
            if (inline is Span span)
            {
                foreach (var childInline in span.Inlines.ToList())
                {
                    UpdateInlineFontSettings(childInline);
                }
            }
            else if (inline is Bold bold)
            {
                foreach (var childInline in bold.Inlines.ToList())
                {
                    UpdateInlineFontSettings(childInline);
                }
            }
            else if (inline is Italic italic)
            {
                foreach (var childInline in italic.Inlines.ToList())
                {
                    UpdateInlineFontSettings(childInline);
                }
            }
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeight > 0)
            {
                var remainingHeight = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight);
                ContentOverflow?.Invoke(this, remainingHeight);
            }
        }

        public void ScrollToNextPage()
        {
            if (scrollViewer != null)
            {
                var targetOffset = Math.Min(scrollViewer.ScrollableHeight, 
                    scrollViewer.VerticalOffset + scrollViewer.ViewportHeight * 0.9);
                scrollViewer.ScrollToVerticalOffset(targetOffset);
            }
        }

        public void ScrollToPreviousPage()
        {
            if (scrollViewer != null)
            {
                var targetOffset = Math.Max(0, 
                    scrollViewer.VerticalOffset - scrollViewer.ViewportHeight * 0.9);
                scrollViewer.ScrollToVerticalOffset(targetOffset);
            }
        }

        public double GetScrollProgress()
        {
            if (scrollViewer != null && scrollViewer.ScrollableHeight > 0)
            {
                return (scrollViewer.VerticalOffset / scrollViewer.ScrollableHeight) * 100;
            }
            return 0;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            
            // Update document width when control size changes
            if (sizeInfo.NewSize.Width > 0)
            {
                document.PageWidth = sizeInfo.NewSize.Width;
                document.ColumnWidth = sizeInfo.NewSize.Width;
            }
        }
    }
} 