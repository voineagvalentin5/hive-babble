using System;
using System.Runtime.InteropServices;
namespace HiveBabble.Capture;
public sealed record CapturedImage(byte[] Bgra, int Width, int Height);
/// <summary>GDI BitBlt of a screen rect (physical pixels). Windows marked WDA_EXCLUDEFROMCAPTURE are left out by Windows.</summary>
public sealed class ScreenCapturer
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER
    { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    public CapturedImage CaptureRect(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Empty capture rect.");
        if (width > 16384 || height > 16384) throw new ArgumentException("Capture area is too large.");
        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new InvalidOperationException("Could not access the display.");
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, width, height);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero) throw new InvalidOperationException("Could not allocate the screen capture.");
            old = SelectObject(mem, bmp);
            if (old == IntPtr.Zero || old == new IntPtr(-1)) throw new InvalidOperationException("Could not prepare the screen capture.");
            if (!BitBlt(mem, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT)) throw new InvalidOperationException("BitBlt failed.");
            SelectObject(mem, old);
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
            var bits = new byte[checked(width * height * 4)];
            if (GetDIBits(mem, bmp, 0, (uint)height, bits, ref bi, 0) != height) throw new InvalidOperationException("Could not read the complete screen capture.");
            for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;
            return new CapturedImage(bits, width, height);
        }
        finally
        {
            if (old != IntPtr.Zero && old != new IntPtr(-1) && mem != IntPtr.Zero) SelectObject(mem, old);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
