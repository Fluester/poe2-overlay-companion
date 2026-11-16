using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Poe2OverlayCompanion.Localization;

namespace Poe2OverlayCompanion.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint WM_APP_TRAY = 0x8001;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONUP = 0x0202;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_CHECKED = 0x0008;

    private const int CMD_SHOW_HIDE = 1001;
    private const int CMD_MINI_HUD = 1002;
    private const int CMD_LOCK = 1003;
    private const int CMD_INSTALL_FILTER = 1004;
    private const int CMD_AUTOSTART = 1005;
    private const int CMD_EXIT = 1099;

    private const string RunRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Poe2OverlayCompanion";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private HwndSource? _source;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _ownsIcon;
    private bool _added;
    private NOTIFYICONDATA _nid;

    public Action? OnToggleShow { get; set; }
    public Action? OnToggleMiniHud { get; set; }
    public Action? OnToggleClickThrough { get; set; }
    public Action? OnInstallFilter { get; set; }
    public Action<bool>? OnAutostartChanged { get; set; }
    public Action? OnExitRequested { get; set; }
    public Func<bool>? IsClickThrough { get; set; }

    public void Attach(Window owner)
    {
        if (_added) return;
        _hwnd = new WindowInteropHelper(owner).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        _hIcon = LoadSmallAppIcon(out _ownsIcon);
        _nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _hIcon,
            szTip = "PoE 2 Overlay Companion"
        };
        _added = Shell_NotifyIconW(NIM_ADD, ref _nid);
    }

    public void UpdateTooltip(string tip)
    {
        if (!_added) return;
        _nid.uFlags = NIF_TIP;
        _nid.szTip = tip.Length > 120 ? tip[..120] : tip;
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY)
        {
            uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
            if (evt == WM_LBUTTONDBLCLK || evt == WM_LBUTTONUP)
            {
                OnToggleShow?.Invoke();
                handled = true;
            }
            else if (evt == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            bool locked = IsClickThrough?.Invoke() == true;
            bool auto = IsAutostartEnabled();

            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_SHOW_HIDE, Loc.T("Показати / Сховати (F9)", "Show / Hide (F9)", "Показать / Скрыть (F9)"));
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_MINI_HUD, Loc.T("Режим Mini-HUD", "Mini-HUD Mode", "Режим Mini-HUD"));
            AppendMenuW(menu, MF_STRING | (locked ? MF_CHECKED : 0), (UIntPtr)CMD_LOCK, Loc.T("Клік-наскрізь (F10)", "Click-Through Lock (F10)", "Клик-сквозь (F10)"));
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_INSTALL_FILTER, Loc.T("Встановити .filter у PoE 2", "Install .filter to PoE 2", "Установить .filter в PoE 2"));
            AppendMenuW(menu, MF_STRING | (auto ? MF_CHECKED : 0), (UIntPtr)CMD_AUTOSTART, Loc.T("Запускати разом з Windows", "Launch with Windows", "Запускать с Windows"));
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)CMD_EXIT, Loc.T("Вийти з програми", "Exit Overlay", "Выйти из программы"));

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            switch (cmd)
            {
                case CMD_SHOW_HIDE: OnToggleShow?.Invoke(); break;
                case CMD_MINI_HUD: OnToggleMiniHud?.Invoke(); break;
                case CMD_LOCK: OnToggleClickThrough?.Invoke(); break;
                case CMD_INSTALL_FILTER: OnInstallFilter?.Invoke(); break;
                case CMD_AUTOSTART:
                    bool next = !auto;
                    SetAutostartEnabled(next);
                    OnAutostartChanged?.Invoke(next);
                    break;
                case CMD_EXIT: OnExitRequested?.Invoke(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static IntPtr LoadSmallAppIcon(out bool owns)
    {
        owns = false;
        string? exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var small = new IntPtr[1];
            uint count = ExtractIconExW(exe, 0, null, small, 1);
            if (count > 0 && small[0] != IntPtr.Zero)
            {
                owns = true;
                return small[0];
            }
        }
        return LoadIconW(IntPtr.Zero, new IntPtr(32512));
    }

    public static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegPath, writable: false);
            return key?.GetValue(RunValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch { return false; }
    }

    public static bool SetAutostartEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunRegPath);
            if (key == null) return false;
            if (enabled)
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return false;
                key.SetValue(RunValueName, $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_added)
        {
            Shell_NotifyIconW(NIM_DELETE, ref _nid);
            _added = false;
        }
        _source?.RemoveHook(WndProc);
        if (_ownsIcon && _hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
