using System.Diagnostics;
using System.Text.Json;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed record TradeShortcut(string Label, string QueryJson);

public static class TradeLinkBuilder
{
    public static readonly IReadOnlyList<TradeShortcut> UniversalShortcuts =
    [
        new("Boots 30%+ MS", """{"query":{"filters":{"type_filters":{"filters":{"category":{"option":"armour.boots"}}},"mod_filters":{"filters":[{"id":"explicit.stat_2250533757","value":{"min":30}}]}}}}"""),
        new("Ring +Life/Res", """{"query":{"filters":{"type_filters":{"filters":{"category":{"option":"accessory.ring"}}}}}}"""),
        new("Amulet +Spirit", """{"query":{"filters":{"type_filters":{"filters":{"category":{"option":"accessory.amulet"}}}}}}"""),
        new("Mana / Life Flask", """{"query":{"filters":{"type_filters":{"filters":{"category":{"option":"flask"}}}}}}"""),
        new("Jewels", """{"query":{"filters":{"type_filters":{"filters":{"category":{"option":"jewel"}}}}}}""")
    ];

    public static List<TradeShortcut> BuildDynamicShortcuts(BuildProfile? profile)
    {
        var list = new List<TradeShortcut>();
        if (profile != null)
        {
            if (!string.IsNullOrWhiteSpace(profile.PreferredWeaponClass))
            {
                list.Add(new TradeShortcut(
                    profile.PreferredWeaponClass,
                    JsonSerializer.Serialize(new { query = new { term = profile.PreferredWeaponClass } })));
            }

            foreach (var hint in profile.TargetGearHints.Take(6))
            {
                string firstLine = hint.Split([':', '\n', '|'], StringSplitOptions.RemoveEmptyEntries)
                    .Skip(hint.Contains(':') ? 1 : 0)
                    .FirstOrDefault()?.Trim() ?? "";
                if (firstLine.Length >= 4 && firstLine.Length <= 42 && !firstLine.Contains('%'))
                {
                    list.Add(new TradeShortcut(
                        firstLine,
                        JsonSerializer.Serialize(new { query = new { term = firstLine } })));
                }
            }
        }

        if (list.Count < 4)
            list.AddRange(UniversalShortcuts);

        return list.DistinctBy(x => x.Label).Take(8).ToList();
    }

    public static string BuildSearchUrl(string league, string queryJson)
    {
        string safeLeague = string.IsNullOrWhiteSpace(league) ? "Standard" : league.Trim();
        string encodedLeague = Uri.EscapeDataString(safeLeague);
        string encodedQuery = Uri.EscapeDataString(queryJson);
        return $"https://www.pathofexile.com/trade2/search/poe2/{encodedLeague}?q={encodedQuery}";
    }

    public static void OpenTradeQuery(string league, string queryJson)
    {
        string url = BuildSearchUrl(league, queryJson);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void OpenTradeByItemName(string league, string itemNameOrBase, bool isUnique)
    {
        object payload = isUnique
            ? new { query = new { name = itemNameOrBase } }
            : new { query = new { term = itemNameOrBase } };
        OpenTradeQuery(league, JsonSerializer.Serialize(payload));
    }

    public static string BuildForCopiedItem(string league, ItemEvaluation ev)
    {
        if (ev.Rarity.Equals("Unique", StringComparison.OrdinalIgnoreCase))
        {
            string uniqName = ev.ItemName.Split(" — ")[0].Trim();
            return BuildSearchUrl(league, JsonSerializer.Serialize(new { query = new { name = uniqName } }));
        }

        string cat = MapItemClassToTradeCategory(ev.ItemClass);
        var typeFilters = string.IsNullOrEmpty(cat)
            ? new Dictionary<string, object>()
            : new Dictionary<string, object> { ["category"] = new { option = cat } };

        var statFilters = new List<object>();
        if (ev.MovementSpeed >= 20)
            statFilters.Add(new { id = "explicit.stat_2250533757", value = new { min = Math.Max(20, (int)ev.MovementSpeed - 5) } });
        if (ev.SpiritBonus >= 25)
            statFilters.Add(new { id = "explicit.stat_3981240776", value = new { min = Math.Max(20, (int)ev.SpiritBonus - 10) } });

        var filters = new Dictionary<string, object>();
        if (typeFilters.Count > 0) filters["type_filters"] = new { filters = typeFilters };

        if (ev.TotalDps >= 150 || ev.EnergyShield >= 150 || ev.Evasion >= 200 || ev.Armour >= 250)
        {
            var equip = new Dictionary<string, object>();
            if (ev.TotalDps >= 150) equip["dps"] = new { min = (int)(ev.TotalDps * 0.85) };
            if (ev.EnergyShield >= 150) equip["es"] = new { min = (int)(ev.EnergyShield * 0.85) };
            if (ev.Evasion >= 200) equip["evasion"] = new { min = (int)(ev.Evasion * 0.85) };
            if (ev.Armour >= 250) equip["ar"] = new { min = (int)(ev.Armour * 0.85) };
            if (equip.Count > 0) filters["equipment_filters"] = new { filters = equip };
        }

        object queryObj = statFilters.Count > 0
            ? new { query = new { filters, stats = new[] { new { type = "and", filters = statFilters } } } }
            : new { query = new { filters, term = ev.RawBaseType } };

        return BuildSearchUrl(league, JsonSerializer.Serialize(queryObj));
    }

    public static string MapItemClassToTradeCategory(string itemClass)
    {
        string ic = (itemClass ?? "").ToLowerInvariant();
        if (ic.Contains("quarterst")) return "weapon.quarterstaff";
        if (ic.Contains("crossbow")) return "weapon.crossbow";
        if (ic.Contains("bow")) return "weapon.bow";
        if (ic.Contains("wand")) return "weapon.wand";
        if (ic.Contains("sceptre")) return "weapon.sceptre";
        if (ic.Contains("spear")) return "weapon.spear";
        if (ic.Contains("talisman")) return "weapon.talisman";
        if (ic.Contains("flail")) return "weapon.flail";
        if (ic.Contains("staves") || ic.Contains("staff")) return "weapon.staff";
        if (ic.Contains("mace")) return ic.Contains("two") ? "weapon.twomace" : "weapon.onemace";
        if (ic.Contains("helmet")) return "armour.helmet";
        if (ic.Contains("body armour")) return "armour.chest";
        if (ic.Contains("gloves")) return "armour.gloves";
        if (ic.Contains("boots")) return "armour.boots";
        if (ic.Contains("shield") || ic.Contains("buckler")) return "armour.shield";
        if (ic.Contains("focus")) return "armour.focus";
        if (ic.Contains("amulet")) return "accessory.amulet";
        if (ic.Contains("ring")) return "accessory.ring";
        if (ic.Contains("belt")) return "accessory.belt";
        if (ic.Contains("jewel")) return "jewel";
        if (ic.Contains("flask")) return "flask";
        if (ic.Contains("charm")) return "charm";
        return "";
    }
}
