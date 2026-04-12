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
                if (_instance == null || !_instance.IsLoaded)
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
                // Load latest screenshot
                if (File.Exists(_outputPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(_outputPath, UriKind.Absolute);
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

                // DPI correction: TextObject coords are in logical units (divided by DPI scale).
                // The screenshot on disk is in physical pixels, so multiply coords by DPI scale.
                DpiHelper.GetDpiForSelectedScreen(out double dpiScaleX, out double dpiScaleY);

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

                    var textBlock = new TextBlock
                    {
                        Text = displayText,
                        Foreground = textObj.TextColor,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 16 * dpiScaleX,
                        TextWrapping = TextWrapping.Wrap,
                        FlowDirection = textObj.FlowDirection,
                        MaxWidth = physWidth > 0 ? physWidth : double.PositiveInfinity
                    };

                    var border = new Border
                    {
                        Background = textObj.BackgroundColor,
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(2),
                        Child = textBlock,
                        IsHitTestVisible = false
                    };

                    if (physWidth > 0)
                    {
                        border.MaxWidth = physWidth + 20;
                        border.MinWidth = physWidth;
                    }
                    if (physHeight > 0)
                    {
                        border.MinHeight = physHeight;
                    }

                    Canvas.SetLeft(border, physX);
                    Canvas.SetTop(border, physY);

                    previewOverlayCanvas.Children.Add(border);
                }

                previewStatusText.Text = $"{textObjects.Count} blocks | {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing preview: {ex.Message}");
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
