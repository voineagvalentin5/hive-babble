using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Media.Ocr;
namespace HiveBabble.Ocr;
/// <summary>OcrLanguage blank or "auto": runs every installed Windows OCR language on the frame and keeps the
/// result whose script matches what it read (CJK engines must return CJK; otherwise the Latin engine wins).</summary>
public sealed class AutoOcrEngine : IOcrEngine
{
    readonly List<WindowsOcrEngine> _engines;
    public string LanguageTag { get; private set; }
    public AutoOcrEngine()
    {
        _engines = OcrEngine.AvailableRecognizerLanguages.Select(l => { try { return new WindowsOcrEngine(l.LanguageTag); } catch { return null; } })
            .Where(e => e != null).Cast<WindowsOcrEngine>().GroupBy(e => e.LanguageTag).Select(g => g.First()).ToList();
        if (_engines.Count == 0) _engines.Add(new WindowsOcrEngine());
        LanguageTag = "auto";
    }
    static bool IsCjkTag(string t) => t.StartsWith("ja") || t.StartsWith("zh") || t.StartsWith("ko");
    static readonly Regex Cjk = new(@"[\u3040-\u30ff\u3400-\u9fff\uac00-\ud7af\uff00-\uffef]");
    static readonly Regex CjkSpace = new(@"(?<=[\u3000-\u9fff\uac00-\ud7af\uff00-\uffef])\s+(?=[\u3000-\u9fff\uac00-\ud7af\uff00-\uffef])");
    public static string JoinCjk(string s) => CjkSpace.Replace(s, "");
    public async Task<IReadOnlyList<OcrLine>> RecognizeAsync(byte[] bgra, int width, int height)
    {
        var results = new List<(WindowsOcrEngine e, IReadOnlyList<OcrLine> lines)>();
        foreach (var e in _engines) results.Add((e, await e.RecognizeAsync(bgra, width, height)));
        double CjkRatio(IReadOnlyList<OcrLine> ls) { var s = string.Concat(ls.Select(l => l.Text)).Replace(" ", ""); return s.Length == 0 ? 0 : (double)Cjk.Matches(s).Count / s.Length; }
        var cjk = results.Where(r => IsCjkTag(r.e.LanguageTag)).OrderByDescending(r => CjkRatio(r.lines)).FirstOrDefault();
        (WindowsOcrEngine e, IReadOnlyList<OcrLine> lines) pick;
        if (cjk.e != null && CjkRatio(cjk.lines) >= 0.3)
            pick = (cjk.e, cjk.lines.Select(l => l with { Text = JoinCjk(l.Text) }).ToList());
        else
            pick = results.Where(r => !IsCjkTag(r.e.LanguageTag)).OrderByDescending(r => r.lines.Sum(l => l.Text.Length)).FirstOrDefault() is var lat && lat.e != null ? lat : results[0];
        LanguageTag = "auto:" + pick.e.LanguageTag;
        return pick.lines;
    }
}