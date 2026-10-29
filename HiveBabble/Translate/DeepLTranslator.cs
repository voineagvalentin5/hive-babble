using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>DeepL API. Free keys end in ":fx" and use api-free.deepl.com. Ready for when a key is added in Settings.</summary>
public sealed class DeepLTranslator(string? apiKey) : ITranslator
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public string Name => "DeepL";
    public async Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new TranslatorException("DeepL needs an API key. Add it in ⚙ Settings, or switch the provider to Libre (local).");
        var host = apiKey.EndsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com";
        var form = new List<KeyValuePair<string, string>> { new("text", text), new("target_lang", targetLang.ToUpperInvariant() == "EN" ? "EN-US" : targetLang.ToUpperInvariant()) };
        if (!string.IsNullOrEmpty(sourceLang) && sourceLang != "auto") form.Add(new("source_lang", sourceLang.ToUpperInvariant()));
        using var req = new HttpRequestMessage(HttpMethod.Post, host + "/v2/translate") { Content = new FormUrlEncodedContent(form) };
        req.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + apiKey);
        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new TranslatorException("Could not reach DeepL. Check the internet connection.", ex); }
        var body = await resp.Content.ReadAsStringAsync();
        if ((int)resp.StatusCode == 403) throw new TranslatorException("DeepL rejected the API key. Check it in ⚙ Settings.");
        if (!resp.IsSuccessStatusCode) throw new TranslatorException($"DeepL error ({(int)resp.StatusCode}).");
        return JsonDocument.Parse(body).RootElement.GetProperty("translations")[0].GetProperty("text").GetString() ?? "";
    }
}