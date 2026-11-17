using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Повна база каменів умінь, підтримки (Tier I–V + Lineage) та каменів Духу (Spirit) Path of Exile 2 (0.5):
/// точні дані з PoE2DB (тіри, вимоги рівня, атрибутів, Spirit, множник вартості, джерела в Актах 1–4 / Interludes).
/// </summary>
public static partial class Poe2GemsDb
{
    private sealed class RawGemEntry
    {
        public string n { get; set; } = "";
        public string k { get; set; } = "A";
        public string a { get; set; } = "Hybrid";
        public int t { get; set; } = 1;
        public int sp { get; set; }
        public int mm { get; set; } = 100;
        public string g { get; set; } = "";
        public string d { get; set; } = "";
    }

    private sealed record LocalizedOverlay(
        string DescUk,
        string DescEn,
        string DescRu,
        string? CustomObtainUk = null,
        string? CustomObtainEn = null,
        string? CustomObtainRu = null);

    private sealed class GemCatalogIndex
    {
        public required List<RawGemEntry> All { get; init; }
        public required Dictionary<string, RawGemEntry> ByNormalized { get; init; }
    }

    // Точна таблиця PoE 2 (0.5, PoE2DB): рівень каменю / Uncut Tier (1..20) -> мін. рівень персонажа
    private static readonly int[] SkillUncutCharLevel =
    [
        1, 1, 3, 6, 10, 14, 18, 22, 26, 31, 36, 41, 46, 52, 58, 64, 66, 72, 78, 84, 90
    ];

    // Точні вимоги одного атрибуту (Str / Dex / Int) для Gem Level 1..20 з PoE2DB
    private static readonly int[] SingleAttrByGemLevel =
    [
        4, 4, 9, 14, 21, 28, 35, 41, 48, 57, 65, 74, 82, 92, 103, 113, 116, 126, 137, 147, 157
    ];

    // Точні вимоги гібридного атрибуту (напр. Dex/Int) для Gem Level 1..20 з PoE2DB
    private static readonly int[] HybridAttrByGemLevel =
    [
        4, 4, 7, 9, 13, 17, 20, 24, 28, 32, 37, 41, 46, 51, 57, 62, 64, 70, 75, 80, 86
    ];

    // Аліаси перейменованих або Mobalytics/PoB-варіантів назв у PoE 2 (0.1–0.5)
    private static readonly Dictionary<string, string> GemAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["martial tempo"] = "Rapid Attacks I",
        ["martial tempo i"] = "Rapid Attacks I",
        ["martial tempo ii"] = "Rapid Attacks II",
        ["martial tempo iii"] = "Rapid Attacks III",
        ["primal armament"] = "Elemental Armament I",
        ["primal armament i"] = "Elemental Armament I",
        ["primal armament ii"] = "Elemental Armament II",
        ["primal armament iii"] = "Elemental Armament III",
        ["magnified effect"] = "Magnified Area I",
        ["magnified effect i"] = "Magnified Area I",
        ["magnified effect ii"] = "Magnified Area II",
        ["cold infusion"] = "Cold Attunement",
        ["fire infusion"] = "Fire Attunement",
        ["lightning infusion"] = "Lightning Attunement",
        ["chaos infusion"] = "Chaos Attunement",
        ["ingenuity"] = "Cooldown Recovery I",
        ["ingenuity i"] = "Cooldown Recovery I",
        ["ingenuity ii"] = "Cooldown Recovery II",
        ["inspiration"] = "Efficiency I",
        ["inspiration i"] = "Efficiency I",
        ["inspiration ii"] = "Efficiency II",
        ["profusion"] = "Charge Profusion I",
        ["profusion i"] = "Charge Profusion I",
        ["profusion ii"] = "Charge Profusion II",
        ["persistence"] = "Prolonged Duration II",
        ["persistence i"] = "Prolonged Duration I",
        ["persistence ii"] = "Prolonged Duration II",
        ["overpower"] = "Stun I",
        ["overpower i"] = "Stun I",
        ["overpower ii"] = "Stun II",
        ["overpower iii"] = "Stun III",
        ["cast on freeze"] = "Cast on Elemental Ailment",
        ["cast on shock"] = "Cast on Elemental Ailment",
        ["cast on ignite"] = "Cast on Elemental Ailment",
        ["ailith lineage"] = "Ailith's Chimes",
        ["twofold"] = "Heightened Charges",
        ["creschendo"] = "Crescendo I",
        ["crescendo"] = "Crescendo I",
        ["scattershot"] = "Multishot I",
        ["controlled destruction"] = "Controlled Destruction"
    };

    // Локалізовані описи та точні квестові наводки для найпопулярніших каменів
    private static readonly Dictionary<string, LocalizedOverlay> Overlays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Whirling Slash"] = new(
            "Круговий ривок списом, що створює вихор навколо вас; осліплює/сповільнює ворогів і накопичує стадії для Twister.",
            "Spinning spear slash kicking up a whirlwind that slows and blinds foes and empowers Twister.",
            "Круговой выпад копьём, создающий вихрь; ослепляет врагов и усиливает Twister."),
        ["Twister"] = new(
            "Запускає вихори-снаряди, які відскакують від стін і підхоплюють стихії / вихори Whirling Slash.",
            "Fires bouncing tornados that inherit elemental ground effects and Whirlwind stages.",
            "Запускает смерчи, отскакивающие от стен и впитывающие стихии / вихри Whirling Slash."),
        ["Fangs of Frost"] = new(
            "Крижаний випад списом (Tier 3, Акт 1 з 6 рівня). При ударі по цілі під Parry створює крижаний вибух і Chilled Ground.",
            "Icy spear thrust (Uncut Tier 3, Char Lvl 6+, Act 1). Consumes Parry to trigger a frost burst and Chilled Ground.",
            "Ледяной выпад копьём (Uncut Tier 3, ур. 6+, Акт 1). Поглощает Parry для взрыва холодом и Chilled Ground."),
        ["Lightning Spear"] = new(
            "Кидає електризований спис (Tier 3, з 6 рівня в Акті 1), який розлітається на додаткові заряди при влучанні.",
            "Hurls a lightning-charged spear (Uncut Tier 3, Lvl 6+, Act 1) that splits on hit.",
            "Бросает копьё молнии (Uncut Tier 3, ур. 6+, Акт 1), расщепляющееся при попадании."),
        ["Whirling Assault"] = new(
            "Серія кругових ударів бойовим посохом (Tier 11, Акт 3 з 41 рівня); рухається вперед крізь паки.",
            "Advancing series of spinning Quarterstaff strikes (Uncut Tier 11, Char Lvl 41+).",
            "Серия круговых ударов боевым посохом (Uncut Tier 11, с 41 ур.)."),
        ["Tempest Flurry"] = new(
            "Швидке комбо блискавки бойовим посохом (Tier 5, з 14 рівня): 3-й удар б'є тричі, 4-й запускає хвилю шоку.",
            "Rapid 4-hit Lightning melee combo (Tier 5, Lvl 14+) culminating in a sweeping shockwave.",
            "Быстрое комбо молнии посохом (Tier 5, ур. 14+) с ударной волной на финальном ударе."),
        ["Falling Thunder"] = new(
            "Удар блискавкою по землі (Tier 1); поглинає Power Charges, випускаючи потужні електричні хвилі.",
            "Slams lightning into the ground, consuming Power Charges for sweeping projectiles.",
            "Удар молнии по земле; поглощает Power Charges, выпуская мощные волны."),
        ["Killing Palm"] = new(
            "Ривок без зброї (Tier 1): добиває ворогів із низьким HP та миттєво дає Power Charge.",
            "Unarmed dash strike (Tier 1) that Culls low-life foes to grant Power Charges.",
            "Рывок без оружия (Tier 1): добивает врагов и даёт Power Charge."),
        ["Charged Staff"] = new(
            "Поглинає Power Charge (Tier 9, Акт 2 з 31 рівня), наділяючи атаки плоским Lightning-уроном і хвилями.",
            "Consumes Power Charges (Tier 9, Lvl 31+) to imbue strikes with Lightning damage and shockwaves.",
            "Поглощает Power Charge (Tier 9, ур. 31+) ради плоского урона молнией и волн."),
        ["Tempest Bell"] = new(
            "Ставить дзвін після комбо (Tier 3, з 6 рівня в Акті 1): удари по дзвону створюють потужні хвилі по босах.",
            "Places a resonant bell (Tier 3, Lvl 6+) that blasts AoE shockwaves each time you strike it.",
            "Ставит колокол (Tier 3, ур. 6+): каждый удар по нему вызывает мощные волны по боссам."),
        ["Ice Strike"] = new(
            "Швидка крижана комбо-атака посохом (Tier 5, з 14 рівня) з високим накопиченням Freeze.",
            "Fast Cold quarterstaff combo (Tier 5, Lvl 14+) with high Freeze buildup.",
            "Быстрая ледяная серия ударов посохом (Tier 5, ур. 14+) с высоким Freeze."),
        ["Shattering Palm"] = new(
            "Ривок із крижаною міткою (Tier 11, з 41 рівня), що вибухає осколками холоду після отримання урону.",
            "Cold dash strike (Tier 11, Lvl 41+) that primes targets to explode in icy shards.",
            "Рывок с ледяной меткой (Tier 11, ур. 41+), взрывающейся осколками холода."),
        ["Flicker Strike"] = new(
            "Миттєва телепортація між цілями (Tier 13, з 52 рівня) за рахунок Power Charges.",
            "Rapidly teleports between targets (Tier 13, Lvl 52+) powered by Power Charges.",
            "Мгновенная телепортация между врагами (Tier 13, ур. 52+) за счёт Power Charges."),
        ["Storm Wave"] = new(
            "Далекобійна хвиля блискавки вздовж землі (Tier 7, Акт 2 з 22 рівня).",
            "Long-reaching Lightning fissure along the ground (Tier 7, Lvl 22+, Act 2).",
            "Дальнобойная волна молнии по земле (Tier 7, ур. 22+, Акт 2)."),
        ["Frost Bomb"] = new(
            "Крижаний імпульс (Tier 1, Акт 1 з 1 рівня): знижує опір холоду (Cold Exposure) та вибухає.",
            "Cold orb (Tier 1, Lvl 1+, Act 1) applying Cold Exposure before detonating.",
            "Ледяная сфера (Tier 1, ур. 1+, Акт 1): накладывает Cold Exposure и взрывается."),
        ["Spark"] = new(
            "Іскри блискавки (Tier 1), що відскакують від стін.",
            "Erratic lightning projectiles (Tier 1) that ricochet off terrain.",
            "Искры молнии (Tier 1), рикошетящие от стен."),
        ["Herald of Ice"] = new(
            "Резервує 30 Spirit (Tier 4, з 10 рівня). Розбиті заморожені вороги вибухають уроном холоду по площі.",
            "30 Spirit (Tier 4, Lvl 10+). Shattering frozen enemies triggers AoE Cold explosions.",
            "30 Spirit (Tier 4, ур. 10+). Расколотые замороженные враги взрываются холодом по области.",
            "Uncut Spirit Gem Lv 4+ (перший гарантовано в Акті 1 — Freythorn, бос King in the Mists).",
            "Uncut Spirit Gem Lv 4+ (first guaranteed in Act 1 Freythorn: King in the Mists).",
            "Uncut Spirit Gem Lv 4+ (первый гарантированно в Акте 1 — Freythorn, босс King in the Mists)."),
        ["Herald of Thunder"] = new(
            "Резервує 30 Spirit (Tier 4, з 10 рівня). Після вбивства шокованої цілі наступні атаки б'ють блискавками.",
            "30 Spirit (Tier 4, Lvl 10+). Killing Shocked foes calls down lightning bolts on hits.",
            "30 Spirit (Tier 4, ур. 10+). После убийства цели под Shock удары бьют молниями.",
            "Uncut Spirit Gem Lv 4+ (Акт 1 Freythorn або Акт 2).",
            "Uncut Spirit Gem Lv 4+ (Act 1 Freythorn or Act 2 drops).",
            "Uncut Spirit Gem Lv 4+ (Акт 1 Freythorn или Акт 2)."),
        ["Herald of Ash"] = new(
            "Резервує 30 Spirit (Tier 4, з 10 рівня). Надлишковий урон підпалює ворогів навколо.",
            "30 Spirit (Tier 4, Lvl 10+). Overkill damage ignites nearby enemies.",
            "30 Spirit (Tier 4, ур. 10+). Избыточный урон поджигает врагов вокруг."),
        ["Wind Dancer"] = new(
            "Резервує 30 Spirit (Tier 4, з 10 рівня). Накопичує стаки +More Evasion та б'є хвилею у відповідь.",
            "30 Spirit (Tier 4, Lvl 10+). Builds Evasion stages and bursts attackers when hit.",
            "30 Spirit (Tier 4, ур. 10+). Копит стаки Evasion и отбрасывает атакующих волной."),
        ["Ghost Dance"] = new(
            "Резервує 30 Spirit (Tier 4, з 10 рівня). Відновлює Energy Shield при отриманні удару пропорційно Evasion.",
            "30 Spirit (Tier 4, Lvl 10+). Recovers ES based on your Evasion when hit.",
            "30 Spirit (Tier 4, ур. 10+). Восстанавливает Energy Shield от вашего Evasion при ударе."),
        ["Combat Frenzy"] = new(
            "Резервує 30 Spirit (Tier 8, Акт 2 з 26 рівня). Дає Frenzy Charge при Freeze / Electrocute / Pin.",
            "30 Spirit (Tier 8, Lvl 26+, Act 2). Grants Frenzy Charges on Freeze, Electrocute, or Pin.",
            "30 Spirit (Tier 8, ур. 26+, Акт 2). Даёт Frenzy Charge при Freeze, Electrocute или Pin.",
            "Uncut Spirit Gem Lv 8+ (Акт 2 Lost City / Golden Tomb або дроп з 26 рівня).",
            "Uncut Spirit Gem Lv 8+ (Act 2 The Lost City / world drops Lvl 26+).",
            "Uncut Spirit Gem Lv 8+ (Акт 2 The Lost City или дроп с 26 уровня)."),
        ["Blink"] = new(
            "Резервує 60 Spirit (Tier 8, Акт 2 з 26 рівня). Перетворює Dodge Roll у миттєву телепортацію.",
            "60 Spirit (Tier 8, Lvl 26+, Act 2). Replaces Dodge Roll with instant teleportation.",
            "60 Spirit (Tier 8, ур. 26+, Акт 2). Заменяет перекат мгновенным телепортом Blink."),
        ["Grim Feast"] = new(
            "Міньйон-баф (Tier 4, з 10 рівня): загиблі міньйони лишають Grim Remnants, що прискорюють їх воскресіння.",
            "Minion/Remnant Persistent Buff (Tier 4, Lvl 10+): slain reviving minions leave Remnants for Grim Resurrection.",
            "Бафф миньонов (Tier 4, ур. 10+): погибшие миньоны оставляют Grim Remnants для воскрешения."),
        ["Trinity"] = new(
            "Резервує 100 Spirit (Tier 14, з 58 рівня). Накопичує стихійний резонанс для великого % More Elemental Damage.",
            "100 Spirit (Tier 14, Lvl 58+). Builds elemental affinity for huge More Elemental Damage.",
            "100 Spirit (Tier 14, ур. 58+). Копит стихийный резонанс для огромного % More урона.",
            "Uncut Spirit Gem Lv 14+ (Interlude 3 Kriar Village бос Lythara +40 Spirit або Waystones).",
            "Uncut Spirit Gem Lv 14+ (Interlude 3 Mount Kriar Lythara +40 Spirit or Waystones).",
            "Uncut Spirit Gem Lv 14+ (Interlude 3 Mount Kriar Lythara +40 Spirit или карты Waystones)."),
        ["Cast on Elemental Ailment"] = new(
            "Резервує 100 Spirit (Tier 14, з 58 рівня). Накопичує енергію при Freeze/Shock/Ignite і авто-кастує чари.",
            "100 Spirit (Tier 14, Lvl 58+). Triggers socketed spells when freezing, shocking, or igniting.",
            "100 Spirit (Tier 14, ур. 58+). Автоматически применяет чары при Freeze/Shock/Ignite."),
        ["Cast on Critical"] = new(
            "Резервує 100 Spirit (Tier 14, з 58 рівня). Авто-кастує прив'язані чари після крітів.",
            "100 Spirit (Tier 14, Lvl 58+). Auto-casts socketed spells on Critical Hits.",
            "100 Spirit (Tier 14, ур. 58+). Автоматически применяет чары от критических ударов.")
    };

    private static readonly Lazy<GemCatalogIndex> LazyIndex = new(LoadCatalog, isThreadSafe: true);

    private static GemCatalogIndex LoadCatalog()
    {
        var all = new List<RawGemEntry>(1000);
        try
        {
            var asm = typeof(Poe2GemsDb).Assembly;
            string? gzRes = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("poe2_gems_05.json.gz", StringComparison.OrdinalIgnoreCase));
            if (gzRes != null)
            {
                using Stream? s = asm.GetManifestResourceStream(gzRes);
                if (s != null)
                {
                    using var gz = new GZipStream(s, CompressionMode.Decompress);
                    var parsed = JsonSerializer.Deserialize<List<RawGemEntry>>(gz);
                    if (parsed != null) all = parsed;
                }
            }
            else
            {
                string? rawRes = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("poe2_gems_05.json", StringComparison.OrdinalIgnoreCase));
                if (rawRes != null)
                {
                    using Stream? s = asm.GetManifestResourceStream(rawRes);
                    if (s != null)
                    {
                        var parsed = JsonSerializer.Deserialize<List<RawGemEntry>>(s);
                        if (parsed != null) all = parsed;
                    }
                }
            }
        }
        catch { }

        var normMap = new Dictionary<string, RawGemEntry>(all.Count * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var g in all)
        {
            if (string.IsNullOrWhiteSpace(g.n)) continue;
            string nk = NormalizeKey(g.n);
            normMap.TryAdd(nk, g);
            // Також додаємо форму без I (напр. "Rapid Attacks" -> "Rapid Attacks I")
            if (nk.EndsWith(" i", StringComparison.Ordinal))
                normMap.TryAdd(nk[..^2], g);
        }

        return new GemCatalogIndex { All = all, ByNormalized = normMap };
    }

    public static PoeGemInfo ResolveGem(string rawGemName, int? stageMinLevel = null)
    {
        string cleaned = (rawGemName ?? "").Trim();
        int? explicitGemLvl = null;
        int quality = 0;

        var mLvl = Regex.Match(cleaned, @"\(Lv(\d+)(?:\s*Q(\d+)%?)?\)", RegexOptions.IgnoreCase);
        if (mLvl.Success)
        {
            if (int.TryParse(mLvl.Groups[1].Value, out int parsedL)) explicitGemLvl = parsedL;
            if (mLvl.Groups[2].Success && int.TryParse(mLvl.Groups[2].Value, out int parsedQ)) quality = parsedQ;
            cleaned = Regex.Replace(cleaned, @"\s*\(Lv\d+[^)]*\)", "").Trim();
        }

        var idx = LazyIndex.Value;
        var (match, parsedRomanTier) = FindBestCatalogMatch(idx, cleaned);

        if (match != null)
        {
            GemCategory cat = match.k switch
            {
                "S" => GemCategory.SpiritBuff,
                "M" => GemCategory.MetaTrigger,
                "P" => GemCategory.Support,
                _ => GemCategory.ActiveSkill
            };

            if (cat == GemCategory.Support)
            {
                bool isLineage = match.t >= 99 || match.g.Contains("Lineage", StringComparison.OrdinalIgnoreCase);
                int supportTier = isLineage ? 0 : Math.Clamp(match.t > 0 ? match.t : (parsedRomanTier > 0 ? parsedRomanTier : 1), 1, 5);
                int minCharLvl = isLineage ? 65 : SupportTierMinLevel(supportTier);
                string tierLabel = isLineage ? "Lineage Support (Lvl 65+)" : $"Uncut Support Gem Tier {supportTier}";
                int reqAttr = isLineage ? 5 : Math.Max(0, supportTier switch { 1 => 0, 2 => 5, 3 => 15, 4 => 30, _ => 45 });

                var (descUk, descEn, descRu, obtUk, obtEn, obtRu) = BuildLocalizedText(match, cat, supportTier, minCharLvl, isLineage);
                return new PoeGemInfo(
                    match.n, GemCategory.Support, match.a,
                    explicitGemLvl ?? 1, quality, minCharLvl,
                    match.a.Contains("Str") ? reqAttr : 0,
                    match.a.Contains("Dex") ? reqAttr : 0,
                    match.a.Contains("Int") ? reqAttr : 0,
                    tierLabel,
                    match.sp, match.mm, match.g,
                    obtUk, obtEn, obtRu,
                    descUk, descEn, descRu
                );
            }

            // Active Skill / Spirit Buff / Meta Trigger (Tier 1..20 -> точна таблиця PoE2DB)
            int reqUncut = Math.Clamp(match.t > 0 ? match.t : 1, 1, 20);
            int minLvl = SkillUncutCharLevel[reqUncut];
            int displayGemLvl = Math.Max(explicitGemLvl ?? reqUncut, reqUncut);
            string uncutLabel = cat is GemCategory.SpiritBuff or GemCategory.MetaTrigger
                ? $"Uncut Spirit Gem Lv {reqUncut}"
                : $"Uncut Skill Gem Lv {reqUncut}";

            bool hybrid = match.a.Contains('/') || match.a.Equals("Hybrid", StringComparison.OrdinalIgnoreCase);
            int perAttr = hybrid ? HybridAttrByGemLevel[reqUncut] : SingleAttrByGemLevel[reqUncut];

            var (aDescUk, aDescEn, aDescRu, aObtUk, aObtEn, aObtRu) = BuildLocalizedText(match, cat, reqUncut, minLvl, isLineage: false);
            return new PoeGemInfo(
                match.n, cat, match.a,
                displayGemLvl, quality, minLvl,
                match.a.Contains("Str") ? perAttr : 0,
                match.a.Contains("Dex") ? perAttr : 0,
                match.a.Contains("Int") ? perAttr : 0,
                uncutLabel,
                match.sp, match.mm, match.g,
                aObtUk, aObtEn, aObtRu,
                aDescUk, aDescEn, aDescRu
            );
        }

        // Безпечний fallback для рідкісних або ще не доданих каменів:
        // НІКОЛИ не плутаємо stageMinLevel (рівень персонажа!) з тіром Uncut каменю!
        bool looksLikeSupport = parsedRomanTier > 0 ||
            cleaned.Contains("support", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" Infusion", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" Attunement", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" Mastery", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" Two", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" Three", StringComparison.OrdinalIgnoreCase);

        if (looksLikeSupport)
        {
            int tier = Math.Clamp(parsedRomanTier > 0 ? parsedRomanTier : 1, 1, 5);
            int charLvl = SupportTierMinLevel(tier);
            return new PoeGemInfo(
                cleaned, GemCategory.Support, "Hybrid",
                explicitGemLvl ?? 1, quality, charLvl, 0, 0, 0,
                $"Uncut Support Gem Tier {tier}",
                0, 100, "Support",
                SupportObtainUk(tier, isLineage: false), SupportObtainEn(tier, isLineage: false), SupportObtainRu(tier, isLineage: false),
                "Камінь підтримки PoE 2. Перевір точний тір у вікні Gemcutting.",
                "PoE 2 Support Gem. Check exact tier inside the in-game Gemcutting menu.",
                "Камень поддержки PoE 2. Проверь точный тир в меню Gemcutting."
            );
        }

        bool inLiveCatalog = LivePoe2DataService.ContainsGem(cleaned);
        int fallbackTier = explicitGemLvl is > 0 ? Math.Clamp(explicitGemLvl.Value, 1, 20) : 1;
        int fallbackMinChar = SkillUncutCharLevel[fallbackTier];
        string badge = explicitGemLvl.HasValue
            ? $"Skill Gem Lv {fallbackTier}"
            : (inLiveCatalog ? "GGG Live Catalog Gem" : "Uncut / Granted Skill");
        return new PoeGemInfo(
            cleaned, GemCategory.ActiveSkill, "Hybrid",
            fallbackTier, quality, fallbackMinChar, 0, 0, 0,
            badge,
            0, 100, inLiveCatalog ? "Skill (Live GGG)" : "Skill",
            UncutSkillObtainUk(fallbackTier, GemCategory.ActiveSkill),
            UncutSkillObtainEn(fallbackTier, GemCategory.ActiveSkill),
            UncutSkillObtainRu(fallbackTier, GemCategory.ActiveSkill),
            inLiveCatalog
                ? "Офіційний камінь PoE 2 із живого каталогу GGG API."
                : "Камінь уміння PoE 2 (або скіл зі зброї / Ascendancy).",
            inLiveCatalog
                ? "Official PoE 2 Skill Gem resolved from live GGG Trade 2 catalog."
                : "PoE 2 Skill Gem (or weapon/Ascendancy-granted skill).",
            inLiveCatalog
                ? "Официальный камень PoE 2 из живого каталога GGG API."
                : "Камень умения PoE 2 (или скилл от оружия / Ascendancy)."
        );
    }

    public static List<PoeGemInfo> GetAllCatalogEntries(string search = "", GemCategory? categoryFilter = null)
    {
        string q = (search ?? "").Trim();
        var idx = LazyIndex.Value;
        return idx.All
            .Where(g => categoryFilter == null || MapCategory(g.k) == categoryFilter.Value)
            .Where(g => string.IsNullOrEmpty(q) ||
                        g.n.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        g.g.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        g.d.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (Overlays.TryGetValue(g.n, out var ov) &&
                         (ov.DescUk.Contains(q, StringComparison.OrdinalIgnoreCase) || ov.DescRu.Contains(q, StringComparison.OrdinalIgnoreCase))))
            .Select(g => ResolveGem(g.n))
            .DistinctBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
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

    private static (RawGemEntry? Match, int ParsedRomanTier) FindBestCatalogMatch(GemCatalogIndex idx, string rawName)
    {
        string norm = NormalizeKey(rawName);
        int romanTier = 0;

        // Розпізнаємо "One/Two/Three/Four/Five" або "I/II/III/IV/V" у кінці назви
        var mSuffix = Regex.Match(norm, @"^(.+?)\s+(i|ii|iii|iv|v|one|two|three|four|five)$");
        if (mSuffix.Success)
        {
            romanTier = mSuffix.Groups[2].Value switch
            {
                "one" or "i" => 1,
                "two" or "ii" => 2,
                "three" or "iii" => 3,
                "four" or "iv" => 4,
                _ => 5
            };
            string converted = $"{mSuffix.Groups[1].Value} {romanTier switch { 1 => "i", 2 => "ii", 3 => "iii", 4 => "iv", _ => "v" }}";
            if (GemAliases.TryGetValue(converted, out var aliasHit) && idx.ByNormalized.TryGetValue(NormalizeKey(aliasHit), out var a1))
                return (a1, romanTier);
            if (idx.ByNormalized.TryGetValue(converted, out var directT))
                return (directT, romanTier);
        }

        if (GemAliases.TryGetValue(norm, out var alias) && idx.ByNormalized.TryGetValue(NormalizeKey(alias), out var a2))
            return (a2, romanTier);

        if (idx.ByNormalized.TryGetValue(norm, out var exact))
            return (exact, romanTier);

        // Скидаємо префікси класу/форми ("Ascendancy Hollow Focus", "Wyvern Devour", "Bear Maul")
        string stripped = Regex.Replace(norm, @"^(?:ascendancy|wyvern|bear|wolf|druid|shaman|oracle|amazon)\s+", "");
        if (stripped != norm && idx.ByNormalized.TryGetValue(stripped, out var stripMatch))
            return (stripMatch, romanTier);

        // Апостроф / префіксне співпадіння лише для достатньо довгих точних фрагментів
        if (norm.Length >= 5)
        {
            var fuzzy = idx.All.FirstOrDefault(g =>
                NormalizeKey(g.n).Equals(stripped, StringComparison.OrdinalIgnoreCase) ||
                NormalizeKey(g.n).StartsWith(norm + " ", StringComparison.OrdinalIgnoreCase));
            if (fuzzy != null)
                return (fuzzy, romanTier);
        }

        return (null, romanTier);
    }

    private static string NormalizeKey(string s)
    {
        string clean = (s ?? "").Trim()
            .Replace("'", "")
            .Replace("’", "")
            .Replace("`", "")
            .Replace("í", "i")
            .Replace("Í", "i")
            .Replace("-", " ")
            .ToLowerInvariant();
        return WhitespaceRegex().Replace(clean, " ").Trim();
    }

    private static GemCategory MapCategory(string k) => k switch
    {
        "S" => GemCategory.SpiritBuff,
        "M" => GemCategory.MetaTrigger,
        "P" => GemCategory.Support,
        _ => GemCategory.ActiveSkill
    };

    private static int SupportTierMinLevel(int tier) => tier switch
    {
        <= 1 => 1,
        2 => 16,
        3 => 33,
        4 => 45,
        _ => 55
    };

    private static (string DescUk, string DescEn, string DescRu, string ObtUk, string ObtEn, string ObtRu) BuildLocalizedText(
        RawGemEntry entry, GemCategory cat, int tier, int minLvl, bool isLineage)
    {
        bool hasOv = Overlays.TryGetValue(entry.n, out var ov);
        string descEn = hasOv ? ov!.DescEn : (!string.IsNullOrWhiteSpace(entry.d) ? entry.d : $"PoE 2 {entry.g} gem.");
        string descUk = hasOv ? ov!.DescUk : $"{descEn} ({entry.g}; мін. рівень: {minLvl}+).";
        string descRu = hasOv ? ov!.DescRu : $"{descEn} ({entry.g}; мин. уровень: {minLvl}+).";

        string obtUk = (hasOv ? ov!.CustomObtainUk : null) ?? (cat == GemCategory.Support ? SupportObtainUk(tier, isLineage) : UncutSkillObtainUk(tier, cat));
        string obtEn = (hasOv ? ov!.CustomObtainEn : null) ?? (cat == GemCategory.Support ? SupportObtainEn(tier, isLineage) : UncutSkillObtainEn(tier, cat));
        string obtRu = (hasOv ? ov!.CustomObtainRu : null) ?? (cat == GemCategory.Support ? SupportObtainRu(tier, isLineage) : UncutSkillObtainRu(tier, cat));
        return (descUk, descEn, descRu, obtUk, obtEn, obtRu);
    }

    private static string SupportObtainUk(int tier, bool isLineage)
    {
        if (isLineage)
            return "Lineage Support (65+ рів.): рідкісний ендгейм-дроп (Waystones, боси) або PoE 2 Trade.";
        return tier switch
        {
            5 => "Uncut Support Gem Tier 5: падає у зонах 55+ рівня (Interludes та Waystones).",
            4 => "Uncut Support Gem Tier 4: падає у зонах 45+ рівня (Акт 4 та Interludes).",
            3 => "Uncut Support Gem Tier 3: падає у зонах 33+ рівня (Акт 3 — Sandswept Marsh і далі).",
            2 => "Uncut Support Gem Tier 2: падає у зонах 16+ рівня (Акт 2 — Vastiri Outskirts і далі).",
            _ => "Uncut Support Gem Tier 1: з 1 рівня (Акт 1 — Grelwood / Tree of Souls, квести та дроп)."
        };
    }

    private static string SupportObtainEn(int tier, bool isLineage)
    {
        if (isLineage)
            return "Lineage Support (Lvl 65+): Endgame Waystones / boss drop or PoE 2 Trade.";
        return tier switch
        {
            5 => "Uncut Support Gem Tier 5: drops in Area Lvl 55+ (Interludes / Endgame Waystones).",
            4 => "Uncut Support Gem Tier 4: drops in Area Lvl 45+ (Act 4 & Interludes).",
            3 => "Uncut Support Gem Tier 3: drops in Area Lvl 33+ (Act 3 zones onwards).",
            2 => "Uncut Support Gem Tier 2: drops in Area Lvl 16+ (Act 2 zones onwards).",
            _ => "Uncut Support Gem Tier 1: available from Lvl 1 (Act 1 quests & world drops)."
        };
    }

    private static string SupportObtainRu(int tier, bool isLineage)
    {
        if (isLineage)
            return "Lineage Support (65+ ур.): эндгейм-дроп (Waystones / боссы) или PoE 2 Trade.";
        return tier switch
        {
            5 => "Uncut Support Gem Tier 5: падает в зонах 55+ уровня (Interludes и Waystones).",
            4 => "Uncut Support Gem Tier 4: падает в зонах 45+ уровня (Акт 4 и Interludes).",
            3 => "Uncut Support Gem Tier 3: падает в зонах 33+ уровня (Акт 3 и далее).",
            2 => "Uncut Support Gem Tier 2: падает в зонах 16+ уровня (Акт 2 и далее).",
            _ => "Uncut Support Gem Tier 1: доступен с 1 уровня (квесты и дроп в 1-м Акте)."
        };
    }

    private static string UncutSkillObtainUk(int tier, GemCategory cat)
    {
        if (cat is GemCategory.SpiritBuff or GemCategory.MetaTrigger)
        {
            return tier switch
            {
                <= 4 => $"Uncut Spirit Gem Lv {tier}: Акт 1 Freythorn (бос King in the Mists +30 Spirit) або ранній дроп.",
                <= 8 => $"Uncut Spirit Gem Lv {tier}: Акт 2 (The Lost City — Golden Tomb) та зони 20–32 рівня.",
                <= 10 => $"Uncut Spirit Gem Lv {tier}: Акт 3 (The Azak Bog — бос Ignagduk +30 Spirit) та зони 33–45 рівня.",
                <= 14 => $"Uncut Spirit Gem Lv {tier}: Акт 4 / Interlude 3 (Kriar Village — Lythara +40 Spirit) або Waystones.",
                _ => $"Uncut Spirit Gem Lv {tier}: Waystones (64+ рів.) або PoE 2 Trade."
            };
        }

        return tier switch
        {
            <= 5 => $"Uncut Skill Gem Lv {tier}: Акт 1 (Clearfell → Ogham Manor, зони 1–16 рівня; мін. {SkillUncutCharLevel[tier]} рів. героя).",
            <= 9 => $"Uncut Skill Gem Lv {tier}: Акт 2 (Vastiri Outskirts → Dreadnought, зони 16–32 рівня).",
            <= 12 => $"Uncut Skill Gem Lv {tier}: Акт 3 (Sandswept Marsh → Black Chambers, зони 33–46 рівня).",
            <= 14 => $"Uncut Skill Gem Lv {tier}: Акт 4 та Interludes 1–3 (зони 47–64 рівня).",
            <= 18 => $"Uncut Skill Gem Lv {tier}: Waystones T1–T15 (зони 65–79 рівня).",
            _ => $"Uncut Skill Gem Lv {tier}: рідкісний ендгейм-дроп (T15+ / Corrupted / Pinnacle-боси) або PoE 2 Trade."
        };
    }

    private static string UncutSkillObtainEn(int tier, GemCategory cat)
    {
        if (cat is GemCategory.SpiritBuff or GemCategory.MetaTrigger)
        {
            return tier switch
            {
                <= 4 => $"Uncut Spirit Gem Lv {tier}: Act 1 Freythorn (King in the Mists +30 Spirit) or early drops.",
                <= 8 => $"Uncut Spirit Gem Lv {tier}: Act 2 (Lost City Golden Tomb) & Area Lvl 20–32 drops.",
                <= 10 => $"Uncut Spirit Gem Lv {tier}: Act 3 (Azak Bog: Ignagduk +30 Spirit) & Area Lvl 33–45.",
                <= 14 => $"Uncut Spirit Gem Lv {tier}: Act 4 / Interlude 3 (Mount Kriar: Lythara +40 Spirit) or Waystones.",
                _ => $"Uncut Spirit Gem Lv {tier}: Waystones (Lvl 64+) or PoE 2 Trade."
            };
        }

        return tier switch
        {
            <= 5 => $"Uncut Skill Gem Lv {tier}: Act 1 quests & zones (Area Lvl 1–16; Char Lvl {SkillUncutCharLevel[tier]}+).",
            <= 9 => $"Uncut Skill Gem Lv {tier}: Act 2 zones (Area Lvl 16–32).",
            <= 12 => $"Uncut Skill Gem Lv {tier}: Act 3 zones (Area Lvl 33–46).",
            <= 14 => $"Uncut Skill Gem Lv {tier}: Act 4 & Interludes 1–3 (Area Lvl 47–64).",
            <= 18 => $"Uncut Skill Gem Lv {tier}: Endgame Waystones T1–T15 (Area Lvl 65–79).",
            _ => $"Uncut Skill Gem Lv {tier}: Rare T15+ / Corrupted / Pinnacle Boss drop or PoE 2 Trade."
        };
    }

    private static string UncutSkillObtainRu(int tier, GemCategory cat)
    {
        if (cat is GemCategory.SpiritBuff or GemCategory.MetaTrigger)
        {
            return tier switch
            {
                <= 4 => $"Uncut Spirit Gem Lv {tier}: Акт 1 Freythorn (King in the Mists +30 Spirit) или ранний дроп.",
                <= 8 => $"Uncut Spirit Gem Lv {tier}: Акт 2 (Lost City Golden Tomb) и зоны 20–32 уровня.",
                <= 10 => $"Uncut Spirit Gem Lv {tier}: Акт 3 (Azak Bog: Ignagduk +30 Spirit) и зоны 33–45 ур.",
                <= 14 => $"Uncut Spirit Gem Lv {tier}: Акт 4 / Interlude 3 (Kriar Village: Lythara +40 Spirit) или Waystones.",
                _ => $"Uncut Spirit Gem Lv {tier}: Waystones (64+ ур.) или PoE 2 Trade."
            };
        }

        return tier switch
        {
            <= 5 => $"Uncut Skill Gem Lv {tier}: Акт 1 (квесты и зоны 1–16 ур.; мин. {SkillUncutCharLevel[tier]} ур. героя).",
            <= 9 => $"Uncut Skill Gem Lv {tier}: Акт 2 (зоны 16–32 ур.).",
            <= 12 => $"Uncut Skill Gem Lv {tier}: Акт 3 (зоны 33–46 ур.).",
            <= 14 => $"Uncut Skill Gem Lv {tier}: Акт 4 и Interludes 1–3 (зоны 47–64 ур.).",
            <= 18 => $"Uncut Skill Gem Lv {tier}: карты Waystones T1–T15 (65–79 ур.).",
            _ => $"Uncut Skill Gem Lv {tier}: редкий дроп T15+ / Pinnacle-боссы или PoE 2 Trade."
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
