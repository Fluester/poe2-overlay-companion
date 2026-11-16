using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion;

public partial class MobalyticsImportWindow : Window
{
    private readonly BuildProfileManager _profiles;
    private readonly string _customName;
    private readonly string _initialUrl;
    private bool _busy;

    public BuildProfile? ImportedProfile { get; private set; }
    public string ResultMessage { get; private set; } = "";

    public MobalyticsImportWindow(BuildProfileManager profiles, string initialUrl, string customName = "")
    {
        InitializeComponent();
        _profiles = profiles;
        _customName = customName;
        _initialUrl = MobalyticsImportService.NormalizeMobalyticsUrl(initialUrl);
        UrlBox.Text = _initialUrl;

        Title = Loc.T("Прямий імпорт із Mobalytics — PoE 2", "Direct Mobalytics Import — PoE 2", "Прямой импорт с Mobalytics — PoE 2");
        BtnNavigate.Content = Loc.T("Відкрити", "Go", "Открыть");
        BtnImportCurrent.Content = Loc.T("Імпорт поточного варіанта", "Import Current Variant", "Импорт текущего варианта");
        BtnImportAll.Content = Loc.T("Імпорт ВСІХ варіантів (етапи)", "Import ALL Variants (Stages)", "Импорт ВСЕХ вариантов (этапы)");
        BtnClipFallback.Content = Loc.T("З буфера (Ctrl+A Ctrl+C)", "From Clipboard (Ctrl+A Ctrl+C)", "Из буфера (Ctrl+A Ctrl+C)");
        StatusText.Text = Loc.T(
            "Відкрий сторінку білду на mobalytics.gg, зачекай завантаження (або пройди перевірку, якщо з'явиться) та натисни «Імпорт».",
            "Open a Mobalytics PoE 2 build page, let it finish loading, then click Import Current or Import ALL Variants.",
            "Открой страницу билда на mobalytics.gg, дождись загрузки и нажми «Импорт».");

        Loaded += MobalyticsImportWindow_Loaded;
        Closed += (_, _) => { try { WebView.Dispose(); } catch { } };
    }

    private async void MobalyticsImportWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(version))
            {
                StatusText.Text = Loc.T("Edge WebView2 Runtime не знайдено — скористайся кнопкою «З буфера (Ctrl+A Ctrl+C)».", "Edge WebView2 Runtime not found — use 'From Clipboard (Ctrl+A Ctrl+C)'.", "Edge WebView2 Runtime не найден — используй кнопку «Из буфера».");
                return;
            }

            string userData = AppPaths.GetWebView2CacheDirectory();

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await WebView.EnsureCoreWebView2Async(env);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.NavigationCompleted += (_, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UrlBox.Text = WebView.Source?.ToString() ?? UrlBox.Text;
                    if (args.IsSuccess)
                        StatusText.Text = Loc.T("Сторінку завантажено. Обери «Імпорт поточного» або «Імпорт ВСІХ варіантів».", "Page ready. Click Import Current Variant or Import ALL Variants.", "Страница загружена. Выбери «Импорт текущего» или «Импорт ВСЕХ вариантов».");
                });
            };
            WebView.CoreWebView2.Navigate(_initialUrl);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"WebView2 error: {ex.Message}";
        }
    }

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Navigate_Click(sender, e);
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 == null) return;
        string target = MobalyticsImportService.NormalizeMobalyticsUrl(UrlBox.Text);
        WebView.CoreWebView2.Navigate(target);
    }

    private async void ImportCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || WebView.CoreWebView2 == null) return;
        _busy = true;
        try
        {
            StatusText.Text = Loc.T("Читаю білд зі сторінки Mobalytics...", "Extracting build from Mobalytics DOM...", "Читаю билд со страницы Mobalytics...");
            var scraped = await ExecuteScrapeAsync();
            if (scraped == null)
            {
                StatusText.Text = Loc.T("Не вдалося розібрати сторінку. Переконайся, що гайд повністю відкритий.", "Failed to parse page. Make sure the build guide finished loading.", "Не удалось разобрать страницу. Убедись, что гайд открыт.");
                return;
            }

            var (ok, msg, profile) = MobalyticsImportService.ImportFromScrapedVariants(_profiles, [scraped], _customName);
            ResultMessage = msg;
            StatusText.Text = msg;
            if (ok && profile != null)
            {
                ImportedProfile = profile;
                DialogResult = true;
                Close();
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async void ImportAllVariants_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || WebView.CoreWebView2 == null) return;
        _busy = true;
        try
        {
            StatusText.Text = Loc.T("Сканую всі варіанти білду (етапи прокачки -> ендгейм)...", "Scanning all build variants (leveling -> endgame)...", "Сканирую все варианты билда (прокачка -> эндгейм)...");
            var first = await ExecuteScrapeAsync();
            if (first == null)
            {
                StatusText.Text = Loc.T("Сторінка ще не готова для зчитування.", "Page is not ready to scrape yet.", "Страница ещё не готова.");
                return;
            }

            var captured = new List<MobalyticsScrapedVariant>();
            int totalTabs = Math.Clamp(first.variantNames.Count, 0, 14);
            if (totalTabs <= 1)
            {
                captured.Add(first);
            }
            else
            {
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < totalTabs; i++)
                {
                    StatusText.Text = Loc.T($"Читаю варіант {i + 1}/{totalTabs}: {first.variantNames[i]}...", $"Capturing variant {i + 1}/{totalTabs}: {first.variantNames[i]}...", $"Читаю вариант {i + 1}/{totalTabs}: {first.variantNames[i]}...");
                    await WebView.CoreWebView2.ExecuteScriptAsync(MobalyticsImportService.BuildClickVariantScript(i));
                    await Task.Delay(750);
                    var v = await ExecuteScrapeAsync();
                    if (v != null && seenKeys.Add($"{v.variantName}|{v.pobCode.Length}"))
                        captured.Add(v);
                }
            }

            if (captured.Count == 0) captured.Add(first);

            var (ok, msg, profile) = MobalyticsImportService.ImportFromScrapedVariants(_profiles, captured, _customName);
            ResultMessage = msg;
            StatusText.Text = msg;
            if (ok && profile != null)
            {
                ImportedProfile = profile;
                DialogResult = true;
                Close();
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void ImportFromClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string clip = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            var (ok, msg, profile) = MobalyticsImportService.ParseClipboardGuideText(_profiles, clip, _customName, UrlBox.Text.Trim());
            ResultMessage = msg;
            StatusText.Text = msg;
            if (ok && profile != null)
            {
                ImportedProfile = profile;
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task<MobalyticsScrapedVariant?> ExecuteScrapeAsync()
    {
        try
        {
            string rawJson = await WebView.CoreWebView2.ExecuteScriptAsync(MobalyticsImportService.ScrapeDomScript);
            if (string.IsNullOrWhiteSpace(rawJson) || rawJson == "null") return null;

            // WebView2 повертає JSON-рядок, загорнутий у ще одні лапки
            string unescaped = rawJson.StartsWith("\"")
                ? JsonSerializer.Deserialize<string>(rawJson) ?? ""
                : rawJson;

            if (string.IsNullOrWhiteSpace(unescaped)) return null;
            return JsonSerializer.Deserialize<MobalyticsScrapedVariant>(unescaped, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}
