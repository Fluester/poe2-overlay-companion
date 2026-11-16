using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class MiniHudWindow : Window
{
    public event Action? ExpandRequested;
    public event Action? AiRequested;
    public event Action? LockRequested;

    public MiniHudWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
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

    public void UpdateHud(string buildShort, string infoLine, string buffText, string expandText, bool clickThrough)
    {
        HudBuildText.Text = buildShort;
        HudInfoText.Text = infoLine;
        HudBuffText.Text = buffText;
        BtnHudExpand.Content = expandText;
        BtnHudLock.Content = clickThrough ? "LOCK F10" : "FREE F10";
        BtnHudLock.Foreground = clickThrough ? UiBrushes.FgAmber : UiBrushes.FgEmerald;

        if (IsVisible)
            Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: true, clickThrough: clickThrough);
    }

    private void Hud_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void ExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke();
    private void AiClick(object sender, RoutedEventArgs e) => AiRequested?.Invoke();
    private void LockClick(object sender, RoutedEventArgs e) => LockRequested?.Invoke();
}
