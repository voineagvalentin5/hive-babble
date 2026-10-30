using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
namespace HiveBabble;
public partial class BarWindow : Window
{
    readonly FrameWindow _frame;
    public bool ExcludedFromCapture { get; private set; }
    public BarWindow(FrameWindow frame)
    {
        _frame = frame; Owner = frame;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            ExcludedFromCapture = Native.SetWindowDisplayAffinity(h, Native.WDA_EXCLUDEFROMCAPTURE); Glass.Enable(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
        };
        LocationChanged += (_, _) => _frame.FollowBar();
    }
    public void SetLockedVisual(bool locked) => LockButton.Content = locked ? "Unlock" : "Lock";
    public void SetPausedVisual(bool paused) => PauseButton.Content = paused ? "Resume" : "Pause";
    void Bar_MouseLeftButtonDown(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void Lock_Click(object s, RoutedEventArgs e) => _frame.ToggleLock();
    void Pause_Click(object s, RoutedEventArgs e) => _frame.TogglePause();
    public void SetLanguageVisual(string code) => LangButton.Content = code.ToUpperInvariant() + " \u25BE";
    public void SetModeVisual(HiveBabble.Settings.DisplayMode mode) => ModeButton.Content = mode + " \u25BE";
    void Lang_Click(object s, RoutedEventArgs e) => ShowMenu((FrameworkElement)s,
        FrameWindow.TargetLanguages.Select(l => (l.Code, $"{l.Label} ({l.Code})")), _frame.Settings.TargetLanguage, _frame.SetTargetLanguage);
    void Mode_Click(object s, RoutedEventArgs e) => ShowMenu((FrameworkElement)s,
        new[] { ("Overlay", "Overlay on the text"), ("Side", "Side panel"), ("Both", "Overlay + side panel") }, _frame.Settings.Mode.ToString(),
        v => _frame.SetMode(Enum.Parse<HiveBabble.Settings.DisplayMode>(v)));
    static void ShowMenu(FrameworkElement anchor, IEnumerable<(string Value, string Label)> items, string current, Action<string> pick)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (value, label) in items)
        {
            var mi = new MenuItem { Header = label, IsCheckable = true, IsChecked = string.Equals(value, current, StringComparison.OrdinalIgnoreCase) };
            mi.Click += (_, _) => pick(value);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }
    void Settings_Click(object s, RoutedEventArgs e) => _frame.OpenSettings(this);
    void Close_Click(object s, RoutedEventArgs e) => _frame.Close();
}
