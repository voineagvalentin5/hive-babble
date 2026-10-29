using System.Collections.Concurrent;
namespace HiveBabble.Translate;
/// <summary>M4: (source text + lang) -> result cache.</summary>
public sealed class TranslationCache
{
    readonly ConcurrentDictionary<string, string> _map = new();
    static string Key(string text, string target) => target + "\u0001" + text;
    public bool TryGet(string text, string target, out string? result) => _map.TryGetValue(Key(text, target), out result);
    public void Set(string text, string target, string result) => _map[Key(text, target)] = result;
}