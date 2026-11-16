using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Localization;

/// <summary>
/// Перекладає назви слотів екіпірування, категорії вузлів, типи захисту, зброї та модифікаторів PoE 2
/// відповідно до обраної мови інтерфейсу (UA / EN / RU) без втрати оригінальних ігрових ідентифікаторів.
/// </summary>
public static class PoeTermLocalizer
{
    private static readonly (Regex rx, string uk, string ru)[] StatRules =
    [
        (new(@"(\d+(?:\.\d+)?)%\s+increased Movement Speed", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% швидкості пересування", "+$1% скорости передвижения"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Attack Speed", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% швидкості атаки", "+$1% скорости атаки"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Cast Speed", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% швидкості касту", "+$1% скорости сотворения чар"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Critical Hit Chance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% шансу кріта", "+$1% шанса крита"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Critical Damage Bonus", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% бонусу кріт-урону", "+$1% бонуса крит-урона"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Energy Shield", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% енергощита (ES)", "+$1% энергощита (ES)"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Evasion Rating", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% ухилення (Evasion)", "+$1% уклонения (Evasion)"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Armour", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% броні", "+$1% брони"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Evasion and Energy Shield", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% EV та ES", "+$1% EV и ES"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Elemental Damage with Attacks", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% стихійного урону атак", "+$1% стихийного урона атак"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Elemental Damage", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% стихійного урону", "+$1% стихийного урона"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Physical Damage", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% фізичного урону", "+$1% физического урона"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Spell Damage", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% урону чарами", "+$1% урона чарами"),
        (new(@"(\d+(?:\.\d+)?)%\s+increased Area of Effect", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% площі дії (AoE)", "+$1% области действия (AoE)"),
        (new(@"Adds (\d+) to (\d+) Fire Damage", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1–$2 плоского урону вогнем", "+$1–$2 плоского урона огнём"),
        (new(@"Adds (\d+) to (\d+) Cold Damage", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1–$2 плоского урону холодом", "+$1–$2 плоского урона холодом"),
        (new(@"Adds (\d+) to (\d+) Lightning Damage", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1–$2 плоского урону блискавкою", "+$1–$2 плоского урона молнией"),
        (new(@"Adds (\d+) to (\d+) Physical Damage", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1–$2 плоского фіз-урону", "+$1–$2 плоского физ-урона"),
        (new(@"\+(\d+) to Level of all (.+?) Skills", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1 до рівня умінь ($2)", "+$1 к уровню умений ($2)"),
        (new(@"\+(\d+) to maximum Energy Shield", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1 до макс. Енергощита", "+$1 к макс. Энергощиту"),
        (new(@"\+(\d+) to maximum Life", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1 до макс. Здоров'я", "+$1 к макс. Здоровью"),
        (new(@"\+(\d+) to maximum Mana", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1 до макс. Мани", "+$1 к макс. Мане"),
        (new(@"\+(\d+) to Spirit", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1 до Духу (Spirit)", "+$1 к Духу (Spirit)"),
        (new(@"\+(\d+)% to all Elemental Resistances", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% до всіх стихійних опорів", "+$1% ко всем стихийным сопротивлениям"),
        (new(@"\+(\d+)% to Fire Resistance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% опору вогню", "+$1% сопротивления огню"),
        (new(@"\+(\d+)% to Cold Resistance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% опору холоду", "+$1% сопротивления холоду"),
        (new(@"\+(\d+)% to Lightning Resistance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% опору блискавці", "+$1% сопротивления молнии"),
        (new(@"\+(\d+)% to Chaos Resistance", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1% опору хаосу", "+$1% сопротивления хаосу"),
        (new(@"\+(\d+) to Strength", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1 до Сили (Str)", "+$1 к Силе (Str)"),
        (new(@"\+(\d+) to Dexterity", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1 до Спритності (Dex)", "+$1 к Ловкости (Dex)"),
        (new(@"\+(\d+) to Intelligence", RegexOptions.IgnoreCase | RegexOptions.Compiled), "+$1 до Інтелекту (Int)", "+$1 к Интеллекту (Int)"),
        (new(@"Life Regeneration per second", RegexOptions.IgnoreCase | RegexOptions.Compiled), "регенерація здоров'я/сек", "регенерация здоровья/сек"),
        (new(@"Gain Deflection Rating equal to (\d+)% of Evasion Rating", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Deflection = $1% від Evasion", "Deflection = $1% от Evasion"),
        (new(@"Arcane Surge when you deal a Critical Hit", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Arcane Surge при кріті", "Arcane Surge при крите"),
    ];

    public static string SlotName(string rawSlot) => rawSlot.Trim() switch
    {
        "Weapon1" or "Weapon 1" => Loc.T("Зброя 1", "Weapon 1", "Оружие 1"),
        "Offhand1" or "Weapon 1 Swap" => Loc.T("Ліва рука 1", "Offhand 1", "Левая рука 1"),
        "Weapon2" or "Weapon 2" => Loc.T("Зброя 2 (Set 2)", "Weapon 2 (Set 2)", "Оружие 2 (Set 2)"),
        "Offhand2" => Loc.T("Ліва рука 2", "Offhand 2", "Левая рука 2"),
        "Helm1" or "Helmet" => Loc.T("Шолом", "Helmet", "Шлем"),
        "BodyArmour1" or "Body Armour" => Loc.T("Нагрудник", "Body Armour", "Нагрудник"),
        "Gloves1" or "Gloves" => Loc.T("Рукавички", "Gloves", "Перчатки"),
        "Boots1" or "Boots" => Loc.T("Чоботи", "Boots", "Сапоги"),
        "Amulet1" or "Amulet" => Loc.T("Амулет", "Amulet", "Амулет"),
        "Ring1" or "Ring 1" => Loc.T("Перстень 1", "Ring 1", "Кольцо 1"),
        "Ring2" or "Ring 2" => Loc.T("Перстень 2", "Ring 2", "Кольцо 2"),
        "Belt1" or "Belt" => Loc.T("Пояс", "Belt", "Пояс"),
        "Charm1" => Loc.T("Талісман 1", "Charm 1", "Чарм 1"),
        "Charm2" => Loc.T("Талісман 2", "Charm 2", "Чарм 2"),
        "Charm3" => Loc.T("Талісман 3", "Charm 3", "Чарм 3"),
        "Flask 1" => Loc.T("Фласка 1", "Flask 1", "Флакон 1"),
        "Flask 2" => Loc.T("Фласка 2", "Flask 2", "Флакон 2"),
        _ => rawSlot
    };

    public static string NodeKindLabel(PassiveNodeKind kind) => kind switch
    {
        PassiveNodeKind.Keystone => Loc.T("Ключовий (Keystone)", "Keystone", "Ключевой (Keystone)"),
        PassiveNodeKind.Notable => Loc.T("Помітний (Notable)", "Notable", "Значимый (Notable)"),
        PassiveNodeKind.JewelSocket => Loc.T("Гніздо самоцвіта", "Jewel Socket", "Гнездо самоцвета"),
        PassiveNodeKind.ClassStart => Loc.T("Старт класу", "Class Start", "Старт класса"),
        PassiveNodeKind.AscendancyNotable => Loc.T("Вознесіння (Головний)", "Ascendancy Notable", "Восхождение (Главный)"),
        PassiveNodeKind.AscendancySmall => Loc.T("Вознесіння (Малий)", "Ascendancy Small", "Восхождение (Малый)"),
        _ => Loc.T("Малий нод", "Small Node", "Малый нод")
    };

    public static string DefenseLabel(string def) => def switch
    {
        "ES" => Loc.T("Енергощит (ES)", "Energy Shield (ES)", "Энергощит (ES)"),
        "EV_ES" => Loc.T("Ухилення + Щит (EV/ES)", "Evasion + ES", "Уклонение + Щит (EV/ES)"),
        "EV" => Loc.T("Ухилення (EV)", "Evasion", "Уклонение (EV)"),
        "ARMOUR" => Loc.T("Броня (Armour)", "Armour", "Броня"),
        "AR_ES" => Loc.T("Броня + Щит (AR/ES)", "Armour + ES", "Броня + Щит (AR/ES)"),
        _ => Loc.T("Гібридний захист", "Hybrid Defense", "Гибридная защита")
    };

    public static string WeaponClassLabel(string cls) => cls switch
    {
        "Quarterstaves" => Loc.T("Бойові посохи (Quarterstaves)", "Quarterstaves", "Боевые посохи (Quarterstaves)"),
        "Bows" => Loc.T("Луки (Bows)", "Bows", "Луки (Bows)"),
        "Crossbows" => Loc.T("Арбалети (Crossbows)", "Crossbows", "Арбалеты (Crossbows)"),
        "Wands" => Loc.T("Жезли (Wands)", "Wands", "Жезлы (Wands)"),
        "Staves" => Loc.T("Посохи (Staves)", "Staves", "Посохи (Staves)"),
        "Spears" => Loc.T("Списи (Spears)", "Spears", "Копья (Spears)"),
        "Sceptres" => Loc.T("Скіпетри (Sceptres)", "Sceptres", "Скипетры (Sceptres)"),
        "Talismans" => Loc.T("Талісмани (Talismans)", "Talismans", "Талисманы (Talismans)"),
        "One Hand Maces" => Loc.T("Одноручні булави", "One Hand Maces", "Одноручные булавы"),
        "Two Hand Maces" => Loc.T("Дворучні булави", "Two Hand Maces", "Двуручные булавы"),
        "Flails" => Loc.T("Ціпи (Flails)", "Flails", "Цепы (Flails)"),
        "Swords" => Loc.T("Мечі (Swords)", "Swords", "Мечи (Swords)"),
        "Axes" => Loc.T("Сокири (Axes)", "Axes", "Топоры (Axes)"),
        _ => string.IsNullOrWhiteSpace(cls) ? Loc.T("Будь-яка / Без зброї", "Any / Unarmed", "Любое / Без оружия") : cls
    };

    public static string StatLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || Loc.Current == AppLanguage.En)
            return raw;

        string line = Regex.Replace(raw.Trim(), @"^\d+\.\s*", "");
        foreach (var (rx, uk, ru) in StatRules)
        {
            if (rx.IsMatch(line))
            {
                string rep = Loc.Current == AppLanguage.Ru ? ru : uk;
                return rx.Replace(line, rep);
            }
        }
        return line;
    }

    public static string TranslateGearDescription(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (Loc.Current == AppLanguage.En) return raw.Replace("\r", "").Trim();
        var lines = raw.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", lines.Select(StatLine));
    }

    public static string BuildSourceLabel(BuildSourceKind kind) => kind switch
    {
        BuildSourceKind.BuildPlannerImport => Loc.T(".build Планувальник", ".build Planner", ".build Планировщик"),
        BuildSourceKind.Pob2Import => Loc.T("Імпорт PoB2", "PoB2 Import", "Импорт PoB2"),
        BuildSourceKind.MobalyticsImport => "Mobalytics",
        BuildSourceKind.GuideUrlImport => Loc.T("Гайд URL", "Guide URL", "Гайд URL"),
        _ => kind.ToString()
    };
}
