using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>DeepL API. Free keys end in ":fx" and use api-free.deepl.com. Ready for when a key is added in Settings.</summary>
public sealed class DeepLTranslator : ITranslator, IDetectsLanguage
{
    readonly string? _apiKey;
    readonly HttpClient _http;
    public DeepLTranslator(string? apiKey) : this(apiKey, TranslatorHttp.Shared) { }
    internal DeepLTranslator(string? apiKey, HttpClient client) { _apiKey = apiKey; _http = client; }
    public string Name => "DeepL";
    public string? LastDetectedLanguage { get; private set; }
    public async Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string key = TranslatorHttp.RequireKey(_apiKey, Name);
        var host = key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase) ? "https://api-free.deepl.com" : "https://api.deepl.com";
        string target = TranslatorHttp.Target(targetLang).ToUpperInvariant();
        var form = new List<KeyValuePair<string, string>> { new("text", text), new("target_lang", target == "EN" ? "EN-US" : target) };
        string? source = TranslatorHttp.Source(sourceLang);
        if (source != null) form.Add(new("source_lang", source.ToUpperInvariant()));
        using var req = new HttpRequestMessage(HttpMethod.Post, host + "/v2/translate") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Add("Authorization", "DeepL-Auth-Key " + key);
        return await TranslatorHttp.SendAsync(_http, req, Name,
            json =>
            {
                var t = json.GetProperty("translations")[0];
                if (t.TryGetProperty("detected_source_language", out var d)) LastDetectedLanguage = d.GetString()?.ToLowerInvariant();
                return t.GetProperty("text").GetString();
            },
            status => status == 456 ? "DeepL's character quota has been reached. Check your account limit." : TranslatorHttp.StatusMessage(Name, status));
    }
}
