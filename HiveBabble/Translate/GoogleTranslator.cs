using System;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>M2: Google backend. Stub in M1.</summary>
public sealed class GoogleTranslator : ITranslator
{
    public string Name => "Google";
    public Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
        => throw new NotImplementedException("Google translator arrives in M2.");
}