using System.Drawing;
using System.IO;
using System.Reflection;

namespace Foreman.App.Tray;

public static class TrayIconSet
{
    public static Icon Green { get; } = LoadIcon("foreman-green.ico");
    public static Icon Amber { get; } = LoadIcon("foreman-amber.ico");
    public static Icon Red   { get; } = LoadIcon("foreman-red.ico");

    private static Icon LoadIcon(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (res is not null)
        {
            using var stream = asm.GetManifestResourceStream(res)!;
            return new Icon(stream);
        }

        // fallback: generate a solid-color icon programmatically
        return CreateFallbackIcon(name.Contains("green") ? Color.FromArgb(0x44, 0xCC, 0x55)
                                : name.Contains("amber") ? Color.FromArgb(0xE8, 0xB2, 0x3C)
                                :                          Color.FromArgb(0xDD, 0x33, 0x33));
    }

    private static Icon CreateFallbackIcon(Color fill)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(fill);
        g.FillEllipse(brush, 1, 1, 14, 14);
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }
}
