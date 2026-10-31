using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using HiveBabble.Diagnostics;
namespace HiveBabble;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Write($"Launch v{typeof(App).Assembly.GetName().Version} pid {Environment.ProcessId} from {AppContext.BaseDirectory}");
        // Crash logging: exception type and stack only (messages can echo screen text).
        DispatcherUnhandledException += (_, ex) => { Log.Write("Unhandled UI error: " + Crash.Describe(ex.Exception)); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write("Fatal: " + Crash.Describe(ex.ExceptionObject as Exception) + (ex.IsTerminating ? " (terminating)" : ""));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Write("Unobserved task error: " + Crash.Describe(ex.Exception)); ex.SetObserved(); };
        SessionEnding += (_, ex) => Log.Write("Session ending: " + ex.ReasonSessionEnding);
        Exit += (_, ex) => Log.Write("Exit code " + ex.ApplicationExitCode + ".");
        int fixtureIndex = Array.IndexOf(e.Args, "--diagnostic-backdrop");
        if (Array.IndexOf(e.Args, "--sample-scene") >= 0)
        {
            base.OnStartup(e);
            MainWindow = SampleScene.Create(e.Args.FirstOrDefault(a => a.StartsWith("--sample-lang="))?.Substring(14) ?? "es");
            MainWindow.Show();
            return;
        }
        if (fixtureIndex < 0) { base.OnStartup(e); MainWindow = new FrameWindow(); MainWindow.Show(); return; }

        // A separate test process renders only the known background, with no protected frame.
        base.OnStartup(e);
        try
        {
            double[] bounds = e.Args[fixtureIndex + 1].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (bounds.Length != 4 || bounds.Any(v => !double.IsFinite(v)) || bounds[2] <= 0 || bounds[3] <= 0)
                throw new ArgumentException("Invalid diagnostic rectangle.");
            MainWindow = FrameVerification.CreateBackdrop(new Rect(bounds[0], bounds[1], bounds[2], bounds[3]));
            MainWindow.Show();
            Log.Write($"Diagnostic background shown: visible={MainWindow.IsVisible}, hwnd={new System.Windows.Interop.WindowInteropHelper(MainWindow).Handle}, rect={MainWindow.Left},{MainWindow.Top},{MainWindow.Width},{MainWindow.Height}");
            // The parent normally closes this window; a timeout prevents orphaned topmost fixtures.
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timeout.Tick += (_, _) => { timeout.Stop(); MainWindow?.Close(); };
            timeout.Start();
        }
        catch (Exception ex) { Log.Write("Diagnostic background failed: " + ex); Shutdown(2); }
    }
}
