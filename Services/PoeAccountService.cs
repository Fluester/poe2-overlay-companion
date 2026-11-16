using System.Text.Json;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed class PoeAccountCharacter
{
    public string name { get; set; } = "";
    public string league { get; set; } = "";
    public string @class { get; set; } = "";
    public int level { get; set; }
    public long experience { get; set; }
}

public sealed class PoeAccountSyncPayload
{
    public bool ok { get; set; }
    public string error { get; set; } = "";
    public string accountName { get; set; } = "";
    public PoeAccountCharacter? character { get; set; }
    public List<PoeAccountCharacter> characters { get; set; } = [];
    public Dictionary<string, string> equippedBySlot { get; set; } = [];
    public List<string> passiveHashes { get; set; } = [];
}

/// <summary>
/// Офіційна синхронізація PoE 2 акаунту через авторизовану сесію pathofexile.com у WebView2
/// (аналогічно підключенню акаунту в PoE Overlay 2, без читання пам'яті гри й без сторонніх проксі):
/// завантажує точний рівень персонажа, лігу, вдягнуті предмети (для порівняння Краще/Гірше) та взяті пасивки.
/// </summary>
public static class PoeAccountService
{
    public const string FetchCharactersAndDataScript = """
(async () => {
  try {
    const profEl = document.querySelector('a[href*="/account/view-profile/"], .profile-link a');
    let accName = profEl ? (profEl.innerText || '').trim() : '';
    if (!accName && profEl && profEl.getAttribute) {
      const m = (profEl.getAttribute('href') || '').match(/view-profile\/([^/?#]+)/i);
      if (m) accName = decodeURIComponent(m[1]);
    }

    const poe1Only = new Set([
      'scion','ascendant','duelist','slayer','gladiator','champion',
      'shadow','assassin','saboteur','trickster','templar','inquisitor','hierophant','guardian',
      'marauder','juggernaut','berserker','chieftain','necromancer','occultist','elementalist','raider','warden'
    ]);

    const urls = [
      '/character-window/get-characters?realm=poe2',
      accName ? ('/character-window/get-characters?realm=poe2&accountName=' + encodeURIComponent(accName)) : null
    ].filter(Boolean);

    let allChars = [];
    for (const u of urls) {
      try {
        const chResp = await fetch(u, { credentials: 'include' });
        if (chResp.ok) {
          const arr = await chResp.json();
          if (Array.isArray(arr) && arr.length > 0) {
            allChars = arr;
            break;
          }
        }
      } catch {}
    }

    if (allChars.length === 0) {
      return JSON.stringify({ ok: false, error: 'Sign in on pathofexile2.com first, then click Load Characters.' });
    }

    const explicitPoe2 = allChars.filter(c => c && String(c.realm || '').toLowerCase() === 'poe2');
    const filtered = explicitPoe2.length > 0
      ? explicitPoe2
      : allChars.filter(c => c && String(c.realm || '').toLowerCase() !== 'pc' && !poe1Only.has(String(c.class || '').toLowerCase()));

    if (filtered.length === 0) {
      return JSON.stringify({
        ok: false,
        error: 'GGG API returned only PoE 1 characters (' + allChars.length + '). Make sure your PoE 2 profile & characters tab is not set to Private on pathofexile2.com.'
      });
    }

    return JSON.stringify({ ok: true, accountName: accName, characters: filtered });
  } catch (e) {
    return JSON.stringify({ ok: false, error: String(e) });
  }
})();
""";

    public static string BuildFetchCharacterDetailsScript(string characterName, string accountName)
    {
        string safeChar = JsonSerializer.Serialize(characterName);
        string safeAcc = JsonSerializer.Serialize(accountName ?? "");
        return $$"""
(async () => {
  try {
    const cName = {{safeChar}};
    const acc = {{safeAcc}};
    const qAcc = acc ? ('&accountName=' + encodeURIComponent(acc)) : '';
    const itemResp = await fetch('/character-window/get-items?realm=poe2&character=' + encodeURIComponent(cName) + qAcc, { credentials: 'include' });
    const passResp = await fetch('/character-window/get-passive-skills?realm=poe2&character=' + encodeURIComponent(cName) + qAcc, { credentials: 'include' });

    const equippedBySlot = {};
    if (itemResp.ok) {
      const idata = await itemResp.json();
      if (Array.isArray(idata.items)) {
        for (const it of idata.items) {
          const inv = it.inventoryId || '';
          if (!inv || inv === 'MainInventory' || inv.startsWith('Flask')) continue;
          const props = (it.properties || []).map(p => {
            const vals = (p.values || []).map(v => v[0]).join(', ');
            return vals ? (p.name + ': ' + vals) : p.name;
          });
          const mods = []
            .concat(it.implicitMods || [])
            .concat(it.explicitMods || [])
            .concat(it.enchantMods || []);
          const title = [it.name, it.typeLine].filter(Boolean).join(' ');
          equippedBySlot[inv] = [title].concat(props).concat(mods).join('\n');
        }
      }
    }

    let passiveHashes = [];
    if (passResp.ok) {
      const pdata = await passResp.json();
      if (Array.isArray(pdata.hashes)) passiveHashes = pdata.hashes.map(h => String(h));
      else if (Array.isArray(pdata.hashes_ex)) passiveHashes = pdata.hashes_ex.map(h => String(h));
    }

    return JSON.stringify({ ok: true, equippedBySlot, passiveHashes });
  } catch (e) {
    return JSON.stringify({ ok: false, error: String(e) });
  }
})();
""";
    }

    public static void ApplySyncedCharacterToConfig(
        OverlayConfig cfg,
        string accountName,
        PoeAccountCharacter ch,
        Dictionary<string, string> rawItems,
        out int syncedSlots)
    {
        if (!string.IsNullOrWhiteSpace(accountName)) cfg.PoeAccountName = accountName.Trim();
        cfg.PoeCharacterName = ch.name;
        cfg.PoeCharacterClass = ch.@class;
        cfg.PoeCharacterLevel = Math.Max(1, ch.level);
        cfg.LastKnownLevel = Math.Max(1, ch.level);
        if (!string.IsNullOrWhiteSpace(ch.league))
            cfg.TradeLeague = ch.league;

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in rawItems)
        {
            string slot = kv.Key switch
            {
                "Weapon" or "Weapon1" => "Weapon1",
                "Weapon2" => "Weapon2",
                "Offhand" or "Offhand1" => "Offhand1",
                "Offhand2" => "Offhand2",
                "Helm" or "Helm1" => "Helm1",
                "BodyArmour" or "BodyArmour1" => "BodyArmour1",
                "Gloves" or "Gloves1" => "Gloves1",
                "Boots" or "Boots1" => "Boots1",
                "Amulet" or "Amulet1" => "Amulet1",
                "Ring" or "Ring1" => "Ring1",
                "Ring2" => "Ring2",
                "Belt" or "Belt1" => "Belt1",
                _ => kv.Key
            };
            if (!normalized.ContainsKey(slot) || kv.Value.Length > normalized[slot].Length)
                normalized[slot] = kv.Value;
        }
        cfg.WornGearBySlot = normalized;
        syncedSlots = normalized.Count;
    }

    public static BuildProfile CreateLiveAccountBuildProfile(
        BuildProfileManager mgr,
        PoeAccountCharacter ch,
        Dictionary<string, string> rawItems,
        List<string> passiveHashes)
    {
        var existing = mgr.Profiles.FirstOrDefault(p =>
            p.Name.Equals($"[Account] {ch.name}", StringComparison.OrdinalIgnoreCase));
        if (existing != null) mgr.DeleteProfile(existing.Id);

        var stage = new BuildStageEntry
        {
            Name = $"Lv {ch.level} — {ch.name} ({ch.@class})",
            MinLevel = Math.Max(1, ch.level),
            MaxLevel = 100,
            PassiveIds = [.. passiveHashes],
            GearSignatures = rawItems.Select(kv => $"{kv.Key}: {kv.Value.Split('\n').FirstOrDefault()}").ToList(),
            GearDetailsBySlot = new(rawItems, StringComparer.OrdinalIgnoreCase),
            Notes = Loc.T($"Синхронізовано з PoE акаунта ({ch.league}).", $"Synced from live PoE 2 Account ({ch.league}).", $"Синхронизировано с PoE аккаунта ({ch.league}).")
        };

        var prof = new BuildProfile
        {
            Name = $"[Account] {ch.name}",
            Author = ch.league,
            ClassName = ch.@class,
            Ascendancy = ch.@class,
            Stages = [stage],
            TargetGearHints = stage.GearSignatures,
            SummaryNotes = Loc.T($"Персонаж {ch.name}, рівень {ch.level}, ліга {ch.league}.", $"Character {ch.name}, Lvl {ch.level}, League {ch.league}.", $"Персонаж {ch.name}, уровень {ch.level}, лига {ch.league}.")
        };
        mgr.Profiles.Add(prof);
        mgr.SelectProfile(prof.Id);
        mgr.SaveProfiles();
        return prof;
    }
}
