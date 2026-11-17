using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public static class BuildPlannerService
{
    private static readonly Dictionary<string, string> KnownAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SupportGemMartialTempo"] = "Rapid Attacks",
        ["SupportGemPersistence"] = "Prolonged Duration",
        ["SupportGemMagnifiedEffect"] = "Magnified Area",
        ["SupportGemRetreat"] = "Retreat",
        ["SupportGemRage"] = "Rage",
        ["SupportGemInnervate"] = "Innervate",
        ["SupportGemCloseCombat"] = "Close Combat",
        ["SupportGemBlind"] = "Blind",
        ["SupportGemBlindside"] = "Blindside",
        ["SupportGemElementalArmament"] = "Elemental Armament",
        ["SupportGemPinpointCritical"] = "Pinpoint Critical",
        ["SupportGemAncestralCall"] = "Ancestral Call",
        ["SupportGemEfficiency"] = "Efficiency",
        ["SupportGemCooldownRecovery"] = "Cooldown Recovery",
        ["SupportGemDurability"] = "Durability",
        ["SupportGemCullingStrike"] = "Culling Strike",
        ["SupportGemBlazingCritical"] = "Blazing Critical",
        ["SupportGemChargeProfusion"] = "Charge Profusion",
        ["SupportGemThrillOfTheKill"] = "Thrill of the Kill",
        ["SupportGemFrostNexus"] = "Frost Nexus",
        ["SupportGemFireMastery"] = "Fire Mastery",
        ["SupportGemBurgeon"] = "Burgeon",
        ["SupportGemLightningAttunement"] = "Lightning Attunement",
        ["SupportGemBranchingFissures"] = "Branching Fissures",
        ["SupportGemPerpetualCharge"] = "Perpetual Charge",
        ["SupportGemHeightenedCharges"] = "Heightened Charges",
        ["SupportGemCulmination"] = "Culmination",
        ["SupportGemMaim"] = "Maim"
    };

    private static readonly Regex LevelRangeRegex = new(@"(?:lvl?|level)?\s*(\d{1,3})\s*[-–~to]+\s*(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LevelPlusRegex = new(@"(?:lvl?|level)?\s*(\d{1,3})\s*\+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string GetBuildPlannerFolder()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "My Games", "Path of Exile 2", "BuildPlanner");
    }

    public static List<LocalBuildPlan> LoadGameBuildPlannerFiles()
    {
        return LoadBuildsFromFolder(GetBuildPlannerFolder());
    }

    public static List<LocalBuildPlan> LoadBuildsFromFolder(string folder)
    {
        var list = new List<LocalBuildPlan>();
        if (!Directory.Exists(folder)) return list;
        foreach (var f in Directory.GetFiles(folder, "*.build"))
        {
            var p = TryParseFile(f);
            if (p != null) list.Add(p);
        }
        return [.. list.OrderBy(p => p.MinSuggestedLevel).ThenBy(p => p.PassivesCount).ThenBy(p => p.Name)];
    }

    public static (int copied, List<string> destPaths) ImportToGameBuildPlanner(IEnumerable<string> sourceFiles)
    {
        string dstDir = GetBuildPlannerFolder();
        Directory.CreateDirectory(dstDir);
        int copied = 0;
        var destPaths = new List<string>();

        foreach (var src in sourceFiles)
        {
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) continue;
            if (!src.EndsWith(".build", StringComparison.OrdinalIgnoreCase)) continue;

            string dst = Path.Combine(dstDir, Path.GetFileName(src));
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(src, dst, overwrite: true);
                copied++;
            }
            destPaths.Add(dst);
        }
        return (copied, destPaths);
    }

    public static LocalBuildPlan? TryParseFile(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string sha = Convert.ToHexString(SHA256.HashData(bytes))[..16];
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;

            string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? Path.GetFileNameWithoutExtension(path) : Path.GetFileNameWithoutExtension(path);
            var (minLvl, maxLvl) = InferLevelWindow(name, Path.GetFileNameWithoutExtension(path));

            var passives = new List<LocalBuildPassive>();
            if (root.TryGetProperty("passives", out var pArr) && pArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var pEl in pArr.EnumerateArray())
                {
                    if (pEl.ValueKind == JsonValueKind.String)
                    {
                        string pid = pEl.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(pid))
                            passives.Add(new LocalBuildPassive(pid, 1, 100, 0, ""));
                    }
                    else if (pEl.ValueKind == JsonValueKind.Object)
                    {
                        string pid = pEl.TryGetProperty("id", out var pidEl) ? pidEl.GetString() ?? "" : "";
                        int ws = pEl.TryGetProperty("weapon_set", out var wsEl) && wsEl.TryGetInt32(out int wv) ? wv : 0;
                        string note = pEl.TryGetProperty("additional_text", out var ntEl) ? ntEl.GetString() ?? "" : "";
                        var (pMin, pMax) = ReadInterval(pEl);
                        if (!string.IsNullOrWhiteSpace(pid))
                            passives.Add(new LocalBuildPassive(pid, pMin, pMax, ws, note));
                    }
                }
            }

            if (minLvl == 1 && maxLvl == 100 && passives.Count > 0)
            {
                // Евристика лише для сортування етапів, якщо в назві немає цифр рівня
                int est = Math.Clamp(passives.Count - 15, 1, 95);
                minLvl = est;
            }

            var plan = new LocalBuildPlan
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Name = name,
                Author = root.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "",
                Link = root.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "",
                Description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Ascendancy = root.TryGetProperty("ascendancy", out var asc) ? asc.GetString() ?? "" : "",
                MinSuggestedLevel = minLvl,
                MaxSuggestedLevel = maxLvl,
                LastModified = File.GetLastWriteTime(path),
                ContentSha256 = sha
            };
            plan.Passives.AddRange(passives);

            if (root.TryGetProperty("inventory_slots", out var inv) && inv.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in inv.EnumerateArray())
                {
                    string slotId = item.TryGetProperty("inventory_id", out var sid) ? sid.GetString() ?? "Slot" : "Slot";
                    string text = item.TryGetProperty("additional_text", out var txt) ? txt.GetString() ?? "" : "";
                    var (sMin, sMax) = ReadInterval(item);
                    if (!string.IsNullOrWhiteSpace(text))
                        plan.Gear.Add(new PlannerGearItem(slotId, text.Replace("\r", "").Trim(), sMin, sMax));
                }
            }

            if (root.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
            {
                foreach (var sk in skills.EnumerateArray())
                {
                    string id = sk.TryGetProperty("id", out var sid) ? sid.GetString() ?? "" : "";
                    var (skMin, skMax) = ReadInterval(sk);
                    var supports = new List<string>();
                    if (sk.TryGetProperty("support_skills", out var sups) && sups.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sup in sups.EnumerateArray())
                        {
                            string supId = sup.TryGetProperty("id", out var sp) ? sp.GetString() ?? "" : "";
                            if (!string.IsNullOrWhiteSpace(supId))
                                supports.Add(HumanizeMetadataId(supId));
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(id))
                        plan.Skills.Add(new PlannerSkillItem(id, HumanizeMetadataId(id), supports, skMin, skMax));
                }
            }

            return plan;
        }
        catch
        {
            return null;
        }
    }

    public static (int minLvl, int maxLvl) InferLevelWindow(string name, string fileName)
    {
        string combined = $"{name} {fileName}";
        string noActs = Regex.Replace(combined, @"\bacts?\s*\d+(?:\s*[-–~to]+\s*\d+)?", " ", RegexOptions.IgnoreCase);
        var mRange = LevelRangeRegex.Match(noActs);
        if (mRange.Success &&
            int.TryParse(mRange.Groups[1].Value, out int a) &&
            int.TryParse(mRange.Groups[2].Value, out int b) &&
            a <= 100 && b <= 100)
        {
            return (Math.Min(a, b), Math.Max(a, b));
        }

        var mPlus = LevelPlusRegex.Match(noActs);
        if (mPlus.Success && int.TryParse(mPlus.Groups[1].Value, out int start) && start <= 100)
            return (start, 100);

        string lower = combined.ToLowerInvariant();
        if (lower.Contains("early endgame") || lower.Contains("early map")) return (65, 82);
        if (lower.Contains("mid endgame") || lower.Contains("midgame")) return (82, 92);
        if (lower.Contains("uber") || lower.Contains("late") || lower.Contains("final") || lower.Contains("endgame")) return (90, 100);

        var mAct = Regex.Match(lower, @"\bacts?\s*(\d+)(?:\s*[-–~_]+\s*(\d+))?");
        if (mAct.Success && int.TryParse(mAct.Groups[1].Value, out int actStart))
        {
            int actEnd = mAct.Groups[2].Success && int.TryParse(mAct.Groups[2].Value, out int ae) ? ae : actStart;
            if (actStart <= 1) return (1, actEnd >= 3 ? 64 : actEnd == 2 ? 35 : 20);
            if (actStart == 2) return (16, actEnd >= 3 ? 64 : 35);
            if (actStart == 3) return (32, 64);
            return (45, 66);
        }

        if (lower.Contains("campaign") || lower.Contains("interlude") || lower.Contains("cruel")) return (45, 66);
        if (lower.Contains("starter") || lower.Contains("leveling")) return (1, 20);

        return (1, 100);
    }

    public static LocalBuildPlan? SuggestBuildForLevel(List<LocalBuildPlan> plans, int level)
    {
        if (plans.Count == 0) return null;
        var exact = plans
            .Where(p => level >= p.MinSuggestedLevel && level <= p.MaxSuggestedLevel)
            .OrderBy(p => p.MaxSuggestedLevel - p.MinSuggestedLevel)
            .ThenByDescending(p => p.MinSuggestedLevel)
            .FirstOrDefault();

        if (exact != null) return exact;

        return plans
            .OrderBy(p => Math.Abs(((p.MinSuggestedLevel + p.MaxSuggestedLevel) / 2.0) - level))
            .First();
    }

    private static (int min, int max) ReadInterval(JsonElement el)
    {
        if (el.TryGetProperty("level_interval", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 2)
            return (arr[0].GetInt32(), arr[1].GetInt32());
        if (el.TryGetProperty("level_interval", out var single) && single.ValueKind == JsonValueKind.Number)
            return (single.GetInt32(), 100);
        return (1, 100);
    }

    public static string HumanizeMetadataId(string metadataId)
    {
        string tail = metadataId.Split('/').LastOrDefault() ?? metadataId;
        if (KnownAliases.TryGetValue(tail, out var alias))
            return alias;
        tail = Regex.Replace(tail, @"^(SkillGem|SupportGem)", "");
        return Regex.Replace(tail, "(?<!^)([A-Z])", " $1").Trim();
    }
}
