using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HiveBabble.Capture;
using HiveBabble.Ocr;
using HiveBabble.Settings;
using HiveBabble.Translate;

namespace HiveBabble;

/// <summary>Live loop counters, read by the --verify-live self-test.</summary>
public sealed class LoopStats
{
    public int Captures, Skipped, Processed, Errors, CacheHits, CacheLookups;
    public long LastLatencyMs, LastOcrMs;
    public string LastHash = "";
}

/// <summary>The see-through frame (interior). The drag bar lives in an owned BarWindow glued above it,
/// so locking makes only this window click-through while the bar stays clickable.</summary>
public partial class FrameWindow : Window
{
    const int WM_HOTKEY = 0x0312, HOTKEY_LOCK = 0xB0B1, HOTKEY_SNAP = 0xB0B2;
    const uint MOD_CONTROL = 0x2, MOD_ALT = 0x1, MOD_NOREPEAT = 0x4000, VK_L = 0x4C, VK_T = 0x54;
    public const int CaptureInset = 10; // skip the gold glass border and glow

    public static readonly (string Code, string Label)[] TargetLanguages =
    {
        ("en", "English"), ("es", "Spanish"), ("ja", "Japanese"), ("fr", "French"), ("de", "German"),
        ("it", "Italian"), ("pt", "Portuguese"), ("zh", "Chinese"), ("ko", "Korean"), ("ru", "Russian"),
    };

    IntPtr _hwnd;
    bool _locked, _paused, _syncing, _busy, _diagnostic, _closed, _settingsOpen;
    int _frameRevision;
    BarWindow? _bar;
    SidePanelWindow? _panel;
    TrayIcon? _tray;
    readonly ScreenCapturer _capturer = new();
    readonly OverlayRenderer _overlay = new();
    readonly DispatcherTimer _loop = new(DispatcherPriority.Background);
    IOcrEngine? _ocr;
    string _lastHash = "";
    DateTime _retryAt = DateTime.MinValue;
    internal Func<AppSettings, ITranslator> CreateTranslator { get; set; } = TranslatorFactory.Create;
    internal bool IsProcessing => _busy;
    public bool ExcludedFromCapture { get; private set; }
    public AppSettings Settings { get; } = AppSettings.Load();
    public TranslationCache Cache { get; } = new();
    public LoopStats Stats { get; } = new();
    public string LastSourceText { get; private set; } = "";
    public string LastTranslatedText { get; private set; } = "";
    public string LastStatus { get; private set; } = "";
    public string? LastDetectedLanguage { get; private set; }
    public IReadOnlyList<OcrLine> LastLines { get; private set; } = Array.Empty<OcrLine>();
    public SidePanelWindow? ResultsPanel => _panel;
    public BarWindow? Bar => _bar;
    public TrayIcon? Tray => _tray;
    public System.Windows.Controls.Canvas Overlay => OverlayCanvas;
    public bool LoopRunning => _loop.IsEnabled;
    /// <summary>Diagnostics only: sees every captured frame that changed.</summary>
    public Action<CapturedImage>? CaptureTap { get; set; }

    public FrameWindow()
    {
        InitializeComponent();
        _diagnostic = Environment.GetCommandLineArgs().Any(a => a.StartsWith("--verify-", StringComparison.Ordinal));
        if (!ApplyCommandLineRect() && !_diagnostic) RestorePlacement();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        LocationChanged += (_, _) => { PlaceChildren(); ResetFrameHash(); _overlay.Clear(OverlayCanvas); };
        SizeChanged += (_, _) => { PlaceChildren(); ResetFrameHash(); _overlay.Clear(OverlayCanvas); };
        DpiChanged += (_, _) => { ResetFrameHash(); PlaceChildren(); };
        StateChanged += (_, _) =>
        {
            var v = WindowState == WindowState.Minimized ? Visibility.Hidden : Visibility.Visible;
            if (_bar != null) _bar.Visibility = v;
        };
        _loop.Tick += LoopTick;
        Closing += (_, _) => { _closed = true; ResetFrameHash(); SavePlacement(); };
        Closed += (_, _) =>
        {
            _loop.Stop(); _tray?.Dispose(); TranslatorHost.Stop();
            if (_hwnd != IntPtr.Zero) { Native.UnregisterHotKey(_hwnd, HOTKEY_LOCK); Native.UnregisterHotKey(_hwnd, HOTKEY_SNAP); }
        };
    }

    // --rect L,T,W,H (DIPs) positions the frame; --autosnap N snaps after N seconds; --exit closes after the snap. Used for testing.
    static string? Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : (i >= 0 ? "" : null);
    }

    bool ApplyCommandLineRect()
    {
        var r = Arg("--rect")?.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (r is not { Length: 4 }) return false;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = r[0]; Top = r[1]; Width = r[2]; Height = r[3];
        return true;
    }

    /// <summary>M4: restore the saved frame rect if it is still on a connected monitor (mixed-DPI safe: checked in DIPs against the virtual screen).</summary>
    void RestorePlacement()
    {
        var s = Settings;
        if (s.WindowLeft is not double l || s.WindowTop is not double t || s.WindowWidth is not double w || s.WindowHeight is not double h) return;
        if (w < MinWidth || h < MinHeight) return;
        // Monitors in WPF units (system DPI), so gaps between monitors and mixed layouts are handled.
        double k; using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) k = 96.0 / g.DpiX;
        var areas = System.Windows.Forms.Screen.AllScreens.Select(sc => new Rect(sc.WorkingArea.X * k, sc.WorkingArea.Y * k, sc.WorkingArea.Width * k, sc.WorkingArea.Height * k)).ToList();
        if (areas.Count == 0) return;
        var bar = new Rect(l, t - 60, w, 60);
        bool onMonitor = areas.Any(a => { var i = Rect.Intersect(a, bar); return !i.IsEmpty && i.Width >= 80 && i.Height >= 30; });
        if (!onMonitor)
        {
            var c0 = new Point(l + w / 2, t + h / 2);
            var a = areas.OrderBy(a => Math.Pow(Math.Clamp(c0.X, a.Left, a.Right) - c0.X, 2) + Math.Pow(Math.Clamp(c0.Y, a.Top, a.Bottom) - c0.Y, 2)).First();
            w = Math.Min(w, a.Width); h = Math.Min(h, Math.Max(MinHeight, a.Height - 60));
            l = Math.Clamp(l, a.Left, a.Right - w); t = Math.Clamp(t, a.Top + 60, Math.Max(a.Top + 60, a.Bottom - h));
            Log.Write("Saved window position was off-screen or in a monitor gap; moved onto the nearest monitor.");
        }
        WindowStartupLocation = WindowStartupLocation.Manual; Left = l; Top = t; Width = w; Height = h;
        Log.Write($"Restored window {l:0},{t:0} {w:0}x{h:0} (monitor match: {onMonitor}).");
    }

    void SavePlacement()
    {
        if (_diagnostic || WindowState != WindowState.Normal) return;
        Settings.WindowLeft = Left; Settings.WindowTop = Top; Settings.WindowWidth = ActualWidth; Settings.WindowHeight = ActualHeight;
        try { Settings.Save(); Log.Write($"Saved window {Left:0},{Top:0} {ActualWidth:0}x{ActualHeight:0}."); }
        catch (Exception ex) { Log.Write("Could not save window position: " + ex.GetType().Name); }
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _bar = new BarWindow(this); _panel = new SidePanelWindow(this);
        PlaceChildren(); _bar.Show(); UpdateStatus(); _bar.SetLanguageVisual(Settings.TargetLanguage); _bar.SetModeVisual(Settings.Mode);
        try { _tray = new TrayIcon(this); } catch (Exception ex) { Log.Write("Tray icon unavailable: " + ex.GetType().Name); }
        Log.Write($"Started. Provider={Settings.Provider} Mode={Settings.Mode} Target={Settings.TargetLanguage} Loop={Settings.LoopIntervalMs} ms OCR languages available: {SafeOcrLanguages()}");
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
        if (Arg("--verify-live") is string liveDirectory)
        {
            try { await Diagnostics.LiveVerification.RunAsync(this, liveDirectory); }
            catch (Exception ex) { Log.Write("Live verification failed: " + ex); }
            Close();
            return;
        }
        if (Arg("--verify-interactions") is string interactionDirectory)
        {
            try { await Diagnostics.InteractionVerification.RunAsync(this, interactionDirectory); }
            catch (Exception ex) { Log.Write("Interaction verification failed: " + ex.GetType().Name); }
            Close();
            return;
        }
        if (Settings.Provider == TranslatorProvider.Libre) await TranslatorHost.EnsureRunningAsync(Settings.LibreUrl, ShowStatus);
        if (_closed) return;
        _ = WarmUpTranslator();
        StartLoop();
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

    /// <summary>The local Argos models load lazily on the first request (~3 s). Pay that cost at startup, not on the first frame.</summary>
    public Task WarmUpTranslator()
    {
        if (Settings.Provider != TranslatorProvider.Libre) return Task.CompletedTask;
        var tr = TranslatorFactory.Create(Settings);
        string src = Settings.SourceLanguage, tgt = Settings.TargetLanguage;
        return Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            try { foreach (var w in new[] { "Hola, buenos d\u00edas.", "\u3053\u3093\u306b\u3061\u306f\u3001\u4e16\u754c\u3002", "\u4f60\u597d\uff0c\u4e16\u754c\u3002", "\uc548\ub155\ud558\uc138\uc694.", "Bonjour le monde.", "Guten Tag, Welt." }) await tr.TranslateAsync(w, src, tgt); Log.Write($"Translator warm-up {sw.ElapsedMilliseconds} ms."); }
            catch (TranslatorException ex) { Log.Write("Translator warm-up: " + ex.Message); }
            catch (Exception ex) { Log.Write("Translator warm-up failed: " + ex.GetType().Name); }
        });
    }

    /// <summary>Diagnostics: SHA-256 of what the capture sees right now inside the frame.</summary>
    public string CaptureHashNow()
    {
        Native.GetWindowRect(_hwnd, out var r);
        var img = _capturer.CaptureRect(r.Left + CaptureInset, r.Top + CaptureInset, r.Right - r.Left - 2 * CaptureInset, r.Bottom - r.Top - 2 * CaptureInset);
        return Convert.ToHexString(SHA256.HashData(img.Bgra));
    }

    // ---- M3 live loop ----
    public void StartLoop()
    {
        _loop.Interval = TimeSpan.FromMilliseconds(Math.Clamp(Settings.LoopIntervalMs, 100, 10000));
        _loop.Start();
        LoopTick(null, EventArgs.Empty);
    }
    public void StopLoop() { _loop.Stop(); _frameRevision++; if (_busy) _lastHash = ""; }
    /// <summary>Forces the next tick to re-OCR even if the pixels did not change (settings, language or mode change).</summary>
    public void ResetOcr() { _ocr = null; ResetFrameHash(); }
    public string OcrTag => _ocr?.LanguageTag ?? "";
    public void ResetFrameHash() { _frameRevision++; _lastHash = ""; _retryAt = DateTime.MinValue; }

    async void LoopTick(object? sender, EventArgs e)
    {
        if (_busy || _paused || _closed || _settingsOpen || !IsVisible || WindowState == WindowState.Minimized) return;
        await ProcessFrameAsync(force: false);
    }

    /// <summary>Hotkey / manual snap: always re-reads the frame.</summary>
    public async Task SnapAsync()
    {
        if (_paused) { ShowStatus("Paused \u00b7 press Resume to translate"); return; }
        if (_busy) return;
        await ProcessFrameAsync(force: true);
    }

    /// <summary>Capture, hash, OCR, translate (cached per line), render. Returns true if the frame was processed. Never throws.</summary>
    public async Task<bool> ProcessFrameAsync(bool force)
    {
        if (_busy || _paused || _closed || _settingsOpen || !IsVisible || _hwnd == IntPtr.Zero) return false;
        if (!force && DateTime.UtcNow < _retryAt) return false;
        _busy = true;
        int revision = _frameRevision;
        bool IsCurrent() => revision == _frameRevision && !_closed && !_paused && !_settingsOpen && IsVisible;
        var sw = Stopwatch.StartNew();
        try
        {
            Native.GetWindowRect(_hwnd, out var r);
            var img = _capturer.CaptureRect(r.Left + CaptureInset, r.Top + CaptureInset, r.Right - r.Left - 2 * CaptureInset, r.Bottom - r.Top - 2 * CaptureInset);
            string hash = Convert.ToHexString(SHA256.HashData(img.Bgra));
            Stats.Captures++; Stats.LastHash = hash;
            if (!force && hash == _lastHash) { Stats.Skipped++; return false; }
            _lastHash = hash;
            CaptureTap?.Invoke(img);

            var ocr = _ocr ??= (string.IsNullOrWhiteSpace(Settings.OcrLanguage) || Settings.OcrLanguage.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)) ? new AutoOcrEngine() : new WindowsOcrEngine(Settings.OcrLanguage);
            var lines = await ocr.RecognizeAsync(img.Bgra, img.Width, img.Height);
            if (!IsCurrent()) return false;
            Stats.LastOcrMs = sw.ElapsedMilliseconds;
            LastLines = lines;
            LastSourceText = string.Join("\n", lines.Select(l => l.Text));
            if (lines.Count == 0)
            {
                _overlay.Clear(OverlayCanvas);
                LastTranslatedText = ""; LastStatus = "No text found inside the frame.";
                if (Settings.Mode != DisplayMode.Overlay) ShowPanel("", LastStatus); else ShowStatus(LastStatus);
                Stats.Processed++;
                return true;
            }
            var tr = CreateTranslator(Settings);
            var (translated, hits) = await TranslateLinesAsync(tr, lines);
            if (!IsCurrent()) return false;
            LastDetectedLanguage = (tr as IDetectsLanguage)?.LastDetectedLanguage ?? (hits == lines.Count ? LastDetectedLanguage : null);
            long ms = sw.ElapsedMilliseconds;
            Stats.Processed++; Stats.CacheHits += hits; Stats.CacheLookups += lines.Count; Stats.LastLatencyMs = ms;
            string detected = LastDetectedLanguage != null ? $" detected={LastDetectedLanguage}" : "";
            Log.Write($"Frame {img.Width}x{img.Height}px OCR[{ocr.LanguageTag}] {lines.Count} lines {Stats.LastOcrMs} ms; {tr.Name} {Settings.SourceLanguage}->{Settings.TargetLanguage}{detected}; cache {hits}/{lines.Count}; total {ms} ms.");
            string status = $"{tr.Name} \u00b7 {lines.Count} lines \u00b7 {hits} cached \u00b7 {ms} ms";
            LastTranslatedText = string.Join("\n", translated);
            LastStatus = status;
            if (Settings.Mode != DisplayMode.Side)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                _overlay.Render(OverlayCanvas, lines, translated, CaptureInset, dpi.DpiScaleX, dpi.DpiScaleY);
            }
            else _overlay.Clear(OverlayCanvas);
            if (Settings.Mode != DisplayMode.Overlay) ShowPanel(LastTranslatedText, status);
            else { _panel?.Hide(); ShowStatus(status); }
            return true;
        }
        catch (TranslatorException ex) { if (!IsCurrent()) return false; Fail("Translator: " + ex.Message, ex.Message); return true; }
        catch (Exception ex) { if (!IsCurrent()) return false; Fail("Frame failed: " + ex.GetType().Name, "Capture or recognition failed. Try a smaller area or check the OCR language in Settings."); return true; }
        finally { _busy = false; }
    }

    void Fail(string log, string message)
    {
        Stats.Errors++;
        _lastHash = "";
        _retryAt = DateTime.UtcNow.AddSeconds(5); // moving backgrounds also respect provider cooldown
        Log.Write(log);
        _overlay.Clear(OverlayCanvas);
        LastTranslatedText = "";
        ShowPanel("", message); // errors always show in the panel so they are readable in every mode
        ShowStatus(message);
    }

    /// <summary>M4: per-line cache. Only uncached lines are sent, joined in one request (one line per row).</summary>
    async Task<(string[] Lines, int Hits)> TranslateLinesAsync(ITranslator tr, IReadOnlyList<OcrLine> lines)
    {
        int revision = _frameRevision;
        string src = Settings.SourceLanguage, tgt = Settings.TargetLanguage, provider = tr.Name;
        var result = new string[lines.Count];
        var miss = new List<int>();
        for (int i = 0; i < lines.Count; i++)
            if (Cache.TryGet(provider, src, tgt, lines[i].Text, out var hit)) result[i] = hit!; else miss.Add(i);
        int hits = lines.Count - miss.Count;
        if (hits > 0) Log.Write($"Cache hit: {hits}/{lines.Count} lines ({provider} {src}->{tgt}).");
        if (miss.Count > 0)
        {
            string joined = string.Join("\n", miss.Select(i => lines[i].Text));
            var parts = (await tr.TranslateAsync(joined, src, tgt)).Replace("\r\n", "\n").Split('\n');
            if (parts.Length != miss.Count)
            {
                parts = new string[miss.Count];
                for (int j = 0; j < miss.Count; j++) parts[j] = await tr.TranslateAsync(lines[miss[j]].Text, src, tgt);
            }
            for (int j = 0; j < miss.Count; j++)
            {
                result[miss[j]] = parts[j].Trim();
                if (revision == _frameRevision) Cache.Set(provider, src, tgt, lines[miss[j]].Text, result[miss[j]]);
            }
        }
        return (result, hits);
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

    // ---- M4 controls ----
    public void SetTargetLanguage(string code)
    {
        Settings.TargetLanguage = code; SaveSettingsQuietly();
        _bar?.SetLanguageVisual(code); ResetFrameHash();
        _overlay.Clear(OverlayCanvas);
        ShowStatus("Target: " + code);
        if (!_loop.IsEnabled && !_diagnostic) _ = SnapAsync();
    }

    public void SetMode(DisplayMode mode)
    {
        Settings.Mode = mode; SaveSettingsQuietly();
        _bar?.SetModeVisual(mode); ResetFrameHash();
        if (mode == DisplayMode.Side) _overlay.Clear(OverlayCanvas);
        if (mode == DisplayMode.Overlay) _panel?.Hide();
        ShowStatus("Mode: " + mode);
    }

    void SaveSettingsQuietly() { if (_diagnostic) return; try { Settings.Save(); } catch (Exception ex) { Log.Write("Settings save failed: " + ex.GetType().Name); } }

    public void OpenSettings(Window owner)
    {
        _settingsOpen = true;
        ResetFrameHash();
        try { if (new SettingsWindow(Settings) { Owner = owner }.ShowDialog() == true) ApplySettingsChanges(); }
        finally { _settingsOpen = false; ResetFrameHash(); }
    }

    public void ApplySettingsChanges()
    {
        _ocr = null; Cache.Clear(); ResetFrameHash(); _overlay.Clear(OverlayCanvas);
        _bar?.SetLanguageVisual(Settings.TargetLanguage);
        _bar?.SetModeVisual(Settings.Mode);
        if (_loop.IsEnabled) _loop.Interval = TimeSpan.FromMilliseconds(Math.Clamp(Settings.LoopIntervalMs, 100, 10000));
    }

    public void ToggleVisible()
    {
        if (IsVisible)
        {
            StopLoop(); _bar?.Hide(); _panel?.Hide(); Hide();
        }
        else
        {
            Show(); _bar?.Show(); PlaceChildren(); ResetFrameHash(); StartLoop();
        }
    }

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

    public void TogglePause() { _paused = !_paused; _bar?.SetPausedVisual(_paused); ResetFrameHash(); UpdateStatus(); }
    public void ShowStatus(string s) => StatusText.Text = s;

    void UpdateStatus() => StatusText.Text =
        (_locked ? "Locked (click-through)" : "Unlocked") + (_paused ? " \u00b7 Paused" : " \u00b7 Live") +
        (ExcludedFromCapture && (_bar?.ExcludedFromCapture ?? true) ? "" : " \u00b7 capture-exclusion unavailable");
}
