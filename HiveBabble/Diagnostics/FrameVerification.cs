using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HiveBabble.Capture;

namespace HiveBabble.Diagnostics;

/// <summary>Opt-in, in-process M1 diagnostics. The preview is a WPF render, not a desktop screenshot.</summary>
public static class FrameVerification
{
    const int ExStyle = -20, Transparent = 0x20, Topmost = 0x8;
    const uint ExcludeFromCapture = 0x11;
    static readonly Color TestColor = Color.FromRgb(24, 74, 99);
    sealed record Check(string Name, bool Passed, string Detail);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();

    public static async Task RunAsync(FrameWindow frame, string outputDirectory)
    {
        if (!frame.Dispatcher.CheckAccess())
        {
            await frame.Dispatcher.InvokeAsync(() => RunAsync(frame, outputDirectory)).Task.Unwrap();
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        var checks = new List<Check>();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        var hwnd = new WindowInteropHelper(frame).Handle;
        double oldLeft = frame.Left, oldTop = frame.Top, oldWidth = frame.Width, oldHeight = frame.Height;
        bool initiallyLocked = HasStyle(hwnd, Transparent);
        Process? backdrop = null;
        Canvas? canvas = null;
        Border? marker = null;
        DpiScale dpi = VisualTreeHelper.GetDpi(frame);

        void CheckResult(string name, bool passed, string detail) => checks.Add(new(name, passed, detail));
        try
        {
            var bar = frame.OwnedWindows.OfType<BarWindow>().SingleOrDefault()
                ?? throw new InvalidOperationException("Run verification after the toolbar has been shown.");
            var barHwnd = new WindowInteropHelper(bar).Handle;
            if (initiallyLocked) frame.ToggleLock();

            // Keep the test on the primary working area. This does not claim mixed-monitor validation.
            Rect work = SystemParameters.WorkArea;
            frame.Width = Math.Max(frame.MinWidth, Math.Min(720, work.Width - 180));
            frame.Height = Math.Max(frame.MinHeight, Math.Min(320, work.Height - bar.Height - 160));
            frame.Left = work.Left + 40;
            frame.Top = work.Top + bar.Height + 40;
            await RenderAsync(frame);
            RECT initial = RectOf(hwnd);

            frame.Left += 32;
            frame.Top += 24;
            await RenderAsync(frame);
            RECT moved = RectOf(hwnd);
            CheckResult("programmatic_move", moved.Left > initial.Left && moved.Top > initial.Top,
                $"Physical position {initial.Left},{initial.Top} -> {moved.Left},{moved.Top}. Mouse dragging is a separate manual check.");

            bar.Left += 24;
            bar.Top += 16;
            await RenderAsync(frame);
            RECT followed = RectOf(hwnd), barRect = RectOf(barHwnd);
            CheckResult("toolbar_move_follows_frame", followed.Left > moved.Left && followed.Top > moved.Top &&
                Math.Abs(followed.Left - barRect.Left) <= 2 && Math.Abs(followed.Top - barRect.Bottom) <= 2,
                $"Frame {Describe(followed)}; toolbar {Describe(barRect)}. Exercises the same position coupling used by DragMove.");

            frame.Width += 48;
            frame.Height += 32;
            await RenderAsync(frame);
            RECT resized = RectOf(hwnd);
            CheckResult("programmatic_resize", resized.Width > followed.Width && resized.Height > followed.Height,
                $"Physical size {followed.Width}x{followed.Height} -> {resized.Width}x{resized.Height}.");
            int edgeHit = HitTest(hwnd, resized.Right - 3, resized.Bottom - 3);
            CheckResult("resize_corner_hit_test", edgeHit == 17,
                $"WM_NCHITTEST at bottom-right returned {edgeHit}; expected HTBOTTOMRIGHT (17).");

            backdrop = StartBackdrop(new Rect(frame.Left - 32, bar.Top - 32,
                frame.ActualWidth + 64, frame.ActualHeight + bar.ActualHeight + 64));
            IntPtr backdropHwnd = IntPtr.Zero;
            for (int attempt = 0; attempt < 50 && backdropHwnd == IntPtr.Zero; attempt++)
            {
                await Task.Delay(100);
                backdrop.Refresh();
                if (backdrop.HasExited) throw new InvalidOperationException($"Diagnostic background exited with code {backdrop.ExitCode}.");
                backdropHwnd = backdrop.MainWindowHandle;
            }
            if (backdropHwnd == IntPtr.Zero) throw new InvalidOperationException("Diagnostic background did not create a window within five seconds.");
            // Activation may be refused when diagnostics were started from a background shell.
            // Place all three test windows explicitly, with the frame and toolbar above the backdrop.
            bool ordered = SetWindowPos(backdropHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13) &&
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13) &&
                SetWindowPos(barHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            await RenderAsync(frame);
            CheckResult("topmost_styles", ordered && HasStyle(hwnd, Topmost) && HasStyle(barHwnd, Topmost) && HasStyle(backdropHwnd, Topmost),
                "Frame and toolbar have WS_EX_TOPMOST and were placed above a temporary topmost diagnostic background.");
            CheckResult("capture_affinity", GetWindowDisplayAffinity(hwnd, out uint fa) && fa == ExcludeFromCapture &&
                GetWindowDisplayAffinity(barHwnd, out uint ba) && ba == ExcludeFromCapture,
                "Both frame and toolbar must report WDA_EXCLUDEFROMCAPTURE; actual pixels are tested below.");
            CheckResult("background_capture_allowed", GetWindowDisplayAffinity(backdropHwnd, out uint da) && da == 0,
                "The background runs in a separate process and has no capture exclusion.");

            frame.ToggleLock();
            await RenderAsync(frame);
            RECT testRect = RectOf(hwnd);
            POINT center = new(testRect.Left + testRect.Width / 2, testRect.Top + testRect.Height / 2);
            IntPtr centerWindow = WindowFromPoint(center);
            CheckResult("locked_click_through", HasStyle(hwnd, Transparent) && centerWindow == backdropHwnd,
                $"WS_EX_TRANSPARENT={HasStyle(hwnd, Transparent)}; center HWND={centerWindow.ToInt64():X}, background HWND={backdropHwnd.ToInt64():X}.");
            barRect = RectOf(barHwnd);
            IntPtr barHit = WindowFromPoint(new POINT(barRect.Left + barRect.Width / 2, barRect.Top + barRect.Height / 2));
            CheckResult("toolbar_remains_interactive", !HasStyle(barHwnd, Transparent) && barHit == barHwnd,
                $"Toolbar WS_EX_TRANSPARENT={HasStyle(barHwnd, Transparent)}; hit HWND={barHit.ToInt64():X}, toolbar HWND={barHwnd.ToInt64():X}.");

            var capturer = new ScreenCapturer();
            CapturedImage before = capturer.CaptureRect(testRect.Left, testRect.Top, testRect.Width, testRect.Height);
            SaveCapture(before, Path.Combine(outputDirectory, "capture-before-marker.png"));
            canvas = frame.FindName("OverlayCanvas") as Canvas
                ?? throw new InvalidOperationException("OverlayCanvas was not found.");
            marker = new Border
            {
                Width = 160, Height = 84, Background = Brushes.Magenta, IsHitTestVisible = false,
                Child = new TextBlock { Text = "EXCLUSION PROBE", Foreground = Brushes.Black, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Canvas.SetLeft(marker, Math.Max(0, frame.ActualWidth / 2 - marker.Width / 2));
            Canvas.SetTop(marker, Math.Max(0, frame.ActualHeight / 2 - marker.Height / 2));
            canvas.Children.Add(marker);
            await RenderAsync(frame);

            CapturedImage after = capturer.CaptureRect(testRect.Left, testRect.Top, testRect.Width, testRect.Height);
            SaveCapture(after, Path.Combine(outputDirectory, "capture-with-marker.png"));
            int changedPixels = 0, magentaPixels = 0;
            for (int i = 0; i < after.Bgra.Length; i += 4)
            {
                if (before.Bgra[i] != after.Bgra[i] || before.Bgra[i + 1] != after.Bgra[i + 1] || before.Bgra[i + 2] != after.Bgra[i + 2]) changedPixels++;
                if (after.Bgra[i] > 240 && after.Bgra[i + 1] < 20 && after.Bgra[i + 2] > 240) magentaPixels++;
            }
            bool centerMatches = RegionMatches(after, after.Width / 2, after.Height / 2, TestColor);
            CheckResult("capture_shows_underlying_pixels", centerMatches,
                $"Center sample must match synthetic background RGB {TestColor.R},{TestColor.G},{TestColor.B} despite the opaque magenta overlay.");
            CheckResult("overlay_excluded_from_capture", changedPixels == 0 && magentaPixels == 0 && centerMatches,
                $"Before/after changed pixels={changedPixels}; magenta pixels={magentaPixels}. The marker is visible in the separate WPF preview.");

            SavePreview(frame, bar, after, Path.Combine(outputDirectory, "frame-render-preview.png"));
            frame.ToggleLock();
            await RenderAsync(frame);
            CheckResult("unlock_restores_interaction", !HasStyle(hwnd, Transparent) && frame.ResizeMode == ResizeMode.CanResize,
                "Unlock removes WS_EX_TRANSPARENT and restores ResizeMode.CanResize.");
        }
        catch (Exception ex)
        {
            CheckResult("diagnostic_completed", false, ex.ToString());
        }
        finally
        {
            if (marker != null) canvas?.Children.Remove(marker);
            if (HasStyle(hwnd, Transparent) != initiallyLocked) frame.ToggleLock();
            if (backdrop != null)
            {
                try
                {
                    if (!backdrop.HasExited)
                    {
                        backdrop.CloseMainWindow();
                        Task exited = backdrop.WaitForExitAsync();
                        if (await Task.WhenAny(exited, Task.Delay(1500)) != exited && !backdrop.HasExited) backdrop.Kill();
                    }
                }
                catch (Exception ex) { CheckResult("background_cleanup", false, ex.Message); }
                finally { backdrop.Dispose(); }
            }
            frame.Width = oldWidth; frame.Height = oldHeight; frame.Left = oldLeft; frame.Top = oldTop;
            await RenderAsync(frame);
            var report = new
            {
                startedUtc = started,
                completedUtc = DateTimeOffset.UtcNow,
                allPassed = checks.Count > 0 && checks.All(c => c.Passed),
                dpiScaleX = dpi.DpiScaleX, dpiScaleY = dpi.DpiScaleY,
                checks,
                limitations = new[] { "Programmatic native-window checks; no physical mouse input was synthesized.",
                    "Browser, PDF, borderless game, and mixed-monitor/scaling interaction require separate live checks.",
                    "frame-render-preview.png is a labeled WPF render composite. capture-*.png are actual ScreenCapturer output." }
            };
            File.WriteAllText(Path.Combine(outputDirectory, "frame-verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    static Process StartBackdrop(Rect bounds)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not find the running executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(FrameVerification).Assembly.Location);
        start.ArgumentList.Add("--diagnostic-backdrop");
        start.ArgumentList.Add(string.Join(",", new[] { bounds.Left, bounds.Top, bounds.Width, bounds.Height }.Select(v => v.ToString(CultureInfo.InvariantCulture))));
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the diagnostic background.");
    }

    public static Window CreateBackdrop(Rect bounds)
    {
        var content = new Grid { Background = new SolidColorBrush(TestColor) };
        var text = new StackPanel { Margin = new Thickness(64, 76, 32, 32), VerticalAlignment = VerticalAlignment.Top };
        text.Children.Add(new TextBlock { Text = "Hive-Babble · M1 verification", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        text.Children.Add(new TextBlock { Text = "This blue surface belongs to a separate window beneath the frame.", FontSize = 15, Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.White });
        content.Children.Add(text);
        return new Window
        {
            Title = "Hive-Babble diagnostic background", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true, WindowStartupLocation = WindowStartupLocation.Manual, Topmost = true,
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, Content = content
        };
    }

    static async Task RenderAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        await Task.Delay(180);
        DwmFlush();
    }
    static bool HasStyle(IntPtr hwnd, int style) => (GetWindowLong(hwnd, ExStyle) & style) != 0;
    static RECT RectOf(IntPtr hwnd) => GetWindowRect(hwnd, out var rect) ? rect : throw new InvalidOperationException("GetWindowRect failed.");
    static string Describe(RECT rect) => $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}";
    static int HitTest(IntPtr hwnd, int x, int y) => (int)SendMessage(hwnd, 0x84, IntPtr.Zero, new IntPtr(unchecked((y & 0xffff) << 16 | (x & 0xffff))));
    static bool RegionMatches(CapturedImage image, int x, int y, Color color)
    {
        for (int yy = y - 4; yy <= y + 4; yy++)
            for (int xx = x - 4; xx <= x + 4; xx++)
            {
                int i = (yy * image.Width + xx) * 4;
                if (Math.Abs(image.Bgra[i] - color.B) > 2 || Math.Abs(image.Bgra[i + 1] - color.G) > 2 || Math.Abs(image.Bgra[i + 2] - color.R) > 2) return false;
            }
        return true;
    }
    static BitmapSource Bitmap(CapturedImage capture) => BitmapSource.Create(capture.Width, capture.Height, 96, 96, PixelFormats.Bgra32, null, capture.Bgra, capture.Width * 4);
    static void SaveCapture(CapturedImage capture, string path) => SavePng(Bitmap(capture), path);
    static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    static void SavePreview(FrameWindow frame, BarWindow bar, CapturedImage capture, string path)
    {
        const double margin = 24, header = 54, footer = 42;
        double width = frame.ActualWidth + margin * 2, height = header + bar.ActualHeight + frame.ActualHeight + footer;
        var visual = new DrawingVisual();
        using (DrawingContext draw = visual.RenderOpen())
        {
            draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 21, 27)), null, new Rect(0, 0, width, height));
            DrawText(draw, "M1 diagnostic · WPF render preview (not a desktop screenshot)", new Point(margin, 18), 13, Brushes.White);
            var barBounds = new Rect(margin, header, bar.ActualWidth, bar.ActualHeight);
            var frameBounds = new Rect(margin, header + bar.ActualHeight, frame.ActualWidth, frame.ActualHeight);
            draw.DrawImage(Bitmap(capture), frameBounds);
            draw.DrawRectangle(new VisualBrush(bar), null, barBounds);
            draw.DrawRectangle(new VisualBrush(frame), null, frameBounds);
            DrawText(draw, "The magenta probe must be absent from capture-with-marker.png.", new Point(margin, height - 27), 12, Brushes.LightGray);
        }
        var render = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        SavePng(render, path);
    }
    static void DrawText(DrawingContext draw, string text, Point position, double size, Brush color) =>
        draw.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, 1), position);
}
