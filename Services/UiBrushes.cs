using System.Windows.Media;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Кешовані заморожені пензлі у стилі PoE 2 Obsidian (плоскі матові кольори, стримана бронза, без градієнтів і неону).
/// </summary>
public static class UiBrushes
{
    public static readonly SolidColorBrush Transparent = Brushes.Transparent;

    // PoE 2 Obsidian Surfaces
    public static readonly SolidColorBrush BgWindow = Freeze(0x12, 0x11, 0x10);      // #121110
    public static readonly SolidColorBrush BgSidebar = Freeze(0x1B, 0x19, 0x17);     // #1B1917
    public static readonly SolidColorBrush BgCard = Freeze(0x1B, 0x19, 0x17);        // #1B1917
    public static readonly SolidColorBrush BgCardElevated = Freeze(0x25, 0x22, 0x1F);// #25221F
    public static readonly SolidColorBrush BgInput = Freeze(0x25, 0x22, 0x1F);       // #25221F
    public static readonly SolidColorBrush BgNavActive = Freeze(0xC8, 0x9B, 0x3C);   // #C89B3C

    // PoE 2 Matte Bronze & Semantic Accents
    public static readonly SolidColorBrush BgBrass = Freeze(0xC8, 0x9B, 0x3C);       // #C89B3C
    public static readonly SolidColorBrush BgBrassDim = Freeze(0x2E, 0x26, 0x18);    // #2E2618
    public static readonly SolidColorBrush BgEmeraldDim = Freeze(0x15, 0x24, 0x18);  // #152418
    public static readonly SolidColorBrush BgSteelDim = Freeze(0x22, 0x20, 0x1D);    // #22201D
    public static readonly SolidColorBrush BgCrimsonDim = Freeze(0x2A, 0x16, 0x16);  // #2A1616

    public static readonly SolidColorBrush BorderSubtle = Freeze(0x34, 0x2F, 0x2A);  // #342F2A
    public static readonly SolidColorBrush BorderBrass = Freeze(0x5C, 0x48, 0x22);   // #5C4822
    public static readonly SolidColorBrush BorderEmerald = Freeze(0x2A, 0x52, 0x31); // #2A5231
    public static readonly SolidColorBrush BorderSteel = Freeze(0x3D, 0x38, 0x32);   // #3D3832
    public static readonly SolidColorBrush BorderCrimson = Freeze(0x63, 0x2A, 0x2A); // #632A2A

    public static readonly SolidColorBrush FgPrimary = Freeze(0xEC, 0xE8, 0xE1);     // #ECE8E1
    public static readonly SolidColorBrush FgDark = Freeze(0x12, 0x11, 0x10);        // #121110 (текст на #C89B3C кнопках)
    public static readonly SolidColorBrush FgMuted = Freeze(0x9C, 0x95, 0x8B);       // #9C958B
    public static readonly SolidColorBrush FgBrass = Freeze(0xC8, 0x9B, 0x3C);       // #C89B3C
    public static readonly SolidColorBrush FgEmerald = Freeze(0x3F, 0xB9, 0x50);     // #3FB950
    public static readonly SolidColorBrush FgSteel = Freeze(0xB5, 0xAE, 0xA4);       // #B5AEA4
    public static readonly SolidColorBrush FgAmber = Freeze(0xD2, 0x99, 0x22);       // #D29922
    public static readonly SolidColorBrush FgCrimson = Freeze(0xE0, 0x52, 0x52);     // #E05252

    // Socket / attribute colors
    public static readonly SolidColorBrush SocketStr = Freeze(0xE0, 0x52, 0x52);
    public static readonly SolidColorBrush SocketDex = Freeze(0x3F, 0xB9, 0x50);
    public static readonly SolidColorBrush SocketInt = Freeze(0x6C, 0x98, 0xC6);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush ForColor(Color c) => (c.R, c.G, c.B) switch
    {
        (0x3F, 0xB9, 0x50) or (0x4E, 0xCF, 0x8F) or (0x75, 0xB7, 0x98) or (0x19, 0x87, 0x54) => FgEmerald,
        (0xD2, 0x99, 0x22) or (0xDF, 0xA7, 0x4A) or (0xE3, 0xB3, 0x41) or (0xFF, 0xDA, 0x6A) => FgAmber,
        (0xE0, 0x52, 0x52) or (0xEA, 0x61, 0x61) or (0xEA, 0x86, 0x8F) or (0xDC, 0x35, 0x45) => FgCrimson,
        (0xC8, 0x9B, 0x3C) or (0xD9, 0xAC, 0x4D) or (0xD4, 0xB0, 0x6A) or (0xC8, 0xA2, 0x5A) => FgBrass,
        (0xB5, 0xAE, 0xA4) or (0x69, 0xAC, 0xEC) or (0x6E, 0xDF, 0xF6) => FgSteel,
        _ => FgMuted
    };
}
