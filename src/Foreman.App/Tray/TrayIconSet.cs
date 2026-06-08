using System.Drawing;
using System.Drawing.Drawing2D;

namespace Foreman.App.Tray;

/// <summary>
/// Tray icons: a colored "F" on a near-black rounded background, generated programmatically.
/// The F's colour is the alert level — green (all clear), amber (warning), red (high/critical) —
/// so the icon itself shows status at a glance. Drawn as three bars rather than a glyph so it
/// stays crisp when Windows scales it down to tray size.
/// </summary>
public static class TrayIconSet
{
    public static Icon Green { get; } = CreateLetterIcon(Color.FromArgb(0x49, 0xD0, 0x5B));
    public static Icon Amber { get; } = CreateLetterIcon(Color.FromArgb(0xF0, 0xB4, 0x3A));
    public static Icon Red   { get; } = CreateLetterIcon(Color.FromArgb(0xF0, 0x46, 0x46));

    private static Icon CreateLetterIcon(Color letter)
    {
        const int n = 32;
        using var bmp = new Bitmap(n, n);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Near-black rounded-square background.
        using (var bg = new SolidBrush(Color.FromArgb(0xFF, 0x10, 0x12, 0x18)))
        using (var path = RoundedRect(0.5f, 0.5f, n - 1f, n - 1f, 6f))
            g.FillPath(bg, path);

        // Colored "F" built from three rectangles (proportional, so it scales cleanly).
        var pad    = n * 0.24f;
        var left   = pad;
        var top    = pad;
        var bottom = n - pad;
        var width  = n - 2 * pad;
        var stemW  = n * 0.17f;
        var armH   = n * 0.16f;

        using var fb = new SolidBrush(letter);
        g.FillRectangle(fb, left, top, stemW, bottom - top);                          // vertical stem
        g.FillRectangle(fb, left, top, width, armH);                                  // top arm
        g.FillRectangle(fb, left, top + (bottom - top) * 0.42f, width * 0.74f, armH); // middle arm

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
