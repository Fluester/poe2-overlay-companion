using System.Diagnostics;
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
    private readonly double[] _alphas = [0.95, 0.82, 1.0];

    private string _lastClipboard = "";
    private string _ignoreClipboardText = "";
    private string? _lastZoneNotified;

    private ItemEvaluation? _lastItemEval;
    private readonly List<(CheckBox box, ItemModFilterOption mod)> _modFilterCheckboxes = [];
    private List<LocalBuildPlan> _plannerBuilds = [];
    private LocalBuildPlan? _selectedPlannerBuild;
    private int _overviewStageIdx;
    private int _treeStageIdx;
    private bool _treeListOpen;
    private int? _treeWeaponSetFilter;
    private int _overviewSubtab; // 0 = main, 1 = gems encyclopedia, 2 = gear obtain
    private string _gemFilterMode = "build";
    private string _inspectorAiPrompt = "";
    private int _filterStrictness = 3;
    private bool _filterGenBusy;
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

    // --- Навігація та Лінива ініціалізація ---
    private void InitNavigation()
    {
        _navMap[NavBtnOverview] = PageOverview;
        _navMap[NavBtnPassives] = PagePassives;
        _navMap[NavBtnItems] = PageItems;
        _navMap[NavBtnPlanner] = PagePlanner;
        _navMap[NavBtnFarm] = PageFarm;
        _navMap[NavBtnUpdates] = PageUpdates;
        _navMap[NavBtnAi] = PageAi;
        _navMap[NavBtnBuilds] = PageBuilds;
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
        if (!ReferenceEquals(activeBtn, NavBtnPassives))
            PassiveTreeService.UnpinCache();

        foreach (var (btn, page) in _navMap)
        {
            bool active = ReferenceEquals(btn, activeBtn);
            page.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            btn.Background = active ? UiBrushes.BgNavActive : UiBrushes.Transparent;
            btn.Foreground = active ? UiBrushes.FgBrass : UiBrushes.FgMuted;
            btn.BorderBrush = active ? UiBrushes.FgBrass : UiBrushes.Transparent;
            btn.BorderThickness = active ? new Thickness(2.5, 0, 0, 0) : new Thickness(0);
        }
    }

    private void EnsureTabInitialized(Button tabBtn)
    {
        if (!_initializedTabs.Add(tabBtn)) return;

        if (ReferenceEquals(tabBtn, NavBtnPlanner))
            LoadBuildPlannerFiles();
        else if (ReferenceEquals(tabBtn, NavBtnPassives))
            RefreshPassiveTreeTab(resetSteps: true);
        else if (ReferenceEquals(tabBtn, NavBtnItems))
        {
            RefreshDynamicTradeButtons();
            RefreshInstalledFiltersLabel();
        }
        else if (ReferenceEquals(tabBtn, NavBtnFarm))
            RefreshFarmStatsUi();
        else if (ReferenceEquals(tabBtn, NavBtnUpdates))
            InitUpdateMonitorUi();
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
        RefreshOverview();
        if (_initializedTabs.Contains(NavBtnPassives)) RefreshPassiveTreeTab(resetSteps: false);
        if (_initializedTabs.Contains(NavBtnPlanner)) RenderPlannerCards();
        if (_initializedTabs.Contains(NavBtnFarm)) RefreshFarmStatsUi();
        if (_initializedTabs.Contains(NavBtnUpdates)) RenderUpdateHistory();
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

        TxtNavOverview.Text = Loc.NavOverview;
        TxtNavPassives.Text = Loc.NavPassives;
        TxtNavItems.Text = Loc.NavItems;
        TxtNavPlanner.Text = Loc.NavPlanner;
        TxtNavFarm.Text = Loc.NavFarm;
        TxtNavUpdates.Text = Loc.NavUpdates;
        TxtNavAi.Text = Loc.NavAi;
        TxtNavBuilds.Text = Loc.NavBuilds;
        TxtNavSettings.Text = Loc.NavSettings;

        LogBtn.Content = Loc.T("Обрати Client.txt", "Select Client.txt", "Выбрать Client.txt");

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
        if (_initializedTabs.Contains(NavBtnItems)) RefreshInstalledFiltersLabel();
        LblTradeTitle.Text = Loc.T("PoE 2 Trade та Regex", "PoE 2 Trade & Regex", "PoE 2 Trade и Regex");
        LblLeague.Text = Loc.T("Ліга:", "League:", "Лига:");
        if (_lastItemEval == null)
        {
            ItemVerdictText.Text = Loc.T("Наведи на предмет у PoE 2 і натисни Ctrl+C", "Hover an item in PoE 2 and press Ctrl+C", "Наведи на предмет в PoE 2 и нажми Ctrl+C");
            ItemDetailsText.Text = Loc.T("Перевірка предмета адаптується під активний білд та поточний етап.", "Evaluation adapts automatically to your active build and stage.", "Оценка предмета адаптируется под активный билд и текущий этап.");
        }

        ChkPlannerFilterByLevel.Content = Loc.T("Фільтр за моїм рівнем (level_interval)", "Filter by my level (level_interval)", "Фильтр по моему уровню (level_interval)");
        BtnImportBuildFiles.Content = Loc.T("+ Файли .build", "+ .build Files", "+ Файлы .build");
        BtnImportBuildFolder.Content = Loc.T("+ З папки", "+ From Folder", "+ Из папки");
        BtnPlannerMakeProfile.Content = Loc.T("Створити білд", "Make Active Build", "Создать билд");
        BtnOpenPlannerDir.Content = Loc.T("Папка гри", "Game Folder", "Папка игры");

        LblFarmHeader.Text = Loc.T("Статистика сесії (Client.txt)", "Session Tracker (Client.txt)", "Статистика сессии (Client.txt)");
        BtnResetFarm.Content = Loc.T("Скинути", "Reset", "Сбросить");
        LblKpiSession.Text = Loc.T("Час сесії", "Session Time", "Время сессии");
        LblKpiZone.Text = Loc.T("Поточна зона", "Current Zone", "Текущая зона");
        LblKpiMaps.Text = Loc.T("Карт / Середній час", "Maps / Avg Clear", "Карт / Среднее время");
        LblKpiEncounters.Text = Loc.T("Енкаунтери / Смерті", "Encounters / Deaths", "Энкаунтеры / Смерти");

        ChkAutoUpdateMonitor.Content = Loc.T("Моніторити автоматично:", "Auto check:", "Проверять автоматически:");
        BtnInterval48.Content = Loc.T("2 дні", "2 days", "2 дня");
        BtnInterval60.Content = Loc.T("2.5 дні", "2.5 days", "2.5 дня");
        BtnInterval72.Content = Loc.T("3 дні", "3 days", "3 дня");
        BtnCheckUpdatesNow.Content = Loc.T("Перевірити білди", "Check All Builds", "Проверить билды");
        BtnOpenGuideUpd.Content = Loc.T("Відкрити гайд", "Open Guide", "Открыть гайд");
        BtnDiffClipboard.Content = Loc.T("Diff (Ctrl+C)", "Clipboard Diff", "Diff (Ctrl+C)");
        BtnAiDiff.Content = Loc.T("AI Diff (Ctrl+C)", "AI Diff", "AI Diff");

        BtnSaveApi.Content = Loc.T("Зберегти", "Save", "Сохранить");
        BtnAiAsk.Content = Loc.T("Запитати", "Ask", "Спросить");
        BtnAiScreen.Content = Loc.T("Скрін + AI (F11)", "Screen + AI (F11)", "Скрин + AI (F11)");
        ChkAutoAi30.Content = Loc.T("Авто-скан 30хв", "Auto-scan 30m", "Авто-скан 30м");
        ChkAiOnlySafeZone.Content = Loc.T("Лише місто/Hideout", "Town/Hideout only", "Только город/Hideout");

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
        LblModFiltersTitle.Text = Loc.T("ПРАЙС-ЧЕК ЗА ВИБРАНИМИ МОДАМИ:", "CUSTOM MOD PRICE CHECK:", "ПРАЙС-ЧЕК ПО ВЫБРАННЫМ МОДАМ:");
        BtnRecheckPriceByMods.Content = Loc.T("Ціна по модах", "Check Selected", "Цена по модам");
        BtnOpenTradeByMods.Content = Loc.T("Відкрити Trade 2", "Open Trade 2", "Открыть Trade 2");
        BtnScanStashNetWorth.Content = Loc.T("Оцінити скриню (Stash)", "Scan Stash Net-Worth", "Оценить тайник (Stash)");
        BtnCopyVendorRegex.Content = Loc.T("Regex Торгівля", "Vendor Regex", "Regex Торговец");
        BtnCopyWaystoneRegex.Content = Loc.T("Regex Мап", "Waystone Regex", "Regex Карт");
        ChkFocusOnCtrlC.Content = Loc.T("Відкривати вкладку при Ctrl+C", "Switch tab on Ctrl+C", "Открывать вкладку при Ctrl+C");
        ChkTradeHelper.Content = Loc.T("Trade Helper (шепіт)", "Trade Helper (whispers)", "Trade Helper (шепот)");
        ChkHideNoGame.Content = Loc.T("Ховати без PoE 2", "Hide without PoE 2", "Скрывать без PoE 2");
        ChkAutostart.Content = Loc.T("Запуск з Windows", "Launch with Windows", "Запуск с Windows");
        InstallFilterBtn.Content = Loc.T("Встановити .filter", "Install .filter", "Установить .filter");
        ChkBuffTimer.Content = Loc.T("Таймер бафу:", "Buff Timer:", "Таймер баффа:");
        LblCustomCmds.Text = Loc.T("F8 команди:", "F8 cmds:", "F8 команды:");

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
            var live = await PoeTradePriceService.FetchLeaguesAsync();
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
        if (_initializedTabs.Contains(NavBtnPlanner)) RenderPlannerCards();
    }

    private void ConnectPoeAccount_Click(object sender, RoutedEventArgs e)
    {
        var win = new PoeAccountLoginWindow(_ai, _profiles) { Owner = this };
        bool? ok = win.ShowDialog();
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
        if (ok == true && win.SyncedOk)
        {
            _watcher.SetManualLevel(Math.Max(1, _ai.Config.LastKnownLevel));
            ApplySelectedLeague(_ai.Config.TradeLeague);
            UpdateAccountStatusLabel();
            RefreshProfilesUi();
            RefreshOverview();
            RefreshLogStatus(showZoneToast: false);
            if (_lastItemEval != null) CheckClipboard(force: true);
            _toast.ShowToast("PoE 2 Account", win.ResultMessage, Color.FromRgb(0x4E, 0xCF, 0x8F), nearCursor: false, seconds: 5.0);
        }
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
        LblPoeAccountStatus.Text = $"{_ai.Config.PoeCharacterName} · Lv {_ai.Config.PoeCharacterLevel} ({_ai.Config.PoeCharacterClass}) · {_ai.Config.WornGearBySlot.Count} {Loc.T("слотів", "slots", "слотов")}";
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

        if (prof == null)
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

        int curLvl = EffectiveLevel;

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
                    OverviewCardsPanel.Children.Add(CreateGearTargetCard(rawSlot, PoeTermLocalizer.SlotName(rawSlot), PoeTermLocalizer.TranslateGearDescription(desc), desc));
                }
            }

            string stageNote = !string.IsNullOrWhiteSpace(activeStage.Notes) ? activeStage.Notes : prof.SummaryNotes;
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
        if (prof == null) return list;

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

        // Поточні вдягнуті слоти користувача (щоб оверлей завжди розумів, що в тебе зараз одягнено)
        AddSectionHeader(GearObtainPanel, Loc.T($"Твій поточний екіп (Вдягнуто зараз · Рів.{curLvl})", $"Your Currently Equipped Gear (Lvl {curLvl})", $"Твой текущий экип (Ур.{curLvl})"));
        var wornWrap = new StackPanel();
        string[] coreSlots = ["Weapon1", "Offhand1", "Helm1", "BodyArmour1", "Gloves1", "Boots1", "Amulet1", "Ring1", "Ring2", "Belt1"];
        foreach (string slotKey in coreSlots)
        {
            _ai.Config.WornGearBySlot.TryGetValue(slotKey, out string? wornText);
            string firstLine = !string.IsNullOrWhiteSpace(wornText)
                ? wornText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? slotKey
                : Loc.T("не задано (Ctrl+C -> Вдягнути / Синхр.)", "empty (Ctrl+C -> Equip / Account Sync)", "пусто");

            var row = new DockPanel { Margin = new Thickness(0, 1.5, 0, 1.5) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(btns, Dock.Right);

            string capturedSlot = slotKey;
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
                };
                btns.Children.Add(btnClr);
            }

            row.Children.Add(btns);
            row.Children.Add(new TextBlock
            {
                Text = $"{PoeTermLocalizer.SlotName(slotKey)}: {firstLine}",
                Foreground = !string.IsNullOrWhiteSpace(wornText) ? UiBrushes.FgPrimary : UiBrushes.FgMuted,
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
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
        if (prof == null)
        {
            GearObtainPanel.Children.Add(CreateSimpleRowCard(
                Loc.T("Спочатку обери або імпортуй білд", "Select or import a build first", "Сначала выбери или импортируй билд"),
                Loc.T("Тут покаже кожен слот екіпу з інструкцією, де його вибити, який потрібен ilvl та як оцінити вигоду.", "Shows every gear slot, boss/drop source, ilvl breakpoints, and craft tips.", "Покажет каждый слот экипа, откуда он падает и какой нужен ilvl.")));
            return;
        }

        var items = new List<(string slotId, string text)>();
        if (prof.Stages.Count > 0 && _overviewStageIdx < prof.Stages.Count)
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
        else if (prof.TargetGearHints.Count > 0)
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
            string firstLine = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? obt.ItemTitle;

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
            top.Children.Add(new TextBlock
            {
                Text = $"{slotLabel} — {firstLine}",
                Foreground = UiBrushes.FgBrass,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
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
            st.Children.Add(top);

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
        var obt = Poe2KnowledgeDb.ResolveItemObtain(rawText, rawSlotId, _profiles.ActiveProfile);
        InspectorTitleText.Text = $"{slotTitle} — {obt.ItemTitle}";
        InspectorMetaText.Text = $"{obt.Category} · {obt.MinItemOrAreaLevel}";
        InspectorMetaText.Foreground = UiBrushes.FgSteel;

        string src = Loc.T(obt.ObtainSourceUk, obt.ObtainSourceEn, obt.ObtainSourceRu);
        string tip = Loc.T(obt.StrategyTipUk, obt.StrategyTipEn, obt.StrategyTipRu);
        InspectorBodyText.Text = $"{Loc.T("Де отримати:", "Where to get:", "Где получить:")} {src}\n{Loc.T("Порада / цінність:", "Tip / Value:", "Совет / ценность:")} {tip}";
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

        List<LocalBuildPassive> curPassives = [];
        List<LocalBuildPassive>? prevPassives = null;
        string title = prof?.Name ?? "";

        if (prof != null && prof.Stages.Count > 0)
        {
            _treeStageIdx = Math.Clamp(_treeStageIdx, 0, prof.Stages.Count - 1);
            var st = prof.Stages[_treeStageIdx];
            title = st.Name;
            if (!string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath))
                curPassives = BuildPlannerService.TryParseFile(st.FilePath)?.Passives ?? [];
            else
                curPassives = st.PassiveIds.Select(id => new LocalBuildPassive(id, 1, 100, 0, "")).ToList();

            if (ChkComparePrevStage.IsChecked == true && _treeStageIdx > 0)
            {
                var prevSt = prof.Stages[_treeStageIdx - 1];
                if (!string.IsNullOrWhiteSpace(prevSt.FilePath) && File.Exists(prevSt.FilePath))
                    prevPassives = BuildPlannerService.TryParseFile(prevSt.FilePath)?.Passives;
                else
                    prevPassives = prevSt.PassiveIds.Select(id => new LocalBuildPassive(id, 1, 100, 0, "")).ToList();
            }
        }
        else if (_selectedPlannerBuild != null)
        {
            curPassives = _selectedPlannerBuild.Passives;
            title = _selectedPlannerBuild.Name;
        }

        TreeStageTitle.Text = ShortStageTitle(title, 18);

        _suppressTreeSliderEvent = true;
        TreeStepSlider.Minimum = 0;
        TreeStepSlider.Maximum = Math.Max(1, curPassives.Count);
        if (resetSteps || TreeStepSlider.Value > TreeStepSlider.Maximum || TreeStepSlider.Value == 0)
            TreeStepSlider.Value = curPassives.Count;
        _suppressTreeSliderEvent = false;

        RenderPassiveDiffAndList(title, curPassives, prevPassives, preserveViewport);
    }

    private void RenderPassiveDiffAndList(string title, IReadOnlyList<LocalBuildPassive> curPassives, IReadOnlyList<LocalBuildPassive>? prevPassives, bool preserveViewport)
    {
        int stepLimit = Math.Clamp((int)TreeStepSlider.Value, 0, curPassives.Count);
        TreeStepLabel.Text = $"{stepLimit} / {curPassives.Count}";

        var diff = PassiveTreeService.AnalyzeBuildPassives(title, curPassives, prevPassives, stepLimit);
        TreeCanvas.SetTreeData(diff, preserveViewport: preserveViewport);

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
        IReadOnlyList<LocalBuildPassive> cur = [];
        IReadOnlyList<LocalBuildPassive>? prev = null;
        string title = prof.Name;
        if (prof.Stages.Count > 0)
        {
            var st = prof.Stages[Math.Clamp(_treeStageIdx, 0, prof.Stages.Count - 1)];
            title = st.Name;
            cur = !string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath)
                ? (BuildPlannerService.TryParseFile(st.FilePath)?.Passives ?? [])
                : st.PassiveIds.Select(id => new LocalBuildPassive(id, 1, 100, 0, "")).ToList();
            if (ChkComparePrevStage.IsChecked == true && _treeStageIdx > 0)
            {
                var pst = prof.Stages[_treeStageIdx - 1];
                prev = !string.IsNullOrWhiteSpace(pst.FilePath) && File.Exists(pst.FilePath)
                    ? BuildPlannerService.TryParseFile(pst.FilePath)?.Passives
                    : pst.PassiveIds.Select(id => new LocalBuildPassive(id, 1, 100, 0, "")).ToList();
            }
        }
        int stepLimit = Math.Clamp((int)TreeStepSlider.Value, 0, cur.Count);
        var diff = PassiveTreeService.AnalyzeBuildPassives(title, cur, prev, stepLimit);
        var pop = new PassiveTreeWindow(title, diff, _treeWeaponSetFilter) { Owner = this };
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
        if (_initializedTabs.Contains(NavBtnUpdates)) RenderUpdateHistory();
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
        var win = new MobalyticsImportWindow(_profiles, url, customName) { Owner = this };
        bool? ok = win.ShowDialog();
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
        if (ok == true && win.ImportedProfile != null)
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
                if (_initializedTabs.Contains(NavBtnPlanner)) LoadBuildPlannerFiles();
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

    private void ImportBuildFilesToPlanner_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Пакетно додати .build файл(и) в гру", "Add .build file(s) to PoE 2 & Overlay", "Пакетно добавить .build файл(ы) в игру"),
            Filter = "PoE 2 Build (*.build)|*.build|All (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
        {
            var (copied, _) = BuildPlannerService.ImportToGameBuildPlanner(dlg.FileNames);
            NotifyPlannerImport(copied);
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void ImportBuildFolderToPlanner_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = Loc.T("Скопіювати всі .build з папки в BuildPlanner гри", "Copy all .build from folder to PoE 2", "Скопировать все .build из папки в BuildPlanner игры")
        };
        if (dlg.ShowDialog() == true)
        {
            var (copied, _) = BuildPlannerService.ImportFolderToGameBuildPlanner(dlg.FolderName);
            NotifyPlannerImport(copied);
        }
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
    }

    private void NotifyPlannerImport(int copied)
    {
        LoadBuildPlannerFiles();
        string title = Loc.T("Build Planner гри", "PoE 2 Build Planner", "Build Planner игры");
        string msg = copied > 0
            ? Loc.T($"Скопійовано {copied} .build у папку PoE 2 — доступні і в грі!", $"Copied {copied} .build to PoE 2 BuildPlanner!", $"Скопировано {copied} .build в папку PoE 2!")
            : Loc.T("Файли вже в папці гри або не знайдено.", "Files already in game folder or none found.", "Файлы уже в папке игры или не найдены.");
        _toast.ShowToast(title, msg, copied > 0 ? Color.FromRgb(0x4E, 0xCF, 0x8F) : Color.FromRgb(0x8B, 0x93, 0xA5), nearCursor: false, seconds: 4.0);
    }

    private void ReloadBuildPlanner_Click(object sender, RoutedEventArgs e) => LoadBuildPlannerFiles();

    private void OpenBuildPlannerFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = BuildPlannerService.GetBuildPlannerFolder();
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    // --- Предмети (Ctrl+C) + Trade2 ---
    private void CheckClipboardNow_Click(object sender, RoutedEventArgs e) => CheckClipboard(force: true);

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

        var ev = ItemEvaluator.Evaluate(text, _profiles.ActiveProfile, _overviewStageIdx, _ai.Config.WornGearBySlot, EffectiveLevel);
        if (!ev.IsPoeItem)
        {
            if (force)
            {
                ItemVerdictText.Text = Loc.T("У буфері не знайдено предмета PoE 2", "Clipboard does not contain a PoE 2 item", "В буфере не найден предмет PoE 2");
                ItemVerdictText.Foreground = UiBrushes.FgMuted;
                ItemUpgradeBadge.Visibility = Visibility.Collapsed;
                ItemPriceBadge.Visibility = Visibility.Collapsed;
                ItemProfitBadge.Visibility = Visibility.Collapsed;
                ItemModFilterBox.Visibility = Visibility.Collapsed;
            }
            return;
        }

        _lastItemEval = ev;
        RenderSelectableModCheckboxes(ev);
        Color col = ev.Verdict == ItemVerdict.Danger
            ? GetVerdictColor(ItemVerdict.Danger)
            : ev.UpgradeDeltaPct >= 8
                ? Color.FromRgb(0x4E, 0xCF, 0x8F)
                : ev.UpgradeDeltaPct <= -8
                    ? Color.FromRgb(0xEA, 0x61, 0x61)
                    : GetVerdictColor(ev.Verdict);
        var brush = UiBrushes.ForColor(col);
        string headlineVerdict = !string.IsNullOrWhiteSpace(ev.UpgradeVerdict) ? ev.UpgradeVerdict : Loc.Verdict(ev.Verdict);
        ItemVerdictText.Text = $"{headlineVerdict}  ·  {ev.ItemName}";
        ItemVerdictText.Foreground = brush;
        ItemVerdictBorder.BorderBrush = brush;

        if (!string.IsNullOrWhiteSpace(ev.UpgradeVerdict))
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
        _ = FetchLivePriceAndRefreshUiAsync(ev);
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
        if (!string.IsNullOrWhiteSpace(ev.LivePriceSummary)) lines.Add(ev.LivePriceSummary);
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
            string updated = BuildFormattedItemDetails(ev);
            ItemDetailsText.Text = updated;
            _toast.UpdateBodyIfVisible(updated);
        });
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
            var chk = new CheckBox
            {
                Content = mod.DisplayText,
                IsChecked = mod.IsDefaultChecked,
                Style = (Style)FindResource("DarkCheck"),
                FontSize = 10,
                Margin = new Thickness(0, 1, 0, 2)
            };
            ItemModChecksPanel.Children.Add(chk);
            _modFilterCheckboxes.Add((chk, mod));
        }
        ItemModFilterBox.Visibility = Visibility.Visible;
    }

    private List<ItemModFilterOption> GetCheckedMods() =>
        _modFilterCheckboxes.Where(x => x.box.IsChecked == true).Select(x => x.mod).ToList();

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

    private void OpenCommandWheel_Click(object sender, RoutedEventArgs e) => OpenCommandWheelNearCursor();

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
        _toast.ShowToast("Vendor Regex", rx, Color.FromRgb(0xD4, 0xB0, 0x6A), nearCursor: false, seconds: 3.5);
    }

    private void CopyWaystoneRegex_Click(object sender, RoutedEventArgs e)
    {
        string rx = RegexGeneratorService.BuildSafeWaystoneRegex(_profiles.ActiveProfile);
        SetClipboardSafe(rx);
        _toast.ShowToast("Safe Waystone Regex", rx, Color.FromRgb(0x4E, 0xCF, 0x8F), nearCursor: false, seconds: 3.5);
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
                Style = (Style)FindResource("BrassButton"),
                FontSize = 10,
                Padding = new Thickness(8, 3, 8, 3),
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
        var win = new PoeAccountLoginWindow(_ai, _profiles, stashModeOnly: true) { Owner = this };
        bool? ok = win.ShowDialog();
        Win32OverlayHelper.ApplyOverlayStyles(this, noActivate: !_allowTyping, clickThrough: _clickThrough);
        if (ok == true && win.ScannedStash is { Ok: true } scan)
        {
            RefreshFarmStatsUi();
            _toast.ShowToast("Stash Net-Worth", win.ResultMessage, Color.FromRgb(0x4E, 0xCF, 0x8F), nearCursor: false, seconds: 5.0);
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
            if (_initializedTabs.Contains(NavBtnUpdates))
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
        }
        else
        {
            AiStatusText.Text = $"{prov}: {Loc.T("ключ не задано", "no API key", "ключ не задан")}";
            AiStatusText.Foreground = UiBrushes.FgAmber;
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
            await Task.Delay(20);
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
        AiQuickBtn.Content = "AI …";
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
            AiMetaText.Text = "Error";
            AiResponseText.Text = ex.Message;
        }
        finally
        {
            _aiBusy = false;
            AiQuickBtn.Content = "AI F11";
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
            FilterStatusText.Text = Loc.T($"Встановлено: {Path.GetFileName(dst)} ({val.BlockCount} блоків{warnSuffix})", $"Installed: {Path.GetFileName(dst)} ({val.BlockCount} blocks{warnSuffix})", $"Установлен: {Path.GetFileName(dst)} ({val.BlockCount} блоков{warnSuffix})");
            FilterStatusText.Foreground = val.Warnings.Count > 0 ? UiBrushes.FgAmber : UiBrushes.FgEmerald;
            InstallFilterBtn.Content = Loc.T("✓ Встановлено!", "✓ Installed!", "✓ Установлено!");
            RefreshInstalledFiltersLabel();
        }
        catch
        {
            FilterStatusText.Text = "Error loading .filter";
            FilterStatusText.Foreground = UiBrushes.FgCrimson;
        }
    }

    private void ShowItemsEvalSubtab_Click(object sender, RoutedEventArgs e)
    {
        ItemsEvalPanel.Visibility = Visibility.Visible;
        LootFilterPanel.Visibility = Visibility.Collapsed;
        BtnSubItemsEval.Background = UiBrushes.BgBrassDim;
        BtnSubItemsEval.BorderBrush = UiBrushes.BorderBrass;
        BtnSubItemsEval.Foreground = UiBrushes.FgBrass;
        BtnSubLootFilter.Background = UiBrushes.BgCard;
        BtnSubLootFilter.BorderBrush = UiBrushes.BorderSubtle;
        BtnSubLootFilter.Foreground = UiBrushes.FgPrimary;
    }

    private void ShowLootFilterSubtab_Click(object sender, RoutedEventArgs e) => ShowLootFilterSubtab();

    private void ShowLootFilterSubtab()
    {
        ItemsEvalPanel.Visibility = Visibility.Collapsed;
        LootFilterPanel.Visibility = Visibility.Visible;
        BtnSubLootFilter.Background = UiBrushes.BgBrassDim;
        BtnSubLootFilter.BorderBrush = UiBrushes.BorderBrass;
        BtnSubLootFilter.Foreground = UiBrushes.FgBrass;
        BtnSubItemsEval.Background = UiBrushes.BgCard;
        BtnSubItemsEval.BorderBrush = UiBrushes.BorderSubtle;
        BtnSubItemsEval.Foreground = UiBrushes.FgPrimary;
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
            b.Background = on ? UiBrushes.BgBrassDim : UiBrushes.BgCard;
            b.BorderBrush = on ? UiBrushes.BorderBrass : UiBrushes.BorderSubtle;
            b.Foreground = on ? UiBrushes.FgBrass : UiBrushes.FgPrimary;
        }
    }

    private async void GenerateFilterWithAi_Click(object sender, RoutedEventArgs e)
    {
        if (_filterGenBusy) return;
        _filterGenBusy = true;
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
            bool hidAny = false;
            if (IsVisible) { Hide(); hidAny = true; }
            if (_miniHud.IsVisible) { _miniHud.Hide(); hidAny = true; }
            if (hidAny)
            {
                PassiveTreeService.UnpinCache();
                Win32OverlayHelper.TrimWorkingSet();
            }
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
        if (LogBtn != null && _watcher.LogPath != null) LogBtn.ToolTip = _watcher.LogPath;

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
            Win32OverlayHelper.SetWindowOpacity(this, 0.78);
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
        if (!_clickThrough) Win32OverlayHelper.SetWindowOpacity(this, _alphas[_alphaIdx]);
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

    // --- UI Builders (чисті структуровані картки без стін сирого тексту) ---
    private static void AddSectionHeader(Panel parent, string text, double topMargin = 0)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = UiBrushes.FgBrass,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, topMargin, 0, 5)
        });
    }

    private static Border CreateSimpleRowCard(string title, string subtitle)
    {
        var st = new StackPanel();
        st.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = UiBrushes.FgPrimary,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            st.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = UiBrushes.FgMuted,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                LineHeight = 15
            });
        }
        return new Border
        {
            Background = UiBrushes.BgCard,
            BorderBrush = UiBrushes.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 5),
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
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 3, 8, 3),
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
                Background = UiBrushes.BgWindow,
                BorderBrush = UiBrushes.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 2, 4, 2),
                Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = supInfo.Name, Foreground = UiBrushes.FgPrimary, FontSize = 10.5 },
                        new TextBlock { Text = $" · {tierShort} · {Loc.LevelShort}{supInfo.MinUnlockCharacterLevel}+", Foreground = UiBrushes.FgMuted, FontSize = 9, VerticalAlignment = VerticalAlignment.Center }
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
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(0, 0, 0, 5),
            Child = wrap
        };
    }

    private Border CreateGearTargetCard(string rawSlotId, string slotName, string localizedBody, string rawEnglishText)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var obt = Poe2KnowledgeDb.ResolveItemObtain(rawEnglishText, rawSlotId, _profiles.ActiveProfile);
        var st = new StackPanel();
        st.Children.Add(new TextBlock
        {
            Text = slotName,
            Foreground = UiBrushes.FgBrass,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11
        });
        if (!string.IsNullOrWhiteSpace(localizedBody))
        {
            st.Children.Add(new TextBlock
            {
                Text = localizedBody,
                Foreground = UiBrushes.FgPrimary,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                LineHeight = 15
            });
        }
        st.Children.Add(new TextBlock
        {
            Text = $"{Loc.T("Джерело:", "Drop/Get:", "Источник:")} {obt.MinItemOrAreaLevel} — {Loc.T(obt.ObtainSourceUk, obt.ObtainSourceEn, obt.ObtainSourceRu)}",
            Foreground = UiBrushes.FgEmerald,
            FontSize = 9.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        grid.Children.Add(st);

        var actions = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var infoBtn = new Button
        {
            Content = Loc.T("Опис", "Guide", "Инфо"),
            Style = (Style)FindResource("FlatButton"),
            Foreground = UiBrushes.FgBrass,
            FontSize = 9.5,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 0, 4)
        };
        infoBtn.Click += (_, _) => ShowGearInspector(rawSlotId, slotName, rawEnglishText);
        actions.Children.Add(infoBtn);

        string searchBase = rawEnglishText.Split(['\n', '|'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(searchBase) && searchBase.Length <= 48 && !searchBase.Contains('%'))
        {
            var tradeBtn = new Button
            {
                Content = "Trade",
                Style = (Style)FindResource("FlatButton"),
                Foreground = UiBrushes.FgSteel,
                FontSize = 9.5,
                Padding = new Thickness(8, 2, 8, 2)
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
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 5),
            Child = grid
        };
    }

    private static string ShortStageTitle(string name, int maxLen)
    {
        string first = name.Split(" - ")[0].Trim();
        return first.Length > maxLen ? first[..(maxLen - 1)] + "…" : first;
    }
}
