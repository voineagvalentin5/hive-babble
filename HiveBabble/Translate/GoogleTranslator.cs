using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>Google Cloud Translation. Not implemented yet.</summary>
public sealed class GoogleTranslator : ITranslator
{
    public string Name => "Google";
    public Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
        => throw new TranslatorException("Google translation isn't available yet. Switch the provider to Libre (local) in ⚙ Settings.");
}