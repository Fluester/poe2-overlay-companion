using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// База зон кампанії та ендгейму Path of Exile 2 (аналог Campaign Guide у PoE Overlay 2):
/// автоматично підказує босів, постійні нагороди (+Spirit, +Пасивки, +Резисти) та тактику для поточної зони.
/// </summary>
public static class CampaignGuideDb
{
    private static readonly List<CampaignZoneTip> Zones =
    [
        new()
        {
            ZonePattern = "clearfell",
            ActBadge = "Act 1",
            BossName = "Beira of the Rotten Pack",
            RewardUk = "+10% Cold Resistance (назавжди)",
            RewardEn = "+10% Cold Resistance (permanent)",
            RewardRu = "+10% Cold Resistance (навсегда)",
            TipUk = "Знайди Beira біля північного вогнища та розбий холодну зону.",
            TipEn = "Clear Beira in northern camp for +10% Cold Resistance.",
            TipRu = "Найди Beira у северного костра ради +10% Cold Resistance."
        },
        new()
        {
            ZonePattern = "mud burrow",
            ActBadge = "Act 1",
            BossName = "The Devourer",
            RewardUk = "Квестовий прохід + Uncut Skill Gem",
            RewardEn = "Quest progress + Uncut Skill Gem",
            RewardRu = "Квестовый прогресс + Uncut Skill Gem",
            TipUk = "Ухиляйся від отруйного шлейфу та копання Devourer.",
            TipEn = "Dodge the burrow slam and poison projectiles.",
            TipRu = "Уклоняйся от выныривания и ядовитых атак Devourer."
        },
        new()
        {
            ZonePattern = "grelwood",
            ActBadge = "Act 1",
            BossName = "The Brambleghast / Tree of Souls",
            RewardUk = "Uncut Support Gem + доступ до Grim Tangle",
            RewardEn = "Uncut Support Gem + Grim Tangle access",
            RewardRu = "Uncut Support Gem + доступ к Grim Tangle",
            TipUk = "Збери руни навколо Tree of Souls та вбий Brambleghast.",
            TipEn = "Interact with Tree of Souls runes and check optional Brambleghast.",
            TipRu = "Активируй руны у Tree of Souls и убей Brambleghast."
        },
        new()
        {
            ZonePattern = "red vale",
            ActBadge = "Act 1",
            BossName = "The Rust King",
            RewardUk = "3 Runed Seals → зброя/артефакти",
            RewardEn = "3 Runed Seals quest",
            RewardRu = "Квест на 3 Runed Seals",
            TipUk = "Знайди 3 обеліски по краях мапи; обережно — фізичні вибухи клинків.",
            TipEn = "Activate 3 obelisks around edges; watch for Rust King's blade barrage.",
            TipRu = "Активируй 3 обелиска и берегись летающих клинков Rust King."
        },
        new()
        {
            ZonePattern = "hunting grounds",
            ActBadge = "Act 1",
            BossName = "The Crowbell",
            RewardUk = "+2 Weapon Set Passive Skill Points (книга!)",
            RewardEn = "+2 Weapon Set Passive Skill Points (Book)",
            RewardRu = "+2 очка пассивок (книга)",
            TipUk = "Обов'язковий бос! Знайди арену Crowbell по дорозі до Ogham Farmlands.",
            TipEn = "Mandatory boss! Grab Book of Specialisation (+2 passive pts) from Crowbell.",
            TipRu = "Обязательный босс: убей Crowbell ради книги на +2 пассивки."
        },
        new()
        {
            ZonePattern = "freythorn",
            ActBadge = "Act 1",
            BossName = "The King in the Mists",
            RewardUk = "+30 Spirit (назавжди!)",
            RewardEn = "+30 Spirit (Gembloom Skull — permanent)",
            RewardRu = "+30 Spirit (навсегда)",
            TipUk = "Активуй вівтарі Ritual, вбий King in the Mists та використай Gembloom Skull в інвентарі.",
            TipEn = "Complete Ritual altars -> defeat King in the Mists -> click Gembloom Skull for +30 Spirit.",
            TipRu = "Пройди алтари Ритуала -> убей King in the Mists -> нажми Gembloom Skull ради +30 Spirit."
        },
        new()
        {
            ZonePattern = "farmlands",
            ActBadge = "Act 1",
            BossName = "Una's Lute",
            RewardUk = "+2 Weapon Set Passive Skill Points",
            RewardEn = "+2 Weapon Set Passive Skill Points (Una's Lute)",
            RewardRu = "+2 очка пассивок (Una's Lute)",
            TipUk = "Знайди хатину з Una's Lute та здай у таборі в обмін на книгу пасивок.",
            TipEn = "Pick up Una's Lute in the ruined hut and turn it in to Una.",
            TipRu = "Подбери Una's Lute в разрушенном домике и отнеси Una ради +2 пассивок."
        },
        new()
        {
            ZonePattern = "ogham village",
            ActBadge = "Act 1",
            BossName = "The Executioner",
            RewardUk = "Розблокування Salvage Bench (Smithing Tools)",
            RewardEn = "Unlocks Salvage Bench (Smithing Tools)",
            RewardRu = "Открывает Salvage Bench (Smithing Tools)",
            TipUk = "Перевір будиночок біля кузні на Smithing Tools перед/після Executioner.",
            TipEn = "Grab Renly's Tools in the village to unlock Salvage Bench in town.",
            TipRu = "Забери инструменты кузнеца в деревне для разблокировки Salvage Bench."
        },
        new()
        {
            ZonePattern = "ogham manor",
            ActBadge = "Act 1 Boss",
            BossName = "Count Geonor",
            RewardUk = "Фінал Акту 1 → перехід до Vastiri",
            RewardEn = "Act 1 Final Boss → Vastiri Desert",
            RewardRu = "Финал Акта 1 → переход в Акт 2",
            TipUk = "Надругий резист до холоду (Cold Res) та бігай по колу під час туманної фази вовків.",
            TipEn = "Stack Cold Resistance; kite around perimeter during fog/wolf phase.",
            TipRu = "Подними Cold Resistance и бегай по кругу во время фазы тумана с волками."
        },
        new()
        {
            ZonePattern = "traitors' passage",
            ActBadge = "Act 2",
            BossName = "Balbala, the Traitor",
            RewardUk = "Djinn Barya → Trial of the Sekhemas (1st Ascendancy!)",
            RewardEn = "Djinn Barya → Trial of the Sekhemas (1st Ascendancy)",
            RewardRu = "Djinn Barya → Испытание Секем (1-й Асценданси)",
            TipUk = "У кімнаті з символами вбий Balbala для розблокування Trial of the Sekhemas.",
            TipEn = "Defeat Balbala in the side seal room to unlock Trial of the Sekhemas & Ascendancy.",
            TipRu = "Убей Balbala в боковой комнате за печатью, чтобы открыть Trial of the Sekhemas."
        },
        new()
        {
            ZonePattern = "keth",
            ActBadge = "Act 2",
            BossName = "Kabala, Constrictor Queen",
            RewardUk = "+2 Weapon Set Passive Skill Points",
            RewardEn = "+2 Weapon Set Passive Skill Points",
            RewardRu = "+2 очка пассивок",
            TipUk = "Шукай арену Kabala в Keth і вбий її заради +2 пасивок.",
            TipEn = "Kill Kabala in Keth pit arena for Book of Specialisation (+2 pts).",
            TipRu = "Найди яму Kabala в Keth ради книги на +2 пассивки."
        },
        new()
        {
            ZonePattern = "valley of the titans",
            ActBadge = "Act 2",
            BossName = "Relic Altar (Ancient Vows)",
            RewardUk = "Постійний баф: +30% Charm Charges або +15% Mana Flask Recovery",
            RewardEn = "Permanent Altar Choice: +30% Charm Charges or +15% Mana Recovery",
            RewardRu = "Выбор баффа алтаря: +30% Charm Charges или +15% Mana Recovery",
            TipUk = "Збери Sun & Kabala Clan Relics (у Keth та Bone Pits) і встав у Вівтар в Valley of the Titans.",
            TipEn = "Socket both Clan Relics into the Altar near waypoint; choice can be swapped anytime.",
            TipRu = "Вставь обе реликвии кланов в алтарь у вейпоинта (выбор можно менять позже)."
        },
        new()
        {
            ZonePattern = "deshar",
            ActBadge = "Act 2",
            BossName = "Final Letter + Sisters of Garukhan",
            RewardUk = "+2 Пасивки (Deshar) +10% Lightning Res (Spires)",
            RewardEn = "+2 Passives (Final Letter) & +10% Lightning Res (Spires shrine)",
            RewardRu = "+2 очка пассивок и +10% Lightning Res (Spires)",
            TipUk = "У Deshar обшукай тіло Fallen Dekhara (Final Letter). У Spires — знайди вівтар Sisters of Garukhan.",
            TipEn = "Click Fallen Dekhara body in Deshar for +2 pts; activate Sisters shrine in Spires for +10% Light Res.",
            TipRu = "Обыщи Fallen Dekhara в Deshar (+2 пассивки), а в Spires найди алтарь (+10% Light Res)."
        },
        new()
        {
            ZonePattern = "dreadnought",
            ActBadge = "Act 2 Boss",
            BossName = "Jamanra, the Abomination",
            RewardUk = "Фінал Акту 2 → Act 3 (Ziggurat)",
            RewardEn = "Act 2 Final Boss → Act 3",
            RewardRu = "Финал Акта 2 → Акт 3",
            TipUk = "Візьми максимум Lightning Resistance! Ховайся за щитом Азали під час піщаної бурі.",
            TipEn = "Cap Lightning Resistance! Stay inside Asala's wind dome during sandstorm.",
            TipRu = "Максимизируй Lightning Res и прячься под щитом Асалы во время песчаной бури."
        },
        new()
        {
            ZonePattern = "jungle ruins",
            ActBadge = "Act 3",
            BossName = "Mighty Silverfist",
            RewardUk = "+2 Weapon Set Passive Skill Points",
            RewardEn = "+2 Weapon Set Passive Skill Points",
            RewardRu = "+2 очка пассивок",
            TipUk = "Величезна горила б'є колоною (ваншот) — катайся їй за спину!",
            TipEn = "Dodge roll behind Mighty Silverfist when he raises the stone pillar slam!",
            TipRu = "Перекатывайся за спину горилле при замахе колонной (опасный ваншот)."
        },
        new()
        {
            ZonePattern = "venom crypts",
            ActBadge = "Act 3",
            BossName = "Venom Vial Choice (Servi)",
            RewardUk = "+25% Stun Thresh / +30% Ailment Thresh / +25% Mana Regen",
            RewardEn = "Choose +25% Stun Threshold, +30% Ailment Threshold, or +25% Mana Regen",
            RewardRu = "Выбор зелья Servi: +25% Stun / +30% Ailment / +25% Mana Regen",
            TipUk = "Знайди Corpse-wagon Venom Vial у Venom Crypts та віднеси Servi в місті.",
            TipEn = "Loot Venom Vial in the crypts and deliver to Servi for a permanent character buff.",
            TipRu = "Забери Venom Vial в крипте и выбери постоянный бонус у Servi."
        },
        new()
        {
            ZonePattern = "azak bog",
            ActBadge = "Act 3",
            BossName = "Ignagduk, the Bog Witch",
            RewardUk = "+30 Spirit (Gembloom / Bog Witch Skull)",
            RewardEn = "+30 Spirit (permanent) + Infernoclasp",
            RewardRu = "+30 Spirit (навсегда)",
            TipUk = "Тримай високий Fire Resistance. Нагорода — ще +30 Spirit!",
            TipEn = "Prepare Fire Resistance; Ignagduk grants +30 permanent Spirit.",
            TipRu = "Нужен Fire Resistance; с ведьмы падает предмет на +30 Spirit."
        },
        new()
        {
            ZonePattern = "jiquani",
            ActBadge = "Act 3",
            BossName = "Blackjaw, the Remnant",
            RewardUk = "+10% Fire Resistance (назавжди)",
            RewardEn = "+10% Fire Resistance (permanent)",
            RewardRu = "+10% Fire Resistance (навсегда)",
            TipUk = "Вбий Blackjaw у Jiquani's Machinarium заради +10% Fire Res (використай Soul Core на генераторах).",
            TipEn = "Slay Blackjaw in Jiquani's Machinarium for permanent +10% Fire Resistance.",
            TipRu = "Убей Blackjaw в Jiquani's Machinarium ради +10% Fire Resistance."
        },
        new()
        {
            ZonePattern = "sekhemas",
            ActBadge = "Ascendancy",
            BossName = "Trial of the Sekhemas",
            RewardUk = "Ascendancy Points + Relic Drops + Time-Lost Jewels",
            RewardEn = "Ascendancy Points & Relics (Honour System)",
            RewardRu = "Очки Восхождения и Реликвии (Honour)",
            TipUk = "Одягни реліквії на Honour Resistance (до 75%) та уникай кімнат із пастками без потреби.",
            TipEn = "Equip Honour Resistance relics up to 75%; prioritize Sacred Water & Merchant rooms.",
            TipRu = "Поставь реликвии на Honour Resistance до 75% и собирай Sacred Water."
        },
        new()
        {
            ZonePattern = "trial of chaos",
            ActBadge = "Ascendancy",
            BossName = "The Trialmaster (Ultimatum)",
            RewardUk = "Ascendancy Points + Soul Cores + Corrupted Uniques",
            RewardEn = "Ascendancy Points + Soul Cores",
            RewardRu = "Очки Восхождения + Soul Cores",
            TipUk = "Ніколи не бери руйнівні моди (Stalking Ruin, Blood Globules, Reduced Defences), якщо слабкий реген.",
            TipEn = "Avoid Stalking Ruin & escalating damage modifiers; Soul Cores sell well on Currency Exchange.",
            TipRu = "Избегай модификаторов Stalking Ruin и потери защит."
        },
        new()
        {
            ZonePattern = "ziggurat",
            ActBadge = "Town / Atlas",
            BossName = "Ziggurat Refuge / Encampment",
            RewardUk = "Map Device + Reforging Bench + Doryani",
            RewardEn = "Safe Town Hub & Atlas Map Device",
            RewardRu = "Городской хаб и Map Device Атласа",
            TipUk = "Тут ти в безпеці: перевір вендорів, скопіюй Regex торговця або згенеруй .filter.",
            TipEn = "Safe zone: check vendors with Vendor Regex, salvage rares, and stash valuable drops.",
            TipRu = "Безопасная зона: проверь торговцев через Vendor Regex и разбери ненужное."
        },
        new()
        {
            ZonePattern = "hideout",
            ActBadge = "Hideout",
            BossName = "Hideout",
            RewardUk = "Крафт, Map Device, трейд-кімната",
            RewardEn = "Crafting, Mapping & Trading Hub",
            RewardRu = "Крафт, Карты и Торговля",
            TipUk = "Ти в Хайдауті: F8 відкриває швидкі команди (/remaining, @last thx), Ctrl+C миттєво оцінює речі.",
            TipEn = "In Hideout: press F8 for quick chat commands or Ctrl+C items for instant Better/Worse + price.",
            TipRu = "В убежище: F8 открывает быстрые команды, Ctrl+C оценивает предметы и показывает цену."
        }
    ];

    public sealed record CampaignMilestone(string Id, string Act, string TitleUk, string TitleEn, string TitleRu);

    public static readonly IReadOnlyList<CampaignMilestone> PermanentMilestones =
    [
        new("a1_beira", "Act 1", "Clearfell · Beira (+10% Cold Res)", "Clearfell · Beira (+10% Cold Res)", "Clearfell · Beira (+10% Cold Res)"),
        new("a1_crowbell", "Act 1", "Hunting Grounds · Crowbell (+2 Пасивки)", "Hunting Grounds · Crowbell (+2 Passives)", "Hunting Grounds · Crowbell (+2 Пассивки)"),
        new("a1_freythorn", "Act 1", "Freythorn · King in the Mists (+30 Spirit)", "Freythorn · King in the Mists (+30 Spirit)", "Freythorn · King in the Mists (+30 Spirit)"),
        new("a1_lute", "Act 1", "Ogham Farmlands · Una's Lute (+2 Пасивки)", "Ogham Farmlands · Una's Lute (+2 Passives)", "Ogham Farmlands · Una's Lute (+2 Пассивки)"),
        new("a2_sekhema", "Act 2", "Traitors' Passage · Balbala (1st Ascendancy)", "Traitors' Passage · Balbala (1st Ascendancy)", "Traitors' Passage · Balbala (1st Ascendancy)"),
        new("a2_kabala", "Act 2", "Keth · Kabala (+2 Пасивки)", "Keth · Kabala (+2 Passives)", "Keth · Kabala (+2 Пассивки)"),
        new("a2_titans", "Act 2", "Valley of the Titans · Relic Altar (Баф)", "Valley of the Titans · Relic Altar (Buff)", "Valley of the Titans · Relic Altar (Бафф)"),
        new("a2_deshar", "Act 2", "Deshar · Final Letter (+2 Пасивки)", "Deshar · Final Letter (+2 Passives)", "Deshar · Final Letter (+2 Пассивки)"),
        new("a2_spires", "Act 2", "Spires of Deshar · Sisters (+10% Light Res)", "Spires of Deshar · Sisters (+10% Light Res)", "Spires of Deshar · Sisters (+10% Light Res)"),
        new("a3_gorilla", "Act 3", "Jungle Ruins · Silverfist (+2 Пасивки)", "Jungle Ruins · Silverfist (+2 Passives)", "Jungle Ruins · Silverfist (+2 Пассивки)"),
        new("a3_venom", "Act 3", "Venom Crypts · Servi Vial (+Stun/Mana)", "Venom Crypts · Servi Vial (+Stun/Mana)", "Venom Crypts · Servi Vial (+Stun/Mana)"),
        new("a3_azak", "Act 3", "Azak Bog · Ignagduk (+30 Spirit)", "Azak Bog · Ignagduk (+30 Spirit)", "Azak Bog · Ignagduk (+30 Spirit)"),
        new("a3_jiquani", "Act 3", "Jiquani · Blackjaw (+10% Fire Res)", "Jiquani · Blackjaw (+10% Fire Res)", "Jiquani · Blackjaw (+10% Fire Res)")
    ];

    public static CampaignZoneTip? Resolve(string? zoneName)
    {
        if (string.IsNullOrWhiteSpace(zoneName)) return null;
        string lower = zoneName.ToLowerInvariant();
        foreach (var z in Zones)
        {
            if (lower.Contains(z.ZonePattern, StringComparison.Ordinal))
                return z;
        }
        return null;
    }

    public static string FormatBannerText(string zoneName)
    {
        var tip = Resolve(zoneName);
        if (tip == null)
            return $"{Loc.T("Зона:", "Zone:", "Зона:")} {zoneName}";

        string reward = Loc.Current switch
        {
            AppLanguage.En => tip.RewardEn,
            AppLanguage.Ru => tip.RewardRu,
            _ => tip.RewardUk
        };
        string guide = Loc.Current switch
        {
            AppLanguage.En => tip.TipEn,
            AppLanguage.Ru => tip.TipRu,
            _ => tip.TipUk
        };
        return $"[{tip.ActBadge}] {zoneName} · {tip.BossName} → {reward}\n{guide}";
    }
}
