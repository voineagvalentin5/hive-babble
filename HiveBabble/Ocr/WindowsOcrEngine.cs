using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace HiveBabble.Ocr;
/// <summary>M2: Windows.Media.Ocr. Stub in M1.</summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    public Task<IReadOnlyList<OcrLine>> RecognizeAsync(byte[] bgraPixels, int width, int height, string? languageTag = null)
        => Task.FromResult<IReadOnlyList<OcrLine>>(Array.Empty<OcrLine>());
}