using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Живий сервіс ліг і цін Path of Exile 2 (офіційний публічний Trade 2 Search + Fetch API без API-ключа)
/// із 5-хвилинним кешем для миттєвого Ctrl+C прайс-чеку.
/// </summary>
public static class PoeTradePriceService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<string, (DateTime cachedUtc, string summary)> PriceCache = new();
    private static List<string> _cachedLeagues = ["Forbidden Rites", "HC Forbidden Rites", "Runes of Aldur", "HC Runes of Aldur", "Standard", "Hardcore"];

    static PoeTradePriceService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Poe2OverlayCompanion/1.0");
    }

    public static IReadOnlyList<string> DefaultLeagues => _cachedLeagues;

    public static async Task<List<string>> FetchLeaguesAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync("https://www.pathofexile.com/api/trade2/data/leagues", ct);
            if (!resp.IsSuccessStatusCode) return [.. _cachedLeagues];

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var fetched = new List<string>();
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.TryGetProperty("id", out var idEl))
                    {
                        string? id = idEl.GetString();
                        if (!string.IsNullOrWhiteSpace(id)) fetched.Add(id);
                    }
                }
                if (fetched.Count > 0)
                    _cachedLeagues = fetched;
            }
        }
        catch { }
        return [.. _cachedLeagues];
    }

    public static async Task<string> EstimateLivePriceAsync(ItemEvaluation ev, string league, CancellationToken ct = default)
    {
        if (!ev.IsPoeItem) return "";
        string safeLeague = string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim();
        string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{safeLeague}|{ev.ItemName}|{ev.ItemClass}|{(int)ev.TotalDps}|{(int)ev.EnergyShield}|{(int)ev.MovementSpeed}|{ev.PlusSkillLevel}|{(int)ev.SpiritBonus}")));

        if (PriceCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.cachedUtc).TotalMinutes < 5)
            return cached.summary;

        try
        {
            string queryJson = BuildPriceCheckPayload(ev);
            string url = $"https://www.pathofexile.com/api/trade2/search/{Uri.EscapeDataString(safeLeague)}";
            using var content = new StringContent(queryJson, Encoding.UTF8, "application/json");
            using var sResp = await Http.PostAsync(url, content, ct);
            if (!sResp.IsSuccessStatusCode) return "";

            string sJson = await sResp.Content.ReadAsStringAsync(ct);
            using var sDoc = JsonDocument.Parse(sJson);
            string? queryId = sDoc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            int total = sDoc.RootElement.TryGetProperty("total", out var totEl) ? totEl.GetInt32() : 0;

            if (string.IsNullOrEmpty(queryId) || !sDoc.RootElement.TryGetProperty("result", out var resArr) || resArr.GetArrayLength() == 0)
            {
                string emptyMsg = Loc.T($"Trade 2 ({safeLeague}): лотів не знайдено (0)", $"Trade 2 ({safeLeague}): no listings found", $"Trade 2 ({safeLeague}): лотов не найдено (0)");
                PriceCache[cacheKey] = (DateTime.UtcNow, emptyMsg);
                return emptyMsg;
            }

            var topIds = new List<string>(5);
            foreach (var item in resArr.EnumerateArray())
            {
                string? s = item.GetString();
                if (!string.IsNullOrEmpty(s)) topIds.Add(s);
                if (topIds.Count >= 5) break;
            }

            string fetchUrl = $"https://www.pathofexile.com/api/trade2/fetch/{string.Join(",", topIds)}?query={Uri.EscapeDataString(queryId)}";
            using var fResp = await Http.GetAsync(fetchUrl, ct);
            if (!fResp.IsSuccessStatusCode)
            {
                string countOnly = $"Trade 2 ({safeLeague}): ~{total} {Loc.T("лотів", "listings", "лотов")}";
                PriceCache[cacheKey] = (DateTime.UtcNow, countOnly);
                return countOnly;
            }

            string fJson = await fResp.Content.ReadAsStringAsync(ct);
            using var fDoc = JsonDocument.Parse(fJson);
            var prices = new List<(double amount, string currency)>();
            if (fDoc.RootElement.TryGetProperty("result", out var fArr))
            {
                foreach (var entry in fArr.EnumerateArray())
                {
                    if (entry.TryGetProperty("listing", out var lst) &&
                        lst.TryGetProperty("price", out var pr) &&
                        pr.TryGetProperty("amount", out var amt) &&
                        pr.TryGetProperty("currency", out var cur))
                    {
                        prices.Add((amt.GetDouble(), cur.GetString() ?? "exalted"));
                    }
                }
            }

            string summary;
            if (prices.Count > 0)
            {
                string priceRange = prices.Count >= 2 && prices[0] != prices[^1]
                    ? $"{prices[0].amount:0.#} {prices[0].currency} – {prices[^1].amount:0.#} {prices[^1].currency}"
                    : $"~{prices[0].amount:0.#} {prices[0].currency}";
                summary = $"Trade 2 ({safeLeague}): {priceRange} ({total})";
            }
            else
            {
                summary = $"Trade 2 ({safeLeague}): {total} {Loc.T("лотів", "listings", "лотов")}";
            }

            PriceCache[cacheKey] = (DateTime.UtcNow, summary);
            return summary;
        }
        catch
        {
            return "";
        }
    }

    public static string BuildCustomTradeQueryJson(
        ItemEvaluation ev,
        IReadOnlyList<ItemModFilterOption> selectedMods,
        bool includeItemCategory = true)
    {
        var filters = new Dictionary<string, object>();
        if (includeItemCategory && !ev.Rarity.Equals("Unique", StringComparison.OrdinalIgnoreCase))
        {
            string cat = TradeLinkBuilder.MapItemClassToTradeCategory(ev.ItemClass);
            if (!string.IsNullOrEmpty(cat))
                filters["type_filters"] = new { filters = new { category = new { option = cat } } };
        }

        var equip = new Dictionary<string, object>();
        var statFilters = new List<object>();

        foreach (var mod in selectedMods)
        {
            if (mod.TradeStatId == "equip.dps") equip["dps"] = new { min = mod.MinSuggested };
            else if (mod.TradeStatId == "equip.es") equip["es"] = new { min = mod.MinSuggested };
            else if (mod.TradeStatId == "equip.ev") equip["evasion"] = new { min = mod.MinSuggested };
            else if (mod.TradeStatId == "equip.ar") equip["ar"] = new { min = mod.MinSuggested };
            else if (!string.IsNullOrWhiteSpace(mod.TradeStatId))
                statFilters.Add(new { id = mod.TradeStatId, value = new { min = mod.MinSuggested } });
        }

        if (equip.Count > 0)
            filters["equipment_filters"] = new { filters = equip };

        object query = ev.Rarity.Equals("Unique", StringComparison.OrdinalIgnoreCase)
            ? new
            {
                status = new { option = "securable" },
                name = ev.ItemName.Split(" — ")[0].Trim(),
                stats = statFilters.Count > 0 ? new[] { new { type = "and", filters = statFilters } } : null
            }
            : new
            {
                status = new { option = "securable" },
                filters = filters.Count > 0 ? filters : null,
                stats = statFilters.Count > 0 ? new[] { new { type = "and", filters = statFilters } } : null
            };

        return JsonSerializer.Serialize(new { query, sort = new { price = "asc" } },
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    }

    public static async Task<string> EstimateCustomPriceAsync(
        ItemEvaluation ev,
        string league,
        IReadOnlyList<ItemModFilterOption> selectedMods,
        bool includeItemCategory = true,
        CancellationToken ct = default)
    {
        if (!ev.IsPoeItem) return "";
        string safeLeague = string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim();
        string modSig = string.Join(",", selectedMods.Select(m => $"{m.TradeStatId}:{m.MinSuggested}"));
        string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"CUSTOM|{safeLeague}|{ev.ItemName}|{ev.ItemClass}|{includeItemCategory}|{modSig}")));

        if (PriceCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.cachedUtc).TotalMinutes < 3)
            return cached.summary;

        try
        {
            string queryJson = BuildCustomTradeQueryJson(ev, selectedMods, includeItemCategory);
            string url = $"https://www.pathofexile.com/api/trade2/search/{Uri.EscapeDataString(safeLeague)}";
            using var content = new StringContent(queryJson, Encoding.UTF8, "application/json");
            using var sResp = await Http.PostAsync(url, content, ct);
            if (!sResp.IsSuccessStatusCode) return "";

            string sJson = await sResp.Content.ReadAsStringAsync(ct);
            using var sDoc = JsonDocument.Parse(sJson);
            string? queryId = sDoc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            int total = sDoc.RootElement.TryGetProperty("total", out var totEl) ? totEl.GetInt32() : 0;

            if (string.IsNullOrEmpty(queryId) || !sDoc.RootElement.TryGetProperty("result", out var resArr) || resArr.GetArrayLength() == 0)
            {
                string emptyMsg = Loc.T($"Trade 2 ({safeLeague}): 0 лотів під вибрані моди", $"Trade 2 ({safeLeague}): 0 listings for selected mods", $"Trade 2 ({safeLeague}): 0 лотов по модам");
                PriceCache[cacheKey] = (DateTime.UtcNow, emptyMsg);
                return emptyMsg;
            }

            var topIds = new List<string>(5);
            foreach (var item in resArr.EnumerateArray())
            {
                string? s = item.GetString();
                if (!string.IsNullOrEmpty(s)) topIds.Add(s);
                if (topIds.Count >= 5) break;
            }

            string fetchUrl = $"https://www.pathofexile.com/api/trade2/fetch/{string.Join(",", topIds)}?query={Uri.EscapeDataString(queryId)}";
            using var fResp = await Http.GetAsync(fetchUrl, ct);
            if (!fResp.IsSuccessStatusCode) return $"Trade 2 ({safeLeague}): {total}";

            string fJson = await fResp.Content.ReadAsStringAsync(ct);
            using var fDoc = JsonDocument.Parse(fJson);
            var prices = new List<(double amount, string currency)>();
            if (fDoc.RootElement.TryGetProperty("result", out var fArr))
            {
                foreach (var entry in fArr.EnumerateArray())
                {
                    if (entry.TryGetProperty("listing", out var lst) &&
                        lst.TryGetProperty("price", out var pr) &&
                        pr.TryGetProperty("amount", out var amt) &&
                        pr.TryGetProperty("currency", out var cur))
                    {
                        prices.Add((amt.GetDouble(), cur.GetString() ?? "exalted"));
                    }
                }
            }

            string summary = prices.Count > 0
                ? $"Trade 2 ({safeLeague}): {(prices.Count >= 2 && prices[0] != prices[^1] ? $"{prices[0].amount:0.#} {prices[0].currency} – {prices[^1].amount:0.#} {prices[^1].currency}" : $"~{prices[0].amount:0.#} {prices[0].currency}")} ({total})"
                : $"Trade 2 ({safeLeague}): {total}";

            PriceCache[cacheKey] = (DateTime.UtcNow, summary);
            return summary;
        }
        catch
        {
            return "";
        }
    }

    public static async Task<double> EstimateExaltedPerDivineAsync(string league, CancellationToken ct = default)
    {
        try
        {
            string safeLeague = string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim();
            string url = $"https://www.pathofexile.com/api/trade2/exchange/{Uri.EscapeDataString(safeLeague)}";
            var payload = JsonSerializer.Serialize(new
            {
                exchange = new { status = new { option = "online" }, have = new[] { "exalted" }, want = new[] { "divine" } }
            });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode) return 120.0;

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var resObj) && resObj.ValueKind == JsonValueKind.Object)
            {
                var ratios = new List<double>();
                foreach (var prop in resObj.EnumerateObject().Take(10))
                {
                    if (prop.Value.TryGetProperty("listing", out var lst) &&
                        lst.TryGetProperty("offers", out var offers) && offers.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var of in offers.EnumerateArray())
                        {
                            if (of.TryGetProperty("exchange", out var exch) && of.TryGetProperty("item", out var itm) &&
                                exch.TryGetProperty("amount", out var exAmt) && itm.TryGetProperty("amount", out var itAmt))
                            {
                                double exVal = exAmt.GetDouble();
                                double divVal = itAmt.GetDouble();
                                if (divVal > 0 && exVal > 0)
                                    ratios.Add(exVal / divVal);
                            }
                        }
                    }
                }
                if (ratios.Count > 0)
                {
                    ratios.Sort();
                    return Math.Round(ratios[ratios.Count / 2], 1);
                }
            }
        }
        catch { }
        return 120.0;
    }

    private static string BuildPriceCheckPayload(ItemEvaluation ev)
    {
        if (ev.Rarity.Equals("Unique", StringComparison.OrdinalIgnoreCase))
        {
            string uniqName = ev.ItemName.Split(" — ")[0].Trim();
            return JsonSerializer.Serialize(new
            {
                query = new { status = new { option = "securable" }, name = uniqName },
                sort = new { price = "asc" }
            });
        }

        string cat = TradeLinkBuilder.MapItemClassToTradeCategory(ev.ItemClass);
        var filters = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(cat))
            filters["type_filters"] = new { filters = new { category = new { option = cat } } };

        var equip = new Dictionary<string, object>();
        if (ev.TotalDps >= 120) equip["dps"] = new { min = (int)(ev.TotalDps * 0.85) };
        if (ev.EnergyShield >= 120) equip["es"] = new { min = (int)(ev.EnergyShield * 0.85) };
        if (ev.Evasion >= 150) equip["evasion"] = new { min = (int)(ev.Evasion * 0.85) };
        if (ev.Armour >= 180) equip["ar"] = new { min = (int)(ev.Armour * 0.85) };
        if (equip.Count > 0) filters["equipment_filters"] = new { filters = equip };

        var statFilters = new List<object>();
        if (ev.MovementSpeed >= 20)
            statFilters.Add(new { id = "explicit.stat_2250533757", value = new { min = Math.Max(20, (int)ev.MovementSpeed) } });
        if (ev.SpiritBonus >= 20)
            statFilters.Add(new { id = "explicit.stat_3981240776", value = new { min = Math.Max(20, (int)ev.SpiritBonus - 5) } });

        if (statFilters.Count > 0 || filters.ContainsKey("equipment_filters"))
        {
            return JsonSerializer.Serialize(new
            {
                query = new
                {
                    status = new { option = "securable" },
                    filters,
                    stats = new[] { new { type = "and", filters = statFilters } }
                },
                sort = new { price = "asc" }
            });
        }

        return JsonSerializer.Serialize(new
        {
            query = new
            {
                status = new { option = "securable" },
                type = ev.RawBaseType
            },
            sort = new { price = "asc" }
        });
    }
}
