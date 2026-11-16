using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed record FilterValidationResult(bool IsValid, int BlockCount, List<string> Warnings);

/// <summary>
/// Робота з лутофільтрами Path of Exile 2 (без вбудованого фільтру):
/// підвантаження користувацьких .filter, AI-генерація під активний білд, валідація та збереження в папку гри.
/// </summary>
public static class LootFilterService
{
    public static string GetGameFilterFolder()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "My Games", "Path of Exile 2");
    }

    public static List<string> ListInstalledFilters()
    {
        string dir = GetGameFilterFolder();
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.filter").OrderBy(x => x).ToList();
    }

    public static (bool ok, string destPath, string content) InstallExistingFilter(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return (false, "", "");

        string dir = GetGameFilterFolder();
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir, Path.GetFileName(sourcePath));
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, dest, overwrite: true);

        string text = File.ReadAllText(dest, Encoding.UTF8);
        return (true, dest, text);
    }

    public static string SaveCustomFilter(string filterName, string content)
    {
        string dir = GetGameFilterFolder();
        Directory.CreateDirectory(dir);
        string safeName = Regex.Replace((filterName ?? "Poe2Overlay").Trim(), @"[^\w\-\.\s]", "_").Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Poe2Overlay";
        if (!safeName.EndsWith(".filter", StringComparison.OrdinalIgnoreCase))
            safeName += ".filter";

        string dest = Path.Combine(dir, safeName);
        File.WriteAllText(dest, content.Replace("\r\n", "\n"), Encoding.UTF8);
        return dest;
    }

    public static FilterValidationResult ValidateFilter(string content)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
            return new FilterValidationResult(false, 0, [Loc.T("Порожній вміст фільтру.", "Filter text is empty.", "Пустой текст фильтра.")]);

        int blocks = Regex.Matches(content, @"^\s*(Show|Hide|Minimal)\b", RegexOptions.Multiline | RegexOptions.IgnoreCase).Count;
        if (blocks == 0)
            warnings.Add(Loc.T("Не знайдено жодного блоку Show або Hide.", "No Show/Hide rule blocks found.", "Не найдено ни одного блока Show или Hide."));

        foreach (string poe1Only in new[] { "LinkedSockets", "SocketGroup", "ShaperItem", "ElderItem" })
        {
            if (Regex.IsMatch(content, $@"\b{poe1Only}\b", RegexOptions.IgnoreCase))
                warnings.Add(Loc.T($"Помічено PoE1-ключ '{poe1Only}' (може викликати помилку в PoE 2).", $"Detected PoE1-only keyword '{poe1Only}' (invalid in PoE 2).", $"Обнаружен ключ PoE1 '{poe1Only}' (недействителен в PoE 2)."));
        }

        return new FilterValidationResult(blocks > 0, blocks, warnings);
    }

    public static async Task<(bool ok, string filterText, string modelOrError)> GenerateWithAiAsync(
        GeminiAdvisorService ai,
        BuildProfile? profile,
        int strictnessLevel,
        string customNotes,
        CancellationToken ct = default)
    {
        if (!ai.HasApiKey)
            return (false, "", Loc.T("Для AI-генерації задай API-ключ у вкладці AI-радника.", "Configure an API key in AI Advisor first to generate filters.", "Для AI-генерации задай API-ключ во вкладке AI-советника."));

        string strictLabel = strictnessLevel switch
        {
            1 => "1 - Campaign / Leveling (show upgrades, flasks, links, currency, all rares matching build)",
            2 => "2 - Early Maps (hide white trash except high bases, highlight build weapon/armour bases, all currency/waystones)",
            3 => "3 - Endgame Strict (hide low bases & gold piles < 100, highlight T1 bases ilvl 80+, Tiered Currency, Waystones 12+)",
            _ => "4 - Uber Very Strict (top high-value currency, uniques, Waystones 14+, high-ilvl crafting bases for the build ONLY)"
        };

        string buildCtx = profile == null
            ? "Universal Path of Exile 2 Endgame/Mapping character (balanced all-round)."
            : $"Build Name: {profile.Name}\nClass/Ascendancy: {profile.ClassName} {profile.Ascendancy}\nDefense: {profile.DefenseFocus} | CI: {profile.IsCi}\nPreferred Weapon Class: {profile.PreferredWeaponClass}\nSkills: {string.Join(", ", profile.TargetSkills.Take(8))}\nTarget Gear: {string.Join("; ", profile.TargetGearHints.Take(8))}";

        string prompt = $"""
Generate a valid, ready-to-load Path of Exile 2 loot filter (`.filter` file).
STRICTNESS: {strictLabel}
ACTIVE BUILD PROFILE:
{buildCtx}
USER EXTRA REQUESTS: {(string.IsNullOrWhiteSpace(customNotes) ? "None" : customNotes)}

STRICT SYNTAX RULES FOR POE 2 `.filter`:
1. Output ONLY raw `.filter` file text (NO markdown fences, NO conversational intro or outro). Add short `#` comments for sections.
2. NEVER use PoE 1 keywords like `LinkedSockets`, `SocketGroup`, `ShaperItem`, `ElderItem`, `ElderMap`, `BlightedMap`! PoE 2 uses `Sockets >=`, `Quality >=`, `ItemLevel >=`, `WaystoneTier >=`, `StackSize >=`, `BaseType ==`, `Class ==`, `Rarity`.
3. Valid PoE 2 classes include: "Stackable Currency", "Waystones", "Tablets", "Uncut Skill Gems", "Uncut Support Gems", "Uncut Spirit Gems", "Jewels", "Charms", "Rings", "Amulets", "Belts", "Body Armours", "Helmets", "Gloves", "Boots", "Shields", "Foci", "Bucklers", "Quarterstaves", "Wands", "Staves", "Bows", "Crossbows", "Spears", "Sceptres", "Talismans", "One Hand Maces", "Two Hand Maces", "Flails", "Life Flasks", "Mana Flasks".
4. Tailor the weapon/armour base rules specifically to this build's Preferred Weapon Class (`{profile?.PreferredWeaponClass}`) and Defense (`{profile?.DefenseFocus}`, CI={profile?.IsCi}). Include Waystones, Currency tiers, Uncut Gems, Jewels, and clean color/beam/sound styling (`SetFontSize`, `SetBorderColor`, `SetTextColor`, `SetBackgroundColor`, `PlayEffect`, `MinimapIcon`, `PlayAlertSound`).
5. End with a catch-all `Show` (for Campaign) or `Hide` (for Strictness >= 2) rule so no unintended rules leak through.
""";

        var (ok, text, model) = await ai.AskAsync(prompt, profile, jpegBytes: null, ct);
        if (!ok) return (false, "", text);

        // Очистити можливі ```filter / ``` обгортки
        string cleaned = Regex.Replace(text.Trim(), @"^```[a-zA-Z]*\s*", "");
        cleaned = Regex.Replace(cleaned, @"\s*```$", "").Trim();
        return (true, cleaned, model);
    }
}
