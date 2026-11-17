using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Довідник походження предметів Path of Exile 2:
/// де вибити унiки/самоцвіти (Pinnacle боси, Breach, Ritual, Expedition, Delirium, Trials),
/// мінімальні ilvl для крафту рідкісних баз, Chance Orb та поради по цінності.
/// </summary>
public static class Poe2KnowledgeDb
{
    private static readonly PoeItemObtainInfo[] CuratedItems =
    [
        // --- Pinnacle / League Boss Uniques & Meta Jewels ---
        new("Heart of the Well", "Jewel (Unique Diamond)", "Area Lvl 75+ / Waystones",
            "Падає в Активності Abyss / рідкісний дроп ліги або через PoE 2 Trade ( шукають Extra Damage + Crit / Rage on Hit).",
            "Drops from Abyss encounters / league mechanics or PoE 2 Trade (chase mods: Extra Cold/Lightning + Rage).",
            "Падает из механики Abyss или покупается на PoE 2 Trade (топ-статы: Extra Cold/Lightning + Rage).",
            "Перевіряй 4 рандомні моди перед продажем — правильна комбінація коштує десятки Divine.",
            "Always price-check all 4 rolled stats before selling; top combos sell for multiple Divines.",
            "Всегда проверяй цену всех 4 модов — удачная комбинация стоит очень дорого."),
        new("Against the Darkness", "Jewel (Time-Lost Diamond)", "Trial of the Sekhemas (Boss)",
            "Ексклюзивний дроп із фінального боса Trial of the Sekhemas (Zarokh, the Temporal, 4 поверх).",
            "Boss-exclusive drop from Zarokh, the Temporal (Floor 4 of Trial of the Sekhemas).",
            "Эксклюзивный дроп с босса Зароха (4 этаж Trial of the Sekhemas).",
            "Додає бонуси до кожної Small/Notable пасивки в радіусі — комбінація Extra Cold/Lightning/Spirit коштує дуже дорого.",
            "Adds stats per Notable/Small node in radius — Spirit + Extra Element combos are ultra-valuable.",
            "Усиливает соседние ноды в радиусе — Spirit и Extra-урон ценятся выше всего."),
        new("Controlled Metamorphosis", "Jewel (Massive / Medium Ring)", "Delirium / Simulacrum",
            "Падає з хвиль Simulacrum (Delirium) або дроп з ліги Delirium на високих Waystones.",
            "Drops from Delirium Simulacrum waves and high-tier Delirium encounters.",
            "Падает в Simulacrum (Delirium) и высокоуровневых Delirium-встречах.",
            "Дозволяє брати пасивки всередині кільця без прокладання шляху (ключове значення — мінус до опорів).",
            "Allocates passives inside a ring without pathing; roll quality depends heavily on lowest negative resistance penalty.",
            "Позволяет брать пассивки внутри кольца без связей; важен минимальный штраф к сопротивлениям."),
        new("Megalomaniac", "Jewel (Diamond)", "Delirium Simulacrum",
            "Падає виключно у Simulacrum; дає 2–3 випадкові Notables з усього дерева пасивок.",
            "Simulacrum-exclusive drop granting 2–3 random Notables anywhere on the tree.",
            "Падает только в Simulacrum; даёт 2–3 случайных значимых пассивки.",
            "Ціна на ринку повністю залежить від синергії ролнутих Notables.",
            "Value varies wildly from 1 Exalt to 50+ Divines depending on rolled Notable combinations.",
            "Цена полностью зависит от выпавших Notables."),
        new("Prism of Belief", "Jewel (Diamond)", "Arbiter of Ash (Pinnacle)",
            "Падає з фінального Pinnacle-боса Атласу — The Arbiter of Ash (Burning Monolith).",
            "Drops from The Arbiter of Ash (Atlas Pinnacle Boss inside the Burning Monolith).",
            "Падает с главного Pinnacle-босса Атласа — Arbiter of Ash.",
            "Дає +1..+3 до рівня конкретного вміння — метові скіли мають величезний попит.",
            "Grants +1 to +3 Levels of a specific meta Skill Gem — huge Trade value on popular skills.",
            "Даёт +1..+3 к уровню конкретного умения — очень высокий спрос на популярные камни."),
        new("Hand of Wisdom and Action", "Unique Gloves (Spiral Wraps · Req Lv33)", "Breach Boss: Xesht",
            "Ексклюзивний дроп із Xesht, We That Are One (Breachstone / Twisted Domain). Вимога: 33 рівень, 24 Dex, 24 Int.",
            "Exclusive drop from Xesht, We That Are One (Breach Pinnacle Boss). Base: Spiral Wraps (Req Lvl 33).",
            "Эксклюзивный дроп с босса Разлома — Xesht (Twisted Domain). База Spiral Wraps, ур. 33+.",
            "Масштабує плоский урон блискавкою від Int та швидкість атаки від Dex — топ для Monk/Attribute-білдів.",
            "Scales Attack Speed per Dex & Lightning Damage per Int — premier attribute-stacking gloves.",
            "Даёт скорость от Dex и урон молнией от Int — топ-перчатки для attribute-билдов."),
        new("Ingenuity", "Unique Belt (Utility Belt · Req Lv55)", "Ritual Boss: King in the Mists",
            "Бос-дроп із King in the Mists (An Audience with the King у Ritual). База Utility Belt (рів. 55+).",
            "Boss drop from The King in the Mists (An Audience with the King Ritual encounter). Req Lvl 55.",
            "Падает с Короля в Туманах (An Audience with the King, Ритуал). База Utility Belt, ур. 55+.",
            "Множить ефект лівого/правого кільця та фляг/чармів; шукай найвищий % ролу та кількість Charm-слотів.",
            "Amplifies equipped Rings bonuses & Flask/Charm recovery — roll % & Charm slots matter most.",
            "Усиливает эффекты колец и фласок — решающее значение имеет высокий % ролла."),
        new("Astramentis", "Unique Amulet (Stellar Amulet · Req Lv24)", "World Drop T0 / Chance Orb (Stellar Amulet)",
            "Ультра-рідкісний світовий дроп (Tier 0) або Орб Шансу (Orb of Chance) у білу базу Stellar Amulet (рів. 24+)!",
            "Ultra-rare T0 World Drop or Orb of Chance into white Stellar Amulet bases (Req Lvl 24).",
            "Ультра-редкий мировой дроп (T0) или Orb of Chance в базу Stellar Amulet (ур. 24+)!",
            "До +100 to All Attributes (+імплісит) — ніколи не ігноруй білі Stellar Amulets.",
            "Massive +to All Attributes — always pick up white Stellar Amulet bases for Chance / Trade.",
            "Огромный бонус ко всем атрибутам — всегда подбирай белые базы Stellar Amulet."),
        new("Headhunter", "Unique Belt (Heavy Belt · Req Lv50)", "World Drop T0 / Chance Orb (Heavy Belt)",
            "Ультра-рідкісний світовий дроп (високі карти/Chance Orb на Heavy Belt, рів. 50+).",
            "Ultra-rare T0 World Drop or Orb of Chance on white Heavy Belt (Req Lvl 50).",
            "Ультра-редкий мировой дроп или Orb of Chance на Heavy Belt (ур. 50+).",
            "Краде моди вбитих рідкісних монстрів — король швидкого кліру мап.",
            "Steals Rare monster modifiers on kill — unmatched speed-mapping belt.",
            "Крадёт свойства редких монстров при убийстве."),
        new("Temporalis", "Unique Body Armour (Silk Robe · Req Lv64)", "Sekhemas Hitless Run",
            "Винагорода за проходження Trial of the Sekhemas з реліквією 'The Last Flame' без отримання урону (1 Honor). Вимога: 64 рів.",
            "Exclusive reward for no-hit Trial of the Sekhemas using 'The Last Flame' relic (Req Lvl 64).",
            "Уникальная награда за прохождение Trial of the Sekhemas без получения урона (1 Honor, ур. 64+).",
            "Різко зменшує перезарядку навичок (включно з Blink).",
            "Massive skill cooldown reduction enabling instant Blink setups.",
            "Резко снижает перезарядку умений (включая Blink)."),
        new("Tangletongue", "Unique Spear (Forked Spear · Req Lv26)", "World Drop / Gambling / Chance Orb",
            "Світовий дроп (з ~26 рівня / Акт 2+), гемблінг списів або Orb of Chance на Forked Spear.",
            "Global world drop (Req Lvl 26) / Spear vendor gambling / Orb of Chance on Forked Spear.",
            "Мировой дроп (с 26 ур.), обмен у торговца или Chance Orb в Forked Spear.",
            "Механіка подвійних крітів ('Forks Critical Hits') робить високий кріт-рол одним із найкращих списів.",
            "Double crit roll mechanic ('Forks Critical Hits') makes high crit rolls extremely strong.",
            "Двойной расчёт критов делает высокий ролл крита невероятно сильным."),
        new("Pillar of the Caged God", "Unique Quarterstaff (Long Quarterstaff · Req Lv4)", "Act 1+ World Drop / Chance Orb",
            "Ранній світовий дроп уже з 4-го рівня (Акт 1+) або Orb of Chance у Long Quarterstaff; топ для стакерів атрибутів.",
            "Early World Drop (Req Lvl 4, Act 1+) or cheap Trade purchase.",
            "Ранний дроп с 4-го уровня (Акт 1+) или дешёвый слот на трейде.",
            "Синергує з плоским уроном на перснях/рукавичках та високими Str/Dex/Int.",
            "Scales AoE/Speed/Damage cleanly off flat added damage on Rings/Gloves and Attributes.",
            "Отлично масштабирует плоский урон с колец и перчаток от ваших атрибутов."),
        new("Shavronne", "Unique Belt (Fine Belt · Req Lv62)", "World Drop / Endgame Trade",
            "Світовий дроп високого рівня (Shavronne's Satchel, рів. 62+) або покупка на PoE 2 Trade.",
            "Endgame World Drop (Shavronne's Satchel, Req Lvl 62) or Trade purchase.",
            "Высокоуровневый мировой дроп (Shavronne's Satchel, ур. 62+) или Trade.",
            "Дозволяє Life Recovery з фляг поповнювати Energy Shield — рятує для CI/гібридів, але стеж за модами фляг.",
            "Life Recovery from Flasks applies to Energy Shield — great for CI/ES sustain.",
            "Позволяет фласкам здоровья восстанавливать Energy Shield — отлично для CI."),
        new("Polcirkeln", "Unique Ring (Sapphire Ring · Req Lv12)", "World Drop / Chance Orb",
            "Світовий дроп з 12+ рівня (Акт 1+) або Orb of Chance на Sapphire Ring.",
            "World Drop (Req Lvl 12+) or Orb of Chance on Sapphire Ring.",
            "Мировой дроп с 12+ ур. или Orb of Chance на Sapphire Ring.",
            "Охолоджені вороги розбиваються як заморожені — миттєвий запуск ланцюгів Herald of Ice.",
            "Chilled enemies shatter as if Frozen — unlocks huge Herald of Ice clears early.",
            "Охлаждённые враги раскалываются как замороженные — запускает цепочки Herald of Ice."),
        new("Thief's Torment", "Unique Ring (Emerald Ring · Req Lv26)", "World Drop / Chance Orb",
            "Світовий дроп (рів. 26+) або Orb of Chance на Emerald Ring (займає обидва слоти кілець!).",
            "Global Drop (Req Lvl 26+) / Chance Orb on Emerald Ring (prevents using second ring).",
            "Мировой дроп (ур. 26+; запрещает использовать второе кольцо).",
            "Дає величезний Life/Mana gain on Hit — топ для прокачки й босів до ендгейму.",
            "Massive Mana/Life gained on Hit solves sustain completely during leveling and bossing.",
            "Полностью решает проблему маны и здоровья при попадании."),
        new("Beacon of Azis", "Unique Amulet (Solar Amulet · Req Lv30)", "World Drop / Endgame Pool",
            "Світовий дроп (рів. 30+, частіше на пізній кампанії/мапах); критами повністю ігнорує стихійні опори ворогів.",
            "World Drop (Req Lvl 30+, Solar Amulet); Critical Hits ignore all enemy Elemental Resistances!",
            "Мировой дроп (ур. 30+, Solar Amulet); критические удары игнорируют стихийные сопротивления.",
            "Дає до ~40–45 Spirit і знімає потребу качати Exposure/Penetration для кріт-білдів.",
            "Grants high Spirit (~40–45) and frees Exposure/Penetration budget on Crit builds.",
            "Даёт ~40–45 Spirit и освобождает от поиска Exposure/Penetration для крит-билдов."),
        new("Alpha's Howl", "Unique Helmet (Plumed Cap · Req Lv61)", "World Drop (Lvl 61+ / Waystones)",
            "Світовий дроп високого рівня (база Plumed Cap, рів. 61+, 86 Dex); дає +60 до Spirit та скіл Werewolf Pack.",
            "High-tier World Drop (Plumed Cap, Req Lvl 61, 86 Dex) granting +60 Spirit & Werewolf Pack.",
            "Высокоуровневый мировой дроп (Plumed Cap, ур. 61+, 86 Dex); даёт +60 к Spirit и умение Werewolf Pack.",
            "Значно підвищує Evasion і дозволяє включити додатковий Spirit-баф.",
            "High Evasion + Cold Res +60 Spirit helps fit extra persistent buffs.",
            "Даёт высокий Evasion, Cold Res и +60 Spirit для дополнительной ауры."),
        new("Atziri's Disdain", "Unique Helmet (Gold Circlet · Req Lv40)", "World Drop / Chance Orb",
            "Світовий дроп (рів. 40+, 58 Int) або Orb of Chance на Gold Circlet; додає екстра ES від Max Life.",
            "World Drop (Req Lvl 40, Gold Circlet) / Chance Orb; grants Extra Max ES from % of Life.",
            "Мировой дроп (ур. 40+, Gold Circlet) или Chance Orb; добавляет экстра ES от здоровья.",
            "Чудовий гібридний шолом до переходу в чистий CI.",
            "Outstanding mid-game ES/Life hybrid helmet before swapping to pure CI.",
            "Отличный переходный гибридный шлем до перехода на CI."),
        new("Ghostwrithe", "Unique Body Armour (Tattered Robe · Req Lv1)", "Early Act 1+ Drop",
            "Ранній світовий дроп з 1-го рівня; дає +100 ES та конвертує 35% Max Life в Energy Shield.",
            "Early Act 1+ World Drop (Req Lvl 1); +100 ES and converts 35% of Max Life to ES.",
            "Ранний дроп с 1-го уровня (Акт 1+); +100 ES и конвертация 35% Max Life в Energy Shield.",
            "Найкраща дешева броня для раннього старту через Energy Shield.",
            "Best budget body armour for early ES transitions during campaign.",
            "Лучшая дешёвая броня для раннего старта через Energy Shield."),
        new("Wanderlust", "Unique Boots (Wrapped Sandals · Req Lv11)", "Act 1 World Drop / Gamble",
            "Світовий дроп (з 11 рівня, 17 Int, кінець Акту 1+) або гемблінг Wrapped Sandals.",
            "World Drop (Req Lvl 11, 17 Int — Wrapped Sandals) or boot vendor gambling.",
            "Мировой дроп (с 11 уровня, 17 Int — Wrapped Sandals) или гемблинг у вендоров.",
            "+20% Movement Speed та захист від Slow з ранньої кампанії.",
            "+20% Movement Speed and immune to Slows — top campaign leveling boots.",
            "+20% скорости бега и защита от замедлений — лучшие прокачные сапоги."),
        new("Goldrim", "Unique Helmet (Felt Cap · Req Lv10)", "Act 1+ World Drop / Gamble",
            "Ранній світовий дроп з 10-го рівня (17 Dex, Felt Cap) або гемблінг шоломів.",
            "Early Leveling World Drop (Req Lvl 10, 17 Dex) / Gamble.",
            "Ранний мировой дроп с 10-го уровня (17 Dex) / гемблинг.",
            "Дає +25–35% до ВСІХ стихійних опорів — легко перекриває дірки в резистах у кампанії.",
            "+25–35% All Elemental Resistances makes campaign resistance capping effortless.",
            "+25–35% ко всем стихийным сопротивлениям на время кампании."),
        new("Sapphire Charm", "Utility Charm (Gold/Blue)", "Act 1+ / Trial Drops / Vendor",
            "Падає з мобів і скринь у кампанії/мапах; крафтиться Alteration/Transmutation/Augmentation.",
            "Drops globally in campaign & Waystones or check NPC vendors on level-up.",
            "Падает по всей игре или покупается у вендоров при повышении уровня.",
            "Стакай Thawing/Dousing моди (анти-фріз / збільшені заряди) — обов'язковий сейв-механізм у PoE 2.",
            "Craft Anti-Freeze / Increased Charges to cover lethal ailments automatically.",
            "Роль анти-фриз или доп. заряды — критически важно для выживания.")
    ];

    public static PoeItemObtainInfo ResolveItemObtain(string itemTitleOrText, string slotId, BuildProfile? profile, int characterLevel = 1)
    {
        string text = (itemTitleOrText ?? "").Trim();
        string lower = text.ToLowerInvariant();
        string slotLower = (slotId ?? "").ToLowerInvariant();
        int curLvl = Math.Clamp(characterLevel, 1, 100);

        // 1. Пошук точного уніка або джевела у кураторській базі (боси / унікальні механіки)
        foreach (var entry in CuratedItems)
        {
            if (lower.Contains(entry.ItemTitle.ToLowerInvariant()))
                return entry;
        }

        // 1b. Пошук серед усіх 770+ офіційних унiків PoE 2 з живого GGG `/api/trade2/data/items`
        if (LivePoe2DataService.TryMatchUnique(text, out var liveUniq))
        {
            string baseLbl = string.IsNullOrWhiteSpace(liveUniq.BaseType) ? liveUniq.Category : liveUniq.BaseType;
            string catUk = liveUniq.Category switch
            {
                "weapon" => "Унікальна зброя",
                "armour" => "Унікальна броня",
                "accessory" => "Унікальна біжутерія",
                "jewel" => "Унікальний самоцвіт",
                "flask" => "Унікальна фласка / чарм",
                _ => "Унікальний предмет"
            };
            string chanceHint = !string.IsNullOrWhiteSpace(liveUniq.BaseType) && liveUniq.Category is "weapon" or "armour" or "accessory"
                ? $" / Orb of Chance у білу базу {liveUniq.BaseType}"
                : "";
            string chanceHintEn = !string.IsNullOrWhiteSpace(liveUniq.BaseType) && liveUniq.Category is "weapon" or "armour" or "accessory"
                ? $" or Orb of Chance into white {liveUniq.BaseType}"
                : "";
            string chanceHintRu = !string.IsNullOrWhiteSpace(liveUniq.BaseType) && liveUniq.Category is "weapon" or "armour" or "accessory"
                ? $" или Orb of Chance в белую базу {liveUniq.BaseType}"
                : "";
            return new PoeItemObtainInfo(
                liveUniq.Name,
                $"{catUk} ({baseLbl})",
                $"Live GGG Catalog · Base: {baseLbl}",
                $"Світовий дроп (Акти / Waystones), гемблінг у торговців за золото{chanceHint} або PoE 2 Trade.",
                $"World Drop (Campaign / Waystones), Vendor Gold Gambling{chanceHintEn}, or PoE 2 Trade.",
                $"Мировой дроп (Акты / Waystones), гемблинг за золото{chanceHintRu} или PoE 2 Trade.",
                $"Перевір ціну та моди {liveUniq.Name} через кнопку Trade 2 / PoE2DB перед продажем або крафтом.",
                $"Check live roll ranges and prices for {liveUniq.Name} via Trade 2 / PoE2DB before corrupting or selling.",
                $"Проверь роллы и актуальную цену {liveUniq.Name} через Trade 2 / PoE2DB."
            );
        }

        // 2. Універсальний рівень-орієнтований розбір + жива база GGG (`LivePoe2DataService`)
        LivePoe2DataService.TryGetBaseCategory(text, out string liveCat);
        static bool HasWord(string hay, string word) =>
            System.Text.RegularExpressions.Regex.IsMatch(hay, $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        bool isFlask = slotLower.Contains("flask") || HasWord(lower, "flask");
        bool isCharm = !isFlask && (slotLower.Contains("charm") || HasWord(lower, "charm"));
        bool isOffhand = !isFlask && !isCharm && (slotLower.Contains("offhand") || HasWord(lower, "shield") || HasWord(lower, "buckler") || HasWord(lower, "focus") || HasWord(lower, "quiver"));
        bool isBoots = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("boot") || HasWord(lower, "boots") || HasWord(lower, "sandals") || HasWord(lower, "greaves") || HasWord(lower, "shoes") || HasWord(lower, "slippers"));
        bool isBody = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("body") || HasWord(lower, "armour") || HasWord(lower, "robe") || HasWord(lower, "vestment") || HasWord(lower, "cuirass") || HasWord(lower, "brigandine") || HasWord(lower, "jacket") || HasWord(lower, "garb") || HasWord(lower, "tunic") || HasWord(lower, "coat") || HasWord(lower, "vest") || HasWord(lower, "doublet") || HasWord(lower, "plate") || HasWord(lower, "mail") || HasWord(lower, "raiment") || HasWord(lower, "regalia") || HasWord(lower, "shroud"));
        bool isHelm = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("helm") || HasWord(lower, "helmet") || HasWord(lower, "circlet") || HasWord(lower, "cap") || HasWord(lower, "hood") || HasWord(lower, "crown") || HasWord(lower, "tiara") || HasWord(lower, "mask") || HasWord(lower, "greathelm") || HasWord(lower, "visage") || HasWord(lower, "sallet"));
        bool isGloves = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("glove") || HasWord(lower, "gloves") || HasWord(lower, "wrappings") || HasWord(lower, "gauntlets") || HasWord(lower, "mitts") || HasWord(lower, "bracers") || HasWord(lower, "wraps") || HasWord(lower, "fists"));
        bool isAmulet = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("amulet") || HasWord(lower, "amulet"));
        bool isRing = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("ring") || HasWord(lower, "ring"));
        bool isBelt = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("belt") || HasWord(lower, "belt") || HasWord(lower, "sash"));
        bool isWeapon = !isFlask && !isCharm && !isOffhand && !isBoots && !isBody && !isHelm && !isGloves && !isAmulet && !isRing && !isBelt &&
            (liveCat == "weapon" || slotLower.Contains("weapon") || HasWord(lower, "spear") || HasWord(lower, "quarterstaff") || HasWord(lower, "quarterstaves") ||
             HasWord(lower, "wand") || HasWord(lower, "bow") || HasWord(lower, "sceptre") || HasWord(lower, "staff") || HasWord(lower, "staves") ||
             HasWord(lower, "mace") || HasWord(lower, "crossbow") || HasWord(lower, "talisman") || HasWord(lower, "flail") ||
             HasWord(lower, "sword") || HasWord(lower, "axe") || HasWord(lower, "dagger") || HasWord(lower, "claw"));
        bool isJewel = !isFlask && !isCharm && !isOffhand && !isBoots && !isBody && !isHelm && !isGloves && !isAmulet && !isRing && !isBelt && !isWeapon &&
            (liveCat == "jewel" || slotLower.Contains("jewel") || HasWord(lower, "jewel") || HasWord(lower, "time-lost") || HasWord(lower, "diamond") || HasWord(lower, "emerald") || HasWord(lower, "ruby") || HasWord(lower, "sapphire"));

        string reqWindow = curLvl < 65
            ? $"Lvl ~{Math.Max(1, curLvl - 2)}–{curLvl + 3} (ilvl {curLvl}+)"
            : curLvl < 78
                ? $"ilvl {curLvl}–77 (Early Waystones T1–T8)"
                : "ilvl 78–82+ (Waystones T11–T16)";

        if (isFlask)
        {
            return new PoeItemObtainInfo(
                "Life / Mana Flask", "Flask", curLvl < 65 ? $"Відповідна до Lvl {curLvl} фляга у торговця" : "ilvl 65+ (Ultimate / Colossal)",
                curLvl < 65
                    ? "Не забувай міняти старі фласки в торговця (Lesser → Medium → Greater → Grand → Giant → Colossal → Gargantuan → Transcendent → Ultimate) + ролити Instant Recovery!"
                    : "Дроп Ultimate Flasks на мапах + Transmutation/Augmentation. Якщо граєш через CI — уникай 'removed from Life'!",
                curLvl < 65
                    ? "Upgrade flask tier at town vendors every 10 levels and roll Instant Recovery!"
                    : "Drop Ultimate Flasks on Maps; roll Instant Recovery and verify NO 'removed from Life' if CI.",
                curLvl < 65
                    ? "Обновляй тир фласок у торговца каждые ~10 уровней и ролль Instant Recovery!"
                    : "Фарм Ultimate-фласок на картах + ролл Instant Recovery (остерегайся 'removed from Life' при CI).",
                "Найкращі моди: 'Instant Recovery', збільшене відновлення та набір зарядів.",
                "Best prefixes/suffixes: Instant Recovery, Charges Gained, Increased Amount Recovered.",
                "Лучшие моды: Instant Recovery и набор зарядов."
            );
        }

        if (isCharm)
        {
            return new PoeItemObtainInfo(
                "Charm", "Charm", curLvl < 65 ? $"Lvl ~{curLvl} (Anti-Freeze / Anti-Stun)" : "ilvl 65+ (Endgame Charm)",
                "Світовий дроп, торговці в місті та скрині. Для комфортного проходження тримай Thawing (Freeze), Grounding (Shock) або Stone (Stun) Charm.",
                "World drops, town vendors, and chests. Prioritize Freeze (Thawing), Stun (Stone), or Bleed immunity charms.",
                "Мировой дроп, торговцы и сундуки. Лучше всего Thawing (от заморозки) и Stone (от оглушения).",
                "Шукай збільшену тривалість і зниження витрати зарядів.",
                "Look for increased duration and reduced charges used.",
                "Ищи увеличенную длительность и меньше трату зарядов."
            );
        }

        if (isOffhand)
        {
            return new PoeItemObtainInfo(
                "Offhand (Shield / Focus / Quiver)", "Offhand", reqWindow,
                curLvl < 65
                    ? "Торговці в місті щоразу при ап-левелі, гемблінг за золото та рідкісний дроп. На щитах/фокусах шукай Life, +% Block / ES та 1–2 резисти."
                    : "Waystones / Rog Expedition / Trade 2. На щитах цінується +ES/Armour, Block% та резисти; на Quiver — Flat Dmg + Attack Speed +Bow Skills.",
                curLvl < 65
                    ? "Level-up vendors, Gold gambling, or campaign drops. Look for Life/ES, Block/Resistances (or Flat Dmg on Quiver)."
                    : "Waystones, Rog Expeditions, or Trade 2 for high ES/Block Shields or +Skills Quivers.",
                curLvl < 65
                    ? "Вендоры при апе уровня, гемблинг и дроп в актах: ищи Life/ES, резисты и Block (или плоский урон на Quiver)."
                    : "Waystones / Trade 2: высокий ES/Block на щитах и фокусах или +Skills на Quiver.",
                "Додай Artificer's Orb + руну в оффхенд, щоб закрити блок або стихійний опір.",
                "Socket a Rune into your offhand to plug resistance or defence gaps early.",
                "Вставь руну в сокет оффхенда, чтобы быстро добрать защиту или резист."
            );
        }

        if (isWeapon)
        {
            string wepName = !string.IsNullOrWhiteSpace(text) ? text.Split(['\n', '\r'])[0].Trim()
                : (!string.IsNullOrEmpty(profile?.PreferredWeaponClass) ? profile.PreferredWeaponClass : "Weapon");

            if (curLvl < 22)
            {
                return new PoeItemObtainInfo(
                    wepName, "Campaign Weapon", $"Req Lvl {Math.Max(1, curLvl - 2)}–{curLvl + 3} (Act 1)",
                    "Кожен ап-левел заходь до торговців у Clearfell (Renly / Finn — асортимент оновлюється щоразу!), шукай зброю з +% Phys або Flat Elemental і докинь Orb of Transmutation / Augmentation + Rune у сокет.",
                    "Check Act 1 town vendors (Renly / Finn) every time you level up (inventory refreshes!), gamble at Finn, or slam Transmute + Augment + Iron Rune into a high base.",
                    "При каждом повышении уровня проверяй торговцев в 1 Акте (Renly / Finn), азартный обмен за золото или крафти базу Transmute + Augment + Iron Rune.",
                    "На старті найсильніше бустить плоский урон на зброї та +Attack Speed — не шукай ендгейм ilvl 80+ баз.",
                    "Early campaign DPS comes mostly from Flat Added Damage + Iron Rune; replace every 6–8 levels at vendors.",
                    "В начале игры урон даёт плоский урон и Iron Rune; обновляй пушку каждые 6–8 уровней у торговцев."
                );
            }
            if (curLvl < 65)
            {
                return new PoeItemObtainInfo(
                    wepName, "Campaign Weapon", $"Req Lvl {Math.Max(1, curLvl - 3)}–{curLvl + 3} (Acts 2–4 / Interludes)",
                    "Перевіряй торговців (Shambrin в Акті 2 / Servi у 3-му / Kingsmarch у 4-му), гемблінг за золото або візьми готову пушку на PoE 2 Trade за 1 Exalt.",
                    "Check Act 2–4 / Interlude vendors on level-up, gamble weapons with excess Gold, use Essences, or grab a 1-Exalt weapon on Trade.",
                    "Проверяй вендоров 2–4 Акта / Interludes при апе уровня, гемблинг или купи пушку на Trade 2 за 1 Exalt.",
                    "Вставляй Iron Runes (+% Phys) або Elemental Runes; не витрачай дорогі Divine/Exalt орби на тимчасову базу.",
                    "Add Iron or Elemental Runes for instant flat/phys scaling without wasting expensive currency.",
                    "Вставляй Iron или Elemental руны в сокеты и не трать дорогие орбы на временную базу."
                );
            }
            return new PoeItemObtainInfo(
                wepName, "Endgame Weapon", "ilvl 78–82+ (Waystones T11–T16)",
                "Дроп із монстрів Waystones T11–T16, Rog-крафт в Expedition або база ilvl 81+ з 1–2 T1 модами на Trade під Essence/Exalt.",
                "Farm T11–T16 Waystones for ilvl 78–82+ bases, Expeditions (Rog), or craft 1–2 T1 mod bases from Trade.",
                "Фармится на картах T11–T16 (базы ilvl 78–82+), крафт Rog или покупка заготовки на Trade 2.",
                "Вставляй Iron Runes (+% Phys) / Soul Cores; для спелів пріоритет +4..+5 Skills + Cast Speed.",
                "Socket Iron Runes for Attacks or prioritize +4/+5 Skill Levels + Cast Speed on Spell weapons.",
                "Вставляй Iron Runes / Soul Cores; для чар важны +уровни умений и Cast Speed."
            );
        }

        if (isBoots)
        {
            return new PoeItemObtainInfo(
                "Boots", "Armour / Boots", curLvl < 65 ? $"Lvl ~{curLvl} (10–25% MS)" : "ilvl 65+ (30% MS) / 82+ (35% MS)",
                curLvl < 65
                    ? "У кампанії дивись торговців щоразу при підвищенні рівня (обов'язково шукай 10–25% Movement Speed!) або гембли за золото у Фінна/Аlva."
                    : "Світовий дроп (Waystones), гемблінг або PoE 2 Trade із фільтром >=25–30% Movement Speed.",
                curLvl < 65
                    ? "Check town vendors every level-up for 10–25% Movement Speed boots, or gamble Boots with spare Gold."
                    : "World drop, Vendor Gambling, or PoE 2 Trade filtered for >=25–30% Movement Speed.",
                curLvl < 65
                    ? "Ищи сапоги с 10–25% Movement Speed у вендоров при каждом апе уровня или в гемблинге."
                    : "Мировой дроп (Waystones) или Trade 2 с фильтром от 25–30% Movement Speed.",
                "Чоботи без Movement Speed майже завжди повільніші навіть за більші резисти.",
                "Movement Speed beats small resistance gains while leveling and mapping.",
                "Скорость бега важнее пары лишних процентов резистов."
            );
        }

        if (isBody)
        {
            return new PoeItemObtainInfo(
                "Body Armour", "Body Armour", reqWindow,
                curLvl < 65
                    ? "Дроп по ходу актів, нагорода за квести або апгрейд у торговця. Пріоритет: потрібні бази (ES/Evasion) + Life та 1–2 резисти."
                    : "Дроп топових баз на Waystones T11+, Artificer's Orb на 2 сокети + Iron/Greater Runes (+% Defences).",
                curLvl < 65
                    ? "Campaign drops / vendors matching your defence bases + Life/Resistances."
                    : "Drop high bases on Waystones T11+, double-socket with Artificer's Orbs, and stack Defence % Runes.",
                curLvl < 65
                    ? "Дроп в актах или у вендоров под твой тип защиты + Life/резисты."
                    : "Топ-базы падают на Waystones T11+, делай 2 гнезда Artificer's Orb и вставляй руны.",
                profile?.IsCi == true && curLvl >= 60
                    ? "Для CI шукай 600–1100+ чистого Energy Shield — Body Armour дає найбільше ES!"
                    : "Шукай високу броню/ухилення/ES під свій білд та 1–2 сокети під руни.",
                profile?.IsCi == true && curLvl >= 60
                    ? "Your Body Armour is the #1 source of Energy Shield for CI."
                    : "Focus on your build's defence base (ES / EV / Armour) plus Resistances.",
                profile?.IsCi == true && curLvl >= 60
                    ? "Главный источник ES для CI — высокий Energy Shield на броне."
                    : "Главный слот выживаемости: профильная защита + резисты."
            );
        }

        if (isHelm)
        {
            return new PoeItemObtainInfo(
                "Helmet", "Armour / Helmet", reqWindow,
                curLvl < 65
                    ? "Світовий дроп / торговці по рівню / гемблінг. Збирай рідкісні шоломи з Life/ES та двома резистами."
                    : "Світовий дроп / Expedition / Trade 2. Шукай +ES/EV, Rarity, Crit або +Level of Skills.",
                curLvl < 65
                    ? "World drops, level-up vendor checks, or Gold gambling for Life/ES + Resistances."
                    : "World drop / Gambling / Trade 2. Look for high ES/EV, Resistances, Rarity, or +Skill Levels.",
                curLvl < 65
                    ? "Дроп в кампании и вендоры по уровню (Life/ES + резисты)."
                    : "Мировой дроп / Trade 2: высокий ES/EV, резисты, Rarity или +уровень умений.",
                "Використовуй Artificer's Orb + руни у сокеті, щоб швидко закрити просілий резист.",
                "Use socket Runes to fix missing Fire/Cold/Lightning resistances cleanly.",
                "Используй руны в сокетах для закрытия просевших резистов."
            );
        }

        if (isGloves)
        {
            return new PoeItemObtainInfo(
                "Gloves", "Armour / Gloves", reqWindow,
                curLvl < 65
                    ? "Світовий дроп / торговці / Alchemy на базу під твій рівень. Для атак шукай Flat Added Damage + Attack Speed."
                    : "Waystones / Trade 2 / Essence. Для Attack-білдів обов'язкові Flat Damage + Attack Speed!",
                curLvl < 65
                    ? "Campaign drops & vendors; Attack builds love any Flat Added Damage or Attack Speed on gloves."
                    : "World drop / Trade 2. Attack builds gain massive DPS from double Flat Added Damage + Attack Speed.",
                curLvl < 65
                    ? "Дроп в кампании и вендоры; атакам ищи плоский урон и Attack Speed."
                    : "Мировой дроп / Trade 2: плоский урон к атакам, Attack Speed и резисты.",
                "Шукай Attack Speed, +Level of Melee/Projectile Skills, резисти або Leech.",
                "Priority: Flat Damage to Attacks, Attack Speed, Crit/Resists, or +Skill Levels.",
                "Приоритет: плоский урон, Attack Speed, резисты или +Melee умения."
            );
        }

        if (isAmulet || isRing || isBelt)
        {
            return new PoeItemObtainInfo(
                isAmulet ? "Amulet" : isRing ? "Ring" : "Belt",
                "Jewellery",
                curLvl < 65 ? $"Lvl ~{curLvl} (Campaign Jewellery)" : "ilvl 75–82+ (Catalysts / Anoint)",
                curLvl < 65
                    ? "Скрині, квестові нагороди, торговці та гемблінг. На кільцях/амулетах у актах закривай опори й атрибути (Str/Dex/Int під камені та зброю)!"
                    : "Ritual, Breach Rings (до +50% Quality!), Delirium (олії Anoint на амулет) та Trade 2.",
                curLvl < 65
                    ? "Quest rewards, rare chests, and town vendors — cover resistances and Str/Dex/Int requirements for your gems/weapons."
                    : "Ritual, Breach Rings (50% Catalyst Quality), Delirium Anoint on Amulet, or Trade.",
                curLvl < 65
                    ? "Квесты, сундуки и вендоры — закрывай резисты и атрибуты (Str/Dex/Int) под пушку и камни."
                    : "Ритуал, Breach-кольца, Delirium-намазка амулета и Trade 2.",
                isAmulet
                    ? "Після 30+ рівня цінуй +Spirit, +Skills і намазку Delirium (Distilled Emotions)."
                    : "Кільця дають найбільше резистів, атрибутів та плоского урону в атаки.",
                isAmulet
                    ? "Look for +Spirit, +Attributes, +Skill Levels, and Delirium Anoint later."
                    : "Rings cleanly fix resistances, attributes, and flat attack damage.",
                isAmulet
                    ? "На амулете ищи +Spirit, атрибуты, +умения и намазку."
                    : "Кольца лучше всего закрывают резисты и дают плоский урон."
            );
        }

        if (isJewel)
        {
            return new PoeItemObtainInfo(
                "Rare / Unique Jewel", "Jewel", "Area Lvl 22+ / Trial of Sekhemas / Chaos",
                "Скрині в Trial of the Sekhemas (найкраще джерело джевелів ще з 2-го Акту!), Trial of Chaos, Ritual та Trade 2.",
                "Trial of the Sekhemas Jeweller caches (available from Act 2!), Trial of Chaos, Ritual, or Trade.",
                "Сундуки Trial of the Sekhemas (уже со 2-го Акта!), Trial of Chaos и Trade 2.",
                "Шукай Max Energy Shield %, Crit Damage Bonus, Attack/Cast Speed або Mana on Kill (2%).",
                "Look for % ES / Crit Bonus / Elemental Dmg / Attack Speed / Gain Mana on Kill.",
                "Ищи % ES, Crit Bonus, скорость и Mana on Kill."
            );
        }

        return new PoeItemObtainInfo(
            string.IsNullOrWhiteSpace(text) ? (slotId ?? "Item") : text,
            "PoE 2 Item",
            reqWindow,
            curLvl < 65
                ? "Дроп по ходу кампанії, перевірка торговців при підвищенні рівня, гемблінг за золото або недорогий апгрейд на PoE 2 Trade."
                : "Світовий дроп (Waystones), гемблінг у Alva/Finn, крафт Essence або пошук на PoE 2 Trade.",
            curLvl < 65
                ? "Campaign drops, vendor checks on level-up, Gold Gambling, or affordable PoE 2 Trade upgrades."
                : "World drop (Waystones), Gold Gambling, Essence crafting, or PoE 2 Trade.",
            curLvl < 65
                ? "Дроп в актах, проверка вендоров при апе уровня, гемблинг или Trade 2."
                : "Мировой дроп (Waystones), азарт у торговцев, Essences или Trade 2.",
            "Використовуй Ctrl+C в грі, щоб порівняти річ і зі своїм екіпом, і з білдом.",
            "Press Ctrl+C in-game to compare both vs your equipped gear and your active build stage.",
            "Нажми Ctrl+C в игре для двойного сравнения: с твоим экипом и с билдом."
        );
    }
}
