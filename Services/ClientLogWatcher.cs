using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed class ClientLogWatcher : IDisposable
{
    private static readonly string[] CommonPaths =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\Path of Exile 2\logs\Client.txt",
        @"C:\Program Files (x86)\Grinding Gear Games\Path of Exile 2\logs\Client.txt",
        @"D:\SteamLibrary\steamapps\common\Path of Exile 2\logs\Client.txt",
        @"E:\SteamLibrary\steamapps\common\Path of Exile 2\logs\Client.txt",
        @"F:\SteamLibrary\steamapps\common\Path of Exile 2\logs\Client.txt",
        @"D:\Games\Path of Exile 2\logs\Client.txt"
    ];

    private static readonly string[] PoeProcessNames =
    [
        "PathOfExile",
        "PathOfExileSteam",
        "PathOfExile_x64",
        "PathOfExile_x64Steam",
        "PathOfExile2",
        "PathOfExile2Steam"
    ];

    private static readonly Regex LevelRegex = new(@"is now level (\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ZoneRegex = new(@"You have entered (.+?)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SlainRegex = new(@"has been slain", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WhisperRegex = new(@"@(From|To)\s+(?:<[^>]+>\s+)?([^:]+?):\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex TradeBuyEnRegex = new(@"like to buy your (.+?) listed for ([\d.,]+\s+[\w\s]+?) in ([^(]+?)(?:\s*\(stash tab ""([^""]+)""; position: left (\d+), top (\d+)\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TradeBuyRuRegex = new(@"купить у вас (.+?) за ([\d.,]+\s+[\w\s]+?) в лиге ([^(]+?)(?:\s*\(секция ""([^""]+)""; позиция: (\d+) слева, (\d+) сверху\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private long _offset;
    private FileSystemWatcher? _fsw;
    private volatile bool _dirty = true;
    private readonly List<TradeWhisperEntry> _pendingWhispers = [];

    private static IntPtr _lastFgHwnd;
    private static bool _lastFgIsPoe;

    public string? LogPath { get; private set; }
    public int? CurrentLevel { get; private set; }
    public void SetManualLevel(int level) => CurrentLevel = Math.Clamp(level, 1, 100);
    public string? CurrentZone { get; private set; }
    public string? LastWhisperPlayer { get; private set; }
    public FarmSessionStats FarmStats { get; } = new();
    public bool IsInSafeZone => IsSafeZoneName(CurrentZone);

    public List<TradeWhisperEntry> DrainNewWhispers()
    {
        lock (_pendingWhispers)
        {
            if (_pendingWhispers.Count == 0) return [];
            var copy = _pendingWhispers.ToList();
            _pendingWhispers.Clear();
            return copy;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    public ClientLogWatcher()
    {
        LogPath = FindClientTxt();
        if (LogPath != null)
        {
            InitFromTail();
            StartWatcher();
        }
    }

    public static string? FindClientTxt() => CommonPaths.FirstOrDefault(File.Exists);

    public static IntPtr FindPoe2WindowHandle()
    {
        try
        {
            IntPtr h = FindWindowW("POEWindowClass", null);
            if (h != IntPtr.Zero) return h;

            foreach (string name in PoeProcessNames)
            {
                var procs = Process.GetProcessesByName(name);
                try
                {
                    foreach (var p in procs)
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                            return p.MainWindowHandle;
                    }
                }
                finally
                {
                    foreach (var p in procs) p.Dispose();
                }
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    public static bool TryGetPoe2ClientRect(out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        try
        {
            IntPtr hwnd = FindPoe2WindowHandle();
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
            if (!GetClientRect(hwnd, out var rc)) return false;
            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            if (w < 320 || h < 200) return false;
            var pt = new POINT { X = rc.Left, Y = rc.Top };
            if (!ClientToScreen(hwnd, ref pt)) return false;
            x = pt.X;
            y = pt.Y;
            width = w;
            height = h;
            return true;
        }
        catch { return false; }
    }

    public static bool IsPoe2ProcessRunning()
    {
        try
        {
            if (FindWindowW("POEWindowClass", null) != IntPtr.Zero)
                return true;

            foreach (string name in PoeProcessNames)
            {
                var procs = Process.GetProcessesByName(name);
                try
                {
                    if (procs.Length > 0) return true;
                }
                finally
                {
                    foreach (var p in procs) p.Dispose();
                }
            }
            return false;
        }
        catch { return false; }
    }

    public static bool IsPoe2Foreground()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (fg == _lastFgHwnd) return _lastFgIsPoe;

            GetWindowThreadProcessId(fg, out uint pid);
            bool res = false;
            if (pid != 0)
            {
                using var proc = Process.GetProcessById((int)pid);
                res = proc.ProcessName.Contains("PathOfExile", StringComparison.OrdinalIgnoreCase);
            }
            _lastFgHwnd = fg;
            _lastFgIsPoe = res;
            return res;
        }
        catch { return false; }
    }

    public void SetPath(string path)
    {
        LogPath = path;
        _offset = 0;
        if (File.Exists(path))
        {
            InitFromTail();
            StartWatcher();
        }
    }

    public void ResetFarmSession()
    {
        FarmStats.SessionStartUtc = DateTime.UtcNow;
        FarmStats.MapsCompleted = 0;
        FarmStats.Deaths = 0;
        FarmStats.SpecialEncounters = 0;
        FarmStats.TotalMapSeconds = 0;
        FarmStats.LastMapSeconds = 0;
        FarmStats.CurrentMapName = "";
        FarmStats.CurrentMapEnteredUtc = null;
    }

    private void StartWatcher()
    {
        _fsw?.Dispose();
        _fsw = null;
        if (LogPath == null) return;
        string? dir = Path.GetDirectoryName(LogPath);
        string file = Path.GetFileName(LogPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        _fsw = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _fsw.Changed += (_, _) => _dirty = true;
    }

    private void InitFromTail()
    {
        if (LogPath == null || !File.Exists(LogPath)) return;
        using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long start = Math.Max(0, fs.Length - 65536);
        fs.Seek(start, SeekOrigin.Begin);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null)
            ParseLine(line, trackStats: false);
        _offset = fs.Position;
    }

    private static bool IsSafeZoneName(string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone)) return false;
        var z = zone.ToLowerInvariant();
        return z.Contains("hideout") || z.Contains("encampment") || z.Contains("caravan")
               || z.Contains("ziggurat") || z.Contains("kingsmarch") || z.Contains("refuge")
               || z.Contains("town");
    }

    private static bool IsSpecialEncounterZone(string zone)
    {
        var z = zone.ToLowerInvariant();
        return z.Contains("breach") || z.Contains("crux of nothingness") || z.Contains("citadel")
               || z.Contains("trial of chaos") || z.Contains("trial of the sekhemas")
               || z.Contains("simulacrum") || z.Contains("matriarch") || z.Contains("patriarch");
    }

    private bool ParseLine(string line, bool trackStats)
    {
        bool changed = false;

        var lm = LevelRegex.Match(line);
        if (lm.Success && int.TryParse(lm.Groups[1].Value, out int lvl))
        {
            CurrentLevel = lvl;
            changed = true;
        }

        if (trackStats && SlainRegex.IsMatch(line))
        {
            FarmStats.Deaths++;
            changed = true;
        }

        var zm = ZoneRegex.Match(line);
        if (zm.Success)
        {
            string nextZone = zm.Groups[1].Value.Trim();
            if (trackStats && !string.Equals(nextZone, CurrentZone, StringComparison.Ordinal))
                OnZoneTransition(nextZone);
            CurrentZone = nextZone;
            changed = true;
        }

        if (ParseTradeWhisper(line, enqueue: trackStats) != null)
            changed = true;

        return changed;
    }

    public TradeWhisperEntry? ParseTradeWhisper(string line, bool enqueue = false)
    {
        var wm = WhisperRegex.Match(line);
        if (!wm.Success) return null;

        bool incoming = wm.Groups[1].Value.Equals("From", StringComparison.OrdinalIgnoreCase);
        string player = wm.Groups[2].Value.Trim();
        string msg = wm.Groups[3].Value.Trim();
        if (!string.IsNullOrWhiteSpace(player) && incoming)
            LastWhisperPlayer = player;

        bool looksLikeTrade = msg.Contains("buy your", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("listed for", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("хочу купить", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("stash tab", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeTrade) return null;

        string item = msg;
        string price = "";
        string league = "";
        string tab = "";
        int left = 0, top = 0;

        var bm = TradeBuyEnRegex.Match(msg);
        if (!bm.Success) bm = TradeBuyRuRegex.Match(msg);
        if (bm.Success)
        {
            item = bm.Groups[1].Value.Trim();
            price = bm.Groups[2].Value.Trim();
            league = bm.Groups[3].Value.Trim();
            if (bm.Groups[4].Success) tab = bm.Groups[4].Value.Trim();
            if (bm.Groups[5].Success) int.TryParse(bm.Groups[5].Value, out left);
            if (bm.Groups[6].Success) int.TryParse(bm.Groups[6].Value, out top);
        }

        var entry = new TradeWhisperEntry
        {
            IsIncoming = incoming,
            PlayerName = player,
            ItemName = item,
            PriceText = price,
            League = league,
            StashTab = tab,
            StashLeft = left,
            StashTop = top,
            RawMessage = msg
        };
        if (enqueue)
        {
            lock (_pendingWhispers)
            {
                _pendingWhispers.Add(entry);
            }
        }
        return entry;
    }

    private void OnZoneTransition(string nextZone)
    {
        bool nextSafe = IsSafeZoneName(nextZone);
        if (!string.IsNullOrEmpty(FarmStats.CurrentMapName) && FarmStats.CurrentMapEnteredUtc is DateTime startUtc)
        {
            double elapsed = (DateTime.UtcNow - startUtc).TotalSeconds;
            if (elapsed is >= 8 and <= 3600)
            {
                FarmStats.MapsCompleted++;
                FarmStats.TotalMapSeconds += elapsed;
                FarmStats.LastMapSeconds = elapsed;
            }
            FarmStats.CurrentMapName = "";
            FarmStats.CurrentMapEnteredUtc = null;
        }

        if (!nextSafe)
        {
            FarmStats.CurrentMapName = nextZone;
            FarmStats.CurrentMapEnteredUtc = DateTime.UtcNow;
            if (IsSpecialEncounterZone(nextZone))
                FarmStats.SpecialEncounters++;
        }
    }

    public bool Poll(out bool zoneChanged, out bool levelChanged)
    {
        zoneChanged = false;
        levelChanged = false;
        if (LogPath == null || !File.Exists(LogPath)) return false;

        if (!_dirty)
        {
            var fi = new FileInfo(LogPath);
            if (fi.Length == _offset) return false;
        }
        _dirty = false;

        using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length < _offset) _offset = 0;
        if (fs.Length == _offset) return false;

        int? prevLvl = CurrentLevel;
        string? prevZone = CurrentZone;

        fs.Seek(_offset, SeekOrigin.Begin);
        using var sr = new StreamReader(fs);
        bool any = false;
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (ParseLine(line, trackStats: true))
                any = true;
        }
        _offset = fs.Position;

        levelChanged = CurrentLevel != prevLvl;
        zoneChanged = !string.Equals(CurrentZone, prevZone, StringComparison.Ordinal);
        return any;
    }

    public void Dispose() => _fsw?.Dispose();
}
