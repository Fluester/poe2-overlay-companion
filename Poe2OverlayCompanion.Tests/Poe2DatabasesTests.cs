using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion.Tests;

public sealed class Poe2DatabasesTests
{
    [Fact]
    public void Poe2GemsDb_LoadsFullCompressedCatalogAndResolvesFangsOfFrostAndAliases()
    {
        Assert.True(Poe2GemsDb.GetAllCatalogEntries().Count >= 900);

        var fangs = Poe2GemsDb.ResolveGem("Fangs Of Frost", 7);
        Assert.Equal("Uncut Skill Gem Lv 3", fangs.UncutTier);
        Assert.Equal(6, fangs.MinUnlockCharacterLevel);
        Assert.Equal(14, fangs.RequiredAttrDex);
        Assert.Contains("Act 1", fangs.ObtainWhereEn);

        var twister = Poe2GemsDb.ResolveGem("Twister (Lv18 Q20%)", 1);
        Assert.Equal(18, twister.GemLevelInBuild);
        Assert.Equal(20, twister.QualityInBuild);

        var rage2 = Poe2GemsDb.ResolveGem("Rage II", 1);
        Assert.Equal(45, rage2.MinUnlockCharacterLevel);

        var hoi = Poe2GemsDb.ResolveGem("Herald of Ice", 10);
        Assert.Equal(30, hoi.SpiritCost);

        var blink = Poe2GemsDb.ResolveGem("Blink", 7);
        Assert.Equal(26, blink.MinUnlockCharacterLevel);
        Assert.Equal(60, blink.SpiritCost);

        var prim2 = Poe2GemsDb.ResolveGem("Primal Armament Two", 7);
        Assert.Equal(GemCategory.Support, prim2.Category);

        var garukhan = Poe2GemsDb.ResolveGem("Garukhans Resolve", 7);
        Assert.Equal(GemCategory.Support, garukhan.Category);
        Assert.Equal(65, garukhan.MinUnlockCharacterLevel);

        var cof = Poe2GemsDb.ResolveGem("Cast on Freeze", 7);
        Assert.Equal(100, cof.SpiritCost);
    }

    [Fact]
    public void Poe2KnowledgeDb_ResolvesUniquesBaseCategoriesAndLevelAwareObtain()
    {
        var astra = Poe2KnowledgeDb.ResolveItemObtain("Astramentis Stellar Amulet", "Amulet1", null);
        Assert.Contains("Stellar Amulet", astra.ObtainSourceEn);

        var alpha = Poe2KnowledgeDb.ResolveItemObtain("Alpha's Howl", "Helm1", null);
        Assert.Contains("Plumed Cap", alpha.Category);
        Assert.Contains("61", alpha.MinItemOrAreaLevel);

        var ghost = Poe2KnowledgeDb.ResolveItemObtain("Ghostwrithe", "BodyArmour1", null);
        Assert.Contains("35%", ghost.ObtainSourceEn);

        var jacket = Poe2KnowledgeDb.ResolveItemObtain("Sleek Jacket", "", null, 70);
        Assert.Equal("Body Armour", jacket.Category);

        var sRing = Poe2KnowledgeDb.ResolveItemObtain("Sapphire Ring", "", null, 15);
        Assert.Equal("Jewellery", sRing.Category);

        var lvl7Spear = Poe2KnowledgeDb.ResolveItemObtain("Keen Ironhead Spear", "Spears", null, 7);
        Assert.Contains("Act 1", lvl7Spear.ObtainSourceEn);
        Assert.DoesNotContain("T12", lvl7Spear.ObtainSourceEn);
    }

    [Fact]
    public void CampaignGuideDb_ContainsAct1FreythornAct4AndInterlude3Lythara()
    {
        var cg = CampaignGuideDb.Resolve("Freythorn");
        Assert.NotNull(cg);
        Assert.Contains("Spirit", cg!.RewardEn);

        var kriar = CampaignGuideDb.Resolve("Kriar Village");
        Assert.NotNull(kriar);
        Assert.Contains("40 Spirit", kriar!.RewardEn);

        Assert.True(CampaignGuideDb.PermanentMilestones.Count >= 14);
        Assert.DoesNotContain(CampaignGuideDb.PermanentMilestones, m => m.Act.Contains("Cruel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PassiveTreeService_LoadsV2BinaryTreeWithValidCoordinatesAndOrbitalArcs()
    {
        var tree = PassiveTreeService.Index;
        Assert.True(tree.AllNodes.Count >= 4500);

        var nSample = tree.Resolve("attack_speed25");
        Assert.NotNull(nSample);
        Assert.False(string.IsNullOrWhiteSpace(nSample!.Name));

        var witchNode = tree.Resolve("witch595");
        Assert.NotNull(witchNode);
        Assert.True(Math.Abs(witchNode!.Y - (-1490.6)) <= 20);

        int arcEdges = tree.AllNodes.Sum(n => n.ConnectionOrbits.Count(o => o != 0));
        Assert.True(arcEdges >= 1000 && tree.OrbitRadii.Length >= 8);

        foreach (var nA in tree.AllNodes)
        {
            if (!string.IsNullOrEmpty(nA.AscendancyName)) continue;
            foreach (var cid in nA.Connections)
            {
                if (!tree.ByNumericId.TryGetValue(cid, out var nB) || !string.IsNullOrEmpty(nB.AscendancyName)) continue;
                double dx = nB.X - nA.X, dy = nB.Y - nA.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                Assert.True(d <= 2500, $"Corrupted cross-map edge: {nA.Name} -> {nB.Name} ({d:F0}px)");
            }
        }

        var prev = new List<LocalBuildPassive> { new("attack_speed25", 1, 100, 0, ""), new("dexterity6", 1, 100, 0, "") };
        var cur = new List<LocalBuildPassive> { new("attack_speed25", 1, 100, 0, ""), new("shadow_monk_notable1", 1, 100, 0, "") };
        var diff = PassiveTreeService.AnalyzeBuildPassives("Test Stage", cur, prev);
        Assert.Equal(1, diff.KeptCount);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
    }

    [Fact]
    public void Localization_SwitchesCleanlyAcrossUkEnRu()
    {
        try
        {
            Loc.Current = AppLanguage.Uk;
            string sUk = PoeTermLocalizer.SlotName("Helm1");
            string mUk = PoeTermLocalizer.StatLine("10% increased Attack Speed");
            Loc.Current = AppLanguage.En;
            string sEn = PoeTermLocalizer.SlotName("Helm1");
            Loc.Current = AppLanguage.Ru;
            string sRu = PoeTermLocalizer.SlotName("Helm1");

            Assert.NotEqual(sUk, sEn);
            Assert.NotEqual(sEn, sRu);
            Assert.Contains("швидкост", mUk);
        }
        finally
        {
            Loc.Current = AppLanguage.Uk;
        }
    }
}
