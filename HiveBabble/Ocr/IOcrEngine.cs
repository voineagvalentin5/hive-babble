using System.Collections.Generic;
using System.Threading.Tasks;
namespace HiveBabble.Ocr;
public record OcrLine(string Text, double X, double Y, double Width, double Height);
public interface IOcrEngine
{
    string LanguageTag { get; }
    Task<IReadOnlyList<OcrLine>> RecognizeAsync(byte[] bgraPixels, int width, int height);
}