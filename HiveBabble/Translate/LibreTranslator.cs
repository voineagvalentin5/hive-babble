using System;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>M2: Libre backend. Stub in M1.</summary>
public sealed class LibreTranslator : ITranslator
{
    public string Name => "Libre";
    public Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
        => throw new NotImplementedException("Libre translator arrives in M2.");
}