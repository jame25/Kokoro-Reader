using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Linq;
using System.Text;

namespace KokoroReader.Models
{
    public class Page
    {
        public string Content { get; set; }

        public bool IsEmpty()
        {
            if (string.IsNullOrWhiteSpace(Content))
                return true;

            var cleanContent = Content.Replace("\n", "").Replace("\r", "").Replace("\t", "");
            cleanContent = System.Text.RegularExpressions.Regex.Replace(cleanContent, @"<[^>]+>", "");
            return string.IsNullOrWhiteSpace(cleanContent);
        }

        public static List<Page> CreatePages(List<string> paragraphs, double pageHeight, double fontSize, double lineHeight, System.Windows.TextAlignment textAlignment, string fontFamilyName)
        {
            if (pageHeight <= 0)
            {
                pageHeight = 800;
            }

            var pages = new List<Page>();
            var currentPageContent = new List<string>();
            double currentHeight = 0;

            paragraphs = paragraphs.Where(p => !string.IsNullOrWhiteSpace(p.Trim())).ToList();

            if (paragraphs.Count == 0)
            {
                return pages;
            }

            double pageWidth = Math.Max(500, Math.Min(pageHeight * 0.8, 800));
            double effectiveWidth = pageWidth - 8;
            double paragraphSpacing = Math.Max(1, fontSize * 0.05);
            double effectivePageHeight = Math.Max(400, pageHeight - 4);
            double heightThreshold = effectivePageHeight * 1.02;

            var measureTextBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily(fontFamilyName),
                FontSize = fontSize,
                LineHeight = fontSize * lineHeight,
                TextAlignment = textAlignment,
                Width = effectiveWidth,
                Margin = new Thickness(0)
            };

            var measurePanel = new Grid { Width = effectiveWidth };
            measurePanel.Children.Add(measureTextBlock);

            var paragraphMeasurements = new List<double>();
            double totalContentHeight = 0;

            for (int i = 0; i < paragraphs.Count; i++)
            {
                var paragraph = paragraphs[i];
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    paragraphMeasurements.Add(0);
                    continue;
                }

                measureTextBlock.Text = paragraph;
                measureTextBlock.Measure(new Size(effectiveWidth, double.PositiveInfinity));
                measureTextBlock.Arrange(new Rect(0, 0, effectiveWidth, measureTextBlock.DesiredSize.Height));
                double height = measureTextBlock.ActualHeight;
                paragraphMeasurements.Add(height);

                var spacing = i < paragraphs.Count - 1 ? paragraphSpacing : 0;
                totalContentHeight += height + spacing;
            }

            int currentParagraph = 0;
            currentHeight = 0;
            int currentPageNumber = 1;

            void CreatePageFromCurrentContent()
            {
                if (currentPageContent.Count > 0)
                {
                    pages.Add(new Page { Content = string.Join("\n", currentPageContent) });
                    currentPageNumber++;
                    currentPageContent.Clear();
                    currentHeight = 0;
                }
            }

            while (currentParagraph < paragraphs.Count)
            {
                if (string.IsNullOrWhiteSpace(paragraphs[currentParagraph]))
                {
                    currentParagraph++;
                    continue;
                }

                double paragraphHeight = paragraphMeasurements[currentParagraph];
                double addedSpacing = currentPageContent.Count > 0 ? paragraphSpacing : 0;
                double projectedHeight = currentHeight + (addedSpacing + paragraphHeight);

                bool shouldAddToCurrent = projectedHeight <= heightThreshold || 
                    (currentPageContent.Count == 0 && paragraphHeight <= effectivePageHeight);

                if (shouldAddToCurrent)
                {
                    if (addedSpacing > 0)
                    {
                        currentHeight += addedSpacing;
                    }
                    currentPageContent.Add(paragraphs[currentParagraph]);
                    currentHeight += paragraphHeight;
                    currentParagraph++;
                }
                else
                {
                    if (currentPageContent.Count == 0 && paragraphHeight > effectivePageHeight)
                    {
                        var words = paragraphs[currentParagraph].Split(' ');
                        var currentPart = new List<string>();
                        double partHeight = 0;

                        foreach (var word in words)
                        {
                            var testText = string.Join(" ", currentPart) + (currentPart.Count > 0 ? " " : "") + word;
                            measureTextBlock.Text = testText;
                            measureTextBlock.Measure(new Size(effectiveWidth, double.PositiveInfinity));
                            measureTextBlock.Arrange(new Rect(0, 0, effectiveWidth, measureTextBlock.DesiredSize.Height));
                            double testHeight = measureTextBlock.ActualHeight;

                            if (testHeight > effectivePageHeight && currentPart.Count > 0)
                            {
                                var content = string.Join(" ", currentPart);
                                pages.Add(new Page { Content = content });
                                currentPageNumber++;
                                currentPart.Clear();
                                partHeight = 0;
                            }

                            currentPart.Add(word);
                            partHeight = testHeight;
                        }

                        if (currentPart.Count > 0)
                        {
                            var content = string.Join(" ", currentPart);
                            currentPageContent.Add(content);
                            currentHeight = partHeight;
                        }
                        currentParagraph++;
                    }
                    else
                    {
                        CreatePageFromCurrentContent();
                    }
                }
            }

            CreatePageFromCurrentContent();
            return pages;
        }
    }
} 