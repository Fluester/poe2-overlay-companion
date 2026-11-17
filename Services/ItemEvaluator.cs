using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public static class ItemEvaluator
{
    private static double? ExtractNumber(string pattern, string text)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val) ? val : null;
    }

    private static double? CalcWeaponPhysDps(string rawText)
    {
        double? aps = ExtractNumber(@"Attacks per Second:\s*([\d.]+)", rawText);
        var m = Regex.Match(rawText, @"Physical Damage:\s*(\d+)\s*-\s*(\d+)", RegexOptions.IgnoreCase);
        if (aps == null || !m.Success) return null;
        double avg = (int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value)) / 2.0;
        return avg * aps.Value;
    }

    private static double? CalcWeaponEdps(string rawText)
    {
        double? aps = ExtractNumber(@"Attacks per Second:\s*([\d.]+)", rawText);
        if (aps == null) return null;

        double avgHit = 0;
        foreach (Match em in Regex.Matches(rawText, @"(?:Elemental|Fire|Cold|Lightning|Chaos)\s+Damage:\s*([^\r\n]+)", RegexOptions.IgnoreCase))
        {
            foreach (Match r in Regex.Matches(em.Groups[1].Value, @"(\d+)\s*-\s*(\d+)"))
                avgHit += (int.Parse(r.Groups[1].Value) + int.Parse(r.Groups[2].Value)) / 2.0;
        }
        return avgHit > 0 ? avgHit * aps.Value : null;
    }

    private static (int eleRes, int chaosRes) SplitResistances(string rawText)
    {
        int ele = 0, chaos = 0;
        foreach (Match m in Regex.Matches(rawText, @"\+(\d+)%\s+to\s+Fire\s+and\s+(?:Cold|Lightning)\s+Resistances?|\+(\d+)%\s+to\s+Cold\s+and\s+Lightning\s+Resistances?", RegexOptions.IgnoreCase))
        {
            string rawVal = !string.IsNullOrEmpty(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
            if (int.TryParse(rawVal, out int dv)) ele += dv * 2;
        }
        foreach (Match m in Regex.Matches(rawText, @"\+(\d+)%\s+to\s+(Fire|Cold|Lightning|Chaos|all Elemental)\s+Resistances?(?!\s+and\b)", RegexOptions.IgnoreCase))
        {
            if (!int.TryParse(m.Groups[1].Value, out int val)) continue;
            string kind = m.Groups[2].Value;
            if (kind.StartsWith("Chaos", StringComparison.OrdinalIgnoreCase)) chaos += val;
            else if (kind.StartsWith("all", StringComparison.OrdinalIgnoreCase)) ele += val * 3;
            else ele += val;
        }
        return (ele, chaos);
    }

    private static int SumResistances(string rawText)
    {
        var (ele, chaos) = SplitResistances(rawText);
        return ele + chaos;
    }

    private static int SumSkillLevels(string rawText)
    {
        int max = 0;
        foreach (Match m in Regex.Matches(rawText, @"\+(\d+) to Level of (?:all )?[^%\n]*Skills?", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(m.Groups[1].Value, out int val) && val > max) max = val;
        }
        return max;
    }

    public static ItemEvaluation Evaluate(
        string rawText,
        BuildProfile? profile = null,
        int stageIndex = 0,
        IReadOnlyDictionary<string, string>? wornGear = null,
        int characterLevel = 1,
        IReadOnlyDictionary<string, MapModSafety>? customMapTags = null)
    {
        if (string.IsNullOrWhiteSpace(rawText) || !rawText.Contains("Item Class:") || !rawText.Contains("--------"))
            return new ItemEvaluation { IsPoeItem = false };

        var lines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToList();

        string itemClass = "";
        string rarity = "";
        var nameLines = new List<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("Item Class:"))
            {
                itemClass = lines[i].Split(':', 2)[1].Trim();
            }
            else if (lines[i].StartsWith("Rarity:"))
            {
                rarity = lines[i].Split(':', 2)[1].Trim();
                for (int j = i + 1; j < Math.Min(i + 4, lines.Count); j++)
                {
                    if (lines[j].StartsWith("--------")) break;
                    nameLines.Add(lines[j]);
                }
                break;
            }
        }

        string itemName = nameLines.Count > 0 ? string.Join(" — ", nameLines) : itemClass;
        string rawBase = nameLines.Count > 1 ? nameLines[^1] : (nameLines.FirstOrDefault() ?? itemClass);
        int ilvl = (int)(ExtractNumber(@"Item Level:\s*(\d+)", rawText) ?? 0);

        var ev = new ItemEvaluation
        {
            IsPoeItem = true,
            ItemName = itemName,
            ItemClass = itemClass,
            RawBaseType = rawBase,
            Rarity = rarity,
            ItemLevel = ilvl,
            RawCopiedText = rawText,
            PhysicalDps = CalcWeaponPhysDps(rawText),
            ElementalDps = CalcWeaponEdps(rawText),
            EnergyShield = ExtractNumber(@"Energy Shield:\s*(\d+)", rawText) ?? 0,
            Evasion = ExtractNumber(@"Evasion Rating:\s*(\d+)", rawText) ?? 0,
            Armour = ExtractNumber(@"Armour:\s*(\d+)", rawText) ?? 0,
            MovementSpeed = ExtractNumber(@"(\d+)% increased Movement Speed", rawText) ?? 0,
            PlusSkillLevel = SumSkillLevels(rawText),
            TotalResistance = SumResistances(rawText),
            SpiritBonus = ExtractNumber(@"\+(\d+) to Spirit", rawText) ?? 0
        };

        string ic = itemClass.ToLowerInvariant();
        string rawLower = rawText.ToLowerInvariant();
        bool isCi = profile?.IsCi ?? false;
        string profileLabel = profile != null && !string.IsNullOrWhiteSpace(profile.Name)
            ? profile.Name
            : Loc.T("Загальний PoE 2", "Universal PoE 2", "Общий PoE 2");

        // 0. Waystones / Мапи (інтерактивний Map Mod Checker у стилі Exiled Exchange 2 / Sidekick)
        if (ic.Contains("waystone") || ic == "maps" || (rawLower.Contains("waystone") && rawLower.Contains("area level")))
        {
            ev.IsWaystone = true;
            var mods = WaystoneModChecker.EvaluateWaystone(rawText, profile, customMapTags);
            ev.WaystoneMods.AddRange(mods);

            int deadly = mods.Count(m => m.Safety == MapModSafety.Deadly);
            int caution = mods.Count(m => m.Safety == MapModSafety.Caution);
            int good = mods.Count(m => m.Safety == MapModSafety.Good);

            foreach (var m in mods)
            {
                if (m.Safety == MapModSafety.Deadly) ev.Warnings.Add($"[DEADLY] {m.ModText} ({m.Reason})");
                else if (m.Safety == MapModSafety.Caution) ev.Warnings.Add($"[CAUTION] {m.ModText}");
                else ev.Positives.Add(m.ModText);
            }

            ev.Verdict = deadly > 0 ? ItemVerdict.Danger : caution >= 2 ? ItemVerdict.Warn : good >= 2 ? ItemVerdict.Great : ItemVerdict.Good;
            ev.UpgradeVerdict = deadly > 0
                ? Loc.T($"[!] НЕБЕЗПЕЧНА МАПА ({deadly} deadly)", $"[!] DEADLY WAYSTONE ({deadly} mods)", $"[!] ОПАСНАЯ КАРТА ({deadly})")
                : caution >= 1
                    ? Loc.T($"[~] ОБЕРЕЖНО ({caution} caution)", $"[~] RUN WITH CAUTION ({caution})", $"[~] ОСТОРОЖНО ({caution})")
                    : Loc.T("[+] БЕЗПЕЧНА МАПА", "[+] SAFE WAYSTONE", "[+] БЕЗОПАСНАЯ КАРТА");
            ev.Summary = Loc.T($"Перевірка Waystone: {deadly} небезпеч., {caution} обережно, {good} ок", $"Waystone Check: {deadly} deadly, {caution} caution, {good} good", $"Проверка карты: {deadly} опасн., {caution} остор., {good} ок");
            ev.WikiUrl = $"https://poe2db.tw/us/search?q={Uri.EscapeDataString("Waystone")}";
            ev.GeneratedTradeUrl = TradeLinkBuilder.BuildForCopiedItem("Standard", ev);
            return ev;
        }

        // 1. Flasks
        if (ic.Contains("mana flask"))
        {
            if (Regex.IsMatch(rawText, @"removed from life", RegexOptions.IgnoreCase))
            {
                if (isCi)
                {
                    ev.Verdict = ItemVerdict.Danger;
                    ev.Summary = Loc.T("КРИТИЧНО ДЛЯ CI! Миттєва смерть при натисканні!", "FATAL FOR CI! Kills you on use!", "КРИТИЧНО ДЛЯ CI! Мгновенная смерть при нажатии!");
                    ev.Warnings.Add(Loc.T("Має '% mana recovered is removed from Life' при активному Chaos Inoculation.", "Has '% mana recovered is removed from Life' with CI allocated.", "Имеет '% mana recovered is removed from Life' при активном CI."));
                }
                else
                {
                    ev.Verdict = ItemVerdict.Warn;
                    ev.Summary = Loc.T("Фласка забирає Life (небезпечно, якщо плануєш CI).", "Drains Life on use (unsafe if transitioning to CI).", "Фласка забирает Life (опасно при переходе в CI).");
                    ev.Warnings.Add("Mod: '% mana recovered is removed from Life'");
                }
                FinalizeAnalysis(ev, rawText, rawLower, profile, stageIndex, wornGear, characterLevel);
                return ev;
            }

            bool instant = Regex.IsMatch(rawText, @"instant recovery|recovered instantly", RegexOptions.IgnoreCase);
            if (instant) ev.Positives.Add("Instant Recovery");
            ev.Verdict = instant ? ItemVerdict.Good : ItemVerdict.Ok;
            ev.Summary = Loc.T($"Безпечна Mana Flask ({profileLabel})", $"Safe Mana Flask ({profileLabel})", $"Безопасная Mana Flask ({profileLabel})");
            FinalizeAnalysis(ev, rawText, rawLower, profile, stageIndex, wornGear, characterLevel);
            return ev;
        }

        if (ic.Contains("life flask"))
        {
            bool instant = Regex.IsMatch(rawText, @"instant recovery|recovered instantly", RegexOptions.IgnoreCase);
            if (instant) ev.Positives.Add("Instant Recovery");
            if (Regex.IsMatch(rawText, @"increased amount recovered", RegexOptions.IgnoreCase)) ev.Positives.Add("+Recovery Amount");
            ev.Verdict = instant ? ItemVerdict.Great : ItemVerdict.Ok;
            ev.Summary = Loc.T($"Оцінка Life Flask ({profileLabel})", $"Life Flask Check ({profileLabel})", $"Оценка Life Flask ({profileLabel})");
            FinalizeAnalysis(ev, rawText, rawLower, profile, stageIndex, wornGear, characterLevel);
            return ev;
        }

        // 2. Charms
        if (ic.Contains("charm"))
        {
            if (rawLower.Contains("slow")) ev.Positives.Add("Anti-Slow");
            if (rawLower.Contains("stun")) ev.Positives.Add("Anti-Stun");
            if (rawLower.Contains("freeze") || rawLower.Contains("thawing")) ev.Positives.Add("Anti-Freeze");
            if (rawLower.Contains("bleed")) ev.Positives.Add("Anti-Bleed");
            if (rawLower.Contains("poison")) ev.Positives.Add("Anti-Poison");
            ev.Verdict = ev.Positives.Count > 0 ? ItemVerdict.Good : ItemVerdict.Ok;
            ev.Summary = Loc.T($"Талісман ({itemClass})", $"Charm ({itemClass})", $"Чарм ({itemClass})");
            FinalizeAnalysis(ev, rawText, rawLower, profile, stageIndex, wornGear, characterLevel);
            return ev;
        }

        // 3. Weapons (враховує зброю САМЕ поточного етапу по рівню, а не сліпо endgame!)
        if (IsWeaponClass(ic))
        {
            double totalDps = ev.TotalDps;
            string expectedWep = BuildProfileManager.ResolveEffectiveStageWeaponClass(profile, stageIndex);
            if (!string.IsNullOrEmpty(expectedWep))
            {
                if (AreSameWeaponFamily(itemClass, expectedWep))
                    ev.Positives.Add(Loc.T($"Клас зброї підходить етапу ({expectedWep})", $"Matches active stage weapon ({expectedWep})", $"Класс оружия подходит этапу ({expectedWep})"));
                else
                    ev.Warnings.Add(Loc.T($"На цьому етапі білд використовує {PoeTermLocalizer.WeaponClassLabel(expectedWep)}", $"Current stage uses {expectedWep}, item is {itemClass}", $"На этом этапе билд использует {PoeTermLocalizer.WeaponClassLabel(expectedWep)}"));
            }

            if (ev.PhysicalDps >= 40) ev.Positives.Add($"pDPS: {ev.PhysicalDps:F0}");
            if (ev.ElementalDps >= 40) ev.Positives.Add($"eDPS: {ev.ElementalDps:F0}");
            if (ev.PlusSkillLevel > 0) ev.Positives.Add($"+{ev.PlusSkillLevel} Skills");
            if (Regex.IsMatch(rawText, @"Critical Hit Chance|Critical Damage|Spell Critical", RegexOptions.IgnoreCase)) ev.Positives.Add(Loc.T("Кріт шанс/бонус", "Critical Mods", "Крит шанс/бонус"));
            if (Regex.IsMatch(rawText, @"Attack Speed|Cast Speed", RegexOptions.IgnoreCase)) ev.Positives.Add(Loc.T("Швидкість атаки/касту", "Attack / Cast Speed", "Скорость атаки/каста"));
            if (Regex.IsMatch(rawText, @"increased Spell Damage|Gain \d+% of Damage as Extra", RegexOptions.IgnoreCase)) ev.Positives.Add("% Spell / Extra Dmg");
            if (ev.SpiritBonus > 0) ev.Positives.Add($"+{(int)ev.SpiritBonus} Spirit");

            ev.Verdict = ev.Warnings.Count > 0 ? ItemVerdict.Warn
                : (ev.Positives.Count >= 3 || totalDps >= 350 ? ItemVerdict.Great : ev.Positives.Count >= 2 ? ItemVerdict.Good : ItemVerdict.Ok);
            string dpsNote = totalDps > 1 ? $" | ~{totalDps:F0} DPS" : "";
            ev.Summary = Loc.T($"Зброя для «{profileLabel}»{dpsNote}", $"Weapon for '{profileLabel}'{dpsNote}", $"Оружие для «{profileLabel}»{dpsNote}");
            CheckBuildTargets(ev, rawLower, profile);
            FinalizeAnalysis(ev, rawText, rawLower, profile, stageIndex, wornGear, characterLevel);
            return ev;
        }

        // 4. Armour & Defense
        if (ic.Contains("helmet") || ic.Contains("body armour") || ic.Contains("gloves") || ic.Contains("boots") || ic.Contains("shield") || ic.Contains("focus") || ic.Contains("buckler"))
        {
            int resCount = Regex.Matches(rawText, @"to (?:Fire|Cold|Lightning|Chaos|all Elemental) Resistance", RegexOptions.IgnoreCase).Count;
            bool hasLife = Regex.IsMatch(rawText, @"to maximum Life", RegexOptions.IgnoreCase);

            string focus = profile?.DefenseFocus ?? "HYBRID";
            if (ev.EnergyShield > 0 && focus is "ES" or "EV_ES" or "AR_ES" or "HYBRID") ev.Positives.Add($"ES: {(int)ev.EnergyShield}");
            if (ev.Evasion > 0 && focus is "EV" or "EV_ES" or "HYBRID") ev.Positives.Add($"Evasion: {(int)ev.Evasion}");
            if (ev.Armour > 0 && focus is "ARMOUR" or "AR_ES" or "HYBRID") ev.Positives.Add($"Armour: {(int)ev.Armour}");

            if (ic.Contains("boots"))
            {
                if (ev.MovementSpeed > 0) ev.Positives.Add($"{(int)ev.MovementSpeed}% MS");
                else ev.Missing.Add(Loc.T("Швидкість руху (Movement Speed)", "Movement Speed", "Скорость бега (Movement Speed)"));
            }

            if (ev.SpiritBonus > 0) ev.Positives.Add($"+{(int)ev.SpiritBonus} Spirit");
            if (!isCi && hasLife) ev.Positives.Add("+Maximum Life");
            if (isCi && hasLife && ev.EnergyShield < 80)
                ev.Warnings.Add(Loc.T("Білду з CI потрібен Energy Shield замість Life.", "CI build wants Energy Shield instead of Life.", "Билду на CI нужен Energy Shield вместо Life."));
            if (resCount > 0) ev.Positives.Add(Loc.T($"{resCount}x опір (~{ev.TotalResistance}%)", $"{resCount}x Res (~{ev.TotalResistance}%)", $"{resCount}x сопр. (~{ev.TotalResistance}%)"));

            ev.Verdict = ev.Warnings.Count > 0 ? ItemVerdict.Warn : (ev.Positives.Count >= 3 ? ItemVerdict.Great : ev.Positives.Count >= 2 ? ItemVerdict.Good : ItemVerdict.Ok);
            ev.Summary = Loc.T($"Броня ({PoeTermLocalizer.DefenseLabel(focus)}) · {profileLabel}", $"Armour ({focus}) · {profileLabel}", $"Броня ({PoeTermLocalizer.DefenseLabel(focus)}) · {profileLabel}");
            CheckBuildTargets(ev, rawLower, profile);
            FinalizeAnalysis(ev, rawText, rawLower, profile, stageIndex, wornGear, characterLevel);
            return ev;
        }

        // 5. Jewellery, Belts, Jewels, Quivers
        int resAny = Regex.Matches(rawText, @"to (?:Fire|Cold|Lightning|Chaos|all Elemental) Resistance", RegexOptions.IgnoreCase).Count;
        bool flatDmg = Regex.IsMatch(rawText, @"Adds \d+ to \d+ (?:Physical|Fire|Cold|Lightning|Chaos) Damage", RegexOptions.IgnoreCase);
        bool critAny = Regex.IsMatch(rawText, @"Critical", RegexOptions.IgnoreCase);
        bool hasLifeMod = Regex.IsMatch(rawText, @"to maximum Life", RegexOptions.IgnoreCase);
        bool hasEsMod = Regex.IsMatch(rawText, @"to maximum Energy Shield", RegexOptions.IgnoreCase);

        if (ev.PlusSkillLevel > 0) ev.Positives.Add($"+{ev.PlusSkillLevel} Skills");
        if (ev.SpiritBonus > 0) ev.Positives.Add($"+{(int)ev.SpiritBonus} Spirit");
        if (flatDmg) ev.Positives.Add(Loc.T("Плоский урон", "Flat Damage", "Плоский урон"));
        if (critAny) ev.Positives.Add(Loc.T("Кріт", "Crit", "Крит"));
        if (!isCi && hasLifeMod) ev.Positives.Add("+Maximum Life");
        else if (hasEsMod) ev.Positives.Add("+Energy Shield");
        if (resAny > 0) ev.Positives.Add(Loc.T($"{resAny}x опір (~{ev.TotalResistance}%)", $"{resAny}x Res (~{ev.TotalResistance}%)", $"{resAny}x рез. (~{ev.TotalResistance}%)"));

        CheckBuildTargets(ev, rawLower, profile);
        ev.Verdict = ev.Positives.Count >= 3 ? ItemVerdict.Great : ev.Positives.Count >= 2 ? ItemVerdict.Good : ItemVerdict.Info;
        ev.Summary = Loc.T($"Оцінка ({itemClass}) · {profileLabel}", $"Check ({itemClass}) · {profileLabel}", $"Оценка ({itemClass}) · {profileLabel}");
        FinalizeAnalysis(ev, rawText, rawLower, profile, stageIndex, wornGear, characterLevel);
        return ev;
    }

    private static bool AreSameWeaponFamily(string itemClass, string expectedWeapon)
    {
        string norm(string s)
        {
            string l = (s ?? "").Trim().ToLowerInvariant();
            if (l.Contains("quarterst")) return "quarterstaff";
            if (l.Contains("crossbow")) return "crossbow";
            if (l.Contains("bow")) return "bow";
            if (l.Contains("spear")) return "spear";
            if (l.Contains("wand")) return "wand";
            if (l.Contains("sceptre")) return "sceptre";
            if (l.Contains("talisman")) return "talisman";
            if (l.Contains("flail")) return "flail";
            if (l.Contains("sword")) return "sword";
            if (l.Contains("axe")) return "axe";
            if (l.Contains("mace")) return "mace";
            if (l.Contains("dagger")) return "dagger";
            if (l.Contains("claw")) return "claw";
            if (l.Contains("staff") || l.Contains("staves")) return "staff";
            return l;
        }
        return norm(itemClass) == norm(expectedWeapon);
    }

    private static bool IsWeaponClass(string icLower)
        => icLower.Contains("quarterstaff") || icLower.Contains("quarterstaves") || icLower.Contains("bow") ||
           icLower.Contains("crossbow") || icLower.Contains("wand") || icLower.Contains("staff") ||
           icLower.Contains("staves") || icLower.Contains("mace") || icLower.Contains("spear") ||
           icLower.Contains("sceptre") || icLower.Contains("talisman") || icLower.Contains("flail") ||
           icLower.Contains("sword") || icLower.Contains("axe") || icLower.Contains("dagger") || icLower.Contains("claw");

    private static void CheckBuildTargets(ItemEvaluation ev, string rawLower, BuildProfile? profile)
    {
        if (profile == null) return;
        foreach (var mod in profile.PreferredMods)
        {
            if (mod.Length >= 4 && rawLower.Contains(mod.ToLowerInvariant()))
                ev.Positives.Add(Loc.T($"Ціль білду: {mod}", $"Build target: {mod}", $"Цель билда: {mod}"));
        }
    }

    private static void FinalizeAnalysis(ItemEvaluation ev, string rawText, string rawLower, BuildProfile? profile, int stageIndex, IReadOnlyDictionary<string, string>? wornGear, int characterLevel = 1)
    {
        // 1. Де отримати / опис походження (строго з урахуванням рівня персонажа)
        int effLvl = characterLevel > 1 ? characterLevel : (ev.ItemLevel > 0 ? ev.ItemLevel : 1);
        var obtain = Poe2KnowledgeDb.ResolveItemObtain(rawText, ev.ItemClass, profile, effLvl);
        string src = Loc.T(obtain.ObtainSourceUk, obtain.ObtainSourceEn, obtain.ObtainSourceRu);
        string tip = Loc.T(obtain.StrategyTipUk, obtain.StrategyTipEn, obtain.StrategyTipRu);
        ev.ObtainInfo = $"{obtain.MinItemOrAreaLevel} · {src}\n{tip}";

        // 2. ПОРІВНЯННЯ: і до твого еквіпнутого зараз, і до еталона активного етапу білду
        CompareAgainstBuildSlot(ev, profile, stageIndex, wornGear, characterLevel);

        // 3. Економічна вигода
        EvaluateMarketProfitability(ev, rawText, rawLower);

        // 4. Моди для інтерактивного прайс-чеку з чекбоксами (+ Pseudo Totals)
        PopulateSelectableMods(ev, rawText);

        // 5. Affix Prefix/Suffix лічильник (Sidekick стиль) та прямий лінк на PoE2DB / Wiki (EE2 Alt+W)
        PopulateWikiAndAffixes(ev, rawText);

        // 6. Trade2 лінк конкретно під цю річ
        ev.GeneratedTradeUrl = TradeLinkBuilder.BuildForCopiedItem("Standard", ev);
    }

    private static string ExtractModBody(string rawText)
    {
        int sepIdx = rawText.IndexOf("--------", StringComparison.Ordinal);
        return sepIdx >= 0 ? rawText[(sepIdx + 8)..] : rawText;
    }

    private static void PopulateWikiAndAffixes(ItemEvaluation ev, string rawText)
    {
        string wikiQuery = ev.Rarity.Equals("Unique", StringComparison.OrdinalIgnoreCase)
            ? ev.ItemName.Split(" — ")[0].Trim()
            : ev.RawBaseType;
        if (string.IsNullOrWhiteSpace(wikiQuery)) wikiQuery = ev.ItemClass;
        ev.WikiUrl = $"https://poe2db.tw/us/search?q={Uri.EscapeDataString(wikiQuery)}";

        if (!ev.Rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase) &&
            !ev.Rarity.Equals("Magic", StringComparison.OrdinalIgnoreCase))
            return;

        int maxPerSide = ev.Rarity.Equals("Magic", StringComparison.OrdinalIgnoreCase) ? 1 : 3;
        int pCount = 0, sCount = 0;

        string modSectionText = ExtractModBody(rawText);
        foreach (string rawLine in modSectionText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length < 5 || line.Contains(':') || line.StartsWith("--------") || line.StartsWith("{")) continue;
            if (line.Contains("(implicit)", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("(enchant)", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("(rune)", StringComparison.OrdinalIgnoreCase))
                continue;

            string low = line.ToLowerInvariant();
            if (low.Contains("resistance") || low.Contains("strength") || low.Contains("dexterity") ||
                low.Contains("intelligence") || low.Contains("all attributes") ||
                low.Contains("attack speed") || low.Contains("cast speed") ||
                low.Contains("critical") || low.Contains("level of all") ||
                low.Contains("regenerate") || low.Contains("leech") || low.Contains("accuracy") ||
                low.Contains("stun threshold") || low.Contains("reduced attribute"))
            {
                sCount++;
            }
            else if (low.Contains("maximum life") || low.Contains("maximum mana") ||
                     low.Contains("energy shield") || low.Contains("evasion") || low.Contains("armour") ||
                     low.Contains("movement speed") || low.Contains("physical damage") ||
                     low.Contains("adds ") || low.Contains("spell damage") || low.Contains("rarity of items") ||
                     low.Contains("spirit"))
            {
                pCount++;
            }
        }

        ev.EstimatedPrefixes = Math.Min(maxPerSide, pCount);
        ev.EstimatedSuffixes = Math.Min(maxPerSide, sCount);
        int openP = Math.Max(0, maxPerSide - ev.EstimatedPrefixes);
        int openS = Math.Max(0, maxPerSide - ev.EstimatedSuffixes);
        int totalOpen = openP + openS;
        ev.AffixSummary = totalOpen > 0
            ? Loc.T($"Афікси ~{ev.EstimatedPrefixes}P/{ev.EstimatedSuffixes}S · вільно {totalOpen} ({openP}P/{openS}S під Exalt)", $"Affixes ~{ev.EstimatedPrefixes}P/{ev.EstimatedSuffixes}S · {totalOpen} open ({openP}P/{openS}S Exalt)", $"Аффиксы ~{ev.EstimatedPrefixes}P/{ev.EstimatedSuffixes}S · своб. {totalOpen}")
            : Loc.T($"Афікси ~{ev.EstimatedPrefixes}P/{ev.EstimatedSuffixes}S (повна)", $"Affixes ~{ev.EstimatedPrefixes}P/{ev.EstimatedSuffixes}S (full)", $"Аффиксы ~{ev.EstimatedPrefixes}P/{ev.EstimatedSuffixes}S (полная)");
    }

    private static readonly (Regex pattern, string statId, bool defaultCheck)[] TradeModMatchers =
    [
        (new Regex(@"([+\-]?\d+)% increased Movement Speed", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_2250533757", true),
        (new Regex(@"([+\-]?\d+) to Spirit", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_3981240776", true),
        (new Regex(@"([+\-]?\d+) to Level of all Spell Skills", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_124131830", true),
        (new Regex(@"([+\-]?\d+) to Level of all Attack Skills", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_3035140377", true),
        (new Regex(@"([+\-]?\d+) to Level of all Projectile Skills", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_1202301673", true),
        (new Regex(@"([+\-]?\d+) to Level of all Melee Skills", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_9187492", true),
        (new Regex(@"([+\-]?\d+) to Level of all Minion Skills", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_2162097452", true),
        (new Regex(@"([+\-]?\d+) to maximum Life", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_3299347043", true),
        (new Regex(@"([+\-]?\d+) to maximum Energy Shield", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_3489782002", true),
        (new Regex(@"([+\-]?\d+) to maximum Mana", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_1050105434", false),
        (new Regex(@"([+\-]?\d+)% to all Elemental Resistances", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_2901986750", true),
        (new Regex(@"([+\-]?\d+)% to Fire Resistance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_3372524247", true),
        (new Regex(@"([+\-]?\d+)% to Cold Resistance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_4220027924", true),
        (new Regex(@"([+\-]?\d+)% to Lightning Resistance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_1671376347", true),
        (new Regex(@"([+\-]?\d+)% to Chaos Resistance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_2923486259", true),
        (new Regex(@"([+\-]?\d+)% increased Attack Speed", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_681332047", true),
        (new Regex(@"([+\-]?\d+)% increased Cast Speed", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_2891184298", true),
        (new Regex(@"([+\-]?\d+)% increased Critical Hit Chance$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_587431675", false),
        (new Regex(@"([+\-]?\d+) to all Attributes", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_1379411836", false),
        (new Regex(@"([+\-]?\d+) to Strength", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_4080418644", false),
        (new Regex(@"([+\-]?\d+) to Dexterity", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_3261801346", false),
        (new Regex(@"([+\-]?\d+) to Intelligence", RegexOptions.IgnoreCase | RegexOptions.Compiled), "explicit.stat_328541901", false),
    ];

    private static void PopulateSelectableMods(ItemEvaluation ev, string rawText)
    {
        if (ev.TotalDps >= 120)
        {
            int minDps = (int)Math.Round(ev.TotalDps * 0.85);
            ev.SelectableMods.Add(new ItemModFilterOption
            {
                DisplayText = $"Total DPS >= {minDps} (item: {ev.TotalDps:F0})",
                TradeStatId = "equip.dps",
                NumericValue = ev.TotalDps,
                MinSuggested = minDps,
                IsDefaultChecked = true,
                Category = "Dps"
            });
        }
        if (ev.EnergyShield >= 100)
        {
            int minEs = (int)Math.Round(ev.EnergyShield * 0.85);
            ev.SelectableMods.Add(new ItemModFilterOption
            {
                DisplayText = $"Energy Shield >= {minEs} (item: {(int)ev.EnergyShield})",
                TradeStatId = "equip.es",
                NumericValue = ev.EnergyShield,
                MinSuggested = minEs,
                IsDefaultChecked = true,
                Category = "Defense"
            });
        }
        if (ev.Evasion >= 150)
        {
            int minEv = (int)Math.Round(ev.Evasion * 0.85);
            ev.SelectableMods.Add(new ItemModFilterOption
            {
                DisplayText = $"Evasion >= {minEv} (item: {(int)ev.Evasion})",
                TradeStatId = "equip.ev",
                NumericValue = ev.Evasion,
                MinSuggested = minEv,
                IsDefaultChecked = ev.EnergyShield < 100,
                Category = "Defense"
            });
        }
        if (ev.Armour >= 180)
        {
            int minAr = (int)Math.Round(ev.Armour * 0.85);
            ev.SelectableMods.Add(new ItemModFilterOption
            {
                DisplayText = $"Armour >= {minAr} (item: {(int)ev.Armour})",
                TradeStatId = "equip.ar",
                NumericValue = ev.Armour,
                MinSuggested = minAr,
                IsDefaultChecked = ev.EnergyShield < 100 && ev.Evasion < 150,
                Category = "Defense"
            });
        }

        // Pseudo сумарні моди (як в Exiled Exchange 2: сумарні елементальні резисти, атрибути)
        int eleResTotal = 0;
        int attrTotal = 0;
        string modBody = ExtractModBody(rawText);
        var addedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in modBody.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length < 5 || line.Contains(':') || line.StartsWith("--------")) continue;
            string cleanLine = Regex.Replace(line, @"\s*\((?:implicit|fractured|enchant|rune|desecrated)\)\s*$", "", RegexOptions.IgnoreCase).Trim();

            var mAllEle = Regex.Match(cleanLine, @"([+\-]?\d+)% to all Elemental Resistances", RegexOptions.IgnoreCase);
            if (mAllEle.Success && int.TryParse(mAllEle.Groups[1].Value, out int ae)) eleResTotal += ae * 3;
            else
            {
                var mSingleEle = Regex.Match(cleanLine, @"([+\-]?\d+)% to (?:Fire|Cold|Lightning) Resistance", RegexOptions.IgnoreCase);
                if (mSingleEle.Success && int.TryParse(mSingleEle.Groups[1].Value, out int se)) eleResTotal += se;
            }

            var mAllAttr = Regex.Match(cleanLine, @"([+\-]?\d+) to all Attributes", RegexOptions.IgnoreCase);
            if (mAllAttr.Success && int.TryParse(mAllAttr.Groups[1].Value, out int aa)) attrTotal += aa * 3;
            else
            {
                var mSingleAttr = Regex.Match(cleanLine, @"([+\-]?\d+) to (?:Strength|Dexterity|Intelligence)$", RegexOptions.IgnoreCase);
                if (mSingleAttr.Success && int.TryParse(mSingleAttr.Groups[1].Value, out int sa)) attrTotal += sa;
            }
        }

        if (eleResTotal >= 25)
        {
            int minPseudoEle = Math.Max(20, (int)Math.Floor(eleResTotal * 0.80));
            ev.SelectableMods.Add(new ItemModFilterOption
            {
                DisplayText = $"+{eleResTotal}% Total Elemental Resistance (Pseudo)",
                TradeStatId = "pseudo.pseudo_total_elemental_resistance",
                NumericValue = eleResTotal,
                MinSuggested = minPseudoEle,
                IsDefaultChecked = eleResTotal >= 40,
                Category = "Pseudo"
            });
        }
        if (attrTotal >= 22)
        {
            int minPseudoAttr = Math.Max(15, (int)Math.Floor(attrTotal * 0.80));
            ev.SelectableMods.Add(new ItemModFilterOption
            {
                DisplayText = $"+{attrTotal} Total Attributes (Pseudo)",
                TradeStatId = "pseudo.pseudo_total_attributes",
                NumericValue = attrTotal,
                MinSuggested = minPseudoAttr,
                IsDefaultChecked = false,
                Category = "Pseudo"
            });
        }

        foreach (string rawLine in modBody.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length < 5 || line.Contains(':') || line.StartsWith("--------") || line.StartsWith("{")) continue;
            string cleanLine = Regex.Replace(line, @"\s*\((?:implicit|fractured|enchant|rune|desecrated)\)\s*$", "", RegexOptions.IgnoreCase).Trim();

            foreach (var (rx, statId, defCheck) in TradeModMatchers)
            {
                var m = rx.Match(cleanLine);
                if (!m.Success || !addedIds.Add(statId)) continue;
                if (!double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) continue;
                int minSug = cleanLine.Contains("Level", StringComparison.OrdinalIgnoreCase)
                    ? Math.Max(1, (int)v)
                    : Math.Max(1, (int)Math.Floor(v * 0.82));
                bool isSingleEleRes = statId is "explicit.stat_3372524247" or "explicit.stat_4220027924" or "explicit.stat_1671376347";
                bool checkByDef = defCheck && !(isSingleEleRes && eleResTotal >= 40) && ev.SelectableMods.Count(x => x.IsDefaultChecked) < 4;
                ev.SelectableMods.Add(new ItemModFilterOption
                {
                    DisplayText = cleanLine,
                    TradeStatId = statId,
                    NumericValue = v,
                    MinSuggested = minSug,
                    IsDefaultChecked = checkByDef,
                    Category = "Explicit"
                });
                break;
            }
        }
    }

    private sealed record ItemStatSnapshot(
        double TotalDps,
        double Es,
        double Ev,
        double Ar,
        double MaxLife,
        double MovementSpeed,
        int EleRes,
        int ChaosRes,
        double Spirit,
        int SkillGeneric,
        int SkillAttack,
        int SkillSpell,
        int SkillMinion,
        double AttackSpeedPct,
        double CastSpeedPct,
        double SpellCritChancePct,
        double GlobalCritChancePct,
        double ExplicitAttackCritChancePct,
        double CritDamageBonusPct,
        double SpellDamagePct,
        double AttackDamagePct,
        double GeneralEleDamagePct,
        double MinionDamagePct,
        double FlatAttackAvg,
        double FlatSpellAvg);

    private sealed record ProfileScoreWeights(
        double Es,
        double Ev,
        double Ar,
        double Life,
        double EleRes,
        double ChaosRes,
        double MovementSpeed,
        double Spirit,
        double SkillBase,
        double SkillGenericMult,
        double SkillAttackMult,
        double SkillSpellMult,
        double SkillMinionMult,
        double AttackDpsMult,
        double AttackSpeed,
        double CastSpeed,
        double SpellCritChance,
        double GlobalCritChance,
        double ExplicitAttackCritChance,
        double CritDamageBonus,
        double SpellDamagePct,
        double AttackDamagePct,
        double GeneralEleDamagePct,
        double MinionDamagePct,
        double FlatAttackDmg,
        double FlatSpellDmg);

    private static double ExtractModSum(string pattern, string text)
    {
        double sum = 0;
        foreach (Match m in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
        {
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                sum += v;
        }
        return sum;
    }

    private static ItemStatSnapshot ExtractItemStatSnapshot(string rawText, bool isWeapon)
    {
        string text = rawText ?? "";
        double physDps = CalcWeaponPhysDps(text) ?? 0;
        double eleDps = CalcWeaponEdps(text) ?? 0;

        double es = ExtractNumber(@"Energy Shield:\s*(\d+)", text) ?? 0;
        double ev = ExtractNumber(@"Evasion Rating:\s*(\d+)", text) ?? 0;
        double ar = ExtractNumber(@"Armour:\s*(\d+)", text) ?? 0;
        if (es == 0) es = ExtractModSum(@"\+(\d+)\s+to maximum Energy Shield", text);
        if (ev == 0) ev = ExtractModSum(@"\+(\d+)\s+to Evasion Rating", text);
        if (ar == 0) ar = ExtractModSum(@"\+(\d+)\s+to Armour", text);

        double life = ExtractModSum(@"\+(\d+)\s+to maximum Life", text);
        double ms = ExtractNumber(@"(\d+)%\s+increased Movement Speed", text) ?? 0;
        var (eleRes, chaosRes) = SplitResistances(text);
        double spirit = ExtractModSum(@"\+(\d+)\s+to Spirit", text);

        int skGen = 0, skAtk = 0, skSpl = 0, skMin = 0;
        foreach (Match sm in Regex.Matches(text, @"\+(\d+)\s+to Level of (?:all )?([^\r\n%]+?Skills?)", RegexOptions.IgnoreCase))
        {
            if (!int.TryParse(sm.Groups[1].Value, out int lv)) continue;
            string kind = sm.Groups[2].Value.ToLowerInvariant();
            if (kind.Contains("spell")) skSpl = Math.Max(skSpl, lv);
            else if (kind.Contains("attack") || kind.Contains("melee")) skAtk = Math.Max(skAtk, lv);
            else if (kind.Contains("minion")) skMin = Math.Max(skMin, lv);
            else skGen = Math.Max(skGen, lv);
        }

        string modBody = ExtractModBody(text);
        double atkSpd = 0, castSpd = 0;
        double spellCrit = 0, globalCrit = 0, explicitAtkCrit = 0, critDmg = 0;
        double spellDmg = 0, atkDmg = 0, genEleDmg = 0, minionDmg = 0;
        double flatAtk = 0, flatSpl = 0;

        foreach (string rawLine in modBody.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = rawLine.Trim();
            if (trimmed.Length < 6 || trimmed.Contains(':') || trimmed.StartsWith("--------") || trimmed.StartsWith("{"))
                continue;

            string line = Regex.Replace(trimmed, @"\s*\((?:implicit|fractured|enchant|rune|desecrated)\)\s*$", "", RegexOptions.IgnoreCase).Trim();

            var flatM = Regex.Match(line, @"Adds (\d+) to (\d+) (?:Physical|Fire|Cold|Lightning|Chaos) Damage(?:\s+to\s+(Attacks|Spells))?", RegexOptions.IgnoreCase);
            if (flatM.Success &&
                int.TryParse(flatM.Groups[1].Value, out int fMin) &&
                int.TryParse(flatM.Groups[2].Value, out int fMax))
            {
                if (!isWeapon)
                {
                    double avg = (fMin + fMax) / 2.0;
                    if (flatM.Groups[3].Value.Equals("Spells", StringComparison.OrdinalIgnoreCase)) flatSpl += avg;
                    else flatAtk += avg;
                }
                continue;
            }

            if (!isWeapon && Regex.IsMatch(line, @"^\d+(?:\.\d+)?%\s+increased Attack Speed", RegexOptions.IgnoreCase))
                atkSpd += ExtractModSum(@"(\d+(?:\.\d+)?)%\s+increased Attack Speed", line);
            if (Regex.IsMatch(line, @"^\d+(?:\.\d+)?%\s+increased Cast Speed", RegexOptions.IgnoreCase))
                castSpd += ExtractModSum(@"(\d+(?:\.\d+)?)%\s+increased Cast Speed", line);

            if (Regex.IsMatch(line, @"Critical\s+(?:Hit\s+)?Chance\s+for\s+Spells|Spell\s+Critical", RegexOptions.IgnoreCase))
                spellCrit += ExtractModSum(@"(\d+(?:\.\d+)?)%", line);
            else if (Regex.IsMatch(line, @"Critical\s+(?:Hit|Strike)\s+Chance\s+with\s+Attacks", RegexOptions.IgnoreCase))
                explicitAtkCrit += ExtractModSum(@"(\d+(?:\.\d+)?)%", line);
            else if (Regex.IsMatch(line, @"^\d+(?:\.\d+)?%\s+increased Critical\s+(?:Hit|Strike)\s+Chance", RegexOptions.IgnoreCase))
            {
                double cv = ExtractModSum(@"(\d+(?:\.\d+)?)%", line);
                if (isWeapon) explicitAtkCrit += cv;
                else globalCrit += cv;
            }

            if (Regex.IsMatch(line, @"Critical\s+(?:Damage\s+Bonus|Strike\s+Multiplier)", RegexOptions.IgnoreCase))
                critDmg += ExtractModSum(@"[+\-]?(\d+(?:\.\d+)?)%", line);

            if (Regex.IsMatch(line, @"^\d+(?:\.\d+)?%\s+increased\s+Spell\s+Damage", RegexOptions.IgnoreCase))
                spellDmg += ExtractModSum(@"(\d+(?:\.\d+)?)%", line);
            else if (Regex.IsMatch(line, @"Minions?\s+deal\s+\d+(?:\.\d+)?%\s+increased\s+Damage|Allies\s+in\s+your\s+Presence\s+deal\s+\d+(?:\.\d+)?%\s+increased\s+Damage|\d+(?:\.\d+)?%\s+increased\s+Minion\s+Damage", RegexOptions.IgnoreCase))
                minionDmg += ExtractModSum(@"(\d+(?:\.\d+)?)%", line);
            else if (Regex.IsMatch(line, @"increased\s+(?:[\w\s]*Damage\s+with\s+(?:Attacks|Bows|Quarterstaves|Spears|Crossbows|Maces)|Attack\s+Damage|Melee\s+Damage)", RegexOptions.IgnoreCase))
                atkDmg += ExtractModSum(@"(\d+(?:\.\d+)?)%", line);
            else if (Regex.IsMatch(line, @"^\d+(?:\.\d+)?%\s+increased\s+(?:Elemental|Fire|Cold|Lightning|Chaos|Physical)\s+Damage", RegexOptions.IgnoreCase))
            {
                bool isLocalPhys = isWeapon && Regex.IsMatch(line, @"\bPhysical\s+Damage\b", RegexOptions.IgnoreCase);
                if (!isLocalPhys)
                    genEleDmg += ExtractModSum(@"(\d+(?:\.\d+)?)%", line);
            }
        }

        return new ItemStatSnapshot(
            physDps + eleDps, es, ev, ar, life, ms, eleRes, chaosRes, spirit,
            skGen, skAtk, skSpl, skMin,
            atkSpd, castSpd, spellCrit, globalCrit, explicitAtkCrit, critDmg,
            spellDmg, atkDmg, genEleDmg, minionDmg, flatAtk, flatSpl);
    }

    private static ProfileScoreWeights GetProfileScoreWeights(BuildProfile? profile, bool isWeapon)
    {
        bool isCi = profile?.IsCi ?? false;
        string focus = (profile?.DefenseFocus ?? "HYBRID").Trim().ToUpperInvariant();
        string style = (profile?.DamageStyle ?? "").Trim().ToUpperInvariant();

        double wEs, wEv, wAr;
        if (isCi)
        {
            wEs = focus == "ES" ? 2.0 : 1.9;
            wEv = focus == "EV_ES" ? 0.65 : focus == "HYBRID" ? 0.45 : 0.1;
            wAr = focus == "AR_ES" ? 0.65 : focus == "HYBRID" ? 0.45 : 0.1;
        }
        else
        {
            (wEs, wEv, wAr) = focus switch
            {
                "ES" => (1.75, 0.25, 0.25),
                "EV_ES" => (1.55, 0.75, 0.20),
                "AR_ES" => (1.55, 0.20, 0.75),
                "EV" => (0.45, 0.95, 0.20),
                "ARMOUR" => (0.45, 0.20, 0.95),
                _ => (1.40, 0.50, 0.50)
            };
        }

        double wLife = isCi ? 0.0 : 1.6;
        double wChaosRes = isCi ? 0.0 : 5.5;
        double wEleRes = 4.0;
        double wSpirit = style == "MINION" ? 10.0 : 6.5;
        double skillBase = isWeapon ? 120.0 : 150.0;

        bool isAtk = style == "ATTACK";
        bool isSpl = style == "SPELL";
        bool isMin = style == "MINION";
        bool isDot = style == "DOT";
        bool isNeutral = !isAtk && !isSpl && !isMin && !isDot;

        return new ProfileScoreWeights(
            Es: wEs,
            Ev: wEv,
            Ar: wAr,
            Life: wLife,
            EleRes: wEleRes,
            ChaosRes: wChaosRes,
            MovementSpeed: 18.0,
            Spirit: wSpirit,
            SkillBase: skillBase,
            SkillGenericMult: 1.0,
            SkillAttackMult: (isAtk || isNeutral) ? 1.0 : 0.0,
            SkillSpellMult: (isSpl || isDot || isNeutral) ? 1.0 : 0.0,
            SkillMinionMult: (isMin || isNeutral) ? 1.2 : 0.0,
            AttackDpsMult: (isAtk || isNeutral) ? 1.0 : 0.0,
            AttackSpeed: (isAtk || isNeutral) ? 7.0 : 0.0,
            CastSpeed: (isSpl || isDot || isNeutral) ? 7.0 : 0.0,
            SpellCritChance: (isSpl || isNeutral) ? 2.5 : 0.0,
            GlobalCritChance: (isAtk || isSpl || isNeutral) ? 2.2 : isDot ? 0.5 : 0.0,
            ExplicitAttackCritChance: (isAtk || isNeutral) ? 2.2 : 0.0,
            CritDamageBonus: (isAtk || isSpl || isNeutral) ? 3.0 : isDot ? 0.5 : 0.0,
            SpellDamagePct: (isSpl || isDot || isNeutral) ? 2.5 : 0.0,
            AttackDamagePct: (isAtk || isNeutral) ? 2.5 : 0.0,
            GeneralEleDamagePct: isMin ? 0.0 : 2.5,
            MinionDamagePct: (isMin || isNeutral) ? 2.8 : 0.0,
            FlatAttackDmg: (isAtk || isNeutral) ? 4.5 : 0.0,
            FlatSpellDmg: (isSpl || isDot || isNeutral) ? 4.5 : 0.0);
    }

    private static int CountPreferredModMatches(string rawText, BuildProfile? profile)
    {
        if (profile == null || profile.PreferredMods.Count == 0 || string.IsNullOrWhiteSpace(rawText)) return 0;
        int hits = 0;
        foreach (string mod in profile.PreferredMods.Where(m => !string.IsNullOrWhiteSpace(m) && m.Trim().Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (rawText.Contains(mod.Trim(), StringComparison.OrdinalIgnoreCase)) hits++;
        }
        return hits;
    }

    private static double GetStageBaselineScore(string slotKey, int effLevel)
    {
        int lv = Math.Clamp(effLevel, 1, 90);
        double slotBase = slotKey switch
        {
            "Weapon1" or "Weapon2" or "BodyArmour1" or "Offhand1" or "Offhand2" => 85.0,
            "Boots1" or "Helm1" or "Amulet1" => 70.0,
            _ => 60.0
        };
        return slotBase + lv * 4.0;
    }

    private static double ScoreSnapshot(ItemStatSnapshot s, ProfileScoreWeights w, int prefMatches)
    {
        double effectiveSkills = s.SkillGeneric * w.SkillGenericMult
                               + s.SkillAttack * w.SkillAttackMult
                               + s.SkillSpell * w.SkillSpellMult
                               + s.SkillMinion * w.SkillMinionMult;

        return s.TotalDps * w.AttackDpsMult
             + s.Es * w.Es
             + s.Ev * w.Ev
             + s.Ar * w.Ar
             + s.MaxLife * w.Life
             + s.MovementSpeed * w.MovementSpeed
             + s.EleRes * w.EleRes
             + s.ChaosRes * w.ChaosRes
             + s.Spirit * w.Spirit
             + effectiveSkills * w.SkillBase
             + s.AttackSpeedPct * w.AttackSpeed
             + s.CastSpeedPct * w.CastSpeed
             + s.SpellCritChancePct * w.SpellCritChance
             + s.GlobalCritChancePct * w.GlobalCritChance
             + s.ExplicitAttackCritChancePct * w.ExplicitAttackCritChance
             + s.CritDamageBonusPct * w.CritDamageBonus
             + s.SpellDamagePct * w.SpellDamagePct
             + s.AttackDamagePct * w.AttackDamagePct
             + s.GeneralEleDamagePct * w.GeneralEleDamagePct
             + s.MinionDamagePct * w.MinionDamagePct
             + s.FlatAttackAvg * w.FlatAttackDmg
             + s.FlatSpellAvg * w.FlatSpellDmg
             + prefMatches * 35.0;
    }

    public static (int pct, string details) ComputeSlotDiffScore(
        ItemEvaluation ev,
        string slotKey,
        string refText,
        BuildProfile? profile = null,
        int characterLevel = 1)
    {
        bool isWeapon = IsWeaponClass(ev.ItemClass.ToLowerInvariant());
        var sNew = ExtractItemStatSnapshot(ev.RawCopiedText, isWeapon);
        var sOld = ExtractItemStatSnapshot(refText, isWeapon);
        var w = GetProfileScoreWeights(profile, isWeapon);

        int prefNew = CountPreferredModMatches(ev.RawCopiedText, profile);
        int prefOld = CountPreferredModMatches(refText, profile);

        double scoreNew = ScoreSnapshot(sNew, w, prefNew);
        double scoreOld = ScoreSnapshot(sOld, w, prefOld);

        var parts = new List<string>();
        void AddCmp(string label, double a, double b, double weight, string unit = "")
        {
            if (weight <= 0 || (a <= 0 && b <= 0)) return;
            parts.Add($"{label} {a:0.#}{unit} vs {b:0.#}{unit}");
        }

        AddCmp("DPS", sNew.TotalDps, sOld.TotalDps, w.AttackDpsMult);
        AddCmp("ES", sNew.Es, sOld.Es, w.Es);
        AddCmp("EV", sNew.Ev, sOld.Ev, w.Ev);
        AddCmp("AR", sNew.Ar, sOld.Ar, w.Ar);
        AddCmp("Life", sNew.MaxLife, sOld.MaxLife, w.Life);
        AddCmp("MS", sNew.MovementSpeed, sOld.MovementSpeed, w.MovementSpeed, "%");
        AddCmp("EleRes", sNew.EleRes, sOld.EleRes, w.EleRes, "%");
        AddCmp("ChaosRes", sNew.ChaosRes, sOld.ChaosRes, w.ChaosRes, "%");
        AddCmp("Spirit", sNew.Spirit, sOld.Spirit, w.Spirit);

        double newSk = sNew.SkillGeneric * w.SkillGenericMult + sNew.SkillAttack * w.SkillAttackMult + sNew.SkillSpell * w.SkillSpellMult + sNew.SkillMinion * w.SkillMinionMult;
        double oldSk = sOld.SkillGeneric * w.SkillGenericMult + sOld.SkillAttack * w.SkillAttackMult + sOld.SkillSpell * w.SkillSpellMult + sOld.SkillMinion * w.SkillMinionMult;
        AddCmp("+Skills", newSk, oldSk, w.SkillBase);

        AddCmp("AtkSpd", sNew.AttackSpeedPct, sOld.AttackSpeedPct, w.AttackSpeed, "%");
        AddCmp("CastSpd", sNew.CastSpeedPct, sOld.CastSpeedPct, w.CastSpeed, "%");
        AddCmp("SpellDmg", sNew.SpellDamagePct, sOld.SpellDamagePct, w.SpellDamagePct, "%");
        AddCmp("AtkDmg", sNew.AttackDamagePct, sOld.AttackDamagePct, w.AttackDamagePct, "%");
        AddCmp("Ele/Phys%", sNew.GeneralEleDamagePct, sOld.GeneralEleDamagePct, w.GeneralEleDamagePct, "%");
        AddCmp("Minion%", sNew.MinionDamagePct, sOld.MinionDamagePct, w.MinionDamagePct, "%");
        AddCmp("FlatAtk", sNew.FlatAttackAvg, sOld.FlatAttackAvg, w.FlatAttackDmg);
        AddCmp("FlatSpl", sNew.FlatSpellAvg, sOld.FlatSpellAvg, w.FlatSpellDmg);

        double critNew = sNew.SpellCritChancePct * (w.SpellCritChance > 0 ? 1 : 0) + sNew.GlobalCritChancePct * (w.GlobalCritChance > 0 ? 1 : 0) + sNew.ExplicitAttackCritChancePct * (w.ExplicitAttackCritChance > 0 ? 1 : 0);
        double critOld = sOld.SpellCritChancePct * (w.SpellCritChance > 0 ? 1 : 0) + sOld.GlobalCritChancePct * (w.GlobalCritChance > 0 ? 1 : 0) + sOld.ExplicitAttackCritChancePct * (w.ExplicitAttackCritChance > 0 ? 1 : 0);
        AddCmp("Crit%", critNew, critOld, Math.Max(w.SpellCritChance, Math.Max(w.GlobalCritChance, w.ExplicitAttackCritChance)), "%");
        AddCmp("CritDmg", sNew.CritDamageBonusPct, sOld.CritDamageBonusPct, w.CritDamageBonus, "%");
        if (prefNew > 0 || prefOld > 0) parts.Add($"Targets {prefNew} vs {prefOld}");

        int effLevel = characterLevel > 1 ? characterLevel : Math.Max(1, ev.ItemLevel);
        double baselineByStage = GetStageBaselineScore(slotKey, effLevel);
        double denom = Math.Max(scoreOld, baselineByStage);

        if (denom > 0.1 && (scoreNew > 0 || scoreOld > 0))
        {
            int pct = (int)Math.Round(((scoreNew - scoreOld) / denom) * 100.0);
            return (pct, string.Join(" · ", parts));
        }

        return (int.MinValue, string.Join(" · ", parts));
    }

    private static void CompareAgainstBuildSlot(ItemEvaluation ev, BuildProfile? profile, int stageIndex, IReadOnlyDictionary<string, string>? wornGear, int characterLevel = 1)
    {
        string slotKey = MapItemClassToSlotKey(ev.ItemClass);
        ev.MatchedBuildSlot = slotKey;

        // A) Реально вдягнутий слот користувача
        string? wornRef = null;
        if (wornGear != null)
        {
            if (wornGear.TryGetValue(slotKey, out var wv) && !string.IsNullOrWhiteSpace(wv)) wornRef = wv;
            else if (slotKey == "Ring1" && wornGear.TryGetValue("Ring2", out var r2v) && !string.IsNullOrWhiteSpace(r2v)) wornRef = r2v;
        }

        int stageMinLvl = profile is { Stages.Count: > 0 }
            ? profile.Stages[Math.Clamp(stageIndex, 0, profile.Stages.Count - 1)].MinLevel
            : 1;
        int effLvl = characterLevel > 1 ? characterLevel : Math.Max(1, stageMinLvl);

        // B) Еталон поточного етапу білду
        string? buildRef = profile != null ? FindReferenceSlotText(profile, stageIndex, slotKey) : null;
        ev.MatchedWornText = wornRef ?? "";
        ev.ComparedAgainstWornGear = !string.IsNullOrWhiteSpace(wornRef);

        if (!string.IsNullOrWhiteSpace(wornRef))
        {
            var (pct, details) = ComputeSlotDiffScore(ev, slotKey, wornRef, profile, effLvl);
            if (pct != int.MinValue)
            {
                ev.UpgradeDeltaPct = pct;
                ev.UpgradeVerdict = pct >= 8
                    ? Loc.T($"[+] КРАЩЕ за твоє (+{pct}%)", $"[+] BETTER than equipped (+{pct}%)", $"[+] ЛУЧШЕ надетого (+{pct}%)")
                    : pct <= -8
                        ? Loc.T($"[-] ГІРШЕ за твоє ({pct}%)", $"[-] WORSE than equipped ({pct}%)", $"[-] ХУЖЕ надетого ({pct}%)")
                        : Loc.T($"[=] РІВНОЦІННО ({(pct >= 0 ? "+" : "")}{pct}%)", $"[=] SIDEGRADE ({(pct >= 0 ? "+" : "")}{pct}%)", $"[=] ПОХОЖЕ ({(pct >= 0 ? "+" : "")}{pct}%)");
                ev.UpgradeComparisonNote = $"{Loc.T("vs Твій екіп", "vs Equipped", "vs Надето")} ({PoeTermLocalizer.SlotName(slotKey)}): {details}";
            }
            else
            {
                string refHead = wornRef.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? slotKey;
                ev.UpgradeVerdict = ev.Warnings.Count > 0
                    ? Loc.T("[-] ГІРШЕ (не підходить)", "[-] WORSE (mismatch)", "[-] ХУЖЕ (не подходит)")
                    : ev.Verdict is ItemVerdict.Great or ItemVerdict.Good
                        ? Loc.T("[+] СИЛЬНА ЗАМІНА", "[+] STRONG UPGRADE", "[+] ХОРОШАЯ ЗАМЕНА")
                        : Loc.T("[=] БАЗОВО ДЛЯ СЛОТА", "[=] AVERAGE FOR SLOT", "[=] СРЕДНЕ ДЛЯ СЛОТА");
                ev.UpgradeComparisonNote = $"{Loc.T("Вдягнуто зараз", "Equipped now", "Надето сейчас")} ({PoeTermLocalizer.SlotName(slotKey)}): {refHead}";
            }
        }
        else
        {
            ev.UpgradeComparisonNote = Loc.T(
                $"Твій {PoeTermLocalizer.SlotName(slotKey)} ще не зафіксовано (натисни «Це на мені» або синхронізуй PoE 2 акаунт).",
                $"No worn {slotKey} tracked yet (click 'Equip as Mine' or sync PoE 2 Account).",
                $"Твой {PoeTermLocalizer.SlotName(slotKey)} ещё не сохранён (нажми «Это на мне»).");
        }

        if (!string.IsNullOrWhiteSpace(buildRef))
        {
            var (bPct, bDetails) = ComputeSlotDiffScore(ev, slotKey, buildRef, profile, effLvl);
            if (bPct != int.MinValue)
            {
                string sign = bPct >= 0 ? "+" : "";
                ev.BuildMatchVerdict = bPct >= 8
                    ? Loc.T($"[+] Вище еталона білду ({sign}{bPct}%)", $"[+] Above build stage ({sign}{bPct}%)", $"[+] Выше эталона билда ({sign}{bPct}%)")
                    : bPct <= -8
                        ? Loc.T($"[-] Слабше еталона білду ({bPct}%)", $"[-] Below build stage ({bPct}%)", $"[-] Слабее эталона ({bPct}%)")
                        : Loc.T($"[=] На рівні білду ({sign}{bPct}%)", $"[=] Matches build ({sign}{bPct}%)", $"[=] На уровне билда ({sign}{bPct}%)");
                ev.BuildComparisonNote = $"{Loc.T("vs Білд", "vs Build", "vs Билд")}: {bDetails}";

                if (string.IsNullOrWhiteSpace(ev.UpgradeVerdict))
                {
                    ev.UpgradeDeltaPct = bPct;
                    ev.UpgradeVerdict = ev.BuildMatchVerdict;
                }
            }
            else
            {
                var keywords = Regex.Matches(buildRef, @"[a-zA-Z]{4,}").Select(m => m.Value.ToLowerInvariant())
                    .Except(["increased", "maximum", "damage", "skills", "added", "attacks", "your", "with", "that", "from", "have", "gain"])
                    .Distinct().Take(8).ToList();
                int matched = keywords.Count(k => ev.Positives.Any(p => p.ToLowerInvariant().Contains(k)) || ev.Summary.ToLowerInvariant().Contains(k));
                ev.BuildMatchVerdict = Loc.T($"Білд: збіг модів {matched}/{Math.Max(1, keywords.Count)}", $"Build mod match: {matched}/{Math.Max(1, keywords.Count)}", $"Билд: совпадение {matched}/{Math.Max(1, keywords.Count)}");
                string refHead = buildRef.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? slotKey;
                ev.BuildComparisonNote = $"{Loc.T("Еталон білду", "Build target", "Эталон билда")}: {refHead}";

                if (string.IsNullOrWhiteSpace(ev.UpgradeVerdict))
                {
                    ev.UpgradeVerdict = ev.Warnings.Count > 0
                        ? Loc.T("[-] ГІРШЕ (не підходить)", "[-] WORSE (warnings)", "[-] ХУЖЕ (замечания)")
                        : ev.Verdict is ItemVerdict.Great or ItemVerdict.Good
                            ? Loc.T($"[+] ПІДХОДИТЬ БІЛДУ ({matched}/{Math.Max(1, keywords.Count)})", $"[+] FITS BUILD ({matched}/{Math.Max(1, keywords.Count)})", $"[+] ПОДХОДИТ БИЛДУ ({matched}/{Math.Max(1, keywords.Count)})")
                            : Loc.T("[=] БАЗОВО ДЛЯ СЛОТА", "[=] BASELINE", "[=] БАЗОВО");
                }
            }
        }
        else if (string.IsNullOrWhiteSpace(ev.UpgradeVerdict))
        {
            ev.UpgradeVerdict = ev.Warnings.Count > 0
                ? Loc.T("[-] НЕ ПІДХОДИТЬ / УВАГА", "[-] MISMATCH / WARN", "[-] ВНИМАНИЕ")
                : ev.Verdict is ItemVerdict.Great or ItemVerdict.Good
                    ? Loc.T("[+] ДОБРА РІЧ", "[+] GOOD UPGRADE", "[+] ХОРОШАЯ ВЕЩЬ")
                    : Loc.T("[=] ПОСЕРЕДНЬО", "[=] AVERAGE", "[=] СРЕДНЕ");
        }
    }

    private static string? FindReferenceSlotText(BuildProfile profile, int stageIndex, string slotKey)
    {
        if (profile.Stages.Count > 0)
        {
            var st = profile.Stages[Math.Clamp(stageIndex, 0, profile.Stages.Count - 1)];
            if (!string.IsNullOrWhiteSpace(st.FilePath) && File.Exists(st.FilePath))
            {
                var plan = BuildPlannerService.TryParseFile(st.FilePath);
                var g = plan?.Gear.FirstOrDefault(x => NormalizeSlot(x.RawSlotId) == slotKey);
                if (g != null) return g.Text;
            }
            foreach (var kv in st.GearDetailsBySlot)
            {
                if (NormalizeSlot(kv.Key) == slotKey) return kv.Value;
            }
            foreach (var sig in st.GearSignatures)
            {
                var p = sig.Split(':', 2);
                if (p.Length == 2 && NormalizeSlot(p[0]) == slotKey) return p[1].Trim();
            }
        }

        if (profile.SourceKind == BuildSourceKind.Pob2Import && !string.IsNullOrWhiteSpace(profile.Pob2Code))
        {
            var pob = Pob2Parser.Parse(profile.Pob2Code);
            foreach (var kv in pob.RawEquippedBySlot)
                if (NormalizeSlot(kv.Key) == slotKey) return kv.Value;
        }

        return profile.TargetGearHints.FirstOrDefault(h => NormalizeSlot(h.Split(':')[0]) == slotKey);
    }

    private static string NormalizeSlot(string raw)
    {
        string s = (raw ?? "").ToLowerInvariant();
        if (s.Contains("weapon1") || s == "weapon 1" || s.Contains("main hand")) return "Weapon1";
        if (s.Contains("offhand") || s.Contains("shield") || s.Contains("quiver") || s.Contains("focus")) return "Offhand1";
        if (s.Contains("helm")) return "Helm1";
        if (s.Contains("body") || s.Contains("chest") || s.Contains("armour")) return "BodyArmour1";
        if (s.Contains("glove")) return "Gloves1";
        if (s.Contains("boot")) return "Boots1";
        if (s.Contains("amulet")) return "Amulet1";
        if (s.Contains("ring")) return "Ring1";
        if (s.Contains("belt")) return "Belt1";
        if (s.Contains("jewel")) return "Jewel";
        if (s.Contains("flask")) return "Flask";
        return raw ?? "";
    }

    public static string MapItemClassToSlotKey(string itemClass)
    {
        string ic = (itemClass ?? "").ToLowerInvariant();
        if (ic.Contains("helmet")) return "Helm1";
        if (ic.Contains("body armour")) return "BodyArmour1";
        if (ic.Contains("gloves")) return "Gloves1";
        if (ic.Contains("boots")) return "Boots1";
        if (ic.Contains("amulet")) return "Amulet1";
        if (ic.Contains("ring")) return "Ring1";
        if (ic.Contains("belt")) return "Belt1";
        if (ic.Contains("shield") || ic.Contains("focus") || ic.Contains("buckler") || ic.Contains("quiver")) return "Offhand1";
        if (ic.Contains("jewel")) return "Jewel";
        if (ic.Contains("flask")) return "Flask";
        return IsWeaponClass(ic) ? "Weapon1" : (itemClass ?? "");
    }

    private static void EvaluateMarketProfitability(ItemEvaluation ev, string rawText, string rawLower)
    {
        int valuePts = 0;
        var reasons = new List<string>();

        if (ev.Rarity.Equals("Unique", StringComparison.OrdinalIgnoreCase))
        {
            valuePts += 35;
            reasons.Add("Unique");
        }
        if (ev.PlusSkillLevel >= 3)
        {
            valuePts += 45;
            reasons.Add($"+{ev.PlusSkillLevel} Skills");
        }
        else if (ev.PlusSkillLevel >= 1)
        {
            valuePts += 20;
            reasons.Add($"+{ev.PlusSkillLevel} Skills");
        }

        if (ev.MovementSpeed >= 30) { valuePts += 40; reasons.Add($"{(int)ev.MovementSpeed}% MS"); }
        else if (ev.MovementSpeed >= 25) { valuePts += 22; reasons.Add($"{(int)ev.MovementSpeed}% MS"); }

        if (ev.TotalDps >= 450) { valuePts += 45; reasons.Add($"~{ev.TotalDps:F0} DPS"); }
        else if (ev.TotalDps >= 280) { valuePts += 22; reasons.Add($"~{ev.TotalDps:F0} DPS"); }

        if (ev.EnergyShield >= 450) { valuePts += 38; reasons.Add($"ES {(int)ev.EnergyShield}"); }
        else if (ev.EnergyShield >= 220) { valuePts += 18; }

        if (ev.SpiritBonus >= 40) { valuePts += 35; reasons.Add($"+{(int)ev.SpiritBonus} Spirit"); }
        else if (ev.SpiritBonus >= 25) { valuePts += 18; reasons.Add($"+{(int)ev.SpiritBonus} Spirit"); }

        if (ev.TotalResistance >= 75) { valuePts += 32; reasons.Add($"{ev.TotalResistance}% Total Res"); }
        else if (ev.TotalResistance >= 45) { valuePts += 15; }

        if (ev.ItemLevel >= 81)
        {
            valuePts += 12;
            reasons.Add($"ilvl {ev.ItemLevel}");
        }

        bool corrupted = rawLower.Contains("corrupted");
        int sockets = Regex.Matches(rawText, @"\bS\b").Count;
        if (sockets >= 2) valuePts += 6;
        if (!corrupted && ev.Rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase) && valuePts >= 35)
        {
            reasons.Add(Loc.T("можна докрафтити Exalt/Socket", "open to Exalt/Rune craft", "можно докрафтить Exalt/Rune"));
        }

        string why = reasons.Count > 0 ? $" ({string.Join(", ", reasons.Take(4))})" : "";
        ev.ProfitVerdict = valuePts >= 65
            ? Loc.T($"ВИСОКА ВИГОДА — варто продавати на Trade 2{why}", $"HIGH VALUE — strong Trade 2 sale candidate{why}", $"ВЫСОКАЯ ВЫГОДА — выгодно продать на Trade 2{why}")
            : valuePts >= 32
                ? Loc.T($"ВИГІДНА — хороша для себе, крафту або середнього продажу{why}", $"PROFITABLE — solid use, Exalt craft base, or mid Trade{why}", $"ВЫГОДНАЯ — отлично для себя, крафта или продажи{why}")
                : valuePts >= 15
                    ? Loc.T($"БАЗОВА ЦІННІСТЬ — для прокачки/тимчасового слота{why}", $"MODEST VALUE — leveling / starter upgrade{why}", $"БАЗОВАЯ ЦЕННОСТЬ — для прокачки/старта{why}")
                    : Loc.T("НИЗЬКА ВИГОДА — підійде на Salvage / Disenchant (Regal/Artificer)", "LOW VALUE — candidate for Disenchant / Salvage", "НИЗКАЯ ВЫГОДА — лучше пустить на Salvage / Disenchant");
    }
}
