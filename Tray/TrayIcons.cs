using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AutoInputTester.Tray;

public static class TrayIcons
{
    public static Icon CreateDefault()
    {
        using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(40, 110, 200));
            g.FillEllipse(brush, 2, 2, 12, 12);
            using var pen = new Pen(Color.White, 1.5f);
            g.DrawLine(pen, 5, 8, 8, 11);
            g.DrawLine(pen, 8, 11, 11, 5);
        }

        return FromBitmap(bmp);
    }

    /// <summary>Red camera glyph — indicates keyboard lock / "camera on".</summary>
    public static Icon CreateCameraOn()
    {
        using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var body = new SolidBrush(Color.FromArgb(200, 40, 40));
            using var lens = new SolidBrush(Color.White);
            using var accent = new SolidBrush(Color.FromArgb(255, 80, 80));
            g.FillRectangle(body, 2, 5, 9, 7);
            g.FillPolygon(body, new[]
            {
                new Point(11, 7),
                new Point(15, 5),
                new Point(15, 12),
                new Point(11, 10)
            });
            g.FillEllipse(lens, 4, 6, 5, 5);
            g.FillEllipse(accent, 13, 2, 3, 3);
        }

        return FromBitmap(bmp);
    }

    private static Icon FromBitmap(Bitmap bmp)
    {
        var hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}
