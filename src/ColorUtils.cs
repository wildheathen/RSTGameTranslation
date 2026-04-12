using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Media;
using Color = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace RSTGameTranslation
{
    public static class ColorUtils
    {
        /// <summary>
        /// Extracts the dominant color from a bitmap region
        /// </summary>
        /// <param name="bitmap">The source bitmap</param>
        /// <param name="x">X coordinate of the region</param>
        /// <param name="y">Y coordinate of the region</param>
        /// <param name="width">Width of the region</param>
        /// <param name="height">Height of the region</param>
        /// <returns>The dominant color in the region</returns>
        public static Color GetDominantColor(Bitmap bitmap, int x, int y, int width, int height)
        {
            // Ensure coordinates are within bitmap bounds
            x = Math.Max(0, Math.Min(x, bitmap.Width - 1));
            y = Math.Max(0, Math.Min(y, bitmap.Height - 1));
            width = Math.Min(width, bitmap.Width - x);
            height = Math.Min(height, bitmap.Height - y);

            if (width <= 0 || height <= 0)
                return Color.Black;

            // Dictionary to count color occurrences
            Dictionary<int, ColorCount> colorCounts = new Dictionary<int, ColorCount>();

            // For small blocks (labels like "DEXTERITY"), text pixels dominate the area
            // and contaminate the background color. Sample only the edges instead.
            bool edgeOnly = height < 20 || width < 50;

            if (edgeOnly)
            {
                // Sample top and bottom rows, plus left and right columns
                var edgePixels = new List<(int px, int py)>();
                int step = Math.Max(1, width / 15);
                // Top edge
                for (int i = x; i < x + width; i += step)
                    edgePixels.Add((i, y));
                // Bottom edge
                for (int i = x; i < x + width; i += step)
                    edgePixels.Add((i, Math.Min(y + height - 1, bitmap.Height - 1)));
                // Left edge
                for (int j = y; j < y + height; j += Math.Max(1, height / 5))
                    edgePixels.Add((x, j));
                // Right edge
                for (int j = y; j < y + height; j += Math.Max(1, height / 5))
                    edgePixels.Add((Math.Min(x + width - 1, bitmap.Width - 1), j));

                foreach (var (px, py) in edgePixels)
                {
                    if (px >= bitmap.Width || py >= bitmap.Height) continue;
                    Color pixelColor = bitmap.GetPixel(px, py);
                    if (pixelColor.A < 10) continue;
                    int quantizedColor = QuantizeColor(pixelColor);
                    if (colorCounts.TryGetValue(quantizedColor, out ColorCount? count))
                        count.Count++;
                    else
                        colorCounts[quantizedColor] = new ColorCount { Color = pixelColor, Count = 1 };
                }
            }
            else
            {
                // Sample pixels (don't need to check every pixel for performance)
                int sampleStep = Math.Max(1, Math.Min(width, height) / 10);

                for (int i = x; i < x + width; i += sampleStep)
                {
                    for (int j = y; j < y + height; j += sampleStep)
                    {
                        Color pixelColor = bitmap.GetPixel(i, j);

                        // Skip fully transparent pixels
                        if (pixelColor.A < 10)
                            continue;

                        // Quantize the color to reduce the number of unique colors
                        int quantizedColor = QuantizeColor(pixelColor);

                        if (colorCounts.TryGetValue(quantizedColor, out ColorCount? count))
                        {
                            count.Count++;
                        }
                        else
                        {
                            colorCounts[quantizedColor] = new ColorCount { Color = pixelColor, Count = 1 };
                        }
                    }
                }
            }

            // If no colors were found (e.g., all transparent), return black
            if (colorCounts.Count == 0)
                return Color.Black;

            // Get the most common color
            var dominantColor = colorCounts.Values.OrderByDescending(c => c.Count).First().Color;

            return dominantColor;
        }

        /// <summary>
        /// Quantizes a color to reduce the number of unique colors
        /// </summary>
        private static int QuantizeColor(Color color)
        {
            // Quantize to fewer bits per channel (e.g., 3 bits per channel)
            int r = color.R & 0xE0; // 3 most significant bits
            int g = color.G & 0xE0;
            int b = color.B & 0xE0;
            
            return (r << 16) | (g << 8) | b;
        }

        /// <summary>
        /// Determines if a color is light or dark
        /// </summary>
        public static bool IsLightColor(Color color)
        {
            // Calculate perceived brightness using the formula:
            // (0.299*R + 0.587*G + 0.114*B)
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return brightness > 0.5;
        }

        /// <summary>
        /// Converts System.Drawing.Color to System.Windows.Media.Color
        /// </summary>
        public static MediaColor ToMediaColor(this Color color)
        {
            return MediaColor.FromArgb(color.A, color.R, color.G, color.B);
        }

        /// <summary>
        /// Gets a contrasting text color (black or white) based on background color
        /// </summary>
        public static MediaColor GetContrastingTextColor(Color backgroundColor)
        {
            double brightness = (0.299 * backgroundColor.R + 0.587 * backgroundColor.G + 0.114 * backgroundColor.B) / 255;

            if (brightness > 0.7)
            {
                return MediaColor.FromRgb(0, 0, 0);
            }
            else if (brightness > 0.5)
            {
                return MediaColor.FromRgb(20, 20, 20);
            }
            else if (brightness < 0.2)
            {
                return MediaColor.FromRgb(255, 255, 255);
            }
            else
            {
                return MediaColor.FromRgb(240, 240, 240);
            }
        }

        /// <summary>
        /// Detects the actual text foreground color from a bitmap region by finding
        /// the most common color that is sufficiently different from the dominant (background) color.
        /// Falls back to a contrasting black/white if no distinct text color is found.
        /// </summary>
        public static MediaColor GetTextForegroundColor(Bitmap bitmap, int x, int y, int width, int height)
        {
            x = Math.Max(0, Math.Min(x, bitmap.Width - 1));
            y = Math.Max(0, Math.Min(y, bitmap.Height - 1));
            width = Math.Min(width, bitmap.Width - x);
            height = Math.Min(height, bitmap.Height - y);

            if (width <= 0 || height <= 0)
                return MediaColor.FromRgb(255, 255, 255);

            // Collect all pixel colors with quantization
            Dictionary<int, ColorCount> colorCounts = new Dictionary<int, ColorCount>();
            int sampleStep = Math.Max(1, Math.Min(width, height) / 15);

            for (int i = x; i < x + width; i += sampleStep)
            {
                for (int j = y; j < y + height; j += sampleStep)
                {
                    Color pixelColor = bitmap.GetPixel(i, j);
                    if (pixelColor.A < 10) continue;

                    int quantizedColor = QuantizeColorFine(pixelColor);
                    if (colorCounts.TryGetValue(quantizedColor, out ColorCount? count))
                    {
                        count.Count++;
                        // Keep a running average for more accurate color
                        count.TotalR += pixelColor.R;
                        count.TotalG += pixelColor.G;
                        count.TotalB += pixelColor.B;
                    }
                    else
                    {
                        colorCounts[quantizedColor] = new ColorCount
                        {
                            Color = pixelColor, Count = 1,
                            TotalR = pixelColor.R, TotalG = pixelColor.G, TotalB = pixelColor.B
                        };
                    }
                }
            }

            if (colorCounts.Count < 2)
                return GetContrastingTextColor(GetDominantColor(bitmap, x, y, width, height));

            // Sort by frequency: most common is likely background, second is likely text
            var sorted = colorCounts.Values.OrderByDescending(c => c.Count).ToList();
            var bgEntry = sorted[0];
            Color bgColor = Color.FromArgb(
                (int)(bgEntry.TotalR / bgEntry.Count),
                (int)(bgEntry.TotalG / bgEntry.Count),
                (int)(bgEntry.TotalB / bgEntry.Count));

            // Find the most frequent color that differs enough from the background
            foreach (var entry in sorted.Skip(1))
            {
                Color avgColor = Color.FromArgb(
                    (int)(entry.TotalR / entry.Count),
                    (int)(entry.TotalG / entry.Count),
                    (int)(entry.TotalB / entry.Count));

                double colorDistance = Math.Sqrt(
                    Math.Pow(avgColor.R - bgColor.R, 2) +
                    Math.Pow(avgColor.G - bgColor.G, 2) +
                    Math.Pow(avgColor.B - bgColor.B, 2));

                // Require minimum distance of 60 in RGB space to be considered "text"
                if (colorDistance > 60)
                {
                    return MediaColor.FromRgb(avgColor.R, avgColor.G, avgColor.B);
                }
            }

            // No distinct text color found, fall back to contrast
            return GetContrastingTextColor(bgColor);
        }

        /// <summary>
        /// Finer quantization for text color detection (4 bits per channel instead of 3)
        /// </summary>
        private static int QuantizeColorFine(Color color)
        {
            int r = color.R & 0xF0;
            int g = color.G & 0xF0;
            int b = color.B & 0xF0;
            return (r << 16) | (g << 8) | b;
        }
        
        /// <summary>
        /// Creates a background color based on the dominant color with full opacity
        /// </summary>
        public static MediaColor CreateBackgroundColor(Color dominantColor, byte alpha = 255)
        {
            return MediaColor.FromArgb(
                alpha, 
                dominantColor.R,
                dominantColor.G,
                dominantColor.B
            );
        }

        private class ColorCount
        {
            public Color Color { get; set; }
            public int Count { get; set; }
            public long TotalR { get; set; }
            public long TotalG { get; set; }
            public long TotalB { get; set; }
        }
    }
}