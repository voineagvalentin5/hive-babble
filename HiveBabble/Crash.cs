using System;
using System.Linq;
namespace HiveBabble;
public static class Crash
{
    /// <summary>Exception type chain plus stack frames, without messages.</summary>
    public static string Describe(Exception? e)
    {
        if (e == null) return "(unknown)";
        var parts = new System.Collections.Generic.List<string>();
        for (var x = e; x != null; x = x.InnerException) parts.Add(x.GetType().FullName + " HResult=0x" + x.HResult.ToString("X8"));
        var st = new System.Diagnostics.StackTrace(e, false).GetFrames().Select(f => f.GetMethod()).Where(m => m != null).Select(m => m!.DeclaringType?.Name + "." + m.Name).Take(12);
        return string.Join(" <- ", parts) + " at " + string.Join(" < ", st);
    }
}