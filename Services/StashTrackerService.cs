using System.Text.Json;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Оцінює Net-Worth вкладок скрині (аналог Stash Tracker у PoE Overlay 2) через офіційну сесію pathofexile.com у WebView2
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
    if (!first.ok) {
      return JSON.stringify({ ok: false, error: 'HTTP ' + first.status + ' — please sign in to pathofexile.com first.' });
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
}
