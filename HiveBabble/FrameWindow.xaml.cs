using System;
using System.Windows;
using System.Windows.Interop;
using HiveBabble.Settings;

namespace HiveBabble;

/// <summary>The see-through frame (interior). The drag bar lives in an owned BarWindow glued above it,
/// so locking makes only this window click-through while the bar stays clickable.</summary>
public partial class FrameWindow : Window
{
    const int WM_HOTKEY = 0x0312, HOTKEY_LOCK = 0xB0B1;
    const uint MOD_CONTROL = 0x2, MOD_ALT = 0x1, VK_L = 0x4C;

    IntPtr _hwnd;
    bool _locked, _paused, _syncing;
    BarWindow? _bar;
    public bool ExcludedFromCapture { get; private set; }
    public AppSettings Settings { get; } = new();

    public FrameWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { _bar = new BarWindow(this); PlaceBar(); _bar.Show(); UpdateStatus(); };
        LocationChanged += (_, _) => PlaceBar();
        SizeChanged += (_, _) => PlaceBar();
        StateChanged += (_, _) => { if (_bar != null) _bar.Visibility = WindowState == WindowState.Minimized ? Visibility.Hidden : Visibility.Visible; };
        Closed += (_, _) => { if (_hwnd != IntPtr.Zero) Native.UnregisterHotKey(_hwnd, HOTKEY_LOCK); };
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        ExcludedFromCapture = Native.SetWindowDisplayAffinity(_hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
        Native.RegisterHotKey(_hwnd, HOTKEY_LOCK, MOD_CONTROL | MOD_ALT, VK_L);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        // Leave room above for the bar on first show.
        if (Top < 34) Top = 34;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_LOCK) { ToggleLock(); handled = true; }
        return IntPtr.Zero;
    }

    void PlaceBar()
    {
        if (_bar == null || _syncing) return;
        _syncing = true;
        _bar.Left = Left; _bar.Top = Top - _bar.Height; _bar.Width = ActualWidth > 0 ? ActualWidth : Width;
        _syncing = false;
    }

    /// <summary>Called when the bar is dragged: move the frame under it.</summary>
    public void FollowBar()
    {
        if (_bar == null || _syncing) return;
        _syncing = true;
        Left = _bar.Left; Top = _bar.Top + _bar.Height;
        _syncing = false;
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
        (_locked ? "Locked (click-through)" : "Unlocked") + (_paused ? " · Paused" : "") +
        (ExcludedFromCapture && (_bar?.ExcludedFromCapture ?? true) ? "" : " · capture-exclusion unavailable");
}