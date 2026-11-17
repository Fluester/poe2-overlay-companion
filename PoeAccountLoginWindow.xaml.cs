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
    private bool _stashScanBusy;
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
        StatusText.Text = Loc.T(
            "Натисни Sign In на pathofexile2.com нижче, увійди в акаунт PoE 2, потім відкрий «Персонажі» → «Завантажити персонажів».",
            "Click Sign In on pathofexile2.com below, log in to your PoE 2 account, then click Load Characters.",
            "Нажми Sign In на pathofexile2.com ниже, войди в аккаунт PoE 2, затем нажми «Загрузить персонажей».");

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
                StatusText.Text = Loc.T("Edge WebView2 Runtime не знайдено.", "Edge WebView2 Runtime missing.", "Edge WebView2 Runtime не найден.");
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
                bool onPoe2Home = uri.StartsWith("https://pathofexile2.com/", StringComparison.OrdinalIgnoreCase) &&
                                  !uri.Contains("/login", StringComparison.OrdinalIgnoreCase);
                bool returnedAfterTradeLogin = _pendingStashScan && uri.Contains("/trade2", StringComparison.OrdinalIgnoreCase);
                if (!onAccountPage && !onPoe2Home && !returnedAfterTradeLogin && !_stashModeOnly && !_pendingStashScan) return;

                await Task.Delay(700);
                if (_isClosed) return;
                if (_pendingStashScan && (onAccountPage || onPoe2Home || returnedAfterTradeLogin))
                {
                    _pendingStashScan = false;
                    await RunStashScanAsync(closeOnSuccess: true);
                    return;
                }
                if (!_stashModeOnly && (onAccountPage || onPoe2Home))
                    await TryAutoLoadCharactersAsync();
            };

            // Завжди стартуємо з /my-account/characters на pathofexile2.com (ніколи не кидаємо на сайт PoE 1!)
            string startUrl = "https://pathofexile2.com/my-account/characters";
            UrlBox.Text = startUrl;
            if (_stashModeOnly && !string.IsNullOrWhiteSpace(_ai.Config.PoeAccountName))
            {
                await RunStashScanAsync(closeOnSuccess: true);
                if (!_isClosed && !_pendingStashScan)
                    WebView.CoreWebView2.Navigate(startUrl);
            }
            else
            {
                if (_stashModeOnly)
                    _pendingStashScan = true;
                WebView.CoreWebView2.Navigate(startUrl);
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

            if (!details.ok && details.equippedBySlot.Count == 0)
            {
                StatusText.Text = !string.IsNullOrWhiteSpace(details.error)
                    ? details.error
                    : Loc.T(
                        "Не вдалося завантажити предмети персонажа з сайту GGG. Спробуй оновити сторінку (🔄) та повторити.",
                        "Failed to fetch character gear from GGG site. Try Reload (🔄) and Sync again.",
                        "Не удалось загрузить экип персонажа с сайта GGG. Нажми Обновить (🔄) и повтори.");
                return;
            }

            PoeAccountService.ApplySyncedCharacterToConfig(_ai.Config, _accountName, chosen, details, out int slotCount);
            _ai.SaveConfig();
            PoeAccountService.RemoveAccountBuildProfiles(_profiles);

            SyncedOk = true;
            ResultMessage = Loc.T(
                $"{chosen.name} · Lv {chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} слотів · {_ai.Config.PoeCharacterSkills.Count} скілів (екіп і зброя; пасивки/несокетовані скіли GGG API ще не віддає).",
                $"{chosen.name} · Lv {chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} slots · {_ai.Config.PoeCharacterSkills.Count} skills (gear & granted; GGG web API doesn't expose passives/unlisted gems).",
                $"{chosen.name} · Ур.{chosen.level} ({chosen.@class}, {chosen.league}) · {slotCount} слотов · {_ai.Config.PoeCharacterSkills.Count} скиллов (экип и оружие; пассивки/камни GGG API не отдаёт).");
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
        if (_isClosed || _stashScanBusy || WebView.CoreWebView2 == null) return;
        _stashScanBusy = true;

        string league = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Standard" : _ai.Config.TradeLeague;
        StatusText.Text = Loc.T(
            $"Сканую виставлені лоти PoE 2 ({league}, Trade 2 API)...",
            $"Scanning PoE 2 listed stash tabs ({league}, Trade 2 API)...",
            $"Сканирую выставленные лоты PoE 2 ({league}, Trade 2 API)...");
        try
        {
            string accName = !string.IsNullOrWhiteSpace(_accountName) ? _accountName : _ai.Config.PoeAccountName;
            if (string.IsNullOrWhiteSpace(accName) &&
                (WebView.Source?.ToString() ?? "").Contains("pathofexile2.com", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string rawAcc = await WebView2Helper.ExecuteAsyncScriptAsync(WebView.CoreWebView2, PoeAccountService.FetchCharactersAndDataScript);
                    if (!string.IsNullOrWhiteSpace(rawAcc))
                    {
                        var p = JsonSerializer.Deserialize<PoeAccountSyncPayload>(rawAcc, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (p is { ok: true } && !string.IsNullOrWhiteSpace(p.accountName))
                        {
                            accName = p.accountName;
                            _accountName = accName;
                            _ai.Config.PoeAccountName = accName;
                        }
                    }
                }
                catch { }
            }

            var cookieList = new List<(string Name, string Value, string Domain)>();
            foreach (string domainUrl in new[] { "https://www.pathofexile.com", "https://pathofexile2.com" })
            {
                try
                {
                    var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(domainUrl);
                    foreach (var c in cookies)
                        cookieList.Add((c.Name, c.Value, c.Domain));
                }
                catch { }
            }

            var (exPerDiv, exPerChaos) = await PoeTradePriceService.FetchKeyExchangeRatesAsync(league);
            string ua = WebView.CoreWebView2.Settings.UserAgent;
            var direct = await Trade2StashService.ScanListedStashAsync(league, accName, cookieList, ua, exPerDiv, exPerChaos);
            if (direct.Ok && direct.Scan != null)
            {
                ApplyStashScanResult(direct.Scan, league, closeOnSuccess);
                return;
            }

            // Якщо Cloudflare/сесія заблокувала HttpClient — робимо запит із фонового WebView на www.pathofexile.com/trade2
            if (direct.NeedBrowserFallback && !string.IsNullOrWhiteSpace(accName))
            {
                var hiddenScan = await FetchStashViaHiddenWebViewAsync(league, accName, exPerDiv, exPerChaos);
                if (hiddenScan.Scan is { Ok: true })
                {
                    ApplyStashScanResult(hiddenScan.Scan, league, closeOnSuccess);
                    return;
                }
                if (!string.IsNullOrWhiteSpace(hiddenScan.Error))
                {
                    StatusText.Text = hiddenScan.Error;
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(accName))
            {
                _pendingStashScan = true;
                StatusText.Text = !string.IsNullOrWhiteSpace(direct.Error)
                    ? direct.Error
                    : Loc.T(
                        "Потрібна авторизація PoE 2: увійди в акаунт нижче — скан продовжиться автоматично.",
                        "PoE 2 sign-in required: sign in below — stash scan will resume automatically.",
                        "Требуется вход PoE 2: войди в аккаунт ниже — скан продолжится автоматически.");

                string curHost = WebView.Source?.ToString() ?? "";
                if (!curHost.Contains("pathofexile2.com/login", StringComparison.OrdinalIgnoreCase) &&
                    !curHost.Contains("/trade2", StringComparison.OrdinalIgnoreCase))
                {
                    NavigateTo("https://pathofexile2.com/login");
                }
            }
            else
            {
                StatusText.Text = !string.IsNullOrWhiteSpace(direct.Error)
                    ? direct.Error
                    : Loc.T("Не вдалося просканувати публічний стеш Trade 2.", "Could not scan Trade 2 listed stash.", "Не удалось просканировать публичный тайник Trade 2.");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _stashScanBusy = false;
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

    private async Task<(StashScanResult? Scan, string Error)> FetchStashViaHiddenWebViewAsync(
        string league,
        string accountName,
        double exPerDiv,
        double exPerChaos)
    {
        if (_isClosed) return (null, "Closed");
        var env = await WebView2Helper.GetEnvironmentAsync();
        if (_isClosed) return (null, "Closed");
        await HiddenStashWebView.EnsureCoreWebView2Async(env);
        if (_isClosed || HiddenStashWebView.CoreWebView2 == null) return (null, "Hidden WebView unavailable");

        string curUrl = HiddenStashWebView.CoreWebView2.Source ?? "";
        if (!curUrl.Contains("www.pathofexile.com/trade2", StringComparison.OrdinalIgnoreCase))
        {
            var tcs = new TaskCompletionSource<bool>();
            void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                if ((HiddenStashWebView.CoreWebView2?.Source ?? "").Contains("pathofexile.com", StringComparison.OrdinalIgnoreCase))
                {
                    HiddenStashWebView.NavigationCompleted -= OnNavCompleted;
                    tcs.TrySetResult(e.IsSuccess);
                }
            }

            HiddenStashWebView.NavigationCompleted += OnNavCompleted;
            HiddenStashWebView.CoreWebView2.Navigate("https://www.pathofexile.com/trade2");
            var done = await Task.WhenAny(tcs.Task, Task.Delay(10000));
            HiddenStashWebView.NavigationCompleted -= OnNavCompleted;
            if (done != tcs.Task || _isClosed)
                return (null, Loc.T("Таймаут завантаження Trade 2 API.", "Trade 2 API load timeout.", "Таймаут загрузки Trade 2 API."));
            await Task.Delay(800);
        }

        string script = Trade2StashService.BuildTrade2BrowserFetchScript(league, accountName);
        string resJson = await WebView2Helper.ExecuteAsyncScriptAsync(HiddenStashWebView.CoreWebView2, script, 18000);
        if (string.IsNullOrWhiteSpace(resJson))
            return (null, Loc.T("Порожня відповідь Trade 2 API.", "Empty Trade 2 API response.", "Пустой ответ Trade 2 API."));

        using var doc = JsonDocument.Parse(resJson);
        if (!doc.RootElement.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
        {
            string err = doc.RootElement.TryGetProperty("error", out var eEl) ? (eEl.GetString() ?? "Trade 2 error") : "Trade 2 error";
            return (null, err);
        }

        int total = doc.RootElement.TryGetProperty("total", out var tEl) ? tEl.GetInt32() : 0;
        var batches = new List<string>();
        if (doc.RootElement.TryGetProperty("batches", out var bEl) && bEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in bEl.EnumerateArray())
            {
                string? s = b.GetString();
                if (!string.IsNullOrWhiteSpace(s)) batches.Add(s);
            }
        }

        return (Trade2StashService.EvaluateTrade2Listings(league, total, batches, exPerDiv, exPerChaos), "");
    }
}
