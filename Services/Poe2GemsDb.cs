using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// База знань каменів умінь, підтримки (Tier I/II/III) та каменів Духу (Spirit) Path of Exile 2:
/// мінімальний рівень персонажа, Uncut-тір, місце отримання (Акт/квест/дроп), вимоги атрибутів, Spirit та опис механіки.
/// </summary>
public static class Poe2GemsDb
{
    private sealed record GemTemplate(
        string CanonicalName,
        GemCategory Category,
        string Attribute,
        int MinUncutLevel,
        int SupportTier,
        int SpiritCost,
        int ManaMultPct,
        string Tags,
        string DescUk,
        string DescEn,
        string DescRu,
        string? CustomObtainUk = null,
        string? CustomObtainEn = null,
        string? CustomObtainRu = null
    );

    // Офіційна таблиця PoE 2: Uncut Skill Gem Tier (1..20) -> орієнтовний мін. рівень персонажа
    private static readonly int[] SkillUncutCharLevel =
    [
        1, 1, 3, 6, 10, 14, 18, 22, 26, 31, 36, 41, 46, 52, 58, 65, 68, 72, 78, 84, 90
    ];

    private static readonly GemTemplate[] Catalog =
    [
        // --- Monk / Quarterstaff / Unarmed / Elemental ---
        new("Whirling Slash", GemCategory.ActiveSkill, "Dex", 1, 0, 0, 100, "Attack, AoE, Melee, Spear",
            "Круговий ривок списом, що створює вихор; накопичує заряди вітру для посилення Twister.",
            "Spinning lunge that kicks up a whirlwind; stacks wind stages to empower Twister.",
            "Круговой выпад копьём, создающий вихрь; копит заряды ветра для усиления Twister."),
        new("Twister", GemCategory.ActiveSkill, "Dex", 1, 0, 0, 100, "Attack, Projectile, Spear",
            "Запускає смерчі, які відскакують від стін і підхоплюють стихійні поверхні / вихори Whirling Slash.",
            "Fires bouncing tornados that inherit ground elements and Whirlwind stacks.",
            "Запускает смерчи, отскакивающие от стен и впитывающие стихийные поверхности и вихри Whirling Slash."),
        new("Whirling Assault", GemCategory.ActiveSkill, "Dex/Int", 7, 0, 0, 100, "Attack, AoE, Melee, Quarterstaff",
            "Серія стрімких кругових ударів бойовим посохом (або без зброї через Hollow Palm).",
            "Series of rapid spinning Quarterstaff strikes (or unarmed with Hollow Palm).",
            "Серия стремительных круговых ударов боевым посохом (или без оружия с Hollow Palm)."),
        new("Tempest Flurry", GemCategory.ActiveSkill, "Dex/Int", 3, 0, 0, 100, "Attack, Lightning, Melee, Strike",
            "Швидке 4-ударне комбо блискавки; останній удар завдає серії шокуючих імпульсів.",
            "Rapid 4-hit Lightning melee combo ending in multi-strike Shockwaves.",
            "Быстрое 4-ударное комбо молнии; финальный удар создаёт серию шоковых волн."),
        new("Falling Thunder", GemCategory.ActiveSkill, "Dex/Int", 1, 0, 0, 100, "Attack, Lightning, AoE, Slam",
            "Удар блискавкою по землі; споживає Power Charges, випускаючи потужні електричні хвилі.",
            "Slams lightning into the ground, consuming Power Charges for sweeping projectiles.",
            "Удар молнии по земле; поглощает Power Charges, выпуская мощные волны."),
        new("Killing Palm", GemCategory.ActiveSkill, "Dex/Int", 1, 0, 0, 100, "Attack, Melee, Execute",
            "Ривок до добивання ворога (Culling Strike) — миттєво дає Power Charge.",
            "Dashing unarmed cull strike that grants Power Charges on kill.",
            "Рывок для добивания врага — мгновенно даёт Power Charge при убийстве."),
        new("Charged Staff", GemCategory.ActiveSkill, "Dex/Int", 7, 0, 0, 100, "Attack, Lightning, Buff",
            "Споживає Power Charge і заряджає атаки плоским Lightning-уроном та дальніми хвилями.",
            "Consumes Power Charges to imbue strikes with flat Lightning damage and shockwaves.",
            "Поглощает Power Charge и наделяет атаки плоским уроном молнией и волнами."),
        new("Tempest Bell", GemCategory.ActiveSkill, "Dex/Int", 3, 0, 0, 100, "Attack, Combo, Lightning, AoE",
            "Ставить дзвін після комбо-ударів; кожен удар по дзвону створює потужну стихійну ударну хвилю по босах.",
            "Drops a resonant bell after combo hits; striking it triggers massive AoE shockwaves on bosses.",
            "Ставит колокол после комбо; каждый удар по колоколу вызывает мощные волны урона по боссам."),
        new("Ice Strike", GemCategory.ActiveSkill, "Dex/Int", 5, 0, 0, 100, "Attack, Cold, Melee",
            "Швидкі крижані атаки з високою швидкістю накопичення Freeze та 3-ударним фінішем.",
            "Fast Cold melee combo with high Freeze buildup and sweeping third hit.",
            "Быстрая ледяная серия ударов с высоким наложением Freeze."),
        new("Shattering Palm", GemCategory.ActiveSkill, "Dex/Int", 5, 0, 0, 100, "Attack, Cold, Dash, AoE",
            "Накладає крижаний заряд на ворога, який вибухає осколками холоду після отримання урону.",
            "Dashes and marks enemies with ice shards that detonate after taking damage.",
            "Рывок, помечающий врагов ледяными осколками, взрывающимися при получении урона."),
        new("Flicker Strike", GemCategory.ActiveSkill, "Dex/Int", 13, 0, 0, 100, "Attack, Physical, Melee, Teleport",
            "Миттєва телепортація між ворогами серією ударів за рахунок Power Charges.",
            "Teleports rapidly between targets with heavy strikes powered by Power Charges.",
            "Мгновенная телепортация между врагами за счёт Power Charges."),
        new("Mantra of Destruction", GemCategory.ActiveSkill, "Dex/Int", 9, 0, 0, 100, "Buff, Chaos, Melee",
            "Споживає комбо-удари для посилення наступної атаки екстра-уроном хаосу.",
            "Consumes Combo stacks to grant extra Chaos damage to your next attack.",
            "Поглощает комбо для усиления следующей атаки дополнительным уроном хаоса."),
        new("Storm Wave", GemCategory.ActiveSkill, "Dex/Int", 9, 0, 0, 100, "Attack, Lightning, Slam",
            "Випускає далекобійну тріщину блискавки вздовж землі.",
            "Sends out a long-reaching crackling Lightning fissure along the ground.",
            "Выпускает дальнобойный разлом молнии по земле."),

        // --- Spear / Bow / Crossbow / Spell / Minion ---
        new("Lightning Spear", GemCategory.ActiveSkill, "Dex", 5, 0, 0, 100, "Attack, Projectile, Lightning, Spear",
            "Кидає спис блискавки, який розлітається в додаткові розряди при влучанні.",
            "Hurls an electrified spear that splits into secondary lightning bolts on impact.",
            "Бросает копьё молнии, расщепляющееся на дополнительные заряды при попадании."),
        new("Ice-Tipped Arrows", GemCategory.ActiveSkill, "Dex", 3, 0, 0, 100, "Attack, Cold, Bow, Buff",
            "Наділяє постріли/вихори крижаним ефектом і високим шансом заморозки.",
            "Imbues projectile skills with Cold damage and Freeze potential.",
            "Наделяет снаряды уроном холода и заморозкой."),
        new("Frost Bomb", GemCategory.ActiveSkill, "Int", 3, 0, 0, 100, "Spell, Cold, AoE, Exposure",
            "Ставить крижану сферу, що знижує опір холоду (Cold Exposure) та вибухає.",
            "Drops a pulsing orb that applies Cold Exposure before detonating.",
            "Ставит ледяную сферу, накладывающую Cold Exposure перед взрывом."),
        new("Spark", GemCategory.ActiveSkill, "Int", 1, 0, 0, 100, "Spell, Lightning, Projectile, Duration",
            "Випускає непередбачувані іскри блискавки, що відскакують по всій кімнаті.",
            "Launches erratic lightning projectiles that ricochet off walls.",
            "Выпускает хаотичные искры молнии, рикошетящие от стен."),
        new("Arc", GemCategory.ActiveSkill, "Int", 5, 0, 0, 100, "Spell, Lightning, Chaining",
            "Миттєвий розряд блискавки, що перестрибує між цілями та споживає Shock.",
            "Chains instant lightning arcs across nearby enemies.",
            "Мгновенная цепная молния, прыгающая по врагам."),
        new("Lightning Rod", GemCategory.ActiveSkill, "Dex", 1, 0, 0, 100, "Attack, Bow, Lightning, AoE",
            "Стріли залишають громовідводи в землі, які вибухають від будь-яких Chain/Lightning атак.",
            "Fires lightning rods into the ground that burst whenever hit by Lightning.",
            "Стрелы оставляют громоотводы, взрывающиеся от урона молнией."),
        new("Lightning Arrow", GemCategory.ActiveSkill, "Dex", 1, 0, 0, 100, "Attack, Bow, Lightning, Projectile",
            "Головна скіл-стріла променями вражає сусідні цілі та активує Lightning Rod.",
            "Fires a charged arrow that blasts nearby foes and triggers Lightning Rods.",
            "Выпускает заряженную стрелу, бьющую соседние цели и громоотводы."),
        new("Orb of Storms", GemCategory.ActiveSkill, "Int", 3, 0, 0, 100, "Spell, Lightning, Duration",
            "Створює сферу блискавки, яка б'є ворогів щоразу, коли ви використовуєте Lightning-скіл поруч.",
            "Stationary storm orb that zaps foes whenever you cast or attack with Lightning nearby.",
            "Сфера бури, бьющая врагов при использовании вами молниевых умений рядом."),
        new("Eye of Winter", GemCategory.ActiveSkill, "Int", 11, 0, 0, 100, "Spell, Cold, Projectile",
            "Сфера холоду, що випускає безліч крижаних уламків у польоті й посилює Critical Weakness.",
            "Travelling cold orb firing spiraling shards and stacking Critical Weakness.",
            "Летящая сфера холода, стреляющая осколками по спирали."),
        new("Flame Wall", GemCategory.ActiveSkill, "Int", 1, 0, 0, 100, "Spell, Fire, Duration",
            "Стіна вогню, крізь яку будь-які снаряди (Spark, Twister, Arrows) отримують додатковий Fire-урон.",
            "Firewall that adds flat Fire damage to every allied projectile passing through.",
            "Стена огня, добавляющая плоский урон огнём всем пролетающим сквозь неё снарядам."),
        new("Essence Drain", GemCategory.ActiveSkill, "Int", 3, 0, 0, 100, "Spell, Chaos, Projectile, DoT",
            "Періодичний хаос-урон із регенерацією та поширенням через Contagion.",
            "Chaos DoT projectile that spreads devastatingly when paired with Contagion.",
            "Снаряд периодического урона хаосом, распространяющийся через Contagion."),
        new("Sniper's Mark", GemCategory.ActiveSkill, "Dex", 3, 0, 0, 100, "Mark, Spell, Critical",
            "Позначає боса/рарку: наступний кріт завдає більше урону й дає Frenzy Charge.",
            "Marks an enemy so your next Critical Hit deals bonus damage and grants a Frenzy Charge.",
            "Метка на врага: следующий крит наносит больше урона и даёт Frenzy Charge."),
        new("Voltaic Mark", GemCategory.ActiveSkill, "Int/Dex", 7, 0, 0, 100, "Mark, Spell, Lightning",
            "Позначає ціль, різко піднімаючи шанс Shock і викликаючи вибух блискавки при смерті.",
            "Marks target for massively higher Shock effect and a lightning detonation on death.",
            "Метка, резко усиливающая эффект Shock и вызывающая взрыв молнии при смерти."),

        // --- Spirit Buffs / Heralds / Meta Triggers ---
        new("Herald of Ice", GemCategory.SpiritBuff, "Dex/Int", 4, 0, 30, 100, "Spirit, Cold, Herald, AoE",
            "Резервує 30 Spirit. Розриває заморожених ворогів (Shatter) крижаним вибухом по площі — ключ до швидкого кліру мап.",
            "30 Spirit. Shattering frozen enemies triggers AoE Cold explosions for massive map clear.",
            "30 Spirit. Раскалывание замороженных врагов вызывает массовые взрывы холода.",
            "Uncut Spirit Gem Lv 4+ (перший дається в Акте 1 — Freythorn, бос King in the Mists +30 Spirit).",
            "Uncut Spirit Gem Lv 4+ (first guaranteed in Act 1 Freythorn: King in the Mists quest).",
            "Uncut Spirit Gem Lv 4+ (первый даётся в Акте 1 — Freythorn, босс King in the Mists)."),
        new("Herald of Thunder", GemCategory.SpiritBuff, "Dex/Int", 4, 0, 30, 100, "Spirit, Lightning, Herald",
            "Резервує 30 Spirit. Вбивство шокованого ворога викликає удари небесних блискавок.",
            "30 Spirit. Killing Shocked targets unleashes automated sky lightning bolts.",
            "30 Spirit. Убийство врагов под Shock вызывает удары молний с неба.",
            "Uncut Spirit Gem Lv 4+ (Акт 1 Freythorn або ранні локації Акту 2).",
            "Uncut Spirit Gem Lv 4+ (Act 1 Freythorn or Act 2 drops).",
            "Uncut Spirit Gem Lv 4+ (Акт 1 Freythorn или дроп со 2-го Акта)."),
        new("Herald of Ash", GemCategory.SpiritBuff, "Str", 4, 0, 30, 100, "Spirit, Fire, Herald",
            "Резервує 30 Spirit. Надлишковий урон (Overkill) підпалює сусідніх ворогів.",
            "30 Spirit. Overkill damage ignites surrounding enemies in burning AoE.",
            "30 Spirit. Избыточный урон поджигает врагов вокруг."),
        new("Wind Dancer", GemCategory.SpiritBuff, "Dex", 4, 0, 30, 100, "Spirit, Evasion, Buff",
            "Резервує 30 Spirit. Накопичує стаки +More Evasion та випускає захисну вибухову хвилю при отриманні удару.",
            "30 Spirit. Builds stacking % more Evasion and blasts attackers away when struck.",
            "30 Spirit. Копит заряды усиления Evasion и отбрасывает врагов волной при ударе."),
        new("Ghost Dance", GemCategory.SpiritBuff, "Dex/Int", 4, 0, 30, 100, "Spirit, Energy Shield, Defence",
            "Резервує 30 Spirit. При отриманні удару миттєво відновлює Energy Shield пропорційно до вашого Evasion!",
            "30 Spirit. Consumes a Ghost Shroud when hit to instantly recover ES based on your Evasion.",
            "30 Spirit. При получении удара мгновенно восстанавливает Energy Shield от вашего Evasion."),
        new("Combat Frenzy", GemCategory.SpiritBuff, "Dex", 8, 0, 30, 100, "Spirit, Charge, Utility",
            "Резервує 30 Spirit. Дає Frenzy Charge при Freeze / Electrocute / Pin ворога.",
            "30 Spirit. Grants Frenzy Charges whenever you Freeze, Electrocute, or Pin enemies.",
            "30 Spirit. Даёт Frenzy Charge при Freeze, Electrocute или Pin врагов.",
            "Uncut Spirit Gem Lv 8+ (Акт 2–3, середина кампанії / Cruel / Waystones).",
            "Uncut Spirit Gem Lv 8+ (Act 2–3 campaign drop or Trade).",
            "Uncut Spirit Gem Lv 8+ (Акт 2–3 или карты)."),
        new("Trinity", GemCategory.SpiritBuff, "Dex/Int", 14, 0, 100, 100, "Spirit, Fire/Cold/Lightning, Damage",
            "Резервує 100 Spirit (пізній ендгейм)! Накопичує Resonance різних стихій для величезного % More Elemental Damage.",
            "100 Spirit (Endgame). Builds Elemental Resonance for massive % More Elemental Damage.",
            "100 Spirit (Эндгейм). Копит стихийный резонанс для огромного % More урона.",
            "Uncut Spirit Gem Lv 14+ (пізній Cruel / Акт 3 Ignagduk +30 Spirit / Waystones T1–T15).",
            "Uncut Spirit Gem Lv 14+ (End of Campaign / Waystones T1–T15).",
            "Uncut Spirit Gem Lv 14+ (конец кампании / Waystones T1–T15)."),
        new("Blink", GemCategory.SpiritBuff, "Int", 14, 0, 60, 100, "Spirit, Travel, Cooldown",
            "Резервує 60 Spirit. Перетворює звичайний Dodge Roll у миттєву телепортацію крізь перешкоди.",
            "60 Spirit. Replaces Dodge Roll with an instant Blink teleport across gaps.",
            "60 Spirit. Заменяет перекат на мгновенный телепорт Blink сквозь препятствия.",
            "Uncut Spirit Gem Lv 14+ (Waystones / Акт 3 Cruel).",
            "Uncut Spirit Gem Lv 14+ (Act 3 Cruel / Endgame Waystones).",
            "Uncut Spirit Gem Lv 14+ (Акт 3 Cruel / эндгейм-карты)."),
        new("Archmage", GemCategory.SpiritBuff, "Int", 14, 0, 100, 100, "Spirit, Spell, Lightning, Mana",
            "Резервує 100 Spirit. Додає величезний % урону блискавкою залежно від максимальної Мани.",
            "100 Spirit. Adds massive flat/extra Lightning Spell damage scaling from Max Mana.",
            "100 Spirit. Добавляет огромный урон молнией в зависимости от максимальной Маны."),
        new("Grim Feast", GemCategory.SpiritBuff, "Int", 8, 0, 30, 100, "Spirit, Energy Shield, Recovery",
            "Резервує 30 Spirit. Збирає енергію з трупів для оверкапу Energy Shield (до 2x).",
            "30 Spirit. Reaps Remnants to overflow Energy Shield up to double cap (verify 0.5 patch notes).",
            "30 Spirit. Восстанавливает и оверкапит Energy Shield с поверженных врагов."),
        new("Elemental Conflux", GemCategory.SpiritBuff, "Dex/Int", 14, 0, 60, 100, "Spirit, Elemental, Buff",
            "Резервує 60 Spirit. Циклічно дає потужні % More Fire / Cold / Lightning бонуси.",
            "60 Spirit. Cycles through More Fire, Cold, and Lightning damage phases.",
            "60 Spirit. Циклично даёт мощные % More бонусы огня, холода и молнии."),
        new("Cast on Freeze", GemCategory.MetaTrigger, "Int", 8, 0, 60, 100, "Spirit, Meta, Cold, Trigger",
            "Резервує 60 Spirit. Автоматично кастує вкладені чари щоразу при заморожуванні ворогів.",
            "60 Spirit. Meta gem that triggers socketed Spells whenever you Freeze enemies.",
            "60 Spirit. Автоматически применяет вставленные чары при заморозке врагов."),
        new("Cast on Critical", GemCategory.MetaTrigger, "Int", 14, 0, 100, 100, "Spirit, Meta, Crit, Trigger",
            "Резервує 100 Spirit. Автоматично запускає прив'язані чари після серії критичних ударів.",
            "100 Spirit. Meta gem that builds Energy on Critical Hits to auto-cast Spells.",
            "100 Spirit. Мета-камень: накапливает энергию от критов и авто-кастит чары."),
        new("Blasphemy", GemCategory.MetaTrigger, "Int", 8, 0, 60, 100, "Spirit, Meta, Curse, Aura",
            "Резервує 60+ Spirit за кожне прокляття. Перетворює Curse (Conductivity, Hypothermia, Temporal Chains) в ауру.",
            "60+ Spirit per Curse. Turns socketed Curses into a permanent surrounding aura.",
            "60+ Spirit за проклятие. Превращает проклятия в постоянную ауру вокруг героя."),
        new("Charge Regulation", GemCategory.SpiritBuff, "Dex/Int", 8, 0, 30, 100, "Spirit, Charge, Buff",
            "Резервує 30 Spirit. Активні Power/Frenzy/Endurance заряди дають % Crit, Attack/Cast Speed та Defences.",
            "30 Spirit. Grants Crit Chance, Speed, and Defences while holding Power/Frenzy/Endurance Charges.",
            "30 Spirit. Заряды дают доп. Крит, Скорость и Защиту."),

        // --- Popular Support Gems (PoE 2 Tiers I / II / III) ---
        new("Martial Tempo", GemCategory.Support, "Dex", 1, 1, 0, 100, "Support, Attack Speed",
            "+25% More Attack Speed для прив'язаної атаки. Must-have для майже всіх мілі/спіс/лук скілів.",
            "25% more Attack Speed for supported Attack skills. Essential DPS + feel support.",
            "+25% More к скорости атаки для привязанного умения."),
        new("Primal Armament", GemCategory.Support, "Dex", 1, 1, 0, 110, "Support, Elemental Attack",
            "+25% More Weapon Elemental Damage. Ключовий ранній саппорт для блискавки/холоду.",
            "25% more Weapon Elemental Damage for supported attacks.",
            "+25% More стихийного урона оружия для атак."),
        new("Creschendo", GemCategory.Support, "Dex", 2, 2, 0, 110, "Support, Combo",
            "Crescendo: прискорює завершальні удари комбо й посилює 4-й удар.",
            "Empowers combo finisher attacks and multi-stage strikes.",
            "Усиливает финальные комбо-атаки и комбо-удары."),
        new("Crescendo", GemCategory.Support, "Dex", 2, 2, 0, 110, "Support, Combo",
            "Дозволяє наносити фінішний комбо-удар двічі поспіль!",
            "Allows the final combo strike to repeat/trigger an extra time.",
            "Позволяет финальному комбо-удару сработать дважды."),
        new("Close Combat", GemCategory.Support, "Str", 1, 1, 0, 120, "Support, Melee, Damage",
            "До +25% More Melee Damage на ближній дистанції.",
            "Up to 25% more Melee damage to enemies at close range.",
            "До +25% More урона ближнего боя на близкой дистанции."),
        new("Concussive", GemCategory.Support, "Str", 2, 2, 0, 110, "Support, Stun, Melee",
            "Збільшує Stun Buildup та фізичний/ударний ефект.",
            "Increases Stun buildup and heavy impact power.",
            "Усиливает накопление Stun и ударный эффект."),
        new("Innervate", GemCategory.Support, "Int", 2, 2, 0, 120, "Support, Lightning, Buff",
            "Убивство шокованого ворога додає плоский Lightning-урон на кілька секунд.",
            "Killing Shocked foes grants temporary flat Lightning damage.",
            "Убийство врага под Shock временно даёт плоский урон молнией."),
        new("Lightning Infusion", GemCategory.Support, "Int", 1, 1, 0, 100, "Support, Lightning",
            "+25% додаткового урону блискавкою (трохи зменшує Fire/Cold).",
            "Gain 25% extra Lightning damage (trades a little Fire/Cold).",
            "+25% доп. урона молнией (слегка снижает огонь/холод)."),
        new("Cold Infusion", GemCategory.Support, "Dex", 1, 1, 0, 100, "Support, Cold",
            "+25% додаткового урону холодом (покращує Freeze).",
            "Gain 25% extra Cold damage for faster Freezing.",
            "+25% доп. урона холодом для быстрого наложения Freeze."),
        new("Ice Bite", GemCategory.Support, "Dex", 2, 2, 0, 120, "Support, Cold, Freeze",
            "Замороження ворогів додає +35% додаткового урону холодом на 3 секунди!",
            "Freezing enemies grants massive Extra Cold Damage for 3 seconds!",
            "Заморозка врагов даёт +35% доп. урона холодом на 3 секунды!"),
        new("Glaciation", GemCategory.Support, "Dex", 1, 1, 0, 110, "Support, Freeze",
            "+50% more Freeze Buildup — допомагає стабільно активувати Herald of Ice.",
            "50% more Freeze Buildup, enabling reliable Herald of Ice chains.",
            "+50% more к накоплению Freeze — помогает запускать Herald of Ice."),
        new("Frost Nexus", GemCategory.Support, "Int", 2, 2, 0, 120, "Support, Chilled Ground",
            "Створює ділянку Chilled Ground на ворогах або навколо точки влучання (ідеально під Twister!).",
            "Creates Chilled Ground on freeze/hit — amazing elemental feed for Twister.",
            "Создаёт Chilled Ground при попадании — идеально подпитывает Twister."),
        new("Magnified Effect", GemCategory.Support, "Int", 1, 1, 0, 120, "Support, AoE",
            "+30% More Area of Effect для прив'язаного вміння.",
            "30% more Area of Effect for massive screen coverage.",
            "+30% More области действия (AoE)."),
        new("Concentrated Effect", GemCategory.Support, "Int", 1, 1, 0, 120, "Support, AoE, Boss",
            "+30–40% More Area Damage ціною меншого радіуса — топ для соло-таргет босів!",
            "Significant More Area Damage at smaller radius — top single-target boss swap.",
            "+30–40% More урона по области за счёт меньшего радиуса — идеально под боссов."),
        new("Heft", GemCategory.Support, "Str", 1, 1, 0, 120, "Support, Physical",
            "+30% More Maximum Physical Hit Damage.",
            "30% more Maximum Physical Hit Damage for high-roll attacks.",
            "+30% More максимального физического урона ударом."),
        new("Rage", GemCategory.Support, "Str", 1, 1, 0, 100, "Support, Rage",
            "Генерує Rage при мілі/атаках (+1% More Attack Damage за кожну одиницю Rage!). Рівні I/II/III масштабують ліміт і приріст.",
            "Generates Rage on hit (+1% more Attack damage per Rage stack; Tier I/II/III increases rate & bonus).",
            "Копит Rage при ударах (+1% More урона атак за стак; уровни I/II/III усиливают бонус)."),
        new("Retreat", GemCategory.Support, "Dex", 1, 1, 0, 110, "Support, Blind/Evasion",
            "Полегшує мобільність / захист при використанні атак переміщення.",
            "Enhances defensive/disengage utility on movement skills.",
            "Повышает мобильность и защиту на умениях передвижения."),
        new("Prolonged Duration", GemCategory.Support, "Int", 1, 1, 0, 110, "Support, Duration",
            "+25%–40% More Skill Effect Duration (для Twister, Orb of Storms, Buffs). Тіри I / II / III.",
            "25–40% more Skill Effect Duration for longer Twisters, orbs, and buffs (Tier I/II/III).",
            "+25–40% More длительности эффекта (для Twister, сфер и баффов; уровни I/II/III)."),
        new("Precision", GemCategory.Support, "Dex", 1, 1, 10, 100, "Spirit Support, Accuracy",
            "+30% Accuracy Rating (резервує +10 Spirit при вставці у Spirit-баф!).",
            "+30% Accuracy Rating (reserves +10 extra Spirit when linked to a Spirit buff).",
            "+30% к меткости Accuracy (резервирует +10 Spirit в связке со Spirit-баффом)."),
        new("Clarity", GemCategory.Support, "Int", 1, 1, 10, 100, "Spirit Support, Mana Regen",
            "+30% Mana Regeneration Rate (резервує +10 Spirit у Spirit-бафі).",
            "+30% Mana Regeneration Rate (reserves +10 Spirit in a Spirit aura).",
            "+30% к регенерации маны (резервирует +10 Spirit в Spirit-баффе)."),
        new("Cannibalism", GemCategory.Support, "Str", 1, 1, 15, 100, "Spirit Support, Recovery",
            "Відновлює % Life/Mana при вбивстві (резервує +15 Spirit).",
            "Recovers Life on Kill (reserves +15 Spirit in a persistent buff).",
            "Восстанавливает здоровье при убийстве (резервирует +15 Spirit)."),
        new("Arcane Tempo", GemCategory.Support, "Int", 1, 1, 0, 100, "Support, Cast Speed",
            "+25% More Cast Speed для чар.",
            "25% more Cast Speed for Spell skills.",
            "+25% More скорости сотворения чар."),
        new("Controlled Destruction", GemCategory.Support, "Int", 1, 1, 0, 120, "Support, Spell",
            "+25–30% More Spell Damage без крітів.",
            "25–30% more Spell Damage (non-crit builds).",
            "+25–30% More урона чар (снижает шанс крита)."),
        new("Scattershot", GemCategory.Support, "Dex", 1, 1, 0, 130, "Support, Projectile",
            "+2 додаткові снаряди ціною швидкості атаки — чудово для покриття карти!",
            "Fires +2 extra projectiles at lower Attack/Cast speed.",
            "+2 дополнительных снаряда — отлично для зачистки."),
        new("Fork", GemCategory.Support, "Dex", 2, 2, 0, 120, "Support, Projectile",
            "Снаряди роздвоюються при влучанні.",
            "Projectiles fork into two upon striking an enemy.",
            "Снаряды раздваиваются при попадании."),
        new("Overcharge", GemCategory.Support, "Int", 2, 2, 0, 110, "Support, Shock",
            "+75% More Magnitude of Shock (довший та сильніший шок на босах).",
            "75% more Shock magnitude for massive boss damage amplification.",
            "+75% More силы эффекта Shock на боссах."),
        new("Profusion", GemCategory.Support, "Dex", 2, 2, 0, 120, "Support, Charges",
            "50% шанс отримати +1 додатковий заряд при отриманні заряду!",
            "50% chance to gain an additional Charge when generating Charges.",
            "50% шанс получить +1 дополнительный заряд при их генерации."),
        new("Perpetual Charge", GemCategory.Support, "Int/Dex", 3, 3, 0, 120, "Support, Charges",
            "Високий шанс НЕ витратити Power/Frenzy Charge при касті скіла!",
            "High chance to not consume Charges when triggering a charge-spending skill.",
            "Высокий шанс НЕ потратить Power/Frenzy Charge при активации."),
        new("Supercritical", GemCategory.Support, "Int", 3, 3, 0, 130, "Support, Critical",
            "+100% Increased Critical Damage Bonus ціною меншого базового шансу кріта.",
            "Huge Critical Damage Bonus boost for high-crit endgame setups.",
            "Огромный бонус к силе критического удара для эндгейма."),
        new("Ambush", GemCategory.Support, "Dex", 2, 2, 0, 120, "Support, Critical",
            "+100% More Critical Hit Chance проти ворогів із повним Life (ідеально для ваншоту паків).",
            "100% more Critical Hit Chance against enemies on Full Life.",
            "+100% More шанса крита по врагам с полным здоровьем."),
        new("Overpower", GemCategory.Support, "Str", 1, 1, 0, 110, "Support, Stun",
            "+50% More Stun Buildup — швидко оглушує еліток і босів.",
            "50% more Stun Buildup to heavy-stun rares and bosses quickly.",
            "+50% More накопления оглушения (Stun).")
    ];

    public static PoeGemInfo ResolveGem(string rawGemName, int? stageMinLevel = null)
    {
        string cleaned = (rawGemName ?? "").Trim();
        int gemLvl = stageMinLevel is > 0 ? stageMinLevel.Value : 1;
        int quality = 0;

        var mLvl = Regex.Match(cleaned, @"\(Lv(\d+)(?:\s*Q(\d+)%?)?\)", RegexOptions.IgnoreCase);
        if (mLvl.Success)
        {
            if (int.TryParse(mLvl.Groups[1].Value, out int parsedL)) gemLvl = parsedL;
            if (mLvl.Groups[2].Success && int.TryParse(mLvl.Groups[2].Value, out int parsedQ)) quality = parsedQ;
            cleaned = Regex.Replace(cleaned, @"\s*\(Lv\d+[^)]*\)", "").Trim();
        }

        // Розпізнаємо римські тіри наприкінці назви ("Rage I", "Prolonged Duration II", "Rage III")
        int explicitRomanTier = 0;
        var mRoman = Regex.Match(cleaned, @"^(.+?)\s+(I|II|III)$", RegexOptions.IgnoreCase);
        string baseLookup = cleaned;
        if (mRoman.Success)
        {
            baseLookup = mRoman.Groups[1].Value.Trim();
            explicitRomanTier = mRoman.Groups[2].Value.ToUpperInvariant() switch
            {
                "III" => 3,
                "II" => 2,
                _ => 1
            };
        }

        var match = Catalog.FirstOrDefault(g =>
            g.CanonicalName.Equals(cleaned, StringComparison.OrdinalIgnoreCase) ||
            g.CanonicalName.Equals(baseLookup, StringComparison.OrdinalIgnoreCase))
            ?? Catalog.FirstOrDefault(g =>
                cleaned.Contains(g.CanonicalName, StringComparison.OrdinalIgnoreCase) ||
                g.CanonicalName.Contains(baseLookup, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            int supportTier = explicitRomanTier > 0 ? explicitRomanTier : match.SupportTier;
            if (match.Category == GemCategory.Support)
            {
                int minCharLvl = supportTier switch { 3 => 40, 2 => 20, _ => 1 };
                string obtainUk = match.CustomObtainUk ?? SupportObtainUk(supportTier);
                string obtainEn = match.CustomObtainEn ?? SupportObtainEn(supportTier);
                string obtainRu = match.CustomObtainRu ?? SupportObtainRu(supportTier);
                int reqAttr = Math.Max(0, (int)Math.Round(minCharLvl * 1.4));
                return new PoeGemInfo(
                    cleaned, GemCategory.Support, match.Attribute,
                    gemLvl, quality, minCharLvl,
                    match.Attribute.Contains("Str") ? reqAttr : 0,
                    match.Attribute.Contains("Dex") ? reqAttr : 0,
                    match.Attribute.Contains("Int") ? reqAttr : 0,
                    $"Uncut Support Gem Tier {supportTier}",
                    match.SpiritCost, match.ManaMultPct, match.Tags,
                    obtainUk, obtainEn, obtainRu,
                    match.DescUk, match.DescEn, match.DescRu
                );
            }

            int reqMin = Math.Clamp(match.MinUncutLevel, 1, 20);
            int minLvl = (match.Category is GemCategory.SpiritBuff or GemCategory.MetaTrigger)
                ? reqMin switch { <= 4 => 10, <= 8 => 22, <= 10 => 36, <= 14 => 52, _ => 65 }
                : SkillUncutCharLevel[reqMin];

            string uncutLabel = match.Category is GemCategory.SpiritBuff or GemCategory.MetaTrigger
                ? $"Uncut Spirit Gem Lv {reqMin}"
                : $"Uncut Skill Gem Lv {reqMin}";

            int attrVal = Math.Max(10, (int)Math.Round(minLvl * 1.6));
            bool hybrid = match.Attribute.Contains('/');
            int perAttr = hybrid ? Math.Max(8, (int)Math.Round(attrVal * 0.65)) : attrVal;

            return new PoeGemInfo(
                cleaned, match.Category, match.Attribute,
                Math.Max(gemLvl, reqMin), quality, minLvl,
                match.Attribute.Contains("Str") ? perAttr : 0,
                match.Attribute.Contains("Dex") ? perAttr : 0,
                match.Attribute.Contains("Int") ? perAttr : 0,
                uncutLabel,
                match.SpiritCost, match.ManaMultPct, match.Tags,
                match.CustomObtainUk ?? UncutSkillObtainUk(reqMin, match.Category),
                match.CustomObtainEn ?? UncutSkillObtainEn(reqMin, match.Category),
                match.CustomObtainRu ?? UncutSkillObtainRu(reqMin, match.Category),
                match.DescUk, match.DescEn, match.DescRu
            );
        }

        // Розумний fallback для будь-якого нового/рідкісного гема PoE 2
        bool looksLikeSupport = explicitRomanTier > 0 ||
            cleaned.Contains("support", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" Infusion", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" Effect", StringComparison.OrdinalIgnoreCase);

        if (looksLikeSupport)
        {
            int tier = explicitRomanTier > 0 ? explicitRomanTier : (gemLvl >= 12 ? 3 : gemLvl >= 6 ? 2 : 1);
            int charLvl = tier switch { 3 => 40, 2 => 20, _ => 1 };
            return new PoeGemInfo(
                cleaned, GemCategory.Support, "Hybrid",
                gemLvl, quality, charLvl, 0, 0, 0,
                $"Uncut Support Gem Tier {tier}",
                0, 120, "Support",
                SupportObtainUk(tier), SupportObtainEn(tier), SupportObtainRu(tier),
                "Камінь підтримки PoE 2. Натисни «Детальніше з AI», якщо потрібен точний розбір синергії під білд.",
                "PoE 2 Support Gem. Click 'Ask AI Details' for exact build synergy math.",
                "Камень поддержки PoE 2. Нажми «Подробнее с AI», если нужен точный разбор синергии."
            );
        }

        int uncutTier = Math.Clamp(gemLvl > 1 ? Math.Min(20, gemLvl) : (stageMinLevel is int s ? CharLevelToUncutTier(s) : 5), 1, 20);
        int unlockChar = SkillUncutCharLevel[uncutTier];
        return new PoeGemInfo(
            cleaned, GemCategory.ActiveSkill, "Hybrid",
            Math.Max(gemLvl, uncutTier), quality, unlockChar, 0, 0, 0,
            $"Uncut Skill Gem Lv {uncutTier}",
            0, 100, "Skill",
            UncutSkillObtainUk(uncutTier, GemCategory.ActiveSkill),
            UncutSkillObtainEn(uncutTier, GemCategory.ActiveSkill),
            UncutSkillObtainRu(uncutTier, GemCategory.ActiveSkill),
            $"Активний камінь умінь PoE 2 (відкривається через Uncut Skill Gem Lv {uncutTier}+ орієнтовно з {unlockChar} рівня).",
            $"PoE 2 Active Skill Gem (engraved from Uncut Skill Gem Lv {uncutTier}+, ~Char Lvl {unlockChar}).",
            $"Активный камень умений PoE 2 (вырезается из Uncut Skill Gem Lv {uncutTier}+ примерно с {unlockChar} уровня)."
        );
    }

    public static List<PoeGemInfo> GetAllCatalogEntries(string search = "", GemCategory? categoryFilter = null)
    {
        string q = (search ?? "").Trim();
        return Catalog
            .Where(c => categoryFilter == null || c.Category == categoryFilter.Value)
            .Where(c => string.IsNullOrEmpty(q) ||
                        c.CanonicalName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.Tags.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.DescUk.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.DescEn.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(c => ResolveGem(c.CanonicalName))
            .DistinctBy(g => g.Name)
            .OrderBy(g => g.MinUnlockCharacterLevel)
            .ThenBy(g => g.Name)
            .ToList();
    }

    public static int CharLevelToUncutTier(int charLvl)
    {
        for (int t = 20; t >= 1; t--)
            if (charLvl >= SkillUncutCharLevel[t]) return t;
        return 1;
    }

    private static string SupportObtainUk(int tier) => tier switch
    {
        3 => "Uncut Support Gem Tier 3: падає з ~40 рівня (кінець Акту 3, Cruel Акти та мапи Waystones) або Trade.",
        2 => "Uncut Support Gem Tier 2: падає з ~20 рівня (Акт 2, Deshar / Valley of the Titans та далі).",
        _ => "Uncut Support Gem Tier 1: з 1 рівня (Акт 1 квести Clearfell / The Rust King, дроп із мобів і скринь)."
    };

    private static string SupportObtainEn(int tier) => tier switch
    {
        3 => "Uncut Support Gem Tier 3: drops from ~Lvl 40 (late Act 3, Cruel Acts, Endgame Waystones).",
        2 => "Uncut Support Gem Tier 2: drops from ~Lvl 20 (Act 2 campaign zones & bosses).",
        _ => "Uncut Support Gem Tier 1: available from Lvl 1 (Act 1 early quests, drops everywhere)."
    };

    private static string SupportObtainRu(int tier) => tier switch
    {
        3 => "Uncut Support Gem Tier 3: падает с ~40 уровня (конец 3-го Акта, Cruel и карты Waystones).",
        2 => "Uncut Support Gem Tier 2: падает с ~20 уровня (Акт 2 и далее).",
        _ => "Uncut Support Gem Tier 1: доступен с 1 уровня (квесты и дроп в 1-м Акте)."
    };

    private static string UncutSkillObtainUk(int tier, GemCategory cat)
    {
        if (cat is GemCategory.SpiritBuff or GemCategory.MetaTrigger)
            return $"Uncut Spirit Gem Lv {tier}: бос King in the Mists (Акт 1 Freythorn) або рідкісний дроп по кампанії та мапах Waystones.";

        return tier switch
        {
            <= 4 => $"Uncut Skill Gem Lv {tier}: Акт 1 (квести Clearfell / Mud Burrow / Cemeteries, зони 1–14 рівня).",
            <= 8 => $"Uncut Skill Gem Lv {tier}: Акт 2 (Vastiri Outskirts → Deshar, зони 16–30 рівня).",
            <= 12 => $"Uncut Skill Gem Lv {tier}: Акт 3 (Sandswept Marsh → Utzaal, зони 32–48 рівня).",
            <= 15 => $"Uncut Skill Gem Lv {tier}: фінал кампанії / ранні Waystones T1–T5 (50–68 рів.).",
            <= 18 => $"Uncut Skill Gem Lv {tier}: Waystones T6–T15 (рівень монстрів 70–79).",
            _ => $"Uncut Skill Gem Lv {tier}: рідкісний ендгейм-дроп (Waystones T15+ / Corrupted T16+ / Pinnacle-боси) або PoE 2 Trade."
        };
    }

    private static string UncutSkillObtainEn(int tier, GemCategory cat)
    {
        if (cat is GemCategory.SpiritBuff or GemCategory.MetaTrigger)
            return $"Uncut Spirit Gem Lv {tier}: Act 1 Freythorn (King in the Mists) or campaign/Waystone drops.";

        return tier switch
        {
            <= 4 => $"Uncut Skill Gem Lv {tier}: Act 1 quests & zones (Monster Lvl 1–14).",
            <= 8 => $"Uncut Skill Gem Lv {tier}: Act 2 zones (Monster Lvl 16–30).",
            <= 12 => $"Uncut Skill Gem Lv {tier}: Act 3 zones (Monster Lvl 32–48).",
            <= 15 => $"Uncut Skill Gem Lv {tier}: Late Campaign / Early Waystones T1–T5 (Lvl 50–68).",
            <= 18 => $"Uncut Skill Gem Lv {tier}: Mid–High Waystones T6–T15 (Area Lvl 70–79).",
            _ => $"Uncut Skill Gem Lv {tier}: Rare T15+ / Corrupted / Pinnacle Boss drop or PoE 2 Trade."
        };
    }

    private static string UncutSkillObtainRu(int tier, GemCategory cat)
    {
        if (cat is GemCategory.SpiritBuff or GemCategory.MetaTrigger)
            return $"Uncut Spirit Gem Lv {tier}: Акт 1 Freythorn (King in the Mists) или дроп на картах Waystones.";

        return tier switch
        {
            <= 4 => $"Uncut Skill Gem Lv {tier}: Акт 1 (квесты и зоны 1–14 ур.).",
            <= 8 => $"Uncut Skill Gem Lv {tier}: Акт 2 (зоны 16–30 ур.).",
            <= 12 => $"Uncut Skill Gem Lv {tier}: Акт 3 (зоны 32–48 ур.).",
            <= 15 => $"Uncut Skill Gem Lv {tier}: финал кампании / ранние карты Waystones T1–T5 (50–68 ур.).",
            <= 18 => $"Uncut Skill Gem Lv {tier}: Waystones T6–T15 (70–79 ур.).",
            _ => $"Uncut Skill Gem Lv {tier}: редкий дроп T15+ / Pinnacle-боссы или PoE 2 Trade."
        };
    }
}
