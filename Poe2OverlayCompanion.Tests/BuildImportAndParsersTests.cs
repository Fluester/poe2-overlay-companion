using System.IO.Compression;
using System.Text;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion.Tests;

public sealed class BuildImportAndParsersTests
{
    [Fact]
    public async Task BuildProfileManager_HasZeroHardcodedBuildsAndImportsPob2AndMobalytics()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "poe2_mgr_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var mgr = new BuildProfileManager(tmpDir);
            Assert.Empty(mgr.Profiles);

            string fakeSorcXml =
                "<PathOfBuilding><Build level=\"82\" className=\"Sorceress\" ascendClassName=\"Stormweaver\">" +
                "<PlayerStat stat=\"EnergyShield\" value=\"5200\"/><PlayerStat stat=\"Life\" value=\"1\"/>" +
                "</Build><Tree><Spec title=\"Endgame\" nodes=\"65413,65324\"/></Tree>" +
                "<Skills><Skill enabled=\"true\"><Gem nameSpec=\"Spark\" level=\"19\" enabled=\"true\"/></Skill></Skills>" +
                "<Items><Item id=\"1\">Item Class: Wands\nRarity: Rare\n--------\n+4 to Level of all Spell Skills</Item>" +
                "<Slot name=\"Weapon 1\" itemId=\"1\"/></Items></PathOfBuilding>";
            byte[] raw = Encoding.UTF8.GetBytes(fakeSorcXml);
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw, 0, raw.Length);
            string pobCode = Convert.ToBase64String(ms.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');

            var pobSummary = Pob2Parser.Parse(pobCode);
            Assert.True(pobSummary.Success);
            Assert.True(pobSummary.HasCi);
            Assert.Single(pobSummary.TreeSpecs);

            var (okPob, errPob, importedPob) = await mgr.ImportFromPob2Async("Temp Sorc", pobCode);
            Assert.True(okPob, errPob);
            Assert.NotNull(importedPob);
            Assert.True(importedPob!.IsCi);
            Assert.Single(importedPob.Stages);
            Assert.NotEmpty(TradeLinkBuilder.BuildDynamicShortcuts(importedPob));

            Assert.True(MobalyticsImportService.IsMobalyticsUrl("https://mobalytics.gg/poe-2/builds/test-build"));
            var mobaVars = new List<MobalyticsScrapedVariant>
            {
                new()
                {
                    title = "Test Mobalytics Twister",
                    url = "https://mobalytics.gg/poe-2/builds/test-build",
                    author = "Tester",
                    className = "Monk",
                    ascendancy = "Invoker",
                    variantName = "Lvl 1-23",
                    skillGroups = [["Whirling Slash", "Martial Tempo", "Rage I"]],
                    gear = [new MobalyticsGearEntry { slot = "armour.boots", name = "Wanderlust", details = "Boots\n15% increased Movement Speed" }]
                },
                new()
                {
                    title = "Test Mobalytics Twister",
                    url = "https://mobalytics.gg/poe-2/builds/test-build",
                    variantName = "65+ Endgame",
                    skillGroups = [["Twister", "Prolonged Duration II", "Frost Nexus"]],
                    gear = [new MobalyticsGearEntry { slot = "armour.boots", name = "Rare Boots", details = "Energy Shield: 120\n20% increased Movement Speed" }]
                }
            };
            var (mOk, mMsg, mProf) = MobalyticsImportService.ImportFromScrapedVariants(mgr, mobaVars, "Temp Moba");
            Assert.True(mOk, mMsg);
            Assert.NotNull(mProf);
            Assert.Equal(2, mProf!.Stages.Count);

            string vRx = RegexGeneratorService.BuildVendorRegex(mProf);
            string wRx = RegexGeneratorService.BuildSafeWaystoneRegex(mProf);
            Assert.False(string.IsNullOrWhiteSpace(vRx));
            Assert.True(vRx.Length <= 50 && wRx.Length <= 50 && wRx.StartsWith("!"));
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task BuildPlanner_InfersAct3To6AndDetectsLocalFileDiffsWithoutTouchingDocuments()
    {
        var actRange = BuildPlannerService.InferLevelWindow("Act 3-6 - Starter", "act3_6.build");
        Assert.Equal(32, actRange.minLvl);
        Assert.Equal(64, actRange.maxLvl);

        var multiWepProf = new BuildProfile
        {
            Name = "Multi-Weapon Stage Build",
            PreferredWeaponClass = "Quarterstaves",
            DamageStyle = "Attack",
            Stages =
            [
                new BuildStageEntry { Name = "Act 3-6 Stage", MinLevel = 3, MaxLevel = 6, GearSignatures = ["Weapon1: Sinister Quarterstaff"] },
                new BuildStageEntry { Name = "Lvl 1-23 Spear Start", MinLevel = 1, MaxLevel = 23, GearSignatures = ["Weapon1: Ironhead Spear\n1. Adds 1 to 4 Physical Damage"] }
            ]
        };
        BuildProfileManager.NormalizeProfileStages(multiWepProf);
        Assert.Equal("Lvl 1-23 Spear Start", multiWepProf.Stages[0].Name);
        Assert.Equal("Spears", multiWepProf.Stages[0].StageWeaponClass);
        Assert.Equal(32, multiWepProf.Stages[1].MinLevel);

        string tmp = Path.Combine(Path.GetTempPath(), "poe2_upd_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string buildPath = Path.Combine(tmp, "stage.build");
            string initialJson = "{\"name\":\"Lvl 65+ Endgame - Test Spark\",\"passives\":[{\"id\":\"attack_speed25\"}],\"skills\":[{\"id\":\"Metadata/Items/Gems/SkillGemSpark\"}]}";
            File.WriteAllText(buildPath, initialJson);
            var plan = BuildPlannerService.TryParseFile(buildPath);
            Assert.NotNull(plan);

            var mgr = new BuildProfileManager(tmp);
            var trackedProf = new BuildProfile
            {
                Name = "Local Tracked",
                SourceKind = BuildSourceKind.BuildPlannerImport,
                Stages =
                [
                    new BuildStageEntry
                    {
                        Name = "Endgame",
                        MinLevel = 65,
                        MaxLevel = 100,
                        FilePath = buildPath,
                        LastFileHash = plan!.ContentSha256,
                        PassiveIds = ["attack_speed25"]
                    }
                ]
            };
            mgr.Profiles.Add(trackedProf);

            File.WriteAllText(buildPath, "{\"name\":\"Lvl 65+ Endgame - Test Spark\",\"passives\":[{\"id\":\"attack_speed25\"},{\"id\":\"dexterity6\"}],\"skills\":[{\"id\":\"Metadata/Items/Gems/SkillGemSpark\"}]}");
            var upd = await BuildUpdateMonitorService.CheckAllBuildsAsync(new OverlayConfig(), mgr, 70);
            Assert.True(upd.HasChanges);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ClientLogWatcherLootFilterAndAppUpdateVersion_WorkOffline()
    {
        var clw = new ClientLogWatcher();
        var tw = clw.ParseTradeWhisper(
            "2026/11/16 20:00:00 12345 [INFO Client 1] @From <GUILD> BuyerDude: Hi, I would like to buy your Emerald Ring listed for 2 divine in Standard (stash tab \"Sales\"; position: left 4, top 7)",
            enqueue: true);
        Assert.NotNull(tw);
        Assert.Equal("BuyerDude", tw!.PlayerName);
        Assert.Equal("2 divine", tw.PriceText);
        Assert.Equal("Sales", tw.StashTab);
        Assert.Equal(4, tw.StashLeft);
        Assert.Single(clw.DrainNewWhispers());

        var okVal = LootFilterService.ValidateFilter("Show\n  BaseType == \"Divine Orb\"\n  SetFontSize 45\n");
        var warnVal = LootFilterService.ValidateFilter("Show\n  LinkedSockets >= 5\n");
        Assert.True(okVal.IsValid && okVal.BlockCount == 1 && okVal.Warnings.Count == 0);
        Assert.NotEmpty(warnVal.Warnings);

        Assert.True(AppUpdateService.IsNewerVersion("v1.2.0", new Version(1, 1, 0)));
        Assert.False(AppUpdateService.IsNewerVersion("v1.1.0", new Version(1, 1, 0)));
    }
}
