using System.Text.Json;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Оцінює Net-Worth валюти, орбів та уламків скрині PoE 2 за живими обмінними коефіцієнтами Trade 2 (Exalted / Divine / базові орби).
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
}
