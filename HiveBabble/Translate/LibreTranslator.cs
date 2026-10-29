using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>LibreTranslate HTTP API (default: local offline Argos server at http://127.0.0.1:5000, no key).</summary>
public sealed class LibreTranslator(string baseUrl, string? apiKey = null) : ITranslator
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public string Name => "Libre (local)";
    public async Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
    {
        var url = baseUrl.TrimEnd('/') + "/translate";
        HttpResponseMessage resp;
        try
        {
            resp = await Http.PostAsJsonAsync(url, new { q = text, source = string.IsNullOrEmpty(sourceLang) ? "auto" : sourceLang, target = targetLang, format = "text", api_key = apiKey ?? "" });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TranslatorException($"Local translator not running at {baseUrl}. Run Start-Translator.ps1 in the Hive-Babble folder, then try again.", ex);
        }
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string err = body;
            try { err = JsonDocument.Parse(body).RootElement.GetProperty("error").GetString() ?? body; } catch { }
            throw new TranslatorException($"Libre translator error ({(int)resp.StatusCode}): {err}");
        }
        return JsonDocument.Parse(body).RootElement.GetProperty("translatedText").GetString() ?? "";
    }
}