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
    private readonly ClientLogWatcher _watcher;
    private readonly TrayIconService _tray = new();
    private IDisposable? _fgHook;
    private FileSystemWatcher? _plannerWatcher;
    private DispatcherTimer? _plannerDebounce;
    private readonly GeminiAdvisorService _ai;
    private readonly BuildProfileManager _profiles;
    private readonly ToastWindow _toast;
    private readonly MiniHudWindow _miniHud;
    private readonly TradeHelperWindow _tradeHelper;
    private readonly CommandWheelWindow _commandWheel;
    private readonly DispatcherTimer _fastTimer;
    private readonly DispatcherTimer _slowTimer;

    private readonly Dictionary<Button, Grid> _navMap = [];
    private readonly HashSet<Button> _initializedTabs = [];
    private readonly Dictionary<string, Button> _plannerFileButtons = [];
    private readonly Dictionary<string, Button> _profileButtons = [];

    private bool _collapsed;
    private bool _clickThrough;
    private bool _allowTyping;
    private bool _miniHudMode;
    private bool _autoSyncLevel = true;
    private readonly bool _startMinimized;
    private int _alphaIdx;
    private readonly double[] _alphas = [0.95, 0.80, 0.65, 0.48, 1.0];

    private string _lastClipboard = "";
    private string _ignoreClipboardText = "";
    private string? _lastZoneNotified;

    private ItemEvaluation? _lastItemEval;
    private string _lastExternalItemText = "";
    private string? _overrideCompareSlotKey;
    private readonly List<(CheckBox box, TextBox minBox, ItemModFilterOption mod)> _modFilterCheckboxes = [];
    private DateTime _lastRatesFetchUtc = DateTime.MinValue;
    private bool _suspendTopmostHook;
    private List<LocalBuildPlan> _plannerBuilds = [];
    private LocalBuildPlan? _selectedPlannerBuild;
    private int _overviewStageIdx;
    private int _treeStageIdx;
    private bool _treeListOpen;
    private int? _treeWeaponSetFilter;
    private int _overviewSubtab; // 0 = main, 1 = gems encyclopedia, 2 = gear obtain
    private int _buildsSubtab;   // 0 = profiles/import, 1 = planner, 2 = updates
    private bool _buildsPlannerInit;
    private bool _buildsUpdatesInit;
    private string _gemFilterMode = "build";
    private string _inspectorAiPrompt = "";
    private int _filterStrictness = 3;
    private bool _filterGenBusy;
    private bool _itemsExtraExpanded;
    private bool _aiHasResponse;
    private bool _filterStatusCustom;
    private AppUpdateInfo? _pendingAppUpdate;
    private bool _appUpdateBusy;
    private Pob2Summary? _cachedActivePob;
    private bool _suppressTreeSliderEvent;
    private bool _suppressLeagueEvent;

    private bool _hkF8Ok, _hkF9Ok, _hkF10Ok, _hkF11Ok;
    private bool _f8Down, _f9Down, _f10Down, _f11Down, _buffDown;
    private int _slowTicks;
    private bool _poeRunningCached;
    private DateTime _lastAutoAiRun = DateTime.Now;
    private bool _aiBusy;
    private bool _updateCheckBusy;
    private DateTime _buffEndUtc = DateTime.MinValue;

    private double ExpandedWidth => Math.Clamp(_ai.Config.WindowWidth, 440, 1200);
    private double ExpandedHeight => Math.Clamp(_ai.Config.WindowHeight, 340, 900);
    private const double CollapsedHeight = 40;

    private int EffectiveLevel => Math.Max(1, _watcher.CurrentLevel ?? _ai.Config.LastKnownLevel);

    public MainWindow(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        _ai = new GeminiAdvisorService();
        _profiles = new BuildProfileManager();
        _profiles.SelectProfile(_ai.Config.ActiveProfileId);

        _watcher = new ClientLogWatcher();
        if (!string.IsNullOrWhiteSpace(_ai.Config.CustomClientTxtPath) && File.Exists(_ai.Config.CustomClientTxtPath))
            _watcher.SetPath(_ai.Config.CustomClientTxtPath);

        InitializeComponent();
        TreeCanvas.NodeClicked += (node, pathPts) =>
        {
            string pts = pathPts > 0 ? $" (+{pathPts} pts)" : "";
            LblTreeCanvasHint.Text = $"{node.Name} · {PoeTermLocalizer.NodeKindLabel(node.Kind)}{pts}";
        };

        _toast = new ToastWindow();
        _tradeHelper = new TradeHelperWindow();
        _tradeHelper.ExecuteChatCommand += ExecutePoeChatMacro;
        _commandWheel = new CommandWheelWindow();
        _commandWheel.ExecuteChatCommand += ExecutePoeChatMacro;
        _miniHud = new MiniHudWindow();
        _miniHud.ExpandRequested += SwitchFromMiniHud;
        _miniHud.AiRequested += () => _ = RunAiQueryAsync(Loc.T("Проаналізуй екран і підкажи наступний крок.", "Analyze my screen and suggest next step.", "Проанализируй экран и подскажи следующий шаг."), includeScreenshot: true);
        _miniHud.LockRequested += ToggleClickThrough;

        InitNavigation();
        LoadConfigToUi();
        ApplyLanguage();
        RefreshProfilesUi();
        RefreshOverview();
        RefreshLogStatus(showZoneToast: false);
        InitPlannerWatcher();

        _fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _fastTimer.Tick += FastTimer_Tick;

        _slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _slowTimer.Tick += SlowTimer_Tick;
        _slowTimer.Start();
        _poeRunningCached = ClientLogWatcher.IsPoe2ProcessRunning();

        if (_profiles.Profiles.Count > 0 && BuildUpdateMonitorService.IsCheckDue(_ai.Config))
            _ = RunUpdateCheckAsync(isAuto: true);

        _ = RefreshLeaguesListAsync();
        _ = CheckAppUpdateAsync(silent: true);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
            Win32OverlayHelper.AddClipboardFormatListener(source.Handle);
            _hkF8Ok = Win32OverlayHelper.RegisterHotKey(source.Handle, Win32OverlayHelper.HOTKEY_F8, 0, Win32OverlayHelper.VK_F8);
            _hkF9Ok = Win32OverlayHelper.RegisterHotKey(source.Handle, Win32OverlayHelper.HOTKEY_F9, 0, Win32OverlayHelper.VK_F9);
            _hkF10Ok = Win32OverlayHelper.RegisterHotKey(source.Handle, Win32OverlayHelper.HOTKEY_F10, 0, Win32OverlayHelper.VK_F10);
            _hkF11Ok = Win32OverlayHelper.RegisterHotKey(source.Handle, Win32OverlayHelper.HOTKEY_F11, 0, Win32OverlayHelper.VK_F11);
        }

        Win32OverlayHelper.ApplyDwmRoundedCorners(this);
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
        Win32OverlayHelper.SetWindowOpacity(this, _alphas[_alphaIdx]);

        _fgHook = Win32OverlayHelper.HookForegroundChange(() =>
            Dispatcher.BeginInvoke(() =>
            {
                if (_suspendTopmostHook) return;
                if (!_miniHudMode && IsVisible) Win32OverlayHelper.EnsureTopmostNoActivate(this);
                else if (_miniHudMode && _miniHud.IsVisible) Win32OverlayHelper.EnsureTopmostNoActivate(_miniHud);
            }));

        _tray.OnToggleShow = ToggleShowFromTray;
        _tray.OnToggleMiniHud = () =>
        {
            if (_miniHudMode) SwitchFromMiniHud();
            else SwitchToMiniHud_Click(this, new RoutedEventArgs());
        };
        _tray.OnToggleClickThrough = ToggleClickThrough;
        _tray.OnInstallFilter = () => InstallFilterCore();
        _tray.OnAutostartChanged = enabled => { if (ChkAutostart != null) ChkAutostart.IsChecked = enabled; };
        _tray.OnExitRequested = Close;
        _tray.IsClickThrough = () => _clickThrough;
        _tray.Attach(this);

        if (_startMinimized)
            Hide();

        SyncFastTimerState();
    }

    protected override void OnClosed(EventArgs e)
    {
        _fastTimer.Stop();
        _slowTimer.Stop();
        _fgHook?.Dispose();
        _tray.Dispose();
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            Win32OverlayHelper.RemoveClipboardFormatListener(source.Handle);
            if (_hkF8Ok) Win32OverlayHelper.UnregisterHotKey(source.Handle, Win32OverlayHelper.HOTKEY_F8);
            if (_hkF9Ok) Win32OverlayHelper.UnregisterHotKey(source.Handle, Win32OverlayHelper.HOTKEY_F9);
            if (_hkF10Ok) Win32OverlayHelper.UnregisterHotKey(source.Handle, Win32OverlayHelper.HOTKEY_F10);
            if (_hkF11Ok) Win32OverlayHelper.UnregisterHotKey(source.Handle, Win32OverlayHelper.HOTKEY_F11);
        }
        _watcher.Dispose();
        _plannerWatcher?.Dispose();
        _plannerDebounce?.Stop();
        _tradeHelper.Close();
        _commandWheel.Close();
        _miniHud.Close();
        _toast.Close();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32OverlayHelper.WM_MOUSEACTIVATE && !_allowTyping)
        {
            handled = true;
            return new IntPtr(Win32OverlayHelper.MA_NOACTIVATE);
        }

        if (msg == Win32OverlayHelper.WM_CLIPBOARDUPDATE)
            Dispatcher.BeginInvoke(new Action(() => CheckClipboard(force: false)));

        if (msg == (int)App.WmShowExistingOverlay)
        {
            Dispatcher.BeginInvoke(RestoreOverlayVisible);
            handled = true;
        }

        if (msg == Win32OverlayHelper.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == Win32OverlayHelper.HOTKEY_F8) { OpenCommandWheelNearCursor(); handled = true; }
            else if (id == Win32OverlayHelper.HOTKEY_F9) { ToggleCollapseOrMiniHud(); handled = true; }
            else if (id == Win32OverlayHelper.HOTKEY_F10) { ToggleClickThrough(); handled = true; }
            else if (id == Win32OverlayHelper.HOTKEY_F11)
            {
                _ = RunAiQueryAsync(Loc.T("Проаналізуй екран у PoE 2 та дай найкорисніші поради.", "Analyze my PoE 2 screen and give actionable tips.", "Проанализируй экран PoE 2 и дай полезные советы."), includeScreenshot: true);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    // --- Drag & Drop (.build файли або папки будь-куди у вікно) ---
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped || dropped.Length == 0) return;

        var filterFiles = dropped.Where(f => File.Exists(f) && f.EndsWith(".filter", StringComparison.OrdinalIgnoreCase)).ToList();
        if (filterFiles.Count > 0)
        {
            foreach (var ff in filterFiles)
                InstallAndPreviewFilter(ff);
            SelectPage(NavBtnItems);
            ShowLootFilterSubtab();
        }

        var buildItems = dropped.Where(f => Directory.Exists(f) || (File.Exists(f) && f.EndsWith(".build", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (buildItems.Length > 0)
        {
            bool merge = ChkMergeBatchStages?.IsChecked ?? true;
            var (ok, msg, added) = _profiles.BatchImportBuildFiles(NewProfileNameBox.Text, buildItems, merge);
            ApplyAfterBuildImport(ok, msg, added);
        }
    }

    // --- Навігація та Лінива ініціалізація (7 чистих Bootstrap вкладок) ---
    private void InitNavigation()
    {
        _navMap[NavBtnOverview] = PageOverview;
        _navMap[NavBtnPassives] = PagePassives;
        _navMap[NavBtnItems] = PageItems;
        _navMap[NavBtnBuilds] = PageBuilds;
        _navMap[NavBtnFarm] = PageFarm;
        _navMap[NavBtnAi] = PageAi;
        _navMap[NavBtnSettings] = PageSettings;
        SelectPage(NavBtnOverview);
    }

    private void GoToSettingsTab_Click(object sender, RoutedEventArgs e) => SelectPage(NavBtnSettings);

    private void NavClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && _navMap.ContainsKey(btn))
            SelectPage(btn);
    }

    private void SelectPage(Button activeBtn)
    {
        EnsureTabInitialized(activeBtn);

        foreach (var (btn, page) in _navMap)
        {
            bool active = ReferenceEquals(btn, activeBtn);
            page.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            btn.Background = active ? UiBrushes.BgBrass : UiBrushes.Transparent;
            btn.Foreground = active ? UiBrushes.FgDark : UiBrushes.FgMuted;
            btn.BorderBrush = UiBrushes.Transparent;
            btn.BorderThickness = new Thickness(0);
        }

        if (ReferenceEquals(activeBtn, NavBtnFarm) && (DateTime.UtcNow - _lastRatesFetchUtc).TotalMinutes >= 3)
            _ = RefreshCurrencyRatesAsync();
    }

    private void EnsureTabInitialized(Button tabBtn)
    {
        if (!_initializedTabs.Add(tabBtn)) return;

        if (ReferenceEquals(tabBtn, NavBtnBuilds))
            SetBuildsSubtab(_buildsSubtab);
        else if (ReferenceEquals(tabBtn, NavBtnPassives))
            RefreshPassiveTreeTab(resetSteps: true);
        else if (ReferenceEquals(tabBtn, NavBtnItems))
        {
            RefreshDynamicTradeButtons();
            RefreshInstalledFiltersLabel();
            RefreshWornSlotsStrip();
        }
        else if (ReferenceEquals(tabBtn, NavBtnFarm))
        {
            RefreshFarmStatsUi();
            _ = RefreshCurrencyRatesAsync();
        }
    }

    private void ShowBuildsMainSubtab_Click(object sender, RoutedEventArgs e) => SetBuildsSubtab(0);
    private void ShowBuildsPlannerSubtab_Click(object sender, RoutedEventArgs e) => SetBuildsSubtab(1);
    private void ShowBuildsUpdatesSubtab_Click(object sender, RoutedEventArgs e) => SetBuildsSubtab(2);

    private void SetBuildsSubtab(int idx)
    {
        _buildsSubtab = idx;
        BuildsMainSubPanel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PagePlanner.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageUpdates.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;

        void StyleSub(Button b, bool on)
        {
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.BorderBrush = on ? UiBrushes.BgBrass : UiBrushes.BorderSubtle;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
            b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        StyleSub(BtnSubBuildsImport, idx == 0);
        StyleSub(BtnSubBuildsPlanner, idx == 1);
        StyleSub(BtnSubBuildsUpdates, idx == 2);

        if (idx == 1 && !_buildsPlannerInit)
        {
            _buildsPlannerInit = true;
            LoadBuildPlannerFiles();
        }
        else if (idx == 2 && !_buildsUpdatesInit)
        {
            _buildsUpdatesInit = true;
            InitUpdateMonitorUi();
        }
    }

    // --- Мови (UA / EN / RU) — миттєво оновлюють І інтерфейс, І весь контент білдів ---
    private void LangUk_Click(object sender, RoutedEventArgs e) => SetLanguage(AppLanguage.Uk);
    private void LangEn_Click(object sender, RoutedEventArgs e) => SetLanguage(AppLanguage.En);
    private void LangRu_Click(object sender, RoutedEventArgs e) => SetLanguage(AppLanguage.Ru);

    private void SetLanguage(AppLanguage lang)
    {
        _ai.Config.Language = lang;
        Loc.Current = lang;
        _ai.SaveConfig();
        ApplyLanguage();

        RefreshLogStatus(showZoneToast: false);
        RefreshProfilesUi();
        RefreshOverview();
        if (_initializedTabs.Contains(NavBtnPassives)) RefreshPassiveTreeTab(resetSteps: false);
        if (_initializedTabs.Contains(NavBtnItems))
        {
            RefreshDynamicTradeButtons();
            RefreshWornSlotsStrip();
        }
        if (_buildsPlannerInit) RenderPlannerCards();
        if (_initializedTabs.Contains(NavBtnFarm)) RefreshFarmStatsUi();
        if (_buildsUpdatesInit) InitUpdateMonitorUi();
        if (_lastItemEval != null) CheckClipboard(force: true);
    }

    private void ApplyLanguage()
    {
        void StyleLang(Button b, bool active)
        {
            b.Background = active ? UiBrushes.BgBrass : UiBrushes.Transparent;
            b.Foreground = active ? UiBrushes.FgDark : UiBrushes.FgMuted;
        }
        StyleLang(LangBtnUk, Loc.Current == AppLanguage.Uk);
        StyleLang(LangBtnEn, Loc.Current == AppLanguage.En);
        StyleLang(LangBtnRu, Loc.Current == AppLanguage.Ru);

        TxtNavOverview.Text = Loc.T("Огляд", "Overview", "Обзор");
        TxtNavPassives.Text = Loc.T("Пасивки", "Passives", "Пассивки");
        TxtNavItems.Text = Loc.T("Предмети", "Items", "Предметы");
        TxtNavBuilds.Text = Loc.T("Білди", "Builds", "Билды");
        TxtNavFarm.Text = Loc.T("Сесія", "Session", "Сессия");
        TxtNavAi.Text = "AI";
        TxtNavSettings.Text = Loc.T("Налаштування", "Settings", "Настройки");
        BtnSubBuildsImport.Content = Loc.T("Профілі та імпорт", "Profiles & Import", "Профили и импорт");
        BtnSubBuildsPlanner.Content = Loc.T("Планувальник .build", ".build Planner", "Планировщик .build");
        BtnSubBuildsUpdates.Content = Loc.T("Оновлення", "Updates", "Обновления");
        UpdateAlphaButtonLabel();
        UpdateLogButtonUi();

        OverviewEmptyTitle.Text = Loc.T("Поки що немає доданого білду", "No Build Profile Loaded", "Пока нет добавленного билда");
        OverviewEmptyBody.Text = Loc.LblNoBuildsYet;
        BtnSubOverviewMain.Content = Loc.T("Огляд білду", "Build Overview", "Обзор билда");
        BtnSubOverviewGems.Content = Loc.T("Камені та рівні", "Gems & Levels", "Камни и уровни");
        BtnSubOverviewGear.Content = Loc.T("Шмотки: де взяти", "Gear & Drops", "Вещи: где взять");
        BtnOnboardMobalytics.Content = Loc.T("+ Mobalytics (Браузер)", "+ Mobalytics (Browser)", "+ Mobalytics (Браузер)");
        BtnOnboardImportFiles.Content = Loc.T("+ Пакетний імпорт .build", "+ Batch Import .build", "+ Пакетный импорт .build");
        BtnOnboardGoImport.Content = Loc.T("PoB2 / pobb.in / URL", "PoB2 / pobb.in / URL", "PoB2 / pobb.in / URL");
        BtnOnboardLoadPlanner.Content = Loc.T("Взяти з BuildPlanner гри", "Load from Game BuildPlanner", "Взять из BuildPlanner игры");
        BtnGemFilterBuild.Content = Loc.T("У білді", "In Build", "В билде");
        BtnGemFilterAll.Content = Loc.T("Каталог PoE 2", "PoE 2 Catalog", "Каталог PoE 2");

        LblTreeStage.Text = Loc.T("Етап:", "Stage:", "Этап:");
        ChkComparePrevStage.Content = Loc.T("Порівняти з попереднім етапом", "Compare vs Previous Stage", "Сравнить с предыдущим этапом");
        BtnTreeFit.Content = Loc.T("Центр", "Fit Tree", "Центр");
        BtnTreeAiExplain.Content = Loc.T("AI-розбір", "AI Tree", "AI-разбор");
        LblTreeCanvasHint.Text = Loc.T("ЛКМ = рух · Колесо = зум · ПКМ = центр · Клік по списку → фокус", "Drag = pan · Wheel = zoom · Right-click = center · Click list -> focus", "ЛКМ = сдвиг · Колесо = зум · ПКМ = центр · Клик по списку -> фокус");
        LblTreeSteps.Text = Loc.T("Крок:", "Step:", "Шаг:");
        LblLegendAdd.Text = Loc.T("+Взяти", "+Take", "+Взять");
        LblLegendKeep.Text = Loc.T("Взято", "Kept", "Взято");
        LblLegendRespec.Text = Loc.T("-Скинути", "-Respec", "-Сбросить");
        ChkOnlyKeyPassives.Content = Loc.T("Лише Keystones / Notables", "Keystones / Notables Only", "Только Keystones / Notables");

        ChkAutoClipboard.Content = Loc.T("Авто Ctrl+C", "Auto Ctrl+C", "Авто Ctrl+C");
        ChkToastPopup.Content = Loc.T("Тост біля курсора", "Cursor Toast", "Тост у курсора");
        ChkSoundAlert.Content = Loc.T("Звук", "Sound", "Звук");
        BtnCheckClipboard.Content = Loc.T("Перевірити", "Check Now", "Проверить");
        BtnTradeLastItem.Content = Loc.T("Trade2 (предмет)", "Trade2 (Item)", "Trade2 (предмет)");
        BtnSubItemsEval.Content = Loc.T("Перевірка Ctrl+C", "Item Check (Ctrl+C)", "Проверка Ctrl+C");
        BtnSubLootFilter.Content = Loc.T("Лутофільтр (.filter / AI)", "Loot Filter (.filter / AI)", "Лутфильтр (.filter / AI)");
        LblFilterName.Text = Loc.T("Файл:", "File:", "Файл:");
        BtnAiGenerateFilter.Content = Loc.T("AI-генерувати", "AI Generate", "AI-генерировать");
        BtnLoadCustomFilter.Content = Loc.T("Завантажити .filter", "Load .filter", "Загрузить .filter");
        BtnSaveFilterToGame.Content = Loc.T("Зберегти в PoE 2", "Save to PoE 2", "Сохранить в PoE 2");
        RefreshInstalledFiltersLabel();
        if (!_filterStatusCustom)
            FilterStatusText.Text = Loc.T("Згенеруй під білд через AI або завантаж власний .filter.", "Generate with AI for your build or load a custom .filter file.", "Сгенерируй под билд через AI или загрузи свой .filter.");
        UpdateItemsExtraButtonUi();
        LblTradeTitle.Text = Loc.T("PoE 2 Trade та Regex", "PoE 2 Trade & Regex", "PoE 2 Trade и Regex");
        LblLeague.Text = Loc.T("Ліга:", "League:", "Лига:");
        LblSideCompareTitle.Text = Loc.T("ПОРІВНЯННЯ В ОВЕРЛЕЇ", "IN-OVERLAY COMPARISON", "СРАВНЕНИЕ В ОВЕРЛЕЕ");
        LblWornQuickTitle.Text = Loc.T("Твої вдягнуті предмети (клік — переглянути або порівняти):", "Your equipped items (click to inspect or compare):", "Твои надетые предметы (клик — осмотр или сравнение):");
        BtnToggleCompareAgainstLast.Content = Loc.T("⇄ Порівняти з буфером", "⇄ Compare vs Last Ctrl+C", "⇄ Сравнить с буфером");
        if (_lastItemEval == null)
        {
            ItemVerdictText.Text = Loc.T("Наведи на предмет у PoE 2 і натисни Ctrl+C", "Hover an item in PoE 2 and press Ctrl+C", "Наведи на предмет в PoE 2 и нажми Ctrl+C");
            ItemDetailsText.Text = Loc.T("Перевірка предмета адаптується під активний білд та поточний етап.", "Evaluation adapts automatically to your active build and stage.", "Оценка предмета адаптируется под активный билд и текущий этап.");
            CompareNewTitle.Text = Loc.T("Оцінюваний предмет", "Evaluated Item", "Оцениваемый предмет");
            CompareWornTitle.Text = Loc.T("На тобі зараз", "Equipped Now", "Надето сейчас");
        }

        ChkPlannerFilterByLevel.Content = Loc.T("Фільтр за моїм рівнем (level_interval)", "Filter by my level (level_interval)", "Фильтр по моему уровню (level_interval)");
        BtnPlannerMakeProfile.Content = Loc.T("Активувати", "Make Active Build", "Активировать");
        BtnOpenPlannerDir.Content = Loc.T("Папка гри", "Game Folder", "Папка игры");

        LblFarmHeader.Text = Loc.T("Статистика сесії (Client.txt)", "Session Tracker (Client.txt)", "Статистика сессии (Client.txt)");
        BtnResetFarm.Content = Loc.T("Скинути", "Reset", "Сбросить");
        LblKpiSession.Text = Loc.T("Час сесії", "Session Time", "Время сессии");
        LblKpiZone.Text = Loc.T("Поточна зона", "Current Zone", "Текущая зона");
        LblKpiMaps.Text = Loc.T("Карт / Середній час", "Maps / Avg Clear", "Карт / Среднее время");
        LblKpiEncounters.Text = Loc.T("Енкаунтери / Смерті", "Encounters / Deaths", "Энкаунтеры / Смерти");
        LblStashHeader.Text = Loc.T("Скриня (Net-Worth)", "Stash (Net-Worth)", "Тайник (Net-Worth)");
        if (_lastRatesFetchUtc == DateTime.MinValue)
            CurrencyRatesText.Text = Loc.T("Натисни «↻ Курси» для завантаження Divine / Chaos → Ex.", "Click '↻ Rates' to load Divine / Chaos -> Ex rates.", "Нажми «↻ Курсы» для загрузки курсов Divine / Chaos → Ex.");
        if (_ai.Config.LastStashScanUtc == DateTime.MinValue)
            StashBreakdownText.Text = Loc.T("Натисни «Оцінити скриню (Stash)».", "Click 'Scan Stash Net-Worth'.", "Нажми «Оценить тайник (Stash)».");

        ChkAutoUpdateMonitor.Content = Loc.T("Моніторити автоматично:", "Auto check:", "Проверять автоматически:");
        BtnInterval48.Content = Loc.T("2 дні", "2 days", "2 дня");
        BtnInterval60.Content = Loc.T("2.5 дні", "2.5 days", "2.5 дня");
        BtnInterval72.Content = Loc.T("3 дні", "3 days", "3 дня");
        BtnCheckUpdatesNow.Content = Loc.T("Перевірити білди", "Check All Builds", "Проверить билды");
        BtnOpenGuideUpd.Content = Loc.T("Відкрити гайд", "Open Guide", "Открыть гайд");
        BtnDiffClipboard.Content = Loc.T("Diff (Ctrl+C)", "Clipboard Diff", "Diff (Ctrl+C)");
        BtnAiDiff.Content = Loc.T("AI Diff (Ctrl+C)", "AI Diff", "AI Diff");
        if (!_buildsUpdatesInit)
            UpdateStatusText.Text = Loc.T("Відслідковування змін у .build, PoB2 і посиланнях.", "Tracks changes across .build files, PoB2, and guide URLs.", "Отслеживание изменений в .build, PoB2 и ссылках.");

        BtnSaveApi.Content = Loc.T("Зберегти", "Save", "Сохранить");
        BtnAiAsk.Content = Loc.T("Запитати", "Ask", "Спросить");
        BtnAiScreen.Content = Loc.T("Скрін + AI (F11)", "Screen + AI (F11)", "Скрин + AI (F11)");
        ChkAutoAi30.Content = Loc.T("Авто-скан 30хв", "Auto-scan 30m", "Авто-скан 30м");
        ChkAiOnlySafeZone.Content = Loc.T("Лише місто/Hideout", "Town/Hideout only", "Только город/Hideout");
        if (!_aiHasResponse && !_aiBusy)
            AiMetaText.Text = Loc.T("Натисни F11 у грі для аналізу екрану.", "Press F11 in-game to analyze your screen.", "Нажми F11 в игре для анализа экрана.");

        LblProfilesTitle.Text = Loc.T("Профілі білдів", "Build Profiles", "Профили билдов");
        BtnDeleteProfile.Content = Loc.T("Видалити обраний", "Delete Selected", "Удалить выбранный");
        LblBatchImportTitle.Text = Loc.T("Імпорт білдів (.build / папка / PoB2 / Mobalytics)", "Import Builds (.build / Folder / PoB2 / Mobalytics)", "Импорт билдов (.build / папка / PoB2 / Mobalytics)");
        LblProfileName.Text = Loc.T("Назва:", "Name:", "Имя:");
        ChkMergeBatchStages.Content = Loc.T("Об'єднати обрані .build файли як етапи ОДНОГО білду (за рівнями)", "Merge selected .build files into ONE multi-stage build", "Объединить выбранные .build файлы в ОДИН поэтапный билд");
        BtnImportMobalytics.Content = Loc.T("+ Mobalytics (Браузер)", "+ Mobalytics (Browser)", "+ Mobalytics (Браузер)");
        BtnBatchFiles.Content = Loc.T("+ Обрати .build (кілька)", "+ Select .build (multi)", "+ Выбрать .build (несколько)");
        BtnBatchFolder.Content = Loc.T("+ З папки", "+ From Folder", "+ Из папки");
        BtnPastePob.Content = Loc.T("З буфера", "Paste Clipboard", "Из буфера");
        BtnImportMultiText.Content = Loc.T("+ Імпорт списку (PoB2 / URL)", "+ Import List (PoB2 / URL)", "+ Импорт списка (PoB2 / URL)");

        LblAppSettingsTitle.Text = Loc.T("Розмір вікна, лог і фільтр", "Overlay Window, Log & Filter", "Размер окна, лог и фильтр");
        LblSettingsAccountTitle.Text = Loc.T("Акаунт PoE 2, персонаж і ліга", "PoE 2 Account, Character & League", "Аккаунт PoE 2, персонаж и лига");
        BtnConnectPoeAccount.Content = Loc.T("Підключити PoE 2 акаунт (pathofexile2.com)", "Connect PoE 2 Account (pathofexile2.com)", "Подключить PoE 2 аккаунт (pathofexile2.com)");
        LblSettingsLeague.Text = Loc.T("Ліга:", "League:", "Лига:");
        BtnRefreshLeagues.Content = Loc.T("↻ Ліги", "↻ Leagues", "↻ Лиги");
        LblOverlaySize.Text = Loc.T("Розмір у грі:", "In-game Size:", "Размер в игре:");
        LblSettingsAiTitle.Text = Loc.T("AI-радник і генератор фільтрів (API)", "AI Advisor & Filter Generator (API)", "AI-советник и генератор фильтров (API)");
        BtnToggleApiDrawer.Content = Loc.T("Налаштування API", "API Settings", "Настройки API");
        BtnToggleTreeList.Content = Loc.T("Список", "Nodes", "Список");
        BtnEquipWornSlot.Content = Loc.T("Це на мені", "Equip as Mine", "Это на мне");
        BtnOpenTradeHelper.Content = Loc.T("Трейди", "Trade Bar", "Трейды");
        BtnOpenWiki.Content = "PoE2DB";
        BtnOpenWiki.ToolTip = Loc.T("Відкрити предмет на PoE2DB / Wiki", "Look up item on PoE2DB / Wiki", "Открыть предмет на PoE2DB / Wiki");
        LblWaystoneModsTitle.Text = Loc.T("МОДИ WAYSTONE / КАРТИ (КЛІК — ЗМІНИТИ ТЕГ):", "WAYSTONE / MAP MODS (CLICK TO TAG):", "МОДЫ WAYSTONE / КАРТЫ (КЛИК — ТЕГ):");
        LblModFiltersTitle.Text = Loc.T("ПРАЙС-ЧЕК ЗА ВИБРАНИМИ МОДАМИ (МІН.):", "CUSTOM MOD PRICE CHECK (MIN):", "ПРАЙС-ЧЕК ПО ВЫБРАННЫМ МОДАМ (МИН.):");
        LblLiveListingsTitle.Text = Loc.T("ОСТАННІ ЛОТИ TRADE 2 (ВІК):", "LIVE TRADE 2 LISTINGS (AGE):", "ПОСЛЕДНИЕ ЛОТЫ TRADE 2 (ВОЗРАСТ):");
        BtnRecheckPriceByMods.Content = Loc.T("Ціна по модах", "Check Selected", "Цена по модам");
        BtnOpenTradeByMods.Content = Loc.T("Відкрити Trade 2", "Open Trade 2", "Открыть Trade 2");
        LblCurrencyRatesTitle.Text = Loc.T("Живі курси валют Trade 2", "Live Trade 2 Currency Rates", "Живые курсы валют Trade 2");
        BtnRefreshCurrencyRates.Content = Loc.T("↻ Курси", "↻ Rates", "↻ Курсы");
        BtnScanStashNetWorth.Content = Loc.T("Оцінити скриню (Stash)", "Scan Stash Net-Worth", "Оценить тайник (Stash)");
        BtnCopyVendorRegex.Content = Loc.T("Regex Торгівля", "Vendor Regex", "Regex Торговец");
        BtnCopyWaystoneRegex.Content = Loc.T("Regex Мап", "Waystone Regex", "Regex Карт");
        ChkFocusOnCtrlC.Content = Loc.T("Відкривати вкладку при Ctrl+C", "Switch tab on Ctrl+C", "Открывать вкладку при Ctrl+C");
        ChkTradeHelper.Content = Loc.T("Trade Helper (шепіт)", "Trade Helper (whispers)", "Trade Helper (шепот)");
        ChkHideNoGame.Content = Loc.T("Ховати без PoE 2", "Hide without PoE 2", "Скрывать без PoE 2");
        ChkAutostart.Content = Loc.T("Запуск з Windows", "Launch with Windows", "Запуск с Windows");
        InstallFilterBtn.Content = Loc.T("Встановити .filter", "Install .filter", "Установить .filter");
        ChkBuffTimer.Content = Loc.T("Таймер бафу:", "Buff Timer:", "Таймер баффа:");
        LblSettingsHotkeysTitle.Text = Loc.T("Таймер бафу та гарячі клавіші", "Buff Timer & Hotkeys", "Таймер баффа и горячие клавиши");
        LblSettingsHotkeyHint.Text = Loc.T("F8 Команди · F9 Згорнути · F10 Клік · F11 AI", "F8 Cmds · F9 Toggle · F10 Lock · F11 AI", "F8 Команды · F9 Свернуть · F10 Клик · F11 AI");
        LblCustomCmds.Text = Loc.T("F8 команди:", "F8 cmds:", "F8 команды:");
        ApiKeyBox.ToolTip = Loc.T("Встав новий API-ключ, щоб зберегти або замінити поточний", "Paste an API key to save or replace the current one", "Вставь API-ключ, чтобы сохранить или заменить текущий");
        BuffKeyBox.ToolTip = Loc.T("Клавіша активації (напр. X, R, Q)", "Trigger key (e.g. X, R, Q)", "Клавиша активации (напр. X, R, Q)");
        BuffLabelBox.ToolTip = Loc.T("Назва бафу на таймері", "Timer display label", "Название баффа на таймере");
        BuffSecBox.ToolTip = Loc.T("Тривалість у секундах", "Duration in seconds", "Длительность в секундах");
        BtnCheckAppUpdate.Content = Loc.T("↻ Оновлення (GitHub)", "↻ Check App Update", "↻ Обновление (GitHub)");
        BtnInstallAppUpdate.Content = _pendingAppUpdate is { HasUpdate: true }
            ? Loc.T($"Оновити до {_pendingAppUpdate.LatestTag}", $"Install {_pendingAppUpdate.LatestTag}", $"Обновить до {_pendingAppUpdate.LatestTag}")
            : Loc.T("Оновити зараз", "Update Now", "Обновить сейчас");
        UpdateAppUpdateStatusLabel();

        _tradeHelper.RefreshUi();
        UpdateTypingButtons();
        UpdateAiStatusLabel();
        UpdateAccountStatusLabel();
        if (LblPortableStatus != null)
            LblPortableStatus.Text = AppPaths.IsPortableMode ? @"Portable (.\Data)" : "";
        _tray.UpdateTooltip($"PoE 2 Overlay Companion · {Loc.T("F9 згорнути", "F9 toggle", "F9 свернуть")}");
    }

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

    // --- Візуальний Огляд Білду + Гемопедія + Де взяти шмотки ---
    private void ShowOverviewMainSubtab_Click(object sender, RoutedEventArgs e) => SetOverviewSubtab(0);
    private void ShowOverviewGemsSubtab_Click(object sender, RoutedEventArgs e) => SetOverviewSubtab(1);
    private void ShowOverviewGearSubtab_Click(object sender, RoutedEventArgs e) => SetOverviewSubtab(2);

    private void SetOverviewSubtab(int idx)
    {
        _overviewSubtab = idx;
        OverviewMainSubPanel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        OverviewGemsPanel.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        OverviewGearPanel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;

        void StyleSub(Button b, bool on)
        {
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.BorderBrush = on ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
            b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        StyleSub(BtnSubOverviewMain, idx == 0);
        StyleSub(BtnSubOverviewGems, idx == 1);
        StyleSub(BtnSubOverviewGear, idx == 2);

        if (idx == 1) RefreshGemsSubtab();
        else if (idx == 2) RefreshGearObtainSubtab();
    }

    private void GemFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && _overviewSubtab == 1) RefreshGemsSubtab();
    }

    private void SetGemFilterBuild_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("build");
    private void SetGemFilterAll_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("all");
    private void SetGemFilterActive_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("active");
    private void SetGemFilterSupport_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("support");
    private void SetGemFilterSpirit_Click(object sender, RoutedEventArgs e) => SetGemFilterMode("spirit");

    private void SetGemFilterMode(string mode)
    {
        _gemFilterMode = mode;
        foreach (var (b, m) in new[] { (BtnGemFilterBuild, "build"), (BtnGemFilterAll, "all"), (BtnGemFilterActive, "active"), (BtnGemFilterSupport, "support"), (BtnGemFilterSpirit, "spirit") })
        {
            bool on = m == mode;
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
            b.BorderBrush = on ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
        }
        RefreshGemsSubtab();
    }

    private void RefreshOverview()
    {
        OverviewCardsPanel.Children.Clear();
        OverviewInspectorCard.Visibility = Visibility.Collapsed;
        var prof = _profiles.ActiveProfile;
        bool hasAccount = !string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName) ||
                          _ai.Config.WornGearBySlot.Count > 0 ||
                          _ai.Config.PoeCharacterSkills.Count > 0;
        int curLvl = EffectiveLevel;

        if (prof == null && !hasAccount)
        {
            HeaderBuildBadge.Text = Loc.NoBuildSelected;
            OverviewBuildTitle.Text = Loc.NoBuildSelected;
            OverviewBuildMeta.Text = Loc.T("Імпортуй з Mobalytics, .build файли, PoB2 або URL", "Import from Mobalytics, .build files, PoB2, or URL", "Импортируй с Mobalytics, .build файлы, PoB2 или URL");
            OverviewStageNav.Visibility = Visibility.Collapsed;
            OverviewEmptyCard.Visibility = Visibility.Visible;
            OverviewContentWrap.Visibility = Visibility.Collapsed;
            if (_overviewSubtab == 1) RefreshGemsSubtab();
            else if (_overviewSubtab == 2) RefreshGearObtainSubtab();
            return;
        }

        OverviewEmptyCard.Visibility = Visibility.Collapsed;
        OverviewContentWrap.Visibility = Visibility.Visible;

        if (prof == null)
        {
            HeaderBuildBadge.Text = Loc.NoBuildSelected;
            OverviewBuildTitle.Text = !string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName)
                ? $"{_ai.Config.PoeCharacterName} · {Loc.LevelShort}{curLvl}"
                : Loc.T("Твій персонаж (без білду)", "Your Character (No Build)", "Твой персонаж (без билда)");
            OverviewBuildMeta.Text = Loc.T(
                $"PoE 2 Акаунт (відображається окремо від білду) · {(string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterClass) ? "PoE 2" : _ai.Config.PoeCharacterClass)} · {_ai.Config.TradeLeague}",
                $"PoE 2 Account (separate from builds) · {(string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterClass) ? "PoE 2" : _ai.Config.PoeCharacterClass)} · {_ai.Config.TradeLeague}",
                $"PoE 2 Аккаунт (отдельно от билда) · {(string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterClass) ? "PoE 2" : _ai.Config.PoeCharacterClass)} · {_ai.Config.TradeLeague}");
            OverviewStageNav.Visibility = Visibility.Collapsed;

            // Компактна панель додавання окремого білд-гайда (щоб не плутати акаунт із білдом)
            var ctaStack = new StackPanel();
            ctaStack.Children.Add(new TextBlock
            {
                Text = Loc.T("Білд-гайд ще не додано (використовуються дані твого акаунта)", "No build guide imported yet (showing live account character)", "Билд-гайд ещё не добавлен (показан персонаж аккаунта)"),
                Foreground = UiBrushes.FgBrass,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11
            });
            ctaStack.Children.Add(new TextBlock
            {
                Text = Loc.T("Імпортуй сторонній білд із Mobalytics, .build або PoB2 — тоді всі предмети й пасивки будуть порівнюватись І з твоїм екіпом, І з білд-гайдом.", "Import a build guide from Mobalytics, .build, or PoB2 to unlock passive tree progression and side-by-side build comparisons.", "Импортируй билд-гайд с Mobalytics, .build или PoB2 для дерева пассивок и двойного сравнения."),
                Foreground = UiBrushes.FgMuted,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 6)
            });
            var ctaBtns = new WrapPanel();
            var btnMob = new Button { Content = "+ Mobalytics", Style = (Style)FindResource("BrassButton"), FontSize = 9.5, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
            btnMob.Click += OpenMobalyticsImport_Click;
            var btnImp = new Button { Content = Loc.T("+ Імпорт білду (.build / PoB2)", "+ Import Build (.build / PoB2)", "+ Импорт билда"), Style = (Style)FindResource("FlatButton"), FontSize = 9.5, Padding = new Thickness(8, 3, 8, 3) };
            btnImp.Click += GoToBuildsTab_Click;
            ctaBtns.Children.Add(btnMob);
            ctaBtns.Children.Add(btnImp);
            ctaStack.Children.Add(ctaBtns);
            OverviewCardsPanel.Children.Add(new Border
            {
                Background = UiBrushes.BgCard,
                BorderBrush = UiBrushes.BorderBrass,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = ctaStack
            });

            if (!string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName))
            {
                string accSummary = FormatAccountStatsSummary();
                OverviewCardsPanel.Children.Add(CreateSimpleRowCard(
                    $"{Loc.T("PoE 2 Акаунт", "PoE 2 Account", "PoE 2 Аккаунт")}: {_ai.Config.PoeCharacterName} · {Loc.LevelShort} {_ai.Config.PoeCharacterLevel} {_ai.Config.PoeCharacterClass} ({_ai.Config.TradeLeague})",
                    !string.IsNullOrWhiteSpace(accSummary)
                        ? accSummary
                        : $"{_ai.Config.WornGearBySlot.Count} {Loc.T("слотів", "slots", "слотов")}"));
            }

            if (_ai.Config.PoeCharacterSkills.Count > 0)
            {
                AddSectionHeader(OverviewCardsPanel, $"{Loc.T("Скіли персонажа", "Synced Character Skills", "Скиллы персонажа")} ({_ai.Config.PoeCharacterSkills.Count})", topMargin: 8);
                foreach (var sig in _ai.Config.PoeCharacterSkills)
                {
                    var arrowSplit = sig.Split("->", 2);
                    string mainSkill = arrowSplit[0].Trim();
                    var sups = arrowSplit.Length == 2
                        ? arrowSplit[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                        : [];
                    OverviewCardsPanel.Children.Add(CreateSkillLinkCard(mainSkill, sups, curLvl));
                }
            }

            if (_ai.Config.WornGearBySlot.Count > 0)
            {
                AddSectionHeader(OverviewCardsPanel, $"{Loc.T("Вдягнуті предмети акаунта", "Equipped Account Gear", "Надетая экипировка")} ({_ai.Config.WornGearBySlot.Count})", topMargin: 10);
                foreach (var kv in _ai.Config.WornGearBySlot)
                {
                    string title = kv.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
                        .FirstOrDefault()?.Trim() ?? kv.Key;
                    OverviewCardsPanel.Children.Add(CreateGearTargetCard(kv.Key, PoeTermLocalizer.SlotName(kv.Key), title, kv.Value));
                }
            }

            string inferredWep = PoeAccountService.InferCharacterWeaponClass(_ai.Config.WornGearBySlot);
            string stageAdvice = PoeAccountService.BuildStageAdvice(
                curLvl,
                _ai.Config.PoeCharacterClass,
                _ai.Config.TradeLeague,
                inferredWep,
                _ai.Config.PoeCharacterSkills,
                _ai.Config.WornGearBySlot);
            AddSectionHeader(OverviewCardsPanel, Loc.T("Аналіз та поради по етапу", "Stage Advice & Diagnostics", "Советы по этапу"), topMargin: 10);
            OverviewCardsPanel.Children.Add(CreateSimpleRowCard(Loc.T("Поради під твій поточний рівень і екіп", "Advice for your current level & gear", "Советы под твой уровень и экип"), stageAdvice));
        }
        else
        {
            string shortBadge = prof.Name.Length > 24 ? prof.Name[..23] + "…" : prof.Name;
            HeaderBuildBadge.Text = shortBadge;
            OverviewBuildTitle.Text = prof.Name;

            string asc = string.IsNullOrWhiteSpace(prof.Ascendancy) ? prof.ClassName : prof.Ascendancy;
            string def = PoeTermLocalizer.DefenseLabel(prof.DefenseFocus);
            string wep = PoeTermLocalizer.WeaponClassLabel(prof.PreferredWeaponClass);
            string ciTag = prof.IsCi ? " · CI" : "";
            OverviewBuildMeta.Text = $"{PoeTermLocalizer.BuildSourceLabel(prof.SourceKind)} · {(string.IsNullOrEmpty(asc) ? "PoE 2" : asc)} · {def} · {wep}{ciTag}";

            BuildStageEntry? activeStage = null;
            if (prof.Stages.Count > 0)
            {
                _overviewStageIdx = Math.Clamp(_overviewStageIdx, 0, prof.Stages.Count - 1);
                activeStage = prof.Stages[_overviewStageIdx];
                OverviewStageNav.Visibility = prof.Stages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                OverviewStageLabel.Text = ShortStageTitle(activeStage.Name, 22);
            }
            else
            {
                OverviewStageNav.Visibility = Visibility.Collapsed;
            }

            // Отримуємо живі скіли та шмотки для поточного етапу
            LocalBuildPlan? livePlan = null;
            if (activeStage != null && !string.IsNullOrWhiteSpace(activeStage.FilePath) && File.Exists(activeStage.FilePath))
                livePlan = BuildPlannerService.TryParseFile(activeStage.FilePath);

            if (curLvl > 0 && !string.IsNullOrWhiteSpace(_ai.Config.PoeCharacterName))
            {
                string accSummary = FormatAccountStatsSummary();
                OverviewCardsPanel.Children.Add(CreateSimpleRowCard(
                    $"{Loc.T("PoE 2 Акаунт", "PoE 2 Account", "PoE 2 Аккаунт")}: {_ai.Config.PoeCharacterName} · {Loc.LevelShort} {_ai.Config.PoeCharacterLevel} {_ai.Config.PoeCharacterClass} ({_ai.Config.TradeLeague})",
                    !string.IsNullOrWhiteSpace(accSummary)
                        ? accSummary
                        : $"{_ai.Config.WornGearBySlot.Count} {Loc.T("слотів", "slots", "слотов")}"));
            }

            if (livePlan != null)
            {
                var activeSkills = livePlan.Skills.Where(s => curLvl >= s.MinLevel && curLvl <= s.MaxLevel).ToList();
                if (activeSkills.Count == 0) activeSkills = livePlan.Skills;

                AddSectionHeader(OverviewCardsPanel, $"{Loc.LblActiveSkills} ({activeSkills.Count})");
                foreach (var sk in activeSkills)
                    OverviewCardsPanel.Children.Add(CreateSkillLinkCard(sk.SkillName, sk.Supports, sk.MinLevel));

                var upcomingSkills = livePlan.Skills.Where(s => s.MinLevel > curLvl).OrderBy(s => s.MinLevel).Take(4).ToList();
                if (upcomingSkills.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.LblUnlocksNext, topMargin: 10);
                    foreach (var sk in upcomingSkills)
                        OverviewCardsPanel.Children.Add(CreateSimpleRowCard($"{Loc.LevelShort} {sk.MinLevel}: {sk.SkillName}", string.Join(", ", sk.Supports)));
                }

                AddSectionHeader(OverviewCardsPanel, $"{Loc.LblGearTargets} ({livePlan.Gear.Count})", topMargin: 10);
                foreach (var g in livePlan.Gear)
                    OverviewCardsPanel.Children.Add(CreateGearTargetCard(g.RawSlotId, PoeTermLocalizer.SlotName(g.RawSlotId), PoeTermLocalizer.TranslateGearDescription(g.Text), g.Text));
            }
            else if (prof.SourceKind == BuildSourceKind.Pob2Import && !string.IsNullOrWhiteSpace(prof.Pob2Code))
            {
                _cachedActivePob ??= Pob2Parser.Parse(prof.Pob2Code);
                if (_cachedActivePob.Success)
                {
                    AddSectionHeader(OverviewCardsPanel, $"{Loc.LblActiveSkills} ({_cachedActivePob.SkillGroups.Count})");
                    foreach (var g in _cachedActivePob.SkillGroups)
                        OverviewCardsPanel.Children.Add(CreateSkillLinkCard(g.MainSkill, g.Supports, 1));

                    AddSectionHeader(OverviewCardsPanel, Loc.LblGearTargets, topMargin: 10);
                    foreach (var itemLine in _cachedActivePob.EquippedItems)
                    {
                        var parts = itemLine.Split(':', 2);
                        string rawSlot = parts.Length == 2 ? parts[0].Trim() : "Item";
                        string slot = parts.Length == 2 ? PoeTermLocalizer.SlotName(rawSlot) : "Item";
                        string desc = parts.Length == 2 ? parts[1].Trim() : itemLine;
                        string fullRaw = _cachedActivePob.RawEquippedBySlot.TryGetValue(rawSlot, out var rawItem) ? rawItem : desc;
                        OverviewCardsPanel.Children.Add(CreateGearTargetCard(rawSlot, slot, desc, fullRaw));
                    }

                    if (_cachedActivePob.Recommendations.Count > 0)
                    {
                        AddSectionHeader(OverviewCardsPanel, Loc.T("Примітки по PoB2", "PoB2 Notes", "Заметки PoB2"), topMargin: 10);
                        foreach (var r in _cachedActivePob.Recommendations)
                            OverviewCardsPanel.Children.Add(CreateSimpleRowCard(r, ""));
                    }
                }
            }
            else if (activeStage != null && (activeStage.SkillSignatures.Count > 0 || activeStage.GearSignatures.Count > 0))
            {
                if (activeStage.SkillSignatures.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, $"{Loc.LblActiveSkills} ({activeStage.SkillSignatures.Count})");
                    foreach (var sig in activeStage.SkillSignatures)
                    {
                        var arrowSplit = sig.Split("->", 2);
                        string mainSkill = arrowSplit[0].Trim();
                        var sups = arrowSplit.Length == 2
                            ? arrowSplit[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                            : [];
                        OverviewCardsPanel.Children.Add(CreateSkillLinkCard(mainSkill, sups, activeStage.MinLevel));
                    }
                }

                if (activeStage.GearSignatures.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, $"{Loc.LblGearTargets} ({activeStage.GearSignatures.Count})", topMargin: 10);
                    foreach (var gh in activeStage.GearSignatures)
                    {
                        var parts = gh.Split(':', 2);
                        string rawSlot = parts.Length == 2 ? parts[0].Trim() : "Item";
                        string desc = parts.Length == 2 ? parts[1].Trim() : gh;
                        string fullRaw = activeStage.GearDetailsBySlot.TryGetValue(rawSlot, out var rawItem)
                            ? rawItem
                            : _ai.Config.WornGearBySlot.TryGetValue(rawSlot, out var wornRaw) ? wornRaw : desc;
                        OverviewCardsPanel.Children.Add(CreateGearTargetCard(rawSlot, PoeTermLocalizer.SlotName(rawSlot), PoeTermLocalizer.TranslateGearDescription(desc), fullRaw));
                    }
                }

                string stageNote = !string.IsNullOrWhiteSpace(activeStage.Notes) ? activeStage.Notes : prof.SummaryNotes;
                if (string.IsNullOrWhiteSpace(stageNote))
                {
                    stageNote = PoeAccountService.BuildStageAdvice(
                        curLvl,
                        prof.ClassName,
                        _ai.Config.TradeLeague,
                        prof.PreferredWeaponClass,
                        activeStage.SkillSignatures,
                        _ai.Config.WornGearBySlot.Count > 0 ? _ai.Config.WornGearBySlot : activeStage.GearDetailsBySlot);
                }
                if (!string.IsNullOrWhiteSpace(stageNote))
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.T("Опис / Нотатки варіанта", "Variant Notes", "Заметки варианта"), topMargin: 10);
                    OverviewCardsPanel.Children.Add(CreateSimpleRowCard(Loc.T("Поради по етапу", "Stage Guide Notes", "Советы по этапу"), stageNote));
                }
            }
            else
            {
                if (prof.TargetSkills.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.LblActiveSkills);
                    foreach (var sk in prof.TargetSkills)
                        OverviewCardsPanel.Children.Add(CreateSkillLinkCard(sk, [], 1));
                }
                if (prof.TargetGearHints.Count > 0)
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.LblGearTargets, topMargin: 10);
                    foreach (var gh in prof.TargetGearHints)
                    {
                        var parts = gh.Split(':', 2);
                        string rawSlot = parts.Length == 2 ? parts[0].Trim() : "Item";
                        string desc = parts.Length == 2 ? parts[1].Trim() : gh;
                        OverviewCardsPanel.Children.Add(CreateGearTargetCard(rawSlot, PoeTermLocalizer.SlotName(rawSlot), PoeTermLocalizer.StatLine(desc), desc));
                    }
                }
                if (!string.IsNullOrWhiteSpace(prof.SummaryNotes))
                {
                    AddSectionHeader(OverviewCardsPanel, Loc.T("Опис / Нотатки", "Notes", "Заметки"), topMargin: 10);
                    OverviewCardsPanel.Children.Add(CreateSimpleRowCard(prof.SummaryNotes, ""));
                }
            }
        }

        // Mobalytics-подібний чекліст постійних нагород кампанії PoE 2 (+Spirit, +Пасивки, +Резисти)
        AddSectionHeader(OverviewCardsPanel, Loc.T("Чекліст нагород кампанії (+Spirit / +Пасивки / +Резисти)", "Campaign Permanent Rewards", "Чеклист наград кампании"), topMargin: 12);
        var checklistWrap = new StackPanel();
        foreach (var ms in CampaignGuideDb.PermanentMilestones)
        {
            var chk = new CheckBox
            {
                Content = Loc.T(ms.TitleUk, ms.TitleEn, ms.TitleRu),
                IsChecked = _ai.Config.CompletedCampaignRewards.Contains(ms.Id),
                Style = (Style)FindResource("DarkCheck"),
                FontSize = 10.5,
                Margin = new Thickness(0, 1.5, 0, 2.5)
            };
            string milestoneId = ms.Id;
            chk.Checked += (_, _) => { _ai.Config.CompletedCampaignRewards.Add(milestoneId); _ai.SaveConfig(); };
            chk.Unchecked += (_, _) => { _ai.Config.CompletedCampaignRewards.Remove(milestoneId); _ai.SaveConfig(); };
            checklistWrap.Children.Add(chk);
        }
        OverviewCardsPanel.Children.Add(new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = UiBrushes.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(9, 7, 9, 7),
            Child = checklistWrap
        });

        if (_overviewSubtab == 1) RefreshGemsSubtab();
        else if (_overviewSubtab == 2) RefreshGearObtainSubtab();
    }

    private void RefreshGemsSubtab()
    {
        GemsCatalogPanel.Children.Clear();
        int curLvl = EffectiveLevel;
        string q = (GemSearchBox.Text ?? "").Trim();

        if (_gemFilterMode == "build")
        {
            var buildGemNames = CollectActiveStageGemPairs();
            if (buildGemNames.Count == 0)
            {
                GemsCatalogPanel.Children.Add(CreateSimpleRowCard(
                    Loc.T("У активному білді поки немає каменів", "No gems in active build yet", "В активном билде пока нет камней"),
                    Loc.T("Перемкнися на «Каталог PoE 2» вище або імпортуй білд (.build / PoB2 / Mobalytics).", "Switch to 'PoE 2 Catalog' above or import a build.", "Переключись на «Каталог PoE 2» выше или импортируй билд.")));
                return;
            }

            foreach (var (rawGem, minStageLvl) in buildGemNames)
            {
                var info = Poe2GemsDb.ResolveGem(rawGem, minStageLvl);
                if (!string.IsNullOrEmpty(q) &&
                    !info.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !info.Tags.Contains(q, StringComparison.OrdinalIgnoreCase))
                    continue;
                GemsCatalogPanel.Children.Add(CreateGemDetailCard(info, curLvl));
            }
            return;
        }

        GemCategory? cat = _gemFilterMode switch
        {
            "active" => GemCategory.ActiveSkill,
            "support" => GemCategory.Support,
            "spirit" => GemCategory.SpiritBuff,
            _ => null
        };
        var items = Poe2GemsDb.GetAllCatalogEntries(q, cat);
        if (_gemFilterMode == "spirit")
            items = items.Concat(Poe2GemsDb.GetAllCatalogEntries(q, GemCategory.MetaTrigger)).DistinctBy(x => x.Name).OrderBy(x => x.MinUnlockCharacterLevel).ToList();

        foreach (var g in items)
            GemsCatalogPanel.Children.Add(CreateGemDetailCard(g, curLvl));
    }

    private List<(string gemName, int minLvl)> CollectActiveStageGemPairs()
    {
        var list = new List<(string, int)>();
        var prof = _profiles.ActiveProfile;
        if (prof == null)
        {
            foreach (var sig in _ai.Config.PoeCharacterSkills)
            {
                var p = sig.Split("->", 2);
                list.Add((p[0].Trim(), Math.Max(1, _ai.Config.PoeCharacterLevel)));
                if (p.Length == 2)
                    foreach (var sup in p[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        list.Add((sup, Math.Max(1, _ai.Config.PoeCharacterLevel)));
            }
            return list.DistinctBy(x => x.Item1.ToLowerInvariant()).ToList();
        }

        if (prof.Stages.Count > 0 && _overviewStageIdx < prof.Stages.Count)
        {
            var st = prof.Stages[_overviewStageIdx];
            if (!string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath))
            {
                var plan = BuildPlannerService.TryParseFile(st.FilePath);
                if (plan != null)
                {
                    foreach (var s in plan.Skills)
                    {
                        list.Add((s.SkillName, s.MinLevel));
                        foreach (var sup in s.Supports) list.Add((sup, s.MinLevel));
                    }
                    return list.DistinctBy(x => x.Item1.ToLowerInvariant()).ToList();
                }
            }
            foreach (var sig in st.SkillSignatures)
            {
                var p = sig.Split("->", 2);
                list.Add((p[0].Trim(), st.MinLevel));
                if (p.Length == 2)
                    foreach (var sup in p[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        list.Add((sup, st.MinLevel));
            }
        }
        else if (prof.SourceKind == BuildSourceKind.Pob2Import && !string.IsNullOrWhiteSpace(prof.Pob2Code))
        {
            _cachedActivePob ??= Pob2Parser.Parse(prof.Pob2Code);
            if (_cachedActivePob.Success)
            {
                foreach (var g in _cachedActivePob.SkillGroups)
                {
                    list.Add((g.MainSkill, 1));
                    foreach (var sup in g.Supports) list.Add((sup, 1));
                }
            }
        }
        return list.DistinctBy(x => x.Item1.ToLowerInvariant()).ToList();
    }

    private void RefreshGearObtainSubtab()
    {
        GearObtainPanel.Children.Clear();
        int curLvl = EffectiveLevel;

        // Поточні вдягнуті слоти користувача (основні + 2-й набір зброї + фласки/чарми, якщо синхронізовані з акаунта)
        AddSectionHeader(GearObtainPanel, Loc.T($"Твій поточний екіп (Вдягнуто зараз · Рів.{curLvl})", $"Your Currently Equipped Gear (Lvl {curLvl})", $"Твой текущий экип (Ур.{curLvl})"));
        var wornWrap = new StackPanel();
        string[] coreSlots = ["Weapon1", "Offhand1", "Weapon2", "Offhand2", "Helm1", "BodyArmour1", "Gloves1", "Boots1", "Amulet1", "Ring1", "Ring2", "Ring3", "Belt1", "Flask1", "Flask2", "Charm1", "Charm2", "Charm3"];
        foreach (string slotKey in coreSlots)
        {
            bool hasItem = _ai.Config.WornGearBySlot.TryGetValue(slotKey, out string? wornText) && !string.IsNullOrWhiteSpace(wornText);
            bool optionalSlot = slotKey is "Weapon2" or "Offhand2" or "Ring3" or "Flask1" or "Flask2" or "Charm1" or "Charm2" or "Charm3";
            if (!hasItem && optionalSlot) continue;

            string firstLine = hasItem
                ? (wornText!.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
                    .FirstOrDefault()?.Trim() ?? slotKey)
                : Loc.T("не задано (Ctrl+C -> Вдягнути / Синхр.)", "empty (Ctrl+C -> Equip / Account Sync)", "пусто");

            var row = new DockPanel { Margin = new Thickness(0, 1.5, 0, 1.5) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(btns, Dock.Right);

            string capturedSlot = slotKey;
            if (hasItem)
            {
                var btnInspect = new Button
                {
                    Content = Loc.T("Оглянути", "Inspect", "Осмотр"),
                    Style = (Style)FindResource("FlatButton"),
                    Foreground = UiBrushes.FgEmerald,
                    FontSize = 9,
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                btnInspect.Click += (_, _) => InspectEquippedSlotInItemsTab(capturedSlot);
                btns.Children.Add(btnInspect);
            }

            var btnPaste = new Button
            {
                Content = Loc.T("З буфера", "From Clip", "Из буфера"),
                Style = (Style)FindResource("FlatButton"),
                FontSize = 9,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(4, 0, 0, 0)
            };
            btnPaste.Click += (_, _) =>
            {
                try
                {
                    if (!Clipboard.ContainsText()) return;
                    string clip = Clipboard.GetText();
                    if (!clip.Contains("Item Class:")) return;
                    _ai.Config.WornGearBySlot[capturedSlot] = clip;
                    _ai.SaveConfig();
                    UpdateAccountStatusLabel();
                    RefreshGearObtainSubtab();
                    RefreshWornSlotsStrip();
                    if (_lastItemEval != null) CheckClipboard(force: true);
                }
                catch { }
            };
            btns.Children.Add(btnPaste);

            if (!string.IsNullOrWhiteSpace(wornText))
            {
                var btnClr = new Button
                {
                    Content = "×",
                    Style = (Style)FindResource("FlatButton"),
                    Foreground = UiBrushes.FgCrimson,
                    FontSize = 9,
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(3, 0, 0, 0)
                };
                btnClr.Click += (_, _) =>
                {
                    _ai.Config.WornGearBySlot.Remove(capturedSlot);
                    _ai.SaveConfig();
                    UpdateAccountStatusLabel();
                    RefreshGearObtainSubtab();
                    RefreshWornSlotsStrip();
                };
                btns.Children.Add(btnClr);
            }

            row.Children.Add(btns);
            var rowTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            rowTextStack.Children.Add(new TextBlock
            {
                Text = $"{PoeTermLocalizer.SlotName(slotKey)}: {firstLine}",
                Foreground = !string.IsNullOrWhiteSpace(wornText) ? UiBrushes.FgPrimary : UiBrushes.FgMuted,
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (hasItem)
            {
                string compact = PoeAccountService.SummarizeItemStatsCompact(wornText!);
                if (!string.IsNullOrWhiteSpace(compact))
                {
                    rowTextStack.Children.Add(new TextBlock
                    {
                        Text = compact,
                        Foreground = UiBrushes.FgSteel,
                        FontSize = 9.3,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
            }
            row.Children.Add(rowTextStack);
            wornWrap.Children.Add(row);
        }
        GearObtainPanel.Children.Add(new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = UiBrushes.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(0, 0, 0, 10),
            Child = wornWrap
        });

        var prof = _profiles.ActiveProfile;
        var items = new List<(string slotId, string text)>();
        if (prof == null)
        {
            if (_ai.Config.WornGearBySlot.Count > 0)
            {
                items.AddRange(_ai.Config.WornGearBySlot.Select(kv => (kv.Key, kv.Value)));
            }
            else
            {
                GearObtainPanel.Children.Add(CreateSimpleRowCard(
                    Loc.T("Спочатку обери або імпортуй білд", "Select or import a build first", "Сначала выбери или импортируй билд"),
                    Loc.T("Тут покаже кожен слот екіпу з інструкцією, де його вибити, який потрібен ilvl та як оцінити вигоду.", "Shows every gear slot, boss/drop source, ilvl breakpoints, and craft tips.", "Покажет каждый слот экипа, откуда он падает и какой нужен ilvl.")));
                return;
            }
        }
        else if (prof.Stages.Count > 0 && _overviewStageIdx < prof.Stages.Count)
        {
            var st = prof.Stages[_overviewStageIdx];
            if (!string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath))
            {
                var plan = BuildPlannerService.TryParseFile(st.FilePath);
                if (plan != null) items.AddRange(plan.Gear.Select(g => (g.RawSlotId, g.Text)));
            }
            if (items.Count == 0 && st.GearDetailsBySlot.Count > 0)
                items.AddRange(st.GearDetailsBySlot.Select(kv => (kv.Key, kv.Value)));
            if (items.Count == 0)
            {
                foreach (var sig in st.GearSignatures)
                {
                    var p = sig.Split(':', 2);
                    items.Add((p.Length == 2 ? p[0].Trim() : "Item", p.Length == 2 ? p[1].Trim() : sig));
                }
            }
        }
        else if (prof != null && prof.TargetGearHints.Count > 0)
        {
            foreach (var h in prof.TargetGearHints)
            {
                var p = h.Split(':', 2);
                items.Add((p.Length == 2 ? p[0].Trim() : "Item", p.Length == 2 ? p[1].Trim() : h));
            }
        }

        if (items.Count == 0)
        {
            GearObtainPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Немає записаних слотів шмоток у цьому білді", "No gear entries recorded in this stage", "В этом этапе нет записей по вещам"),
                Loc.T("Імпортуй .build, PoB2 або скористайся Ctrl+C у грі у вкладці «Предмети» для аналізу дропу.", "Import .build/PoB2 or press Ctrl+C in-game on any item inside the Item Check tab.", "Импортируй .build/PoB2 или нажми Ctrl+C в игре во вкладке «Предметы».")));
            return;
        }

        AddSectionHeader(GearObtainPanel, Loc.T($"Де отримати / скрафтити під твій рівень (Рів.{curLvl})", $"Where to Obtain / Craft for Lvl {curLvl}", $"Где выбить / скрафтить (Ур.{curLvl})"));
        foreach (var (slotId, rawText) in items)
        {
            var obt = Poe2KnowledgeDb.ResolveItemObtain(rawText, slotId, prof, curLvl);
            string slotLabel = PoeTermLocalizer.SlotName(slotId);
            string src = Loc.T(obt.ObtainSourceUk, obt.ObtainSourceEn, obt.ObtainSourceRu);
            string tip = Loc.T(obt.StrategyTipUk, obt.StrategyTipEn, obt.StrategyTipRu);
            string firstLine = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
                .FirstOrDefault()?.Trim() ?? obt.ItemTitle;
            string compactStats = PoeAccountService.SummarizeItemStatsCompact(rawText);

            var card = new Border
            {
                Background = UiBrushes.BgCard,
                BorderBrush = UiBrushes.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var st = new StackPanel();
            var top = new DockPanel();
            var tradeBtn = new Button
            {
                Content = "Trade 2",
                Style = (Style)FindResource("FlatButton"),
                Foreground = UiBrushes.FgSteel,
                FontSize = 9.5,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(tradeBtn, Dock.Right);
            string searchQuery = firstLine.Length <= 42 ? firstLine : obt.ItemTitle;
            tradeBtn.Click += (_, _) =>
            {
                SaveTradeLeague();
                TradeLinkBuilder.OpenTradeByItemName(_ai.Config.TradeLeague, searchQuery, isUnique: obt.Category.Contains("Unique"));
            };
            top.Children.Add(tradeBtn);
            top.Children.Add(new TextBlock
            {
                Text = $"{slotLabel} — {firstLine}",
                Foreground = UiBrushes.FgBrass,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            st.Children.Add(top);
            if (!string.IsNullOrWhiteSpace(compactStats))
            {
                st.Children.Add(new TextBlock
                {
                    Text = compactStats,
                    Foreground = UiBrushes.FgSteel,
                    FontSize = 9.8,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            st.Children.Add(new TextBlock
            {
                Text = $"{obt.Category} · {obt.MinItemOrAreaLevel}",
                Foreground = UiBrushes.FgEmerald,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 3)
            });
            st.Children.Add(new TextBlock
            {
                Text = $"{Loc.T("Де отримати:", "Where to get:", "Где получить:")} {src}\n{Loc.T("Порада:", "Tip:", "Совет:")} {tip}",
                Foreground = UiBrushes.FgPrimary,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 15.5
            });
            card.Child = st;
            GearObtainPanel.Children.Add(card);
        }
    }

    private void ShowGemInspector(PoeGemInfo gem)
    {
        int curLvl = EffectiveLevel;
        bool unlocked = curLvl >= gem.MinUnlockCharacterLevel;
        string catLabel = gem.Category switch
        {
            GemCategory.Support => Loc.T("Підтримка", "Support", "Поддержка"),
            GemCategory.SpiritBuff => "Spirit Buff / Herald",
            GemCategory.MetaTrigger => "Meta Trigger",
            _ => Loc.T("Активне вміння", "Active Skill", "Активное умение")
        };
        InspectorTitleText.Text = $"{gem.Name}  ·  {catLabel}  ({gem.Attribute})";

        string statusIcon = unlocked
            ? Loc.T($"✅ Доступний зараз (потр. Рів.{gem.MinUnlockCharacterLevel})", $"✅ Unlocked (req Lvl {gem.MinUnlockCharacterLevel})", $"✅ Доступен сейчас (нужен Ур.{gem.MinUnlockCharacterLevel})")
            : Loc.T($"🔒 З Рів.{gem.MinUnlockCharacterLevel} персонажа", $"🔒 Unlocks at Lvl {gem.MinUnlockCharacterLevel}", $"🔒 С Ур.{gem.MinUnlockCharacterLevel} персонажа");

        string spiritPart = gem.SpiritCost > 0 ? $" · Spirit: {gem.SpiritCost}" : $" · Mana Mult: {gem.ManaMultiplierPct}%";
        string buildLvl = $"Gem Lv {gem.GemLevelInBuild}{(gem.QualityInBuild > 0 ? $" Q{gem.QualityInBuild}%" : "")}";
        InspectorMetaText.Text = $"{buildLvl} · {gem.UncutTier} · {statusIcon}{spiritPart}";
        InspectorMetaText.Foreground = unlocked ? UiBrushes.FgEmerald : UiBrushes.FgAmber;

        string where = Loc.T(gem.ObtainWhereUk, gem.ObtainWhereEn, gem.ObtainWhereRu);
        string desc = Loc.T(gem.DescriptionUk, gem.DescriptionEn, gem.DescriptionRu);
        string attrs = "";
        if (gem.RequiredAttrStr > 0 || gem.RequiredAttrDex > 0 || gem.RequiredAttrInt > 0)
            attrs = $"\n{Loc.T("Атрибути ~:", "Est. Attrs:", "Атрибуты ~:")} Str {gem.RequiredAttrStr} / Dex {gem.RequiredAttrDex} / Int {gem.RequiredAttrInt}";

        InspectorBodyText.Text = $"{desc}\n{Loc.T("Де отримати:", "Where to get:", "Где получить:")} {where}{attrs}";
        _inspectorAiPrompt = $"Explain PoE 2 gem '{gem.Name}' ({gem.UncutTier}, unlocks at char level {gem.MinUnlockCharacterLevel}) for my build: scaling, links, and practical tips.";
        OverviewInspectorCard.Visibility = Visibility.Visible;
    }

    private void ShowGearInspector(string rawSlotId, string slotTitle, string rawText)
    {
        var obt = Poe2KnowledgeDb.ResolveItemObtain(rawText, rawSlotId, _profiles.ActiveProfile, EffectiveLevel);
        InspectorTitleText.Text = $"{slotTitle} — {obt.ItemTitle}";
        InspectorMetaText.Text = $"{obt.Category} · {obt.MinItemOrAreaLevel}";
        InspectorMetaText.Foreground = UiBrushes.FgSteel;

        string src = Loc.T(obt.ObtainSourceUk, obt.ObtainSourceEn, obt.ObtainSourceRu);
        string tip = Loc.T(obt.StrategyTipUk, obt.StrategyTipEn, obt.StrategyTipRu);
        string compactStats = PoeAccountService.SummarizeItemStatsCompact(rawText);
        string modLines = rawText.Contains("--------")
            ? string.Join(" · ", rawText.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(l => !l.StartsWith("--------"))
                .Where(l => !l.StartsWith("--------") && !l.StartsWith("Item Level:") && !l.StartsWith("Requires"))
                .Take(6)
                .Select(PoeTermLocalizer.StatLine))
            : "";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(compactStats)) parts.Add($"{Loc.T("Стати:", "Stats:", "Статы:")} {compactStats}");
        if (!string.IsNullOrWhiteSpace(modLines) && modLines != compactStats) parts.Add(modLines);
        parts.Add($"{Loc.T("Де отримати:", "Where to get:", "Где получить:")} {src}");
        parts.Add($"{Loc.T("Порада / цінність:", "Tip / Value:", "Совет / ценность:")} {tip}");

        InspectorBodyText.Text = string.Join("\n", parts);
        _inspectorAiPrompt = $"Explain where to farm/craft '{obt.ItemTitle}' ({rawSlotId}) in PoE 2, key stats to look for, and when it is worth upgrading or selling: {rawText}";
        OverviewInspectorCard.Visibility = Visibility.Visible;
    }

    private void CloseInspector_Click(object sender, RoutedEventArgs e) => OverviewInspectorCard.Visibility = Visibility.Collapsed;

    private async void AskAiAboutInspector_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_inspectorAiPrompt)) return;
        SelectPage(NavBtnAi);
        await RunAiQueryAsync(_inspectorAiPrompt, includeScreenshot: false);
    }

    private Border CreateGemDetailCard(PoeGemInfo gem, int curLvl)
    {
        bool unlocked = curLvl >= gem.MinUnlockCharacterLevel;
        string where = Loc.T(gem.ObtainWhereUk, gem.ObtainWhereEn, gem.ObtainWhereRu);
        string desc = Loc.T(gem.DescriptionUk, gem.DescriptionEn, gem.DescriptionRu);
        string spiritStr = gem.SpiritCost > 0 ? $" · {gem.SpiritCost} Spirit" : "";

        var st = new StackPanel();
        var top = new DockPanel();
        top.Children.Add(new TextBlock
        {
            Text = $"{gem.Name}  ·  Lv {gem.GemLevelInBuild}{(gem.QualityInBuild > 0 ? $" Q{gem.QualityInBuild}%" : "")}  ·  {gem.Attribute}",
            Foreground = UiBrushes.FgBrass,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5
        });
        top.Children.Add(new TextBlock
        {
            Text = unlocked
                ? Loc.T($"✅ З Рів.{gem.MinUnlockCharacterLevel}", $"✅ Lvl {gem.MinUnlockCharacterLevel}+", $"✅ С Ур.{gem.MinUnlockCharacterLevel}")
                : Loc.T($"🔒 Рів.{gem.MinUnlockCharacterLevel}", $"🔒 Lvl {gem.MinUnlockCharacterLevel}", $"🔒 Ур.{gem.MinUnlockCharacterLevel}"),
            Foreground = unlocked ? UiBrushes.FgEmerald : UiBrushes.FgAmber,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        st.Children.Add(top);

        st.Children.Add(new TextBlock
        {
            Text = $"{gem.UncutTier}{spiritStr} · [{gem.Tags}]",
            Foreground = UiBrushes.FgSteel,
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 3)
        });
        st.Children.Add(new TextBlock
        {
            Text = $"{desc}\n{Loc.T("Отримати:", "Obtain:", "Получить:")} {where}",
            Foreground = UiBrushes.FgPrimary,
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 15
        });

        return new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = unlocked ? UiBrushes.BorderSubtle : UiBrushes.BorderBrass,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 5),
            Child = st
        };
    }

    private void OverviewPrevStage_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile is not { Stages.Count: > 0 }) return;
        _autoSyncLevel = false;
        _overviewStageIdx = Math.Max(0, _overviewStageIdx - 1);
        RefreshOverview();
    }

    private void OverviewNextStage_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile is not { Stages.Count: > 0 } p) return;
        _autoSyncLevel = false;
        _overviewStageIdx = Math.Min(p.Stages.Count - 1, _overviewStageIdx + 1);
        RefreshOverview();
    }

    // --- Інтерактивне Дерево Пасивок (як у Mobalytics) ---
    private void RefreshPassiveTreeTab(bool resetSteps, bool preserveViewport = false)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null || (prof.Stages.Count == 0 && _selectedPlannerBuild == null))
        {
            TreeStageTitle.Text = "—";
            TreeCanvas.SetTreeData(null);
            PassivesOrderPanel.Children.Clear();
            PassivesOrderPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Немає завантаженого дерева пасивок", "No passive tree loaded", "Нет загруженного дерева пассивок"),
                Loc.T("Додай .build файли, Mobalytics або PoB2 білд, щоб побачити 2D карту й порядок прокачки.", "Import .build files, Mobalytics, or a PoB2 build to inspect progression.", "Импортируй .build, Mobalytics или PoB2 билд, чтобы увидеть карту и порядок прокачки.")));
            return;
        }

        var (title, curPassives, prevPassives, activeClass) = ResolveStagePassives(prof);

        TreeStageTitle.Text = ShortStageTitle(title, 18);

        _suppressTreeSliderEvent = true;
        TreeStepSlider.Minimum = 0;
        TreeStepSlider.Maximum = Math.Max(1, curPassives.Count);
        if (resetSteps || TreeStepSlider.Value > TreeStepSlider.Maximum || TreeStepSlider.Value == 0)
            TreeStepSlider.Value = curPassives.Count;
        _suppressTreeSliderEvent = false;

        RenderPassiveDiffAndList(title, curPassives, prevPassives, preserveViewport, activeClass);
    }

    private (string title, List<LocalBuildPassive> cur, List<LocalBuildPassive>? prev, string? activeClass) ResolveStagePassives(BuildProfile? prof)
    {
        List<LocalBuildPassive> curPassives = [];
        List<LocalBuildPassive>? prevPassives = null;
        string title = prof?.Name ?? "";
        string? activeClass = prof == null ? _ai.Config.PoeCharacterClass
            : !string.IsNullOrWhiteSpace(prof.Ascendancy) ? prof.Ascendancy : prof.ClassName;

        if (prof != null && prof.Stages.Count > 0)
        {
            _treeStageIdx = Math.Clamp(_treeStageIdx, 0, prof.Stages.Count - 1);
            var st = prof.Stages[_treeStageIdx];
            title = st.Name;
            curPassives = !string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath)
                ? (BuildPlannerService.TryParseFile(st.FilePath)?.Passives ?? [])
                : st.PassiveIds.Select(ParseStoredPassive).ToList();

            if (ChkComparePrevStage.IsChecked == true && _treeStageIdx > 0)
            {
                var prevSt = prof.Stages[_treeStageIdx - 1];
                prevPassives = !string.IsNullOrWhiteSpace(prevSt.FilePath) && File.Exists(prevSt.FilePath)
                    ? BuildPlannerService.TryParseFile(prevSt.FilePath)?.Passives
                    : prevSt.PassiveIds.Select(ParseStoredPassive).ToList();
            }
        }
        else if (_selectedPlannerBuild != null)
        {
            curPassives = _selectedPlannerBuild.Passives;
            title = _selectedPlannerBuild.Name;
        }
        return (title, curPassives, prevPassives, activeClass);
    }

    private static LocalBuildPassive ParseStoredPassive(string raw)
    {
        int bar = raw.IndexOf('|');
        if (bar > 0 && int.TryParse(raw[(bar + 1)..], out int ws))
            return new LocalBuildPassive(raw[..bar], 1, 100, ws, "");
        return new LocalBuildPassive(raw, 1, 100, 0, "");
    }

    private void RenderPassiveDiffAndList(string title, IReadOnlyList<LocalBuildPassive> curPassives, IReadOnlyList<LocalBuildPassive>? prevPassives, bool preserveViewport, string? activeClassName = null)
    {
        int stepLimit = Math.Clamp((int)TreeStepSlider.Value, 0, curPassives.Count);
        TreeStepLabel.Text = $"{stepLimit} / {curPassives.Count}";

        var diff = PassiveTreeService.AnalyzeBuildPassives(title, curPassives, prevPassives, stepLimit);
        TreeCanvas.SetTreeData(diff, preserveViewport: preserveViewport, activeClassName: activeClassName);

        if (!_treeListOpen) return;

        PassivesOrderPanel.Children.Clear();
        bool onlyKey = ChkOnlyKeyPassives.IsChecked == true;
        string search = (PassiveSearchBox.Text ?? "").Trim().ToLowerInvariant();

        var filtered = diff.Entries.Where(e =>
        {
            if (onlyKey && e.Status != PassiveDiffStatus.Removed)
            {
                if (e.Node == null) return false;
                if (e.Node.Kind is not (PassiveNodeKind.Keystone or PassiveNodeKind.Notable or PassiveNodeKind.JewelSocket or PassiveNodeKind.AscendancyNotable))
                    return false;
            }
            if (!string.IsNullOrEmpty(search))
            {
                string nodeName = (e.Node?.Name ?? e.RawId).ToLowerInvariant();
                string stats = string.Join(" ", e.Node?.Stats ?? []).ToLowerInvariant();
                if (!nodeName.Contains(search) && !stats.Contains(search))
                    return false;
            }
            return true;
        }).ToList();

        if (filtered.Count == 0)
        {
            PassivesOrderPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Вузлів за фільтром не знайдено", "No matching nodes", "Узлы по фильтру не найдены"),
                Loc.T("Спробуй зняти фільтр Keystones/Notables.", "Try unchecking Keystones/Notables filter.", "Попробуй отключить фильтр Keystones/Notables.")));
            return;
        }

        foreach (var item in filtered)
        {
            string name = item.Node != null && !string.IsNullOrWhiteSpace(item.Node.Name)
                ? item.Node.Name
                : item.RawId;
            string kind = item.Node != null ? PoeTermLocalizer.NodeKindLabel(item.Node.Kind) : "Attribute / Node";
            string statusTag = item.Status switch
            {
                PassiveDiffStatus.Added => item.StepOrder > 0 ? $"#{item.StepOrder} +" : "+",
                PassiveDiffStatus.Removed => Loc.T("Скинути", "Respec", "Сброс"),
                _ => $"#{item.StepOrder}"
            };

            string stats = item.Node != null && item.Node.Stats.Length > 0
                ? string.Join("\n", item.Node.Stats.Select(PoeTermLocalizer.StatLine))
                : item.Note;

            var baseBrush = item.Status switch
            {
                PassiveDiffStatus.Added => UiBrushes.BorderEmerald,
                PassiveDiffStatus.Removed => UiBrushes.BorderCrimson,
                _ => (item.Node?.Kind == PassiveNodeKind.Keystone ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle)
            };

            var card = new Border
            {
                Background = UiBrushes.BgWindow,
                BorderBrush = baseBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 5),
                Cursor = Cursors.Hand
            };

            string focusTarget = item.Node?.NumericId ?? item.RawId;
            card.MouseEnter += (_, _) => card.BorderBrush = UiBrushes.BorderBrass;
            card.MouseLeave += (_, _) => card.BorderBrush = baseBrush;
            card.MouseLeftButtonUp += (_, _) => TreeCanvas.FocusNode(focusTarget, zoomIn: true);

            var stack = new StackPanel();
            var topRow = new DockPanel();
            var badge = new TextBlock
            {
                Text = statusTag,
                Foreground = item.Status switch
                {
                    PassiveDiffStatus.Added => UiBrushes.FgEmerald,
                    PassiveDiffStatus.Removed => UiBrushes.FgCrimson,
                    _ => UiBrushes.FgBrass
                },
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(badge);

            var titleTb = new TextBlock
            {
                Text = $"{name}  ·  {kind}",
                Foreground = UiBrushes.FgPrimary,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            topRow.Children.Add(titleTb);
            stack.Children.Add(topRow);

            if (!string.IsNullOrWhiteSpace(stats))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = stats,
                    Foreground = UiBrushes.FgMuted,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                    LineHeight = 14
                });
            }

            card.Child = stack;
            PassivesOrderPanel.Children.Add(card);
        }
    }

    private void TreePrevStage_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile is not { Stages.Count: > 0 }) return;
        _treeStageIdx = Math.Max(0, _treeStageIdx - 1);
        RefreshPassiveTreeTab(resetSteps: true, preserveViewport: false);
    }

    private void TreeNextStage_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.ActiveProfile is not { Stages.Count: > 0 } p) return;
        _treeStageIdx = Math.Min(p.Stages.Count - 1, _treeStageIdx + 1);
        RefreshPassiveTreeTab(resetSteps: true, preserveViewport: false);
    }

    private void TreeOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshPassiveTreeTab(resetSteps: false, preserveViewport: true);
    }

    private void PassiveSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || PagePassives.Visibility != Visibility.Visible) return;
        RefreshPassiveTreeTab(resetSteps: false, preserveViewport: true);
    }

    private void TreeStepSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressTreeSliderEvent || !IsLoaded || PagePassives.Visibility != Visibility.Visible) return;
        RefreshPassiveTreeTab(resetSteps: false, preserveViewport: true);
    }

    private void TreeStepDec_Click(object sender, RoutedEventArgs e)
    {
        if (TreeStepSlider.Value > TreeStepSlider.Minimum)
            TreeStepSlider.Value = Math.Max(TreeStepSlider.Minimum, TreeStepSlider.Value - 1);
    }

    private void TreeStepInc_Click(object sender, RoutedEventArgs e)
    {
        if (TreeStepSlider.Value < TreeStepSlider.Maximum)
            TreeStepSlider.Value = Math.Min(TreeStepSlider.Maximum, TreeStepSlider.Value + 1);
    }

    private void TreeZoomIn_Click(object sender, RoutedEventArgs e) => TreeCanvas.ZoomBy(1.3);
    private void TreeZoomOut_Click(object sender, RoutedEventArgs e) => TreeCanvas.ZoomBy(1.0 / 1.3);
    private void TreeFit_Click(object sender, RoutedEventArgs e) => TreeCanvas.FitToActive();

    private void ToggleTreeListDrawer_Click(object sender, RoutedEventArgs e)
    {
        _treeListOpen = !_treeListOpen;
        TreeListCol.Width = _treeListOpen ? new GridLength(205) : new GridLength(0);
        TreeListGap.Width = _treeListOpen ? new GridLength(6) : new GridLength(0);
        BtnToggleTreeList.Background = _treeListOpen ? UiBrushes.BgBrass : UiBrushes.BgCard;
        BtnToggleTreeList.Foreground = _treeListOpen ? UiBrushes.FgDark : UiBrushes.FgMuted;
        if (_treeListOpen) RefreshPassiveTreeTab(resetSteps: false, preserveViewport: true);
    }

    private void TreeSetAll_Click(object sender, RoutedEventArgs e) => ApplyTreeWeaponSet(null);
    private void TreeSet1_Click(object sender, RoutedEventArgs e) => ApplyTreeWeaponSet(1);
    private void TreeSet2_Click(object sender, RoutedEventArgs e) => ApplyTreeWeaponSet(2);

    private void ApplyTreeWeaponSet(int? setFilter)
    {
        _treeWeaponSetFilter = setFilter;
        TreeCanvas.SetWeaponSetFilter(setFilter);
        foreach (var (b, val) in new[] { (BtnSetAll, (int?)null), (BtnSet1, 1), (BtnSet2, 2) })
        {
            bool on = val == setFilter;
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
            b.BorderBrush = on ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
        }
    }

    private void PopoutTreeWindow_Click(object sender, RoutedEventArgs e)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null) return;
        var (title, cur, prev, activeClass) = ResolveStagePassives(prof);
        int stepLimit = Math.Clamp((int)TreeStepSlider.Value, 0, cur.Count);
        var diff = PassiveTreeService.AnalyzeBuildPassives(title, cur, prev, stepLimit);
        var pop = new PassiveTreeWindow(title, diff, _treeWeaponSetFilter, activeClass);
        pop.Show();
    }

    private async void TreeExplainAi_Click(object sender, RoutedEventArgs e)
    {
        var prof = _profiles.ActiveProfile;
        if (prof == null) return;
        SelectPage(NavBtnAi);
        await RunAiQueryAsync(Loc.T("Поясни пріоритет пасивок на поточному етапі цього білду та що брати далі.", "Explain passive tree priority for this build stage and what to allocate next.", "Объясни приоритет пассивок на этом этапе билда и что брать дальше."), includeScreenshot: false);
    }

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
        if (_profiles.DeleteProfile(_profiles.ActiveProfile.Id))
        {
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

    // --- Предмети (Ctrl+C) + Порівняння вдягнутого екіпу в оверлеї + Trade2 ---
    private void CheckClipboardNow_Click(object sender, RoutedEventArgs e)
    {
        _overrideCompareSlotKey = null;
        CheckClipboard(force: true);
    }

    private void CheckClipboard(bool force)
    {
        if (!force && ChkAutoClipboard.IsChecked != true) return;

        string text;
        try { if (!Clipboard.ContainsText()) return; text = Clipboard.GetText(); }
        catch { return; }

        if (!string.IsNullOrEmpty(_ignoreClipboardText) && text == _ignoreClipboardText)
        {
            _ignoreClipboardText = "";
            return;
        }
        if (!force && text == _lastClipboard) return;
        _lastClipboard = text;
        if (!force) _overrideCompareSlotKey = null;

        EvaluateAndDisplayItemText(text, force, isExternalDrop: true, fetchLivePrice: true);
    }

    private void InspectEquippedSlotInItemsTab(string slotKey)
    {
        if (!_ai.Config.WornGearBySlot.TryGetValue(slotKey, out string? wornRaw) || string.IsNullOrWhiteSpace(wornRaw))
            return;

        SelectPage(NavBtnItems);
        ShowItemsEvalSubtab_Click(this, new RoutedEventArgs());
        _overrideCompareSlotKey = slotKey;
        EvaluateAndDisplayItemText(wornRaw, force: true, isExternalDrop: false, fetchLivePrice: false);
    }

    private void InspectEquippedSlotOrRawInItemsTab(string rawSlotId, string rawText)
    {
        SelectPage(NavBtnItems);
        ShowItemsEvalSubtab_Click(this, new RoutedEventArgs());
        if (_ai.Config.WornGearBySlot.TryGetValue(rawSlotId, out string? wornRaw) &&
            !string.IsNullOrWhiteSpace(wornRaw) &&
            wornRaw.Trim() == rawText.Trim())
        {
            _overrideCompareSlotKey = rawSlotId;
            EvaluateAndDisplayItemText(wornRaw, force: true, isExternalDrop: false, fetchLivePrice: false);
            return;
        }
        _overrideCompareSlotKey = _ai.Config.WornGearBySlot.ContainsKey(rawSlotId) ? rawSlotId : null;
        EvaluateAndDisplayItemText(rawText, force: true, isExternalDrop: true, fetchLivePrice: false);
    }

    private void ToggleCompareWithLastExternal_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastExternalItemText)) return;
        EvaluateAndDisplayItemText(_lastExternalItemText, force: true, isExternalDrop: true, fetchLivePrice: false);
    }

    private void RefreshWornSlotsStrip()
    {
        if (WornSlotsStripPanel == null || WornGearQuickBox == null) return;
        WornSlotsStripPanel.Children.Clear();
        if (_ai.Config.WornGearBySlot.Count == 0)
        {
            WornGearQuickBox.Visibility = Visibility.Collapsed;
            return;
        }

        string[] orderedSlots = ["Weapon1", "Offhand1", "Weapon2", "Offhand2", "Helm1", "BodyArmour1", "Gloves1", "Boots1", "Amulet1", "Ring1", "Ring2", "Ring3", "Belt1", "Flask1", "Flask2", "Charm1", "Charm2", "Charm3"];
        foreach (string slot in orderedSlots.Concat(_ai.Config.WornGearBySlot.Keys).Distinct())
        {
            if (!_ai.Config.WornGearBySlot.TryGetValue(slot, out string? raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            string title = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
                .FirstOrDefault()?.Trim() ?? slot;
            if (title.Length > 22) title = title[..21] + "…";
            string statsTip = PoeAccountService.SummarizeItemStatsCompact(raw);
            bool selected = string.Equals(_overrideCompareSlotKey, slot, StringComparison.OrdinalIgnoreCase) ||
                            (_lastItemEval != null && _lastItemEval.RawCopiedText.Trim() == raw.Trim());

            string capturedSlot = slot;
            var btn = new Button
            {
                Content = $"{PoeTermLocalizer.SlotName(slot)}: {title}",
                ToolTip = string.IsNullOrWhiteSpace(statsTip) ? title : $"{PoeTermLocalizer.SlotName(slot)} — {title}\n{statsTip}",
                Style = (Style)FindResource("FlatButton"),
                Background = selected ? UiBrushes.BgEmeraldDim : UiBrushes.BgCard,
                BorderBrush = selected ? UiBrushes.BorderEmerald : UiBrushes.BorderSubtle,
                Foreground = selected ? UiBrushes.FgEmerald : UiBrushes.FgPrimary,
                FontSize = 9.3,
                Padding = new Thickness(7, 2.5, 7, 2.5),
                Margin = new Thickness(0, 0, 4, 4)
            };
            btn.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(_lastExternalItemText) &&
                    _lastItemEval != null &&
                    !_ai.Config.WornGearBySlot.Values.Any(w => w.Trim() == _lastItemEval.RawCopiedText.Trim()))
                {
                    // Якщо вже скопійований інший предмет і клікаємо на слот — порівнюємо скопійований саме з цим слотом!
                    _overrideCompareSlotKey = capturedSlot;
                    EvaluateAndDisplayItemText(_lastExternalItemText, force: true, isExternalDrop: true, fetchLivePrice: false);
                }
                else
                {
                    InspectEquippedSlotInItemsTab(capturedSlot);
                }
            };
            WornSlotsStripPanel.Children.Add(btn);
        }
        WornGearQuickBox.Visibility = WornSlotsStripPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderSideBySideCompare(ItemEvaluation ev, bool inspectingWornItself)
    {
        if (SideBySideCompareBox == null) return;
        bool hasWornRef = !string.IsNullOrWhiteSpace(ev.MatchedWornText);
        bool sameAsWorn = hasWornRef && ev.MatchedWornText.Trim() == ev.RawCopiedText.Trim();

        if (!hasWornRef || sameAsWorn || inspectingWornItself)
        {
            if (inspectingWornItself && !string.IsNullOrWhiteSpace(_lastExternalItemText) &&
                _lastExternalItemText.Trim() != ev.RawCopiedText.Trim())
            {
                CompareNewTitle.Text = Loc.T("Оглядаєш вдягнутий слот", "Inspecting Equipped Slot", "Осмотр надетого слота") + $": {ev.ItemName}";
                CompareNewBody.Text = PoeAccountService.SummarizeItemStatsCompact(ev.RawCopiedText)
                    is { Length: > 0 } cs ? cs : ev.Summary;
                CompareWornTitle.Text = Loc.T("Останній Ctrl+C / Буфер", "Last Ctrl+C Item", "Последний Ctrl+C");
                CompareWornBody.Text = PoeAccountService.SummarizeItemStatsCompact(_lastExternalItemText)
                    is { Length: > 0 } es ? es : _lastExternalItemText.Split('\n').FirstOrDefault()?.Trim() ?? "";
                BtnToggleCompareAgainstLast.Visibility = Visibility.Visible;
                SideBySideCompareBox.Visibility = Visibility.Visible;
                return;
            }
            BtnToggleCompareAgainstLast.Visibility = Visibility.Collapsed;
            SideBySideCompareBox.Visibility = Visibility.Collapsed;
            return;
        }

        BtnToggleCompareAgainstLast.Visibility = Visibility.Collapsed;
        string slotLabel = PoeTermLocalizer.SlotName(_overrideCompareSlotKey ?? ev.MatchedBuildSlot);
        string wornTitle = ev.MatchedWornText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(l => l.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("--------"))
            .FirstOrDefault()?.Trim() ?? slotLabel;

        static string ExtractCompareLines(string raw)
        {
            var mods = raw.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .SkipWhile(l => !l.StartsWith("--------"))
                .Where(l => !l.StartsWith("--------") && !l.StartsWith("Item Level:") && !l.StartsWith("Requires") && !l.StartsWith("Sockets:"))
                .Take(7)
                .Select(PoeTermLocalizer.StatLine)
                .ToList();
            string stats = PoeAccountService.SummarizeItemStatsCompact(raw);
            if (!string.IsNullOrWhiteSpace(stats) && mods.Count > 0)
                return $"{stats}\n{string.Join("\n", mods.Select(m => $"• {m}"))}";
            return mods.Count > 0 ? string.Join("\n", mods.Select(m => $"• {m}")) : stats;
        }

        CompareNewTitle.Text = $"{ev.UpgradeVerdict}\n{ev.ItemName}";
        CompareNewBody.Text = ExtractCompareLines(ev.RawCopiedText);
        CompareWornTitle.Text = $"{Loc.T("На тобі зараз", "Equipped now", "Надето сейчас")} ({slotLabel})\n{wornTitle}";
        CompareWornBody.Text = ExtractCompareLines(ev.MatchedWornText);
        SideBySideCompareBox.Visibility = Visibility.Visible;
    }

    private void EvaluateAndDisplayItemText(string text, bool force, bool isExternalDrop, bool fetchLivePrice)
    {
        IReadOnlyDictionary<string, string> compareMap = _ai.Config.WornGearBySlot;
        if (!string.IsNullOrWhiteSpace(_overrideCompareSlotKey) &&
            _ai.Config.WornGearBySlot.TryGetValue(_overrideCompareSlotKey, out string? forcedWorn))
        {
            var probe = ItemEvaluator.Evaluate(text, _profiles.ActiveProfile, _overviewStageIdx, null, EffectiveLevel, _ai.Config.CustomWaystoneModTags);
            string slotToMap = !string.IsNullOrWhiteSpace(probe.ItemClass) ? ItemEvaluator.MapItemClassToSlotKey(probe.ItemClass) : _overrideCompareSlotKey;
            compareMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [slotToMap] = forcedWorn,
                [_overrideCompareSlotKey] = forcedWorn
            };
        }

        var ev = ItemEvaluator.Evaluate(text, _profiles.ActiveProfile, _overviewStageIdx, compareMap, EffectiveLevel, _ai.Config.CustomWaystoneModTags);
        if (!ev.IsPoeItem)
        {
            if (force)
            {
                ItemVerdictText.Text = Loc.T("У буфері не знайдено предмета PoE 2", "Clipboard does not contain a PoE 2 item", "В буфере не найден предмет PoE 2");
                ItemVerdictText.Foreground = UiBrushes.FgMuted;
                ItemUpgradeBadge.Visibility = Visibility.Collapsed;
                ItemPriceBadge.Visibility = Visibility.Collapsed;
                ItemProfitBadge.Visibility = Visibility.Collapsed;
                ItemAffixBadge.Visibility = Visibility.Collapsed;
                SideBySideCompareBox.Visibility = Visibility.Collapsed;
                WaystoneModsBox.Visibility = Visibility.Collapsed;
                ItemModFilterBox.Visibility = Visibility.Collapsed;
                LiveListingsBox.Visibility = Visibility.Collapsed;
                RefreshWornSlotsStrip();
            }
            return;
        }

        if (isExternalDrop) _lastExternalItemText = text;
        bool isWornItself = !isExternalDrop || _ai.Config.WornGearBySlot.Values.Any(v => v.Trim() == text.Trim());

        _lastItemEval = ev;
        BtnOpenWiki.IsEnabled = !string.IsNullOrWhiteSpace(ev.WikiUrl);
        RenderSelectableModCheckboxes(ev);
        RenderWaystoneModsPanel(ev);
        RenderLiveListings(ev);
        RenderSideBySideCompare(ev, inspectingWornItself: !isExternalDrop);
        RefreshWornSlotsStrip();

        Color col = ev.Verdict == ItemVerdict.Danger
            ? GetVerdictColor(ItemVerdict.Danger)
            : isWornItself
                ? Color.FromRgb(0x69, 0xAC, 0xEC)
                : ev.UpgradeDeltaPct >= 8
                    ? Color.FromRgb(0x4E, 0xCF, 0x8F)
                    : ev.UpgradeDeltaPct <= -8
                        ? Color.FromRgb(0xEA, 0x61, 0x61)
                        : GetVerdictColor(ev.Verdict);
        var brush = UiBrushes.ForColor(col);
        string slotDisplay = PoeTermLocalizer.SlotName(_overrideCompareSlotKey ?? ev.MatchedBuildSlot);
        string headlineVerdict = isWornItself
            ? Loc.T($"[ЕКІП] {slotDisplay}", $"[EQUIPPED] {slotDisplay}", $"[НАДЕТО] {slotDisplay}")
            : !string.IsNullOrWhiteSpace(ev.UpgradeVerdict) ? ev.UpgradeVerdict : Loc.Verdict(ev.Verdict);
        ItemVerdictText.Text = $"{headlineVerdict}  ·  {ev.ItemName}";
        ItemVerdictText.Foreground = brush;
        ItemVerdictBorder.BorderBrush = brush;

        if (!isWornItself && !string.IsNullOrWhiteSpace(ev.UpgradeVerdict))
        {
            ItemUpgradeBadgeText.Text = ev.UpgradeVerdict;
            ItemUpgradeBadgeText.Foreground = ev.UpgradeDeltaPct >= 8 ? UiBrushes.FgEmerald : ev.UpgradeDeltaPct <= -8 ? UiBrushes.FgCrimson : UiBrushes.FgSteel;
            ItemUpgradeBadge.Visibility = Visibility.Visible;
        }
        else ItemUpgradeBadge.Visibility = Visibility.Collapsed;

        ItemPriceBadge.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(ev.ProfitVerdict))
        {
            ItemProfitBadgeText.Text = ev.ProfitVerdict;
            ItemProfitBadge.Visibility = Visibility.Visible;
        }
        else ItemProfitBadge.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(ev.AffixSummary))
        {
            ItemAffixBadgeText.Text = ev.AffixSummary;
            ItemAffixBadge.Visibility = Visibility.Visible;
        }
        else ItemAffixBadge.Visibility = Visibility.Collapsed;

        string details = BuildFormattedItemDetails(ev);
        ItemDetailsText.Text = details;

        bool canSwitchTab = _ai.Config.FocusOverlayOnCtrlC || PageItems.Visibility == Visibility.Visible;
        if (!_collapsed && !_clickThrough && !_miniHudMode && !force && canSwitchTab)
            SelectPage(NavBtnItems);

        if (ChkToastPopup.IsChecked == true && !force)
            _toast.ShowToast($"{headlineVerdict} · {ev.ItemName}", details, col, nearCursor: true, seconds: ev.Verdict == ItemVerdict.Danger ? 7.5 : 5.0);

        if (ChkSoundAlert.IsChecked == true && _ai.Config.SoundAlertsEnabled && !force)
        {
            if (ev.Verdict == ItemVerdict.Danger) SystemSounds.Hand.Play();
            else if (ev.Verdict == ItemVerdict.Great || ev.UpgradeDeltaPct >= 10) SystemSounds.Asterisk.Play();
        }

        // Фоновий живий Trade 2 прайс-чек (не блокує миттєвий тост)
        if (fetchLivePrice) _ = FetchLivePriceAndRefreshUiAsync(ev);
    }

    private static string BuildFormattedItemDetails(ItemEvaluation ev)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(ev.UpgradeComparisonNote)) lines.Add(ev.UpgradeComparisonNote);
        if (!string.IsNullOrWhiteSpace(ev.BuildMatchVerdict) || !string.IsNullOrWhiteSpace(ev.BuildComparisonNote))
        {
            string buildLine = string.Join(" · ", new[] { ev.BuildMatchVerdict, ev.BuildComparisonNote }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(buildLine)) lines.Add(buildLine);
        }
        if (!string.IsNullOrWhiteSpace(ev.AffixSummary)) lines.Add(ev.AffixSummary);
        if (!string.IsNullOrWhiteSpace(ev.LivePriceSummary)) lines.Add(ev.LivePriceSummary);
        if (ev.LiveListings.Count > 0)
        {
            string topListings = string.Join(" · ", ev.LiveListings.Take(4).Select(l => $"{l.Amount:0.##} {l.Currency} ({l.AgeText})"));
            lines.Add($"{Loc.T("Лоти:", "Listings:", "Лоты:")} {topListings}");
        }
        if (!string.IsNullOrWhiteSpace(ev.ProfitVerdict)) lines.Add(ev.ProfitVerdict);
        lines.Add(ev.Summary);
        lines.Add("");
        if (ev.Warnings.Count > 0) { lines.Add(Loc.T("ПОПЕРЕДЖЕННЯ:", "WARNINGS:", "ПРЕДУПРЕЖДЕНИЯ:")); lines.AddRange(ev.Warnings.Select(w => $"• {w}")); lines.Add(""); }
        if (ev.Positives.Count > 0) { lines.Add(Loc.T("ПЛЮСИ:", "POSITIVES:", "ПЛЮСЫ:")); lines.AddRange(ev.Positives.Select(p => $"+ {p}")); lines.Add(""); }
        if (ev.Missing.Count > 0) { lines.Add(Loc.T("БАЖАНО ДОДАТИ:", "MISSING / UPGRADE:", "ЖЕЛАТЕЛЬНО ДОБАВИТЬ:")); lines.AddRange(ev.Missing.Select(m => $"• {m}")); lines.Add(""); }
        if (!string.IsNullOrWhiteSpace(ev.ObtainInfo))
        {
            lines.Add(Loc.T("ДЕ ОТРИМАТИ:", "WHERE TO OBTAIN:", "ГДЕ ПОЛУЧИТЬ:"));
            lines.Add(ev.ObtainInfo);
        }
        return string.Join("\n", lines).Trim();
    }

    private async Task FetchLivePriceAndRefreshUiAsync(ItemEvaluation ev)
    {
        string league = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Forbidden Rites" : _ai.Config.TradeLeague;
        string price = await PoeTradePriceService.EstimateLivePriceAsync(ev, league);
        if (string.IsNullOrWhiteSpace(price)) return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (_lastItemEval != ev) return;
            ev.LivePriceSummary = price;
            ItemPriceBadgeText.Text = price;
            ItemPriceBadge.Visibility = Visibility.Visible;
            RenderLiveListings(ev);
            string updated = BuildFormattedItemDetails(ev);
            ItemDetailsText.Text = updated;
            _toast.UpdateBodyIfVisible(updated);
        });
    }

    private void RenderWaystoneModsPanel(ItemEvaluation ev)
    {
        WaystoneModsPanel.Children.Clear();
        if (!ev.IsWaystone || ev.WaystoneMods.Count == 0)
        {
            WaystoneModsBox.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var wm in ev.WaystoneMods)
        {
            var (fg, bg, border, icon) = wm.Safety switch
            {
                MapModSafety.Deadly => (UiBrushes.FgCrimson, UiBrushes.BgCrimsonDim, UiBrushes.BorderCrimson, "✖"),
                MapModSafety.Caution => (UiBrushes.FgAmber, UiBrushes.BgBrassDim, UiBrushes.BorderBrass, "▲"),
                _ => (UiBrushes.FgEmerald, UiBrushes.BgEmeraldDim, UiBrushes.BorderEmerald, "✓")
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"{icon} {wm.ModText}",
                Foreground = fg,
                FontSize = 10,
                FontWeight = wm.Safety == MapModSafety.Deadly ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(wm.Reason))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = wm.Reason,
                    Foreground = UiBrushes.FgMuted,
                    FontSize = 9,
                    Margin = new Thickness(12, 1, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var btn = new Button
            {
                Content = stack,
                Background = bg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            btn.Click += (_, _) =>
            {
                WaystoneModChecker.CycleCustomTag(_ai.Config.CustomWaystoneModTags, wm.PatternKey, wm.Safety);
                _ai.SaveConfig();
                CheckClipboard(force: true);
            };
            WaystoneModsPanel.Children.Add(btn);
        }
        WaystoneModsBox.Visibility = Visibility.Visible;
    }

    private void RenderLiveListings(ItemEvaluation ev)
    {
        if (ev.LiveListings.Count == 0)
        {
            LiveListingsBox.Visibility = Visibility.Collapsed;
            return;
        }
        LiveListingsText.Text = string.Join("   ·   ",
            ev.LiveListings.Take(6).Select(l => $"{l.Amount:0.##} {l.Currency} ({l.AgeText})"));
        LiveListingsBox.Visibility = Visibility.Visible;
    }

    private void RenderSelectableModCheckboxes(ItemEvaluation ev)
    {
        ItemModChecksPanel.Children.Clear();
        _modFilterCheckboxes.Clear();
        if (ev.SelectableMods.Count == 0)
        {
            ItemModFilterBox.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var mod in ev.SelectableMods)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 2), LastChildFill = true };
            var minBox = new TextBox
            {
                Width = 42,
                Height = 20,
                Text = mod.MinSuggested > 0 ? mod.MinSuggested.ToString() : "",
                Background = UiBrushes.BgInput,
                Foreground = UiBrushes.FgPrimary,
                BorderBrush = UiBrushes.BorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1),
                FontSize = 9.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = Loc.T("Мін. значення (min) для Trade 2", "Minimum stat threshold for Trade 2", "Мин. порог (min) для Trade 2")
            };
            DockPanel.SetDock(minBox, Dock.Right);
            row.Children.Add(minBox);

            var chk = new CheckBox
            {
                Content = mod.DisplayText,
                IsChecked = mod.IsDefaultChecked,
                Style = (Style)FindResource("DarkCheck"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(chk);
            ItemModChecksPanel.Children.Add(row);
            _modFilterCheckboxes.Add((chk, minBox, mod));
        }
        ItemModFilterBox.Visibility = _itemsExtraExpanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateItemsExtraButtonUi();
    }

    private void ToggleItemsExtra_Click(object sender, RoutedEventArgs e)
    {
        _itemsExtraExpanded = !_itemsExtraExpanded;
        TradeShortcutsBox.Visibility = _itemsExtraExpanded ? Visibility.Visible : Visibility.Collapsed;
        ItemModFilterBox.Visibility = (_itemsExtraExpanded && _modFilterCheckboxes.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        UpdateItemsExtraButtonUi();
    }

    private void UpdateItemsExtraButtonUi()
    {
        if (BtnToggleItemsExtra == null) return;
        string arrow = _itemsExtraExpanded ? "▴" : "▾";
        string cnt = _modFilterCheckboxes.Count > 0 ? $" ({_modFilterCheckboxes.Count})" : "";
        BtnToggleItemsExtra.Content = Loc.T($"Trade / Моди{cnt} {arrow}", $"Trade / Mods{cnt} {arrow}", $"Trade / Моды{cnt} {arrow}");
        BtnToggleItemsExtra.Background = _itemsExtraExpanded ? UiBrushes.BgBrass : UiBrushes.BgCard;
        BtnToggleItemsExtra.Foreground = _itemsExtraExpanded ? UiBrushes.FgDark : UiBrushes.FgPrimary;
        BtnToggleItemsExtra.BorderBrush = _itemsExtraExpanded ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
    }

    private List<ItemModFilterOption> GetCheckedMods()
    {
        var selected = new List<ItemModFilterOption>();
        foreach (var (box, minBox, mod) in _modFilterCheckboxes)
        {
            if (box.IsChecked != true) continue;
            string rawMin = minBox.Text.Trim();
            if (int.TryParse(rawMin, out int parsedInt) && parsedInt >= 0)
                mod.MinSuggested = parsedInt;
            else if (double.TryParse(rawMin.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble) && parsedDouble >= 0)
                mod.MinSuggested = (int)Math.Floor(parsedDouble);
            else if (string.IsNullOrEmpty(rawMin))
                mod.MinSuggested = 0;
            selected.Add(mod);
        }
        return selected;
    }

    private async void RecheckPriceByMods_Click(object sender, RoutedEventArgs e)
    {
        if (_lastItemEval == null || !_lastItemEval.IsPoeItem) return;
        BtnRecheckPriceByMods.IsEnabled = false;
        try
        {
            var mods = GetCheckedMods();
            string league = string.IsNullOrWhiteSpace(_ai.Config.TradeLeague) ? "Standard" : _ai.Config.TradeLeague;
            string res = await PoeTradePriceService.EstimateCustomPriceAsync(_lastItemEval, league, mods);
            if (!string.IsNullOrWhiteSpace(res))
            {
                _lastItemEval.LivePriceSummary = res;
                ItemPriceBadgeText.Text = res;
                ItemPriceBadge.Visibility = Visibility.Visible;
                RenderLiveListings(_lastItemEval);
                ItemDetailsText.Text = BuildFormattedItemDetails(_lastItemEval);
            }
        }
        finally
        {
            BtnRecheckPriceByMods.IsEnabled = true;
        }
    }

    private void OpenTradeByMods_Click(object sender, RoutedEventArgs e)
    {
        if (_lastItemEval == null || !_lastItemEval.IsPoeItem) return;
        SaveTradeLeague();
        string q = PoeTradePriceService.BuildCustomTradeQueryJson(_lastItemEval, GetCheckedMods());
        TradeLinkBuilder.OpenTradeQuery(_ai.Config.TradeLeague, q);
    }

    private void OpenWikiLastItem_Click(object sender, RoutedEventArgs e)
    {
        string url = !string.IsNullOrWhiteSpace(_lastItemEval?.WikiUrl)
            ? _lastItemEval.WikiUrl
            : "https://poe2db.tw/us/";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenTradeHelper_Click(object sender, RoutedEventArgs e)
    {
        if (_tradeHelper.IsVisible) _tradeHelper.Hide();
        else
        {
            _tradeHelper.RefreshUi();
            _tradeHelper.Show();
            Win32OverlayHelper.EnsureTopmostNoActivate(_tradeHelper);
        }
    }

    private void OpenCommandWheelNearCursor()
    {
        string c1 = CustomCmd1Box?.Text ?? _ai.Config.CustomCmd1;
        string c2 = CustomCmd2Box?.Text ?? _ai.Config.CustomCmd2;
        _commandWheel.ToggleNearCursor(_watcher.LastWhisperPlayer, c1, c2);
    }

    private void ExecutePoeChatMacro(string command)
    {
        bool sentToGame = Win32OverlayHelper.SendPoeChatCommand(command, s => _ignoreClipboardText = s);
        if (!sentToGame)
        {
            _toast.ShowToast(
                Loc.T("Команда скопійована", "Command Copied", "Команда скопирована"),
                Loc.T($"PoE 2 не знайдено — встав у чат гри (Enter → Ctrl+V):\n{command}", $"PoE 2 window not active — paste into chat:\n{command}", $"Окно PoE 2 не активно — вставь в чат:\n{command}"),
                Color.FromRgb(0x69, 0xAC, 0xEC), nearCursor: false, seconds: 3.5);
        }
    }

    private void EquipCopiedItemToSlot_Click(object sender, RoutedEventArgs e)
    {
        if (_lastItemEval == null || !_lastItemEval.IsPoeItem || string.IsNullOrWhiteSpace(_lastItemEval.RawCopiedText))
            return;

        string slot = string.IsNullOrWhiteSpace(_lastItemEval.MatchedBuildSlot)
            ? ItemEvaluator.MapItemClassToSlotKey(_lastItemEval.ItemClass)
            : _lastItemEval.MatchedBuildSlot;
        if (slot == "Ring1" && _ai.Config.WornGearBySlot.ContainsKey("Ring1") && !_ai.Config.WornGearBySlot.ContainsKey("Ring2"))
            slot = "Ring2";
        _ai.Config.WornGearBySlot[slot] = _lastItemEval.RawCopiedText;
        _ai.SaveConfig();
        UpdateAccountStatusLabel();
        if (_overviewSubtab == 2) RefreshGearObtainSubtab();
        CheckClipboard(force: true);
        _toast.ShowToast(
            PoeTermLocalizer.SlotName(slot),
            Loc.T($"Збережено як твою поточну річ для порівнянь: {_lastItemEval.ItemName}", $"Saved as worn baseline: {_lastItemEval.ItemName}", $"Сохранено как твоя вещь: {_lastItemEval.ItemName}"),
            Color.FromRgb(0x4E, 0xCF, 0x8F), nearCursor: false, seconds: 3.5);
    }

    private void CopyVendorRegex_Click(object sender, RoutedEventArgs e)
    {
        string rx = RegexGeneratorService.BuildVendorRegex(_profiles.ActiveProfile);
        SetClipboardSafe(rx);
        _toast.ShowToast(Loc.T("Regex Торгівля", "Vendor Regex", "Regex Торговец"), rx, Color.FromRgb(0xD4, 0xB0, 0x6A), nearCursor: false, seconds: 3.5);
    }

    private void CopyWaystoneRegex_Click(object sender, RoutedEventArgs e)
    {
        string rx = RegexGeneratorService.BuildSafeWaystoneRegex(_profiles.ActiveProfile);
        SetClipboardSafe(rx);
        _toast.ShowToast(Loc.T("Regex Мап", "Safe Waystone Regex", "Regex Карт"), rx, Color.FromRgb(0x4E, 0xCF, 0x8F), nearCursor: false, seconds: 3.5);
    }

    private void SetClipboardSafe(string text)
    {
        try
        {
            _ignoreClipboardText = text;
            Clipboard.SetText(text);
        }
        catch { }
    }

    private void RefreshDynamicTradeButtons()
    {
        TradeButtonsPanel.Children.Clear();
        foreach (var sc in TradeLinkBuilder.BuildDynamicShortcuts(_profiles.ActiveProfile))
        {
            var shortcut = sc;
            var btn = new Button
            {
                Content = shortcut.Label,
                Style = (Style)FindResource("FlatButton"),
                Foreground = UiBrushes.FgBrass,
                FontSize = 10,
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 5, 5)
            };
            btn.Click += (_, _) =>
            {
                SaveTradeLeague();
                TradeLinkBuilder.OpenTradeQuery(_ai.Config.TradeLeague, shortcut.QueryJson);
            };
            TradeButtonsPanel.Children.Add(btn);
        }
    }

    private void TradeLastItem_Click(object sender, RoutedEventArgs e)
    {
        if (_lastItemEval == null || !_lastItemEval.IsPoeItem) return;
        SaveTradeLeague();
        string url = TradeLinkBuilder.BuildForCopiedItem(_ai.Config.TradeLeague, _lastItemEval);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void SaveTradeLeague()
    {
        if (!string.IsNullOrWhiteSpace(TradeLeagueBox.Text))
            ApplySelectedLeague(TradeLeagueBox.Text.Trim());
    }

    private static Color GetVerdictColor(ItemVerdict v) => v switch
    {
        ItemVerdict.Great => Color.FromRgb(0x4E, 0xCF, 0x8F),
        ItemVerdict.Good => Color.FromRgb(0x4E, 0xCF, 0x8F),
        ItemVerdict.Ok => Color.FromRgb(0xD4, 0xB0, 0x6A),
        ItemVerdict.Warn => Color.FromRgb(0xDF, 0xA7, 0x4A),
        ItemVerdict.Danger => Color.FromRgb(0xEA, 0x61, 0x61),
        _ => Color.FromRgb(0x8B, 0x93, 0xA5)
    };

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
            _toast.ShowToast(Loc.T("Вартість скрині", "Stash Net-Worth", "Стоимость тайника"), win.ResultMessage, Color.FromRgb(0x4E, 0xCF, 0x8F), nearCursor: false, seconds: 5.0);
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
                    report.HasChanges ? Color.FromRgb(0xDF, 0xA7, 0x4A) : Color.FromRgb(0x4E, 0xCF, 0x8F),
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
                    Color.FromRgb(0x75, 0xB7, 0x98), nearCursor: false, seconds: 6.0);
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
                _toast.ShowToast("AI", answer.Length > 340 ? answer[..340] + "…" : answer, Color.FromRgb(0xD4, 0xB0, 0x6A), nearCursor: false, seconds: 7.0);
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

    // --- Інструменти, Налаштування, Фільтр, Таймери ---
    private void InstallFilter_Click(object sender, RoutedEventArgs e) => InstallFilterCore();

    private void InstallFilterCore()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Обери свій .filter файл для PoE 2", "Select custom .filter file for PoE 2", "Выбери свой .filter файл для PoE 2"),
            Filter = "PoE 2 Loot Filter (*.filter)|*.filter|All (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            InstallAndPreviewFilter(dlg.FileName);
            SelectPage(NavBtnItems);
            ShowLootFilterSubtab();
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void InstallAndPreviewFilter(string sourcePath)
    {
        try
        {
            var (ok, dst, content) = LootFilterService.InstallExistingFilter(sourcePath);
            if (!ok) return;
            FilterNameBox.Text = Path.GetFileNameWithoutExtension(dst);
            FilterEditorBox.Text = content;
            var val = LootFilterService.ValidateFilter(content);
            string warnSuffix = val.Warnings.Count > 0 ? $" · ⚠ {string.Join("; ", val.Warnings)}" : " · OK";
            _filterStatusCustom = true;
            FilterStatusText.Text = Loc.T($"Встановлено: {Path.GetFileName(dst)} ({val.BlockCount} блоків{warnSuffix})", $"Installed: {Path.GetFileName(dst)} ({val.BlockCount} blocks{warnSuffix})", $"Установлен: {Path.GetFileName(dst)} ({val.BlockCount} блоков{warnSuffix})");
            FilterStatusText.Foreground = val.Warnings.Count > 0 ? UiBrushes.FgAmber : UiBrushes.FgEmerald;
            InstallFilterBtn.Content = Loc.T("✓ Встановлено!", "✓ Installed!", "✓ Установлено!");
            RefreshInstalledFiltersLabel();
        }
        catch
        {
            _filterStatusCustom = true;
            FilterStatusText.Text = Loc.T("Помилка завантаження .filter", "Error loading .filter", "Ошибка загрузки .filter");
            FilterStatusText.Foreground = UiBrushes.FgCrimson;
        }
    }

    private void ShowItemsEvalSubtab_Click(object sender, RoutedEventArgs e)
    {
        ItemsEvalPanel.Visibility = Visibility.Visible;
        LootFilterPanel.Visibility = Visibility.Collapsed;
        BtnSubItemsEval.Background = UiBrushes.BgBrass;
        BtnSubItemsEval.BorderBrush = UiBrushes.BgBrass;
        BtnSubItemsEval.Foreground = UiBrushes.FgDark;
        BtnSubLootFilter.Background = UiBrushes.BgCard;
        BtnSubLootFilter.BorderBrush = UiBrushes.BorderSubtle;
        BtnSubLootFilter.Foreground = UiBrushes.FgMuted;
    }

    private void ShowLootFilterSubtab_Click(object sender, RoutedEventArgs e) => ShowLootFilterSubtab();

    private void ShowLootFilterSubtab()
    {
        ItemsEvalPanel.Visibility = Visibility.Collapsed;
        LootFilterPanel.Visibility = Visibility.Visible;
        BtnSubLootFilter.Background = UiBrushes.BgBrass;
        BtnSubLootFilter.BorderBrush = UiBrushes.BgBrass;
        BtnSubLootFilter.Foreground = UiBrushes.FgDark;
        BtnSubItemsEval.Background = UiBrushes.BgCard;
        BtnSubItemsEval.BorderBrush = UiBrushes.BorderSubtle;
        BtnSubItemsEval.Foreground = UiBrushes.FgMuted;
        RefreshInstalledFiltersLabel();
        if (string.IsNullOrWhiteSpace(FilterEditorBox.Text) && _profiles.ActiveProfile != null)
            FilterNameBox.Text = string.Concat(_profiles.ActiveProfile.Name.Split([' ', '-', '/', '\\'], StringSplitOptions.RemoveEmptyEntries).Take(3));
    }

    private void RefreshInstalledFiltersLabel()
    {
        var files = LootFilterService.ListInstalledFilters().Select(Path.GetFileName).ToList();
        LblFilterInstalled.Text = files.Count == 0
            ? Loc.T("Фільтри в PoE 2: немає жодного .filter", "PoE 2 Filters: none installed", "Фильтры в PoE 2: нет ни одного .filter")
            : $"{Loc.T("В PoE 2:", "In PoE 2:", "В PoE 2:")} {string.Join(", ", files.Take(4))}{(files.Count > 4 ? $" (+{files.Count - 4})" : "")}";
    }

    private void SetFilterStrict1_Click(object sender, RoutedEventArgs e) => SetFilterStrictness(1);
    private void SetFilterStrict2_Click(object sender, RoutedEventArgs e) => SetFilterStrictness(2);
    private void SetFilterStrict3_Click(object sender, RoutedEventArgs e) => SetFilterStrictness(3);
    private void SetFilterStrict4_Click(object sender, RoutedEventArgs e) => SetFilterStrictness(4);

    private void SetFilterStrictness(int level)
    {
        _filterStrictness = level;
        var btns = new[] { (1, BtnStrict1), (2, BtnStrict2), (3, BtnStrict3), (4, BtnStrict4) };
        foreach (var (lvl, b) in btns)
        {
            bool on = lvl == level;
            b.Background = on ? UiBrushes.BgBrass : UiBrushes.BgCard;
            b.BorderBrush = on ? UiBrushes.BgBrass : UiBrushes.BorderSubtle;
            b.Foreground = on ? UiBrushes.FgDark : UiBrushes.FgMuted;
        }
    }

    private async void GenerateFilterWithAi_Click(object sender, RoutedEventArgs e)
    {
        if (_filterGenBusy) return;
        _filterGenBusy = true;
        _filterStatusCustom = true;
        BtnAiGenerateFilter.Content = "AI …";
        FilterStatusText.Text = Loc.T("Генерую PoE 2 лутофільтр під активний білд...", "Generating PoE 2 loot filter for active build...", "Генерирую PoE 2 лутфильтр под активный билд...");
        FilterStatusText.Foreground = UiBrushes.FgBrass;

        try
        {
            var (ok, text, modelOrErr) = await LootFilterService.GenerateWithAiAsync(_ai, _profiles.ActiveProfile, _filterStrictness, FilterPromptBox.Text);
            if (!ok)
            {
                FilterStatusText.Text = modelOrErr;
                FilterStatusText.Foreground = UiBrushes.FgCrimson;
                return;
            }
            FilterEditorBox.Text = text;
            var val = LootFilterService.ValidateFilter(text);
            string warn = val.Warnings.Count > 0 ? $" · ⚠ {string.Join("; ", val.Warnings)}" : "";
            FilterStatusText.Text = Loc.T($"Згенеровано ({modelOrErr}) · {val.BlockCount} блоків{warn}. Натисни «Зберегти в PoE 2»!", $"Generated ({modelOrErr}) · {val.BlockCount} blocks{warn}. Click 'Save to PoE 2'!", $"Сгенерировано ({modelOrErr}) · {val.BlockCount} блоков{warn}. Нажми «Сохранить в PoE 2»!");
            FilterStatusText.Foreground = val.IsValid && val.Warnings.Count == 0 ? UiBrushes.FgEmerald : UiBrushes.FgAmber;
        }
        finally
        {
            _filterGenBusy = false;
            BtnAiGenerateFilter.Content = Loc.T("AI-генерувати", "AI Generate", "AI-генерировать");
        }
    }

    private void SaveFilterEditorToGame_Click(object sender, RoutedEventArgs e)
    {
        string text = FilterEditorBox.Text;
        _filterStatusCustom = true;
        var val = LootFilterService.ValidateFilter(text);
        if (!val.IsValid)
        {
            FilterStatusText.Text = string.Join(" ", val.Warnings);
            FilterStatusText.Foreground = UiBrushes.FgCrimson;
            return;
        }
        try
        {
            string savedPath = LootFilterService.SaveCustomFilter(FilterNameBox.Text, text);
            string warn = val.Warnings.Count > 0 ? $" · ⚠ {string.Join("; ", val.Warnings)}" : "";
            FilterStatusText.Text = Loc.T($"Збережено в PoE 2: {Path.GetFileName(savedPath)} ({val.BlockCount} блоків{warn})", $"Saved to PoE 2: {Path.GetFileName(savedPath)} ({val.BlockCount} blocks{warn})", $"Сохранено в PoE 2: {Path.GetFileName(savedPath)} ({val.BlockCount} блоков{warn})");
            FilterStatusText.Foreground = UiBrushes.FgEmerald;
            RefreshInstalledFiltersLabel();
        }
        catch (Exception ex)
        {
            FilterStatusText.Text = ex.Message;
            FilterStatusText.Foreground = UiBrushes.FgCrimson;
        }
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

    // --- Шапка та Режими вікна ---
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleCollapse_Click(object sender, RoutedEventArgs e) => ToggleCollapseOrMiniHud();

    private void ToggleCollapseOrMiniHud()
    {
        if (_miniHudMode) { SwitchFromMiniHud(); return; }
        _collapsed = !_collapsed;
        if (_collapsed)
        {
            BodyGrid.Visibility = Visibility.Collapsed;
            ResizeGrip.Visibility = Visibility.Collapsed;
            NavbarBorder.CornerRadius = new CornerRadius(5);
            NavbarBorder.BorderThickness = new Thickness(0);
            MinHeight = CollapsedHeight;
            Height = CollapsedHeight;
            Width = ExpandedWidth;
            CollapseBtn.Content = "+ F9";
        }
        else
        {
            MinHeight = 340;
            Height = ExpandedHeight;
            Width = ExpandedWidth;
            NavbarBorder.CornerRadius = new CornerRadius(5, 5, 0, 0);
            NavbarBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            BodyGrid.Visibility = Visibility.Visible;
            ResizeGrip.Visibility = Visibility.Visible;
            CollapseBtn.Content = "F9";
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void SwitchToMiniHud_Click(object sender, RoutedEventArgs e)
    {
        _miniHudMode = true;
        Hide();
        UpdateMiniHudContent();
        _miniHud.Show();
        Win32OverlayHelper.ApplyOverlayStyles(_miniHud, noActivate: true, clickThrough: _clickThrough);
    }

    private void SwitchFromMiniHud()
    {
        _miniHudMode = false;
        _miniHud.Hide();
        Show();
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void ToggleShowFromTray()
    {
        if (_miniHudMode) SwitchFromMiniHud();
        else if (IsVisible) Hide();
        else RestoreOverlayVisible();
    }

    private void RestoreOverlayVisible()
    {
        if (_miniHudMode) SwitchFromMiniHud();
        if (!IsVisible) Show();
        if (_collapsed) ToggleCollapseOrMiniHud();
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
        Win32OverlayHelper.EnsureTopmostNoActivate(this);
    }

    private void UpdateMiniHudContent()
    {
        if (!_miniHudMode) return;
        string name = _profiles.ActiveProfile?.Name ?? "PoE 2";
        string buildShort = name.Length > 20 ? name[..19] + "…" : name;
        var fs = _watcher.FarmStats;
        string lvl = EffectiveLevel.ToString();
        string avg = fs.MapsCompleted > 0 ? $" · {Loc.T("Мап", "Maps", "Карт")}: {fs.MapsCompleted} ({FormatDuration(fs.AverageMapSeconds)})" : "";
        _miniHud.UpdateHud(buildShort, $"{Loc.LevelShort} {lvl}{avg}", BuffTimerText.Text, Loc.T("Розгорнути", "Expand", "Развернуть"), _clickThrough);
    }

    private void ToggleClickThrough_Click(object sender, RoutedEventArgs e) => ToggleClickThrough();

    private void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        if (_clickThrough)
        {
            _allowTyping = false;
            UpdateTypingButtons();
            LockBtn.Content = "LOCK F10";
            LockBtn.Foreground = UiBrushes.FgAmber;
            Win32OverlayHelper.SetWindowOpacity(this, Math.Min(_alphas[_alphaIdx], 0.78));
        }
        else
        {
            LockBtn.Content = "F10";
            LockBtn.Foreground = UiBrushes.FgEmerald;
            Win32OverlayHelper.SetWindowOpacity(this, _alphas[_alphaIdx]);
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
        if (_miniHudMode) UpdateMiniHudContent();
    }

    private void CycleAlpha_Click(object sender, RoutedEventArgs e)
    {
        _alphaIdx = (_alphaIdx + 1) % _alphas.Length;
        _ai.Config.WindowOpacity = _alphas[_alphaIdx];
        _ai.SaveConfig();
        UpdateAlphaButtonLabel();
        if (!_clickThrough) Win32OverlayHelper.SetWindowOpacity(this, _alphas[_alphaIdx]);
    }

    private void UpdateAlphaButtonLabel()
    {
        if (AlphaBtn == null) return;
        int pct = (int)Math.Round(_alphas[_alphaIdx] * 100);
        AlphaBtn.Content = Loc.T($"Прозорість: {pct}%", $"Opacity: {pct}%", $"Прозрачность: {pct}%");
    }

    private void UpdateLogButtonUi()
    {
        if (LogBtn == null) return;
        if (_watcher.LogPath != null)
        {
            LogBtn.Content = Loc.T("Client.txt ✓", "Client.txt ✓", "Client.txt ✓");
            LogBtn.ToolTip = Loc.T(
                $"Лог PoE 2 знайдено:\n{_watcher.LogPath}\n\nЗ нього оверлей автоматично бере поточну локацію, level-up, статистику сесії та Trade-шепіт. Натисни, щоб змінити шлях.",
                $"PoE 2 log found:\n{_watcher.LogPath}\n\nUsed automatically for current zone, level-ups, session tracker, and Trade whispers. Click to change path.",
                $"Лог PoE 2 найден:\n{_watcher.LogPath}\n\nИспользуется для текущей зоны, level-up, трекера сессии и Trade-шёпота. Нажми, чтобы изменить путь.");
        }
        else
        {
            LogBtn.Content = Loc.T("Обрати Client.txt", "Select Client.txt", "Выбрать Client.txt");
            LogBtn.ToolTip = Loc.T(
                "Лог Client.txt не знайдено автоматично.\nВкажи файл logs\\Client.txt з папки PoE 2 — з нього читаються локації, level-up, статистика сесії та Trade-шепіт.",
                "Client.txt was not auto-detected.\nPick logs\\Client.txt from your PoE 2 folder to enable zone tracking, level-ups, session stats, and Trade whispers.",
                "Файл Client.txt не найден автоматически.\nУкажи logs\\Client.txt из папки PoE 2 для отслеживания зон, level-up, статистики сессии и Trade-шёпота.");
        }
    }

    private void ToggleTyping_Click(object sender, RoutedEventArgs e)
    {
        _allowTyping = !_allowTyping;
        UpdateTypingButtons();
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void UpdateTypingButtons()
    {
        string text = _allowTyping ? Loc.BtnLockFocus : Loc.BtnAllowTyping;
        var fg = _allowTyping ? UiBrushes.FgEmerald : UiBrushes.FgMuted;
        var bg = _allowTyping ? UiBrushes.BgEmeraldDim : UiBrushes.BgCard;
        foreach (var b in new[] { PobTypeToggleBtn, AiTypeBtn, TreeTypeBtn, FilterTypeBtn, GemsTypeBtn, SettingsTypeBtn })
        {
            if (b == null) continue;
            b.Content = text;
            b.Foreground = fg;
            b.Background = bg;
        }
    }

    // --- UI Builders (чисті Bootstrap 5.3 Dark .card без зайвого візуального шуму) ---
    private static void AddSectionHeader(Panel parent, string text, double topMargin = 0)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = UiBrushes.FgMuted,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, topMargin, 0, 6)
        });
    }

    private static Border CreateSimpleRowCard(string title, string subtitle)
    {
        var st = new StackPanel();
        st.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = UiBrushes.FgPrimary,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            st.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = UiBrushes.FgMuted,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
                LineHeight = 16.5
            });
        }
        return new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = UiBrushes.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Child = st
        };
    }

    private Border CreateSkillLinkCard(string skillName, List<string> supports, int stageMinLvl)
    {
        var wrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var mainInfo = Poe2GemsDb.ResolveGem(skillName, stageMinLvl);
        string spiritTag = mainInfo.SpiritCost > 0 ? $" · {mainInfo.SpiritCost}Sp" : "";

        var mainPill = new Border
        {
            Background = UiBrushes.BgBrassDim,
            BorderBrush = UiBrushes.BorderBrass,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 2, 7, 2),
            Cursor = Cursors.Hand,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = mainInfo.Name, Foreground = UiBrushes.FgBrass, FontWeight = FontWeights.SemiBold, FontSize = 11 },
                    new TextBlock { Text = $"  Lv{mainInfo.GemLevelInBuild} · {Loc.LevelShort}{mainInfo.MinUnlockCharacterLevel}+{spiritTag}", Foreground = UiBrushes.FgMuted, FontSize = 9.5, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        mainPill.MouseLeftButtonUp += (_, _) => ShowGemInspector(mainInfo);
        wrap.Children.Add(mainPill);

        foreach (var sup in supports)
        {
            var supInfo = Poe2GemsDb.ResolveGem(sup, stageMinLvl);
            string tierShort = supInfo.UncutTier.Replace("Uncut Support Gem Tier ", "T");
            var supPill = new Border
            {
                Background = UiBrushes.BgCardElevated,
                BorderBrush = UiBrushes.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 2, 5, 2),
                Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = supInfo.Name, Foreground = UiBrushes.FgPrimary, FontSize = 10.5 },
                        new TextBlock { Text = $" · {tierShort} · {Loc.LevelShort}{supInfo.MinUnlockCharacterLevel}+", Foreground = UiBrushes.FgMuted, FontSize = 9.5, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            supPill.MouseEnter += (_, _) => supPill.BorderBrush = UiBrushes.BorderBrass;
            supPill.MouseLeave += (_, _) => supPill.BorderBrush = UiBrushes.BorderSubtle;
            supPill.MouseLeftButtonUp += (_, _) => ShowGemInspector(supInfo);
            wrap.Children.Add(supPill);
        }

        return new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = UiBrushes.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Child = wrap
        };
    }

    private Border CreateGearTargetCard(string rawSlotId, string slotName, string localizedBody, string rawEnglishText)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var obt = Poe2KnowledgeDb.ResolveItemObtain(rawEnglishText, rawSlotId, _profiles.ActiveProfile, EffectiveLevel);
        var st = new StackPanel();
        st.Children.Add(new TextBlock
        {
            Text = slotName,
            Foreground = UiBrushes.FgBrass,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5
        });
        if (!string.IsNullOrWhiteSpace(localizedBody))
        {
            st.Children.Add(new TextBlock
            {
                Text = localizedBody,
                Foreground = UiBrushes.FgPrimary,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                LineHeight = 15.5
            });
        }
        string compactStats = PoeAccountService.SummarizeItemStatsCompact(rawEnglishText);
        if (!string.IsNullOrWhiteSpace(compactStats) &&
            !localizedBody.Contains(compactStats, StringComparison.OrdinalIgnoreCase))
        {
            st.Children.Add(new TextBlock
            {
                Text = compactStats,
                Foreground = UiBrushes.FgSteel,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        st.Children.Add(new TextBlock
        {
            Text = $"{Loc.T("Джерело:", "Drop/Get:", "Источник:")} {obt.MinItemOrAreaLevel} — {Loc.T(obt.ObtainSourceUk, obt.ObtainSourceEn, obt.ObtainSourceRu)}",
            Foreground = UiBrushes.FgEmerald,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        grid.Children.Add(st);

        var actions = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var infoBtn = new Button
        {
            Content = Loc.T("Опис", "Guide", "Инфо"),
            Style = (Style)FindResource("FlatButton"),
            Foreground = UiBrushes.FgPrimary,
            FontSize = 10,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 0, 4)
        };
        infoBtn.Click += (_, _) => ShowGearInspector(rawSlotId, slotName, rawEnglishText);
        actions.Children.Add(infoBtn);

        if (rawEnglishText.Contains("Item Class:"))
        {
            var cmpBtn = new Button
            {
                Content = Loc.T("Порівняти", "Compare", "Сравнить"),
                Style = (Style)FindResource("FlatButton"),
                Foreground = UiBrushes.FgEmerald,
                FontSize = 10,
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 0, 4)
            };
            cmpBtn.Click += (_, _) => InspectEquippedSlotOrRawInItemsTab(rawSlotId, rawEnglishText);
            actions.Children.Add(cmpBtn);
        }

        string searchBase = rawEnglishText.Split(['\n', '|'], StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(l => l.StartsWith("Item Class:") || l.StartsWith("Rarity:") || l.StartsWith("--------"))
            .FirstOrDefault()?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(searchBase) && searchBase.Length <= 48 && !searchBase.Contains('%'))
        {
            var tradeBtn = new Button
            {
                Content = "Trade",
                Style = (Style)FindResource("FlatButton"),
                Foreground = UiBrushes.FgBrass,
                FontSize = 10,
                Padding = new Thickness(9, 3, 9, 3)
            };
            tradeBtn.Click += (_, _) =>
            {
                SaveTradeLeague();
                TradeLinkBuilder.OpenTradeByItemName(_ai.Config.TradeLeague, searchBase, isUnique: obt.Category.Contains("Unique"));
            };
            actions.Children.Add(tradeBtn);
        }
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        return new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = UiBrushes.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid
        };
    }

    private static string ShortStageTitle(string name, int maxLen)
    {
        string first = name.Split(" - ")[0].Trim();
        return first.Length > maxLen ? first[..(maxLen - 1)] + "…" : first;
    }
}
