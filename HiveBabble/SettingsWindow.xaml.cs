using System;
using System.Windows;
using HiveBabble.Settings;
namespace HiveBabble;
public partial class SettingsWindow : Window
{
    readonly AppSettings _s;
    public SettingsWindow(AppSettings s)
    {
        _s = s; InitializeComponent();
        ProviderBox.ItemsSource = Enum.GetValues<TranslatorProvider>();
        ProviderBox.SelectedItem = s.Provider;
        LibreUrlBox.Text = s.LibreUrl; SourceBox.Text = s.SourceLanguage; TargetBox.Text = s.TargetLanguage;
        string available = "";
        try { available = Ocr.WindowsOcrEngine.Available; } catch { }
        OcrBox.Items.Add("auto");
        foreach (var tag in available.Split(',', StringSplitOptions.RemoveEmptyEntries)) OcrBox.Items.Add(tag);
        OcrBox.Text = s.OcrLanguage;
        OcrState.Text = "Installed OCR languages: " + (available.Length > 0 ? available : "none") + ". Add more under Windows Settings > Time & language > Language.";
        LoopBox.Text = s.LoopIntervalMs.ToString();
        KeyState.Text = s.ProtectedApiKey == null ? "No key saved." : "A key is saved. Leave blank to keep it.";
    }
    void Save_Click(object sender, RoutedEventArgs e)
    {
        _s.Provider = (TranslatorProvider)ProviderBox.SelectedItem;
        _s.LibreUrl = LibreUrlBox.Text.Trim();
        _s.SourceLanguage = string.IsNullOrWhiteSpace(SourceBox.Text) ? "auto" : SourceBox.Text.Trim();
        _s.TargetLanguage = string.IsNullOrWhiteSpace(TargetBox.Text) ? "en" : TargetBox.Text.Trim();
        _s.OcrLanguage = OcrBox.Text.Trim();
        if (int.TryParse(LoopBox.Text.Trim(), out var ms)) _s.LoopIntervalMs = Math.Clamp(ms, 100, 10000);
        if (KeyBox.Password.Length > 0) _s.SetApiKey(KeyBox.Password);
        _s.Save(); DialogResult = true;
    }
}