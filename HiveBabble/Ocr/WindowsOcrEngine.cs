using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
namespace HiveBabble.Ocr;
/// <summary>Windows.Media.Ocr (built-in, offline).</summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    readonly OcrEngine _engine;
    public string LanguageTag => _engine.RecognizerLanguage.LanguageTag;
    public WindowsOcrEngine(string? preferredTag = null)
    {
        OcrEngine? e = null;
        if (!string.IsNullOrWhiteSpace(preferredTag))
        {
            var lang = new Language(preferredTag);
            if (OcrEngine.IsLanguageSupported(lang)) e = OcrEngine.TryCreateFromLanguage(lang);
        }
        e ??= OcrEngine.TryCreateFromUserProfileLanguages();
        e ??= OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage).FirstOrDefault(x => x != null);
        _engine = e ?? throw new InvalidOperationException("No Windows OCR language is installed. Add one in Settings > Time & language > Language.");
    }
    public static string Available => string.Join(",", OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag));
    public async Task<IReadOnlyList<OcrLine>> RecognizeAsync(byte[] bgra, int width, int height)
    {
        double scale = Math.Min(1, (double)OcrEngine.MaxImageDimension / Math.Max(width, height));
        if (scale < 1)
        {
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
            var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            width = resized.PixelWidth; height = resized.PixelHeight;
            bgra = new byte[checked(width * height * 4)];
            resized.CopyPixels(bgra, width * 4, 0);
        }
        using var bmp = SoftwareBitmap.CreateCopyFromBuffer(CryptographicBuffer.CreateFromByteArray(bgra), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bmp);
        var lines = new List<OcrLine>();
        foreach (var l in result.Lines)
        {
            var r = l.Words.Select(w => w.BoundingRect).ToList();
            if (r.Count == 0) continue;
            double x = r.Min(b => b.X), y = r.Min(b => b.Y);
            lines.Add(new OcrLine(l.Text, x / scale, y / scale, (r.Max(b => b.X + b.Width) - x) / scale, (r.Max(b => b.Y + b.Height) - y) / scale));
        }
        return lines;
    }
}
