using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using HiveBabble.Settings;

namespace HiveBabble.Diagnostics;

/// <summary>M3/M4 live checks: real capture of a separate Spanish sample window, real Windows OCR, real local LibreTranslate.
/// Acceptance 2 (overlay latency + cache hits on static text), 3 (no feedback loop), 5 (bad backend/key = message, no crash), plus M4 controls.</summary>
public static class LiveVerification
{
    sealed record Check(string Name, bool Passed, string Detail);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();

    static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (!condition()) { if (sw.ElapsedMilliseconds > timeoutMs) return false; await Task.Delay(10); }
        return true;
    }

    static void SavePng(HiveBabble.Capture.CapturedImage img, string path)
    {
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(img.Width, img.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, img.Bgra, img.Width * 4);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(path); enc.Save(fs);
    }

    public static async Task RunAsync(FrameWindow frame, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<Check>();
        var metrics = new Dictionary<string, object?>();
        void Add(string name, bool pass, string detail) { checks.Add(new(name, pass, detail)); Log.Write($"verify-live {(pass ? "PASS" : "FAIL")} {name}: {detail}"); }
        var s = frame.Settings;
        var saved = JsonSerializer.Serialize(s);
        Process? scene = null;
        var started = DateTimeOffset.Now;
        try
        {
            s.Provider = TranslatorProvider.Libre; s.LibreUrl = "http://127.0.0.1:5000"; s.SourceLanguage = "auto"; s.TargetLanguage = "en";
            s.Mode = DisplayMode.Overlay; s.LoopIntervalMs = 750; s.ProtectedApiKey = null;
            frame.Width = 760; frame.Height = 320; frame.Left = 100; frame.Top = 190;
            scene = SampleScene.Start();
            IntPtr sceneHwnd = IntPtr.Zero;
            for (int i = 0; i < 100 && sceneHwnd == IntPtr.Zero; i++) { await Task.Delay(100); scene.Refresh(); sceneHwnd = scene.MainWindowHandle; }
            if (sceneHwnd == IntPtr.Zero) throw new InvalidOperationException("Sample scene did not open.");
            SetWindowPos(sceneHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            SetWindowPos(new WindowInteropHelper(frame).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            if (frame.Bar != null) SetWindowPos(new WindowInteropHelper(frame.Bar).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            await frame.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); DwmFlush(); await Task.Delay(500);

            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(frame);
            metrics["dpiScale"] = dpi.DpiScaleX;
            metrics["monitors"] = System.Windows.Forms.Screen.AllScreens.Select(m => $"{m.DeviceName} {m.Bounds.Width}x{m.Bounds.Height} primary={m.Primary}").ToArray();

            // --- Acceptance 2: Spanish -> English overlay, timed from loop start (first tick is immediate).
            var warm = Stopwatch.StartNew(); await frame.WarmUpTranslator(); metrics["warmUpMs"] = warm.ElapsedMilliseconds;
            int tapIndex = 0;
            frame.CaptureTap = img => { if (tapIndex < 12) SavePng(img, Path.Combine(outputDirectory, $"capture-{tapIndex++:00}.png")); };
            frame.Cache.Clear(); frame.ResetFrameHash();
            var st = frame.Stats;
            var sw = Stopwatch.StartNew();
            frame.StartLoop();
            bool drawn = await WaitFor(() => st.Processed + st.Errors >= 1 && frame.Overlay.Children.Count > 0 || st.Errors > 0, 10000);
            long firstMs = sw.ElapsedMilliseconds;
            metrics["firstOverlayMs"] = firstMs; metrics["firstPipelineMs"] = st.LastLatencyMs; metrics["firstOcrMs"] = st.LastOcrMs;
            metrics["source"] = frame.LastSourceText; metrics["translation"] = frame.LastTranslatedText; metrics["detected"] = frame.LastDetectedLanguage;
            Add("Overlay drawn over OCR rects", drawn && frame.Overlay.Children.Count == frame.LastLines.Count,
                $"{frame.Overlay.Children.Count} boxes for {frame.LastLines.Count} OCR lines");
            Add("Spanish -> English translated", frame.LastTranslatedText.Contains("cat", StringComparison.OrdinalIgnoreCase) || frame.LastTranslatedText.Contains("Thank", StringComparison.OrdinalIgnoreCase),
                frame.LastTranslatedText.Replace("\n", " | "));
            Add("Source language auto-detected", s.SourceLanguage == "auto" && frame.LastDetectedLanguage == "es", $"detected={frame.LastDetectedLanguage ?? "(none)"}");
            Add("Overlay within 1.5 s", drawn && firstMs <= 1500, $"{firstMs} ms from loop start (pipeline {st.LastLatencyMs} ms, OCR {st.LastOcrMs} ms)");
            if (frame.Overlay.Children.Count > 0 && frame.LastLines.Count > 0)
            {
                var box = (Border)frame.Overlay.Children[0]; var l = frame.LastLines[0];
                double expectX = (l.X + FrameWindow.CaptureInset) / dpi.DpiScaleX - 3;
                var tb = (TextBlock)box.Child;
                Add("Box placed at OCR rect (DPI-converted) and font fits", Math.Abs(Canvas.GetLeft(box) - expectX) < 0.5 && tb.FontSize >= 6,
                    $"left={Canvas.GetLeft(box):0.0} expected={expectX:0.0} DIP, w={box.Width:0} h={box.Height:0}, font={tb.FontSize:0.0}");
            }

            // --- Acceptance 3: static text + overlay drawn -> later captures are byte-identical -> skipped, no re-OCR.
            // Let the sample window settle (it re-lays out once at 125%), then require a stable run of skipped frames.
            await WaitFor(() => { int k = st.Skipped; return false; }, 1500);
            int processed = st.Processed, captures = st.Captures, skipped = st.Skipped;
            string firstHash = st.LastHash;
            await Task.Delay(3000);
            metrics["staticCaptures"] = st.Captures - captures; metrics["staticSkipped"] = st.Skipped - skipped;
            int boxes = frame.Overlay.Children.Count;
            string withOverlay = frame.CaptureHashNow();
            frame.StopLoop();
            var kept = frame.Overlay.Children.Cast<UIElement>().ToList();
            frame.Overlay.Children.Clear();
            await frame.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); DwmFlush(); await Task.Delay(150);
            string withoutOverlay = frame.CaptureHashNow();
            foreach (var k in kept) frame.Overlay.Children.Add(k);
            frame.StartLoop();
            Add("No feedback loop (overlay never captured)", boxes > 0 && withOverlay == withoutOverlay && st.Skipped - skipped == st.Captures - captures && st.Processed == processed && st.Captures - captures >= 3,
                $"capture with {boxes} overlay boxes == capture without: {withOverlay == withoutOverlay}; static 3 s: {st.Captures - captures} captures, {st.Skipped - skipped} skipped as unchanged, {st.Processed - processed} re-OCR'd");
            // --- Cache: force a re-read of the same static text; every line must come from the cache.
            int hitsBefore = st.CacheHits; frame.ResetFrameHash(); processed = st.Processed;
            sw.Restart();
            await WaitFor(() => st.Processed > processed || st.Errors > 0, 5000);
            metrics["cachedPassMs"] = st.LastLatencyMs;
            Add("Cache hits on static text (logged)", st.CacheHits - hitsBefore == frame.LastLines.Count && frame.LastLines.Count > 0,
                $"{st.CacheHits - hitsBefore}/{frame.LastLines.Count} lines from cache, pipeline {st.LastLatencyMs} ms");

            // --- M4 target language picker: ja is a separate cache entry and is translated live.
            processed = st.Processed;
            frame.SetTargetLanguage("ja");
            await WaitFor(() => st.Processed > processed || st.Errors > 0, 8000);
            Add("Target language picker (en -> ja)", frame.LastTranslatedText.Any(c => c > 0x3000), $"{frame.LastTranslatedText.Replace("\n", " | ")} ({st.LastLatencyMs} ms)");
            frame.SetTargetLanguage("en");
            processed = st.Processed;
            await WaitFor(() => st.Processed > processed, 5000);

            // --- M4 modes.
            processed = st.Processed; frame.SetMode(DisplayMode.Side);
            await WaitFor(() => st.Processed > processed + 1, 5000);
            bool side = frame.Overlay.Children.Count == 0 && frame.ResultsPanel?.IsVisible == true;
            processed = st.Processed; frame.SetMode(DisplayMode.Both);
            await WaitFor(() => st.Processed > processed + 1, 5000);
            bool both = frame.Overlay.Children.Count > 0 && frame.ResultsPanel?.IsVisible == true;
            processed = st.Processed; frame.SetMode(DisplayMode.Overlay);
            await WaitFor(() => st.Processed > processed + 1, 5000);
            string overlayDetail = $"boxes={frame.Overlay.Children.Count} panelVisible={frame.ResultsPanel?.IsVisible} status={frame.LastStatus}";
            bool overlay = frame.Overlay.Children.Count > 0 && frame.ResultsPanel?.IsVisible != true;
            Add("Modes Overlay / Side / Both", side && both && overlay, $"side={side} both={both} overlay={overlay} ({overlayDetail})");
            Add("Tray icon present", frame.Tray?.Visible == true && frame.Tray.MenuItemCount >= 6, $"visible={frame.Tray?.Visible} items={frame.Tray?.MenuItemCount}");

            // --- Acceptance 5: broken backends and keys produce a message; the loop keeps running.
            async Task Broken(string name, Action setup, string expected)
            {
                setup(); frame.Cache.Clear(); int errors = st.Errors; frame.ResetFrameHash();
                var t = Stopwatch.StartNew();
                await WaitFor(() => st.Errors > errors, 25000);
                bool ok = st.Errors > errors && frame.LastStatus.Contains(expected, StringComparison.OrdinalIgnoreCase) && frame.LoopRunning && frame.IsVisible;
                Add("Error message: " + name, ok, $"\"{frame.LastStatus}\" after {t.ElapsedMilliseconds} ms, loop running={frame.LoopRunning}");
            }
            await Broken("local translator not running", () => { s.Provider = TranslatorProvider.Libre; s.LibreUrl = "http://127.0.0.1:5999"; }, "Local translator not running");
            await Broken("invalid server URL", () => { s.LibreUrl = "not a url"; }, "valid HTTP");
            await Broken("DeepL without key", () => { s.Provider = TranslatorProvider.DeepL; s.ProtectedApiKey = null; }, "needs an API key");
            await Broken("DeepL malformed key", () => { s.SetApiKey("bad key with spaces"); }, "invalid characters");
            await Broken("Google without key", () => { s.Provider = TranslatorProvider.Google; s.ProtectedApiKey = null; }, "API key");

            // Recovery: back to the working local translator without restarting.
            s.Provider = TranslatorProvider.Libre; s.LibreUrl = "http://127.0.0.1:5000"; s.ProtectedApiKey = null;
            processed = st.Processed; frame.ResetFrameHash();
            await WaitFor(() => st.Processed > processed, 8000);
            Add("Recovers after the backend is fixed", st.Processed > processed && frame.Overlay.Children.Count > 0, $"{frame.LastStatus}");
        }
        catch (Exception ex) { Add("Run completed", false, ex.GetType().Name + ": " + ex.Message); }
        finally
        {
            frame.CaptureTap = null;
            frame.StopLoop();
            try { scene?.Kill(); } catch { }
            var restored = JsonSerializer.Deserialize<AppSettings>(saved)!;
            foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite)) p.SetValue(s, p.GetValue(restored));
        }
        metrics["stats"] = new { frame.Stats.Captures, frame.Stats.Skipped, frame.Stats.Processed, frame.Stats.Errors, frame.Stats.CacheHits, frame.Stats.CacheLookups };
        var report = new { started, finished = DateTimeOffset.Now, passed = checks.All(c => c.Passed), checks, metrics };
        File.WriteAllText(Path.Combine(outputDirectory, "live-verification.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}