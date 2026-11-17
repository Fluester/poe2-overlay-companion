using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;
using Poe2OverlayCompanion.Services;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Poe2OverlayCompanion.Tests;

public sealed class ItemEvaluatorTests
{
    private static BuildProfile MakeLifeArmourAttackProfile() => new()
    {
        Name = "Slam Titan",
        ClassName = "Warrior",
        Ascendancy = "Titan",
        IsCi = false,
        DefenseFocus = "ARMOUR",
        DamageStyle = "Attack",
        PreferredWeaponClass = "Maces",
        PreferredMods = ["Reduced Stun"]
    };

    private static BuildProfile MakeCiEsEvAttackProfile() => new()
    {
        Name = "CI Invoker",
        ClassName = "Monk",
        Ascendancy = "Invoker",
        IsCi = true,
        DefenseFocus = "EV_ES",
        DamageStyle = "Attack",
        PreferredWeaponClass = "Quarterstaves",
        PreferredMods = ["to Spirit"]
    };

    private static BuildProfile MakeSpellCritProfile() => new()
    {
        Name = "Crit Spark Stormweaver",
        ClassName = "Sorceress",
        Ascendancy = "Stormweaver",
        IsCi = false,
        DefenseFocus = "ES",
        DamageStyle = "Spell",
        PreferredWeaponClass = "Wands",
        PreferredMods = ["Cast Speed"]
    };

    public static IEnumerable<object[]> ComputeSlotDiffProfileCases()
    {
        // 1. Life/Armour Attack profile: Armour+Life+ChaosRes beats pure ES+Spell on chest
        yield return
        [
            "Life/Armour Attack Chest",
            MakeLifeArmourAttackProfile(),
            "BodyArmour1",
            "Item Class: Body Armours\nRarity: Rare\nTitan Plate\n--------\nArmour: 420\nItem Level: 75\n--------\n+95 to maximum Life\n+28% to Chaos Resistance\n+30% to Fire Resistance",
            "Item Class: Body Armours\nRarity: Rare\nMage Robe\n--------\nEnergy Shield: 160\nItem Level: 75\n--------\n+35% to Fire Resistance",
            75,
            true
        ];

        // 2. Life/Armour Attack profile: Attack ring (Life + AtkSpd + FlatAtk + ChaosRes) beats Spell ring (CastSpd + SpellDmg + ES)
        yield return
        [
            "Life/Armour Attack Ring",
            MakeLifeArmourAttackProfile(),
            "Ring1",
            "Item Class: Rings\nRarity: Rare\nIron Grip\n--------\nItem Level: 72\n--------\n+75 to maximum Life\n9% increased Attack Speed\nAdds 12 to 24 Physical Damage to Attacks\n+22% to Chaos Resistance",
            "Item Class: Rings\nRarity: Rare\nMage Band\n--------\nItem Level: 72\n--------\n+35 to maximum Energy Shield\n12% increased Cast Speed\n25% increased Spell Damage\nAdds 8 to 16 Chaos Damage to Spells",
            72,
            true
        ];

        // 3. CI ES/EV profile: Hybrid ES/EV chest strongly beats Life+Armour+ChaosRes chest
        yield return
        [
            "CI ES/EV Chest vs Life Armour",
            MakeCiEsEvAttackProfile(),
            "BodyArmour1",
            "Item Class: Body Armours\nRarity: Rare\nAstral Coat\n--------\nEnergy Shield: 240\nEvasion Rating: 360\nItem Level: 80\n--------\n+30% to Lightning Resistance\n+30 to Spirit",
            "Item Class: Body Armours\nRarity: Rare\nLife Plate\n--------\nArmour: 550\nItem Level: 80\n--------\n+120 to maximum Life\n+35% to Chaos Resistance\n+30% to Lightning Resistance",
            80,
            true
        ];

        // 4. Spell/Crit profile: Caster Wand (CastSpd + SpellDmg + SpellCrit + CritDmg + +Skills) beats Attack/Flat-Attack gear
        yield return
        [
            "Spell/Crit Wand",
            MakeSpellCritProfile(),
            "Weapon1",
            "Item Class: Wands\nRarity: Rare\nStorm Wand\n--------\nAttacks per Second: 1.40\nItem Level: 78\n--------\n+3 to Level of all Spell Skills\n75% increased Spell Damage\n22% increased Cast Speed\n45% increased Critical Hit Chance for Spells\n+32% to Critical Damage Bonus",
            "Item Class: Wands\nRarity: Rare\nDull Wand\n--------\nAttacks per Second: 1.40\nPhysical Damage: 40-70\nItem Level: 78\n--------\n+1 to Level of all Spell Skills\n20% increased Spell Damage\n20% increased Critical Hit Chance",
            78,
            true
        ];

        // 5. Spell/Crit Gloves: CastSpd + Crit + ES beats AttackSpd + FlatAtk + Armour
        yield return
        [
            "Spell/Crit Gloves",
            MakeSpellCritProfile(),
            "Gloves1",
            "Item Class: Gloves\nRarity: Rare\nCaster Wraps\n--------\nEnergy Shield: 110\nItem Level: 74\n--------\n14% increased Cast Speed\n+28% to Critical Damage Bonus\n+30% to Cold Resistance",
            "Item Class: Gloves\nRarity: Rare\nBrawler Mitts\n--------\nArmour: 140\nItem Level: 74\n--------\n14% increased Attack Speed\nAdds 10 to 20 Fire Damage to Attacks\n+30% to Cold Resistance",
            74,
            true
        ];

        // 6. Downgrade check on Life/Armour boots
        yield return
        [
            "Life/Armour Downgrade Boots",
            MakeLifeArmourAttackProfile(),
            "Boots1",
            "Item Class: Boots\nRarity: Magic\nSlow Greaves\n--------\nArmour: 40\nItem Level: 30\n--------\n+5% to Cold Resistance",
            "Item Class: Boots\nRarity: Rare\nWar Stride\n--------\nArmour: 240\nItem Level: 70\n--------\n30% increased Movement Speed\n+80 to maximum Life\n+35% to Fire Resistance",
            70,
            false
        ];
    }

    [Theory]
    [MemberData(nameof(ComputeSlotDiffProfileCases))]
    public void ComputeSlotDiffScore_AdaptsCorrectlyAcrossThreeProfiles(
        string caseName,
        BuildProfile profile,
        string slotKey,
        string candidateItemText,
        string referenceItemText,
        int charLevel,
        bool expectUpgrade)
    {
        var ev = ItemEvaluator.Evaluate(candidateItemText, profile, stageIndex: 0, wornGear: null, characterLevel: charLevel);
        var (pct, details) = ItemEvaluator.ComputeSlotDiffScore(ev, slotKey, referenceItemText, profile, charLevel);

        Assert.NotEqual(int.MinValue, pct);
        Assert.False(string.IsNullOrWhiteSpace(details), $"Expected non-empty diff details for {caseName}");
        if (expectUpgrade)
            Assert.True(pct > 15, $"{caseName}: expected clear positive upgrade %, got {pct}% ({details})");
        else
            Assert.True(pct < -15, $"{caseName}: expected clear negative downgrade %, got {pct}% ({details})");
    }

    [Fact]
    public void ComputeSlotDiffScore_CiZerosLifeAndChaosRes_AndSpellIgnoresAttackMods()
    {
        var ciProf = MakeCiEsEvAttackProfile();
        string baseCiRing = "Item Class: Rings\nRarity: Rare\nBase Ring\n--------\nItem Level: 75\n--------\n+40 to maximum Energy Shield\n+25% to Fire Resistance";
        string lifeAndChaosRing = baseCiRing + "\n+95 to maximum Life\n+35% to Chaos Resistance";

        var evCi = ItemEvaluator.Evaluate(lifeAndChaosRing, ciProf, characterLevel: 75);
        var (ciPct, ciDetails) = ItemEvaluator.ComputeSlotDiffScore(evCi, "Ring1", baseCiRing, ciProf, 75);
        Assert.Equal(0, ciPct);
        Assert.DoesNotContain("Life", ciDetails);
        Assert.DoesNotContain("ChaosRes", ciDetails);

        var atkProf = MakeLifeArmourAttackProfile();
        string atkRing = "Item Class: Rings\nRarity: Rare\nAtk Ring\n--------\nItem Level: 75\n--------\n+70 to maximum Life\n8% increased Attack Speed";
        string atkPlusIrrelevantSpell = atkRing + "\n20% increased Cast Speed\n40% increased Spell Damage\nAdds 10 to 20 Chaos Damage to Spells";
        var evAtk = ItemEvaluator.Evaluate(atkPlusIrrelevantSpell, atkProf, characterLevel: 75);
        var (atkPct, _) = ItemEvaluator.ComputeSlotDiffScore(evAtk, "Ring1", atkRing, atkProf, 75);
        Assert.Equal(0, atkPct);

        var spellProf = MakeSpellCritProfile();
        string splRing = "Item Class: Rings\nRarity: Rare\nSpl Ring\n--------\nItem Level: 75\n--------\n14% increased Cast Speed\n+30% to Cold Resistance";
        string splPlusIrrelevantAtk = splRing + "\n14% increased Attack Speed\n25% increased Damage with Attacks\nAdds 12 to 24 Physical Damage to Attacks";
        var evSpl = ItemEvaluator.Evaluate(splPlusIrrelevantAtk, spellProf, characterLevel: 75);
        var (splPct, _) = ItemEvaluator.ComputeSlotDiffScore(evSpl, "Ring1", splRing, spellProf, 75);
        Assert.Equal(0, splPct);
    }

    [Fact]
    public void ComputeSlotDiffScore_NormalizesWeakStarterGearWithStageBaseline()
    {
        var lifeProf = MakeLifeArmourAttackProfile();
        string weakStarterRing = "Item Class: Rings\nRarity: Magic\nStarter Ring\n--------\nItem Level: 2\n--------\n+5% to Cold Resistance";
        string decentAct1Ring = "Item Class: Rings\nRarity: Rare\nAct 1 Rare Ring\n--------\nItem Level: 7\n--------\n+35 to maximum Life\n+15% to Fire Resistance";

        var ev = ItemEvaluator.Evaluate(decentAct1Ring, lifeProf, characterLevel: 7);
        var (pct, _) = ItemEvaluator.ComputeSlotDiffScore(ev, "Ring1", weakStarterRing, lifeProf, 7);

        // Without stage baseline denominator, (116 - 20) / 20 would exceed +480%.
        Assert.InRange(pct, 25, 160);
    }

    [Fact]
    public void Evaluate_HandlesCiManaFlaskWaystonesAffixesAndDualComparison()
    {
        Loc.Current = AppLanguage.Uk;
        var pobProf = MakeCiEsEvAttackProfile();
        pobProf.DamageStyle = "Spell";
        pobProf.PreferredWeaponClass = "Wands";

        var wandEval = ItemEvaluator.Evaluate(
            "Item Class: Wands\nRarity: Rare\nStorm Rod\n--------\n+4 to Level of all Lightning Spell Skills\n25% increased Cast Speed",
            pobProf);
        Assert.True(wandEval.Verdict is ItemVerdict.Good or ItemVerdict.Great);

        var flaskDanger = ItemEvaluator.Evaluate(
            "Item Class: Mana Flasks\nRarity: Magic\n--------\n50% of mana recovered is removed from Life",
            pobProf);
        Assert.Equal(ItemVerdict.Danger, flaskDanger.Verdict);

        string waystoneText =
            "Item Class: Waystones\nRarity: Rare\nDread Path Waystone (Tier 15)\n--------\nWaystone Tier: 15\n--------\n" +
            "Monsters deal 35% of Damage as Extra Chaos\n" +
            "Players have -12% to all Maximum Resistances\n" +
            "55% increased Quantity of Items found in this Area";
        var wsEval = ItemEvaluator.Evaluate(waystoneText, pobProf, stageIndex: 0, characterLevel: 80);
        Assert.True(wsEval.IsWaystone);
        Assert.Equal(3, wsEval.WaystoneMods.Count);
        Assert.Equal(ItemVerdict.Danger, wsEval.Verdict);
        Assert.Contains("poe2db.tw", wsEval.WikiUrl);

        var customTags = new Dictionary<string, MapModSafety>(StringComparer.OrdinalIgnoreCase);
        var deadlyMod = wsEval.WaystoneMods.First(m => m.Safety == MapModSafety.Deadly);
        WaystoneModChecker.CycleCustomTag(customTags, deadlyMod.PatternKey, deadlyMod.Safety);
        var wsReeval = ItemEvaluator.Evaluate(waystoneText, pobProf, stageIndex: 0, characterLevel: 80, customMapTags: customTags);
        Assert.NotEqual(deadlyMod.Safety, wsReeval.WaystoneMods.First(m => m.PatternKey == deadlyMod.PatternKey).Safety);

        var bootsWorn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Boots1"] = "Item Class: Boots\nRarity: Rare\nOld Shoes\n--------\nEnergy Shield: 80\nItem Level: 60\n--------\n10% increased Movement Speed\n+15% to Fire Resistance"
        };
        var betterBoots = ItemEvaluator.Evaluate(
            "Item Class: Boots\nRarity: Rare\nFaster Boots\n--------\nEnergy Shield: 240\nItem Level: 82\n--------\n35% increased Movement Speed\n+42% to Fire Resistance\n+38% to Cold Resistance",
            pobProf, stageIndex: 0, wornGear: bootsWorn, characterLevel: 82);
        Assert.True(betterBoots.ComparedAgainstWornGear);
        Assert.True(betterBoots.UpgradeDeltaPct > 0);
        Assert.False(string.IsNullOrWhiteSpace(betterBoots.AffixSummary));
        Assert.True(betterBoots.EstimatedSuffixes > 0);

        var pseudoRes = betterBoots.SelectableMods.FirstOrDefault(m => m.TradeStatId == "pseudo.pseudo_total_elemental_resistance");
        Assert.NotNull(pseudoRes);
        pseudoRes!.MinSuggested = 65;
        string pseudoQ = PoeTradePriceService.BuildCustomTradeQueryJson(betterBoots, [pseudoRes]);
        Assert.Contains("pseudo.pseudo_total_elemental_resistance", pseudoQ);
        Assert.Contains("65", pseudoQ);
    }
}
