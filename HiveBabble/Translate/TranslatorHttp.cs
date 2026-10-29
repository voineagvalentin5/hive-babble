using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HiveBabble.Translate;

internal static class TranslatorHttp
{
    internal static readonly HttpClient Shared = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 4 * 1024 * 1024 };

    internal static string RequireKey(string? key, string provider)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new TranslatorException($"{provider} needs an API key. Add it in Settings, or choose Libre (local).");
        key = key.Trim();
        if (key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new TranslatorException($"The {provider} API key contains invalid characters. Check it in Settings.");
        return key;
    }

    internal static string Target(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new TranslatorException("Choose a target language in Settings.");
        return target.Trim();
    }

    internal static string? Source(string? source) => string.IsNullOrWhiteSpace(source) ||
        source.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : source.Trim();

    internal static async Task<string> SendAsync(HttpClient client, HttpRequestMessage request, string provider,
        Func<JsonElement, string?> readText, Func<int, string> statusMessage, string? connectionMessage = null)
    {
        try
        {
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            // Never surface a provider error body: it can repeat the submitted text or credentials.
            if (!response.IsSuccessStatusCode) throw new TranslatorException(statusMessage((int)response.StatusCode));
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            string? result = readText(document.RootElement);
            if (string.IsNullOrWhiteSpace(result)) throw new TranslatorException($"{provider} returned an empty translation. Try again.");
            return result;
        }
        catch (TaskCanceledException) { throw new TranslatorException($"{provider} took too long to respond. Try again when the service is ready."); }
        catch (HttpRequestException) { throw new TranslatorException(connectionMessage ?? $"Could not reach {provider}. Check your internet connection."); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        { throw new TranslatorException($"{provider} returned an unexpected response. Check the service and try again."); }
    }

    internal static string StatusMessage(string provider, int status) => status switch
    {
        401 or 403 => $"{provider} denied access. Check the API key and account settings.",
        413 => $"The selected text is too large for {provider}. Make the frame smaller.",
        429 => $"{provider} is rate-limiting requests. Wait a moment before trying again.",
        >= 500 => $"{provider} is temporarily unavailable. Try again shortly.",
        _ => $"{provider} could not translate this text (HTTP {status}). Check the selected languages and service settings."
    };
}
