using System.Windows.Media;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Кешовані заморожені пензлі у стилі Bootstrap 5.3 Dark (data-bs-theme="dark") без зайвих алокацій.
/// </summary>
public static class UiBrushes
{
    public static readonly SolidColorBrush Transparent = Brushes.Transparent;

    // Bootstrap 5.3 Dark Surfaces
    public static readonly SolidColorBrush BgWindow = Freeze(0x16, 0x19, 0x1D);      // --bs-tertiary-bg
    public static readonly SolidColorBrush BgSidebar = Freeze(0x1B, 0x1F, 0x23);
    public static readonly SolidColorBrush BgCard = Freeze(0x21, 0x25, 0x29);        // --bs-body-bg / .card
    public static readonly SolidColorBrush BgCardElevated = Freeze(0x2B, 0x30, 0x35);// --bs-secondary-bg
    public static readonly SolidColorBrush BgInput = Freeze(0x16, 0x19, 0x1D);
    public static readonly SolidColorBrush BgNavActive = Freeze(0x0D, 0x6E, 0xFD);   // --bs-primary (.nav-pills .active)

    // Bootstrap Semantic Accents (Primary / Success / Info / Danger)
    public static readonly SolidColorBrush BgBrass = Freeze(0x0D, 0x6E, 0xFD);       // .btn-primary / .nav-pills active
    public static readonly SolidColorBrush BgBrassDim = Freeze(0x03, 0x16, 0x33);    // --bs-primary-bg-subtle
    public static readonly SolidColorBrush BgEmeraldDim = Freeze(0x05, 0x1B, 0x11);  // --bs-success-bg-subtle
    public static readonly SolidColorBrush BgSteelDim = Freeze(0x03, 0x28, 0x30);    // --bs-info-bg-subtle
    public static readonly SolidColorBrush BgCrimsonDim = Freeze(0x2C, 0x0B, 0x0E);  // --bs-danger-bg-subtle

    public static readonly SolidColorBrush BorderSubtle = Freeze(0x34, 0x3A, 0x40);  // --bs-border-color
    public static readonly SolidColorBrush BorderBrass = Freeze(0x08, 0x42, 0x98);   // --bs-primary-border-subtle
    public static readonly SolidColorBrush BorderEmerald = Freeze(0x0F, 0x51, 0x32); // --bs-success-border-subtle
    public static readonly SolidColorBrush BorderSteel = Freeze(0x08, 0x79, 0x90);   // --bs-info-border-subtle
    public static readonly SolidColorBrush BorderCrimson = Freeze(0x84, 0x20, 0x29); // --bs-danger-border-subtle

    public static readonly SolidColorBrush FgPrimary = Freeze(0xDE, 0xE2, 0xE6);     // --bs-body-color
    public static readonly SolidColorBrush FgDark = Freeze(0xFF, 0xFF, 0xFF);        // --bs-white (на #0D6EFD кнопках/pills)
    public static readonly SolidColorBrush FgMuted = Freeze(0xAD, 0xB5, 0xBD);       // --bs-secondary-color
    public static readonly SolidColorBrush FgBrass = Freeze(0x6E, 0xA8, 0xFE);       // --bs-primary-text-emphasis
    public static readonly SolidColorBrush FgEmerald = Freeze(0x75, 0xB7, 0x98);     // --bs-success-text-emphasis
    public static readonly SolidColorBrush FgSteel = Freeze(0x6E, 0xDF, 0xF6);       // --bs-info-text-emphasis
    public static readonly SolidColorBrush FgAmber = Freeze(0xFF, 0xDA, 0x6A);       // --bs-warning-text-emphasis
    public static readonly SolidColorBrush FgCrimson = Freeze(0xEA, 0x86, 0x8F);     // --bs-danger-text-emphasis

    // Socket / attribute colors
    public static readonly SolidColorBrush SocketStr = Freeze(0xEA, 0x86, 0x8F);
    public static readonly SolidColorBrush SocketDex = Freeze(0x75, 0xB7, 0x98);
    public static readonly SolidColorBrush SocketInt = Freeze(0x6E, 0xA8, 0xFE);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush ForColor(Color c) => (c.R, c.G, c.B) switch
    {
        (0x4E, 0xCF, 0x8F) or (0x3D, 0xDC, 0x84) or (0x56, 0xE3, 0x9F) or (0x7E, 0xE7, 0x87) or (0x75, 0xB7, 0x98) or (0x19, 0x87, 0x54) => FgEmerald,
        (0xDF, 0xA7, 0x4A) or (0xE3, 0xB3, 0x41) or (0xF0, 0x88, 0x3E) or (0xFF, 0xDA, 0x6A) or (0xFF, 0xC1, 0x07) => FgAmber,
        (0xEA, 0x61, 0x61) or (0xFF, 0x4D, 0x4F) or (0xFF, 0x6B, 0x6B) or (0xEA, 0x86, 0x8F) or (0xDC, 0x35, 0x45) => FgCrimson,
        (0xD4, 0xB0, 0x6A) or (0xC8, 0xA2, 0x5A) or (0xE2, 0xB9, 0x6F) or (0x6E, 0xA8, 0xFE) or (0x0D, 0x6E, 0xFD) => FgBrass,
        (0x69, 0xAC, 0xEC) or (0x4E, 0xA8, 0xFF) or (0x6E, 0xDF, 0xF6) => FgSteel,
        _ => FgMuted
    };
}
