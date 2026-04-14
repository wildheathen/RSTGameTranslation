using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RSTGameTranslation
{
    public partial class TranslatedPreviewWindow : Window
    {
        private static TranslatedPreviewWindow? _instance;

        public static TranslatedPreviewWindow Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TranslatedPreviewWindow();
                }
                return _instance;
            }
        }

        private readonly string _outputPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            MainWindow.DEFAULT_OUTPUT_PATH);

        public TranslatedPreviewWindow()
        {
            InitializeComponent();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            MainWindow.Instance.OnPreviewWindowClosed();
        }

        public void RefreshPreview()
        {
            if (!this.IsVisible) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RefreshPreview());
                return;
            }

            try
            {
                // Check if there are translated text objects first — if not,
                // keep the last translated frame on screen instead of refreshing
                var textObjects = Logic.Instance.GetTextObjects();
                if (textObjects == null || textObjects.Count == 0)
                {
                    // Don't update — keep showing the last translated preview
                    return;
                }

                // Check if any text has been translated
                bool hasTranslation = false;
                foreach (var obj in textObjects)
                {
                    if (obj != null && !string.IsNullOrEmpty(obj.TextTranslated))
                    {
                        hasTranslation = true;
                        break;
                    }
                }
                if (!hasTranslation)
                {
                    // Text detected but not yet translated — don't refresh yet
                    return;
                }

                // Load latest screenshot using stream to avoid file locking
                if (File.Exists(_outputPath))
                {
                    byte[] imageBytes = File.ReadAllBytes(_outputPath);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new MemoryStream(imageBytes);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    previewImage.Source = bitmap;
                    previewImage.Width = bitmap.PixelWidth;
                    previewImage.Height = bitmap.PixelHeight;
                    previewContainer.Width = bitmap.PixelWidth;
                    previewContainer.Height = bitmap.PixelHeight;
                }

                previewOverlayCanvas.Children.Clear();
                previewOverlayCanvas.Width = previewContainer.Width;
                previewOverlayCanvas.Height = previewContainer.Height;

                // DPI correction: TextObject coords are in logical units (divided by DPI scale
                // in DisplayOcrResults). The screenshot on disk is in physical pixels, so
                // multiply coords back by the exact same DPI scale that was used during OCR.
                double dpiScaleX = Logic.Instance.LastOcrDpiScaleX;
                double dpiScaleY = Logic.Instance.LastOcrDpiScaleY;

                // Diagnostic logging
                string diagLog = $"[{DateTime.Now:HH:mm:ss}] DPI: {dpiScaleX:F2}x{dpiScaleY:F2} | Image: {previewContainer.Width}x{previewContainer.Height}\n";

                // === Phase 1: Calculate font size for each block ===
                var blockInfos = new System.Collections.Generic.List<(
                    TextObject textObj, string text, double physX, double physY,
                    double physWidth, double physHeight, double fontSize,
                    SolidColorBrush fgBrush, SolidColorBrush bgBrush)>();

                foreach (var textObj in textObjects)
                {
                    if (textObj == null) continue;

                    // Use translated text; for untranslated blocks, render background-only
                    // to cover original text in the screenshot
                    string displayText = (textObj.TextTranslated ?? "")
                        .Replace("##|||##", "\n")
                        .Replace("|", "")
                        .Trim();

                    double physX = textObj.X * dpiScaleX;
                    double physY = textObj.Y * dpiScaleY;
                    double physWidth = textObj.Width > 0 ? textObj.Width * dpiScaleX : 0;
                    double physHeight = textObj.Height > 0 ? textObj.Height * dpiScaleY : 0;

                    var fgColor = textObj.TextColor?.Color ?? System.Windows.Media.Colors.White;
                    var bgColor = textObj.BackgroundColor?.Color ?? System.Windows.Media.Color.FromArgb(200, 0, 0, 0);

                    // Normalize height to reduce OCR variance
                    double normalizedHeight = physHeight > 0 ? Math.Max(10, Math.Round(physHeight / 10.0) * 10.0) : 0;
                    double maxFontSize = normalizedHeight > 0 ? normalizedHeight * 0.9 : 16;
                    double fontSize = Math.Clamp(maxFontSize, 6, 96);

                    // Use available width (from block X to canvas right edge) so translated
                    // text can extend rightward instead of wrapping in the narrow OCR box.
                    double availableWidth = Math.Max(physWidth, previewContainer.Width - physX);

                    // Binary search for font that fits the box
                    if (physWidth > 0 && physHeight > 0)
                    {
                        var probeWeight = physHeight <= 15 ? FontWeights.Normal : FontWeights.Bold;
                        var probe = new TextBlock
                        {
                            Text = displayText, FontWeight = probeWeight,
                            FontSize = fontSize, TextWrapping = TextWrapping.Wrap,
                        };
                        double lo = 6, hi = fontSize;
                        for (int iter = 0; iter < 10; iter++)
                        {
                            probe.FontSize = hi;
                            probe.Measure(new System.Windows.Size(availableWidth, double.PositiveInfinity));
                            if (probe.DesiredSize.Height <= physHeight)
                                break;
                            double mid = (lo + hi) / 2;
                            probe.FontSize = mid;
                            probe.Measure(new System.Windows.Size(availableWidth, double.PositiveInfinity));
                            if (probe.DesiredSize.Height <= physHeight)
                                lo = mid;
                            else
                                hi = mid;
                        }
                        fontSize = probe.FontSize;
                    }

                    blockInfos.Add((textObj, displayText, physX, physY, physWidth, physHeight, fontSize,
                        new SolidColorBrush(fgColor), new SolidColorBrush(bgColor)));
                }

                // === Phase 2: Group nearby blocks and equalize font sizes ===
                // Blocks with similar X and consecutive Y are a "paragraph".
                // Compare against the closest block in the group (by Y) to handle
                // long paragraphs where first and last block are far apart.
                var used = new bool[blockInfos.Count];
                var finalFontSizes = new double[blockInfos.Count];
                for (int i = 0; i < blockInfos.Count; i++)
                {
                    finalFontSizes[i] = blockInfos[i].fontSize;
                    // Exclude background-only blocks (no translation) from font grouping
                    if (string.IsNullOrEmpty(blockInfos[i].text))
                        used[i] = true;
                }

                for (int i = 0; i < blockInfos.Count; i++)
                {
                    if (used[i]) continue;
                    used[i] = true;

                    var group = new System.Collections.Generic.List<int> { i };
                    // Keep scanning until no more blocks can be added
                    bool added = true;
                    while (added)
                    {
                        added = false;
                        for (int j = 0; j < blockInfos.Count; j++)
                        {
                            if (used[j]) continue;
                            // Find the closest group member by Y, then check X-overlap
                            double minYDist = double.MaxValue;
                            bool xOverlaps = false;
                            foreach (int gi in group)
                            {
                                double yd = Math.Abs(blockInfos[j].physY - blockInfos[gi].physY);
                                if (yd < minYDist)
                                {
                                    minYDist = yd;
                                    // Check if X ranges overlap (catches indented continuation lines)
                                    double jL = blockInfos[j].physX;
                                    double jR = jL + Math.Max(blockInfos[j].physWidth, 10);
                                    double gL = blockInfos[gi].physX;
                                    double gR = gL + Math.Max(blockInfos[gi].physWidth, 10);
                                    xOverlaps = (jL < gR + 20) && (gL < jR + 20);
                                }
                            }
                            // X ranges overlap and vertically close to nearest neighbor (±40px)
                            if (xOverlaps && minYDist < 40)
                            {
                                group.Add(j);
                                used[j] = true;
                                added = true;
                            }
                        }
                    }

                    // Equalize: use the median font in the group so one outlier
                    // (a block where translation is much longer) doesn't shrink all others.
                    if (group.Count > 1)
                    {
                        var groupFonts = new System.Collections.Generic.List<double>();
                        foreach (int idx in group)
                            groupFonts.Add(finalFontSizes[idx]);
                        groupFonts.Sort();
                        double medianFont = groupFonts[groupFonts.Count / 2];
                        foreach (int idx in group)
                            finalFontSizes[idx] = medianFont;
                    }
                }

                // === Phase 3: Render blocks ===
                int renderedCount = 0;
                for (int i = 0; i < blockInfos.Count; i++)
                {
                    var (textObj, displayText, physX, physY, physWidth, physHeight, _, fgBrush, bgBrush) = blockInfos[i];
                    double fontSize = finalFontSizes[i];

                    // Background-only block: cover original text when no translation exists
                    if (string.IsNullOrEmpty(displayText))
                    {
                        var coverBorder = new Border
                        {
                            Background = bgBrush,
                            Width = physWidth,
                            Height = physHeight,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(coverBorder, physX);
                        Canvas.SetTop(coverBorder, physY);
                        previewOverlayCanvas.Children.Add(coverBorder);
                        continue;
                    }

                    // Use Normal weight for small labels (height ≤ 15px) to prevent overflow
                    var fontWeight = physHeight <= 15 ? FontWeights.Normal : FontWeights.Bold;

                    var textBlock = new TextBlock
                    {
                        Text = displayText,
                        Foreground = fgBrush,
                        FontWeight = fontWeight,
                        FontSize = fontSize,
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.None,
                        FlowDirection = textObj.FlowDirection,
                    };

                    var border = new Border
                    {
                        Background = bgBrush,
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(1),
                        Child = textBlock,
                        IsHitTestVisible = false,
                        ClipToBounds = false
                    };

                    if (physWidth > 0)
                    {
                        // Let text extend rightward into available space instead of
                        // wrapping inside the narrow OCR box (translations are often longer)
                        double availWidth = previewContainer.Width - physX;
                        border.MinWidth = physWidth;
                        border.MaxWidth = availWidth;
                        textBlock.MaxWidth = availWidth - 2;
                    }
                    if (physHeight > 0)
                    {
                        border.MinHeight = physHeight;
                    }

                    Canvas.SetLeft(border, physX);
                    Canvas.SetTop(border, physY);

                    previewOverlayCanvas.Children.Add(border);
                    renderedCount++;
                    diagLog += $"  [{renderedCount}] pos=({physX:F0},{physY:F0}) size=({physWidth:F0}x{physHeight:F0}) font={fontSize:F1} txt={displayText.Substring(0, Math.Min(30, displayText.Length))}\n";
                }

                previewStatusText.Text = $"{renderedCount} blocks | {DateTime.Now:HH:mm:ss}";
#if DEBUG
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_debug.log"), diagLog); } catch { }
#endif
            }
            catch (Exception ex)
            {
                string errorLog = $"[{DateTime.Now:HH:mm:ss}] Error refreshing preview: {ex.Message}\n{ex.StackTrace}\n";
                Console.WriteLine(errorLog);
#if DEBUG
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_debug.log"), errorLog); } catch { }
#endif
                previewStatusText.Text = $"Error: {ex.Message}";
            }
        }

        public void ClearPreview()
        {
            if (!this.IsVisible) return;

            Dispatcher.Invoke(() =>
            {
                previewOverlayCanvas.Children.Clear();
                previewStatusText.Text = "Waiting for translation...";
            });
        }
    }
}
