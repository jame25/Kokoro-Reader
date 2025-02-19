using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using System.Globalization;
using KokoroReader.Models;

namespace KokoroReader.Controls
{
    public class BookContentCanvas : Control
    {
        private readonly DispatcherTimer renderTimer;
        private List<TextLine> formattedLines;
        private bool needsUpdate;

        public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
            nameof(Content), typeof(string), typeof(BookContentCanvas),
            new FrameworkPropertyMetadata(string.Empty, 
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnContentChanged));

        public static readonly DependencyProperty FontFamilyNameProperty = DependencyProperty.Register(
            nameof(FontFamilyName), typeof(string), typeof(BookContentCanvas),
            new FrameworkPropertyMetadata("Segoe UI", 
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnFontPropertyChanged));

        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(BookContentCanvas),
            new FrameworkPropertyMetadata(16.0, 
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnFontPropertyChanged));

        public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
            nameof(TextAlignment), typeof(System.Windows.TextAlignment), typeof(BookContentCanvas),
            new FrameworkPropertyMetadata(System.Windows.TextAlignment.Left, 
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnTextPropertyChanged));

        public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
            nameof(LineHeight), typeof(double), typeof(BookContentCanvas),
            new FrameworkPropertyMetadata(1.6, 
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnTextPropertyChanged));

        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Brush), typeof(BookContentCanvas),
            new FrameworkPropertyMetadata(Brushes.Black, 
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnTextPropertyChanged));

        public static readonly DependencyProperty IsManualNavigationInProgressProperty = DependencyProperty.Register(
            nameof(IsManualNavigationInProgress), typeof(bool), typeof(BookContentCanvas),
            new FrameworkPropertyMetadata(false));

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

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public System.Windows.TextAlignment TextAlignment
        {
            get => (System.Windows.TextAlignment)GetValue(TextAlignmentProperty);
            set => SetValue(TextAlignmentProperty, value);
        }

        public double LineHeight
        {
            get => (double)GetValue(LineHeightProperty);
            set => SetValue(LineHeightProperty, value);
        }

        public new Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public bool IsManualNavigationInProgress
        {
            get => (bool)GetValue(IsManualNavigationInProgressProperty);
            set
            {
                System.Diagnostics.Debug.WriteLine($"[BookContentCanvas] Setting IsManualNavigationInProgress to {value}");
                SetValue(IsManualNavigationInProgressProperty, value);
                // Force immediate layout update when manual navigation changes
                if (value)
                {
                    System.Diagnostics.Debug.WriteLine("[BookContentCanvas] Manual navigation started - suppressing overflow checks");
                    needsUpdate = false; // Prevent automatic updates
                }
            }
        }

        public event EventHandler<double>? ContentOverflow;

        private double lastMeasuredHeight;
        private int lastVisibleLineIndex;

        public BookContentCanvas()
        {
            formattedLines = new List<TextLine>();
            renderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(8)
            };
            renderTimer.Tick += (s, e) =>
            {
                if (needsUpdate)
                {
                    InvalidateVisual();
                    needsUpdate = false;
                }
            };
            renderTimer.Start();

            // Prevent text selection and focus
            Focusable = false;
            IsHitTestVisible = true;
            IsTabStop = false;
            Background = Brushes.Transparent;
        }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BookContentCanvas canvas)
            {
                System.Diagnostics.Debug.WriteLine($"[BookContentCanvas] Content changed - IsManualNav: {canvas.IsManualNavigationInProgress}, Old: {e.OldValue?.ToString()?.Length ?? 0} chars, New: {e.NewValue?.ToString()?.Length ?? 0} chars");
                
                if (canvas.IsManualNavigationInProgress)
                {
                    System.Diagnostics.Debug.WriteLine("[BookContentCanvas] Deferring content update due to manual navigation");
                    canvas.needsUpdate = false;
                    return;
                }

                canvas.InvalidateMeasure();
                canvas.InvalidateArrange();
                canvas.UpdateLayout();
                canvas.RecreateFormattedParagraphs();
                canvas.needsUpdate = true;
            }
        }

        private static void OnFontPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BookContentCanvas canvas)
            {
                canvas.InvalidateMeasure();
                canvas.InvalidateArrange();
                canvas.UpdateLayout();
                canvas.RecreateFormattedParagraphs();
                canvas.needsUpdate = true;
            }
        }

        private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BookContentCanvas canvas)
            {
                canvas.InvalidateMeasure();
                canvas.InvalidateArrange();
                canvas.UpdateLayout();
                canvas.RecreateFormattedParagraphs();
                canvas.needsUpdate = true;
            }
        }

        private void RecreateFormattedParagraphs()
        {
            System.Diagnostics.Debug.WriteLine($"[BookContentCanvas] RecreateFormattedParagraphs START");
            
            if (string.IsNullOrEmpty(Content))
            {
                formattedLines = new List<TextLine>();
                return;
            }

            var paragraphs = Content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var textFormatter = TextFormatter.Create();
            var typeface = new Typeface(new FontFamily(FontFamilyName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var brush = Foreground ?? Brushes.Black;
            formattedLines = new List<TextLine>();

            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph)) continue;

                var textRunProperties = new ContentTextRunProperties(typeface, FontSize, brush);
                var paragraphProperties = new ContentTextParagraphProperties(
                    textRunProperties,
                    FontSize * LineHeight,
                    TextAlignment,
                    TextWrapping.Wrap,
                    FlowDirection.LeftToRight);

                var textSource = new ContentTextSource(paragraph.Trim(), typeface, FontSize, brush);
                var textLength = paragraph.Length;
                var textOffset = 0;

                while (textOffset < textLength)
                {
                    var textLine = textFormatter.FormatLine(
                        textSource,
                        textOffset,
                        Math.Max(1, ActualWidth),
                        paragraphProperties,
                        null);

                    formattedLines.Add(textLine);
                    textOffset += textLine.Length;
                }

                // Add paragraph spacing
                if (formattedLines.Count > 0)
                {
                    formattedLines.Add(textFormatter.FormatLine(
                        new ContentTextSource(" ", typeface, FontSize * 0.5, brush),
                        0,
                        Math.Max(1, ActualWidth),
                        paragraphProperties,
                        null));
                }
            }

            System.Diagnostics.Debug.WriteLine($"[BookContentCanvas] RecreateFormattedParagraphs END - Created {formattedLines.Count} lines");
            needsUpdate = true;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (formattedLines == null || formattedLines.Count == 0)
            {
                RecreateFormattedParagraphs();
            }

            var y = 0.0;
            foreach (var line in formattedLines)
            {
                var x = 0.0;
                switch (TextAlignment)
                {
                    case System.Windows.TextAlignment.Center:
                        x = (ActualWidth - line.Width) / 2;
                        break;
                    case System.Windows.TextAlignment.Right:
                        x = ActualWidth - line.Width;
                        break;
                }

                line.Draw(drawingContext, new Point(x, y), InvertAxes.None);
                y += line.Height;
            }
        }

        protected override Size MeasureOverride(Size constraint)
        {
            System.Diagnostics.Debug.WriteLine($"[BookContentCanvas] MeasureOverride BEGIN - IsManualNav: {IsManualNavigationInProgress}, Available Height: {constraint.Height}");

            if (formattedLines == null || formattedLines.Count == 0)
            {
                RecreateFormattedParagraphs();
            }

            var totalHeight = 0.0;
            lastVisibleLineIndex = formattedLines.Count - 1; // Show all lines
            
            // Calculate total height of all content
            foreach (var line in formattedLines)
            {
                totalHeight += line.Height;
            }

            lastMeasuredHeight = totalHeight + FontSize; // Add padding
            System.Diagnostics.Debug.WriteLine($"[BookContentCanvas] MeasureOverride END - Total height: {lastMeasuredHeight}, Lines: {formattedLines.Count}");
            return new Size(constraint.Width, lastMeasuredHeight);
        }

        private class ContentTextSource : System.Windows.Media.TextFormatting.TextSource
        {
            private readonly string text;
            private readonly Typeface typeface;
            private readonly double fontSize;
            private readonly Brush foregroundBrush;

            public ContentTextSource(string text, Typeface typeface, double fontSize, Brush foregroundBrush)
            {
                this.text = text;
                this.typeface = typeface;
                this.fontSize = fontSize;
                this.foregroundBrush = foregroundBrush;
            }

            public override TextRun GetTextRun(int textSourceCharacterIndex)
            {
                if (textSourceCharacterIndex < text.Length)
                {
                    return new TextCharacters(
                        text.Substring(textSourceCharacterIndex),
                        new ContentTextRunProperties(typeface, fontSize, foregroundBrush));
                }
                return new TextEndOfParagraph(1);
            }

            public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
            {
                return new TextSpan<CultureSpecificCharacterBufferRange>(
                    textSourceCharacterIndexLimit,
                    new CultureSpecificCharacterBufferRange(
                        CultureInfo.CurrentCulture,
                        new CharacterBufferRange(string.Empty, 0, 0)));
            }

            public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex)
            {
                return textSourceCharacterIndex;
            }
        }

        private class ContentTextRunProperties : System.Windows.Media.TextFormatting.TextRunProperties
        {
            private readonly Typeface typeface;
            private readonly double fontSize;
            private readonly Brush foregroundBrush;

            public ContentTextRunProperties(Typeface typeface, double fontSize, Brush foregroundBrush)
            {
                this.typeface = typeface;
                this.fontSize = fontSize;
                this.foregroundBrush = foregroundBrush;
            }

            public override Typeface Typeface => typeface;
            public override double FontRenderingEmSize => fontSize;
            public override double FontHintingEmSize => fontSize;
            public override TextDecorationCollection TextDecorations => null;
            public override Brush ForegroundBrush => foregroundBrush;
            public override Brush BackgroundBrush => null;
            public override CultureInfo CultureInfo => CultureInfo.CurrentCulture;
            public override TextEffectCollection TextEffects => null;
        }

        private class ContentTextParagraphProperties : TextParagraphProperties
        {
            private readonly TextRunProperties defaultTextRunProperties;
            private readonly double lineHeight;
            private readonly System.Windows.TextAlignment textAlignment;
            private readonly TextWrapping textWrapping;
            private readonly FlowDirection flowDirection;

            public ContentTextParagraphProperties(
                TextRunProperties defaultTextRunProperties,
                double lineHeight,
                System.Windows.TextAlignment textAlignment,
                TextWrapping textWrapping,
                FlowDirection flowDirection)
            {
                this.defaultTextRunProperties = defaultTextRunProperties;
                this.lineHeight = lineHeight;
                this.textAlignment = textAlignment;
                this.textWrapping = textWrapping;
                this.flowDirection = flowDirection;
            }

            public override TextRunProperties DefaultTextRunProperties => defaultTextRunProperties;
            public override double LineHeight => lineHeight;
            public override bool FirstLineInParagraph => false;
            public override System.Windows.TextAlignment TextAlignment => textAlignment;
            public override double Indent => 0;
            public override TextWrapping TextWrapping => textWrapping;
            public override FlowDirection FlowDirection => flowDirection;
            public override bool AlwaysCollapsible => false;
            public override TextMarkerProperties TextMarkerProperties => null;
        }
    }
} 
