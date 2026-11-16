using System.Text.Json;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed class MobalyticsScrapedVariant
{
    public string title { get; set; } = "";
    public string url { get; set; } = "";
    public string author { get; set; } = "";
    public string className { get; set; } = "";
    public string ascendancy { get; set; } = "";
    public string variantName { get; set; } = "Default";
    public string pobCode { get; set; } = "";
    public List<string> variantNames { get; set; } = [];
    public List<List<string>> skillGroups { get; set; } = [];
    public List<MobalyticsGearEntry> gear { get; set; } = [];
    public string notes { get; set; } = "";
    public string pageHashText { get; set; } = "";
}

public sealed class MobalyticsGearEntry
{
    public string slot { get; set; } = "";
    public string name { get; set; } = "";
    public string details { get; set; } = "";
}

/// <summary>
/// Прямий імпорт білдів із mobalytics.gg (через видиме вікно WebView2 з підтримкою варіантів білду,
/// витягування вбудованого Path of Building 2 коду, скілів, екіпу й нотаток + fallback з буфера Ctrl+A Ctrl+C).
/// </summary>
public static class MobalyticsImportService
{
    public static bool IsMobalyticsUrl(string input)
        => !string.IsNullOrWhiteSpace(input) &&
           input.Trim().Contains("mobalytics.gg/", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeMobalyticsUrl(string raw)
    {
        string s = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return "https://mobalytics.gg/poe-2/builds";
        if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            s = "https://" + s;
        return s;
    }

    public const string ScrapeDomScript = """
(() => {
  const txt = el => (el && (el.innerText || el.textContent) || '').trim();
  const h1 = txt(document.querySelector('h1')) || document.title.replace(/\s*[-|]\s*Mobalytics.*$/i, '').trim();

  // Class / Ascendancy detection from badges/headings/text
  const bodyHead = txt(document.body).slice(0, 6000);
  const ascList = ['Invoker','Acolyte of Chayula','Stormweaver','Chronomancer','Infernalist','Blood Mage','Lich','Deadeye','Pathfinder','Witchhunter','Gemling Legionnaire','Tactician','Titan','Warbringer','Smith of Kitava','Amazon','Ritualist'];
  const clsList = ['Monk','Sorceress','Witch','Ranger','Mercenary','Warrior','Huntress','Druid'];
  let ascendancy = ascList.find(a => new RegExp('\\b' + a + '\\b', 'i').test(bodyHead)) || '';
  let className = clsList.find(c => new RegExp('\\b' + c + '\\b', 'i').test(bodyHead)) || '';

  // Author guess
  let author = 'Mobalytics';
  const authorLink = document.querySelector('a[href*="/profile/"], a[href*="/authors/"]');
  if (authorLink && txt(authorLink).length > 1 && txt(authorLink).length < 40) author = txt(authorLink);

  // Active Variant Tabs (role="tablist")
  let variantNames = [];
  let activeVariant = 'Main';
  const tablists = Array.from(document.querySelectorAll('[role="tablist"]'));
  for (const tl of tablists) {
    const tabs = Array.from(tl.querySelectorAll('[role="tab"], button'));
    if (tabs.length >= 2 && tabs.length <= 16) {
      const names = tabs.map(t => txt(t)).filter(Boolean);
      if (names.length >= 2) {
        variantNames = names;
        const sel = tabs.find(t => t.getAttribute('aria-selected') === 'true' || t.getAttribute('data-state') === 'active');
        if (sel) activeVariant = txt(sel);
        break;
      }
    }
  }

  // Extract Path of Building Code anywhere on the page
  let pobCode = '';
  const inputs = Array.from(document.querySelectorAll('input, textarea, code, pre'));
  for (const el of inputs) {
    const val = (el.value !== undefined ? el.value : (el.textContent || '')).trim();
    if (val.length > 120 && (/^(eNr|eNq)/.test(val) || val.includes('<PathOfBuilding'))) {
      pobCode = val.replace(/\s+/g, '');
      break;
    }
  }
  if (!pobCode) {
    const mPob = document.body.innerText.match(/\b(eN[rq][A-Za-z0-9_\-+=]{140,})/);
    if (mPob) pobCode = mPob[1];
  }

  // Find sections by heading
  const findSectionByHeading = (rx) => {
    const headings = Array.from(document.querySelectorAll('h1,h2,h3,h4'));
    for (const h of headings) {
      if (rx.test(txt(h))) {
        return h.closest('section') || h.parentElement?.parentElement || h.parentElement;
      }
    }
    return null;
  };

  // Skill Gems
  const skillGroups = [];
  const skillSec = findSectionByHeading(/skill\s*gems?/i) || document.body;
  const gemLinks = Array.from(skillSec.querySelectorAll('a[href*="/poe-2/database/gems/"]'));
  const groupMap = new Map();
  for (const a of gemLinks) {
    const gName = txt(a);
    if (!gName || gName.length > 48) continue;
    const row = a.closest('li, tr, div[class*="group"], div[class*="row"], div[class*="card"], p') || a.parentElement;
    if (!groupMap.has(row)) groupMap.set(row, []);
    const list = groupMap.get(row);
    if (!list.includes(gName)) list.push(gName);
  }
  for (const arr of groupMap.values()) {
    if (arr.length >= 1 && skillGroups.length < 18) skillGroups.push(arr.slice(0, 7));
  }

  // Equipment & Trade hints
  const gear = [];
  const eqSec = findSectionByHeading(/^equipment/i) || document.body;
  const eqLinks = Array.from(eqSec.querySelectorAll('a[href*="/poe-2/database/"], a[href*="pathofexile.com/trade2/search/"]'));
  const seenSlots = new Set();
  for (const a of eqLinks) {
    const href = a.getAttribute('href') || '';
    let slot = '';
    if (href.includes('trade2/search')) {
      const mCat = decodeURIComponent(href).match(/"category":\{"option":"([^"]+)"\}/);
      if (mCat) slot = mCat[1];
    }
    const card = a.closest('div[class*="slot"], div[class*="item"], div[class*="card"], li, tr') || a.parentElement;
    const rawName = txt(a);
    const details = txt(card).slice(0, 260);
    const key = (slot || rawName || details.slice(0, 30)).toLowerCase();
    if (!key || seenSlots.has(key) || gear.length >= 16) continue;
    seenSlots.add(key);
    gear.push({ slot: slot || 'Item', name: rawName || slot || 'Gear', details });
  }

  // Useful guide text notes
  const lines = txt(document.body).split('\n').map(l => l.trim()).filter(l => l.length >= 4 && l.length <= 240);
  const noteSnippets = [];
  for (let i = 0; i < lines.length && noteSnippets.length < 28; i++) {
    if (/regex|how it plays|quest reward|spirit|order of|leveling|chaos inoculation|passive/i.test(lines[i])) {
      noteSnippets.push(lines[i]);
    }
  }

  return JSON.stringify({
    title: h1,
    url: location.href,
    author,
    className,
    ascendancy,
    variantName: activeVariant,
    variantNames,
    pobCode,
    skillGroups,
    gear,
    notes: noteSnippets.join('\n'),
    pageHashText: lines.slice(0, 120).join('\n')
  });
})();
""";

    public static string BuildClickVariantScript(int tabIndex) => $$"""
(() => {
  const tablists = Array.from(document.querySelectorAll('[role="tablist"]'));
  for (const tl of tablists) {
    const tabs = Array.from(tl.querySelectorAll('[role="tab"], button'));
    if (tabs.length >= 2 && tabs.length <= 16) {
      if ({{tabIndex}} < tabs.length) {
        tabs[{{tabIndex}}].click();
        return (tabs[{{tabIndex}}].innerText || '').trim();
      }
    }
  }
  return '';
})();
""";

    public static (bool ok, string message, BuildProfile? profile) ImportFromScrapedVariants(
        BuildProfileManager mgr,
        List<MobalyticsScrapedVariant> variants,
        string customName = "")
    {
        if (variants.Count == 0)
            return (false, Loc.T("Не вдалося зчитати дані зі сторінки Mobalytics.", "Could not extract data from Mobalytics page.", "Не удалось прочитать данные со страницы Mobalytics."), null);

        var primary = variants[0];
        string title = !string.IsNullOrWhiteSpace(customName)
            ? customName.Trim()
            : (!string.IsNullOrWhiteSpace(primary.title) ? primary.title : "Mobalytics PoE 2 Build");
        if (title.Length > 68) title = title[..67] + "…";

        var stages = new List<BuildStageEntry>();
        var targetSkills = new List<string>();
        var targetGearHints = new List<string>();
        string bestPobCode = variants.Select(v => v.pobCode).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "";
        Pob2Summary? bestPob = !string.IsNullOrWhiteSpace(bestPobCode) ? Pob2Parser.Parse(bestPobCode) : null;
        if (bestPob is { Success: false }) bestPob = null;

        int autoLvl = 1;
        for (int i = 0; i < variants.Count; i++)
        {
            var v = variants[i];
            Pob2Summary? vpob = (!string.IsNullOrWhiteSpace(v.pobCode) ? Pob2Parser.Parse(v.pobCode) : null);
            if (vpob is { Success: false }) vpob = null;

            // Рівні з назви варіанта ("Lvl 1-23", "23+", "Endgame", "Act 3-6")
            var (minLvl, maxLvl) = InferLevelRange(v.variantName, i, variants.Count, ref autoLvl);

            if (vpob != null && vpob.TreeSpecs.Count > 1 && variants.Count == 1)
            {
                // Якщо PoB містить одразу кілька спеків дерева всередині 1 варіанта
                foreach (var ts in vpob.TreeSpecs)
                {
                    var tsRange = InferLevelRange(ts.Title, stages.Count, vpob.TreeSpecs.Count, ref autoLvl);
                    stages.Add(new BuildStageEntry
                    {
                        Name = ts.Title,
                        MinLevel = tsRange.min,
                        MaxLevel = tsRange.max,
                        PassiveIds = [.. ts.NodeIds],
                        SkillSignatures = vpob.SkillGroups.Select(g => $"{g.MainSkill} -> {string.Join(", ", g.Supports)}").ToList(),
                        GearSignatures = [.. vpob.EquippedItems],
                        GearDetailsBySlot = new(vpob.RawEquippedBySlot, StringComparer.OrdinalIgnoreCase),
                        Notes = v.notes
                    });
                }
                continue;
            }

            var stageSkills = new List<string>();
            if (v.skillGroups.Count > 0)
            {
                foreach (var grp in v.skillGroups)
                {
                    if (grp.Count == 0) continue;
                    string sig = grp.Count > 1 ? $"{grp[0]} -> {string.Join(", ", grp.Skip(1))}" : grp[0];
                    stageSkills.Add(sig);
                }
            }
            else if (vpob != null)
            {
                stageSkills.AddRange(vpob.SkillGroups.Select(g => $"{g.MainSkill} -> {string.Join(", ", g.Supports)}"));
            }

            var stageGearSigs = new List<string>();
            var gearBySlot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (vpob != null && vpob.RawEquippedBySlot.Count > 0)
            {
                stageGearSigs.AddRange(vpob.EquippedItems);
                foreach (var kv in vpob.RawEquippedBySlot) gearBySlot[kv.Key] = kv.Value;
            }
            foreach (var g in v.gear)
            {
                string cleanSlot = NormalizeMobaSlot(g.slot, g.name);
                string desc = string.IsNullOrWhiteSpace(g.details) ? g.name : g.details;
                string line = $"{cleanSlot}: {desc}";
                if (!stageGearSigs.Any(x => x.StartsWith(cleanSlot + ":", StringComparison.OrdinalIgnoreCase)))
                    stageGearSigs.Add(line);
                if (!gearBySlot.ContainsKey(cleanSlot))
                    gearBySlot[cleanSlot] = desc;
            }

            var passives = vpob?.TreeSpecs.LastOrDefault()?.NodeIds ?? [];
            stages.Add(new BuildStageEntry
            {
                Name = v.variantName,
                MinLevel = vpob?.Level > 1 ? Math.Min(minLvl, vpob.Level) : minLvl,
                MaxLevel = maxLvl,
                PassiveIds = [.. passives],
                SkillSignatures = stageSkills.Distinct().ToList(),
                GearSignatures = stageGearSigs,
                GearDetailsBySlot = gearBySlot,
                Notes = v.notes
            });

            targetSkills.AddRange(stageSkills);
            targetGearHints.AddRange(stageGearSigs);
        }

        if (bestPob != null)
        {
            if (string.IsNullOrWhiteSpace(primary.className)) primary.className = bestPob.ClassName;
            if (string.IsNullOrWhiteSpace(primary.ascendancy)) primary.ascendancy = bestPob.Ascendancy;
            targetSkills.AddRange(bestPob.SkillNames);
            targetGearHints.AddRange(bestPob.EquippedItems);
        }

        string combinedText = (string.Join(" ", targetSkills) + " " + string.Join(" ", targetGearHints) + " " + primary.notes).ToLowerInvariant();
        string defFocus = bestPob != null
            ? (bestPob.EnergyShield > bestPob.Armour && bestPob.EnergyShield > bestPob.Evasion ? "ES" : (bestPob.Evasion > 0 && bestPob.EnergyShield > 0 ? "EV_ES" : "HYBRID"))
            : (combinedText.Contains("chaos inoculation") || combinedText.Contains("energy shield")) && combinedText.Contains("evasion") ? "EV_ES"
            : combinedText.Contains("chaos inoculation") ? "ES" : "HYBRID";
        string weaponClass = !string.IsNullOrWhiteSpace(bestPob?.PrimaryWeaponClass)
            ? bestPob!.PrimaryWeaponClass
            : Pob2Parser.DetectWeaponClass(combinedText);

        var profile = new BuildProfile
        {
            Name = title,
            Author = string.IsNullOrWhiteSpace(primary.author) ? "Mobalytics" : primary.author,
            GuideUrl = primary.url,
            SourceKind = BuildSourceKind.MobalyticsImport,
            ClassName = primary.className,
            Ascendancy = primary.ascendancy,
            IsCi = bestPob?.HasCi == true || combinedText.Contains("chaos inoculation"),
            DefenseFocus = defFocus,
            PreferredWeaponClass = weaponClass,
            DamageStyle = combinedText.Contains("spell") || combinedText.Contains("spark") || combinedText.Contains("comet") ? "Spell" : "Attack",
            Pob2Code = bestPobCode,
            Stages = stages,
            SummaryNotes = primary.notes,
            TargetSkills = targetSkills.Distinct().Take(15).ToList(),
            TargetGearHints = targetGearHints.Distinct().Take(15).ToList(),
            LastGuideSnapshotText = primary.pageHashText,
            LastGuideSnapshotHash = BuildProfileManager.ComputeHash(primary.pageHashText),
            LastPobSnapshotHash = bestPob != null ? BuildProfileManager.ComputeHash(bestPob.RawContextSummary) : ""
        };

        mgr.Profiles.Add(profile);
        mgr.SelectProfile(profile.Id);
        mgr.SaveProfiles();

        string msg = Loc.T(
            $"Імпортовано з Mobalytics: «{profile.Name}» ({profile.Stages.Count} етап(ів){(bestPob != null ? ", +PoB2 дерево пасивок" : "")}).",
            $"Imported from Mobalytics: '{profile.Name}' ({profile.Stages.Count} stage(s){(bestPob != null ? ", +PoB2 tree" : "")}).",
            $"Импортировано с Mobalytics: «{profile.Name}» ({profile.Stages.Count} этап(ов){(bestPob != null ? ", +PoB2 дерево" : "")}).");
        return (true, msg, profile);
    }

    public static (bool ok, string message, BuildProfile? profile) ParseClipboardGuideText(
        BuildProfileManager mgr,
        string clipboardText,
        string customName = "",
        string guideUrl = "")
    {
        if (string.IsNullOrWhiteSpace(clipboardText) || clipboardText.Length < 20)
            return (false, Loc.T("Буфер порожній або занадто короткий.", "Clipboard text is empty or too short.", "Буфер пуст или слишком короткий."), null);

        // Перевіряємо, чи є всередині скопійований код PoB2
        var mPob = Regex.Match(clipboardText, @"\b(eN[rq][A-Za-z0-9_\-+=]{140,})");
        string pobCode = mPob.Success ? mPob.Groups[1].Value : "";

        var lines = clipboardText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string title = !string.IsNullOrWhiteSpace(customName)
            ? customName.Trim()
            : lines.FirstOrDefault(l => l.Length is >= 8 and <= 85 && !l.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? "Mobalytics Guide";

        // Розпізнаємо всі камені по нашій базі Poe2GemsDb
        var foundGems = new List<string>();
        foreach (var g in Poe2GemsDb.GetAllCatalogEntries())
        {
            if (clipboardText.Contains(g.Name, StringComparison.OrdinalIgnoreCase))
                foundGems.Add(g.Name);
        }

        var v = new MobalyticsScrapedVariant
        {
            title = title,
            url = guideUrl,
            author = "Mobalytics (Clipboard)",
            variantName = Loc.T("Скопійований гайд", "Copied Guide", "Скопированный гайд"),
            pobCode = pobCode,
            skillGroups = foundGems.Count > 0 ? foundGems.Take(10).Select(x => new List<string> { x }).ToList() : [],
            notes = string.Join("\n", lines.Where(l => l.Length >= 8).Take(25)),
            pageHashText = string.Join("\n", lines.Take(120))
        };

        return ImportFromScrapedVariants(mgr, [v], customName);
    }

    private static (int min, int max) InferLevelRange(string title, int index, int total, ref int autoLvl)
    {
        var mRange = Regex.Match(title ?? "", @"(\d{1,3})\s*[-–]\s*(\d{1,3})");
        if (mRange.Success && int.TryParse(mRange.Groups[1].Value, out int a) && int.TryParse(mRange.Groups[2].Value, out int b))
        {
            autoLvl = Math.Max(autoLvl, b + 1);
            return (a, b);
        }
        var mPlus = Regex.Match(title ?? "", @"(\d{1,3})\s*\+");
        if (mPlus.Success && int.TryParse(mPlus.Groups[1].Value, out int p))
        {
            autoLvl = Math.Max(autoLvl, p + 15);
            return (p, 100);
        }
        if ((title ?? "").Contains("endgame", StringComparison.OrdinalIgnoreCase) || (title ?? "").Contains("uber", StringComparison.OrdinalIgnoreCase))
            return (65, 100);
        int start = autoLvl;
        autoLvl = Math.Min(90, start + (total > 1 ? 22 : 99));
        return (index == 0 ? 1 : start, index == total - 1 ? 100 : autoLvl - 1);
    }

    private static string NormalizeMobaSlot(string rawSlot, string fallbackName)
    {
        string s = (rawSlot ?? "").ToLowerInvariant();
        if (s.Contains("weapon")) return "Weapon1";
        if (s.Contains("boots")) return "Boots1";
        if (s.Contains("gloves")) return "Gloves1";
        if (s.Contains("helmet") || s.Contains("helm")) return "Helm1";
        if (s.Contains("chest") || s.Contains("body")) return "BodyArmour1";
        if (s.Contains("amulet")) return "Amulet1";
        if (s.Contains("ring")) return "Ring1";
        if (s.Contains("belt")) return "Belt1";
        if (s.Contains("jewel")) return "Jewel";
        if (s.Contains("charm")) return "Charm1";
        return string.IsNullOrWhiteSpace(rawSlot) || rawSlot == "Item" ? fallbackName : rawSlot;
    }
}
