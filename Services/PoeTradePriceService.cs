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
    private static readonly ConcurrentDictionary<string, (DateTime cachedUtc, string summary, List<TradePriceListing> listings)> PriceCache = new();
    private static readonly ConcurrentDictionary<string, (DateTime cachedUtc, double exPerDiv, double exPerChaos)> RateCache = new();
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
        {
            ev.LiveListings = [.. cached.listings];
            return cached.summary;
        }

        string queryJson = BuildPriceCheckPayload(ev);
        return await ExecuteTradeSearchAndFetchAsync(ev, safeLeague, queryJson, cacheKey, isCustom: false, ct);
    }

    private static async Task<string> ExecuteTradeSearchAndFetchAsync(
        ItemEvaluation ev,
        string safeLeague,
        string queryJson,
        string cacheKey,
        bool isCustom,
        CancellationToken ct)
    {
        try
        {
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
                string emptyMsg = isCustom
                    ? Loc.T($"Trade 2 ({safeLeague}): 0 лотів під вибрані моди", $"Trade 2 ({safeLeague}): 0 listings for selected mods", $"Trade 2 ({safeLeague}): 0 лотов по модам")
                    : Loc.T($"Trade 2 ({safeLeague}): лотів не знайдено (0)", $"Trade 2 ({safeLeague}): no listings found", $"Trade 2 ({safeLeague}): лотов не найдено (0)");
                PriceCache[cacheKey] = (DateTime.UtcNow, emptyMsg, []);
                ev.LiveListings = [];
                return emptyMsg;
            }

            var topIds = new List<string>(8);
            foreach (var item in resArr.EnumerateArray())
            {
                string? s = item.GetString();
                if (!string.IsNullOrEmpty(s)) topIds.Add(s);
                if (topIds.Count >= 8) break;
            }

            string fetchUrl = $"https://www.pathofexile.com/api/trade2/fetch/{string.Join(",", topIds)}?query={Uri.EscapeDataString(queryId)}";
            using var fResp = await Http.GetAsync(fetchUrl, ct);
            if (!fResp.IsSuccessStatusCode)
            {
                string countOnly = $"Trade 2 ({safeLeague}): ~{total} {Loc.T("лотів", "listings", "лотов")}";
                PriceCache[cacheKey] = (DateTime.UtcNow, countOnly, []);
                return countOnly;
            }

            string fJson = await fResp.Content.ReadAsStringAsync(ct);
            using var fDoc = JsonDocument.Parse(fJson);
            var listings = new List<TradePriceListing>();
            if (fDoc.RootElement.TryGetProperty("result", out var fArr))
            {
                foreach (var entry in fArr.EnumerateArray())
                {
                    if (!entry.TryGetProperty("listing", out var lst) || !lst.TryGetProperty("price", out var pr)) continue;
                    if (!pr.TryGetProperty("amount", out var amt) || !pr.TryGetProperty("currency", out var cur)) continue;
                    string age = FormatListingAge(lst.TryGetProperty("indexed", out var idxEl) ? idxEl.GetString() : null);
                    string acc = lst.TryGetProperty("account", out var accEl) && accEl.TryGetProperty("name", out var nEl)
                        ? nEl.GetString() ?? ""
                        : "";
                    listings.Add(new TradePriceListing(amt.GetDouble(), cur.GetString() ?? "exalted", age, acc));
                }
            }

            string summary;
            if (listings.Count > 0)
            {
                var first = listings[0];
                var last = listings[^1];
                string priceRange = listings.Count >= 2 && (first.Amount != last.Amount || first.Currency != last.Currency)
                    ? $"{first.Amount:0.#} {first.Currency} – {last.Amount:0.#} {last.Currency}"
                    : $"~{first.Amount:0.#} {first.Currency}";
                string ages = string.Join("/", listings.Take(3).Select(l => l.AgeText).Where(a => a.Length > 0));
                string ageNote = ages.Length > 0 ? $" · {ages}" : "";
                summary = $"Trade 2 ({safeLeague}): {priceRange} ({total}{ageNote})";
            }
            else
            {
                summary = $"Trade 2 ({safeLeague}): {total} {Loc.T("лотів", "listings", "лотов")}";
            }

            ev.LiveListings = listings;
            PriceCache[cacheKey] = (DateTime.UtcNow, summary, listings);
            return summary;
        }
        catch
        {
            return "";
        }
    }

    private static string FormatListingAge(string? indexedIso)
    {
        if (string.IsNullOrWhiteSpace(indexedIso) || !DateTime.TryParse(indexedIso, out var dt))
            return "";
        var span = DateTime.UtcNow - dt.ToUniversalTime();
        if (span.TotalMinutes < 2) return "now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h";
        return $"{(int)span.TotalDays}d";
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
        {
            ev.LiveListings = [.. cached.listings];
            return cached.summary;
        }

        string queryJson = BuildCustomTradeQueryJson(ev, selectedMods, includeItemCategory);
        return await ExecuteTradeSearchAndFetchAsync(ev, safeLeague, queryJson, cacheKey, isCustom: true, ct);
    }

    public static async Task<(double exPerDiv, double exPerChaos)> FetchKeyExchangeRatesAsync(string league, CancellationToken ct = default, bool forceRefresh = false)
    {
        string safeLeague = string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim();
        if (!forceRefresh && RateCache.TryGetValue(safeLeague, out var cached) && (DateTime.UtcNow - cached.cachedUtc).TotalMinutes < 5)
            return (cached.exPerDiv, cached.exPerChaos);

        // Первинне джерело: poe.ninja PoE 2 Currency Overview (миттєво, без жорсткого 5:15:60 ліміту Trade 2 API)
        var ninjaRates = await TryFetchPoeNinjaRatesAsync(safeLeague, ct);
        if (ninjaRates is (double nDiv, double nChaos) && nDiv > 0 && nChaos > 0)
        {
            RateCache[safeLeague] = (DateTime.UtcNow, nDiv, nChaos);
            return (nDiv, nChaos);
        }

        // Резерв: послідовний запит до Trade 2 Exchange (щоб не тригерити 429 паралельними запитами)
        double? exDiv = await TryFetchExchangeRatioAsync(safeLeague, "exalted", "divine", ct);
        if (exDiv != null) await Task.Delay(250, ct);
        double? exChaos = await TryFetchExchangeRatioAsync(safeLeague, "exalted", "chaos", ct);
        double resolvedDiv = exDiv ?? 120.0;
        double resolvedChaos = exChaos ?? 3.5;
        if (exDiv != null && exChaos != null)
            RateCache[safeLeague] = (DateTime.UtcNow, resolvedDiv, resolvedChaos);
        return (resolvedDiv, resolvedChaos);
    }

    public static async Task<double> EstimateExaltedPerDivineAsync(string league, CancellationToken ct = default) =>
        (await FetchKeyExchangeRatesAsync(league, ct)).exPerDiv;

    private static async Task<(double exPerDiv, double exPerChaos)?> TryFetchPoeNinjaRatesAsync(string safeLeague, CancellationToken ct)
    {
        try
        {
            string url = $"https://poe.ninja/poe2/api/economy/exchange/current/overview?league={Uri.EscapeDataString(safeLeague)}&type=Currency";
            using var resp = await Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("core", out var core) &&
                core.TryGetProperty("rates", out var rates))
            {
                double exPerDiv = rates.TryGetProperty("exalted", out var exEl) ? exEl.GetDouble() : 0;
                double chaosPerDiv = rates.TryGetProperty("chaos", out var chEl) ? chEl.GetDouble() : 0;
                if (exPerDiv > 0 && chaosPerDiv > 0.01)
                {
                    double exPerChaos = Math.Round(exPerDiv / chaosPerDiv, 1);
                    return (Math.Round(exPerDiv, 1), Math.Max(0.1, exPerChaos));
                }
            }
        }
        catch { }
        return null;
    }

    private static async Task<double?> TryFetchExchangeRatioAsync(string safeLeague, string haveCurrency, string wantCurrency, CancellationToken ct)
    {
        try
        {
            string url = $"https://www.pathofexile.com/api/trade2/exchange/{Uri.EscapeDataString(safeLeague)}";
            var payload = JsonSerializer.Serialize(new
            {
                exchange = new { status = new { option = "any" }, have = new[] { haveCurrency }, want = new[] { wantCurrency } }
            });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode) return null;

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
                                double haveVal = exAmt.GetDouble();
                                double wantVal = itAmt.GetDouble();
                                if (wantVal > 0 && haveVal > 0)
                                    ratios.Add(haveVal / wantVal);
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
        return null;
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
