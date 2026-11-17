using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poe2OverlayCompanion.Services;

public sealed record LiveUniqueEntry(string Name, string BaseType, string Category);

/// <summary>
/// Живий кеш офіційного каталогу PoE 2 з `/api/trade2/data/items` (усі бази, ~770+ унiків та ~900+ каменів).
/// Оновлюється у фоні раз на 24 години або при ручному оновленні ліг; між запусками читається з локального кешу за мілісекунди.
/// </summary>
public static class LivePoe2DataService
{
    private sealed class CachedPayload
    {
        public DateTime FetchedUtc { get; set; }
        public List<LiveUniqueEntry> Uniques { get; set; } = [];
        public Dictionary<string, string> Bases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Gems { get; set; } = [];
    }

    private sealed class RuntimeIndex
    {
        public DateTime FetchedUtc { get; init; }
        public List<LiveUniqueEntry> UniquesLongestFirst { get; init; } = [];
        public Dictionary<string, LiveUniqueEntry> UniquesByName { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> CategoryByBase { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> BasesLongestFirst { get; init; } = [];
        public HashSet<string> GemNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static readonly object LoadLock = new();
    private static RuntimeIndex? _index;

    public static int CachedUniquesCount => EnsureLoaded().UniquesByName.Count;
    public static int CachedBasesCount => EnsureLoaded().CategoryByBase.Count;
    public static int CachedGemsCount => EnsureLoaded().GemNames.Count;

    private static string CachePath => Path.Combine(AppPaths.GetAppDataDirectory(), "live_trade2_items.json");

    private static RuntimeIndex EnsureLoaded()
    {
        var cur = Volatile.Read(ref _index);
        if (cur != null) return cur;

        lock (LoadLock)
        {
            if (_index != null) return _index;
            var loaded = TryLoadFromDisk() ?? new RuntimeIndex { FetchedUtc = DateTime.MinValue };
            _index = loaded;
            return loaded;
        }
    }

    public static async Task<bool> RefreshIfStaleAsync(bool force = false, CancellationToken ct = default)
    {
        var cur = EnsureLoaded();
        if (!force && cur.UniquesByName.Count > 0 && (DateTime.UtcNow - cur.FetchedUtc).TotalHours < 24)
            return true;

        if (!await RefreshGate.WaitAsync(0, ct))
            return false;

        try
        {
            cur = EnsureLoaded();
            if (!force && cur.UniquesByName.Count > 0 && (DateTime.UtcNow - cur.FetchedUtc).TotalHours < 24)
                return true;

            using var resp = await PoeTradePriceService.SharedHttp.GetAsync("https://www.pathofexile.com/api/trade2/data/items", ct);
            if (!resp.IsSuccessStatusCode) return false;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("result", out var cats) || cats.ValueKind != JsonValueKind.Array)
                return false;

            var uniques = new Dictionary<string, LiveUniqueEntry>(800, StringComparer.OrdinalIgnoreCase);
            var bases = new Dictionary<string, string>(3000, StringComparer.OrdinalIgnoreCase);
            var gems = new HashSet<string>(1000, StringComparer.OrdinalIgnoreCase);

            foreach (var catEl in cats.EnumerateArray())
            {
                string catId = catEl.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "").ToLowerInvariant() : "";
                if (string.IsNullOrEmpty(catId) || !catEl.TryGetProperty("entries", out var entriesEl) || entriesEl.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var e in entriesEl.EnumerateArray())
                {
                    string type = e.TryGetProperty("type", out var tEl) ? (tEl.GetString() ?? "").Trim() : "";
                    string name = e.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "").Trim() : "";
                    bool isUnique = e.TryGetProperty("flags", out var flEl) &&
                                    flEl.TryGetProperty("unique", out var uEl) && uEl.GetBoolean();

                    if (isUnique && !string.IsNullOrEmpty(name))
                    {
                        uniques.TryAdd(name, new LiveUniqueEntry(name, type, catId));
                    }
                    else if (!string.IsNullOrEmpty(type))
                    {
                        if (catId == "gem")
                            gems.Add(type);
                        else
                            bases.TryAdd(type, catId);
                    }
                }
            }

            if (bases.Count == 0 && uniques.Count == 0)
                return false;

            var payload = new CachedPayload
            {
                FetchedUtc = DateTime.UtcNow,
                Uniques = [.. uniques.Values],
                Bases = bases,
                Gems = [.. gems]
            };

            Volatile.Write(ref _index, BuildRuntimeIndex(payload));
            TrySaveToDisk(payload);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    public static bool TryMatchUnique(string rawItemOrTitle, out LiveUniqueEntry entry)
    {
        entry = default!;
        if (string.IsNullOrWhiteSpace(rawItemOrTitle)) return false;
        var idx = EnsureLoaded();
        if (idx.UniquesByName.Count == 0) return false;

        string trimmed = rawItemOrTitle.Trim();
        if (idx.UniquesByName.TryGetValue(trimmed, out var exact))
        {
            entry = exact;
            return true;
        }

        // Перевіряємо перші рядки (назва унiка в Ctrl+C завжди перед "--------")
        int dashIdx = trimmed.IndexOf("--------", StringComparison.Ordinal);
        string header = (dashIdx > 0 ? trimmed[..dashIdx] : trimmed);
        foreach (string line in header.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string l = line.Trim();
            if (idx.UniquesByName.TryGetValue(l, out var lineHit))
            {
                entry = lineHit;
                return true;
            }
        }

        // Якщо передано злитий рядок штибу "Andvarius Gold Ring"
        foreach (var u in idx.UniquesLongestFirst)
        {
            if (header.StartsWith(u.Name + " ", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(header, $@"\b{Regex.Escape(u.Name)}\b", RegexOptions.IgnoreCase))
            {
                entry = u;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetBaseCategory(string rawItemOrTitle, out string category)
    {
        category = "";
        if (string.IsNullOrWhiteSpace(rawItemOrTitle)) return false;
        var idx = EnsureLoaded();
        if (idx.CategoryByBase.Count == 0) return false;

        int dashIdx = rawItemOrTitle.IndexOf("--------", StringComparison.Ordinal);
        string header = (dashIdx > 0 ? rawItemOrTitle[..dashIdx] : rawItemOrTitle).Trim();

        foreach (string line in header.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string l = line.Trim();
            if (idx.CategoryByBase.TryGetValue(l, out var exactCat))
            {
                category = exactCat;
                return true;
            }
        }

        // Для Magic/Rare назв на кшталт "Shining Twig Circlet of Plunder" шукаємо точну базу найдовшим збігом
        foreach (string b in idx.BasesLongestFirst)
        {
            if (header.Contains(b, StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(header, $@"\b{Regex.Escape(b)}\b", RegexOptions.IgnoreCase))
            {
                category = idx.CategoryByBase[b];
                return true;
            }
        }

        return false;
    }

    public static bool ContainsGem(string gemName)
    {
        if (string.IsNullOrWhiteSpace(gemName)) return false;
        return EnsureLoaded().GemNames.Contains(gemName.Trim());
    }

    public static void SeedForTests(IEnumerable<LiveUniqueEntry> uniques, IReadOnlyDictionary<string, string> bases, IEnumerable<string> gems)
    {
        var payload = new CachedPayload
        {
            FetchedUtc = DateTime.UtcNow,
            Uniques = [.. uniques],
            Bases = new Dictionary<string, string>(bases, StringComparer.OrdinalIgnoreCase),
            Gems = [.. gems]
        };
        Volatile.Write(ref _index, BuildRuntimeIndex(payload));
    }

    private static RuntimeIndex BuildRuntimeIndex(CachedPayload p)
    {
        var uDict = new Dictionary<string, LiveUniqueEntry>(p.Uniques.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var u in p.Uniques)
        {
            if (!string.IsNullOrWhiteSpace(u.Name))
                uDict[u.Name] = u;
        }
        return new RuntimeIndex
        {
            FetchedUtc = p.FetchedUtc,
            UniquesByName = uDict,
            UniquesLongestFirst = [.. uDict.Values.OrderByDescending(u => u.Name.Length)],
            CategoryByBase = new Dictionary<string, string>(p.Bases, StringComparer.OrdinalIgnoreCase),
            BasesLongestFirst = [.. p.Bases.Keys.OrderByDescending(k => k.Length)],
            GemNames = new HashSet<string>(p.Gems, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static RuntimeIndex? TryLoadFromDisk()
    {
        try
        {
            string path = CachePath;
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<CachedPayload>(json);
            return payload != null ? BuildRuntimeIndex(payload) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveToDisk(CachedPayload payload)
    {
        try
        {
            string path = CachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(payload));
            File.Move(tmp, path, overwrite: true);
        }
        catch { }
    }
}
