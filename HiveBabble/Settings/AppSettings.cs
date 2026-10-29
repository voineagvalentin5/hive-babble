namespace HiveBabble.Settings;
/// <summary>App settings. API keys will be stored with DPAPI (M2); never committed.</summary>
public sealed class AppSettings
{
    public string TargetLanguage { get; set; } = "en";
    public string Translator { get; set; } = "DeepL";
    public int LoopIntervalMs { get; set; } = 750;
}