using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HiveBabble.Diagnostics;

/// <summary>A public-domain Spanish sample in a separate, capturable process.</summary>
public static class SampleScene
{
    public static Process Start(string lang = "es")
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the app executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(SampleScene).Assembly.Location);
        start.ArgumentList.Add("--sample-scene"); if (lang != "es") start.ArgumentList.Add("--sample-lang=" + lang);
        return Process.Start(start) ?? throw new InvalidOperationException("Cannot launch the sample scene.");
    }

    public static Window Create(string lang = "es")
    {
        var ink = new SolidColorBrush(Color.FromRgb(40, 48, 50));
        var muted = new SolidColorBrush(Color.FromRgb(110, 115, 109));
        var surface = new Grid { Background = new SolidColorBrush(Color.FromRgb(246, 242, 231)) };
        surface.Children.Add(new TextBlock
        {
            Text = lang == "ja" ? "ノート / 短い読み物" : "EL CUADERNO  /  UNA PEQUEÑA LECTURA", FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = muted, Margin = new Thickness(60, 36, 30, 0)
        });
        var copy = new StackPanel { Margin = new Thickness(60, 125, 40, 40), VerticalAlignment = VerticalAlignment.Top };
        copy.Children.Add(new TextBlock { Text = lang == "ja" ? "こんにちは、世界。" : "Hola, mundo.", FontFamily = new FontFamily(lang == "ja" ? "Yu Gothic UI" : "Georgia"), FontSize = 38, Foreground = ink });
        copy.Children.Add(new TextBlock { Text = lang == "ja" ? "猫は家の中で寝ています。" : "El gato duerme en la casa.", FontSize = 27, Foreground = ink, Margin = new Thickness(0, 26, 0, 0) });
        copy.Children.Add(new TextBlock { Text = lang == "ja" ? "読んでくれてありがとう。" : "Gracias por leer con nosotros.", FontSize = 27, Foreground = ink, Margin = new Thickness(0, 22, 0, 0) });
        surface.Children.Add(copy);
        surface.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(211, 170, 74)), Height = 3,
            Width = 48, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(60, 0, 0, 44) });
        var window = new Window
        {
            Title = "Hive-Babble sample " + lang, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 80, Top = 100, Width = 850, Height = 480, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = true, Topmost = true, Content = surface
        };
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timeout.Tick += (_, _) => { timeout.Stop(); window.Close(); };
        window.Loaded += (_, _) => timeout.Start();
        window.Closed += (_, _) => timeout.Stop();
        return window;
    }
}
