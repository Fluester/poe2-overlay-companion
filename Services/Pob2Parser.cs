using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public static class Pob2Parser
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    static Pob2Parser()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 Poe2OverlayCompanion/2.0");
    }

    public static async Task<Pob2Summary> ResolveAndParseAsync(string input, CancellationToken ct = default)
    {
        string trimmed = (input ?? "").Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string rawCode = await FetchPobCodeFromUrlAsync(uri, ct);
                if (string.IsNullOrWhiteSpace(rawCode))
                    return new Pob2Summary { Success = false, Error = Loc.T("Не вдалося отримати код PoB2 за цим URL.", "Could not extract PoB2 code from URL.", "Не удалось получить код PoB2 по этому URL.") };
                var parsed = Parse(rawCode);
                parsed.SourceUrl = trimmed;
                parsed.RawCode = rawCode;
                return parsed;
            }
            catch (Exception ex)
            {
                return new Pob2Summary { Success = false, Error = Loc.T($"Помилка завантаження PoB2: {ex.Message}", $"PoB2 download error: {ex.Message}", $"Ошибка загрузки PoB2: {ex.Message}") };
            }
        }
        return Parse(trimmed);
    }

    private static async Task<string> FetchPobCodeFromUrlAsync(Uri uri, CancellationToken ct)
    {
        string host = uri.Host.ToLowerInvariant();
        if (host.Contains("pobb.in"))
        {
            string id = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? "";
            string rawUrl = $"https://pobb.in/{id}/raw";
            return (await Http.GetStringAsync(rawUrl, ct)).Trim();
        }
        if (host.Contains("pastebin.com") && !uri.AbsolutePath.StartsWith("/raw/", StringComparison.OrdinalIgnoreCase))
        {
            string id = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? "";
            return (await Http.GetStringAsync($"https://pastebin.com/raw/{id}", ct)).Trim();
        }
        return (await Http.GetStringAsync(uri, ct)).Trim();
    }

    public static Pob2Summary Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new Pob2Summary { Success = false, Error = Loc.T("Встав код експорту Path of Building 2 або посилання pobb.in.", "Paste a Path of Building 2 export code or pobb.in link.", "Вставь код экспорта Path of Building 2 или ссылку pobb.in.") };

        string trimmed = input.Trim();
        string xml;

        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<PathOfBuilding", StringComparison.OrdinalIgnoreCase))
        {
            xml = trimmed;
        }
        else
        {
            try
            {
                xml = DecodePobBase64(trimmed);
            }
            catch (Exception ex)
            {
                return new Pob2Summary
                {
                    Success = false,
                    Error = Loc.T($"Не вдалося розпакувати PoB2-код: {ex.Message}", $"Failed to decode PoB2 code: {ex.Message}", $"Не удалось распаковать PoB2-код: {ex.Message}")
                };
            }
        }

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null)
                return new Pob2Summary { Success = false, Error = Loc.T("Порожній XML у PoB2.", "Empty PoB2 XML.", "Пустой XML в PoB2.") };

            var buildEl = root.Element("Build");
            int level = ParseInt(buildEl?.Attribute("level")?.Value);
            string className = buildEl?.Attribute("className")?.Value ?? "";
            string asc = buildEl?.Attribute("ascendClassName")?.Value ?? "";

            var stats = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (buildEl != null)
            {
                foreach (var ps in buildEl.Elements("PlayerStat"))
                {
                    string? sName = ps.Attribute("stat")?.Value;
                    string? sVal = ps.Attribute("value")?.Value;
                    if (!string.IsNullOrWhiteSpace(sName) &&
                        double.TryParse(sVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    {
                        stats[sName] = v;
                    }
                }
            }

            double es = GetStat(stats, "EnergyShield", "TotalES");
            double ev = GetStat(stats, "Evasion", "TotalEvasion");
            double evPct = GetStat(stats, "EvadeChance", "MeleeEvadeChance", "EvadeChanceTotal");
            double armour = GetStat(stats, "Armour", "TotalArmour");
            double life = GetStat(stats, "Life", "TotalLife");
            double dps = GetStat(stats, "CombinedDPS", "TotalDPS", "FullDPS");
            double spirit = GetStat(stats, "Spirit", "TotalSpirit", "SpiritTotal");

            bool hasCi = xml.Contains("Chaos Inoculation", StringComparison.OrdinalIgnoreCase) || life == 1;

            var summary = new Pob2Summary
            {
                Success = true,
                Level = level,
                ClassName = className,
                Ascendancy = asc,
                EnergyShield = es,
                Evasion = ev,
                EvasionPercent = evPct,
                Armour = armour,
                Life = life,
                TotalDps = dps,
                Spirit = spirit,
                HasCi = hasCi,
                Notes = root.Element("Notes")?.Value?.Trim() ?? ""
            };

            // Skills
            var skillsEl = root.Element("Skills");
            if (skillsEl != null)
            {
                foreach (var skill in skillsEl.Descendants("Skill"))
                {
                    if (skill.Attribute("enabled")?.Value == "false") continue;
                    var gems = skill.Elements("Gem")
                        .Where(g => g.Attribute("enabled")?.Value != "false")
                        .Select(g =>
                        {
                            string name = g.Attribute("nameSpec")?.Value ?? g.Attribute("skillId")?.Value ?? "";
                            string lvl = g.Attribute("level")?.Value ?? "";
                            string qual = g.Attribute("quality")?.Value ?? "";
                            if (string.IsNullOrEmpty(lvl)) return name;
                            return (!string.IsNullOrEmpty(qual) && qual != "0")
                                ? $"{name} (Lv{lvl} Q{qual}%)"
                                : $"{name} (Lv{lvl})";
                        })
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList();

                    if (gems.Count > 0)
                    {
                        summary.SkillNames.Add(string.Join(" + ", gems));
                        summary.SkillGroups.Add(new PobSkillGroup(gems[0], gems.Skip(1).ToList(), true));
                    }
                }
            }

            // Tree Specs (including leveling variants inside PoB2!)
            var treeEl = root.Element("Tree");
            if (treeEl != null)
            {
                int idx = 0;
                foreach (var spec in treeEl.Elements("Spec"))
                {
                    idx++;
                    string title = spec.Attribute("title")?.Value ?? $"Tree {idx}";
                    string specAsc = spec.Attribute("ascendClassName")?.Value ?? asc;
                    string nodesRaw = spec.Attribute("nodes")?.Value ?? "";
                    var nodeIds = nodesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    if (nodeIds.Count > 0)
                        summary.TreeSpecs.Add(new PobTreeSpec { Title = title, Ascendancy = specAsc, NodeIds = nodeIds });
                }
            }

            // Items
            var itemsById = new Dictionary<string, string>();
            var itemsEl = root.Element("Items");
            if (itemsEl != null)
            {
                foreach (var itemEl in itemsEl.Elements("Item"))
                {
                    string? id = itemEl.Attribute("id")?.Value;
                    if (id != null) itemsById[id] = itemEl.Value.Trim();
                }

                foreach (var slot in itemsEl.Descendants("Slot"))
                {
                    string slotName = slot.Attribute("name")?.Value ?? "";
                    string itemId = slot.Attribute("itemId")?.Value ?? "";
                    if (!string.IsNullOrWhiteSpace(itemId) && itemId != "0" && itemsById.TryGetValue(itemId, out var rawItem))
                    {
                        if (string.IsNullOrEmpty(summary.PrimaryWeaponClass) &&
                            (slotName.Equals("Weapon 1", StringComparison.OrdinalIgnoreCase) || slotName.Equals("Weapon1", StringComparison.OrdinalIgnoreCase)))
                        {
                            summary.PrimaryWeaponClass = DetectWeaponClass(rawItem);
                        }

                        var firstLines = rawItem.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .Where(l => !l.StartsWith("Rarity:") && !l.StartsWith("Item Class:") && !l.StartsWith("---"))
                            .Take(2);
                        summary.EquippedItems.Add($"{slotName}: {string.Join(" — ", firstLines)}");
                        summary.RawEquippedBySlot[slotName] = rawItem;
                    }
                }
            }

            BuildUniversalRecommendations(summary, stats, itemsById.Values);

            summary.RawContextSummary =
                $"PoB2: Lvl {summary.Level} {summary.ClassName} ({summary.Ascendancy}) | " +
                $"ES: {summary.EnergyShield:F0}, EV: {summary.Evasion:F0} ({summary.EvasionPercent:F0}%), Armour: {summary.Armour:F0}, " +
                $"Life: {summary.Life:F0}, DPS: {summary.TotalDps:F0}, Spirit: {summary.Spirit:F0}, CI: {summary.HasCi}, Weapon: {summary.PrimaryWeaponClass}\n" +
                $"Skills: {string.Join(" | ", summary.SkillNames.Take(8))}\n" +
                $"Items: {string.Join(" | ", summary.EquippedItems.Take(10))}";

            return summary;
        }
        catch (Exception ex)
        {
            return new Pob2Summary { Success = false, Error = Loc.T($"Помилка XML PoB2: {ex.Message}", $"PoB2 XML error: {ex.Message}", $"Ошибка XML PoB2: {ex.Message}") };
        }
    }

    public static string DetectWeaponClass(string rawText)
    {
        var lower = rawText.ToLowerInvariant();
        if (lower.Contains("quarterstaff") || lower.Contains("quarterstaves")) return "Quarterstaves";
        if (lower.Contains("crossbow")) return "Crossbows";
        if (lower.Contains("bow")) return "Bows";
        if (lower.Contains("wand")) return "Wands";
        if (lower.Contains("sceptre")) return "Sceptres";
        if (lower.Contains("spear")) return "Spears";
        if (lower.Contains("talisman")) return "Talismans";
        if (lower.Contains("flail")) return "Flails";
        if (lower.Contains("dagger")) return "Daggers";
        if (lower.Contains("claw")) return "Claws";
        if (lower.Contains("two hand sword")) return "Two Hand Swords";
        if (lower.Contains("sword")) return "One Hand Swords";
        if (lower.Contains("two hand axe")) return "Two Hand Axes";
        if (lower.Contains("axe")) return "One Hand Axes";
        if (lower.Contains("two hand mace")) return "Two Hand Maces";
        if (lower.Contains("mace")) return "One Hand Maces";
        if (lower.Contains("staff") || lower.Contains("staves")) return "Staves";
        return "";
    }

    private static void BuildUniversalRecommendations(Pob2Summary s, Dictionary<string, double> stats, IEnumerable<string> rawItems)
    {
        string allItems = string.Join("\n", rawItems).ToLowerInvariant();

        if (s.HasCi && allItems.Contains("removed from life"))
            s.Recommendations.Add(Loc.T("КРИТИЧНО: При CI одягнено фласку з 'removed from Life' — це миттєва смерть!", "CRITICAL: Mana Flask with 'removed from Life' equipped with CI — instant death!", "КРИТИЧНО: При CI надета фласка с 'removed from Life' — мгновенная смерть!"));

        if (s.HasCi && s.EnergyShield is > 0 and < 4000)
            s.Recommendations.Add(Loc.T($"Для Chaos Inoculation замало ES ({s.EnergyShield:F0}). Ціль: ≥ 4,500–6,000+ ES.", $"Energy Shield is low for CI ({s.EnergyShield:F0}). Target >= 4,500–6,000+ ES.", $"Мало ES для Chaos Inoculation ({s.EnergyShield:F0}). Цель: >= 4,500–6,000+ ES."));

        if (!s.HasCi && s.Level >= 65 && s.Life is > 0 and < 1800 && s.EnergyShield < 1500)
            s.Recommendations.Add(Loc.T($"Низький пул виживаності (Life {s.Life:F0} + ES {s.EnergyShield:F0}) для рівня {s.Level}.", $"Low life/ES pool (Life {s.Life:F0} + ES {s.EnergyShield:F0}) for level {s.Level}.", $"Низкий пул выживаемости (Life {s.Life:F0} + ES {s.EnergyShield:F0}) для уровня {s.Level}."));

        foreach (var (statKey, label) in new[] { ("FireResist", "Fire"), ("ColdResist", "Cold"), ("LightningResist", "Lightning") })
        {
            if (stats.TryGetValue(statKey, out double rv) && rv < 75 && s.Level >= 60)
                s.Recommendations.Add(Loc.T($"Недокап опору {label}: {rv:F0}% (потрібно 75%).", $"Uncapped {label} Resistance: {rv:F0}% (aim for 75%).", $"Недокап сопротивления {label}: {rv:F0}% (нужно 75%)."));
        }

        if (s.TreeSpecs.Count > 1)
            s.Recommendations.Add(Loc.T($"У PoB2 знайдено {s.TreeSpecs.Count} дерев(а) пасивок — дивись покрокове порівняння у вкладці «Дерево пасивок».", $"Found {s.TreeSpecs.Count} passive trees in PoB2 — view progression diff in Passive Tree tab.", $"В PoB2 найдено {s.TreeSpecs.Count} деревьев пассивок — смотри сравнение во вкладке «Дерево пассивок»."));

        if (s.Recommendations.Count == 0)
            s.Recommendations.Add(Loc.T("Профіль PoB2 проаналізовано успішно; оцінка предметів адаптована під параметри білду.", "PoB2 build parsed cleanly; item checks now follow your build stats.", "Профиль PoB2 успешно разобран; оценка вещей адаптирована под параметры билда."));
    }

    private static string DecodePobBase64(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c switch { '-' => '+', '_' => '/', _ => c });
        }
        int mod = sb.Length % 4;
        if (mod > 0) sb.Append('=', 4 - mod);

        byte[] compressed = Convert.FromBase64String(sb.ToString());
        try
        {
            using var ms = new MemoryStream(compressed);
            using var z = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            z.CopyTo(outMs);
            return Encoding.UTF8.GetString(outMs.ToArray());
        }
        catch
        {
            using var ms = new MemoryStream(compressed);
            using var d = new DeflateStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            d.CopyTo(outMs);
            return Encoding.UTF8.GetString(outMs.ToArray());
        }
    }

    private static int ParseInt(string? val) => int.TryParse(val, out int res) ? res : 0;

    private static double GetStat(Dictionary<string, double> stats, params string[] keys)
    {
        foreach (var k in keys)
            if (stats.TryGetValue(k, out double v)) return v;
        return 0;
    }
}
