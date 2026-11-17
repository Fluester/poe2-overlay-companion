using System.Net;
using System.Net.Http;
using System.Text.Json;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Оцінює Net-Worth вкладок скрині PoE 2 через cookie/сесію без перенаправлення видимого вікна на сайт PoE 1
/// та живі обмінні коефіцієнти Trade 2 (Exalted / Divine / базові орби).
/// </summary>
public static class StashTrackerService
{
    // Значення у відносних частках від 1 Divine Orb (за винятком Divine/Exalted, які рахуються від живого курсу)
    private static readonly Dictionary<string, double> BaseValueInExalted = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Exalted Orb"] = 1.0,
        ["Chaos Orb"] = 3.5,
        ["Vaal Orb"] = 1.8,
        ["Orb of Alchemy"] = 1.2,
        ["Orb of Chance"] = 6.0,
        ["Regal Orb"] = 0.6,
        ["Artificer's Orb"] = 0.8,
        ["Gemcutter's Prism"] = 2.5,
        ["Glassblower's Bauble"] = 1.5,
        ["Arcanist's Etcher"] = 2.0,
        ["Armourer's Scrap"] = 0.25,
        ["Blacksmith's Whetstone"] = 0.25,
        ["Orb of Transmutation"] = 0.1,
        ["Orb of Augmentation"] = 0.1,
        ["Greater Orb of Transmutation"] = 0.4,
        ["Perfect Orb of Transmutation"] = 1.0,
        ["Greater Orb of Augmentation"] = 0.4,
        ["Perfect Orb of Augmentation"] = 1.0,
        ["Lesser Jeweller's Orb"] = 1.0,
        ["Greater Jeweller's Orb"] = 18.0,
        ["Perfect Jeweller's Orb"] = 85.0,
        ["Orb of Annulment"] = 22.0,
        ["Fracturing Orb"] = 65.0
    };

    public static string BuildFetchStashScript(string league, int maxTabs = 4)
    {
        string safeLeague = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim());
        return $$"""
(async () => {
  try {
    const lg = encodeURIComponent({{safeLeague}});
    const first = await fetch('/character-window/get-stash-items?realm=poe2&league=' + lg + '&tabs=1&tabIndex=0', { credentials: 'include' });
    const ct = (first.headers.get('content-type') || '').toLowerCase();
    if (!first.ok || !ct.includes('json')) {
      return JSON.stringify({
        ok: false,
        needLoginHost: 'pathofexile.com',
        error: 'HTTP ' + first.status + ' — увійди на www.pathofexile.com у вікні нижче й повтори скан скрині.'
      });
    }
    const d0 = await first.json();
    const numTabs = Math.min({{maxTabs}}, Number(d0.numTabs || 1));
    const allItems = Array.isArray(d0.items) ? [...d0.items] : [];

    for (let i = 1; i < numTabs; i++) {
      const r = await fetch('/character-window/get-stash-items?realm=poe2&league=' + lg + '&tabs=0&tabIndex=' + i, { credentials: 'include' });
      if (r.ok) {
        const di = await r.json();
        if (Array.isArray(di.items)) allItems.push(...di.items);
      }
    }

    const counts = {};
    for (const it of allItems) {
      const name = (it.typeLine || it.baseType || it.name || '').trim();
      if (!name) continue;
      const qty = Math.max(1, Number(it.stackSize || 1));
      counts[name] = (counts[name] || 0) + qty;
    }
    return JSON.stringify({ ok: true, tabsScanned: numTabs, counts });
  } catch (e) {
    return JSON.stringify({ ok: false, error: String(e) });
  }
})();
""";
    }

    public static StashScanResult EvaluateCounts(string league, int tabsScanned, IReadOnlyDictionary<string, int> counts, double exPerDivine)
    {
        double ratio = exPerDivine > 5 ? exPerDivine : 120.0;
        var priced = new List<(string name, int count, double exTotal)>();
        double sumEx = 0;
        int totalItems = 0;

        foreach (var (name, qty) in counts)
        {
            if (qty <= 0) continue;
            totalItems += qty;
            double unitEx = 0;

            if (name.Equals("Divine Orb", StringComparison.OrdinalIgnoreCase)) unitEx = ratio;
            else if (name.Equals("Mirror of Kalandra", StringComparison.OrdinalIgnoreCase)) unitEx = ratio * 650;
            else if (name.Equals("Hinekora's Lock", StringComparison.OrdinalIgnoreCase)) unitEx = ratio * 35;
            else if (BaseValueInExalted.TryGetValue(name, out double knownEx)) unitEx = knownEx;
            else if (name.Contains("Soul Core", StringComparison.OrdinalIgnoreCase)) unitEx = 8.0;
            else if (name.Contains("Omen of", StringComparison.OrdinalIgnoreCase)) unitEx = 15.0;
            else if (name.Contains("Splinter", StringComparison.OrdinalIgnoreCase)) unitEx = 0.6;
            else if (name.Contains("Distilled", StringComparison.OrdinalIgnoreCase)) unitEx = 3.0;
            else if (name.Contains("Catalyst", StringComparison.OrdinalIgnoreCase)) unitEx = 1.5;

            if (unitEx > 0)
            {
                double lineEx = unitEx * qty;
                sumEx += lineEx;
                priced.Add((name, qty, lineEx));
            }
        }

        var top = priced.OrderByDescending(x => x.exTotal)
            .Take(10)
            .Select(x => $"{x.count}× {x.name} (~{x.exTotal:0.#} Ex)")
            .ToList();

        return new StashScanResult
        {
            Ok = true,
            League = league,
            TabsScanned = tabsScanned,
            CurrencyItemsCount = totalItems,
            TotalExalted = Math.Round(sumEx, 1),
            TotalDivine = Math.Round(sumEx / ratio, 2),
            ExPerDivine = ratio,
            BreakdownLines = top,
            ScannedUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Вичитує предмети з JSON-відповіді `/character-window/get-stash-items?realm=poe2` у словник кількостей.
    /// </summary>
    public static bool TryAccumulateStashTabJson(string json, Dictionary<string, int> counts, out int numTabs)
    {
        numTabs = 1;
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{", StringComparison.Ordinal))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out _)) return false;
            if (doc.RootElement.TryGetProperty("numTabs", out var nt) && nt.ValueKind == JsonValueKind.Number)
                numTabs = Math.Max(1, nt.GetInt32());
            if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return doc.RootElement.TryGetProperty("numTabs", out _);

            foreach (var it in arr.EnumerateArray())
            {
                string typeLine = it.TryGetProperty("typeLine", out var tl) ? tl.GetString() ?? "" : "";
                string baseType = it.TryGetProperty("baseType", out var bt) ? bt.GetString() ?? "" : "";
                string name = it.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                string resolved = !string.IsNullOrWhiteSpace(typeLine) ? typeLine
                    : !string.IsNullOrWhiteSpace(baseType) ? baseType : name;
                if (string.IsNullOrWhiteSpace(resolved)) continue;
                int qty = it.TryGetProperty("stackSize", out var st) && st.ValueKind == JsonValueKind.Number
                    ? Math.Max(1, st.GetInt32())
                    : 1;
                counts[resolved.Trim()] = counts.GetValueOrDefault(resolved.Trim()) + qty;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Завантажує скриню PoE 2 напряму через HttpClient з cookie з WebView2 (без показу PoE 1 сайту користувачу).
    /// </summary>
    public static async Task<(bool Ok, bool NeedBrowserFallback, StashScanResult? Scan, string Error)> FetchStashDirectAsync(
        string league,
        IReadOnlyList<(string Domain, string Name, string Value)> cookies,
        string userAgent,
        double exPerDiv,
        int maxTabs = 4,
        CancellationToken ct = default)
    {
        if (cookies.Count == 0)
            return (false, true, null, "No session cookies yet.");

        try
        {
            var jar = new CookieContainer();
            foreach (var (domain, name, value) in cookies)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                try
                {
                    string hostUri = domain.Contains("pathofexile2.com", StringComparison.OrdinalIgnoreCase)
                        ? "https://pathofexile2.com"
                        : "https://www.pathofexile.com";
                    jar.Add(new Uri(hostUri), new Cookie(name, value ?? "", "/", domain.TrimStart('.')));
                }
                catch { }
            }

            using var handler = new HttpClientHandler
            {
                CookieContainer = jar,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                string.IsNullOrWhiteSpace(userAgent) ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" : userAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://pathofexile2.com/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

            string lg = Uri.EscapeDataString(string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim());
            string[] bases = ["https://www.pathofexile.com", "https://pathofexile2.com"];
            string? workingBase = null;
            string? firstBody = null;

            foreach (string b in bases)
            {
                using var r0 = await client.GetAsync($"{b}/character-window/get-stash-items?realm=poe2&league={lg}&tabs=1&tabIndex=0", ct);
                if (!r0.IsSuccessStatusCode) continue;
                string body = await r0.Content.ReadAsStringAsync(ct);
                if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    workingBase = b;
                    firstBody = body;
                    break;
                }
            }

            if (workingBase == null || firstBody == null)
                return (false, true, null, "Direct Stash API requires WebView2 session.");

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!TryAccumulateStashTabJson(firstBody, counts, out int totalTabs))
                return (false, true, null, "Stash JSON parse failed.");

            int tabsToScan = Math.Clamp(Math.Min(maxTabs, totalTabs), 1, maxTabs);
            for (int i = 1; i < tabsToScan; i++)
            {
                using var ri = await client.GetAsync($"{workingBase}/character-window/get-stash-items?realm=poe2&league={lg}&tabs=0&tabIndex={i}", ct);
                if (!ri.IsSuccessStatusCode) break;
                string bi = await ri.Content.ReadAsStringAsync(ct);
                TryAccumulateStashTabJson(bi, counts, out _);
            }

            return (true, false, EvaluateCounts(league, tabsToScan, counts, exPerDiv), "");
        }
        catch (Exception ex)
        {
            return (false, true, null, ex.Message);
        }
    }
}
