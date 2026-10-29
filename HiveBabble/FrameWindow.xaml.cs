using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using HiveBabble.Settings;

namespace HiveBabble;

public partial class FrameWindow : Window
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_LAYERED = 0x80000;
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_UNLOCK = 0xB0B1;
    const uint MOD_CONTROL = 0x2, MOD_ALT = 0x1, VK_L = 0x4C;

    [DllImport("user32.dll", SetLastError = true)] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr h, int id);

    IntPtr _hwnd;
    bool _locked, _paused;
    public bool ExcludedFromCapture { get; private set; }
    public AppSettings Settings { get; } = new();

    public FrameWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => { if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HOTKEY_UNLOCK); };
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        ExcludedFromCapture = SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE);
        RegisterHotKey(_hwnd, HOTKEY_UNLOCK, MOD_CONTROL | MOD_ALT, VK_L);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        UpdateStatus();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_UNLOCK) { SetLocked(!_locked); handled = true; }
        return IntPtr.Zero;
    }

    /// <summary>Locked = whole window is click-through (WS_EX_TRANSPARENT). Ctrl+Alt+L toggles back.</summary>
    public void SetLocked(bool locked)
    {
        _locked = locked;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        ex = locked ? (ex | WS_EX_TRANSPARENT | WS_EX_LAYERED) : (ex & ~WS_EX_TRANSPARENT);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
        LockButton.Content = locked ? "🔒 Locked" : "🔓 Lock";
        UpdateStatus();
    }

    void UpdateStatus() => StatusText.Text =
        (_locked ? "Locked · Ctrl+Alt+L to unlock" : "Unlocked") +
        (_paused ? " · Paused" : "") +
        (ExcludedFromCapture ? "" : " · capture-exclusion unavailable");

    void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_locked) DragMove();
    }

    void Lock_Click(object sender, RoutedEventArgs e) => SetLocked(!_locked);
    void Pause_Click(object sender, RoutedEventArgs e) { _paused = !_paused; PauseButton.Content = _paused ? "Resume" : "Pause"; UpdateStatus(); }
    void Placeholder_Click(object sender, RoutedEventArgs e) => StatusText.Text = "Coming in M4";
    void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}