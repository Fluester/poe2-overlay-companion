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
    // --- Пакетний Імпорт & Профілі ---
    private void GoToBuildsTab_Click(object sender, RoutedEventArgs e) => SelectPage(NavBtnBuilds);

    private void RefreshProfilesUi()
    {
        ProfilesPillsPanel.Children.Clear();
        _profileButtons.Clear();

        if (_profiles.Profiles.Count == 0)
        {
            ProfilesPillsPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("Профілів ще немає — скористайся кнопками нижче або перетягни сюди .build файли.", "No profiles yet — use buttons below or drag & drop .build files here.", "Профилей пока нет — используй кнопки ниже или перетащи сюда .build файлы."),
                Foreground = UiBrushes.FgMuted,
                FontSize = 11
            });
            BtnDeleteProfile.IsEnabled = false;
            BtnDeleteProfile.Opacity = 0.4;
            return;
        }

        foreach (var p in _profiles.Profiles)
        {
            var prof = p;
            var btn = new Button
            {
                Content = prof.Name.Length > 28 ? prof.Name[..27] + "…" : prof.Name,
                Style = (Style)FindResource("FlatButton"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 5, 5)
            };
            btn.Click += (_, _) => SelectActiveProfile(prof.Id);
            _profileButtons[prof.Id] = btn;
            ProfilesPillsPanel.Children.Add(btn);
        }
        HighlightActiveProfile();
    }

    private void SelectActiveProfile(string id)
    {
        _profiles.SelectProfile(id);
        _ai.Config.ActiveProfileId = _profiles.ActiveProfile?.Id ?? "";
        PoeAccountService.BindCharacterToProfile(_ai.Config, _ai.Config.ActiveProfileId);
        _ai.SaveConfig();
        _cachedActivePob = null;
        _overviewStageIdx = 0;
        _treeStageIdx = 0;

        HighlightActiveProfile();
        RefreshOverview();
        if (_initializedTabs.Contains(NavBtnPassives)) RefreshPassiveTreeTab(resetSteps: true);
        if (_initializedTabs.Contains(NavBtnItems))
        {
            RefreshDynamicTradeButtons();
            RefreshInstalledFiltersLabel();
        }
        if (_buildsUpdatesInit) RenderUpdateHistory();
        if (_lastItemEval != null) CheckClipboard(force: true);
    }

    private void HighlightActiveProfile()
    {
        var act = _profiles.ActiveProfile;
        foreach (var (id, btn) in _profileButtons)
        {
            bool on = act != null && id == act.Id;
            btn.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            btn.BorderBrush = on ? UiBrushes.BgBrass : UiBrushes.BorderSubtle;
            btn.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
        }
        BtnDeleteProfile.IsEnabled = act != null;
        BtnDeleteProfile.Opacity = act != null ? 1.0 : 0.4;
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile == null) return;
        string deletedId = _profiles.ActiveProfile.Id;
        if (_profiles.DeleteProfile(deletedId))
        {
            PoeAccountService.UnlinkDeletedProfile(_ai.Config, deletedId);
            _ai.Config.ActiveProfileId = _profiles.ActiveProfile?.Id ?? "";
            _ai.SaveConfig();
            _cachedActivePob = null;
            RefreshProfilesUi();
            RefreshOverview();
            if (_initializedTabs.Contains(NavBtnPassives)) RefreshPassiveTreeTab(resetSteps: true);
            if (_initializedTabs.Contains(NavBtnItems)) RefreshDynamicTradeButtons();
        }
    }

    private void BatchImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Обери один або кілька .build файлів", "Select one or more .build files", "Выбери один или несколько .build файлов"),
            Filter = "PoE 2 Build (*.build)|*.build|All (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            bool merge = ChkMergeBatchStages.IsChecked == true;
            var (ok, msg, added) = _profiles.BatchImportBuildFiles(NewProfileNameBox.Text, dlg.FileNames, merge);
            ApplyAfterBuildImport(ok, msg, added);
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void BatchImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = Loc.T("Обери папку з .build файлами білду", "Select folder with .build files", "Выбери папку с .build файлами билда")
        };
        if (dlg.ShowDialog() == true)
        {
            bool merge = ChkMergeBatchStages.IsChecked == true;
            var (ok, msg, added) = _profiles.BatchImportBuildFiles(NewProfileNameBox.Text, [dlg.FolderName], merge);
            ApplyAfterBuildImport(ok, msg, added);
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void ImportFromGamePlannerFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = BuildPlannerService.GetBuildPlannerFolder();
        bool merge = ChkMergeBatchStages?.IsChecked ?? true;
        var (ok, msg, added) = _profiles.BatchImportBuildFiles(NewProfileNameBox?.Text ?? "", [dir], merge);
        ApplyAfterBuildImport(ok, msg, added);
    }

    private void PastePob_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
                PobInputBox.Text = Clipboard.GetText().Trim();
        }
        catch { }
    }

    private void OpenMobalyticsImport_Click(object sender, RoutedEventArgs e)
    {
        string raw = (PobInputBox?.Text ?? "").Trim();
        string initialUrl = MobalyticsImportService.IsMobalyticsUrl(raw)
            ? raw
            : (_profiles.ActiveProfile is { GuideUrl: var gu } && MobalyticsImportService.IsMobalyticsUrl(gu)
                ? gu
                : "https://mobalytics.gg/poe-2/builds");
        LaunchMobalyticsWindow(initialUrl, NewProfileNameBox?.Text ?? "");
    }

    private void LaunchMobalyticsWindow(string url, string customName)
    {
        var win = new MobalyticsImportWindow(_profiles, url, customName);
        if (ShowStandaloneDialog(win) == true && win.ImportedProfile != null)
            ApplyAfterBuildImport(true, win.ResultMessage, [win.ImportedProfile]);
    }

    private async void BatchImportLines_Click(object sender, RoutedEventArgs e)
    {
        string raw = PobInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;

        ImportStatusText.Text = Loc.T("Імпортую...", "Importing...", "Импортирую...");
        var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 1 && MobalyticsImportService.IsMobalyticsUrl(lines[0]))
        {
            LaunchMobalyticsWindow(lines[0], NewProfileNameBox.Text);
            return;
        }
        if (lines.Length == 1 && !lines[0].StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var (ok, err, prof) = await _profiles.ImportFromPob2Async(NewProfileNameBox.Text, lines[0]);
            ApplyAfterBuildImport(ok, ok ? prof!.Name : err, prof != null ? [prof] : []);
        }
        else if (lines.Length == 1 && lines[0].StartsWith("http", StringComparison.OrdinalIgnoreCase) && !lines[0].Contains("pobb.in", StringComparison.OrdinalIgnoreCase) && !lines[0].Contains("pastebin", StringComparison.OrdinalIgnoreCase))
        {
            var (ok, err, prof) = await _profiles.ImportFromGuideUrlAsync(NewProfileNameBox.Text, lines[0]);
            ApplyAfterBuildImport(ok, ok ? prof!.Name : err, prof != null ? [prof] : []);
        }
        else
        {
            var (okCount, _, summary) = await _profiles.BatchImportMultiLinesAsync(raw);
            ApplyAfterBuildImport(okCount > 0, summary, []);
        }
    }

    private void ApplyAfterBuildImport(bool ok, string msg, List<BuildProfile> added)
    {
        ImportStatusText.Text = msg;
        ImportStatusText.Foreground = ok ? UiBrushes.FgEmerald : UiBrushes.FgCrimson;
        if (!ok) return;

        NewProfileNameBox.Text = "";
        PobInputBox.Text = "";
        _ai.Config.ActiveProfileId = _profiles.ActiveProfile?.Id ?? "";
        PoeAccountService.BindCharacterToProfile(_ai.Config, _ai.Config.ActiveProfileId);
        _ai.SaveConfig();
        _cachedActivePob = null;
        _overviewStageIdx = 0;
        _treeStageIdx = 0;

        RefreshProfilesUi();
        RefreshOverview();
        LoadBuildPlannerFiles();
        if (_initializedTabs.Contains(NavBtnPassives)) RefreshPassiveTreeTab(resetSteps: true);
        if (_initializedTabs.Contains(NavBtnItems)) RefreshDynamicTradeButtons();

        if (_profiles.ActiveProfile != null)
            SelectPage(NavBtnOverview);
    }

    // --- Будований Build Planner (.build лише з папки гри) ---
    private void InitPlannerWatcher()
    {
        try
        {
            string dir = BuildPlannerService.GetBuildPlannerFolder();
            Directory.CreateDirectory(dir);
            _plannerDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _plannerDebounce.Tick += (_, _) =>
            {
                _plannerDebounce.Stop();
                if (_buildsPlannerInit) LoadBuildPlannerFiles();
            };

            _plannerWatcher = new FileSystemWatcher(dir, "*.build")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler onFs = (_, _) => Dispatcher.BeginInvoke(() =>
            {
                _plannerDebounce?.Stop();
                _plannerDebounce?.Start();
            });
            _plannerWatcher.Created += onFs;
            _plannerWatcher.Changed += onFs;
            _plannerWatcher.Deleted += onFs;
            _plannerWatcher.Renamed += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                _plannerDebounce?.Stop();
                _plannerDebounce?.Start();
            });
        }
        catch { }
    }

    private void LoadBuildPlannerFiles()
    {
        string? prev = _selectedPlannerBuild?.FilePath;
        _plannerBuilds = BuildPlannerService.LoadGameBuildPlannerFiles();
        PlannerFilesPanel.Children.Clear();
        _plannerFileButtons.Clear();

        if (_plannerBuilds.Count == 0)
        {
            _selectedPlannerBuild = null;
            PlannerMetaText.Text = Loc.T("0 файлів", "0 files", "0 файлов");
            PlannerCardsPanel.Children.Clear();
            PlannerCardsPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Папка BuildPlanner порожня", "Game BuildPlanner folder is empty", "Папка BuildPlanner пуста"),
                Loc.T("Натисни «+ Файли .build» або перетягни файли сюди — вони автоматично скопіюються в папку PoE 2.", "Click '+ .build Files' or drop files here — they auto-copy into PoE 2's BuildPlanner folder.", "Нажми «+ Файлы .build» или перетащи файлы сюда — они скопируются в папку PoE 2.")));
            return;
        }

        foreach (var b in _plannerBuilds)
        {
            var plan = b;
            string shortName = ShortStageTitle(plan.Name, 20);
            var btn = new Button
            {
                Content = shortName,
                Style = (Style)FindResource("FlatButton"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 5, 5)
            };
            btn.Click += (_, _) => SelectPlannerBuild(plan);
            _plannerFileButtons[plan.FilePath] = btn;
            PlannerFilesPanel.Children.Add(btn);
        }

        int lvl = EffectiveLevel;
        var next = _plannerBuilds.FirstOrDefault(p => string.Equals(p.FilePath, prev, StringComparison.OrdinalIgnoreCase))
            ?? BuildPlannerService.SuggestBuildForLevel(_plannerBuilds, lvl)
            ?? _plannerBuilds[0];
        SelectPlannerBuild(next);
    }

    private void SelectPlannerBuild(LocalBuildPlan plan)
    {
        _selectedPlannerBuild = plan;
        foreach (var (path, btn) in _plannerFileButtons)
        {
            bool active = path == plan.FilePath;
            btn.Background = active ? UiBrushes.BgBrass : UiBrushes.BgCard;
            btn.BorderBrush = active ? UiBrushes.BgBrass : UiBrushes.BorderSubtle;
            btn.Foreground = active ? UiBrushes.FgDark : UiBrushes.FgMuted;
        }
        RenderPlannerCards();
    }

    private void PlannerFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _selectedPlannerBuild == null) return;
        RenderPlannerCards();
    }

    private void RenderPlannerCards()
    {
        PlannerCardsPanel.Children.Clear();
        if (_selectedPlannerBuild == null) return;
        var p = _selectedPlannerBuild;
        int lvl = EffectiveLevel;
        bool filterByLevel = ChkPlannerFilterByLevel.IsChecked == true;

        PlannerMetaText.Text = $"BuildPlanner · {p.PassivesCount} {Loc.LblPointsShort} · {Loc.LevelShort} {lvl}";
        PlannerCardsPanel.Children.Add(CreateSimpleRowCard(
            p.Name,
            $"{p.Author} · {p.LastModified:dd.MM.yyyy HH:mm} · {Loc.T("Офіційна папка PoE 2", "In PoE 2 BuildPlanner", "Официальная папка PoE 2")}"));

        var activeSkills = p.Skills.Where(s => !filterByLevel || (lvl >= s.MinLevel && lvl <= s.MaxLevel)).ToList();
        AddSectionHeader(PlannerCardsPanel, $"{Loc.LblActiveSkills} ({(filterByLevel ? $"{Loc.LevelShort} {lvl}" : Loc.LblAllLevels)})", topMargin: 8);
        foreach (var sk in activeSkills)
            PlannerCardsPanel.Children.Add(CreateSkillLinkCard(sk.SkillName, sk.Supports, sk.MinLevel));

        var upcomingSkills = p.Skills.Where(s => filterByLevel && s.MinLevel > lvl).OrderBy(s => s.MinLevel).Take(5).ToList();
        if (upcomingSkills.Count > 0)
        {
            AddSectionHeader(PlannerCardsPanel, Loc.LblUnlocksNext, topMargin: 8);
            foreach (var sk in upcomingSkills)
                PlannerCardsPanel.Children.Add(CreateSimpleRowCard($"{Loc.LevelShort} {sk.MinLevel}: {sk.SkillName}", string.Join(", ", sk.Supports)));
        }

        var activeGear = p.Gear.Where(g => !filterByLevel || (lvl >= g.MinLevel && lvl <= g.MaxLevel)).ToList();
        AddSectionHeader(PlannerCardsPanel, $"{Loc.LblGearTargets} ({(filterByLevel ? $"{Loc.LevelShort} {lvl}" : Loc.LblAllLevels)})", topMargin: 8);
        foreach (var g in activeGear)
            PlannerCardsPanel.Children.Add(CreateGearTargetCard(g.RawSlotId, PoeTermLocalizer.SlotName(g.RawSlotId), PoeTermLocalizer.TranslateGearDescription(g.Text), g.Text));
    }

    private void ReloadBuildPlanner_Click(object sender, RoutedEventArgs e) => LoadBuildPlannerFiles();

    private void OpenBuildPlannerFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = BuildPlannerService.GetBuildPlannerFolder();
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }


    // --- Оновлення Білдів (Без YouTube відео!) ---
    private void InitUpdateMonitorUi()
    {
        ChkAutoUpdateMonitor.IsChecked = _ai.Config.AutoUpdateMonitorEnabled;
        RefreshIntervalButtons();
        string lastCheck = _ai.Config.LastUpdateCheckUtc == DateTime.MinValue
            ? Loc.T("ще не перевірялось", "never checked", "ещё не проверялось")
            : _ai.Config.LastUpdateCheckUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        UpdateStatusText.Text = $"{lastCheck} · {_ai.Config.UpdateCheckIntervalHours / 24.0:0.#}d";
        RenderUpdateHistory();
    }

    private void RenderUpdateHistory()
    {
        UpdateHistoryPanel.Children.Clear();
        if (_profiles.Profiles.Count == 0)
        {
            UpdateHistoryPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Немає білдів для моніторингу", "No builds to monitor", "Нет билдов для мониторинга"),
                Loc.T("Додай білд у вкладці «Імпорт і Профілі» — оверлей відслідковуватиме зміни пасивок, скілів, екіпу та посилань.", "Add a build in Builds & Import to track passive, gem, gear, and link updates.", "Добавь билд во вкладке «Импорт и Профили» — оверлей будет отслеживать изменения.")));
            return;
        }

        foreach (var prof in _profiles.Profiles)
        {
            if (prof.Changelog.Count == 0)
            {
                UpdateHistoryPanel.Children.Add(CreateSimpleRowCard(
                    prof.Name,
                    Loc.T("Поки змін не зафіксовано. Натисни «Перевірити білди».", "No changes recorded yet. Click 'Check All Builds'.", "Пока изменений не зафиксировано. Нажми «Проверить билды».")));
                continue;
            }
            foreach (var ch in prof.Changelog.Take(6))
            {
                string body = string.Join("\n", ch.DiffLines.Select(l => $"• {l}"));
                UpdateHistoryPanel.Children.Add(CreateSimpleRowCard(
                    $"{prof.Name} · {ch.CheckedUtc.ToLocalTime():dd.MM HH:mm} · {ch.Headline}",
                    body));
            }
        }
    }

    private void RefreshIntervalButtons()
    {
        int h = _ai.Config.UpdateCheckIntervalHours;
        void StyleBtn(Button b, bool active)
        {
            b.Background = active ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.BorderBrush = active ? UiBrushes.BgBrass : UiBrushes.BorderSubtle;
            b.Foreground = active ? UiBrushes.FgDark : UiBrushes.FgMuted;
        }
        StyleBtn(BtnInterval48, h <= 48);
        StyleBtn(BtnInterval60, h > 48 && h <= 64);
        StyleBtn(BtnInterval72, h > 64);
    }

    private void UpdateSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _ai == null || ChkAutoUpdateMonitor == null) return;
        _ai.Config.AutoUpdateMonitorEnabled = ChkAutoUpdateMonitor.IsChecked == true;
        _ai.SaveConfig();
    }

    private void SetInterval48_Click(object sender, RoutedEventArgs e) => SetUpdateIntervalHours(48);
    private void SetInterval60_Click(object sender, RoutedEventArgs e) => SetUpdateIntervalHours(60);
    private void SetInterval72_Click(object sender, RoutedEventArgs e) => SetUpdateIntervalHours(72);

    private void SetUpdateIntervalHours(int hours)
    {
        _ai.Config.UpdateCheckIntervalHours = hours;
        _ai.SaveConfig();
        RefreshIntervalButtons();
        InitUpdateMonitorUi();
    }

    private async void CheckUpdatesNow_Click(object sender, RoutedEventArgs e) => await RunUpdateCheckAsync(isAuto: false);

    private async Task RunUpdateCheckAsync(bool isAuto)
    {
        if (_updateCheckBusy) return;
        _updateCheckBusy = true;
        try
        {
            var report = await BuildUpdateMonitorService.CheckAllBuildsAsync(_ai.Config, _profiles, EffectiveLevel);
            _ai.SaveConfig();
            if (_buildsUpdatesInit)
            {
                UpdateStatusText.Text = $"{DateTime.Now:dd.MM HH:mm} · {report.Headline}";
                RenderUpdateHistory();
            }

            if (!isAuto || report.HasChanges)
            {
                _toast.ShowToast(
                    Loc.T("Оновлення білдів", "Build Updates", "Обновления билдов"),
                    report.Headline,
                    report.HasChanges ? Color.FromRgb(0xD2, 0x99, 0x22) : Color.FromRgb(0x3F, 0xB9, 0x50),
                    nearCursor: false, seconds: 5.5);
            }
        }
        finally { _updateCheckBusy = false; }
    }

    // --- Авто-оновлення самого додатка з GitHub Releases ---
    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e) => await CheckAppUpdateAsync(silent: false);

    private async Task CheckAppUpdateAsync(bool silent)
    {
        if (_appUpdateBusy) return;
        _appUpdateBusy = true;
        try
        {
            if (!silent)
            {
                LblAppUpdateStatus.Text = Loc.T("Перевіряю GitHub...", "Checking GitHub...", "Проверяю GitHub...");
                LblAppUpdateStatus.Foreground = UiBrushes.FgMuted;
            }
            var info = await AppUpdateService.CheckForUpdateAsync();
            _pendingAppUpdate = info;
            if (!info.Ok)
            {
                if (!silent)
                {
                    LblAppUpdateStatus.Text = $"v{info.CurrentVersion} ({info.Error})";
                    LblAppUpdateStatus.Foreground = UiBrushes.FgMuted;
                }
                return;
            }

            if (info.HasUpdate)
            {
                UpdateAppUpdateStatusLabel();
                BtnInstallAppUpdate.Content = Loc.T($"Оновити до {info.LatestTag}", $"Install {info.LatestTag}", $"Обновить до {info.LatestTag}");
                BtnInstallAppUpdate.Visibility = Visibility.Visible;
                _toast.ShowToast(
                    Loc.T("Оновлення PoE 2 Overlay", "PoE 2 Overlay Update", "Обновление PoE 2 Overlay"),
                    Loc.T($"Вийшла версія {info.LatestTag}. Налаштування → «Оновити» встановить її в 1 клік.", $"Release {info.LatestTag} is out! Open Settings to update in 1 click.", $"Вышла версия {info.LatestTag}. Настройки → «Обновить»."),
                    Color.FromRgb(0x3F, 0xB9, 0x50), nearCursor: false, seconds: 6.0);
            }
            else
            {
                BtnInstallAppUpdate.Visibility = Visibility.Collapsed;
                if (!silent) UpdateAppUpdateStatusLabel();
            }
        }
        finally { _appUpdateBusy = false; }
    }

    private void UpdateAppUpdateStatusLabel()
    {
        if (LblAppUpdateStatus == null || _appUpdateBusy) return;
        var info = _pendingAppUpdate;
        if (info == null)
        {
            LblAppUpdateStatus.Text = $"v{AppUpdateService.GetCurrentVersionString()}";
            LblAppUpdateStatus.Foreground = UiBrushes.FgMuted;
            return;
        }
        if (!info.Ok)
        {
            LblAppUpdateStatus.Text = $"v{info.CurrentVersion}";
            LblAppUpdateStatus.Foreground = UiBrushes.FgMuted;
            return;
        }
        if (info.HasUpdate)
        {
            LblAppUpdateStatus.Text = Loc.T(
                $"Доступна {info.LatestTag} (зараз v{info.CurrentVersion})",
                $"Available: {info.LatestTag} (current v{info.CurrentVersion})",
                $"Доступна {info.LatestTag} (сейчас v{info.CurrentVersion})");
            LblAppUpdateStatus.Foreground = UiBrushes.FgEmerald;
        }
        else
        {
            LblAppUpdateStatus.Text = Loc.T(
                $"Вже найновіша (v{info.CurrentVersion})",
                $"Up to date (v{info.CurrentVersion})",
                $"Последняя версия (v{info.CurrentVersion})");
            LblAppUpdateStatus.Foreground = UiBrushes.FgEmerald;
        }
    }

    private async void InstallAppUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_appUpdateBusy) return;
        var info = _pendingAppUpdate;
        if (info == null || !info.HasUpdate) return;

        if (string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            Process.Start(new ProcessStartInfo(info.ReleaseUrl) { UseShellExecute = true });
            return;
        }

        _appUpdateBusy = true;
        BtnInstallAppUpdate.IsEnabled = false;
        LblAppUpdateStatus.Text = Loc.T($"Завантажую {info.AssetName}...", $"Downloading {info.AssetName}...", $"Скачиваю {info.AssetName}...");
        try
        {
            var (ok, err) = await AppUpdateService.DownloadAndLaunchUpdateAsync(info);
            if (!ok)
            {
                LblAppUpdateStatus.Text = err;
                LblAppUpdateStatus.Foreground = UiBrushes.FgCrimson;
                BtnInstallAppUpdate.IsEnabled = true;
                return;
            }
            LblAppUpdateStatus.Text = Loc.T("Запускаю оновлення...", "Restarting to apply update...", "Запускаю обновление...");
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LblAppUpdateStatus.Text = ex.Message;
            LblAppUpdateStatus.Foreground = UiBrushes.FgCrimson;
            BtnInstallAppUpdate.IsEnabled = true;
        }
        finally { _appUpdateBusy = false; }
    }

    private void OpenGuideUrl_Click(object sender, RoutedEventArgs e)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null || string.IsNullOrWhiteSpace(prof.GuideUrl))
        {
            UpdateStatusText.Text = Loc.T("У активного білду не вказаний URL гайду.", "Active build has no guide URL set.", "У активного билда не указан URL гайда.");
            return;
        }
        Process.Start(new ProcessStartInfo(prof.GuideUrl) { UseShellExecute = true });
    }

    private void DiffClipboardGuide_Click(object sender, RoutedEventArgs e)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null) return;
        string clip = "";
        try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
        if (string.IsNullOrWhiteSpace(clip) || clip.Length < 40)
        {
            UpdateStatusText.Text = Loc.T("Спочатку скопіюй текст зі сторінки гайду (Ctrl+A → Ctrl+C).", "Copy guide text to clipboard first (Ctrl+A -> Ctrl+C).", "Сначала скопируй текст со страницы гайда (Ctrl+A -> Ctrl+C).");
            return;
        }
        var ch = BuildUpdateMonitorService.DiffClipboardGuideText(prof, clip);
        _profiles.SaveProfiles();
        UpdateStatusText.Text = ch.Headline;
        RenderUpdateHistory();
    }

    private async void CompareGuideWithAi_Click(object sender, RoutedEventArgs e)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null) return;
        string clip = "";
        try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
        if (string.IsNullOrWhiteSpace(clip) || clip.Length < 80)
        {
            UpdateStatusText.Text = Loc.T("Скопіюй текст сторінки гайду (Ctrl+A → Ctrl+C)!", "Copy guide page text first (Ctrl+A -> Ctrl+C)!", "Скопируй текст страницы гайда (Ctrl+A -> Ctrl+C)!");
            return;
        }
        string trimmed = clip.Length > 9000 ? clip[..9000] : clip;
        SelectPage(NavBtnAi);
        await RunAiQueryAsync($"Compare this guide text with my build ({prof.Name}) and list any skill, support gem, passive, or gear changes:\n\n{trimmed}", includeScreenshot: false);
    }
}
