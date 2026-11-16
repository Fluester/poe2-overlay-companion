using System.Threading;
using System.Windows;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class App : Application
{
    public static readonly uint WmShowExistingOverlay =
        Win32OverlayHelper.RegisterWindowMessageW("Poe2OverlayCompanion_ShowWindow");

    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, @"Local\Poe2OverlayCompanion_SingleInstance_v1", out bool createdNew);
        if (!createdNew)
        {
            Win32OverlayHelper.PostMessageW(Win32OverlayHelper.HWND_BROADCAST, WmShowExistingOverlay, IntPtr.Zero, IntPtr.Zero);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
        bool minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var win = new MainWindow(startMinimized: minimized);
        MainWindow = win;
        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
