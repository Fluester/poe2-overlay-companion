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
        PreviewGotKeyboardFocus += MainWindow_PreviewGotKeyboardFocus;
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
            if (_allowTyping) SetTypingMode(false, returnFocusToGame: true);
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
        if (_allowTyping) SetTypingMode(false, returnFocusToGame: true);
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
            LockBtn.Content = "LOCK F10";
            LockBtn.Foreground = UiBrushes.FgAmber;
            Win32OverlayHelper.SetWindowOpacity(this, Math.Min(_alphas[_alphaIdx], 0.78));
            SetTypingMode(false, returnFocusToGame: _allowTyping || IsKeyboardFocusWithin);
        }
        else
        {
            LockBtn.Content = "F10";
            LockBtn.Foreground = UiBrushes.FgEmerald;
            Win32OverlayHelper.SetWindowOpacity(this, _alphas[_alphaIdx]);
            Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: false);
        }
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

    private void MainWindow_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => Win32OverlayHelper.HandlePreviewGotTextFocus(_allowTyping, e);

    private void ToggleTyping_Click(object sender, RoutedEventArgs e)
        => SetTypingMode(!_allowTyping, returnFocusToGame: true);

    private void SetTypingMode(bool enabled, bool returnFocusToGame)
    {
        bool hadFocusOrTyping = _allowTyping || IsKeyboardFocusWithin;
        _allowTyping = enabled;
        UpdateTypingButtons();
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
        if (!_allowTyping)
            Win32OverlayHelper.ReleaseTypingFocus(this, returnFocusToGame: returnFocusToGame && hadFocusOrTyping);
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
