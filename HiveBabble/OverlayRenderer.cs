using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HiveBabble.Ocr;
namespace HiveBabble;
/// <summary>M3: draws each translated line in a filled box over its OCR rect, font shrunk to fit the box.
/// OCR rects are physical pixels of the capture; they are converted to DIPs with the frame's current DPI (per-monitor).</summary>
public sealed class OverlayRenderer
{
    static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Brush Fill = Freeze(new LinearGradientBrush(Color.FromArgb(0xF0, 0x2A, 0x31, 0x3A), Color.FromArgb(0xF0, 0x15, 0x1A, 0x20), 90));
    static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF0, 0xE9)));
    static readonly Brush Edge = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));
    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public void Clear(Canvas canvas) => canvas.Children.Clear();

    public int Render(Canvas canvas, IReadOnlyList<OcrLine> lines, IReadOnlyList<string> texts, double offsetPx, double scaleX, double scaleY)
    {
        canvas.Children.Clear();
        double ppd = VisualTreeHelper.GetDpi(canvas).PixelsPerDip;
        for (int i = 0; i < lines.Count && i < texts.Count; i++)
        {
            string t = texts[i];
            if (string.IsNullOrWhiteSpace(t)) continue;
            var l = lines[i];
            double x = (l.X + offsetPx) / scaleX - 3, y = (l.Y + offsetPx) / scaleY - 2;
            double w = Math.Max(12, l.Width / scaleX + 6), h = Math.Max(10, l.Height / scaleY + 4);
            double size = FitFont(t, w - 6, h - 2, ppd);
            var box = new Border
            {
                Background = Fill, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Width = w, Height = h, Padding = new Thickness(3, 0, 3, 0),
                Child = new TextBlock { Text = t, FontFamily = Face.FontFamily, FontWeight = Face.Weight, FontSize = size, Foreground = Ink,
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap }
            };
            Canvas.SetLeft(box, x); Canvas.SetTop(box, y);
            canvas.Children.Add(box);
        }
        return canvas.Children.Count;
    }

    /// <summary>Largest size (from ~line height) whose single-line width fits the box.</summary>
    public static double FitFont(string text, double width, double height, double pixelsPerDip)
    {
        double size = Math.Max(6, height * 0.8);
        for (int k = 0; k < 4; k++)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White, pixelsPerDip);
            if (ft.WidthIncludingTrailingWhitespace <= width && ft.Height <= height + 2) break;
            size = Math.Max(6, size * Math.Min(width / Math.Max(1, ft.WidthIncludingTrailingWhitespace), (height + 2) / Math.Max(1, ft.Height)) * 0.98);
            if (size <= 6) break;
        }
        return size;
    }
}
