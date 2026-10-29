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
        int fixtureIndex = Array.IndexOf(e.Args, "--diagnostic-backdrop");
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
