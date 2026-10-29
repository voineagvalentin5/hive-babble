using System;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>M2: DeepL backend. Stub in M1.</summary>
public sealed class DeepLTranslator : ITranslator
{
    public string Name => "DeepL";
    public Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
        => throw new NotImplementedException("DeepL translator arrives in M2.");
}