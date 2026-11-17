using System.Text.Json;
using System.Windows;
using System.Windows.Input;
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
    private string _latestCharId = "";
    private List<PoeAccountCharacter> _chars = [];
    private bool _autoLoadingChars;
    private bool _pendingStashScan;
    private bool _isClosed;

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
        BtnSyncSelected.Content = Loc.T("Синхронізувати (Рівень + Шмот + Скіли)", "Sync (Level + Gear + Skills)", "Синхронизировать (Ур + Шмот + Скиллы)");
        BtnScanStash.Content = Loc.T("Оцінити скриню (Stash)", "Scan Stash Net-Worth", "Оценить тайник (Stash)");
        BtnGoChars.Content = Loc.T("👤 Персонажі", "👤 Characters", "👤 Персонажи");
        BtnOpenExternal.Content = Loc.T("🌐 Браузер", "🌐 Browser", "🌐 Браузер");
        TxtSyncNote.Text = Loc.T("Оновлює персонажа (без перезапису білду)", "Syncs live character (keeps build separate)", "Обновляет персонажа (без замены билда)");

        Loaded += PoeAccountLoginWindow_Loaded;
        Closed += (_, _) =>
        {
            _isClosed = true;
            try { WebView.Dispose(); } catch { }
            try { HiddenStashWebView.Dispose(); } catch { }
        };
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

            var env = await WebView2Helper.GetEnvironmentAsync();
            if (_isClosed) return;
            await WebView.EnsureCoreWebView2Async(env);
            if (_isClosed || WebView.CoreWebView2 == null) return;

            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.Settings.IsScriptEnabled = true;

            WebView.CoreWebView2.ProcessFailed += (_, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = Loc.T(
                        $"WebView2 процес перезапущено ({args.ProcessFailedKind}). Натисни ↻ Оновити або скористайся pathofexile.com.",
                        $"WebView2 process recovered ({args.ProcessFailedKind}). Click ↻ Reload or use pathofexile.com.",
                        $"Процесс WebView2 перезапущен ({args.ProcessFailedKind}). Нажми ↻ или pathofexile.com.");
                });
            };

            WebView.SourceChanged += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (WebView.Source != null && !UrlBox.IsKeyboardFocusWithin)
                        UrlBox.Text = WebView.Source.ToString();
                });
            };

            WebView.NavigationCompleted += async (_, args) =>
            {
                if (_isClosed) return;
                string uri = WebView.Source?.ToString() ?? "";
                if (!args.IsSuccess ||
                    uri.Contains("/oauth/", StringComparison.OrdinalIgnoreCase) ||
                    uri.Contains("cdn-cgi", StringComparison.OrdinalIgnoreCase) ||
                    uri.Contains("__cf_chl", StringComparison.OrdinalIgnoreCase))
                    return;

                if (uri.Contains("/login", StringComparison.OrdinalIgnoreCase))
                {
                    if (_pendingStashScan || _stashModeOnly)
                        StatusText.Text = Loc.T("Увійди в акаунт PoE 2 нижче (pathofexile2.com) — після входу скриня просканується автоматично.", "Sign in to PoE 2 below — stash scan will resume automatically.", "Войди в аккаунт PoE 2 ниже — скан запустится автоматически.");
                    return;
                }

                bool onAccountPage = uri.Contains("pathofexile2.com/my-account", StringComparison.OrdinalIgnoreCase);
                bool returnedAfterTradeLogin = _pendingStashScan && uri.Contains("/trade2", StringComparison.OrdinalIgnoreCase);
                if (!onAccountPage && !returnedAfterTradeLogin && !_stashModeOnly && !_pendingStashScan) return;

                await Task.Delay(700);
                if (_isClosed) return;
                if (_pendingStashScan && (onAccountPage || returnedAfterTradeLogin))
                {
                    _pendingStashScan = false;
                    await RunStashScanAsync(closeOnSuccess: true);
                    return;
                }
                if (onAccountPage)
                    await TryAutoLoadCharactersAsync();
                if (_stashModeOnly && !_isClosed)
                    await RunStashScanAsync(closeOnSuccess: true);
            };

            // Завжди стартуємо на pathofexile2.com (ніколи не кидаємо користувача на сайт PoE 1!)
            string startUrl = _stashModeOnly
                ? "https://pathofexile2.com/my-account/characters"
                : "https://pathofexile2.com/";
            UrlBox.Text = startUrl;
            WebView.CoreWebView2.Navigate(startUrl);
            if (_stashModeOnly)
            {
                _pendingStashScan = true;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(900);
                    if (!_isClosed)
                        await Dispatcher.InvokeAsync(async () => await RunStashScanAsync(closeOnSuccess: true));
                });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void NavBack_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoBack == true) WebView.CoreWebView2.GoBack();
    }

    private void NavReload_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.Reload();
    private void GoSignIn_Click(object sender, RoutedEventArgs e) => NavigateTo("https://pathofexile2.com/login");
    private void GoCharacters_Click(object sender, RoutedEventArgs e) => NavigateTo("https://pathofexile2.com/my-account/characters");
    private void GoTrade2_Click(object sender, RoutedEventArgs e) => NavigateTo("https://www.pathofexile.com/trade2");
    private void OpenExternalBrowser_Click(object sender, RoutedEventArgs e) => WebView2Helper.OpenInSystemBrowser("https://pathofexile2.com/my-account/characters");

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) NavigateTo(UrlBox.Text);
    }

    private void NavigateTo(string url)
    {
        if (WebView.CoreWebView2 == null || string.IsNullOrWhiteSpace(url)) return;
        string target = url.Trim();
        if (!target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            target = "https://" + target;
        UrlBox.Text = target;
        WebView.CoreWebView2.Navigate(target);
    }

    private async void FetchCharacters_Click(object sender, RoutedEventArgs e) => await TryAutoLoadCharactersAsync();

    private async Task TryAutoLoadCharactersAsync()
    {
        if (_autoLoadingChars || _isClosed || WebView.CoreWebView2 == null) return;
        _autoLoadingChars = true;
        StatusText.Text = Loc.T("Завантажую персонажів з акаунта PoE 2...", "Loading PoE 2 characters from account...", "Загружаю персонажей с аккаунта PoE 2...");
        try
        {
            string unescaped = await WebView2Helper.ExecuteAsyncScriptAsync(WebView.CoreWebView2, PoeAccountService.FetchCharactersAndDataScript);
            if (string.IsNullOrWhiteSpace(unescaped))
            {
                StatusText.Text = Loc.T("Порожня відповідь від браузера — спробуй відкрити «👤 Персонажі» або оновити.", "Empty response from page — click Characters or Reload.", "Пустой ответ браузера.");
                return;
            }

            var payload = JsonSerializer.Deserialize<PoeAccountSyncPayload>(unescaped, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload == null || !payload.ok || payload.characters.Count == 0)
            {
                StatusText.Text = !string.IsNullOrWhiteSpace(payload?.error)
                    ? payload.error
                    : Loc.T(
                        "Увійди в акаунт у браузері нижче (Sign In), потім натисни «Завантажити персонажів».",
                        "Sign in above then click 'Load Characters'.",
                        "Войди в аккаунт в браузере и нажми «Загрузить персонажей».");
                return;
            }

            _accountName = payload.accountName;
            _latestCharId = payload.latestCharacterId;
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
                else if (string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName) &&
                         !string.IsNullOrWhiteSpace(_latestCharId) &&
                         c.id.Equals(_latestCharId, StringComparison.OrdinalIgnoreCase))
                    pickIdx = i;
            }
            if (CharCombo.Items.Count > 0) CharCombo.SelectedIndex = pickIdx;
            StatusText.Text = Loc.T(
                $"Акаунт {(_accountName.Length > 0 ? _accountName + " · " : "")}знайдено {_chars.Count} персонаж(ів). Натисни «Синхронізувати».",
                $"Found {_chars.Count} PoE 2 character(s). Click Sync to pull level, gear & skills.",
                $"Найдено {_chars.Count} персонаж(ей). Нажми «Синхронизировать».");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _autoLoadingChars = false;
        }
    }

    private async void SyncSelected_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 == null) return;
        if (_chars.Count == 0)
        {
            await TryAutoLoadCharactersAsync();
            if (_chars.Count == 0) return;
        }

        int idx = CharCombo.SelectedIndex;
        if (idx < 0 || idx >= _chars.Count) idx = 0;
        var chosen = _chars[idx];

        StatusText.Text = Loc.T(
            $"Завантажую рівень, шмот, фласки та скіли для {chosen.name}...",
            $"Fetching level, gear, flasks & skills for {chosen.name}...",
            $"Загружаю уровень, экип, фласки и скиллы для {chosen.name}...");
        try
        {
            string script = PoeAccountService.BuildFetchCharacterDetailsScript(chosen.name, chosen.id, _accountName);
            string unescaped = await WebView2Helper.ExecuteAsyncScriptAsync(WebView.CoreWebView2, script);
            var details = JsonSerializer.Deserialize<PoeAccountSyncPayload>(unescaped, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new PoeAccountSyncPayload();

            PoeAccountService.ApplySyncedCharacterToConfig(_ai.Config, _accountName, chosen, details, out int slotCount);
            _ai.SaveConfig();
            PoeAccountService.RemoveAccountBuildProfiles(_profiles);

            SyncedOk = true;
            ResultMessage = Loc.T(
                $"{chosen.name} · Lv {chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} слотів · {_ai.Config.PoeCharacterSkills.Count} скілів.",
                $"{chosen.name} · Lv {chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} slots · {_ai.Config.PoeCharacterSkills.Count} skills.",
                $"{chosen.name} · Ур.{chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} слотов · {_ai.Config.PoeCharacterSkills.Count} скиллов.");
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
        if (_isClosed || WebView.CoreWebView2 == null) return;

        string league = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Standard" : _ai.Config.TradeLeague;
        StatusText.Text = Loc.T(
            $"Сканую скриню PoE 2 ({league}, без відкриття сайту PoE 1)...",
            $"Scanning PoE 2 stash ({league}, keeping PoE 2 view)...",
            $"Сканирую тайник PoE 2 ({league})...");
        try
        {
            // 1. Зчитуємо cookie з WebView2 для обох доменів GGG і пробуємо швидкий прямий запит (нуль навігації!)
            var cookieList = new List<(string Domain, string Name, string Value)>();
            foreach (string domainUrl in new[] { "https://www.pathofexile.com", "https://pathofexile2.com" })
            {
                try
                {
                    var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(domainUrl);
                    foreach (var c in cookies)
                        cookieList.Add((c.Domain, c.Name, c.Value));
                }
                catch { }
            }

            double exPerDiv = await PoeTradePriceService.EstimateExaltedPerDivineAsync(league);
            string ua = WebView.CoreWebView2.Settings.UserAgent;
            var direct = await StashTrackerService.FetchStashDirectAsync(league, cookieList, ua, exPerDiv, maxTabs: 4);
            if (direct.Ok && direct.Scan != null)
            {
                ApplyStashScanResult(direct.Scan, league, closeOnSuccess);
                return;
            }

            // 2. Тихий фоновий WebView2 1x1 прямо на JSON-ендпоінт скрині PoE 2 (ніколи не відкриває видиму сторінку PoE 1 /my-account!)
            var hiddenScan = await FetchStashViaHiddenWebViewAsync(league, exPerDiv, maxTabs: 4);
            if (hiddenScan.Scan is { Ok: true })
            {
                ApplyStashScanResult(hiddenScan.Scan, league, closeOnSuccess);
                return;
            }

            _pendingStashScan = true;
            StatusText.Text = !string.IsNullOrWhiteSpace(hiddenScan.Error)
                ? hiddenScan.Error
                : Loc.T(
                    "Потрібна авторизація PoE 2: натисни «🔐 Sign In» вище (або «⚖ Trade 2» → Sign In) і увійди в акаунт.",
                    "PoE 2 sign-in required: click '🔐 Sign In' (or '⚖ Trade 2' → Sign In) and log in.",
                    "Требуется вход в PoE 2: нажми «🔐 Sign In» выше и войди в аккаунт.");

            string curHost = WebView.Source?.ToString() ?? "";
            if (!curHost.Contains("pathofexile2.com/login", StringComparison.OrdinalIgnoreCase) &&
                !curHost.Contains("/trade2", StringComparison.OrdinalIgnoreCase))
            {
                NavigateTo("https://pathofexile2.com/login");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void ApplyStashScanResult(StashScanResult scan, string league, bool closeOnSuccess)
    {
        ScannedStash = scan;
        _pendingStashScan = false;
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
        if (closeOnSuccess && !_isClosed)
        {
            DialogResult = true;
            Close();
        }
    }

    private async Task<(StashScanResult? Scan, string Error)> FetchStashViaHiddenWebViewAsync(string league, double exPerDiv, int maxTabs)
    {
        if (_isClosed) return (null, "Closed");
        var env = await WebView2Helper.GetEnvironmentAsync();
        if (_isClosed) return (null, "Closed");
        await HiddenStashWebView.EnsureCoreWebView2Async(env);
        if (_isClosed || HiddenStashWebView.CoreWebView2 == null) return (null, "Hidden WebView unavailable");

        string lg = Uri.EscapeDataString(string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim());
        string endpoint = $"https://www.pathofexile.com/character-window/get-stash-items?realm=poe2&league={lg}&tabs=1&tabIndex=0";

        var tcs = new TaskCompletionSource<bool>();
        void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            HiddenStashWebView.NavigationCompleted -= OnNavCompleted;
            tcs.TrySetResult(e.IsSuccess);
        }

        HiddenStashWebView.NavigationCompleted += OnNavCompleted;
        HiddenStashWebView.CoreWebView2.Navigate(endpoint);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(9000));
        HiddenStashWebView.NavigationCompleted -= OnNavCompleted;
        if (done != tcs.Task || _isClosed)
            return (null, Loc.T("Таймаут відповіді Stash API. Переконайся, що увійшов у PoE 2 акаунт нижче.", "Stash API timeout. Sign in to PoE 2 below.", "Таймаут Stash API. Войди в PoE 2 аккаунт ниже."));

        string navUrl = HiddenStashWebView.CoreWebView2.Source ?? "";
        if (!navUrl.Contains("get-stash-items", StringComparison.OrdinalIgnoreCase))
        {
            return (null, Loc.T(
                "Скриня вимагає активної сесії PoE 2 — увійди через «🔐 Sign In» (або «⚖ Trade 2» → Login) і повтори скан. Сайт PoE 1 не відкриватиметься!",
                "Stash requires an active PoE 2 login — sign in via '🔐 Sign In' or '⚖ Trade 2' and scan again.",
                "Тайник требует активного входа PoE 2 — войди через «🔐 Sign In» или «⚖ Trade 2» и повтори."));
        }

        string bodyJson = await WebView2Helper.ExecuteAsyncScriptAsync(
            HiddenStashWebView.CoreWebView2,
            "(function(){ return (document.body && (document.body.innerText || document.body.textContent)) || ''; })()");

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!StashTrackerService.TryAccumulateStashTabJson(bodyJson, counts, out int totalTabs))
        {
            return (null, Loc.T(
                "Не вдалося прочитати JSON скрині (сесія не авторизована). Увійди через Sign In або Trade 2 нижче.",
                "Could not read Stash JSON (sign in below via Sign In or Trade 2).",
                "Не удалось прочитать JSON тайника. Войди через Sign In или Trade 2."));
        }

        int tabsToScan = Math.Clamp(Math.Min(maxTabs, totalTabs), 1, maxTabs);
        if (tabsToScan > 1)
        {
            string restJson = await WebView2Helper.ExecuteAsyncScriptAsync(HiddenStashWebView.CoreWebView2, $$"""
(async () => {
  const counts = {};
  const lg = {{JsonSerializer.Serialize(league)}};
  for (let i = 1; i < {{tabsToScan}}; i++) {
    try {
      const r = await fetch('/character-window/get-stash-items?realm=poe2&league=' + encodeURIComponent(lg) + '&tabs=0&tabIndex=' + i, { credentials: 'include' });
      if (!r.ok) break;
      const d = await r.json();
      if (Array.isArray(d.items)) {
        for (const it of d.items) {
          const n = (it.typeLine || it.baseType || it.name || '').trim();
          if (n) counts[n] = (counts[n] || 0) + Math.max(1, Number(it.stackSize || 1));
        }
      }
    } catch {}
  }
  return JSON.stringify(counts);
})();
""");
            if (!string.IsNullOrWhiteSpace(restJson) && restJson.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                using var rDoc = JsonDocument.Parse(restJson);
                foreach (var p in rDoc.RootElement.EnumerateObject())
                    counts[p.Name] = counts.GetValueOrDefault(p.Name) + p.Value.GetInt32();
            }
        }

        return (StashTrackerService.EvaluateCounts(league, tabsToScan, counts, exPerDiv), "");
    }
}
