using System.IO;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Визначає папку даних додатка:
/// Portable режим (поруч з .exe у папці Data\, якщо є файл portable.mode або прапорець --portable)
/// або стандартний Windows профіль (%LocalAppData%\Poe2OverlayCompanion).
/// </summary>
public static class AppPaths
{
    public static bool IsPortableMode { get; } = DetectPortableMode();

    private static bool DetectPortableMode()
    {
        try
        {
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase)))
                return true;
            string baseDir = AppContext.BaseDirectory;
            return File.Exists(Path.Combine(baseDir, "portable.mode")) ||
                   File.Exists(Path.Combine(baseDir, "portable.txt"));
        }
        catch
        {
            return false;
        }
    }

    public static string GetAppDataDirectory()
    {
        string dir = IsPortableMode
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Poe2OverlayCompanion");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetWebView2CacheDirectory()
    {
        string dir = Path.Combine(GetAppDataDirectory(), "WebView2Cache");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
