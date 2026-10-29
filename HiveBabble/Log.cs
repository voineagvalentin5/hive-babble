using System;
using System.IO;
namespace HiveBabble;
public static class Log
{
    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HiveBabble");
    public static string FilePath => Path.Combine(Dir, "hivebabble.log");
    public static void Write(string msg)
    {
        try { Directory.CreateDirectory(Dir); File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); } catch { }
    }
}