using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed class BuildProfileManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly string _profilesPath;

    public List<BuildProfile> Profiles { get; private set; } = [];
    public BuildProfile? ActiveProfile { get; private set; }

    static BuildProfileManager()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Poe2OverlayCompanion/2.0");
    }

    public BuildProfileManager(string? customAppDataDir = null)
    {
        string appDataDir = string.IsNullOrWhiteSpace(customAppDataDir)
            ? AppPaths.GetAppDataDirectory()
            : customAppDataDir;
        Directory.CreateDirectory(appDataDir);
        _profilesPath = Path.Combine(appDataDir, "build_profiles.json");

        LoadProfiles();
        ActiveProfile = Profiles.FirstOrDefault();
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        bool removedAccountPseudoBuilds = false;
        try
        {
            if (File.Exists(_profilesPath))
            {
                var json = File.ReadAllText(_profilesPath, Encoding.UTF8);
                var saved = JsonSerializer.Deserialize<List<BuildProfile>>(json);
                if (saved != null)
                {
                    foreach (var p in saved)
                    {
                        // Акаунт персонажа не є білдом — мігруємо та прибираємо старі [Account] псевдо-профілі
                        if (p.SourceKind == BuildSourceKind.AccountSync ||
                            p.Name.StartsWith("[Account]", StringComparison.OrdinalIgnoreCase))
                        {
                            removedAccountPseudoBuilds = true;
                            continue;
                        }
                        NormalizeProfileStages(p);
                        Profiles.Add(p);
                    }
                }
            }
        }
        catch { }
        if (removedAccountPseudoBuilds) SaveProfiles();
    }

    public void SaveProfiles()
    {
        var json = JsonSerializer.Serialize(Profiles, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_profilesPath, json, Encoding.UTF8);
    }

    public void SelectProfile(string? id)
    {
        if (Profiles.Count == 0) { ActiveProfile = null; return; }
        ActiveProfile = Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles[0];
    }

    public bool DeleteProfile(string id)
    {
        var target = Profiles.FirstOrDefault(p => p.Id == id);
        if (target == null) return false;
        Profiles.Remove(target);
        if (ActiveProfile?.Id == id)
            ActiveProfile = Profiles.FirstOrDefault();
        SaveProfiles();
        return true;
    }

    public async Task<(bool ok, string error, BuildProfile? profile)> ImportFromPob2Async(string customName, string pobCodeOrUrl, CancellationToken ct = default)
    {
        var pob = await Pob2Parser.ResolveAndParseAsync(pobCodeOrUrl, ct);
        if (!pob.Success)
            return (false, pob.Error, null);

        string defFocus = InferDefenseFocus(pob.EnergyShield, pob.Evasion, pob.Armour);
        string dmgStyle = InferDamageStyle(string.Join(" ", pob.SkillNames));
        string title = string.IsNullOrWhiteSpace(customName)
            ? $"{pob.ClassName} ({(string.IsNullOrWhiteSpace(pob.Ascendancy) ? "PoB2" : pob.Ascendancy)})"
            : customName.Trim();

        var stages = pob.TreeSpecs.Select((ts, i) => new BuildStageEntry
        {
            Name = ts.Title,
            MinLevel = pob.TreeSpecs.Count == 1 ? Math.Max(1, pob.Level) : (i == pob.TreeSpecs.Count - 1 ? Math.Max(1, pob.Level) : 1),
            MaxLevel = 100,
            PassiveIds = [.. ts.NodeIds],
            SkillSignatures = pob.SkillGroups.Select(g => $"{g.MainSkill} -> {string.Join(", ", g.Supports)}").ToList(),
            GearSignatures = [.. pob.EquippedItems],
            GearDetailsBySlot = new(pob.RawEquippedBySlot, StringComparer.OrdinalIgnoreCase),
            Notes = i == pob.TreeSpecs.Count - 1 ? pob.Notes : ""
        }).ToList();

        var profile = new BuildProfile
        {
            Name = title,
            Author = "PoB2",
            SourceKind = BuildSourceKind.Pob2Import,
            PobSourceUrl = pob.SourceUrl,
            ClassName = pob.ClassName,
            Ascendancy = pob.Ascendancy,
            IsCi = pob.HasCi,
            DefenseFocus = defFocus,
            PreferredWeaponClass = pob.PrimaryWeaponClass,
            DamageStyle = dmgStyle,
            Pob2Code = string.IsNullOrWhiteSpace(pob.RawCode) ? pobCodeOrUrl.Trim() : pob.RawCode,
            Stages = stages,
            SummaryNotes = pob.Notes,
            TargetSkills = pob.SkillNames.Take(12).ToList(),
            TargetGearHints = pob.EquippedItems.Take(12).ToList(),
            LastPobSnapshotHash = ComputeHash(pob.RawContextSummary)
        };

        Profiles.Add(profile);
        ActiveProfile = profile;
        SaveProfiles();
        return (true, "", profile);
    }

    public (bool ok, string message, List<BuildProfile> added) BatchImportBuildFiles(
        string customName,
        IEnumerable<string> rawFilesOrFolders,
        bool mergeAsSingleProfileStages)
    {
        var sourceFiles = new List<string>();
        foreach (var path in rawFilesOrFolders)
        {
            if (File.Exists(path) && path.EndsWith(".build", StringComparison.OrdinalIgnoreCase))
                sourceFiles.Add(path);
            else if (Directory.Exists(path))
                sourceFiles.AddRange(Directory.GetFiles(path, "*.build"));
        }

        if (sourceFiles.Count == 0)
            return (false, Loc.T("Не знайдено валідних .build файлів.", "No valid .build files found.", "Не найдено валидных .build файлов."), []);

        // Копіюємо всі файли прямо в BuildPlanner гри!
        var (_, destPaths) = BuildPlannerService.ImportToGameBuildPlanner(sourceFiles.Distinct(StringComparer.OrdinalIgnoreCase));
        var plans = destPaths
            .Select(BuildPlannerService.TryParseFile)
            .OfType<LocalBuildPlan>()
            .OrderBy(p => p.MinSuggestedLevel)
            .ThenBy(p => p.PassivesCount)
            .ToList();

        if (plans.Count == 0)
            return (false, Loc.T("Не вдалося розібрати жоден .build файл.", "Failed to parse any .build file.", "Не удалось разобрать ни один .build файл."), []);

        var added = new List<BuildProfile>();
        if (mergeAsSingleProfileStages)
        {
            var profile = CreateProfileFromPlans(customName, plans);
            Profiles.Add(profile);
            ActiveProfile = profile;
            added.Add(profile);
        }
        else
        {
            foreach (var p in plans)
            {
                var single = CreateProfileFromPlans(plans.Count == 1 ? customName : "", [p]);
                Profiles.Add(single);
                ActiveProfile = single;
                added.Add(single);
            }
        }

        SaveProfiles();
        return (true, Loc.T($"Імпортовано: {plans.Count} .build файл(ів) у {added.Count} профіль(ів).", $"Imported {plans.Count} .build file(s) into {added.Count} profile(s).", $"Импортировано: {plans.Count} .build файл(ов) в {added.Count} профиль(ей)."), added);
    }

    private static BuildProfile CreateProfileFromPlans(string customName, List<LocalBuildPlan> plans)
    {
        var latest = plans.OrderByDescending(p => p.MaxSuggestedLevel).ThenByDescending(p => p.PassivesCount).First();
        string allGear = string.Join("\n", plans.SelectMany(p => p.Gear.Select(g => g.Text))).ToLowerInvariant();
        string allSkills = string.Join(" ", plans.SelectMany(p => p.Skills.Select(s => s.SkillName))).ToLowerInvariant();

        bool isCi = allGear.Contains("shavronne") || plans.Any(p => p.Name.Contains("CI", StringComparison.OrdinalIgnoreCase));
        string weapon = Pob2Parser.DetectWeaponClass(string.Join("\n", latest.Gear.Select(g => g.Text)));
        if (string.IsNullOrEmpty(weapon)) weapon = Pob2Parser.DetectWeaponClass(allGear);

        string defFocus = (allGear.Contains("energy shield") && allGear.Contains("evasion")) ? "EV_ES"
            : allGear.Contains("energy shield") ? "ES"
            : allGear.Contains("armour") && allGear.Contains("energy shield") ? "AR_ES"
            : allGear.Contains("armour") ? "ARMOUR"
            : allGear.Contains("evasion") ? "EV" : "HYBRID";

        string dmgStyle = InferDamageStyle(allSkills);

        string cleanTitle = string.IsNullOrWhiteSpace(customName)
            ? BuildCleanSeriesTitle(plans, latest)
            : customName.Trim();

        var stages = plans.Select(p =>
        {
            string gearTxt = string.Join("\n", p.Gear.Select(g => $"{g.RawSlotId}: {g.Text}"));
            return new BuildStageEntry
            {
                Name = p.Name,
                MinLevel = p.MinSuggestedLevel,
                MaxLevel = p.MaxSuggestedLevel,
                FilePath = p.FilePath,
                LastFileHash = p.ContentSha256,
                StageWeaponClass = Pob2Parser.DetectWeaponClass(gearTxt),
                PassiveIds = p.Passives.Select(x => x.Id).ToList(),
                SkillSignatures = p.Skills.Select(s => $"{s.SkillName} -> {string.Join(", ", s.Supports)}").ToList(),
                GearSignatures = p.Gear.Select(g => $"{g.RawSlotId}: {g.Text}").ToList()
            };
        }).ToList();

        return new BuildProfile
        {
            Name = cleanTitle,
            Author = latest.Author,
            GuideUrl = plans.Select(p => p.Link).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "",
            SourceKind = BuildSourceKind.BuildPlannerImport,
            Ascendancy = latest.Ascendancy,
            IsCi = isCi,
            DefenseFocus = defFocus,
            PreferredWeaponClass = weapon,
            DamageStyle = dmgStyle,
            BuildFolderOrFile = plans.Count == 1 ? latest.FilePath : Path.GetDirectoryName(latest.FilePath) ?? "",
            LinkedBuildFiles = plans.Select(p => p.FilePath).ToList(),
            Stages = stages,
            SummaryNotes = latest.Description,
            TargetSkills = latest.Skills.Select(s => s.SkillName).Distinct().Take(12).ToList(),
            TargetGearHints = latest.Gear.Select(g => $"{g.RawSlotId}: {g.Text.Split('\n')[0]}").Take(12).ToList()
        };
    }

    public async Task<(bool ok, string error, BuildProfile? profile)> ImportFromGuideUrlAsync(string customName, string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return (false, Loc.T("Некоректний URL.", "Invalid URL.", "Некорректный URL."), null);

        string pageTitle = uri.Segments.LastOrDefault()?.Trim('/') ?? uri.Host;
        string snapshotHash = "";
        string snapshotText = "";
        string updatedMarker = "";

        try
        {
            using var resp = await Http.GetAsync(uri, ct);
            if (resp.IsSuccessStatusCode)
            {
                string html = await resp.Content.ReadAsStringAsync(ct);
                var tm = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (tm.Success)
                    pageTitle = WebUtility.HtmlDecode(tm.Groups[1].Value).Trim();
                snapshotText = ExtractCleanTextFromHtml(html);
                snapshotHash = ComputeHash(snapshotText);
                updatedMarker = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
        }
        catch { }

        string name = string.IsNullOrWhiteSpace(customName) ? pageTitle : customName.Trim();
        var profile = new BuildProfile
        {
            Name = name.Length > 55 ? name[..54] + "…" : name,
            Author = uri.Host,
            GuideUrl = url.Trim(),
            SourceKind = BuildSourceKind.GuideUrlImport,
            LastGuideSnapshotHash = snapshotHash,
            LastGuideSnapshotText = snapshotText,
            LastOnlineUpdatedMarker = updatedMarker,
            SummaryNotes = url.Trim()
        };

        Profiles.Add(profile);
        ActiveProfile = profile;
        SaveProfiles();
        return (true, "", profile);
    }

    public async Task<(int okCount, int failCount, string summary)> BatchImportMultiLinesAsync(string multiLineText, CancellationToken ct = default)
    {
        var lines = (multiLineText ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        int ok = 0, fail = 0;
        foreach (string line in lines)
        {
            if (File.Exists(line) && line.EndsWith(".build", StringComparison.OrdinalIgnoreCase))
            {
                var (resOk, _, _) = BatchImportBuildFiles("", [line], mergeAsSingleProfileStages: false);
                if (resOk) ok++; else fail++;
            }
            else if (line.Contains("pobb.in", StringComparison.OrdinalIgnoreCase) || line.Contains("pastebin.com", StringComparison.OrdinalIgnoreCase) || !line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var (resOk, _, _) = await ImportFromPob2Async("", line, ct);
                if (resOk) ok++; else fail++;
            }
            else
            {
                var (resOk, _, _) = await ImportFromGuideUrlAsync("", line, ct);
                if (resOk) ok++; else fail++;
            }
        }
        return (ok, fail, Loc.T($"Пакетний імпорт: {ok} успішно, {fail} помилок.", $"Batch import: {ok} added, {fail} failed.", $"Пакетный импорт: {ok} добавлено, {fail} ошибок."));
    }

    private static string BuildCleanSeriesTitle(List<LocalBuildPlan> plans, LocalBuildPlan latest)
    {
        if (plans.Count == 1) return latest.Name;
        // Якщо серія має " - Назва білду", беремо спільну частину після тире
        int dash = latest.Name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0 && dash + 3 < latest.Name.Length)
            return $"{latest.Name[(dash + 3)..].Trim()} ({plans.Count})";
        return $"{latest.Name} (+{plans.Count - 1})";
    }

    private static string InferDefenseFocus(double es, double ev, double armour)
    {
        if (es > ev && es > armour) return "ES";
        if (ev > 0 && es > 0) return "EV_ES";
        if (armour > 0 && es > 0) return "AR_ES";
        if (armour > ev && armour > es) return "ARMOUR";
        if (ev > 0) return "EV";
        return "HYBRID";
    }

    private static string InferDamageStyle(string skillsLower)
    {
        if (skillsLower.Contains("minion") || skillsLower.Contains("skeletal") || skillsLower.Contains("zombie") || skillsLower.Contains("raging spirits"))
            return "Minion";
        if (skillsLower.Contains("spark") || skillsLower.Contains("arc") || skillsLower.Contains("comet") || skillsLower.Contains("fireball") || skillsLower.Contains("frostbolt") || skillsLower.Contains("essense drain"))
            return "Spell";
        return "Attack";
    }

    public static string ExtractCleanTextFromHtml(string html)
    {
        string noScript = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        string text = Regex.Replace(noScript, @"<[^>]+>", "\n");
        text = WebUtility.HtmlDecode(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length >= 3)
            .Take(250);
        var joined = string.Join("\n", lines);
        return joined.Length > 12000 ? joined[..12000] : joined;
    }

    public static string ComputeHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? "")))[..16];

    public static void NormalizeProfileStages(BuildProfile prof)
    {
        if (prof.Stages.Count == 0) return;
        foreach (var st in prof.Stages)
        {
            if (st.Name.Contains("act", StringComparison.OrdinalIgnoreCase) && st.MaxLevel <= 10)
            {
                var (minL, maxL) = BuildPlannerService.InferLevelWindow(st.Name, Path.GetFileName(st.FilePath));
                st.MinLevel = minL;
                st.MaxLevel = maxL;
            }
            if (string.IsNullOrWhiteSpace(st.StageWeaponClass))
            {
                string src = string.Join("\n", st.GearSignatures.Concat(st.GearDetailsBySlot.Select(kv => $"{kv.Key}: {kv.Value}")));
                st.StageWeaponClass = Pob2Parser.DetectWeaponClass(src);
            }
        }
        prof.Stages = prof.Stages
            .OrderBy(s => s.MinLevel)
            .ThenBy(s => s.PassiveIds.Count)
            .ToList();
    }

    public static string ResolveEffectiveStageWeaponClass(BuildProfile? profile, int stageIndex)
    {
        if (profile == null) return "";
        if (profile.Stages.Count > 0)
        {
            var st = profile.Stages[Math.Clamp(stageIndex, 0, profile.Stages.Count - 1)];
            if (st.Name.Contains("unarmed", StringComparison.OrdinalIgnoreCase) ||
                st.Name.Contains("hollow palm", StringComparison.OrdinalIgnoreCase))
                return "";
            if (!string.IsNullOrWhiteSpace(st.StageWeaponClass))
                return st.StageWeaponClass;
            string gearTxt = string.Join("\n", st.GearSignatures.Concat(st.GearDetailsBySlot.Select(kv => $"{kv.Key}: {kv.Value}")));
            string detected = Pob2Parser.DetectWeaponClass(gearTxt);
            if (!string.IsNullOrWhiteSpace(detected))
                return detected;
        }
        return profile.PreferredWeaponClass ?? "";
    }
}
