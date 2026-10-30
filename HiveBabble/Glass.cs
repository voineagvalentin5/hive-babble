using System;
using System.Runtime.InteropServices;
namespace HiveBabble;
/// <summary>Frosted glass: Windows acrylic blur behind a window, with rounded corners on Windows 11.</summary>
static class Glass
{
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags, Gradient, Anim; }
    [StructLayout(LayoutKind.Sequential)] struct WcaData { public int Attr; public IntPtr Data; public int Size; }
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WcaData d);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    /// <summary>tint is 0xAARRGGBB. Returns false if the OS refused (falls back to the XAML tint only).</summary>
    public static bool Enable(IntPtr hwnd, uint tint = 0x99151A20)
    {
        try
        {
            int corner = 2; DwmSetWindowAttribute(hwnd, 33, ref corner, 4); // DWMWCP_ROUND
            uint a = tint >> 24, r = (tint >> 16) & 0xFF, g = (tint >> 8) & 0xFF, b = tint & 0xFF;
            var p = new AccentPolicy { State = 4, Flags = 2, Gradient = (int)((a << 24) | (b << 16) | (g << 8) | r) };
            int n = Marshal.SizeOf(p); IntPtr ptr = Marshal.AllocHGlobal(n);
            try { Marshal.StructureToPtr(p, ptr, false); var d = new WcaData { Attr = 19, Data = ptr, Size = n }; bool ok = SetWindowCompositionAttribute(hwnd, ref d) != 0; Log.Write("Frosted glass " + (ok ? "on" : "unavailable") + "."); return ok; }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch (Exception ex) { Log.Write("Frosted glass unavailable: " + ex.GetType().Name); return false; }
    }
}