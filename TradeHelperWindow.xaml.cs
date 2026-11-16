using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class TradeHelperWindow : Window
{
    private readonly List<TradeWhisperEntry> _entries = [];
    private int _index;

    public event Action<string>? ExecuteChatCommand;

    public TradeHelperWindow()
    {
        InitializeComponent();
        Left = Math.Max(20, SystemParameters.WorkArea.Right - 425);
        Top = 18;
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

    public int QueueCount => _entries.Count;

    public void Enqueue(TradeWhisperEntry entry, bool showIfHidden = true)
    {
        _entries.Add(entry);
        if (_entries.Count > 15) _entries.RemoveAt(0);
        _index = _entries.Count - 1;
        RefreshUi();
        if (showIfHidden && !IsVisible) Show();
    }

    public void RefreshUi()
    {
        TitleText.Text = Loc.T("ТРЕЙД ХЕЛПЕР (1 клік = 1 команда)", "TRADE HELPER (1-Click Macros)", "ТРЕЙД ХЕЛПЕР (1 клик)");
        BtnInvite.Content = Loc.T("Інвайт", "Invite", "Инвайт");
        BtnTrade.Content = Loc.T("Трейд", "Trade", "Трейд");
        BtnWait.Content = Loc.T("Секунду", "1 min", "Секунду");
        BtnThanks.Content = Loc.T("Дякую", "Thx", "Спасибо");
        BtnSold.Content = Loc.T("Продано", "Sold", "Продано");

        if (_entries.Count == 0)
        {
            CountText.Text = "0/0";
            PlayerText.Text = Loc.T("Очікування trade-шепоту з Client.txt", "Waiting for trade whispers from Client.txt", "Ожидание trade-шепота из Client.txt");
            PriceText.Text = "";
            ItemText.Text = Loc.T("Щойно хтось напише про покупку — картка з'явиться тут.", "Incoming/outgoing trade whispers appear here automatically.", "Входящие/исходящие трейд-шепоты появятся здесь.");
            StashText.Text = "";
            return;
        }

        _index = Math.Clamp(_index, 0, _entries.Count - 1);
        var cur = _entries[_index];
        CountText.Text = $"{_index + 1}/{_entries.Count}";
        string dir = cur.IsIncoming ? "← FROM" : "→ TO";
        PlayerText.Text = $"{dir} {cur.PlayerName}";
        PriceText.Text = cur.PriceText;
        ItemText.Text = cur.ItemName;
        StashText.Text = !string.IsNullOrWhiteSpace(cur.StashTab)
            ? $"Stash: \"{cur.StashTab}\" (X:{cur.StashLeft}, Y:{cur.StashTop}) · {cur.League}"
            : cur.League;
    }

    private TradeWhisperEntry? Current => _entries.Count > 0 ? _entries[Math.Clamp(_index, 0, _entries.Count - 1)] : null;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) { if (_entries.Count > 1) { _index = (_index - 1 + _entries.Count) % _entries.Count; RefreshUi(); } }
    private void Next_Click(object sender, RoutedEventArgs e) { if (_entries.Count > 1) { _index = (_index + 1) % _entries.Count; RefreshUi(); } }

    private void DismissCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0) { Hide(); return; }
        _entries.RemoveAt(Math.Clamp(_index, 0, _entries.Count - 1));
        if (_entries.Count == 0) Hide();
        else { _index = Math.Clamp(_index, 0, _entries.Count - 1); RefreshUi(); }
    }

    private void Invite_Click(object sender, RoutedEventArgs e) { if (Current is { PlayerName.Length: > 0 } c) ExecuteChatCommand?.Invoke($"/invite {c.PlayerName}"); }
    private void Trade_Click(object sender, RoutedEventArgs e) { if (Current is { PlayerName.Length: > 0 } c) ExecuteChatCommand?.Invoke($"/tradewith {c.PlayerName}"); }
    private void Hideout_Click(object sender, RoutedEventArgs e) { if (Current is { PlayerName.Length: > 0 } c) ExecuteChatCommand?.Invoke($"/hideout {c.PlayerName}"); }
    private void Wait_Click(object sender, RoutedEventArgs e) { if (Current is { PlayerName.Length: > 0 } c) ExecuteChatCommand?.Invoke($"@{c.PlayerName} 1 min please, finishing map!"); }
    private void Thanks_Click(object sender, RoutedEventArgs e) { if (Current is { PlayerName.Length: > 0 } c) ExecuteChatCommand?.Invoke($"@{c.PlayerName} thanks, gl!"); }
    private void Sold_Click(object sender, RoutedEventArgs e) { if (Current is { PlayerName.Length: > 0 } c) ExecuteChatCommand?.Invoke($"@{c.PlayerName} sorry, sold"); }
    private void Kick_Click(object sender, RoutedEventArgs e) { if (Current is { PlayerName.Length: > 0 } c) ExecuteChatCommand?.Invoke($"/kick {c.PlayerName}"); }
}
