using System.Text.Json;
using System.Text.RegularExpressions;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed class PoeAccountCharacter
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string league { get; set; } = "";
    public string @class { get; set; } = "";
    public int level { get; set; }
    public long experience { get; set; }
    public string realm { get; set; } = "";
    public bool expired { get; set; }
}

public sealed class PoeAccountSkillDto
{
    public string mainSkill { get; set; } = "";
    public List<string> supports { get; set; } = [];
    public int level { get; set; } = 1;
    public int quality { get; set; }
    public string summaryText { get; set; } = "";
}

public sealed class PoeAccountSyncPayload
{
    public bool ok { get; set; }
    public string error { get; set; } = "";
    public string accountName { get; set; } = "";
    public string latestCharacterId { get; set; } = "";
    public PoeAccountCharacter? character { get; set; }
    public List<PoeAccountCharacter> characters { get; set; } = [];
    public Dictionary<string, string> equippedBySlot { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PoeAccountSkillDto> skillGroups { get; set; } = [];
}

/// <summary>
/// Повна синхронізація PoE 2 акаунта через авторизовану сесію pathofexile2.com / pathofexile.com:
/// витягує акаунт, список персонажів PoE 2, точний рівень, досвід, лігу, весь екіп (включно з 2-м набором зброї,
/// 3-м кільцем, фласками та чармами), сокетовані руни та скіли + камені підтримки.
/// </summary>
public static partial class PoeAccountService
{
    private const string SharedJsApiPreamble = """
  const getVueCtx = async () => {
    try {
      const app = document.querySelector('#app')?.__vue_app__;
      const pinia = app?.config?.globalProperties?.$pinia;
      const router = app?.config?.globalProperties?.$router;
      const provides = app?._context?.provides || {};
      let sess = null;
      for (const sym of Object.getOwnPropertySymbols(provides)) {
        if (sym.description === 'session') { sess = provides[sym]; break; }
      }
      if (sess?.ready) { try { await sess.ready(); } catch {} }
      const dpop = sess?.getDPoPToken?.()?.value || '';
      const loggedIn = Boolean(sess?.loggedIn?.value || dpop);
      const headers = dpop ? { Authorization: 'DPoP ' + dpop } : {};
      return {
        pinia,
        router,
        sess,
        loggedIn,
        accStore: pinia?._s?.get('common.account') || null,
        charStore: pinia?._s?.get('common.characters') || null,
        headers
      };
    } catch { return { pinia: null, router: null, sess: null, loggedIn: false, accStore: null, charStore: null, headers: {} }; }
  };

  const makeSafeJson = (headers) => async (url, withAuth = false) => {
    try {
      const opts = withAuth && headers?.Authorization
        ? { credentials: 'include', headers }
        : { credentials: 'include' };
      const r = await fetch(url, opts);
      if (!r.ok) return null;
      const ct = (r.headers.get('content-type') || '').toLowerCase();
      if (!ct.includes('json')) {
        const txt = await r.text();
        const s = txt.trim();
        if (!s.startsWith('{') && !s.startsWith('[')) return null;
        return JSON.parse(s);
      }
      return await r.json();
    } catch { return null; }
  };
""";

    public const string FetchCharactersAndDataScript = $$"""
(async () => {
{{SharedJsApiPreamble}}
  try {
    let ctx = await getVueCtx();
    let safeJson = makeSafeJson(ctx.headers);

    // Якщо користувач залогінений на pathofexile2.com, але ще не відкрив /my-account/characters,
    // робимо миттєвий SPA-перехід через Vue $router без перезавантаження сторінки!
    if (ctx.loggedIn && !ctx.charStore && ctx.router?.push && location.hostname.includes('pathofexile2.com')) {
      try {
        await ctx.router.push('/my-account/characters');
        for (let w = 0; w < 24; w++) {
          await new Promise(r => setTimeout(r, 150));
          ctx = await getVueCtx();
          if (ctx.charStore && Array.isArray(ctx.charStore.currentCharacters)) break;
        }
        safeJson = makeSafeJson(ctx.headers);
      } catch {}
    }

    let accName = '';
    if (ctx.accStore) {
      try {
        if (!ctx.accStore.currentAccount && ctx.accStore.loadCurrentAccount)
          await ctx.accStore.loadCurrentAccount('7ijg');
        const ca = ctx.accStore.currentAccount;
        if (ca?.name) accName = ca.name + (ca.discriminator ? '#' + ca.discriminator : '');
      } catch {}
    }
    if (!accName) {
      const myAcc = await safeJson('/internal-api/my-account', true) || await safeJson('https://www.pathofexile.com/api/profile');
      if (myAcc) {
        const d = myAcc.data || myAcc;
        accName = d.name || d.username || '';
      }
    }
    if (!accName) {
      const profEl = document.querySelector('a[href*="/account/view-profile/"], .profile-link a, .account-name');
      accName = profEl ? (profEl.innerText || '').trim() : '';
      if (!accName && profEl && profEl.getAttribute) {
        const m = (profEl.getAttribute('href') || '').match(/view-profile\/([^/?#]+)/i);
        if (m) accName = decodeURIComponent(m[1]);
      }
    }

    const poe1Only = new Set([
      'scion','ascendant','duelist','slayer','gladiator','champion',
      'shadow','assassin','saboteur','trickster','templar','inquisitor','hierophant','guardian',
      'marauder','juggernaut','berserker','chieftain','necromancer','occultist','elementalist','raider','warden'
    ]);

    let allChars = [];
    let latestId = '';

    // 1. Рідний Vue Pinia store (автоматично виконує JWT scope upgrade + DPoP на pathofexile2.com)
    if (ctx.charStore) {
      try {
        if (!Array.isArray(ctx.charStore.currentCharacters) && ctx.charStore.loadCurrentCharacters)
          await ctx.charStore.loadCurrentCharacters('7ijg');
        if (Array.isArray(ctx.charStore.currentCharacters))
          allChars = ctx.charStore.currentCharacters;
        latestId = ctx.charStore.latestCharacter?.id || '';
      } catch {}
    }

    // 2. Прямий /internal-api з DPoP токеном
    if (allChars.length === 0) {
      const v2 = await safeJson('/internal-api/my-account/characters?realm=poe2&meta=latestCharacterId', true);
      if (v2 && Array.isArray(v2.data)) {
        allChars = v2.data;
        latestId = v2.meta?.latestCharacterId || '';
      }
    }

    // 3. Резерв: класичний API character-window на pathofexile.com
    if (allChars.length === 0) {
      const cwUrls = [
        '/character-window/get-characters?realm=poe2',
        'https://www.pathofexile.com/character-window/get-characters?realm=poe2',
        accName ? ('https://www.pathofexile.com/character-window/get-characters?realm=poe2&accountName=' + encodeURIComponent(accName)) : null
      ].filter(Boolean);
      for (const u of cwUrls) {
        const arr = await safeJson(u);
        if (Array.isArray(arr) && arr.length > 0) { allChars = arr; break; }
      }
    }

    if (allChars.length === 0) {
      return JSON.stringify({
        ok: false,
        accountName: accName,
        error: 'Sign in on pathofexile2.com (or open My Account -> Characters) and click Load Characters.'
      });
    }

    const explicitPoe2 = allChars.filter(c => c && String(c.realm || '').toLowerCase() === 'poe2');
    const filtered = explicitPoe2.length > 0
      ? explicitPoe2
      : allChars.filter(c => c && String(c.realm || '').toLowerCase() !== 'pc' && !poe1Only.has(String(c.class || '').toLowerCase()));

    if (filtered.length === 0) {
      return JSON.stringify({
        ok: false,
        accountName: accName,
        error: 'Returned only PoE 1 characters (' + allChars.length + '). Ensure PoE 2 characters are visible on your account.'
      });
    }

    return JSON.stringify({ ok: true, accountName: accName, latestCharacterId: latestId, characters: filtered });
  } catch (e) {
    return JSON.stringify({ ok: false, error: String(e) });
  }
})();
""";

    public static string BuildFetchCharacterDetailsScript(string characterName, string characterId, string accountName)
    {
        string safeChar = JsonSerializer.Serialize(characterName);
        string safeId = JsonSerializer.Serialize(characterId ?? "");
        string safeAcc = JsonSerializer.Serialize(accountName ?? "");
        return $$"""
(async () => {
{{SharedJsApiPreamble}}
  const ctx = await getVueCtx();
  const safeJson = makeSafeJson(ctx.headers);

  const rarityName = (ft) => ({ 0:'Normal', 1:'Magic', 2:'Rare', 3:'Unique', 4:'Gem', 5:'Currency', 9:'Relic' }[ft] || 'Rare');

  const guessClass = (inv, it) => {
    if (it.baseType && /Spear/i.test(it.baseType)) return 'Spears';
    if (it.typeLine && /Quarterstaff/i.test(it.typeLine)) return 'Quarterstaves';
    if (it.typeLine && /Crossbow/i.test(it.typeLine)) return 'Crossbows';
    if (it.typeLine && /Sceptre/i.test(it.typeLine)) return 'Sceptres';
    if (it.typeLine && /Wand/i.test(it.typeLine)) return 'Wands';
    if (it.typeLine && /Bow/i.test(it.typeLine)) return 'Bows';
    if (it.typeLine && /Staff/i.test(it.typeLine)) return 'Staves';
    if (it.typeLine && /Focus/i.test(it.typeLine)) return 'Foci';
    if (it.typeLine && /Buckler/i.test(it.typeLine)) return 'Bucklers';
    if (it.typeLine && /Shield/i.test(it.typeLine)) return 'Shields';
    if (it.typeLine && /Quiver/i.test(it.typeLine)) return 'Quivers';
    if (it.typeLine && /Charm/i.test(it.typeLine)) return 'Charms';
    if (inv.startsWith('Helm')) return 'Helmets';
    if (inv.startsWith('BodyArmour')) return 'Body Armours';
    if (inv.startsWith('Gloves')) return 'Gloves';
    if (inv.startsWith('Boots')) return 'Boots';
    if (inv.startsWith('Amulet')) return 'Amulets';
    if (inv.startsWith('Ring')) return 'Rings';
    if (inv.startsWith('Belt')) return 'Belts';
    if (inv.startsWith('Flask')) return (it.typeLine || '').includes('Mana') ? 'Mana Flasks' : 'Life Flasks';
    if (it.typeLine && /Jewel/i.test(it.typeLine)) return 'Jewels';
    return inv;
  };

  const cleanTag = (s) => String(s ?? '')
    .replace(/<[^>]+>\{([^}]*)\}/g, '$1')
    .replace(/\[(?:[^|\]]*\|)?([^\]]+)\]/g, '$1')
    .trim();
  const extractModText = (m) => {
    if (typeof m === 'string') return cleanTag(m);
    if (m && typeof m === 'object') return cleanTag(m.description || m.text || m.name || '');
    return '';
  };
  const fmtProp = (p) => {
    if (!p || !p.name) return '';
    const nm = cleanTag(p.name);
    const vals = Array.isArray(p.values)
      ? p.values.map(v => Array.isArray(v) ? cleanTag(v[0]) : cleanTag(v)).filter(Boolean)
      : [];
    if (/\{\d+\}/.test(nm)) {
      return nm.replace(/\{(\d+)\}/g, (_, idx) => vals[Number(idx)] ?? '').trim();
    }
    const vStr = vals.join(', ');
    return vStr ? (nm + ': ' + vStr) : nm;
  };
  const fmtItem = (it, slotOverride) => {
    const inv = slotOverride || it.inventoryId || '';
    const lines = [];
    lines.push('Item Class: ' + guessClass(inv, it));
    lines.push('Rarity: ' + rarityName(it.frameType));
    if (it.name) lines.push(cleanTag(it.name));
    if (it.typeLine) lines.push(cleanTag(it.typeLine));
    lines.push('--------');
    const props = (it.properties || []).map(fmtProp).filter(Boolean);
    if (props.length) { lines.push(...props); lines.push('--------'); }
    if (Array.isArray(it.requirements) && it.requirements.length) {
      const reqParts = it.requirements.map(r => {
        const rn = cleanTag(r?.name);
        const rv = Array.isArray(r?.values) && r.values[0] ? cleanTag(r.values[0][0]) : '';
        if (!rn || !rv) return rv || rn;
        return /^Level$/i.test(rn) ? ('Level ' + rv) : (rv + ' ' + rn);
      }).filter(Boolean);
      if (reqParts.length) { lines.push('Requires: ' + reqParts.join(', ')); lines.push('--------'); }
    }
    if (it.ilvl) { lines.push('Item Level: ' + it.ilvl); lines.push('--------'); }
    const addBlock = (arr, suffix = '') => {
      if (!Array.isArray(arr) || !arr.length) return;
      const cleaned = arr.map(extractModText).filter(Boolean).map(t => suffix ? (t + suffix) : t);
      if (cleaned.length) { lines.push(...cleaned); lines.push('--------'); }
    };
    addBlock(it.enchantMods);
    addBlock(it.utilityMods);
    addBlock(it.implicitMods);
    addBlock(it.runeMods);
    if (!(Array.isArray(it.runeMods) && it.runeMods.length) && Array.isArray(it.socketedItems)) {
      const runes = it.socketedItems.filter(s => s && s.support === undefined && !/Gem/i.test(s.typeLine || '') && s.frameType !== 4);
      for (const r of runes) {
        const rMods = [].concat(r.explicitMods || [], r.runeMods || []).map(extractModText).filter(Boolean);
        if (rMods.length) addBlock(rMods, ' (rune)');
      }
    }
    addBlock(it.fracturedMods);
    addBlock(it.desecratedMods);
    addBlock(it.craftedMods);
    addBlock(it.explicitMods);
    if (it.corrupted) lines.push('Corrupted');
    return lines.join('\n').trim();
  };

  const extractSkillsFromItems = (items) => {
    const out = [];
    const seen = new Set();
    const parseGemProp = (g, name) => {
      const p = (g.properties || []).find(x => x.name && x.name.toLowerCase() === name);
      if (!p || !p.values || !p.values[0]) return 0;
      const m = String(p.values[0][0]).match(/\d+/);
      return m ? parseInt(m[0], 10) : 0;
    };

    const pushSkill = (mainName, lvl, qual, supps) => {
      if (!mainName) return;
      const key = mainName + '|' + supps.join(',');
      if (seen.has(key)) return;
      seen.add(key);
      out.push({
        mainSkill: mainName,
        supports: supps,
        level: lvl,
        quality: qual,
        summaryText: mainName + ' (Lv' + lvl + (qual > 0 ? ' Q' + qual + '%' : '') + ')' + (supps.length ? ' -> ' + supps.join(', ') : '')
      });
    };

    for (const it of items) {
      if (!it) continue;
      // Випадок А: окремі скіли/контейнери скілів PoE 2 або socketedItems
      const sock = Array.isArray(it.socketedItems) ? it.socketedItems : [];
      const gemChildren = sock.filter(s => s && (s.support !== undefined || s.frameType === 4 || /Gem/i.test(s.typeLine || '')));
      const isSkillItem = it.frameType === 4 || it.support === false || (it.inventoryId && /Skill/i.test(it.inventoryId));
      if (isSkillItem && it.typeLine) {
        const supps = gemChildren.map(g => g.typeLine || g.baseType).filter(Boolean);
        pushSkill((it.typeLine || it.baseType || '').replace(/\s+Gem$/i, '').trim(), parseGemProp(it, 'level') || 1, parseGemProp(it, 'quality') || 0, supps);
      } else if (gemChildren.length > 0) {
        const actives = gemChildren.filter(g => !g.support);
        const supps = gemChildren.filter(g => g.support).map(g => g.typeLine || g.baseType).filter(Boolean);
        for (const a of actives)
          pushSkill((a.typeLine || a.baseType || '').trim(), parseGemProp(a, 'level') || 1, parseGemProp(a, 'quality') || 0, supps);
      }
    }
    return out;
  };

  try {
    const cName = {{safeChar}};
    const cId = {{safeId}};
    const acc = {{safeAcc}};
    const qAcc = acc ? ('&accountName=' + encodeURIComponent(acc)) : '';

    // 1. Рідний Vue Pinia loadCharacter (з автоматичним upgrade JWT + DPoP) -> fallback DPoP fetch
    let v2Char = null;
    if (ctx.charStore?.loadCharacter && (cId || cName)) {
      try { v2Char = await ctx.charStore.loadCharacter('zQ1U', cId || cName); } catch {}
    }
    if (!v2Char && cId) v2Char = await safeJson('/internal-api/my-account/character/' + encodeURIComponent(cId) + '?realm=poe2', true);
    if (!v2Char && cName) v2Char = await safeJson('/internal-api/my-account/character/' + encodeURIComponent(cName) + '?realm=poe2', true);
    if (v2Char && v2Char.data) v2Char = v2Char.data;
    if (v2Char && v2Char.character) v2Char = v2Char.character;

    // Примітка: www.pathofexile.com/character-window/get-items підтримує лише PoE 1 (для PoE 2 повертає 404/CORS)
    const allItems = []
      .concat(Array.isArray(v2Char?.equipment) ? v2Char.equipment : [])
      .concat(Array.isArray(v2Char?.inventory) ? v2Char.inventory : []);

    const equippedBySlot = {};
    for (const it of allItems) {
      if (!it) continue;
      let inv = it.inventoryId || '';
      if (!inv || inv === 'MainInventory') continue;
      if (inv === 'Flask') {
        const x = Number(it.x ?? 0);
        inv = x >= 2 ? ('Charm' + (x - 1)) : ('Flask' + (x + 1));
      } else if (inv === 'Chakra') {
        inv = 'Chakra' + (Number(it.x ?? 0) + 1);
      }
      if (/Skill/i.test(inv)) continue;
      const txt = fmtItem(it, inv);
      if (!equippedBySlot[inv] || txt.length > equippedBySlot[inv].length)
        equippedBySlot[inv] = txt;
    }

    const extraGranted = [];
    for (const eq of allItems) {
      if (!eq || !Array.isArray(eq.grantedSkills)) continue;
      const existingNames = new Set((eq.socketedItems || []).map(s => String(s?.typeLine || s?.baseType || '').toLowerCase()).filter(Boolean));
      for (const gs of eq.grantedSkills) {
        const gName = Array.isArray(gs?.values) && Array.isArray(gs.values[0]) ? String(gs.values[0][0] || '').trim() : '';
        if (gName && !existingNames.has(gName.toLowerCase())) {
          extraGranted.push({ typeLine: gName, baseType: gName, frameType: 4, properties: [{ name: 'Level', values: [['1', 0]] }] });
        }
      }
    }

    const skillGroups = extractSkillsFromItems(allItems.concat(Array.isArray(v2Char?.skills) ? v2Char.skills : []).concat(extraGranted));

    return JSON.stringify({
      ok: true,
      character: v2Char ? {
        id: v2Char.id || cId || '',
        name: v2Char.name || cName,
        league: v2Char.league || '',
        class: v2Char.class || '',
        level: v2Char.level || 0,
        experience: v2Char.experience || 0,
        realm: v2Char.realm || 'poe2'
      } : null,
      equippedBySlot,
      skillGroups
    });
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
        PoeAccountSyncPayload details,
        out int syncedSlots)
    {
        if (!string.IsNullOrWhiteSpace(accountName)) cfg.PoeAccountName = accountName.Trim();
        if (!string.IsNullOrWhiteSpace(ch.id)) cfg.PoeCharacterId = ch.id;
        cfg.PoeCharacterName = ch.name;
        cfg.PoeCharacterClass = ch.@class;
        int lvl = Math.Max(1, details.character?.level > 0 ? details.character.level : ch.level);
        cfg.PoeCharacterLevel = lvl;
        cfg.LastKnownLevel = lvl;
        cfg.PoeCharacterExperience = details.character?.experience > 0 ? details.character.experience : ch.experience;
        cfg.PoeLastSyncUtc = DateTime.UtcNow;

        string league = !string.IsNullOrWhiteSpace(details.character?.league) ? details.character!.league : ch.league;
        if (!string.IsNullOrWhiteSpace(league))
            cfg.TradeLeague = league;

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in details.equippedBySlot)
        {
            string slot = NormalizeSlotKey(kv.Key);
            if (!normalized.ContainsKey(slot) || kv.Value.Length > normalized[slot].Length)
                normalized[slot] = kv.Value;
        }
        cfg.WornGearBySlot = normalized;
        cfg.PoeCharacterSkills = details.skillGroups.Select(s => s.summaryText).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        cfg.PoeCharacterStatsSummary = ComputeCharacterStatsSummary(normalized, cfg.PoeCharacterSkills.Count);
        syncedSlots = normalized.Count;
    }

    public static string NormalizeSlotKey(string rawKey) => rawKey switch
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
        "Ring3" => "Ring3",
        "Trinket" or "Trinket1" => "Trinket1",
        "Belt" or "Belt1" => "Belt1",
        _ => rawKey
    };

    /// <summary>
    /// Прибирає старі псевдо-профілі "[Account] ..." зі списку білдів (акаунт персонажа зберігається окремо від білд-гайдів).
    /// </summary>
    public static int RemoveAccountBuildProfiles(BuildProfileManager mgr)
    {
        var toDelete = mgr.Profiles
            .Where(p => p.SourceKind == BuildSourceKind.AccountSync || p.Name.StartsWith("[Account]", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Id)
            .ToList();
        foreach (var id in toDelete)
            mgr.DeleteProfile(id);
        return toDelete.Count;
    }

    public static string InferCharacterWeaponClass(IReadOnlyDictionary<string, string> gear)
    {
        if (gear.TryGetValue("Weapon1", out string? w1) && !string.IsNullOrWhiteSpace(w1))
        {
            var m = WeaponClassRegex().Match(w1);
            if (m.Success) return m.Groups[1].Value.Trim();
        }
        return "";
    }

    public static string ComputeCharacterStatsSummary(IReadOnlyDictionary<string, string> gear, int skillsCount)
    {
        double es = 0, ev = 0, ar = 0, spirit = 0, life = 0, fRes = 0, cRes = 0, lRes = 0, chRes = 0;
        foreach (var kv in gear)
        {
            if (kv.Key.StartsWith("Weapon2", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.StartsWith("Offhand2", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.StartsWith("Flask", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.StartsWith("Charm", StringComparison.OrdinalIgnoreCase))
                continue;

            string t = kv.Value;
            es += ExtractSum(@"Energy Shield:\s*(\d+)", t);
            ev += ExtractSum(@"Evasion Rating:\s*(\d+)", t);
            ar += ExtractSum(@"Armour:\s*(\d+)", t);
            spirit += ExtractSum(@"Spirit:\s*\+?(\d+)", t) + ExtractSum(@"([+\-]?\d+) to Spirit", t);
            life += ExtractSum(@"([+\-]?\d+) to maximum Life", t);

            double allEle = ExtractSum(@"([+\-]?\d+)% to all Elemental Resistances", t);
            fRes += allEle + ExtractSum(@"([+\-]?\d+)% to Fire Resistance", t);
            cRes += allEle + ExtractSum(@"([+\-]?\d+)% to Cold Resistance", t);
            lRes += allEle + ExtractSum(@"([+\-]?\d+)% to Lightning Resistance", t);
            chRes += ExtractSum(@"([+\-]?\d+)% to Chaos Resistance", t);
        }

        var parts = new List<string>();
        if (es > 0) parts.Add($"ES {es:F0}");
        if (ev > 0) parts.Add($"EV {ev:F0}");
        if (ar > 0) parts.Add($"AR {ar:F0}");
        if (life > 0) parts.Add($"+Life {life:F0}");
        if (spirit > 0) parts.Add($"Spirit +{spirit:F0}");
        if (fRes > 0 || cRes > 0 || lRes > 0)
            parts.Add($"Res {fRes:F0}/{cRes:F0}/{lRes:F0}" + (chRes > 0 ? $" (Ch {chRes:F0})" : ""));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Генерує детальні поради по поточному етапу персонажа (акт, квестові бонуси, просілі слоти, резисти, чоботи та зброя).
    /// </summary>
    public static string BuildStageAdvice(
        int level,
        string className,
        string league,
        string weaponClass,
        IReadOnlyList<string> skills,
        IReadOnlyDictionary<string, string> gear)
    {
        int lv = Math.Clamp(level, 1, 100);
        var tips = new List<string>();

        string actTip = lv switch
        {
            <= 16 => Loc.T(
                $"• Етап (Рів.{lv}, Акт 1 — {league}): шукай нагороди в Clearfell (Beira +10% Cold Res), Hunting Grounds (+2 пасивки), Freythorn (+30 Spirit) і Ogham Farmlands (+2 пасивки).",
                $"• Act 1 Stage (Lv {lv}, {league}): Grab Beira (+10% Cold Res), Crowbell (+2 Passives), Freythorn (+30 Spirit) & Ogham Lute (+2 Passives).",
                $"• Акт 1 (Ур.{lv}, {league}): забери Beira (+10% Cold Res), Crowbell (+2 пассивки), Freythorn (+30 Spirit) и лютню в Ogham (+2 пассивки)."),
            <= 32 => Loc.T(
                $"• Етап (Рів.{lv}, Акт 2 — {league}): відкрий 1-й Ascendancy в Traitors' Passage, збери Keth (+2 пасивки), Deshar (+2 пасивки) та Spires of Deshar (+10% Light Res).",
                $"• Act 2 Stage (Lv {lv}, {league}): Unlock 1st Ascendancy (Traitors' Passage), Keth (+2 Passives), Deshar (+2 Passives) & Sisters (+10% Light Res).",
                $"• Акт 2 (Ур.{lv}, {league}): открой 1-е Восхождение в Traitors' Passage, Keth (+2 пассивки), Deshar (+2) и Sisters (+10% Light Res)."),
            <= 46 => Loc.T(
                $"• Етап (Рів.{lv}, Акт 3 — {league}): не пропусти Azak Bog (+30 Spirit), Jungle Ruins (+2 пасивки), Venom Crypts (Servi Vial), Jiquani (+10% Fire Res) і Trial of Chaos.",
                $"• Act 3 Stage (Lv {lv}, {league}): Grab Azak Bog (+30 Spirit), Jungle Ruins (+2 Passives), Venom Crypts (Servi Vial), Jiquani (+10% Fire Res) & Trial of Chaos.",
                $"• Акт 3 (Ур.{lv}, {league}): забери Azak Bog (+30 Spirit), Jungle Ruins (+2 пассивки), Servi Vial, Jiquani (+10% Fire Res) и Trial of Chaos."),
            <= 64 => Loc.T(
                $"• Акт 4 / Interludes (Рів.{lv} — {league}): забери Lythara в Interlude 3 Kriar Village (+40 Spirit, разом 100 бази!), добий резисти до ~75% та онови базу перед Waystones.",
                $"• Act 4 / Interludes (Lv {lv}, {league}): Grab Lythara in Interlude 3 Kriar Village (+40 Spirit, 100 base total!), push Ele Res toward 75% & upgrade weapon before Waystones.",
                $"• Акт 4 / Interludes (Ур.{lv}, {league}): забери Lythara в Interlude 3 Kriar Village (+40 Spirit = 100 базы!), подтяни резисты к 75% и обнови оружие."),
            _ => Loc.T(
                $"• Ендгейм Waystones (Рів.{lv} — {league}): тримай Fire/Cold/Light Res 75%+, шукай ilvl 78–82+ бази, Artificer's Orbs на 2 сокети та +3..+5 Skills / високий pDPS+eDPS.",
                $"• Endgame Waystones (Lv {lv}, {league}): Cap 75%+ Elemental Res, hunt ilvl 78–82+ bases, 2-socket Armour/Weapon with Runes & +Skill levels.",
                $"• Эндгейм Waystones (Ур.{lv}, {league}): держи 75%+ резистов, ищи ilvl 78–82+ базы, сокеты Artificer's Orb и топ-аффиксы.")
        };
        tips.Add(actTip);

        // Діагностика реально вдягнутих слотів
        string[] checkSlots = ["Weapon1", "Helm1", "BodyArmour1", "Gloves1", "Boots1", "Amulet1", "Ring1", "Ring2", "Belt1"];
        var emptySlots = checkSlots
            .Where(s => !gear.TryGetValue(s, out var txt) || string.IsNullOrWhiteSpace(txt))
            .Select(PoeTermLocalizer.SlotName)
            .ToList();
        if (emptySlots.Count > 0)
        {
            tips.Add(Loc.T(
                $"• Порожні слоти екіпу: {string.Join(", ", emptySlots)} — обов'язково закрий їх речами з торговців/скринь під атрибути (Str/Dex/Int) та резисти.",
                $"• Empty gear slots: {string.Join(", ", emptySlots)} — fill with vendor/chest drops matching required Attributes and Resistances.",
                $"• Пустые слоты экипа: {string.Join(", ", emptySlots)} — заполни вещами от вендоров с атрибутами и резистами."));
        }

        // Резисти та швидкість бігу на чоботях
        double fRes = 0, cRes = 0, lRes = 0, bootsMs = 0;
        foreach (var kv in gear)
        {
            if (kv.Key.StartsWith("Weapon2") || kv.Key.StartsWith("Offhand2") || kv.Key.StartsWith("Flask")) continue;
            double all = ExtractSum(@"([+\-]?\d+)% to all Elemental Resistances", kv.Value);
            fRes += all + ExtractSum(@"([+\-]?\d+)% to Fire Resistance", kv.Value);
            cRes += all + ExtractSum(@"([+\-]?\d+)% to Cold Resistance", kv.Value);
            lRes += all + ExtractSum(@"([+\-]?\d+)% to Lightning Resistance", kv.Value);
            if (kv.Key.Equals("Boots1", StringComparison.OrdinalIgnoreCase))
                bootsMs = ExtractSum(@"(\d+(?:\.\d+)?)%\s+increased Movement Speed", kv.Value);
        }

        double minEle = Math.Min(fRes, Math.Min(cRes, lRes));
        if (lv >= 20 && minEle < 45 || lv < 20 && (fRes + cRes + lRes) < 20)
        {
            tips.Add(Loc.T(
                $"• Опори зараз: Fire {fRes:0}% / Cold {cRes:0}% / Light {lRes:0}% — за кожної нагоди вставляй Glacial/Desert/Storm Runes через Artificer's Orb та бери кільця з резистами.",
                $"• Current Resists: Fire {fRes:0}% / Cold {cRes:0}% / Light {lRes:0}% — socket Runes with Artificer's Orbs and prioritize resist rings.",
                $"• Текущие резисты: Fire {fRes:0}% / Cold {cRes:0}% / Light {lRes:0}% — вставляй руны и добирай резисты на кольцах."));
        }

        if (bootsMs < 10)
        {
            tips.Add(Loc.T(
                "• Чоботи: на вдягнутих чоботях не видно +% Movement Speed — перевіряй торговців у місті щоразу при підвищенні рівня (шукай 10–25% MS!).",
                "• Boots: No Movement Speed mod detected — check town vendors on every level-up for 10–25% Movement Speed!",
                "• Сапоги: нет +% Movement Speed — проверяй торговцев при каждом апе уровня ради 10–25% MS!"));
        }

        // Порада по зброї й скілах
        string wepLabel = PoeTermLocalizer.WeaponClassLabel(weaponClass);
        string wepAdvice = lv < 22
            ? Loc.T(
                $"• Зброя ({wepLabel}): що 6–8 рівнів бери нову базу в торговця (Renly / Finn), шукай +% Physical або Flat Added Damage + Iron Rune у сокет.",
                $"• Weapon ({wepLabel}): Refresh vendor base every 6–8 levels; prioritize Flat Added Damage, +% Phys & socket an Iron Rune.",
                $"• Оружие ({wepLabel}): обновляй базу каждые 6–8 уровней у вендора (плоский урон, +% физ + Iron Rune).")
            : Loc.T(
                $"• Зброя та урон ({wepLabel}, {className}): апгрейдь базу під поточний ilvl, тримай 2 руни в сокетах і качай пасивки на пріоритетний тип урону.",
                $"• Weapon & Scaling ({wepLabel}, {className}): Upgrade weapon base regularly and socket Iron/Elemental Runes.",
                $"• Оружие и урон ({wepLabel}, {className}): обновляй базу под текущий уровень и держи сокеты с рунами.");
        tips.Add(wepAdvice);

        if (skills.Count > 0)
        {
            tips.Add(Loc.T(
                $"• Активні скіли ({skills.Count}): {string.Join(" | ", skills.Take(4))} — вставляй Uncut Support Gems першочергово в основний атакуючий скіл.",
                $"• Synced Skills ({skills.Count}): {string.Join(" | ", skills.Take(4))} — focus Uncut Support Gems on your main DPS skill first.",
                $"• Синхронизированные скиллы ({skills.Count}): {string.Join(" | ", skills.Take(4))} — усиливай Uncut Support камнями основной скилл."));
        }

        return string.Join("\n", tips);
    }

    /// <summary>
    /// Коротко витягує ключові цифри предмета (DPS / ES / EV / AR / Life / Res / MS%) для показу в картках оверлея.
    /// </summary>
    public static string SummarizeItemStatsCompact(string rawItemText)
    {
        if (string.IsNullOrWhiteSpace(rawItemText)) return "";
        var tags = new List<string>();

        double aps = ExtractFirst(@"Attacks per Second:\s*([\d.]+)", rawItemText);
        var phys = Regex.Match(rawItemText, @"Physical Damage:\s*(\d+)\s*-\s*(\d+)", RegexOptions.IgnoreCase);
        double pdps = aps > 0 && phys.Success ? (int.Parse(phys.Groups[1].Value) + int.Parse(phys.Groups[2].Value)) / 2.0 * aps : 0;
        double edps = 0;
        if (aps > 0)
        {
            foreach (Match em in Regex.Matches(rawItemText, @"(?:Elemental|Fire|Cold|Lightning|Chaos)\s+Damage:\s*([^\r\n]+)", RegexOptions.IgnoreCase))
            {
                foreach (Match r in Regex.Matches(em.Groups[1].Value, @"(\d+)\s*-\s*(\d+)"))
                    edps += (int.Parse(r.Groups[1].Value) + int.Parse(r.Groups[2].Value)) / 2.0 * aps;
            }
        }
        if (pdps + edps > 1) tags.Add($"~{pdps + edps:F0} DPS");

        double es = ExtractSum(@"Energy Shield:\s*(\d+)", rawItemText);
        double ev = ExtractSum(@"Evasion Rating:\s*(\d+)", rawItemText);
        double ar = ExtractSum(@"Armour:\s*(\d+)", rawItemText);
        if (es > 0) tags.Add($"ES {es:0}");
        if (ev > 0) tags.Add($"EV {ev:0}");
        if (ar > 0) tags.Add($"AR {ar:0}");

        double ms = ExtractSum(@"(\d+(?:\.\d+)?)%\s+increased Movement Speed", rawItemText);
        if (ms > 0) tags.Add($"+{ms:0}% MS");

        double life = ExtractSum(@"([+\-]?\d+) to maximum Life", rawItemText);
        if (life > 0) tags.Add($"+{life:0} Life");

        double spirit = ExtractSum(@"Spirit:\s*\+?(\d+)", rawItemText) + ExtractSum(@"([+\-]?\d+) to Spirit", rawItemText);
        if (spirit > 0) tags.Add($"+{spirit:0} Spirit");

        double fRes = ExtractSum(@"([+\-]?\d+)% to Fire Resistance", rawItemText);
        double cRes = ExtractSum(@"([+\-]?\d+)% to Cold Resistance", rawItemText);
        double lRes = ExtractSum(@"([+\-]?\d+)% to Lightning Resistance", rawItemText);
        double allRes = ExtractSum(@"([+\-]?\d+)% to all Elemental Resistances", rawItemText);
        double totRes = fRes + cRes + lRes + allRes * 3;
        if (totRes > 0) tags.Add($"+{totRes:0}% Res");

        var skillM = Regex.Match(rawItemText, @"\+(\d+) to Level of (?:all )?([^\r\n]+?Skills?)", RegexOptions.IgnoreCase);
        if (skillM.Success) tags.Add($"+{skillM.Groups[1].Value} Skills");

        var mods = rawItemText.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("Item Class:") && !l.StartsWith("Rarity:") && !l.StartsWith("--------") &&
                        !l.StartsWith("Requires") && !l.StartsWith("Item Level:") && !l.StartsWith("Sockets:") &&
                        !l.StartsWith("Attacks per Second:") && !l.StartsWith("Critical Hit Chance:") &&
                        !l.StartsWith("Physical Damage:") && !l.StartsWith("Elemental Damage:") &&
                        !l.StartsWith("Fire Damage:") && !l.StartsWith("Cold Damage:") &&
                        !l.StartsWith("Lightning Damage:") && !l.StartsWith("Chaos Damage:") &&
                        !l.StartsWith("Quality:") && !l.StartsWith("Block chance:") &&
                        !l.StartsWith("Energy Shield:") && !l.StartsWith("Evasion Rating:") && !l.StartsWith("Armour:"))
            .Skip(1) // пропустити рядок назви/бази
            .Take(3)
            .Select(PoeTermLocalizer.StatLine)
            .ToList();

        string head = tags.Count > 0 ? $"[{string.Join(" · ", tags)}]" : "";
        string tail = mods.Count > 0 ? string.Join("; ", mods) : "";
        if (!string.IsNullOrEmpty(head) && !string.IsNullOrEmpty(tail)) return $"{head} {tail}";
        return !string.IsNullOrEmpty(head) ? head : tail;
    }

    private static double ExtractFirst(string pattern, string text)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    private static string FirstItemTitleLine(string raw)
    {
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.SkipWhile(l => l.StartsWith("Item Class:") || l.StartsWith("Rarity:")).FirstOrDefault()?.Trim() ?? "";
    }

    private static double ExtractSum(string pattern, string text)
    {
        double sum = 0;
        foreach (Match m in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
            if (double.TryParse(m.Groups[1].Value, out double v)) sum += v;
        return sum;
    }

    /// <summary>
    /// Запам'ятовує зв'язку PoeCharacterId -> BuildProfileId у локальній конфігурації.
    /// </summary>
    public static void BindCharacterToProfile(OverlayConfig cfg, string? profileId)
    {
        string charKey = !string.IsNullOrWhiteSpace(cfg.PoeCharacterId) ? cfg.PoeCharacterId.Trim() : cfg.PoeCharacterName.Trim();
        if (string.IsNullOrWhiteSpace(charKey) || string.IsNullOrWhiteSpace(profileId)) return;
        cfg.CharacterProfileLinks[charKey] = profileId.Trim();
    }

    /// <summary>
    /// При перемиканні чи синхронізації персонажа підтягує прив'язаний до нього BuildProfileId (або прив'язує вільний активний профіль).
    /// </summary>
    public static bool TryApplyLinkedProfileForCharacter(OverlayConfig cfg, BuildProfileManager profiles)
    {
        string charKey = !string.IsNullOrWhiteSpace(cfg.PoeCharacterId) ? cfg.PoeCharacterId.Trim() : cfg.PoeCharacterName.Trim();
        if (string.IsNullOrWhiteSpace(charKey)) return false;

        if (cfg.CharacterProfileLinks.TryGetValue(charKey, out string? linkedId) &&
            !string.IsNullOrWhiteSpace(linkedId) &&
            profiles.Profiles.Any(p => p.Id.Equals(linkedId, StringComparison.OrdinalIgnoreCase)))
        {
            profiles.SelectProfile(linkedId);
            cfg.ActiveProfileId = profiles.ActiveProfile?.Id ?? linkedId;
            return true;
        }

        if (profiles.ActiveProfile is { Id: var activeId } &&
            !string.IsNullOrWhiteSpace(activeId) &&
            !cfg.CharacterProfileLinks.Values.Contains(activeId, StringComparer.OrdinalIgnoreCase))
        {
            cfg.CharacterProfileLinks[charKey] = activeId;
        }
        return false;
    }

    /// <summary>
    /// Видаляє з CharacterProfileLinks посилання на вилучений профіль білду.
    /// </summary>
    public static void UnlinkDeletedProfile(OverlayConfig cfg, string? deletedProfileId)
    {
        if (string.IsNullOrWhiteSpace(deletedProfileId)) return;
        var deadKeys = cfg.CharacterProfileLinks
            .Where(kv => kv.Value.Equals(deletedProfileId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var k in deadKeys)
            cfg.CharacterProfileLinks.Remove(k);
    }

    [GeneratedRegex(@"Item Class:\s*([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WeaponClassRegex();
}
