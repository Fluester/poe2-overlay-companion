using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class MainWindow : Window
{
    // --- Трекер сесії ---
    private void ResetFarmSession_Click(object sender, RoutedEventArgs e)
    {
        _watcher.ResetFarmSession();
        _ai.Config.SessionStartNetWorthDiv = _ai.Config.LastStashNetWorthDiv;
        _ai.SaveConfig();
        RefreshFarmStatsUi();
    }

    private void ScanStashNetWorth_Click(object sender, RoutedEventArgs e)
    {
        var win = new PoeAccountLoginWindow(_ai, _profiles, stashModeOnly: true);
        if (ShowStandaloneDialog(win) == true && win.ScannedStash is { Ok: true })
        {
            RefreshFarmStatsUi();
            _toast.ShowToast(Loc.T("Вартість скрині", "Stash Net-Worth", "Стоимость тайника"), win.ResultMessage, Color.FromRgb(0x3F, 0xB9, 0x50), nearCursor: false, seconds: 5.0);
        }
    }

    private void RefreshFarmStatsUi()
    {
        var fs = _watcher.FarmStats;
        double sessionMins = (DateTime.UtcNow - fs.SessionStartUtc).TotalMinutes;
        double sessionHrs = Math.Max(0.05, (DateTime.UtcNow - fs.SessionStartUtc).TotalHours);
        ValKpiSession.Text = $"{sessionMins:F0} {Loc.T("хв", "min", "мин")}";
        ValKpiZone.Text = fs.CurrentMapEnteredUtc is DateTime dt
            ? $"{fs.CurrentMapName} ({FormatDuration((DateTime.UtcNow - dt).TotalSeconds)})"
            : (_watcher.CurrentZone ?? Loc.T("Місто / Сховок", "Town / Hideout", "Город / Убежище"));
        ValKpiMaps.Text = $"{fs.MapsCompleted} ({FormatDuration(fs.AverageMapSeconds)})";
        ValKpiEncounters.Text = $"{fs.SpecialEncounters} / {fs.Deaths}";

        FarmStatsBodyText.Text =
            $"{Loc.T("Остання мапа:", "Last map:", "Последняя карта:")} {FormatDuration(fs.LastMapSeconds)}\n" +
            $"{Loc.T("Файл логу:", "Client log:", "Файл лога:")} {_watcher.LogPath ?? Loc.LogNotFound}";

        if (_ai.Config.LastStashScanUtc > DateTime.MinValue)
        {
            double net = _ai.Config.LastStashNetWorthDiv;
            double delta = net - _ai.Config.SessionStartNetWorthDiv;
            double perHr = delta / sessionHrs;
            string sign = delta >= 0 ? "+" : "";
            ValStashNetWorth.Text = $"{net:F2} Div  ({sign}{delta:F2} | {sign}{perHr:F1}/hr)";
            StashBreakdownText.Text = _ai.Config.LastStashTopLines.Count > 0
                ? string.Join(" · ", _ai.Config.LastStashTopLines)
                : Loc.T("Останній скан порожній.", "Last stash scan found 0 priced items.", "Последний скан пуст.");
        }
    }

    private void RefreshCurrencyRates_Click(object sender, RoutedEventArgs e) =>
        _ = RefreshCurrencyRatesAsync(force: true);

    private async Task RefreshCurrencyRatesAsync(bool force = false)
    {
        if (BtnRefreshCurrencyRates != null) BtnRefreshCurrencyRates.IsEnabled = false;
        try
        {
            string league = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Standard" : _ai.Config.TradeLeague;
            var (exPerDiv, exPerChaos) = await PoeTradePriceService.FetchKeyExchangeRatesAsync(league, forceRefresh: force);
            _lastRatesFetchUtc = DateTime.UtcNow;
            double chaosPerDiv = exPerChaos > 0.01 ? exPerDiv / exPerChaos : 0;
            CurrencyRatesText.Text =
                $"1 Divine = {exPerDiv:0.#} Ex" +
                (chaosPerDiv > 0 ? $"  (~{chaosPerDiv:0.#} Chaos)" : "") +
                $"   ·   1 Chaos = {exPerChaos:0.#} Ex   ·   {league}";
            CurrencyRatesText.Foreground = UiBrushes.FgPrimary;
        }
        catch
        {
            CurrencyRatesText.Text = Loc.T("Не вдалося завантажити курси Trade 2.", "Could not load Trade 2 currency rates.", "Не удалось загрузить курсы Trade 2.");
            CurrencyRatesText.Foreground = UiBrushes.FgMuted;
        }
        finally
        {
            if (BtnRefreshCurrencyRates != null) BtnRefreshCurrencyRates.IsEnabled = true;
        }
    }

    private static string FormatDuration(double sec)
    {
        if (sec <= 0) return "0s";
        int s = (int)sec;
        return s >= 60 ? $"{s / 60}m {s % 60:D2}s" : $"{s}s";
    }


    // --- AI-радник ---
    private void SelectProviderGemini_Click(object sender, RoutedEventArgs e)
    {
        _ai.Config.AiProvider = AiProviderKind.Gemini;
        _ai.SaveConfig();
        RefreshProviderUi();
    }

    private void SelectProviderOpenAi_Click(object sender, RoutedEventArgs e)
    {
        _ai.Config.AiProvider = AiProviderKind.OpenAiCompatible;
        _ai.SaveConfig();
        RefreshProviderUi();
    }

    private void RefreshProviderUi()
    {
        bool isGem = _ai.Config.AiProvider == AiProviderKind.Gemini;
        BtnProvGemini.Background = isGem ? UiBrushes.BgBrass : UiBrushes.BgCard;
        BtnProvGemini.Foreground = isGem ? UiBrushes.FgDark : UiBrushes.FgMuted;
        BtnProvOpenAi.Background = !isGem ? UiBrushes.BgBrass : UiBrushes.BgCard;
        BtnProvOpenAi.Foreground = !isGem ? UiBrushes.FgDark : UiBrushes.FgMuted;
        OpenAiUrlRow.Visibility = isGem ? Visibility.Collapsed : Visibility.Visible;
        AiModelBox.Text = isGem ? _ai.Config.PreferredModel : _ai.Config.OpenAiModel;
        UpdateAiStatusLabel();
    }

    private void UpdateAiStatusLabel()
    {
        string prov = _ai.Config.AiProvider == AiProviderKind.Gemini ? "Gemini" : "OpenAI-API";
        string model = _ai.Config.AiProvider == AiProviderKind.Gemini ? _ai.Config.PreferredModel : _ai.Config.OpenAiModel;
        if (_ai.HasApiKey)
        {
            AiStatusText.Text = $"{prov}: OK ({_ai.MaskedApiKey} · {model})";
            AiStatusText.Foreground = UiBrushes.FgEmerald;
            if (LblSettingsAiStatus != null)
            {
                LblSettingsAiStatus.Text = Loc.T($"✓ Ключ: {_ai.MaskedApiKey}", $"✓ Key saved: {_ai.MaskedApiKey}", $"✓ Ключ: {_ai.MaskedApiKey}");
                LblSettingsAiStatus.Foreground = UiBrushes.FgEmerald;
            }
        }
        else
        {
            AiStatusText.Text = $"{prov}: {Loc.T("ключ не задано", "no API key", "ключ не задан")}";
            AiStatusText.Foreground = UiBrushes.FgAmber;
            if (LblSettingsAiStatus != null)
            {
                LblSettingsAiStatus.Text = Loc.T("Ключ не задано", "No API key", "Ключ не задан");
                LblSettingsAiStatus.Foreground = UiBrushes.FgAmber;
            }
        }
    }

    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        string key = ApiKeyBox.Text.Trim();
        string model = AiModelBox.Text.Trim();
        if (_ai.Config.AiProvider == AiProviderKind.Gemini)
        {
            if (!string.IsNullOrEmpty(key)) _ai.Config.GeminiApiKey = key;
            if (!string.IsNullOrEmpty(model)) _ai.Config.PreferredModel = model;
        }
        else
        {
            if (!string.IsNullOrEmpty(key)) _ai.Config.OpenAiApiKey = key;
            if (!string.IsNullOrEmpty(model)) _ai.Config.OpenAiModel = model;
            if (!string.IsNullOrWhiteSpace(OpenAiUrlBox.Text)) _ai.Config.OpenAiBaseUrl = OpenAiUrlBox.Text.Trim();
        }
        _ai.SaveConfig();
        ApiKeyBox.Text = "";
        UpdateAiStatusLabel();
        AiMetaText.Text = Loc.T("Налаштування API збережено локально.", "API settings saved locally.", "Настройки API сохранены локально.");
    }

    private async void AiScanNow_Click(object sender, RoutedEventArgs e) =>
        await RunAiQueryAsync(Loc.T("Проаналізуй екран PoE 2 і підкажи, що можна покращити зараз.", "Analyze my PoE 2 screen and suggest improvements right now.", "Проанализируй экран PoE 2 и подскажи, что улучшить сейчас."), includeScreenshot: true);

    private async void AiAskText_Click(object sender, RoutedEventArgs e)
    {
        string q = string.IsNullOrWhiteSpace(AiQuestionBox.Text) ? Loc.T("Що далі по білду?", "What next for my build?", "Что дальше по билду?") : AiQuestionBox.Text.Trim();
        await RunAiQueryAsync(q, includeScreenshot: false);
    }

    private async Task<byte[]> CaptureCleanScreenAsync()
    {
        double restoreAlpha = _clickThrough ? 0.78 : _alphas[_alphaIdx];
        bool miniVis = _miniHud.IsVisible;
        try
        {
            Win32OverlayHelper.SetWindowOpacity(this, 0);
            if (miniVis) _miniHud.Opacity = 0;
            await Dispatcher.Yield(DispatcherPriority.Render);
            await Task.Delay(35);
            return await ScreenCaptureService.CaptureScreenJpegAsync();
        }
        finally
        {
            Win32OverlayHelper.SetWindowOpacity(this, restoreAlpha);
            if (miniVis) _miniHud.Opacity = 1;
        }
    }

    private async Task RunAiQueryAsync(string question, bool includeScreenshot, bool isAutoSchedule = false)
    {
        if (_aiBusy) return;
        _aiBusy = true;
        AiMetaText.Text = includeScreenshot ? Loc.T("Знімаю вікно PoE 2...", "Capturing PoE 2 screen...", "Снимаю экран PoE 2...") : Loc.T("Запит до AI...", "Asking AI...", "Запрос к AI...");

        try
        {
            byte[]? jpeg = includeScreenshot ? await CaptureCleanScreenAsync() : null;
            if (isAutoSchedule && jpeg != null && _ai.IsDuplicateAutoFrame(jpeg))
            {
                AiMetaText.Text = Loc.T("Авто-скан пропущено (екран не змінився).", "Auto-scan skipped (screen unchanged).", "Авто-скан пропущен (экран не изменился).");
                return;
            }

            string ctx = BuildContextHeader();
            var (ok, answer, model) = await _ai.AskAsync(
                $"{ctx}\n\nUSER QUERY:\n{question}",
                _profiles.ActiveProfile,
                jpeg,
                characterLevel: EffectiveLevel,
                stageIndex: _overviewStageIdx,
                wornGear: _ai.Config.WornGearBySlot);
            _aiHasResponse = true;
            if (ok)
            {
                AiMetaText.Text = $"{DateTime.Now:HH:mm:ss} ({model})";
                AiResponseText.Text = answer;
                if (!_collapsed && !_clickThrough && !_miniHudMode)
                    SelectPage(NavBtnAi);
                _toast.ShowToast("AI", answer.Length > 340 ? answer[..340] + "…" : answer, Color.FromRgb(0xC8, 0x9B, 0x3C), nearCursor: false, seconds: 7.0);
            }
            else
            {
                AiMetaText.Text = Loc.T("Помилка AI", "AI Error", "Ошибка AI");
                AiResponseText.Text = answer;
            }
        }
        catch (Exception ex)
        {
            _aiHasResponse = true;
            AiMetaText.Text = Loc.T("Помилка", "Error", "Ошибка");
            AiResponseText.Text = ex.Message;
        }
        finally
        {
            _aiBusy = false;
        }
    }

    private string BuildContextHeader()
    {
        var prof = _profiles.ActiveProfile;
        string itemInfo = _lastItemEval is { IsPoeItem: true } ? $"{_lastItemEval.ItemName} ({_lastItemEval.Summary})" : "none";
        return $"BUILD: {prof?.Name ?? "Universal"} | League: {_ai.Config.TradeLeague} | Level: {EffectiveLevel} | Zone: {_watcher.CurrentZone ?? "?"} | LastItem: {itemInfo}";
    }
}
