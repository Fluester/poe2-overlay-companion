using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class PoeAccountLoginWindow : Window
{
    private readonly GeminiAdvisorService _ai;
    private readonly BuildProfileManager _profiles;
    private readonly bool _stashModeOnly;
    private string _accountName = "";
    private List<PoeAccountCharacter> _chars = [];

    public bool SyncedOk { get; private set; }
    public string ResultMessage { get; private set; } = "";
    public StashScanResult? ScannedStash { get; private set; }

    public PoeAccountLoginWindow(GeminiAdvisorService ai, BuildProfileManager profiles, bool stashModeOnly = false)
    {
        InitializeComponent();
        _ai = ai;
        _profiles = profiles;
        _stashModeOnly = stashModeOnly;

        Title = Loc.T("Підключення акаунта PoE 2 — pathofexile2.com", "Connect PoE 2 Account — pathofexile2.com", "Подключение аккаунта PoE 2 — pathofexile2.com");
        BtnFetchChars.Content = Loc.T("Завантажити персонажів", "Load Characters", "Загрузить персонажей");
        BtnSyncSelected.Content = Loc.T("Синхронізувати (Рівень + Шмот + Пасивки)", "Sync (Level + Gear + Tree)", "Синхронизировать (Ур + Шмот + Дерево)");
        BtnScanStash.Content = Loc.T("Оцінити скриню (Stash)", "Scan Stash Net-Worth", "Оценить тайник (Stash)");
        ChkImportAsBuild.Content = Loc.T("+ створити профіль білду", "+ save as build profile", "+ сохранить как билд");

        Loaded += PoeAccountLoginWindow_Loaded;
        Closed += (_, _) => { try { WebView.Dispose(); } catch { } };
    }

    private async void PoeAccountLoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(version))
            {
                StatusText.Text = "Edge WebView2 Runtime missing.";
                return;
            }
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: AppPaths.GetWebView2CacheDirectory());
            await WebView.EnsureCoreWebView2Async(env);
            WebView.NavigationCompleted += async (_, args) =>
            {
                string host = WebView.Source?.Host ?? "";
                if (args.IsSuccess && (host.Contains("pathofexile2.com", StringComparison.OrdinalIgnoreCase) || host.Contains("pathofexile.com", StringComparison.OrdinalIgnoreCase)))
                {
                    await TryAutoLoadCharactersAsync();
                    if (_stashModeOnly)
                        await RunStashScanAsync(closeOnSuccess: true);
                }
            };
            WebView.CoreWebView2.Navigate("https://pathofexile2.com/my-account/characters");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void FetchCharacters_Click(object sender, RoutedEventArgs e) => await TryAutoLoadCharactersAsync();

    private async Task TryAutoLoadCharactersAsync()
    {
        if (WebView.CoreWebView2 == null) return;
        try
        {
            string raw = await WebView.CoreWebView2.ExecuteScriptAsync(PoeAccountService.FetchCharactersAndDataScript);
            string unescaped = raw.StartsWith("\"") ? JsonSerializer.Deserialize<string>(raw) ?? "" : raw;
            if (string.IsNullOrWhiteSpace(unescaped)) return;

            var payload = JsonSerializer.Deserialize<PoeAccountSyncPayload>(unescaped, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload == null || !payload.ok || payload.characters.Count == 0)
            {
                StatusText.Text = payload?.error ?? Loc.T("Увійди в акаунт вище й натисни «Завантажити персонажів».", "Sign in above then click 'Load Characters'.", "Войди в аккаунт и нажми «Загрузить персонажей».");
                return;
            }

            _accountName = payload.accountName;
            _chars = payload.characters;
            CharCombo.Items.Clear();
            int pickIdx = 0;
            for (int i = 0; i < _chars.Count; i++)
            {
                var c = _chars[i];
                CharCombo.Items.Add($"{c.name} · Lv {c.level} {c.@class} ({c.league})");
                if (!string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName) &&
                    c.name.Equals(_ai.Config.PoeCharacterName, StringComparison.OrdinalIgnoreCase))
                    pickIdx = i;
            }
            if (CharCombo.Items.Count > 0) CharCombo.SelectedIndex = pickIdx;
            StatusText.Text = Loc.T($"Знайдено {_chars.Count} персонаж(ів). Обери потрібного й натисни «Синхронізувати».", $"Found {_chars.Count} character(s). Click Sync to import level & gear.", $"Найдено {_chars.Count} персонаж(ей). Выбери и нажми «Синхронизировать».");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void SyncSelected_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 == null || _chars.Count == 0) return;
        int idx = CharCombo.SelectedIndex;
        if (idx < 0 || idx >= _chars.Count) idx = 0;
        var chosen = _chars[idx];

        StatusText.Text = Loc.T($"Завантажую екіп та пасивки для {chosen.name}...", $"Syncing items & passives for {chosen.name}...", $"Загружаю экип и пассивки для {chosen.name}...");
        try
        {
            string script = PoeAccountService.BuildFetchCharacterDetailsScript(chosen.name, _accountName);
            string raw = await WebView.CoreWebView2.ExecuteScriptAsync(script);
            string unescaped = raw.StartsWith("\"") ? JsonSerializer.Deserialize<string>(raw) ?? "" : raw;
            var details = JsonSerializer.Deserialize<PoeAccountSyncPayload>(unescaped, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var gear = details?.equippedBySlot ?? [];
            var passives = details?.passiveHashes ?? [];
            PoeAccountService.ApplySyncedCharacterToConfig(_ai.Config, _accountName, chosen, gear, out int slotCount);
            _ai.SaveConfig();

            if (ChkImportAsBuild.IsChecked == true)
                PoeAccountService.CreateLiveAccountBuildProfile(_profiles, chosen, gear, passives);

            SyncedOk = true;
            ResultMessage = Loc.T(
                $"Акаунт синхронізовано: {chosen.name} · Рів.{chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} слотів екіпу.",
                $"Account synced: {chosen.name} · Lvl {chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} equipped slots.",
                $"Аккаунт синхронизирован: {chosen.name} · Ур.{chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} слотов.");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void ScanStash_Click(object sender, RoutedEventArgs e) => await RunStashScanAsync(closeOnSuccess: true);

    private async Task RunStashScanAsync(bool closeOnSuccess)
    {
        if (WebView.CoreWebView2 == null) return;
        string league = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Standard" : _ai.Config.TradeLeague;
        StatusText.Text = Loc.T($"Сканую скриню PoE 2 ({league}, до 4 вкладок)...", $"Scanning PoE 2 stash ({league}, up to 4 tabs)...", $"Сканирую тайник PoE 2 ({league})...");
        try
        {
            string script = StashTrackerService.BuildFetchStashScript(league, maxTabs: 4);
            string raw = await WebView.CoreWebView2.ExecuteScriptAsync(script);
            string unescaped = raw.StartsWith("\"") ? JsonSerializer.Deserialize<string>(raw) ?? "" : raw;
            using var doc = JsonDocument.Parse(unescaped);
            if (!doc.RootElement.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
            {
                string err = doc.RootElement.TryGetProperty("error", out var errEl) ? errEl.GetString() ?? "Error" : "Sign in first.";
                StatusText.Text = err;
                return;
            }

            int tabs = doc.RootElement.TryGetProperty("tabsScanned", out var tbEl) ? tbEl.GetInt32() : 1;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("counts", out var cntObj) && cntObj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in cntObj.EnumerateObject())
                    counts[prop.Name] = prop.Value.GetInt32();
            }

            double exPerDiv = await PoeTradePriceService.EstimateExaltedPerDivineAsync(league);
            var scan = StashTrackerService.EvaluateCounts(league, tabs, counts, exPerDiv);
            ScannedStash = scan;

            _ai.Config.LastStashNetWorthDiv = scan.TotalDivine;
            if (_ai.Config.SessionStartNetWorthDiv <= 0)
                _ai.Config.SessionStartNetWorthDiv = scan.TotalDivine;
            _ai.Config.LastStashScanUtc = scan.ScannedUtc;
            _ai.Config.LastStashTopLines = scan.BreakdownLines;
            _ai.SaveConfig();

            ResultMessage = Loc.T(
                $"Скриня ({league}): ~{scan.TotalDivine:F2} Div ({scan.TotalExalted:0.#} Ex)",
                $"Stash ({league}): ~{scan.TotalDivine:F2} Div ({scan.TotalExalted:0.#} Ex)",
                $"Тайник ({league}): ~{scan.TotalDivine:F2} Div ({scan.TotalExalted:0.#} Ex)");
            StatusText.Text = ResultMessage;
            if (closeOnSuccess)
            {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
