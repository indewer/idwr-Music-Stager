using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicStagerWidget;

public static class TrayIconHelper
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon CreateMusicStagerIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Dark rounded box (#26283B)
            using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(38, 40, 59));
            using var path = CreateRoundedRectanglePath(new RectangleF(2, 2, 28, 28), 7);
            g.FillPath(bgBrush, path);

            // Vinyl circle in center
            using var vinylBrush = new SolidBrush(System.Drawing.Color.FromArgb(20, 21, 31));
            g.FillEllipse(vinylBrush, 6, 6, 20, 20);

            // Vinyl groove ring (#6366F1 / #818CF8)
            using var groovePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(129, 140, 248), 1.2f);
            g.DrawEllipse(groovePen, 9, 9, 14, 14);

            // Vinyl center hole (#EC4899 - accent pink)
            using var centerBrush = new SolidBrush(System.Drawing.Color.FromArgb(236, 72, 153));
            g.FillEllipse(centerBrush, 14, 14, 4, 4);
        }

        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    public static void EnsureIcoFileExists(string outputPath)
    {
        try
        {
            if (!System.IO.File.Exists(outputPath))
            {
                using var icon = CreateMusicStagerIcon();
                using var fs = new System.IO.FileStream(outputPath, System.IO.FileMode.Create);
                icon.Save(fs);
            }
        }
        catch { }
    }

    public static ImageSource CreateMusicStagerImageSource(Icon icon)
    {
        return Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
    }

    private static GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
