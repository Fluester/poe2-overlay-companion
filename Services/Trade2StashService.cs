using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Сканер стешу PoE 2 через офіційний Trade 2 Search/Fetch API для публічних/Merchant вкладок.
/// (Класичний `/character-window/get-stash-items` у GGG працює лише для PoE 1 і не віддає PoE 2 ліги/валюту.)
/// </summary>
public static class Trade2StashService
{
    public static string BuildSellerSearchJson(string accountName) => JsonSerializer.Serialize(new
    {
        query = new
        {
            status = new { option = "any" },
            filters = new
            {
                trade_filters = new
                {
                    filters = new
                    {
                        account = new { input = (accountName ?? "").Trim() }
                    }
                }
            }
        },
        sort = new { price = "asc" }
    });

    /// <summary>
    /// Перевіряє, чи відповідь `/character-window/get-stash-items` реально належить PoE 2, а не PoE 1 (`Standard` fallback).
    /// </summary>
    public static bool IsGenuinePoe2StashTabJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{", StringComparison.Ordinal))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out _))
                return false;
            if (doc.RootElement.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in itemsEl.EnumerateArray())
                {
                    if (it.TryGetProperty("realm", out var rEl) &&
                        string.Equals(rEl.GetString(), "poe2", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static double CurrencySlugToExalted(string? slug, double exPerDiv, double exPerChaos)
    {
        string s = (slug ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return 0;
        double safeDiv = Math.Max(1.0, exPerDiv);
        double safeChaos = Math.Max(0.1, exPerChaos);
        return s switch
        {
            "divine" or "div" => safeDiv,
            "exalted" or "ex" => 1.0,
            "chaos" or "c" => safeChaos,
            "mirror" => safeDiv * 450.0,
            "perfect-exalted" => 15.0,
            "greater-exalted" => 4.0,
            "annul" or "orb-of-annulment" => 8.0,
            "chance" or "orb-of-chance" => 4.0,
            "vaal" or "vaal-orb" => 1.2,
            "alch" or "alchemy" or "orb-of-alchemy" => 0.5,
            "regal" or "regal-orb" => 0.35,
            "gcp" or "gemcutters-prism" => 1.5,
            "prism" or "glassblowers-bauble" => 0.3,
            "jeweller" or "artificer" => 0.8,
            "aug" or "orb-of-augmentation" => 0.04,
            "transmute" or "orb-of-transmutation" => 0.02,
            "wisdom" or "scroll-of-wisdom" => 0.01,
            _ => 0
        };
    }

    public static StashScanResult EvaluateTrade2Listings(
        string league,
        int totalListings,
        IReadOnlyList<string> fetchJsons,
        double exPerDiv,
        double exPerChaos)
    {
        double safeDiv = Math.Max(1.0, exPerDiv);
        string safeLeague = string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim();

        if (totalListings <= 0)
        {
            return new StashScanResult
            {
                Ok = true,
                League = safeLeague,
                TabsScanned = 0,
                CurrencyItemsCount = 0,
                TotalExalted = 0,
                TotalDivine = 0,
                ExPerDivine = safeDiv,
                ScannedUtc = DateTime.UtcNow,
                BreakdownLines =
                [
                    Loc.T("Публічні/Merchant вкладки: 0 виставлених лотів", "Public/Merchant tabs: 0 listed items", "Публичные/Merchant вкладки: 0 выставленных лотов"),
                    Loc.T("Приватні вкладки та Currency-стеш у PoE 2 ще недоступні через API GGG", "Private tabs & Currency stash aren't exposed by GGG's PoE 2 web API yet", "Приватные вкладки и Currency-тайник в PoE 2 пока недоступны через API GGG")
                ]
            };
        }

        var uniqueTabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Title, string Tab, double ExVal, double Amount, string Currency)>();
        double totalEx = 0;

        foreach (string fJson in fetchJsons)
        {
            if (string.IsNullOrWhiteSpace(fJson)) continue;
            try
            {
                using var doc = JsonDocument.Parse(fJson);
                if (!doc.RootElement.TryGetProperty("result", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var entry in arr.EnumerateArray())
                {
                    if (!entry.TryGetProperty("listing", out var lst)) continue;
                    string tabName = lst.TryGetProperty("stash", out var stEl) && stEl.TryGetProperty("name", out var snEl)
                        ? (snEl.GetString() ?? "Trade")
                        : "Trade";
                    uniqueTabs.Add(tabName);

                    double amount = 0;
                    string cur = "";
                    if (lst.TryGetProperty("price", out var pr))
                    {
                        if (pr.TryGetProperty("amount", out var aEl) && aEl.TryGetDouble(out double a)) amount = a;
                        if (pr.TryGetProperty("currency", out var cEl)) cur = cEl.GetString() ?? "";
                    }

                    double unitEx = CurrencySlugToExalted(cur, safeDiv, exPerChaos);
                    double exVal = amount > 0 && unitEx > 0 ? amount * unitEx : 0;
                    totalEx += exVal;

                    string title = "Item";
                    if (entry.TryGetProperty("item", out var it))
                    {
                        string name = it.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
                        string typeLine = it.TryGetProperty("typeLine", out var tEl) ? (tEl.GetString() ?? "") : "";
                        string baseType = it.TryGetProperty("baseType", out var bEl) ? (bEl.GetString() ?? "") : "";
                        title = !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(typeLine)
                            ? $"{name} {typeLine}".Trim()
                            : (!string.IsNullOrWhiteSpace(typeLine) ? typeLine : (!string.IsNullOrWhiteSpace(baseType) ? baseType : "Item"));
                        if (it.TryGetProperty("stackSize", out var ssEl) && ssEl.TryGetInt32(out int ss) && ss > 1)
                            title += $" ×{ss}";
                    }

                    entries.Add((title, tabName, exVal, amount, cur));
                }
            }
            catch { }
        }

        var breakdown = new List<string>
        {
            Loc.T(
                $"Trade 2 ({uniqueTabs.Count} вкл., {entries.Count}/{totalListings} лотів)",
                $"Trade 2 ({uniqueTabs.Count} tabs, {entries.Count}/{totalListings} listings)",
                $"Trade 2 ({uniqueTabs.Count} вкл., {entries.Count}/{totalListings} лотов)")
        };

        foreach (var e in entries.OrderByDescending(x => x.ExVal).Take(5))
        {
            string priceTag = e.Amount > 0 && !string.IsNullOrEmpty(e.Currency)
                ? $"{e.Amount:0.#} {e.Currency}"
                : Loc.T("без ціни", "unpriced", "без цены");
            breakdown.Add($"{e.Title} [{e.Tab}]: {priceTag}");
        }
        if (totalListings > entries.Count)
            breakdown.Add(Loc.T($"+ ще {totalListings - entries.Count} лотів", $"+ {totalListings - entries.Count} more listings", $"+ ещё {totalListings - entries.Count} лотов"));

        return new StashScanResult
        {
            Ok = true,
            League = safeLeague,
            TabsScanned = Math.Max(1, uniqueTabs.Count),
            CurrencyItemsCount = entries.Count,
            TotalExalted = Math.Round(totalEx, 2),
            TotalDivine = Math.Round(totalEx / safeDiv, 2),
            ExPerDivine = safeDiv,
            BreakdownLines = breakdown,
            ScannedUtc = DateTime.UtcNow
        };
    }

    public static async Task<(bool Ok, bool NeedBrowserFallback, StashScanResult? Scan, string Error)> ScanWithHttpClientAsync(
        HttpClient client,
        string league,
        string? accountName,
        double exPerDiv,
        double exPerChaos = 12.0,
        CancellationToken ct = default)
    {
        string safeLeague = string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim();
        string resolvedAcc = (accountName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(resolvedAcc))
        {
            try
            {
                using var profResp = await client.GetAsync("https://www.pathofexile.com/api/profile", ct);
                if (profResp.IsSuccessStatusCode)
                {
                    string profJson = await profResp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(profJson);
                    if (doc.RootElement.TryGetProperty("name", out var nEl))
                        resolvedAcc = (nEl.GetString() ?? "").Trim();
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(resolvedAcc))
        {
            return (false, true, null, Loc.T(
                "Не знайдено ім'я акаунта — спочатку підключи PoE 2 акаунт (Налаштування -> Connect PoE 2 Account).",
                "Account name not resolved — connect your PoE 2 account in Settings first.",
                "Имя аккаунта не найдено — сначала подключи аккаунт PoE 2 в Настройках."));
        }

        try
        {
            string searchUrl = $"https://www.pathofexile.com/api/trade2/search/poe2/{Uri.EscapeDataString(safeLeague)}";
            using var content = new StringContent(BuildSellerSearchJson(resolvedAcc), Encoding.UTF8, "application/json");
            using var sResp = await client.PostAsync(searchUrl, content, ct);

            if (sResp.StatusCode == HttpStatusCode.Unauthorized || sResp.StatusCode == HttpStatusCode.Forbidden)
            {
                return (false, true, null, Loc.T(
                    "Trade 2 вимагає підтвердження сесії у браузері (Cloudflare / вхід).",
                    "Trade 2 requires browser session verification.",
                    "Trade 2 требует подтверждения сессии в браузере."));
            }

            string sBody = await sResp.Content.ReadAsStringAsync(ct);
            if (!sResp.IsSuccessStatusCode)
                return (false, false, null, $"Trade 2 HTTP {(int)sResp.StatusCode}: {ExtractApiError(sBody)}");

            using var sDoc = JsonDocument.Parse(sBody);
            if (sDoc.RootElement.TryGetProperty("error", out _))
                return (false, false, null, $"Trade 2: {ExtractApiError(sBody)}");

            string queryId = sDoc.RootElement.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
            int total = sDoc.RootElement.TryGetProperty("total", out var totEl) ? totEl.GetInt32() : 0;

            var ids = new List<string>();
            if (sDoc.RootElement.TryGetProperty("result", out var resArr) && resArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in resArr.EnumerateArray())
                {
                    string? id = el.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                    if (ids.Count >= 40) break;
                }
            }

            if (total <= 0 || ids.Count == 0 || string.IsNullOrWhiteSpace(queryId))
            {
                var emptyScan = EvaluateTrade2Listings(safeLeague, 0, [], exPerDiv, exPerChaos);
                return (true, false, emptyScan, "");
            }

            var fetchJsons = new List<string>();
            for (int i = 0; i < ids.Count; i += 10)
            {
                if (i > 0) await Task.Delay(200, ct);
                var batch = ids.Skip(i).Take(10);
                string fetchUrl = $"https://www.pathofexile.com/api/trade2/fetch/{string.Join(",", batch)}?query={Uri.EscapeDataString(queryId)}";
                using var fResp = await client.GetAsync(fetchUrl, ct);
                if (!fResp.IsSuccessStatusCode) break;
                fetchJsons.Add(await fResp.Content.ReadAsStringAsync(ct));
            }

            var scan = EvaluateTrade2Listings(safeLeague, total, fetchJsons, exPerDiv, exPerChaos);
            return (true, false, scan, "");
        }
        catch (Exception ex)
        {
            return (false, true, null, ex.Message);
        }
    }

    public static async Task<(bool Ok, bool NeedBrowserFallback, StashScanResult? Scan, string Error)> ScanListedStashAsync(
        string league,
        string? accountName,
        IReadOnlyList<(string Name, string Value, string Domain)> cookies,
        string userAgent,
        double exPerDiv,
        double exPerChaos = 12.0,
        CancellationToken ct = default)
    {
        try
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };
            foreach (var c in cookies)
            {
                if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Value)) continue;
                string dom = (c.Domain ?? "").TrimStart('.');
                try
                {
                    if (dom.Contains("pathofexile.com", StringComparison.OrdinalIgnoreCase))
                        handler.CookieContainer.Add(new Uri("https://www.pathofexile.com"), new Cookie(c.Name, c.Value, "/", c.Domain));
                }
                catch { }
            }

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(14) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(userAgent) ? "Mozilla/5.0" : userAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            return await ScanWithHttpClientAsync(client, league, accountName, exPerDiv, exPerChaos, ct);
        }
        catch (Exception ex)
        {
            return (false, true, null, ex.Message);
        }
    }

    public static string BuildTrade2BrowserFetchScript(string league, string accountName)
    {
        string lgLit = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim());
        string bodyLit = JsonSerializer.Serialize(BuildSellerSearchJson(accountName));
        return $$"""
(async () => {
  try {
    const lg = {{lgLit}};
    const sr = await fetch('/api/trade2/search/poe2/' + encodeURIComponent(lg), {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: {{bodyLit}}
    });
    const st = sr.status;
    const sTxt = await sr.text();
    if (!sr.ok) return JSON.stringify({ ok: false, status: st, error: sTxt.slice(0, 240) });
    const sData = JSON.parse(sTxt);
    const total = Number(sData.total || 0);
    const qId = String(sData.id || '');
    const ids = Array.isArray(sData.result) ? sData.result.slice(0, 40) : [];
    if (total <= 0 || !ids.length || !qId) {
      return JSON.stringify({ ok: true, total: 0, batches: [] });
    }
    const batches = [];
    for (let i = 0; i < ids.length; i += 10) {
      if (i > 0) await new Promise(r => setTimeout(r, 200));
      const chunk = ids.slice(i, i + 10).join(',');
      const fr = await fetch('/api/trade2/fetch/' + chunk + '?query=' + encodeURIComponent(qId), { credentials: 'include' });
      if (!fr.ok) break;
      batches.push(await fr.text());
    }
    return JSON.stringify({ ok: true, total, batches });
  } catch (e) {
    return JSON.stringify({ ok: false, error: String(e) });
  }
})();
""";
    }

    private static string ExtractApiError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? json;
        }
        catch { }
        return json.Length > 180 ? json[..180] : json;
    }
}
