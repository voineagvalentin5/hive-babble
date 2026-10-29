using System.Collections.Concurrent;
namespace HiveBabble.Translate;
/// <summary>M4: (provider, source, target, line) -> translation. Bounded; static text never hits the network twice.</summary>
public sealed class TranslationCache
{
    const int MaxEntries = 5000;
    readonly ConcurrentDictionary<string, string> _map = new();
    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Count => _map.Count;
    static string Key(string provider, string source, string target, string text) => $"{provider}\u0001{source}\u0001{target}\u0001{text.Trim()}";
    public bool TryGet(string provider, string source, string target, string text, out string? result)
    {
        bool hit = _map.TryGetValue(Key(provider, source, target, text), out result);
        if (hit) Hits++; else Misses++;
        return hit;
    }
    public void Set(string provider, string source, string target, string text, string result)
    {
        if (_map.Count >= MaxEntries) _map.Clear();
        _map[Key(provider, source, target, text)] = result;
    }
    public void Clear() { _map.Clear(); Hits = 0; Misses = 0; }
}