using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using HiveBabble.Capture;
using HiveBabble.Ocr;
using HiveBabble.Settings;
using HiveBabble.Translate;

namespace HiveBabble;

/// <summary>The see-through frame (interior). The drag bar lives in an owned BarWindow glued above it,
/// so locking makes only this window click-through while the bar stays clickable.</summary>
public partial class FrameWindow : Window
{
    const int WM_HOTKEY = 0x0312, HOTKEY_LOCK = 0xB0B1, HOTKEY_SNAP = 0xB0B2;
    const uint MOD_CONTROL = 0x2, MOD_ALT = 0x1, MOD_NOREPEAT = 0x4000, VK_L = 0x4C, VK_T = 0x54;

    IntPtr _hwnd;
    bool _locked, _paused, _syncing, _busy;
    BarWindow? _bar;
    SidePanelWindow? _panel;
    readonly ScreenCapturer _capturer = new();
    public bool ExcludedFromCapture { get; private set; }
    public AppSettings Settings { get; } = AppSettings.Load();
    public string LastSourceText { get; private set; } = "";
    public string LastTranslatedText { get; private set; } = "";
    public string LastStatus { get; private set; } = "";
    public SidePanelWindow? ResultsPanel => _panel;

    public FrameWindow()
    {
        InitializeComponent();
        ApplyCommandLineRect();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        LocationChanged += (_, _) => PlaceChildren();
        SizeChanged += (_, _) => PlaceChildren();
        StateChanged += (_, _) =>
        {
            var v = WindowState == WindowState.Minimized ? Visibility.Hidden : Visibility.Visible;
            if (_bar != null) _bar.Visibility = v;
        };
        Closed += (_, _) => { if (_hwnd != IntPtr.Zero) { Native.UnregisterHotKey(_hwnd, HOTKEY_LOCK); Native.UnregisterHotKey(_hwnd, HOTKEY_SNAP); } };
    }

    // --rect L,T,W,H (DIPs) positions the frame; --autosnap N snaps after N seconds; --exit closes after the snap. Used for testing.
    static string? Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : (i >= 0 ? "" : null);
    }

    void ApplyCommandLineRect()
    {
        var r = Arg("--rect")?.Split(',').Select(double.Parse).ToArray();
        if (r is { Length: 4 }) { WindowStartupLocation = WindowStartupLocation.Manual; Left = r[0]; Top = r[1]; Width = r[2]; Height = r[3]; }
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _bar = new BarWindow(this); _panel = new SidePanelWindow(this);
        PlaceChildren(); _bar.Show(); UpdateStatus();
        Log.Write($"Started. Provider={Settings.Provider} OCR languages available: {SafeOcrLanguages()}");
        if (Arg("--verify-providers") is string providerDirectory)
        {
            await Diagnostics.ProviderVerification.RunAsync(providerDirectory);
            Close();
            return;
        }
        if (Arg("--verify-frame") is string directory)
        {
            try { await Diagnostics.FrameVerification.RunAsync(this, directory); }
            catch (Exception ex) { Log.Write("Frame verification failed: " + ex); }
            Close();
            return;
        }
        if (Arg("--verify-translation") is string translationDirectory)
        {
            try { await Diagnostics.TranslationVerification.RunAsync(this, translationDirectory); }
            catch (Exception ex) { Log.Write("Translation verification failed: " + ex.GetType().Name); }
            Close();
            return;
        }
        if (Arg("--autosnap") is string s)
        {
            await Task.Delay(TimeSpan.FromSeconds(double.TryParse(s, out var sec) ? sec : 3));
            await SnapAsync();
            if (Arg("--exit") != null) Close();
        }
    }

    static string SafeOcrLanguages() { try { return WindowsOcrEngine.Available; } catch (Exception ex) { return "error: " + ex.Message; } }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        ExcludedFromCapture = Native.SetWindowDisplayAffinity(_hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
        if (!Native.RegisterHotKey(_hwnd, HOTKEY_LOCK, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_L)) Log.Write("Ctrl+Alt+L is taken by another app.");
        if (!Native.RegisterHotKey(_hwnd, HOTKEY_SNAP, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_T)) Log.Write("Ctrl+Alt+T is taken by another app.");
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            if (wParam.ToInt32() == HOTKEY_LOCK) { ToggleLock(); handled = true; }
            else if (wParam.ToInt32() == HOTKEY_SNAP) { _ = SnapAsync(); handled = true; }
        }
        return IntPtr.Zero;
    }

    /// <summary>M2: capture the frame rect, OCR it, translate, show in the side panel. Never throws.</summary>
    public async Task SnapAsync()
    {
        if (_busy || _paused) { if (_paused) ShowStatus("Paused · press Resume to snap"); return; }
        _busy = true;
        LastSourceText = "";
        LastTranslatedText = "";
        var sw = Stopwatch.StartNew();
        try
        {
            ShowStatus("Snapping…");
            Native.GetWindowRect(_hwnd, out var r);
            const int inset = 4; // skip the gold border
            var img = _capturer.CaptureRect(r.Left + inset, r.Top + inset, r.Right - r.Left - 2 * inset, r.Bottom - r.Top - 2 * inset);
            var ocr = new WindowsOcrEngine(string.IsNullOrWhiteSpace(Settings.OcrLanguage) ? null : Settings.OcrLanguage);
            var lines = await ocr.RecognizeAsync(img.Bgra, img.Width, img.Height);
            var source = string.Join("\n", lines.Select(l => l.Text));
            LastSourceText = source;
            Log.Write($"Snap {img.Width}x{img.Height}px OCR[{ocr.LanguageTag}] {lines.Count} lines.");
            if (lines.Count == 0) { ShowPanel("", "No text found inside the frame."); return; }
            var tr = TranslatorFactory.Create(Settings);
            var translated = await tr.TranslateAsync(source, Settings.SourceLanguage, Settings.TargetLanguage);
            Log.Write($"Translated via {tr.Name} -> {Settings.TargetLanguage} in {sw.ElapsedMilliseconds} ms.");
            ShowPanel(translated, $"{tr.Name} · {lines.Count} lines · {sw.ElapsedMilliseconds} ms");
        }
        catch (TranslatorException ex) { Log.Write("Translator: " + ex.Message); ShowPanel("", ex.Message); }
        catch (Exception ex) { Log.Write("Snap failed: " + ex.GetType().Name); ShowPanel("", "Capture or recognition failed. Try a smaller area or check the OCR language in Settings."); }
        finally { _busy = false; UpdateStatus(); }
    }

    void ShowPanel(string text, string status)
    {
        LastTranslatedText = text;
        LastStatus = status;
        if (_panel == null) return;
        _panel.ShowResult(text, status);
        if (!_panel.IsVisible) _panel.Show();
        PlaceChildren();
    }

    public void OpenSettings(Window owner) => new SettingsWindow(Settings) { Owner = owner }.ShowDialog();

    void PlaceChildren()
    {
        if (_syncing) return;
        _syncing = true;
        if (_bar != null) { _bar.Left = Left; _bar.Top = Top - _bar.Height; _bar.Width = ActualWidth > 0 ? ActualWidth : Width; }
        if (_panel != null)
        {
            double w = ActualWidth > 0 ? ActualWidth : Width;
            _panel.Left = Left + w + 6 + _panel.Width > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ? Left - _panel.Width - 6 : Left + w + 6;
            _panel.Top = Top - (_bar?.Height ?? 0);
        }
        _syncing = false;
    }

    /// <summary>Called when the bar is dragged: move the frame under it.</summary>
    public void FollowBar()
    {
        if (_bar == null || _syncing) return;
        _syncing = true;
        Left = _bar.Left; Top = _bar.Top + _bar.Height;
        _syncing = false;
        PlaceChildren();
    }

    public void ToggleLock()
    {
        _locked = !_locked;
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        ex = _locked ? (ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED) : (ex & ~Native.WS_EX_TRANSPARENT);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
        ResizeMode = _locked ? ResizeMode.NoResize : ResizeMode.CanResize;
        _bar?.SetLockedVisual(_locked);
        UpdateStatus();
    }

    public void TogglePause() { _paused = !_paused; _bar?.SetPausedVisual(_paused); UpdateStatus(); }
    public void ShowStatus(string s) => StatusText.Text = s;

    void UpdateStatus() => StatusText.Text =
        (_locked ? "Locked (click-through)" : "Unlocked") + (_paused ? " · Paused" : " · Ctrl+Alt+T to translate") +
        (ExcludedFromCapture && (_bar?.ExcludedFromCapture ?? true) ? "" : " · capture-exclusion unavailable");
}
