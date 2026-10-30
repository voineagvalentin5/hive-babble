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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HiveBabble.Capture;
using HiveBabble.Settings;

namespace HiveBabble.Diagnostics;

/// <summary>Explicit M2 checks using only synthetic screen content and the local translator.</summary>
public static class TranslationVerification
{
    sealed record Check(string Name, bool Passed, string Detail);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();

    public static async Task RunAsync(FrameWindow frame, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<Check>();
        var settings = frame.Settings;
        var oldProvider = settings.Provider;
        string oldUrl = settings.LibreUrl, oldSource = settings.SourceLanguage, oldTarget = settings.TargetLanguage;
        string? oldKey = settings.ProtectedApiKey;
        double oldLeft = frame.Left, oldTop = frame.Top, oldWidth = frame.Width, oldHeight = frame.Height;
        Process? scene = null;
        string source = "", translation = "";
        long elapsedMs = 0;
        DateTimeOffset started = DateTimeOffset.UtcNow;
        void CheckResult(string name, bool pass, string detail) => checks.Add(new(name, pass, detail));
        try
        {
            settings.Provider = TranslatorProvider.Libre;
            settings.LibreUrl = "http://127.0.0.1:5000";
            settings.SourceLanguage = "es";
            settings.TargetLanguage = "en";
            settings.ProtectedApiKey = null;
            frame.Width = 760; frame.Height = 320; frame.Left = 100; frame.Top = 190;
            var bar = frame.OwnedWindows.OfType<BarWindow>().Single();
            var hwnd = new WindowInteropHelper(frame).Handle;
            scene = SampleScene.Start();
            IntPtr sampleHwnd = IntPtr.Zero;
            for (int i = 0; i < 100 && sampleHwnd == IntPtr.Zero; i++)
            {
                await Task.Delay(100);
                scene.Refresh();
                if (scene.HasExited) throw new InvalidOperationException($"Sample scene exited with code {scene.ExitCode}.");
                sampleHwnd = scene.MainWindowHandle;
            }
            if (sampleHwnd == IntPtr.Zero) throw new InvalidOperationException("Sample scene did not open.");
            SetWindowPos(sampleHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            SetWindowPos(new WindowInteropHelper(bar).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            await RenderAsync(frame);

            var stopwatch = Stopwatch.StartNew();
            await frame.SnapAsync();
            elapsedMs = stopwatch.ElapsedMilliseconds;
            source = frame.LastSourceText;
            translation = frame.LastTranslatedText;
            bool recognizedSample = source.Contains("gato", StringComparison.OrdinalIgnoreCase) && source.Contains("casa", StringComparison.OrdinalIgnoreCase);
            CheckResult("screen_capture_and_ocr", recognizedSample, "The captured Spanish sample must contain gato and casa.");
            CheckResult("local_translation", translation.Contains("cat", StringComparison.OrdinalIgnoreCase) && translation.Contains("house", StringComparison.OrdinalIgnoreCase),
                "The real local translation must contain cat and house.");
            var panel = frame.ResultsPanel;
            CheckResult("side_panel_visible", panel?.IsVisible == true && !string.IsNullOrWhiteSpace(translation), "The translated result is displayed in the side panel.");
            if (panel != null)
            {
                string? previousText = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                bool clipboardWasEmpty = Clipboard.GetDataObject()?.GetFormats().Length is null or 0;
                if (previousText != null || clipboardWasEmpty)
                {
                    try
                    {
                        bool copied = panel.CopyResult();
                        CheckResult("copy_roundtrip", copied && Clipboard.ContainsText() && Clipboard.GetText() == translation,
                            "Copy uses the actual side-panel action; previous clipboard text is restored immediately.");
                    }
                    finally { if (previousText != null) Clipboard.SetText(previousText); else Clipboard.Clear(); }
                }
                else CheckResult("copy_roundtrip", false, "Skipped to preserve non-text clipboard content; perform the Copy check manually.");
            }
            if (recognizedSample && panel != null)
            {
                await RenderAsync(frame);
                GetWindowRect(hwnd, out var rect);
                var capture = new ScreenCapturer().CaptureRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                SaveCapture(capture, Path.Combine(outputDirectory, "sample-capture.png"));
                SavePreview(frame, bar, panel, capture, Path.Combine(outputDirectory, "translation-render-preview.png"));
            }

            settings.Provider = TranslatorProvider.DeepL;
            settings.ProtectedApiKey = null;
            await frame.SnapAsync();
            CheckResult("missing_key_message", frame.LastStatus.Contains("key", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(frame.LastTranslatedText),
                "Selecting DeepL without a key must show an actionable message and keep the app alive.");
            settings.Provider = TranslatorProvider.Libre;
            // Restore a successful view before returning to the caller.
            await frame.SnapAsync();
        }
        catch (Exception ex) { CheckResult("diagnostic_completed", false, ex.GetType().Name + ": " + ex.Message); }
        finally
        {
            settings.Provider = oldProvider; settings.LibreUrl = oldUrl; settings.SourceLanguage = oldSource;
            settings.TargetLanguage = oldTarget; settings.ProtectedApiKey = oldKey;
            if (scene != null)
            {
                try
                {
                    if (!scene.HasExited)
                    {
                        scene.CloseMainWindow();
                        Task exited = scene.WaitForExitAsync();
                        if (await Task.WhenAny(exited, Task.Delay(1500)) != exited && !scene.HasExited) scene.Kill();
                    }
                }
                catch (Exception ex) { CheckResult("sample_cleanup", false, ex.GetType().Name + ": " + ex.Message); }
                finally { scene.Dispose(); }
            }
            frame.Width = oldWidth; frame.Height = oldHeight; frame.Left = oldLeft; frame.Top = oldTop;
            bool knownSample = source.Contains("gato", StringComparison.OrdinalIgnoreCase) && source.Contains("casa", StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(Path.Combine(outputDirectory, "translation-verification.json"), JsonSerializer.Serialize(new
            {
                startedUtc = started, completedUtc = DateTimeOffset.UtcNow, allPassed = checks.Count > 0 && checks.All(c => c.Passed),
                elapsedMs, sourceText = knownSample ? source : "[unexpected screen content omitted]",
                translatedText = knownSample ? translation : "[omitted]", checks,
                limitations = new[] { "Cloud providers are not called; missing credentials are checked locally.",
                    "Preview is a WPF render composite; sample-capture.png contains actual captured pixels.",
                    "This exercises SnapAsync directly. The global translation hotkey requires a separate live input check." }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    static async Task RenderAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        await Task.Delay(400);
        DwmFlush();
    }
    static BitmapSource Bitmap(CapturedImage capture) => BitmapSource.Create(capture.Width, capture.Height, 96, 96, PixelFormats.Bgra32, null, capture.Bgra, capture.Width * 4);
    static void SaveCapture(CapturedImage capture, string path) => SavePng(Bitmap(capture), path);
    static void SavePng(BitmapSource bitmap, string path)
    { if (Environment.GetEnvironmentVariable("HIVEBABBLE_KEEP_TEST_IMAGES") != "1") return; 
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    static void SavePreview(FrameWindow frame, BarWindow bar, SidePanelWindow panel, CapturedImage capture, string path)
    { if (Environment.GetEnvironmentVariable("HIVEBABBLE_KEEP_TEST_IMAGES") != "1") return; 
        const double margin = 28, header = 62, gap = 18, footer = 48;
        double contentHeight = Math.Max(bar.ActualHeight + frame.ActualHeight, panel.ActualHeight);
        double width = margin * 2 + frame.ActualWidth + gap + panel.ActualWidth;
        double height = header + contentHeight + footer;
        var visual = new DrawingVisual();
        using (DrawingContext draw = visual.RenderOpen())
        {
            draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 19, 25)), null, new Rect(0, 0, width, height));
            DrawText(draw, "READ THE WORLD  /  SPANISH → ENGLISH", new Point(margin, 21), 14, new SolidColorBrush(Color.FromRgb(224, 165, 38)));
            var frameBounds = new Rect(margin, header + bar.ActualHeight, frame.ActualWidth, frame.ActualHeight);
            draw.DrawImage(Bitmap(capture), frameBounds);
            draw.DrawRectangle(new VisualBrush(frame), null, frameBounds);
            draw.DrawRectangle(new VisualBrush(bar), null, new Rect(margin, header, bar.ActualWidth, bar.ActualHeight));
            draw.DrawRectangle(new VisualBrush(panel), null, new Rect(margin + frame.ActualWidth + gap, header, panel.ActualWidth, panel.ActualHeight));
            DrawText(draw, "Live OCR + local translation · WPF render composite of the running app", new Point(margin, height - 30), 12, Brushes.LightGray);
        }
        var result = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); SavePng(result, path);
    }
    static void DrawText(DrawingContext draw, string text, Point point, double size, Brush color) =>
        draw.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, 1), point);
}
