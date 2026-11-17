using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Класифікатор модифікаторів Waystones / Мап PoE 2 (у стилі Exiled Exchange 2 / Sidekick):
/// розпізнає небезпечні, обережні та вигідні моди з урахуванням активного білду й власних тегів гравця.
/// </summary>
public static partial class WaystoneModChecker
{
    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();

    public static string NormalizePatternKey(string line) =>
        NumberRegex().Replace(line.Trim().ToLowerInvariant(), "#");

    public static List<WaystoneModEvaluation> EvaluateWaystone(
        string rawText,
        BuildProfile? profile,
        IReadOnlyDictionary<string, MapModSafety>? customOverrides = null)
    {
        var list = new List<WaystoneModEvaluation>();
        if (string.IsNullOrWhiteSpace(rawText)) return list;

        string bodyText = rawText;
        int firstSep = rawText.IndexOf("--------", StringComparison.Ordinal);
        if (firstSep >= 0)
            bodyText = rawText[(firstSep + 8)..];

        var lines = bodyText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length < 6 || line.StartsWith("--------") || line.Contains(':') ||
                line.StartsWith("{") ||
                line.StartsWith("Unidentified", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Corrupted", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Mirrored", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Split", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Place this", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Can be used", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Waystones cannot", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Right click", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Shift click", StringComparison.OrdinalIgnoreCase))
                continue;

            string key = NormalizePatternKey(line);
            string low = line.ToLowerInvariant();

            if (customOverrides != null && customOverrides.TryGetValue(key, out var customTag))
            {
                list.Add(new WaystoneModEvaluation(line, key, customTag, Loc.T("Користувацький тег", "Custom tag", "Пользовательский тег")));
                continue;
            }

            var (safety, reason) = ClassifyMod(low, profile);
            list.Add(new WaystoneModEvaluation(line, key, safety, reason));
        }
        return list;
    }

    private static (MapModSafety safety, string reason) ClassifyMod(string low, BuildProfile? profile)
    {
        bool isAttackOrLeech = profile?.DamageStyle is "Attack" or "Minion";
        bool isCi = profile?.IsCi == true;

        if (low.Contains("maximum player resistances") || low.Contains("maximum resistances"))
            return (MapModSafety.Deadly, Loc.T("Зріз макс. опорів (ризик ваншоту)", "Max Res penalty (oneshot risk)", "Срез макс. резистов"));

        if (low.Contains("penetrate") && low.Contains("elemental resistance"))
            return (MapModSafety.Deadly, Loc.T("Пробиття стихійних опорів", "Monsters penetrate Ele Res", "Пробитие стихийных резистов"));

        if (low.Contains("cannot regenerate") || low.Contains("no regeneration"))
            return (MapModSafety.Deadly, Loc.T("Немає регену Life/ES/Mana", "No Regeneration", "Нет регенерации"));

        if (low.Contains("less recovery rate"))
            return (isCi || isAttackOrLeech ? MapModSafety.Deadly : MapModSafety.Caution,
                Loc.T("Слабке відновлення HP/ES/Mana", "Less Recovery Rate", "Сниженное восстановление"));

        if (low.Contains("cannot leech") && isAttackOrLeech)
            return (MapModSafety.Deadly, Loc.T("Неможливий Leech для білду", "Cannot Leech (breaks sustain)", "Невозможен Leech"));

        if (low.Contains("critical") && (low.Contains("chance") || low.Contains("damage") || low.Contains("multiplier")))
            return (MapModSafety.Caution, Loc.T("Кріт-мод по гравцю", "Crit spike danger", "Опасность критов"));

        if (low.Contains("extra chaos") || (isCi && low.Contains("desecrated ground")))
            return (MapModSafety.Caution, Loc.T("Додатковий Chaos / дегенерація", "Extra Chaos / ground degen", "Доп. Chaos урон"));

        if (low.Contains("extra fire") || low.Contains("extra cold") || low.Contains("extra lightning"))
            return (MapModSafety.Caution, Loc.T("Додатковий стихійний урон монстрів", "Extra Elemental Damage", "Доп. стихийный урон"));

        if (low.Contains("cursed with") || low.Contains("temporal chains") || low.Contains("elemental weakness") || low.Contains("vulnerability"))
            return (MapModSafety.Caution, Loc.T("Прокляття на гравці", "Player Curse", "Проклятие на игроке"));

        if (low.Contains("attack speed") || low.Contains("cast speed") || low.Contains("movement speed") || low.Contains("additional projectiles"))
            return (MapModSafety.Caution, Loc.T("Прискорені монстри / снаряди", "Fast monsters / Extra proj", "Ускоренные монстры"));

        if (low.Contains("burning ground") || low.Contains("chilled ground") || low.Contains("shocked ground"))
            return (MapModSafety.Caution, Loc.T("Небезпечна поверхня під ногами", "Elemental ground hazard", "Опасная поверхность"));

        if (low.Contains("waystone") || low.Contains("rarity") || low.Contains("quantity") ||
            low.Contains("pack size") || low.Contains("additional") || low.Contains("rare monsters") ||
            low.Contains("magic monsters"))
            return (MapModSafety.Good, Loc.T("Вигідний бонус луту/карт", "High pack size / loot bonus", "Выгодный бонус лута"));

        return (MapModSafety.Good, Loc.T("Нейтральний / безпечний мод", "Safe / Neutral mod", "Безопасный мод"));
    }

    public static MapModSafety CycleCustomTag(Dictionary<string, MapModSafety> map, string patternKey, MapModSafety current)
    {
        MapModSafety next = current switch
        {
            MapModSafety.Good => MapModSafety.Caution,
            MapModSafety.Caution => MapModSafety.Deadly,
            _ => MapModSafety.Good
        };
        map[patternKey] = next;
        return next;
    }
}
