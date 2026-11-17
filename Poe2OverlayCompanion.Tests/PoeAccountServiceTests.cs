using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion.Tests;

public sealed class PoeAccountServiceTests
{
    [Fact]
    public void BuildFetchCharacterDetailsScript_UsesPoe2ModNormalizationWithoutPoE1Cors()
    {
        string script = PoeAccountService.BuildFetchCharacterDetailsScript("CharOne", "15442196", "Tester#1234");
        Assert.Contains("extractModText", script);
        Assert.Contains("cleanTag", script);
        Assert.DoesNotContain("safeJson('/character-window/get-items", script);
    }

    [Fact]
    public void ApplySyncedCharacterToConfig_NormalizesSlotsStatsAndElementalWeaponDps()
    {
        var simPayload = new PoeAccountSyncPayload
        {
            ok = true,
            accountName = "Tester#1234",
            character = new PoeAccountCharacter { id = "15442196", name = "MonkChar", @class = "Monk", league = "Forbidden Rites", level = 7, realm = "poe2" },
            equippedBySlot = new Dictionary<string, string>
            {
                ["Helm"] = "Item Class: Helmets\nRarity: Magic\nTwig Circlet\n--------\nHelmet\nEnergy Shield: 36\n--------\nRequires: Level 2\n--------\n+17 to maximum Energy Shield",
                ["BodyArmour"] = "Item Class: Body Armours\nRarity: Magic\nLeather Vest\n--------\nBody Armour\nEvasion Rating: 30\n--------\n+9% to Fire Resistance",
                ["Gloves"] = "Item Class: Gloves\nRarity: Magic\nTorn Gloves\n--------\nGloves\nEnergy Shield: 9",
                ["Boots"] = "Item Class: Boots\nRarity: Magic\nFrayed Shoes\n--------\nBoots\nEvasion Rating: 15\nEnergy Shield: 10\n--------\n+27 to maximum Life",
                ["Offhand"] = "Item Class: Shields\nRarity: Normal\nBlazon Crest Shield\n--------\nShield\nArmour: 10\nEnergy Shield: 6",
                ["Weapon"] = "Item Class: Weapon\nRarity: Rare\nWild Gnash\nCinderbark Talisman\n--------\nTalisman\nPhysical Damage: 20-42\nFire Damage: 11-26\nAttacks per Second: 1.20",
                ["Ring3"] = "Item Class: Rings\nRarity: Rare\nThird Ring\n--------\n+35% to Cold Resistance",
                ["Flask1"] = "Item Class: Life Flasks\nRarity: Magic\nDivine Life Flask",
                ["Charm1"] = "Item Class: Charms\nRarity: Magic\nThawing Charm"
            },
            skillGroups = [new PoeAccountSkillDto { mainSkill = "Raise Shield", level = 1, summaryText = "Raise Shield (Lv1)" }]
        };

        var cfg = new OverlayConfig();
        PoeAccountService.ApplySyncedCharacterToConfig(cfg, "Tester#1234", simPayload.character, simPayload, out int syncedSlots);

        Assert.Equal(9, syncedSlots);
        Assert.Equal(7, cfg.PoeCharacterLevel);
        Assert.Equal("Forbidden Rites", cfg.TradeLeague);
        Assert.Equal("ES 61 · EV 45 · AR 10 · +Life 27 · Res 9/35/0", cfg.PoeCharacterStatsSummary);
        Assert.True(cfg.WornGearBySlot.ContainsKey("Weapon1"));
        Assert.True(cfg.WornGearBySlot.ContainsKey("Ring3"));
        Assert.True(cfg.WornGearBySlot.ContainsKey("Flask1"));
        Assert.True(cfg.WornGearBySlot.ContainsKey("Charm1"));

        string compactWep = PoeAccountService.SummarizeItemStatsCompact(cfg.WornGearBySlot["Weapon1"]);
        Assert.Contains("~59 DPS", compactWep);
    }

    [Fact]
    public async Task CharacterProfileLinks_RemembersAndSwitchesProfilesPerCharacterId()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "poe2_link_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var mgr = new BuildProfileManager(tmp);
            await mgr.ImportFromPob2Async("MonkBuild", CreateMiniPob("Monk", "Quarterstaves"));
            await mgr.ImportFromPob2Async("SorcBuild", CreateMiniPob("Sorceress", "Wands"));
            string monkProfId = mgr.Profiles[0].Id;
            string sorcProfId = mgr.Profiles[1].Id;

            var cfg = new OverlayConfig { PoeCharacterId = "char-monk", PoeCharacterName = "MonkChar" };
            mgr.SelectProfile(monkProfId);
            PoeAccountService.BindCharacterToProfile(cfg, monkProfId);

            // Switch to 2nd character -> does not steal MonkBuild, binds SorcBuild once chosen
            cfg.PoeCharacterId = "char-sorc";
            cfg.PoeCharacterName = "SorcChar";
            bool switched = PoeAccountService.TryApplyLinkedProfileForCharacter(cfg, mgr);
            Assert.False(switched);
            mgr.SelectProfile(sorcProfId);
            PoeAccountService.BindCharacterToProfile(cfg, sorcProfId);

            // Switch back to char-monk -> automatically restores MonkBuild
            cfg.PoeCharacterId = "char-monk";
            cfg.PoeCharacterName = "MonkChar";
            Assert.True(PoeAccountService.TryApplyLinkedProfileForCharacter(cfg, mgr));
            Assert.Equal(monkProfId, mgr.ActiveProfile?.Id);
            Assert.Equal(monkProfId, cfg.ActiveProfileId);

            // Deleting profile cleans stale links
            PoeAccountService.UnlinkDeletedProfile(cfg, monkProfId);
            Assert.False(cfg.CharacterProfileLinks.ContainsKey("char-monk"));
            Assert.Equal(sorcProfId, cfg.CharacterProfileLinks["char-sorc"]);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BuildStageAdvice_UsesAct4AndInterludesWithoutCruel()
    {
        Loc.Current = AppLanguage.En;
        try
        {
            string act4Advice = PoeAccountService.BuildStageAdvice(52, "Monk", "Forbidden Rites", "Quarterstaves", ["Tempest Flurry"], new Dictionary<string, string>());
            Assert.Contains("Lythara", act4Advice);
            Assert.Contains("Interlude 3", act4Advice);
            Assert.DoesNotContain("Cruel", act4Advice, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Loc.Current = AppLanguage.Uk;
        }
    }

    [Fact]
    public void Trade2StashService_RejectsPoE1ResponsesAndEvaluatesListings()
    {
        Assert.False(Trade2StashService.IsGenuinePoe2StashTabJson("""{"error":{"code":2,"message":"Invalid query"}}"""));
        Assert.False(Trade2StashService.IsGenuinePoe2StashTabJson("""{"numTabs":5,"items":[{"typeLine":"Scroll of Wisdom","league":"Standard"}]}"""));
        Assert.True(Trade2StashService.IsGenuinePoe2StashTabJson("""{"items":[{"realm":"poe2","typeLine":"Exalted Orb"}]}"""));

        var zeroStash = Trade2StashService.EvaluateTrade2Listings("Forbidden Rites", 0, [], 100, 10);
        Assert.True(zeroStash.Ok);
        Assert.True(zeroStash.BreakdownLines.Count >= 2);
        Assert.Equal(0, zeroStash.TotalDivine);

        string fakeFetchJson = """
        {
          "result": [
            {"id":"1","listing":{"stash":{"name":"Sell1"},"price":{"amount":1,"currency":"divine"}},"item":{"typeLine":"Astramentis"}},
            {"id":"2","listing":{"stash":{"name":"Sell2"},"price":{"amount":15,"currency":"exalted"}},"item":{"typeLine":"Hardwood Spear"}},
            {"id":"3","listing":{"stash":{"name":"Sell2"},"price":{"amount":2,"currency":"chaos"}},"item":{"name":"Beast Lock","typeLine":"Twig Circlet"}}
          ]
        }
        """;
        var priced = Trade2StashService.EvaluateTrade2Listings("Forbidden Rites", 3, [fakeFetchJson], exPerDiv: 100, exPerChaos: 10);
        Assert.True(priced.Ok);
        Assert.Equal(2, priced.TabsScanned);
        Assert.Equal(3, priced.CurrencyItemsCount);
        Assert.Equal(135.0, priced.TotalExalted, 2);
        Assert.Equal(1.35, priced.TotalDivine, 2);

        var stash = StashTrackerService.EvaluateCounts("Standard", 2, new Dictionary<string, int>
        {
            ["Divine Orb"] = 2,
            ["Exalted Orb"] = 10
        }, 120.0);
        Assert.Equal(250.0, stash.TotalExalted, 1);
        Assert.Equal(2.08, stash.TotalDivine, 2);
    }

    private static string CreateMiniPob(string className, string itemClass)
    {
        string xml = $"<PathOfBuilding><Build level=\"70\" className=\"{className}\" ascendancyName=\"\"/><Tree><Spec title=\"Main\" nodes=\"65413\"/></Tree><Skills/><Items><Item id=\"1\">Item Class: {itemClass}\nRarity: Rare</Item><Slot name=\"Weapon 1\" itemId=\"1\"/></Items></PathOfBuilding>";
        byte[] raw = System.Text.Encoding.UTF8.GetBytes(xml);
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        return Convert.ToBase64String(ms.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
