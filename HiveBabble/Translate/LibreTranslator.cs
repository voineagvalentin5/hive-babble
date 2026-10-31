using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>LibreTranslate HTTP API (default: local offline Argos server at http://127.0.0.1:5000, no key).</summary>
public sealed class LibreTranslator : ITranslator, IDetectsLanguage
{
    readonly string _baseUrl;
    readonly string? _apiKey;
    readonly HttpClient _http;
    public LibreTranslator(string baseUrl, string? apiKey = null) : this(baseUrl, apiKey, TranslatorHttp.Shared) { }
    internal LibreTranslator(string baseUrl, string? apiKey, HttpClient client) { _baseUrl = baseUrl; _apiKey = apiKey; _http = client; }
    public string Name => "Libre (local)";
    public string? LastDetectedLanguage { get; private set; }
    public async Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (!Uri.TryCreate(_baseUrl?.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new TranslatorException("Enter a valid HTTP or HTTPS translator server URL in Settings, without credentials or query parameters.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.AbsoluteUri.TrimEnd('/') + "/translate")
        {
            Content = JsonContent.Create(new { q = text, source = TranslatorHttp.Source(sourceLang) ?? "auto",
                target = TranslatorHttp.Target(targetLang), format = "text", api_key = _apiKey ?? "" })
        };
        return await TranslatorHttp.SendAsync(_http, request, "LibreTranslate", json =>
            {
                if (json.TryGetProperty("detectedLanguage", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    d.TryGetProperty("language", out var dl)) LastDetectedLanguage = dl.GetString();
                return json.GetProperty("translatedText").GetString();
            },
            status => status == 400 ? "LibreTranslate rejected the request. Check the selected languages and installed translation models." : TranslatorHttp.StatusMessage("LibreTranslate", status),
            endpoint.IsLoopback ? "Local translator not running. Restart Hive-Babble to start it, or run Start-Translator.ps1 for a source build."
                : "Could not reach LibreTranslate. Check the server URL and connection in Settings.");
    }
}
