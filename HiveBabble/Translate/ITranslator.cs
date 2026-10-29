using System;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
public interface ITranslator
{
    string Name { get; }
    /// <param name="sourceLang">null or "auto" = detect.</param>
    Task<string> TranslateAsync(string text, string? sourceLang, string targetLang);
}
/// <summary>A user-facing problem (no backend, no key, bad key). Shown as a message, never a crash.</summary>
public sealed class TranslatorException(string message, Exception? inner = null) : Exception(message, inner);