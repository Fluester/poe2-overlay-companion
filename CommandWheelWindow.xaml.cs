using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class CommandWheelWindow : Window
{
    private string _custom1 = "/hideout";
    private string _custom2 = "/remaining";
    private string _lastWhisperPlayer = "";

    public event Action<string>? ExecuteChatCommand;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    public CommandWheelWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = PresentationSource.FromVisual(this) as HwndSource;
        src?.AddHook(WndProc);
        Win32OverlayHelper.ApplyDwmRoundedCorners(this);
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: true, clickThrough: false);
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

    public void ToggleNearCursor(string? lastPlayer, string custom1, string custom2)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        _lastWhisperPlayer = lastPlayer ?? "";
        _custom1 = string.IsNullOrWhiteSpace(custom1) ? "/hideout" : custom1.Trim();
        _custom2 = string.IsNullOrWhiteSpace(custom2) ? "/remaining" : custom2.Trim();

        TitleText.Text = Loc.T("ШВИДКІ КОМАНДИ POE 2 (F8)", "POE 2 COMMAND WHEEL (F8)", "БЫСТРЫЕ КОМАНДЫ POE 2 (F8)");
        BtnCustom1.Content = _custom1;
        BtnCustom2.Content = _custom2;
        BtnLastThanks.IsEnabled = !string.IsNullOrWhiteSpace(_lastWhisperPlayer);
        BtnLastWait.IsEnabled = !string.IsNullOrWhiteSpace(_lastWhisperPlayer);
        BtnLastThanks.Content = string.IsNullOrWhiteSpace(_lastWhisperPlayer) ? "@Last Thx" : $"@{_lastWhisperPlayer} Thx";
        BtnLastWait.Content = string.IsNullOrWhiteSpace(_lastWhisperPlayer) ? "@Last Wait" : $"@{_lastWhisperPlayer} 1m";

        var wa = SystemParameters.WorkArea;
        if (GetCursorPos(out var pt))
        {
            Left = Math.Clamp(pt.X - 175, wa.Left + 8, wa.Right - 360);
            Top = Math.Clamp(pt.Y - 70, wa.Top + 8, wa.Bottom - 160);
        }
        else
        {
            Left = wa.Left + (wa.Width - 350) / 2;
            Top = wa.Top + (wa.Height - 140) / 2;
        }

        Show();
        Win32OverlayHelper.EnsureTopmostNoActivate(this);
    }

    private void SendAndClose(string cmd)
    {
        Hide();
        ExecuteChatCommand?.Invoke(cmd);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void RunHideout_Click(object sender, RoutedEventArgs e) => SendAndClose("/hideout");
    private void RunKingsmarch_Click(object sender, RoutedEventArgs e) => SendAndClose("/kingsmarch");
    private void RunRemaining_Click(object sender, RoutedEventArgs e) => SendAndClose("/remaining");
    private void RunPassives_Click(object sender, RoutedEventArgs e) => SendAndClose("/passives");
    private void RunDeaths_Click(object sender, RoutedEventArgs e) => SendAndClose("/deaths");
    private void RunLastThanks_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(_lastWhisperPlayer)) SendAndClose($"@{_lastWhisperPlayer} thanks, gl!"); }
    private void RunLastWait_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(_lastWhisperPlayer)) SendAndClose($"@{_lastWhisperPlayer} 1 min please, finishing map!"); }
    private void RunCustom1_Click(object sender, RoutedEventArgs e) => SendAndClose(_custom1);
    private void RunCustom2_Click(object sender, RoutedEventArgs e) => SendAndClose(_custom2);
}
