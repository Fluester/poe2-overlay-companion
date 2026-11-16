using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hideTimer;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    public ToastWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: true, clickThrough: true);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32OverlayHelper.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(Win32OverlayHelper.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    public void ShowToast(string title, string body, Color accentColor, bool nearCursor = false, double seconds = 5.0)
    {
        var accent = UiBrushes.ForColor(accentColor);
        TitleText.Text = title;
        TitleText.Foreground = accent;
        CardBorder.BorderBrush = accent;
        BodyText.Text = body;

        UpdateLayout();

        var workArea = SystemParameters.WorkArea;
        if (nearCursor && GetCursorPos(out var pt))
        {
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
            double left = pt.X * dpiX + 18;
            double top = pt.Y * dpiY + 18;
            if (left + ActualWidth > workArea.Right) left = workArea.Right - ActualWidth - 12;
            if (top + ActualHeight > workArea.Bottom) top = workArea.Bottom - ActualHeight - 12;
            Left = Math.Max(workArea.Left + 8, left);
            Top = Math.Max(workArea.Top + 8, top);
        }
        else
        {
            Left = workArea.Right - ActualWidth - 18;
            Top = workArea.Top + 18;
        }

        if (!IsVisible) Show();
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: true, clickThrough: true);

        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromSeconds(seconds);
        _hideTimer.Start();
    }

    public void UpdateBodyIfVisible(string newBody)
    {
        if (!IsVisible) return;
        BodyText.Text = newBody;
        UpdateLayout();
    }
}
