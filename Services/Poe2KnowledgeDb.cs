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
        new("Hand of Wisdom and Action", "Unique Gloves (Furtive Wraps)", "Breach Boss: Xesht",
            "Ексклюзивний дроп із Xesht, We That Are One (Breachstone / Twisted Domain).",
            "Exclusive drop from Xesht, We That Are One (Breach Pinnacle Boss).",
            "Эксклюзивный дроп с босса Разлома — Xesht (Twisted Domain).",
            "Масштабує плоский урон блискавкою та швидкість атаки від Int/Dex — топ для Monk/Attribute-білдів.",
            "Scales Attack Speed per Dex & Lightning Damage per Int — premier attribute-stacking gloves.",
            "Даёт скорость от Dex и урон молнией от Int — топ-перчатки для attribute-билдов."),
        new("Ingenuity", "Unique Belt (Utility Belt)", "Ritual Boss: King in the Mists",
            "Бос-дроп із King in the Mists (An Audience with the King у Ritual).",
            "Boss drop from The King in the Mists (An Audience with the King Ritual encounter).",
            "Падает с Короля в Туманах (An Audience with the King, Ритуал).",
            "Множить ефект обох кілець; шукай якомога вищий % бонусу та Corruption.",
            "Amplifies bonuses from both equipped Rings — roll % is everything.",
            "Усиливает эффекты обоих колец — решающее значение имеет высокий % ролла."),
        new("Astramentis", "Unique Amulet (Stellar Amulet)", "World Drop T0 / Chance Orb (Stellar Amulet)",
            "Ультра-рідкісний світовий дроп (Tier 0) або Орб Шансу (Orb of Chance) у білу базу Stellar Amulet!",
            "Ultra-rare T0 World Drop or Orb of Chance into white Stellar Amulet bases.",
            "Ультра-редкий мировой дроп (T0) или Orb of Chance в базу Stellar Amulet!",
            "Ніколи не ігноруй білі Stellar Amulets — їх скуповують навіть пустими під крафт через Chance.",
            "Always pick up white Stellar Amulet bases for gambling / selling in bulk.",
            "Всегда подбирай белые базы Stellar Amulet — они ценятся на рынке."),
        new("Headhunter", "Unique Belt (Heavy Belt)", "World Drop T0 / Chance Orb (Heavy Belt)",
            "Ультра-рідкісний світовий дроп (високі карти/Chance Orb на Heavy Belt).",
            "Ultra-rare T0 World Drop or Orb of Chance on white Heavy Belt.",
            "Ультра-редкий мировой дроп или Orb of Chance на Heavy Belt.",
            "Краде моди вбитих рідкісних монстрів на 60с — король швидкого кліру мап.",
            "Steals Rare monster modifiers on kill — unmatched speed-mapping belt.",
            "Крадёт свойства редких монстров при убийстве."),
        new("Temporalis", "Unique Body Armour (Silk Robe)", "Sekhemas Hitless Run",
            "Винагорода за проходження Trial of the Sekhemas з реліквією 'The Last Flame' без отримання урону (1 Honor).",
            "Exclusive reward for no-hit Trial of the Sekhemas using 'The Last Flame' relic.",
            "Уникальная награда за прохождение Trial of the Sekhemas без получения урона (1 Honor).",
            "Зменшує перезарядку навичок (включно з Blink).",
            "Massive skill cooldown reduction enabling instant Blink setups.",
            "Резко снижает перезарядку умений (включая Blink)."),
        new("Tangletongue", "Unique Spear (Forked Spear)", "World Drop / Gambling",
            "Світовий дроп (кампанія / карти), гемблінг списів у торговця або Orb of Chance на Forked Spear.",
            "Global world drop / Spear vendor gambling / Orb of Chance on Forked Spear base.",
            "Мировой дроп, азартный обмен у торговца или Chance Orb в Forked Spear.",
            "Високий базовий шанс кріта і Fork Критів роблять його одним із найкращих дешевих/середніх списів PoE 2.",
            "Double crit roll mechanic ('Forks Critical Hits') makes high crit rolls extremely strong.",
            "Двойной расчёт критов делает высокий ролл крита невероятно сильным."),
        new("Pillar of the Caged God", "Unique Quarterstaff", "Act 1+ World Drop / Chance Orb",
            "Частий ранній світовий дроп з Акту 1 або Orb of Chance; ідеальний для прокачки стакерів атрибутів.",
            "Common early World Drop from Act 1+ or cheap Trade purchase.",
            "Частый ранний дроп с 1-го Акта или дешёвый слот на трейде.",
            "Синергує з плоским уроном на перснях/рукавичках та великою кількістю Str/Dex/Int.",
            "Scales AoE/Speed/Damage cleanly off flat added damage on Rings/Gloves and Attributes.",
            "Отлично масштабирует плоский урон с колец и перчаток от ваших атрибутов."),
        new("Shavronne", "Unique Satchel / Flask / Belt", "World Drop / Campaign+",
            "Світовий дроп (мапи / кампанія) або швидка покупка на Trade за кілька Exalt.",
            "Global World Drop or inexpensive Trade purchase.",
            "Мировой дроп или недорогая покупка на трейде.",
            "Для CI-білдів дозволяє безпечно поповнювати ES від фляг — обов'язково уникай моду 'removed from Life' без ES-конверсії.",
            "Crucial Life-to-ES interaction; verify flask mods carefully when playing Chaos Inoculation.",
            "Полезно для CI, но внимательно проверяй отсутствие смертельных модов на фласках."),
        new("Polcirkeln", "Unique Ring (Sapphire Ring)", "World Drop / Chance Orb",
            "Світовий дроп (Акти/Waystones) або Orb of Chance на Sapphire Ring.",
            "World Drop or Orb of Chance on Sapphire Ring.",
            "Мировой дроп или Orb of Chance на Sapphire Ring.",
            "Охолоджені вороги розбиваються як заморожені — миттєвий запуск ланцюгів Herald of Ice.",
            "Chilled enemies shatter as if Frozen — unlocks huge Herald of Ice clears early.",
            "Охлаждённые враги раскалываются как замороженные — запускает цепочки Herald of Ice."),
        new("Thief's Torment", "Unique Ring (Emerald Ring)", "World Drop / Chance Orb",
            "Світовий дроп або Orb of Chance на Emerald Ring (займає обидва слоти кілець!).",
            "Global Drop / Chance Orb on Emerald Ring (prevents using second ring).",
            "Мировой дроп (запрещает использовать второе кольцо).",
            "Дає величезний Life/Mana gain on Hit — топ для прокачки й складних босів до ендгейму.",
            "Massive Mana/Life gained on Hit solves sustain completely during leveling and bossing.",
            "Полностью решает проблему маны и здоровья при попадании."),
        new("Beacon of Azis", "Unique Amulet (Solar Amulet)", "World Drop / Arbiter Pool",
            "Світовий дроп високого рівня / боси Атласу; дозволяє критам повністю ігнорувати стихійні опори ворогів.",
            "High-tier drop; Critical Hits ignore all enemy Elemental Resistances!",
            "Высокоуровневый дроп; критические удары полностью игнорируют стихийные сопротивления.",
            "Знімає потребу брати Exposure/Penetration на дереві для кріт-білдів.",
            "Frees massive passive tree/curse budget since Crits treat enemy Ele Res as 0%.",
            "Освобождает кучу очков пассивок, так как криты игнорируют резисты."),
        new("Alpha's Howl", "Unique Helmet (Armoured Cap)", "World Drop T1+",
            "Рідкісний світовий дроп високих рівнів; подвоює Presence radius або дає великий +Spirit (+100 Spirit).",
            "High-tier World Drop granting +100 Spirit and doubled Presence area.",
            "Редкий мировой дроп высокого уровня; даёт +100 Spirit и удваивает Presence.",
            "Дозволяє включити зайву ауру/Trinity/Blink у Дух-резерві.",
            "Unlocks an entire extra Spirit gem or Meta aura setup.",
            "Позволяет включить дополнительный Spirit-бафф или ауру."),
        new("Atziri's Disdain", "Unique Helmet (Gold Circlet)", "World Drop / Chance Orb",
            "Світовий дроп або Orb of Chance; додає екстра Energy Shield від максимального Life.",
            "World Drop / Chance Orb; grants Extra Maximum Energy Shield equal to % of Life.",
            "Мировой дроп или Chance Orb; добавляет экстра ES от максимального здоровья.",
            "Чудовий гібридний шолом перед повним переходом у CI.",
            "Outstanding mid-game ES/Life hybrid helmet before swapping to pure CI.",
            "Отличный переходный гибридный шлем до перехода на CI."),
        new("Ghostwrithe", "Unique Body Armour (Tattered Robe)", "Early Act 1+ Drop",
            "Дешевий ранній унiк з Акту 1+; конвертує 50% Life в Energy Shield.",
            "Early Act 1+ World Drop; converts 50% Max Life to Energy Shield.",
            "Ранний дроп с 1-го Акта; конвертирует 50% здоровья в Energy Shield.",
            "Найкраща дешева броня для переходу в ES-білди ще в кампанії.",
            "Best budget body armour for early ES / CI transitions during campaign.",
            "Лучшая дешёвая броня для раннего старта через Energy Shield."),
        new("Wanderlust", "Unique Boots (Wrapped Sandals)", "Act 1 World Drop / Gamble",
            "Світовий дроп з 1-го рівня (Act 1 Mud Burrow / Clearfell) або гемблінг у Фінна (Finn).",
            "Drops from Level 1+ anywhere in Act 1 or gamble Wrapped Sandals.",
            "Падает с 1-го уровня (Акт 1) или выбивается через азарт у Фінна.",
            "+20% Movement Speed та імунітет до Chill/Freeze (через Slow potency) з 1-го рівня.",
            "+20% Movement Speed right at Level 1 — ideal campaign leveling boots.",
            "+20% скорости бега уже с 1 уровня — лучшие стартовые сапоги."),
        new("Goldrim", "Unique Helmet (Felt Cap)", "Act 1+ World Drop / Gamble",
            "Світовий дроп з низьких рівнів або гемблінг шоломів.",
            "Early Leveling World Drop / Gamble.",
            "Ранний мировой дроп с низких уровней.",
            "Дає +30–40% до ВСІХ стихійних опорів — перекриває дірки в резистах на всю кампанію.",
            "Up to +40% All Elemental Resistances makes campaign resistance capping effortless.",
            "До +40% ко всем стихийным сопротивлениям на всю кампанию."),
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

        // 1. Пошук точного уніка або джевела у базі
        foreach (var entry in CuratedItems)
        {
            if (lower.Contains(entry.ItemTitle.ToLowerInvariant()))
                return entry;
        }

        // 2. Універсальний рівень-орієнтований розбір (спочатку явні Flask/Charm/Offhand/Jewel і цілі слова, щоб 'simmeRING' не падав в Jewellery!)
        static bool HasWord(string hay, string word) =>
            System.Text.RegularExpressions.Regex.IsMatch(hay, $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        bool isFlask = slotLower.Contains("flask") || HasWord(lower, "flask");
        bool isCharm = !isFlask && (slotLower.Contains("charm") || HasWord(lower, "charm"));
        bool isOffhand = !isFlask && !isCharm && (slotLower.Contains("offhand") || HasWord(lower, "shield") || HasWord(lower, "buckler") || HasWord(lower, "focus") || HasWord(lower, "quiver"));
        bool isJewel = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("jewel") || HasWord(lower, "diamond") || HasWord(lower, "emerald") || HasWord(lower, "ruby") || HasWord(lower, "sapphire"));
        bool isBoots = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("boot") || HasWord(lower, "boots") || HasWord(lower, "sandals") || HasWord(lower, "greaves") || HasWord(lower, "shoes"));
        bool isBody = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("body") || HasWord(lower, "armour") || HasWord(lower, "robe") || HasWord(lower, "vestment") || HasWord(lower, "cuirass") || HasWord(lower, "brigandine"));
        bool isHelm = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("helm") || HasWord(lower, "helmet") || HasWord(lower, "circlet") || HasWord(lower, "cap") || HasWord(lower, "hood") || HasWord(lower, "crown"));
        bool isGloves = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("glove") || HasWord(lower, "gloves") || HasWord(lower, "wrappings") || HasWord(lower, "gauntlets") || HasWord(lower, "mitts") || HasWord(lower, "bracers"));
        bool isAmulet = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("amulet") || HasWord(lower, "amulet"));
        bool isRing = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("ring") || HasWord(lower, "ring"));
        bool isBelt = !isFlask && !isCharm && !isOffhand && (slotLower.Contains("belt") || HasWord(lower, "belt") || HasWord(lower, "sash"));
        bool isWeapon = !isFlask && !isCharm && !isOffhand && !isBoots && !isBody && !isHelm && !isGloves && !isAmulet && !isRing && !isBelt && !isJewel &&
            (slotLower.Contains("weapon") || HasWord(lower, "spear") || HasWord(lower, "quarterstaff") || HasWord(lower, "quarterstaves") ||
             HasWord(lower, "wand") || HasWord(lower, "bow") || HasWord(lower, "sceptre") || HasWord(lower, "staff") || HasWord(lower, "staves") ||
             HasWord(lower, "mace") || HasWord(lower, "crossbow") || HasWord(lower, "talisman") || HasWord(lower, "flail") ||
             HasWord(lower, "sword") || HasWord(lower, "axe") || HasWord(lower, "dagger") || HasWord(lower, "claw"));

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
                    ? "Не забувай міняти старі фласки в торговця (Lesser → Medium → Greater → Grand) що 10–12 рівнів + ролити Instant Recovery через Transmutation!"
                    : "Дроп Ultimate/Colossal Flasks на мапах + Transmutation/Augmentation. Якщо граєш через CI — уникай 'removed from Life'!",
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
                    wepName, "Campaign Weapon", $"Req Lvl {Math.Max(1, curLvl - 3)}–{curLvl + 3} (Acts 2–3)",
                    "Перевіряй торговців (Shambrin в Акту 2 / Servi у 3-му), гемблінг за золото або візьми готову синю/жовту пушку на PoE 2 Trade за 1 Exalt.",
                    "Check Act 2–3 vendors on level-up, gamble weapons with excess Gold, use Lesser Essences on clean bases, or grab a 1-Exalt leveling weapon on Trade.",
                    "Проверяй вендоров 2–3 Акта при апе уровня, гемблинг за золото или купи прокачную пушку на Trade 2 за 1 Exalt.",
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
