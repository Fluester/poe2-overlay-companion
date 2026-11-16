using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

/// <summary>
/// Генерує готові для вставки в PoE 2 (<50 символів) пошукові Regex-рядки:
/// 1) для NPC-торговців (Movement Speed, Spirit, +Skills, тип зброї),
/// 2) для фільтрації небезпечних Waystones перед запуском мапи.
/// </summary>
public static class RegexGeneratorService
{
    public static string BuildVendorRegex(BuildProfile? profile)
    {
        if (profile == null)
            return "\"movement speed|spirit|to level\"";

        var tokens = new List<string> { "movement" };
        if (profile.DefenseFocus is "ES" or "EV_ES" or "AR_ES")
            tokens.Add("energy shield");
        else if (profile.DefenseFocus is "ARMOUR")
            tokens.Add("armour");
        else if (profile.DefenseFocus is "EV")
            tokens.Add("evasion");

        if (!string.IsNullOrWhiteSpace(profile.PreferredWeaponClass))
        {
            string w = profile.PreferredWeaponClass.ToLowerInvariant();
            if (w.Contains("quarter")) tokens.Add("quarter");
            else if (w.Contains("spear")) tokens.Add("spear");
            else if (w.Contains("wand")) tokens.Add("wand");
            else if (w.Contains("bow")) tokens.Add("bow");
            else if (w.Contains("mace")) tokens.Add("mace");
            else if (w.Contains("scept")) tokens.Add("sceptre");
        }

        tokens.Add("spirit|to level");
        string joined = $"\"{string.Join("|", tokens.Distinct())}\"";
        return joined.Length <= 50 ? joined : "\"movement|spirit|to level\"";
    }

    public static string BuildSafeWaystoneRegex(BuildProfile? profile)
    {
        // Скриває небезпечні для білду моди мап (символ "!" в пошуку PoE 2 вимагає ВІДСУТНІСТЬ збігу)
        var danger = new List<string> { "ele pen", "less rec" };
        if (profile?.IsCi != true)
            danger.Add("no regen");
        if (profile?.DamageStyle is "Attack" or "Minion")
            danger.Add("leech");

        string body = string.Join("|", danger.Distinct());
        if (body.Length > 47) body = body[..47].TrimEnd('|');
        return $"!\"{body}\"";
    }
}
