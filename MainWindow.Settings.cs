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
    private void LoadConfigToUi()
    {
        Width = ExpandedWidth;
        Height = ExpandedHeight;
        HighlightSizePresetButtons(_ai.Config.OverlaySizePreset);

        double targetAlpha = _ai.Config.WindowOpacity is > 0.2 and <= 1.0 ? _ai.Config.WindowOpacity : 0.95;
        int bestAlphaIdx = 0;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < _alphas.Length; i++)
        {
            double diff = Math.Abs(_alphas[i] - targetAlpha);
            if (diff < bestDiff) { bestDiff = diff; bestAlphaIdx = i; }
        }
        _alphaIdx = bestAlphaIdx;
        Win32OverlayHelper.SetWindowOpacity(this, _alphas[_alphaIdx]);
        UpdateAlphaButtonLabel();

        ChkAutoAi30.IsChecked = _ai.Config.AutoScanEnabled;
        ChkAiOnlySafeZone.IsChecked = _ai.Config.AutoScanOnlySafeZone;
        ChkHideNoGame.IsChecked = _ai.Config.HideWhenGameClosed;
        ChkFocusOnCtrlC.IsChecked = _ai.Config.FocusOverlayOnCtrlC;
        ChkTradeHelper.IsChecked = _ai.Config.TradeHelperEnabled;
        CustomCmd1Box.Text = string.IsNullOrWhiteSpace(_ai.Config.CustomCmd1) ? "/hideout" : _ai.Config.CustomCmd1;
        CustomCmd2Box.Text = string.IsNullOrWhiteSpace(_ai.Config.CustomCmd2) ? "/remaining" : _ai.Config.CustomCmd2;
        ChkAutostart.IsChecked = TrayIconService.IsAutostartEnabled();
        OpenAiUrlBox.Text = _ai.Config.OpenAiBaseUrl;

        string activeLeague = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Forbidden Rites" : _ai.Config.TradeLeague;
        PopulateLeagueCombo(PoeTradePriceService.DefaultLeagues, activeLeague);

        ChkBuffTimer.IsChecked = _ai.Config.BuffTimerEnabled;
        BuffKeyBox.Text = string.IsNullOrWhiteSpace(_ai.Config.BuffTimerKey) ? "X" : _ai.Config.BuffTimerKey;
        BuffLabelBox.Text = string.IsNullOrWhiteSpace(_ai.Config.BuffTimerLabel) ? "Buff" : _ai.Config.BuffTimerLabel;
        BuffSecBox.Text = Math.Max(1, _ai.Config.BuffTimerSeconds).ToString();

        LblPortableStatus.Text = AppPaths.IsPortableMode ? "PORTABLE (.\\Data\\)" : "AppData";
        RefreshProviderUi();
        UpdateAccountStatusLabel();
    }

    private void PopulateLeagueCombo(IEnumerable<string> leagues, string selectedLeague)
    {
        _suppressLeagueEvent = true;
        try
        {
            var list = leagues.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!list.Contains(selectedLeague, StringComparer.OrdinalIgnoreCase))
                list.Insert(0, selectedLeague);

            LeagueCombo.Items.Clear();
            foreach (var lg in list) LeagueCombo.Items.Add(lg);
            LeagueCombo.Text = selectedLeague;
            TradeLeagueBox.Text = selectedLeague;
            HeaderLeagueBtn.Content = selectedLeague;
        }
        finally
        {
            _suppressLeagueEvent = false;
        }
    }

    private async Task RefreshLeaguesListAsync()
    {
        var live = await PoeTradePriceService.FetchLeaguesAsync();
        await Dispatcher.InvokeAsync(() =>
        {
            string cur = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? (live.FirstOrDefault() ?? "Forbidden Rites") : _ai.Config.TradeLeague;
            PopulateLeagueCombo(live, cur);
        });
    }

    private async void RefreshLeagues_Click(object sender, RoutedEventArgs e)
    {
        BtnRefreshLeagues.Content = "…";
        try
        {
            var liveTask = PoeTradePriceService.FetchLeaguesAsync();
            var itemsTask = LivePoe2DataService.RefreshIfStaleAsync(force: true);
            await Task.WhenAll(liveTask, itemsTask);
            var live = liveTask.Result;
            string cur = string.IsNullOrWhiteSpace(LeagueCombo.Text) ? (live.FirstOrDefault() ?? "Forbidden Rites") : LeagueCombo.Text.Trim();
            PopulateLeagueCombo(live, cur);
        }
        finally
        {
            BtnRefreshLeagues.Content = Loc.T("↻ Ліги", "↻ Leagues", "↻ Лиги");
        }
    }

    private void LeagueCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLeagueEvent || !IsLoaded) return;
        string? sel = LeagueCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(sel)) return;
        ApplySelectedLeague(sel);
    }

    private void ApplySelectedLeague(string league)
    {
        string clean = league.Trim();
        if (string.IsNullOrWhiteSpace(clean)) return;
        _ai.Config.TradeLeague = clean;
        _ai.SaveConfig();
        _suppressLeagueEvent = true;
        TradeLeagueBox.Text = clean;
        LeagueCombo.Text = clean;
        HeaderLeagueBtn.Content = clean;
        _suppressLeagueEvent = false;
        _lastRatesFetchUtc = DateTime.MinValue;
        if (_initializedTabs.Contains(NavBtnFarm)) _ = RefreshCurrencyRatesAsync();
        if (_lastItemEval != null) CheckClipboard(force: true);
    }

    private void SetSizeS_Click(object sender, RoutedEventArgs e) => ApplyWindowSizePreset("S", 460, 360);
    private void SetSizeM_Click(object sender, RoutedEventArgs e) => ApplyWindowSizePreset("M", 560, 430);
    private void SetSizeL_Click(object sender, RoutedEventArgs e) => ApplyWindowSizePreset("L", 740, 560);

    private void ApplyWindowSizePreset(string preset, double w, double h)
    {
        _ai.Config.OverlaySizePreset = preset;
        _ai.Config.WindowWidth = w;
        _ai.Config.WindowHeight = h;
        _ai.SaveConfig();
        if (!_collapsed) { Width = w; Height = h; }
        HighlightSizePresetButtons(preset);
    }

    private void HighlightSizePresetButtons(string preset)
    {
        foreach (var (key, btn) in new[] { ("S", BtnSizeS), ("M", BtnSizeM), ("L", BtnSizeL) })
        {
            bool on = string.Equals(key, preset, StringComparison.OrdinalIgnoreCase);
            btn.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            btn.BorderBrush = on ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
            btn.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
        }
    }

    private void ResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (_collapsed) return;
        double nw = Math.Clamp(Width + e.HorizontalChange, 440, 1200);
        double nh = Math.Clamp(Height + e.VerticalChange, 340, 900);
        Width = nw;
        Height = nh;
        _ai.Config.OverlaySizePreset = "Custom";
        _ai.Config.WindowWidth = nw;
        _ai.Config.WindowHeight = nh;
        _ai.SaveConfig();
        HighlightSizePresetButtons("Custom");
    }

    private void LevelDec_Click(object sender, RoutedEventArgs e) => SetManualLevel(EffectiveLevel - 1);
    private void LevelInc_Click(object sender, RoutedEventArgs e) => SetManualLevel(EffectiveLevel + 1);

    private void SetManualLevel(int newLvl)
    {
        int clamped = Math.Clamp(newLvl, 1, 100);
        _ai.Config.LastKnownLevel = clamped;
        _watcher.SetManualLevel(clamped);
        _ai.SaveConfig();
        _autoSyncLevel = true;
        RefreshLogStatus(showZoneToast: false);
        RefreshOverview();
        if (_buildsPlannerInit) RenderPlannerCards();
    }

    private bool? ShowStandaloneDialog(Window dlg)
    {
        _suspendTopmostHook = true;
        bool wasTop = Topmost;
        try
        {
            Topmost = false;
            return dlg.ShowDialog();
        }
        finally
        {
            Topmost = wasTop;
            _suspendTopmostHook = false;
            Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
        }
    }

    private void ConnectPoeAccount_Click(object sender, RoutedEventArgs e)
    {
        var win = new PoeAccountLoginWindow(_ai, _profiles);
        if (ShowStandaloneDialog(win) == true && win.SyncedOk)
        {
            _watcher.SetManualLevel(Math.Max(1, _ai.Config.LastKnownLevel));
            ApplySelectedLeague(_ai.Config.TradeLeague);
            if (PoeAccountService.TryApplyLinkedProfileForCharacter(_ai.Config, _profiles))
            {
                _cachedActivePob = null;
                _overviewStageIdx = 0;
                _treeStageIdx = 0;
            }
            _ai.SaveConfig();
            UpdateAccountStatusLabel();
            RefreshProfilesUi();
            RefreshOverview();
            if (_initializedTabs.Contains(NavBtnPassives)) RefreshPassiveTreeTab(resetSteps: true);
            if (_initializedTabs.Contains(NavBtnItems)) RefreshWornSlotsStrip();
            RefreshLogStatus(showZoneToast: false);
            if (_lastItemEval != null) CheckClipboard(force: true);
            _toast.ShowToast(Loc.T("PoE 2 Акаунт", "PoE 2 Account", "PoE 2 Аккаунт"), win.ResultMessage, Color.FromRgb(0x4E, 0xCF, 0x8F), nearCursor: false, seconds: 5.0);
        }
    }

    private string FormatAccountStatsSummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterStatsSummary))
        {
            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                _ai.Config.PoeCharacterStatsSummary,
                @"(?:\s*·\s*)?\d+\s+(?:скілів|skills|скиллов)\b",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim(' ', '·');
            if (!string.IsNullOrWhiteSpace(cleaned))
                parts.Add(cleaned);
        }
        if (_ai.Config.PoeCharacterSkills.Count > 0)
            parts.Add($"{_ai.Config.PoeCharacterSkills.Count} {Loc.T("скілів", "skills", "скиллов")}");
        return string.Join(" · ", parts);
    }

    private void UpdateAccountStatusLabel()
    {
        if (string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName))
        {
            int manualWorn = _ai.Config.WornGearBySlot.Count;
            LblPoeAccountStatus.Text = manualWorn > 0
                ? Loc.T($"Локально вдягнуто слотів: {manualWorn}", $"Equipped slots tracked: {manualWorn}", $"Сохранено слотов: {manualWorn}")
                : Loc.T("Акаунт не підключено", "Account not connected", "Аккаунт не подключён");
            LblPoeAccountStatus.Foreground = UiBrushes.FgMuted;
            return;
        }
        string summary = FormatAccountStatsSummary();
        string extra = !string.IsNullOrWhiteSpace(summary) ? $" · {summary}" : "";
        LblPoeAccountStatus.Text = $"{_ai.Config.PoeCharacterName} · {Loc.LevelShort} {_ai.Config.PoeCharacterLevel} ({_ai.Config.PoeCharacterClass}) · {_ai.Config.WornGearBySlot.Count} {Loc.T("слотів", "slots", "слотов")}{extra}";
        LblPoeAccountStatus.Foreground = UiBrushes.FgEmerald;
    }


    private void AiSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _ai == null || ChkAutoAi30 == null || ChkAiOnlySafeZone == null || ChkHideNoGame == null) return;
        _ai.Config.AutoScanEnabled = ChkAutoAi30.IsChecked == true;
        _ai.Config.AutoScanOnlySafeZone = ChkAiOnlySafeZone.IsChecked == true;
        _ai.Config.HideWhenGameClosed = ChkHideNoGame.IsChecked == true;
        if (ChkFocusOnCtrlC != null) _ai.Config.FocusOverlayOnCtrlC = ChkFocusOnCtrlC.IsChecked == true;
        if (ChkTradeHelper != null) _ai.Config.TradeHelperEnabled = ChkTradeHelper.IsChecked == true;
        _ai.SaveConfig();
        HandleAutoHideWhenNoGame(_poeRunningCached);
    }

    private void AutostartChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || ChkAutostart == null) return;
        TrayIconService.SetAutostartEnabled(ChkAutostart.IsChecked == true);
    }

    private void BuffSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _ai == null) return;
        _ai.Config.BuffTimerEnabled = ChkBuffTimer.IsChecked == true;
        if (!_ai.Config.BuffTimerEnabled) { _buffEndUtc = DateTime.MinValue; BuffTimerText.Text = ""; }
        _ai.SaveConfig();
        SyncFastTimerState();
    }

    private void SaveBuffTimer_Click(object sender, RoutedEventArgs e)
    {
        _ai.Config.BuffTimerKey = string.IsNullOrWhiteSpace(BuffKeyBox.Text) ? "X" : BuffKeyBox.Text.Trim().ToUpperInvariant();
        _ai.Config.BuffTimerLabel = string.IsNullOrWhiteSpace(BuffLabelBox.Text) ? "Buff" : BuffLabelBox.Text.Trim();
        _ai.Config.BuffTimerSeconds = int.TryParse(BuffSecBox.Text.Trim(), out int s) ? Math.Clamp(s, 1, 120) : 12;
        if (!string.IsNullOrWhiteSpace(CustomCmd1Box?.Text)) _ai.Config.CustomCmd1 = CustomCmd1Box.Text.Trim();
        if (!string.IsNullOrWhiteSpace(CustomCmd2Box?.Text)) _ai.Config.CustomCmd2 = CustomCmd2Box.Text.Trim();
        _ai.SaveConfig();
        SyncFastTimerState();
    }

    private void SyncFastTimerState()
    {
        if (_fastTimer == null) return;
        bool needFast = !_hkF8Ok || !_hkF9Ok || !_hkF10Ok || !_hkF11Ok
            || _buffEndUtc > DateTime.UtcNow
            || (_ai.Config.BuffTimerEnabled && _poeRunningCached);

        if (needFast && !_fastTimer.IsEnabled) _fastTimer.Start();
        else if (!needFast && _fastTimer.IsEnabled) _fastTimer.Stop();
    }

    private void SlowTimer_Tick(object? sender, EventArgs e)
    {
        _slowTicks++;
        _poeRunningCached = ClientLogWatcher.IsPoe2ProcessRunning();

        if (_watcher.Poll(out bool zoneChanged, out bool levelChanged))
        {
            if (levelChanged && _watcher.CurrentLevel is int wl && _ai.Config.LastKnownLevel != wl)
            {
                _ai.Config.LastKnownLevel = wl;
                _ai.SaveConfig();
            }
            RefreshLogStatus(showZoneToast: zoneChanged);
            if (levelChanged && _autoSyncLevel) RefreshOverview();

            var whispers = _watcher.DrainNewWhispers();
            if (whispers.Count > 0 && _ai.Config.TradeHelperEnabled)
            {
                foreach (var w in whispers)
                    _tradeHelper.Enqueue(w, showIfHidden: true);
                if (ChkSoundAlert?.IsChecked == true && _ai.Config.SoundAlertsEnabled)
                    SystemSounds.Asterisk.Play();
            }
        }
        else if (_watcher.FarmStats.CurrentMapEnteredUtc != null && PageFarm.Visibility == Visibility.Visible)
        {
            RefreshFarmStatsUi();
        }

        HandleAutoHideWhenNoGame(_poeRunningCached);
        if (_miniHudMode) UpdateMiniHudContent();
        SyncFastTimerState();

        if (_slowTicks % 15 == 0)
            CheckAutoAiSchedule(_poeRunningCached);
    }

    private void FastTimer_Tick(object? sender, EventArgs e)
    {
        PollFallbackHotkeys();
        UpdateBuffTimer();
        if (_buffEndUtc <= DateTime.UtcNow) SyncFastTimerState();
    }

    private void HandleAutoHideWhenNoGame(bool running)
    {
        if (ChkHideNoGame.IsChecked != true) return;
        if (!running)
        {
            if (IsVisible) Hide();
            if (_miniHud.IsVisible) _miniHud.Hide();
        }
        else
        {
            if (_miniHudMode && !_miniHud.IsVisible) _miniHud.Show();
            else if (!_miniHudMode && !IsVisible) Show();
        }
    }

    private void PollFallbackHotkeys()
    {
        if (!_hkF8Ok)
        {
            bool f8 = Win32OverlayHelper.IsKeyDown(Win32OverlayHelper.VK_F8);
            if (f8 && !_f8Down) OpenCommandWheelNearCursor();
            _f8Down = f8;
        }
        if (!_hkF9Ok)
        {
            bool f9 = Win32OverlayHelper.IsKeyDown(Win32OverlayHelper.VK_F9);
            if (f9 && !_f9Down) ToggleCollapseOrMiniHud();
            _f9Down = f9;
        }
        if (!_hkF10Ok)
        {
            bool f10 = Win32OverlayHelper.IsKeyDown(Win32OverlayHelper.VK_F10);
            if (f10 && !_f10Down) ToggleClickThrough();
            _f10Down = f10;
        }
        if (!_hkF11Ok)
        {
            bool f11 = Win32OverlayHelper.IsKeyDown(Win32OverlayHelper.VK_F11);
            if (f11 && !_f11Down) _ = RunAiQueryAsync(Loc.T("Проаналізуй екран.", "Analyze screen.", "Проанализируй экран."), includeScreenshot: true);
            _f11Down = f11;
        }

        if (_poeRunningCached && _ai.Config.BuffTimerEnabled)
        {
            int vk = Win32OverlayHelper.KeyNameToVk(_ai.Config.BuffTimerKey);
            bool down = Win32OverlayHelper.IsKeyDown(vk);
            if (down && !_buffDown) _buffEndUtc = DateTime.UtcNow.AddSeconds(Math.Max(1, _ai.Config.BuffTimerSeconds));
            _buffDown = down;
        }
    }

    private void UpdateBuffTimer()
    {
        if (!_ai.Config.BuffTimerEnabled || _buffEndUtc <= DateTime.UtcNow)
        {
            if (BuffTimerText.Text.Length > 0) BuffTimerText.Text = "";
            return;
        }
        double left = (_buffEndUtc - DateTime.UtcNow).TotalSeconds;
        BuffTimerText.Text = $"{_ai.Config.BuffTimerLabel}: {left:F1}s";
    }

    private void CheckAutoAiSchedule(bool poeRunning)
    {
        if (_profiles.Profiles.Count > 0 && BuildUpdateMonitorService.IsCheckDue(_ai.Config) && !_updateCheckBusy)
            _ = RunUpdateCheckAsync(isAuto: true);

        if (!_ai.HasApiKey || !_ai.Config.AutoScanEnabled || _aiBusy) return;
        if ((DateTime.Now - _lastAutoAiRun).TotalMinutes < Math.Max(5, _ai.Config.AutoScanMinutes)) return;
        if (_ai.Config.AutoScanOnlySafeZone && !_watcher.IsInSafeZone) return;
        if (!poeRunning) return;

        _lastAutoAiRun = DateTime.Now;
        _ = RunAiQueryAsync(Loc.T("Планова перевірка (раз на 30 хв): оціни екран і нагадай наступний крок.", "Scheduled 30m check: evaluate screen and suggest next step.", "Плановая проверка (раз в 30 мин): оцени экран и подскажи следующий шаг."), includeScreenshot: true, isAutoSchedule: true);
    }

    private void PickClientTxt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Обери Client.txt з папки logs PoE 2", "Select PoE 2 Client.txt", "Выбери Client.txt из папки logs PoE 2"),
            Filter = "Logs (*.txt)|*.txt|All (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _watcher.SetPath(dlg.FileName);
            _ai.Config.CustomClientTxtPath = dlg.FileName;
            _ai.SaveConfig();
            RefreshLogStatus(showZoneToast: false);
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void RefreshLogStatus(bool showZoneToast)
    {
        int lvl = EffectiveLevel;
        string zone = _watcher.CurrentZone ?? "";
        string lvlTxt = $"{Loc.LevelShort} {lvl}";
        if (_watcher.LogPath != null && !string.IsNullOrWhiteSpace(zone))
        {
            string shortZone = zone.Length > 12 ? zone[..11] + "…" : zone;
            StatusText.Text = $"{lvlTxt} · {shortZone}";
            StatusText.ToolTip = $"{zone}\n{_watcher.LogPath}";
        }
        else
        {
            StatusText.Text = lvlTxt;
            StatusText.ToolTip = _watcher.LogPath ?? Loc.T("Обери Client.txt у Налаштуваннях", "Select Client.txt in Settings", "Выбери Client.txt в Настройках");
        }
        if (LogBtn != null) UpdateLogButtonUi();

        if (_autoSyncLevel && _profiles.ActiveProfile is { Stages.Count: > 1 } prof)
        {
            int bestIdx = 0;
            int bestDist = int.MaxValue;
            for (int i = 0; i < prof.Stages.Count; i++)
            {
                var st = prof.Stages[i];
                int d = lvl >= st.MinLevel && lvl <= st.MaxLevel ? 0 : Math.Min(Math.Abs(lvl - st.MinLevel), Math.Abs(lvl - st.MaxLevel));
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            _overviewStageIdx = bestIdx;
            _treeStageIdx = bestIdx;
        }

        if (!string.IsNullOrWhiteSpace(_watcher.CurrentZone) && (_lastZoneNotified != _watcher.CurrentZone || showZoneToast))
        {
            _lastZoneNotified = _watcher.CurrentZone;
            ZoneTipText.Text = CampaignGuideDb.FormatBannerText(_watcher.CurrentZone);
            ZoneTipBanner.Visibility = Visibility.Visible;
        }
        else if (string.IsNullOrWhiteSpace(_watcher.CurrentZone))
        {
            ZoneTipBanner.Visibility = Visibility.Collapsed;
        }

        if (_initializedTabs.Contains(NavBtnFarm)) RefreshFarmStatsUi();
    }
}
