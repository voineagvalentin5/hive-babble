using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HiveBabble.Settings;
using HiveBabble.Translate;

namespace HiveBabble.Diagnostics;

/// <summary>Deterministic provider checks. All HTTP is intercepted; no cloud request or real settings write occurs.</summary>
public static class ProviderVerification
{
    sealed record Check(string Name, bool Passed, string Detail);
    const string PrivateMarker = "diagnostic-private-content-do-not-echo";

    public static async Task<bool> RunAsync(string outputDirectory)
    {
        var checks = new List<Check>();
        async Task Test(string name, Func<Task<bool>> action)
        {
            try { bool passed = await action(); checks.Add(new(name, passed, passed ? "Passed with intercepted HTTP or in-memory settings." : "Unexpected result.")); }
            catch (Exception ex) { checks.Add(new(name, false, $"Unexpected {ex.GetType().Name}.")); }
        }
        static async Task<bool> Error(Func<Task<string>> action, string expected)
        {
            try { await action(); return false; }
            catch (TranslatorException ex)
            { return ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase) && !ex.ToString().Contains(PrivateMarker, StringComparison.Ordinal); }
        }
        using var forbidden = new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("HTTP must not run for missing credentials.")));
        await Test("deepl_missing_key", () => Error(() => new DeepLTranslator(null, forbidden).TranslateAsync("Hola", "es", "en"), "API key"));
        await Test("google_missing_key", () => Error(() => new GoogleTranslator(null, forbidden).TranslateAsync("Hola", "es", "en"), "API key"));
        await Test("malformed_key", () => Error(() => new GoogleTranslator("bad\r\nkey", forbidden).TranslateAsync("Hola", "es", "en"), "invalid characters"));
        await Test("invalid_libre_url", () => Error(() => new LibreTranslator("file:///private", null, forbidden).TranslateAsync("Hola", "es", "en"), "server URL"));

        await Test("google_wire_format_html_decode_and_disposal", async () =>
        {
            bool validRequest = false;
            var content = new ObservedContent("{\"data\":{\"translations\":[{\"translatedText\":\"Read &amp; learn &#39;today&#39;\"}]}}");
            using var client = new HttpClient(new StubHandler(async (request, _) =>
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                validRequest = request.RequestUri?.AbsolutePath == "/language/translate/v2" && request.RequestUri.Query == "" &&
                    request.Headers.GetValues("X-Goog-Api-Key").Single() == "test-only-key" &&
                    json.RootElement.GetProperty("q").GetString() == "Hola" && !json.RootElement.TryGetProperty("source", out var ignoredSource);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }));
            string result = await new GoogleTranslator("test-only-key", client).TranslateAsync("Hola", "AUTO", "en");
            return validRequest && result == "Read & learn 'today'" && content.WasDisposed;
        });
        await Test("deepl_free_endpoint_and_disposal", async () =>
        {
            bool validRequest = false;
            var content = new ObservedContent("{\"translations\":[{\"text\":\"Hello\"}]}");
            using var client = new HttpClient(new StubHandler(async (request, _) =>
            {
                string form = await request.Content!.ReadAsStringAsync();
                validRequest = request.RequestUri?.Host == "api-free.deepl.com" &&
                    request.Headers.GetValues("Authorization").Single() == "DeepL-Auth-Key test-only-key:fx" &&
                    form.Contains("target_lang=EN-US", StringComparison.Ordinal) && !form.Contains("source_lang", StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }));
            return await new DeepLTranslator("test-only-key:fx", client).TranslateAsync("Hola", "auto", "en") == "Hello" && validRequest && content.WasDisposed;
        });
        await Test("libre_wire_format_and_disposal", async () =>
        {
            bool validRequest = false;
            var content = new ObservedContent("{\"translatedText\":\"Hello\"}");
            using var client = new HttpClient(new StubHandler(async (request, _) =>
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                validRequest = request.RequestUri?.AbsolutePath == "/translate" && json.RootElement.GetProperty("source").GetString() == "auto" &&
                    json.RootElement.GetProperty("api_key").GetString() == "";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }));
            return await new LibreTranslator("http://127.0.0.1:5000", null, client).TranslateAsync("Hola", null, "en") == "Hello" && validRequest && content.WasDisposed;
        });
        foreach (int status in new[] { 403, 429, 456, 503 })
        {
            await Test($"deepl_http_{status}_safe_error_and_disposal", async () =>
            {
                var content = new ObservedContent(PrivateMarker);
                using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content })));
                bool safeError = await Error(() => new DeepLTranslator("test-only-key", client).TranslateAsync(PrivateMarker, "es", "en"),
                    status switch { 403 => "denied", 429 => "rate-limit", 456 => "quota", _ => "unavailable" });
                return safeError && content.WasDisposed;
            });
        }
        await Test("malformed_response_is_safe", async () =>
        {
            using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PrivateMarker) })));
            return await Error(() => new GoogleTranslator("test-only-key", client).TranslateAsync("Hola", "es", "en"), "unexpected response");
        });
        await Test("timeout_is_actionable", async () =>
        {
            using var client = new HttpClient(new StubHandler((_, _) => throw new TaskCanceledException(PrivateMarker)));
            return await Error(() => new GoogleTranslator("test-only-key", client).TranslateAsync("Hola", "es", "en"), "too long");
        });
        await Test("local_offline_is_actionable_and_safe", async () =>
        {
            using var client = new HttpClient(new StubHandler((_, _) => throw new HttpRequestException(PrivateMarker)));
            return await Error(() => new LibreTranslator("http://127.0.0.1:5000", null, client).TranslateAsync("Hola", "es", "en"), "Start-Translator.ps1");
        });
        await Test("dpapi_roundtrip_clear_and_corruption", () =>
        {
            var settings = new AppSettings();
            settings.SetApiKey(PrivateMarker);
            bool protectedKey = settings.GetApiKey() == PrivateMarker && settings.ProtectedApiKey != null &&
                !JsonSerializer.Serialize(settings).Contains(PrivateMarker, StringComparison.Ordinal);
            settings.SetApiKey(null);
            bool cleared = settings.GetApiKey() == null;
            settings.ProtectedApiKey = "not-valid-base64";
            return Task.FromResult(protectedKey && cleared && settings.GetApiKey() == null);
        });
        bool allPassed = checks.All(c => c.Passed);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "provider-verification.json"), JsonSerializer.Serialize(new
        {
            completedUtc = DateTimeOffset.UtcNow, allPassed, checks,
            limitations = "HTTP was intercepted. Live cloud credentials, access, quota and translation quality were not verified. DPAPI used only an in-memory test value."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return allPassed;
    }

    sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
    sealed class ObservedContent(string value) : StringContent(value)
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
