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
using HiveBabble.Ocr;
using HiveBabble.Settings;
using HiveBabble.Translate;

namespace HiveBabble.Diagnostics;

/// <summary>Regression checks for live-frame interactions, using real capture/OCR and synthetic translators only.</summary>
public static class InteractionVerification
{
    sealed record Check(string Name, bool Passed, string Detail);
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();

    public static async Task RunAsync(FrameWindow frame, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var checks = new List<Check>();
        var started = DateTimeOffset.UtcNow;
        var settings = frame.Settings;
        string savedSettings = JsonSerializer.Serialize(settings);
        var originalFactory = frame.CreateTranslator;
        double left = frame.Left, top = frame.Top, width = frame.Width, height = frame.Height;
        bool originallyVisible = frame.IsVisible, originallyRunning = frame.LoopRunning, pausedByTest = false;
        Process? scene = null;
        var gates = new List<DelayedTranslator>();
        var outstanding = new List<Task<bool>>();

        void Add(string name, bool passed, string detail)
        {
            checks.Add(new(name, passed, detail));
            Log.Write($"verify-interactions {(passed ? "PASS" : "FAIL")} {name}: {detail}");
        }
        async Task Test(string name, Func<Task<(bool Passed, string Detail)>> test)
        {
            try { var result = await test(); Add(name, result.Passed, result.Detail); }
            catch (Exception ex) { Add(name, false, ex.GetType().Name + ": " + ex.Message); }
        }
        async Task<(DelayedTranslator Gate, Task<bool> Work)> BeginPending(string marker)
        {
            if (frame.IsProcessing) throw new InvalidOperationException("Previous diagnostic operation is still running.");
            var gate = new DelayedTranslator(marker);
            gates.Add(gate);
            frame.CreateTranslator = _ => gate;
            frame.Cache.Clear();
            frame.ResetFrameHash();
            Task<bool> work = frame.ProcessFrameAsync(force: true);
            outstanding.Add(work);
            await Within(gate.Entered.Task, "OCR did not reach the synthetic translator.");
            return (gate, work);
        }

        try
        {
            frame.StopLoop();
            if (frame.IsProcessing) throw new InvalidOperationException("Run this diagnostic before starting the live loop.");
            settings.Provider = TranslatorProvider.Libre;
            settings.SourceLanguage = "auto"; settings.TargetLanguage = "en";
            settings.Mode = DisplayMode.Overlay; settings.LoopIntervalMs = 750;
            settings.OcrLanguage = WindowsOcrEngine.Available.Split(',').FirstOrDefault(t => t.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? "auto";
            frame.CreateTranslator = _ => new ImmediateTranslator("BASELINE");
            frame.ApplySettingsChanges();
            frame.Width = 760; frame.Height = 320; frame.Left = 100; frame.Top = 190;
            if (!frame.IsVisible) frame.Show();
            frame.Bar?.Show();

            scene = SampleScene.Start();
            IntPtr sceneHwnd = IntPtr.Zero;
            for (int i = 0; i < 60 && sceneHwnd == IntPtr.Zero; i++)
            {
                await Task.Delay(100);
                scene.Refresh();
                if (scene.HasExited) throw new InvalidOperationException("Sample scene exited before verification.");
                sceneHwnd = scene.MainWindowHandle;
            }
            if (sceneHwnd == IntPtr.Zero) throw new InvalidOperationException("Sample scene did not open.");
            RaiseTestWindows(frame, sceneHwnd);
            await Settle(frame, 500);
            bool baseline = await frame.ProcessFrameAsync(force: true);
            Add("Synthetic baseline reaches overlay", baseline && frame.LastLines.Count > 0 && frame.Overlay.Children.Count > 0 &&
                frame.LastTranslatedText.Contains("BASELINE", StringComparison.Ordinal), $"{frame.LastLines.Count} OCR lines, {frame.Overlay.Children.Count} boxes.");
            if (frame.Overlay.Children.Count == 0) throw new InvalidOperationException("No baseline overlay; interaction checks require a visible sample and working OCR.");

            await Test("Move away and back redraws identical pixels", async () =>
            {
                string originalHash = frame.CaptureHashNow();
                double x = frame.Left, y = frame.Top;
                frame.Left = x + 24; frame.Top = y + 16;
                await Settle(frame);
                frame.Left = x; frame.Top = y;
                await Settle(frame);
                bool identical = frame.CaptureHashNow() == originalHash;
                bool processed = await frame.ProcessFrameAsync(force: false);
                return (identical && processed && frame.Overlay.Children.Count > 0,
                    $"Identical captured pixels={identical}; processed={processed}; boxes={frame.Overlay.Children.Count}.");
            });

            await Test("Pause discards an outstanding translation", async () =>
            {
                var (gate, work) = await BeginPending("STALE_PAUSE");
                int errors = frame.Stats.Errors;
                try
                {
                    frame.TogglePause(); pausedByTest = true;
                    gate.Release();
                    await Within(work, "Paused translation did not finish.");
                    bool published = HasMarker(frame, "STALE_PAUSE");
                    return (!published && frame.Stats.Errors == errors,
                        $"Stale result published={published}; new errors={frame.Stats.Errors - errors}.");
                }
                finally { gate.Release(); if (pausedByTest) { frame.TogglePause(); pausedByTest = false; } }
            });

            await Test("Settings change discards pending result and cache writes", async () =>
            {
                var (gate, work) = await BeginPending("STALE_SETTINGS");
                int errors = frame.Stats.Errors;
                try
                {
                    frame.Cache.Set("seed", "auto", "en", "old server", "old result");
                    settings.LibreUrl = "http://127.0.0.1:5998";
                    frame.ApplySettingsChanges();
                    bool cleared = frame.Cache.Count == 0;
                    gate.Release();
                    await Within(work, "Old-settings translation did not finish.");
                    bool published = HasMarker(frame, "STALE_SETTINGS");
                    return (cleared && !published && frame.Cache.Count == 0 && frame.Stats.Errors == errors,
                        $"Cache cleared immediately={cleared}; stale published={published}; cache after old response={frame.Cache.Count}; new errors={frame.Stats.Errors - errors}.");
                }
                finally { gate.Release(); }
            });

            await Test("Fresh settings recover normally", async () =>
            {
                settings.LibreUrl = "http://127.0.0.1:5000";
                frame.CreateTranslator = _ => new ImmediateTranslator("FRESH_SETTINGS");
                frame.ApplySettingsChanges();
                bool processed = await frame.ProcessFrameAsync(force: false);
                return (processed && HasMarker(frame, "FRESH_SETTINGS") && frame.Overlay.Children.Count > 0,
                    $"Processed={processed}; boxes={frame.Overlay.Children.Count}; fresh result={frame.LastTranslatedText.Contains("FRESH_SETTINGS", StringComparison.Ordinal)}.");
            });

            await Test("Hide discards pending result without reopening results", async () =>
            {
                settings.Mode = DisplayMode.Side;
                frame.ApplySettingsChanges();
                var (gate, work) = await BeginPending("STALE_HIDDEN");
                int errors = frame.Stats.Errors;
                try
                {
                    frame.ToggleVisible();
                    gate.Release();
                    await Within(work, "Hidden-frame translation did not finish.");
                    await Settle(frame, 100);
                    bool panelVisible = frame.ResultsPanel?.IsVisible == true;
                    bool published = HasMarker(frame, "STALE_HIDDEN");
                    return (!frame.IsVisible && !panelVisible && !published && frame.Stats.Errors == errors,
                        $"Frame visible={frame.IsVisible}; panel visible={panelVisible}; stale published={published}; new errors={frame.Stats.Errors - errors}.");
                }
                finally
                {
                    gate.Release();
                    // Show without starting the timer: subsequent checks deliberately control every processing call.
                    frame.Show(); frame.Bar?.Show(); frame.StopLoop();
                    settings.Mode = DisplayMode.Overlay;
                    frame.ApplySettingsChanges();
                    RaiseTestWindows(frame, sceneHwnd);
                }
            });

            await Test("Provider cooldown survives changed screen pixels", async () =>
            {
                var failing = new FailingTranslator();
                frame.CreateTranslator = _ => failing;
                frame.Cache.Clear(); frame.ResetFrameHash();
                int beforeErrors = frame.Stats.Errors;
                await frame.ProcessFrameAsync(force: true);
                int callsAfterFailure = failing.Calls, errorsAfterFailure = frame.Stats.Errors;
                string failedHash = frame.CaptureHashNow();
                if (!GetWindowRect(sceneHwnd, out var sceneRect)) throw new InvalidOperationException("Cannot read the sample rectangle.");
                // Move the separate scene, not the frame. Frame invalidation must not reset the cooldown here.
                if (!SetWindowPos(sceneHwnd, new IntPtr(-1), sceneRect.Left + 17, sceneRect.Top + 11, 0, 0, 0x11))
                    throw new InvalidOperationException("Cannot move the diagnostic sample.");
                RaiseTestWindows(frame, sceneHwnd);
                await Settle(frame);
                bool pixelsChanged = failedHash != frame.CaptureHashNow();
                bool processed = await frame.ProcessFrameAsync(force: false);
                return (errorsAfterFailure == beforeErrors + 1 && callsAfterFailure > 0 && pixelsChanged && !processed &&
                    failing.Calls == callsAfterFailure && frame.Stats.Errors == errorsAfterFailure,
                    $"Pixels changed={pixelsChanged}; processed during cooldown={processed}; provider calls={callsAfterFailure}->{failing.Calls}; errors={errorsAfterFailure}->{frame.Stats.Errors}.");
            });
        }
        catch (Exception ex) { Add("Interaction run completed", false, ex.GetType().Name + ": " + ex.Message); }
        finally
        {
            frame.StopLoop();
            foreach (var gate in gates) gate.Release();
            try { await Within(Task.WhenAll(outstanding), "Diagnostic requests did not finish during cleanup."); }
            catch (Exception ex) { Add("Pending-request cleanup", false, ex.GetType().Name); }
            if (pausedByTest) frame.TogglePause();
            var restored = JsonSerializer.Deserialize<AppSettings>(savedSettings)!;
            foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.CanWrite)) property.SetValue(settings, property.GetValue(restored));
            frame.CreateTranslator = originalFactory;
            frame.ApplySettingsChanges();
            frame.Width = width; frame.Height = height; frame.Left = left; frame.Top = top;
            frame.Overlay.Children.Clear(); frame.ResultsPanel?.Hide();
            if (originallyVisible) { if (!frame.IsVisible) frame.Show(); frame.Bar?.Show(); }
            else { frame.Bar?.Hide(); frame.Hide(); }
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
                catch (Exception ex) { Add("Sample cleanup", false, ex.GetType().Name); }
                finally { scene.Dispose(); }
            }
            if (originallyRunning) frame.StartLoop();
            File.WriteAllText(Path.Combine(outputDir, "interaction-verification.json"), JsonSerializer.Serialize(new
            {
                startedUtc = started, completedUtc = DateTimeOffset.UtcNow, passed = checks.Count > 0 && checks.All(c => c.Passed), checks,
                limitations = "Uses real screen capture and Windows OCR with synthetic translators. No cloud requests or real settings writes. Mouse gestures are represented by the same window/state methods."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    static async Task Within(Task task, string timeoutMessage)
    {
        if (await Task.WhenAny(task, Task.Delay(8000)) != task) throw new TimeoutException(timeoutMessage);
        await task;
    }
    static async Task Settle(Window frame, int delay = 180)
    {
        await frame.Dispatcher.InvokeAsync(frame.UpdateLayout, DispatcherPriority.Render);
        await Task.Delay(delay); DwmFlush();
    }
    static void RaiseTestWindows(FrameWindow frame, IntPtr scene)
    {
        SetWindowPos(scene, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        SetWindowPos(new WindowInteropHelper(frame).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        if (frame.Bar != null) SetWindowPos(new WindowInteropHelper(frame.Bar).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
    }
    static bool HasMarker(FrameWindow frame, string marker) => frame.LastTranslatedText.Contains(marker, StringComparison.Ordinal) ||
        frame.Overlay.Children.OfType<Border>().Any(b => b.Child is TextBlock text && text.Text.Contains(marker, StringComparison.Ordinal)) ||
        (frame.ResultsPanel?.FindName("ResultBox") is TextBox result && result.Text.Contains(marker, StringComparison.Ordinal));
    static string TranslateFake(string text, string marker) => string.Join("\n", text.Split('\n').Select((_, index) => $"{marker} line {index + 1}"));
    sealed class ImmediateTranslator(string marker) : ITranslator
    {
        public string Name => "Interaction test";
        public Task<string> TranslateAsync(string text, string? sourceLang, string targetLang) => Task.FromResult(TranslateFake(text, marker));
    }
    sealed class DelayedTranslator(string marker) : ITranslator
    {
        readonly TaskCompletionSource<string> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string _text = "";
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name => "Interaction test";
        public Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
        { _text = text; Entered.TrySetResult(true); return _result.Task; }
        public void Release() => _result.TrySetResult(TranslateFake(_text, marker));
    }
    sealed class FailingTranslator : ITranslator
    {
        public int Calls { get; private set; }
        public string Name => "Interaction test";
        public Task<string> TranslateAsync(string text, string? sourceLang, string targetLang)
        { Calls++; return Task.FromException<string>(new TranslatorException("Synthetic provider outage for interaction verification.")); }
    }
}
