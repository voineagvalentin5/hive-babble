using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;
namespace HiveBabble;
/// <summary>M4: notification-area icon with show/hide, translate, lock, pause, settings and exit.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly WinForms.NotifyIcon _icon;
    public TrayIcon(FrameWindow f)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show / hide", null, (_, _) => f.ToggleVisible());
        menu.Items.Add("Translate now  (Ctrl+Alt+T)", null, (_, _) => _ = f.SnapAsync());
        menu.Items.Add("Lock / unlock  (Ctrl+Alt+L)", null, (_, _) => f.ToggleLock());
        menu.Items.Add("Pause / resume", null, (_, _) => f.TogglePause());
        menu.Items.Add("Settings\u2026", null, (_, _) => f.OpenSettings(f));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => f.Close());
        _icon = new WinForms.NotifyIcon { Text = "Hive-Babble", Icon = CreateIcon(), ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => f.ToggleVisible();
    }
    public bool Visible => _icon.Visible;
    public int MenuItemCount => _icon.ContextMenuStrip?.Items.Count ?? 0;
    static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            PointF[] Hex(float cx, float cy, float r) { var p = new PointF[6]; for (int i = 0; i < 6; i++) { double a = Math.PI / 3 * i - Math.PI / 2; p[i] = new PointF(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a)); } return p; }
            using var dark = new SolidBrush(Color.FromArgb(21, 26, 32)); using var gold = new SolidBrush(Color.FromArgb(224, 165, 38));
            g.FillPolygon(gold, Hex(16, 16, 15)); g.FillPolygon(dark, Hex(16, 16, 11)); g.FillPolygon(gold, Hex(16, 16, 6));
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}