using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>Google Cloud Translation Basic v2. Credentials stay in a header rather than the URL.</summary>
public sealed class GoogleTranslator : ITranslator
{
    readonly string? _apiKey;
    readonly HttpClient _http;
    public GoogleTranslator(string? apiKey = null) : this(apiKey, TranslatorHttp.Shared) { }
    internal GoogleTranslator(string? apiKey, HttpClient client) { _apiKey = apiKey; _http = client; }
    public string Name => "Google";
    public async Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string key = TranslatorHttp.RequireKey(_apiKey, Name);
        var body = new Dictionary<string, object> { ["q"] = text, ["target"] = TranslatorHttp.Target(targetLang), ["format"] = "text" };
        string? source = TranslatorHttp.Source(sourceLang);
        if (source != null) body["source"] = source;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://translation.googleapis.com/language/translate/v2") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Goog-Api-Key", key);
        string translated = await TranslatorHttp.SendAsync(_http, request, Name,
            json => json.GetProperty("data").GetProperty("translations")[0].GetProperty("translatedText").GetString(),
            status => status switch
            {
                400 => "Google rejected the request. Check the API key and selected languages in Settings.",
                403 => "Google denied access. Check the key, Cloud Translation API access, and billing settings.",
                _ => TranslatorHttp.StatusMessage(Name, status)
            });
        return WebUtility.HtmlDecode(translated);
    }
}
