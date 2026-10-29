using System;
using System.Windows;
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
            ExcludedFromCapture = Native.SetWindowDisplayAffinity(h, Native.WDA_EXCLUDEFROMCAPTURE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
        };
        LocationChanged += (_, _) => _frame.FollowBar();
    }
    public void SetLockedVisual(bool locked) => LockButton.Content = locked ? "白 Unlock" : "箔 Lock";
    public void SetPausedVisual(bool paused) => PauseButton.Content = paused ? "Resume" : "Pause";
    void Bar_MouseLeftButtonDown(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void Lock_Click(object s, RoutedEventArgs e) => _frame.ToggleLock();
    void Pause_Click(object s, RoutedEventArgs e) => _frame.TogglePause();
    void Placeholder_Click(object s, RoutedEventArgs e) => _frame.ShowStatus("Coming in M4");
    void Settings_Click(object s, RoutedEventArgs e) => _frame.OpenSettings(this);
    void Close_Click(object s, RoutedEventArgs e) => _frame.Close();
}