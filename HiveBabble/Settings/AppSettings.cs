using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace HiveBabble.Settings;
public enum TranslatorProvider { Libre, DeepL, Google }
/// <summary>Saved to %APPDATA%\HiveBabble\settings.json. The API key is DPAPI-encrypted (current user) and never stored in plain text.</summary>
public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TranslatorProvider Provider { get; set; } = TranslatorProvider.Libre;
    public string LibreUrl { get; set; } = "http://127.0.0.1:5000";
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "en";
    public string OcrLanguage { get; set; } = "";
    public int LoopIntervalMs { get; set; } = 750;
    public string? ProtectedApiKey { get; set; }

    static string PathFile => System.IO.Path.Combine(Log.Dir, "settings.json");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("com.codexhive.babble");

    public string? GetApiKey()
    {
        if (string.IsNullOrEmpty(ProtectedApiKey)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedApiKey), Entropy, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }
    public void SetApiKey(string? key) => ProtectedApiKey = string.IsNullOrWhiteSpace(key) ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser));

    public static AppSettings Load()
    {
        try { if (File.Exists(PathFile)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathFile)) ?? new(); }
        catch (Exception ex) { Log.Write("Settings load failed: " + ex.Message); }
        return new();
    }
    public void Save()
    {
        Directory.CreateDirectory(Log.Dir);
        File.WriteAllText(PathFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}