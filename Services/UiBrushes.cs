using System.Windows.Media;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Кешовані заморожені пензлі у стилі темного фентезі PoE 2 (Obsidian Slate + Antique Brass) без зайвих алокацій.
/// </summary>
public static class UiBrushes
{
    public static readonly SolidColorBrush Transparent = Brushes.Transparent;

    public static readonly SolidColorBrush BgWindow = Freeze(0x0A, 0x0B, 0x0D);
    public static readonly SolidColorBrush BgSidebar = Freeze(0x0F, 0x11, 0x15);
    public static readonly SolidColorBrush BgCard = Freeze(0x13, 0x15, 0x1B);
    public static readonly SolidColorBrush BgCardElevated = Freeze(0x18, 0x1B, 0x22);
    public static readonly SolidColorBrush BgInput = Freeze(0x0C, 0x0E, 0x12);
    public static readonly SolidColorBrush BgNavActive = Freeze(0x19, 0x1B, 0x21);

    public static readonly SolidColorBrush BgBrass = Freeze(0xC4, 0x9E, 0x57);
    public static readonly SolidColorBrush BgBrassDim = Freeze(0x1E, 0x19, 0x10);
    public static readonly SolidColorBrush BgEmeraldDim = Freeze(0x12, 0x20, 0x1A);
    public static readonly SolidColorBrush BgSteelDim = Freeze(0x15, 0x1D, 0x28);
    public static readonly SolidColorBrush BgCrimsonDim = Freeze(0x23, 0x13, 0x15);

    public static readonly SolidColorBrush BorderSubtle = Freeze(0x22, 0x25, 0x2E);
    public static readonly SolidColorBrush BorderBrass = Freeze(0x6E, 0x59, 0x32);
    public static readonly SolidColorBrush BorderEmerald = Freeze(0x2B, 0x55, 0x42);
    public static readonly SolidColorBrush BorderSteel = Freeze(0x31, 0x45, 0x62);
    public static readonly SolidColorBrush BorderCrimson = Freeze(0x5E, 0x29, 0x2D);

    public static readonly SolidColorBrush FgPrimary = Freeze(0xE4, 0xDF, 0xD5);
    public static readonly SolidColorBrush FgDark = Freeze(0x0A, 0x0B, 0x0D);
    public static readonly SolidColorBrush FgMuted = Freeze(0x84, 0x8B, 0x98);
    public static readonly SolidColorBrush FgBrass = Freeze(0xC8, 0xA4, 0x62);
    public static readonly SolidColorBrush FgEmerald = Freeze(0x52, 0xC4, 0x8B);
    public static readonly SolidColorBrush FgSteel = Freeze(0x6B, 0xA3, 0xDB);
    public static readonly SolidColorBrush FgAmber = Freeze(0xD8, 0xA0, 0x4C);
    public static readonly SolidColorBrush FgCrimson = Freeze(0xDC, 0x62, 0x62);

    // Socket / attribute colors
    public static readonly SolidColorBrush SocketStr = Freeze(0xC9, 0x4B, 0x4B);
    public static readonly SolidColorBrush SocketDex = Freeze(0x4E, 0xB8, 0x6E);
    public static readonly SolidColorBrush SocketInt = Freeze(0x52, 0x95, 0xE0);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush ForColor(Color c) => (c.R, c.G, c.B) switch
    {
        (0x4E, 0xCF, 0x8F) or (0x3D, 0xDC, 0x84) or (0x56, 0xE3, 0x9F) or (0x7E, 0xE7, 0x87) => FgEmerald,
        (0xDF, 0xA7, 0x4A) or (0xE3, 0xB3, 0x41) or (0xF0, 0x88, 0x3E) => FgAmber,
        (0xEA, 0x61, 0x61) or (0xFF, 0x4D, 0x4F) or (0xFF, 0x6B, 0x6B) => FgCrimson,
        (0xD4, 0xB0, 0x6A) or (0xC8, 0xA2, 0x5A) or (0xE2, 0xB9, 0x6F) => FgBrass,
        (0x69, 0xAC, 0xEC) or (0x4E, 0xA8, 0xFF) => FgSteel,
        _ => FgMuted
    };
}
