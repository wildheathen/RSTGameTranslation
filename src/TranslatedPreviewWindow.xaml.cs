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

                // Get text objects from Logic singleton
                var textObjects = Logic.Instance.GetTextObjects();
                if (textObjects == null || textObjects.Count == 0)
                {
                    previewStatusText.Text = "No text detected";
                    return;
                }

                // DPI correction: TextObject coords are in logical units (divided by DPI scale
                // in DisplayOcrResults). The screenshot on disk is in physical pixels, so
                // multiply coords back by the exact same DPI scale that was used during OCR.
                double dpiScaleX = Logic.Instance.LastOcrDpiScaleX;
                double dpiScaleY = Logic.Instance.LastOcrDpiScaleY;

                // Diagnostic logging
                string diagLog = $"[{DateTime.Now:HH:mm:ss}] DPI: {dpiScaleX:F2}x{dpiScaleY:F2} | Image: {previewContainer.Width}x{previewContainer.Height}\n";
                int renderedCount = 0;
                foreach (var textObj in textObjects)
                {
                    if (textObj == null) continue;

                    string displayText = !string.IsNullOrEmpty(textObj.TextTranslated)
                        ? textObj.TextTranslated
                        : textObj.Text;

                    if (string.IsNullOrEmpty(displayText)) continue;

                    double physX = textObj.X * dpiScaleX;
                    double physY = textObj.Y * dpiScaleY;
                    double physWidth = textObj.Width > 0 ? textObj.Width * dpiScaleX : 0;
                    double physHeight = textObj.Height > 0 ? textObj.Height * dpiScaleY : 0;

                    // Create new brushes from the Color values to avoid cross-thread ownership issues
                    var fgColor = textObj.TextColor?.Color ?? System.Windows.Media.Colors.White;
                    var bgColor = textObj.BackgroundColor?.Color ?? System.Windows.Media.Color.FromArgb(200, 0, 0, 0);
                    var fgBrush = new SolidColorBrush(fgColor);
                    var bgBrush = new SolidColorBrush(bgColor);

                    // Calculate font size to fit within the detected box height.
                    // Use a fraction of the box height as a starting point, clamped to reasonable range.
                    double baseFontSize = physHeight > 0
                        ? Math.Clamp(physHeight * 0.6, 8, 48)
                        : 14 * dpiScaleX;

                    var textBlock = new TextBlock
                    {
                        Text = displayText,
                        Foreground = fgBrush,
                        FontWeight = FontWeights.Normal,
                        FontSize = baseFontSize,
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
                        ClipToBounds = true
                    };

                    if (physWidth > 0)
                    {
                        border.Width = physWidth;
                        textBlock.MaxWidth = physWidth - 2;
                    }
                    if (physHeight > 0)
                    {
                        border.Height = physHeight;
                    }

                    Canvas.SetLeft(border, physX);
                    Canvas.SetTop(border, physY);

                    previewOverlayCanvas.Children.Add(border);
                    renderedCount++;
                    diagLog += $"  [{renderedCount}] logical=({textObj.X:F0},{textObj.Y:F0}) phys=({physX:F0},{physY:F0}) size=({physWidth:F0}x{physHeight:F0}) txt={displayText.Substring(0, Math.Min(30, displayText.Length))}\n";
                }

                previewStatusText.Text = $"{renderedCount} blocks | {DateTime.Now:HH:mm:ss}";
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_debug.log"), diagLog); } catch { }
            }
            catch (Exception ex)
            {
                string errorLog = $"[{DateTime.Now:HH:mm:ss}] Error refreshing preview: {ex.Message}\n{ex.StackTrace}\n";
                Console.WriteLine(errorLog);
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_debug.log"), errorLog); } catch { }
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
