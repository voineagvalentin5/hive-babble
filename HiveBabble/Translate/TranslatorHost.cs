using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>Starts the bundled offline translator (installer build) when the local URL isn't answering, and stops it on exit if we started it.</summary>
public static class TranslatorHost
{
    static Process? _proc;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    public static string BundledDir => Path.Combine(AppContext.BaseDirectory, "translator");
    public static bool StartedByApp => _proc is { HasExited: false };
    static TranslatorHost() { AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop(); }

    static async Task<bool> PingAsync(Uri u)
    {
        try { using var r = await Http.GetAsync(new Uri(u, "/languages")); return r.IsSuccessStatusCode; } catch { return false; }
    }

    /// <summary>Returns true when the translator answers. Only loopback URLs are auto-started.</summary>
    public static async Task<bool> EnsureRunningAsync(string url, Action<string> status, int timeoutSeconds = 180)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || !u.IsLoopback) return true;
        if (await PingAsync(u)) return true;
        string py = Path.Combine(BundledDir, "python", "python.exe"), script = Path.Combine(BundledDir, "run_server.py");
        if (!File.Exists(py) || !File.Exists(script)) { Log.Write("Translator not answering and no bundled translator found."); return false; }
        status("Starting offline translator\u2026");
        var sw = Stopwatch.StartNew();
        try
        {
            var psi = new ProcessStartInfo(py) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = BundledDir };
            foreach (var a in new[] { "-E", script, "--host", "127.0.0.1", "--port", u.Port.ToString() }) psi.ArgumentList.Add(a);
            _proc = Process.Start(psi);
        }
        catch (Exception ex) { Log.Write("Translator start failed: " + ex.GetType().Name); status("Could not start the offline translator."); return false; }
        while (sw.Elapsed.TotalSeconds < timeoutSeconds)
        {
            if (_proc == null || _proc.HasExited) { Log.Write($"Translator exited during startup (code {_proc?.ExitCode})."); status("Offline translator stopped unexpectedly. Restart Hive-Babble."); _proc = null; return false; }
            if (await PingAsync(u)) { Log.Write($"Translator started by app in {sw.ElapsedMilliseconds} ms (pid {_proc.Id})."); status("Offline translator ready."); return true; }
            await Task.Delay(500);
        }
        Log.Write($"Translator not ready after {timeoutSeconds} s."); status("Offline translator is taking too long to start."); return false;
    }

    public static void Stop()
    {
        var p = _proc; _proc = null;
        try { if (p is { HasExited: false }) { p.Kill(entireProcessTree: true); p.WaitForExit(5000); Log.Write("Translator stopped."); } } catch { }
    }
}
