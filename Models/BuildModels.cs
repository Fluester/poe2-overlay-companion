namespace Poe2OverlayCompanion.Models;

public enum AppLanguage
{
    Uk,
    En,
    Ru
}

public enum AiProviderKind
{
    Gemini,
    OpenAiCompatible
}

public enum BuildSourceKind
{
    BuildPlannerImport,
    Pob2Import,
    GuideUrlImport,
    MobalyticsImport
}

public enum ItemVerdict
{
    Great,
    Good,
    Ok,
    Warn,
    Danger,
    Info
}

public sealed class ItemEvaluation
{
    public bool IsPoeItem { get; init; }
    public string ItemName { get; init; } = "";
    public string ItemClass { get; init; } = "";
    public string RawBaseType { get; init; } = "";
    public string Rarity { get; set; } = "";
    public int ItemLevel { get; set; }
    public double? PhysicalDps { get; set; }
    public double? ElementalDps { get; set; }
    public double TotalDps => (PhysicalDps ?? 0) + (ElementalDps ?? 0);
    public double EnergyShield { get; set; }
    public double Evasion { get; set; }
    public double Armour { get; set; }
    public double MovementSpeed { get; set; }
    public int PlusSkillLevel { get; set; }
    public int TotalResistance { get; set; }
    public double SpiritBonus { get; set; }
    public ItemVerdict Verdict { get; set; } = ItemVerdict.Info;
    public string Summary { get; set; } = "";
    public string MatchedBuildSlot { get; set; } = "";
    public string UpgradeVerdict { get; set; } = "";
    public int UpgradeDeltaPct { get; set; }
    public string UpgradeComparisonNote { get; set; } = "";
    public string BuildMatchVerdict { get; set; } = "";
    public string BuildComparisonNote { get; set; } = "";
    public bool ComparedAgainstWornGear { get; set; }
    public string ProfitVerdict { get; set; } = "";
    public string LivePriceSummary { get; set; } = "";
    public string ObtainInfo { get; set; } = "";
    public string GeneratedTradeUrl { get; set; } = "";
    public string RawCopiedText { get; set; } = "";
    public List<string> Positives { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Missing { get; } = [];
    public List<ItemModFilterOption> SelectableMods { get; } = [];
}

public sealed class ItemModFilterOption
{
    public string DisplayText { get; set; } = "";
    public string TradeStatId { get; set; } = "";
    public double NumericValue { get; set; }
    public int MinSuggested { get; set; }
    public bool IsDefaultChecked { get; set; }
    public string Category { get; set; } = "Explicit"; // Explicit, Defense, Dps
}

public sealed class PobTreeSpec
{
    public string Title { get; set; } = "Default";
    public string Ascendancy { get; set; } = "";
    public List<string> NodeIds { get; set; } = [];
}

public sealed class Pob2Summary
{
    public bool Success { get; init; }
    public string Error { get; init; } = "";
    public int Level { get; init; }
    public string ClassName { get; init; } = "";
    public string Ascendancy { get; init; } = "";
    public double EnergyShield { get; init; }
    public double Evasion { get; init; }
    public double EvasionPercent { get; init; }
    public double Armour { get; init; }
    public double Life { get; init; }
    public double TotalDps { get; init; }
    public double Spirit { get; init; }
    public bool HasCi { get; init; }
    public string PrimaryWeaponClass { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string RawCode { get; set; } = "";
    public List<string> SkillNames { get; } = [];
    public List<PobSkillGroup> SkillGroups { get; } = [];
    public List<string> EquippedItems { get; } = [];
    public Dictionary<string, string> RawEquippedBySlot { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PobTreeSpec> TreeSpecs { get; } = [];
    public List<string> Recommendations { get; } = [];
    public string Notes { get; set; } = "";
    public string RawContextSummary { get; set; } = "";
}

public sealed record PobSkillGroup(string MainSkill, List<string> Supports, bool Enabled);

public sealed record LocalBuildPassive(string Id, int MinLevel, int MaxLevel, int WeaponSet, string AdditionalText);

public sealed record PlannerGearItem(string RawSlotId, string Text, int MinLevel, int MaxLevel);

public sealed record PlannerSkillItem(string RawSkillId, string SkillName, List<string> Supports, int MinLevel, int MaxLevel);

public sealed class LocalBuildPlan
{
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Link { get; init; } = "";
    public string Description { get; init; } = "";
    public string Ascendancy { get; init; } = "";
    public int MinSuggestedLevel { get; init; } = 1;
    public int MaxSuggestedLevel { get; init; } = 100;
    public DateTime LastModified { get; init; }
    public string ContentSha256 { get; init; } = "";
    public List<LocalBuildPassive> Passives { get; } = [];
    public List<PlannerGearItem> Gear { get; } = [];
    public List<PlannerSkillItem> Skills { get; } = [];
    public int PassivesCount => Passives.Count;
}

public sealed class BuildStageEntry
{
    public string Name { get; set; } = "";
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 100;
    public string FilePath { get; set; } = "";
    public string LastFileHash { get; set; } = "";
    public string StageWeaponClass { get; set; } = "";
    public List<string> PassiveIds { get; set; } = [];
    public List<string> SkillSignatures { get; set; } = [];
    public List<string> GearSignatures { get; set; } = [];
    public Dictionary<string, string> GearDetailsBySlot { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Notes { get; set; } = "";
}

public sealed class BuildChangelogEntry
{
    public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "";
    public string Headline { get; set; } = "";
    public List<string> DiffLines { get; set; } = [];
}

public sealed class BuildProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string GuideUrl { get; set; } = "";
    public string PobSourceUrl { get; set; } = "";
    public BuildSourceKind SourceKind { get; set; } = BuildSourceKind.Pob2Import;
    public string ClassName { get; set; } = "";
    public string Ascendancy { get; set; } = "";
    public bool IsCi { get; set; }
    public string DefenseFocus { get; set; } = "HYBRID"; // ES, EV_ES, EV, ARMOUR, AR_ES, HYBRID
    public string PreferredWeaponClass { get; set; } = "";
    public string DamageStyle { get; set; } = "Attack"; // Attack, Spell, Minion, DoT
    public string Pob2Code { get; set; } = "";
    public string BuildFolderOrFile { get; set; } = "";
    public List<string> LinkedBuildFiles { get; set; } = [];
    public List<BuildStageEntry> Stages { get; set; } = [];
    public string SummaryNotes { get; set; } = "";
    public List<string> TargetSkills { get; set; } = [];
    public List<string> TargetGearHints { get; set; } = [];
    public List<string> PreferredMods { get; set; } = [];
    public string LastGuideSnapshotHash { get; set; } = "";
    public string LastGuideSnapshotText { get; set; } = "";
    public string LastOnlineUpdatedMarker { get; set; } = "";
    public string LastPobSnapshotHash { get; set; } = "";
    public List<BuildChangelogEntry> Changelog { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FarmSessionStats
{
    public DateTime SessionStartUtc { get; set; } = DateTime.UtcNow;
    public int MapsCompleted { get; set; }
    public int Deaths { get; set; }
    public int SpecialEncounters { get; set; }
    public string CurrentMapName { get; set; } = "";
    public DateTime? CurrentMapEnteredUtc { get; set; }
    public double TotalMapSeconds { get; set; }
    public double LastMapSeconds { get; set; }
    public double AverageMapSeconds => MapsCompleted > 0 ? TotalMapSeconds / MapsCompleted : 0;
}

public enum PassiveNodeKind
{
    Small = 0,
    JewelSocket = 1,
    Notable = 2,
    Keystone = 3,
    ClassStart = 4,
    AscendancySmall = 5,
    AscendancyNotable = 6
}

public sealed class PassiveNodeInfo
{
    public string NumericId { get; init; } = "";
    public string StringId { get; init; } = "";
    public string Name { get; init; } = "";
    public PassiveNodeKind Kind { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public string[] Stats { get; init; } = [];
    public string AscendancyName { get; init; } = "";
    public string[] Connections { get; init; } = [];
}

public enum PassiveDiffStatus
{
    Keep,
    Added,
    Removed
}

public sealed record PassiveStepEntry(
    int StepOrder,
    string RawId,
    PassiveNodeInfo? Node,
    PassiveDiffStatus Status,
    int WeaponSet,
    string Note
);

public sealed class PassiveDiffResult
{
    public string Title { get; init; } = "";
    public int TotalPoints { get; init; }
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public int KeptCount { get; init; }
    public List<PassiveStepEntry> Entries { get; } = [];
}

public enum GemCategory
{
    ActiveSkill,
    Support,
    SpiritBuff,
    MetaTrigger
}

public sealed record PoeGemInfo(
    string Name,
    GemCategory Category,
    string Attribute,
    int GemLevelInBuild,
    int QualityInBuild,
    int MinUnlockCharacterLevel,
    int RequiredAttrStr,
    int RequiredAttrDex,
    int RequiredAttrInt,
    string UncutTier,
    int SpiritCost,
    int ManaMultiplierPct,
    string Tags,
    string ObtainWhereUk,
    string ObtainWhereEn,
    string ObtainWhereRu,
    string DescriptionUk,
    string DescriptionEn,
    string DescriptionRu
);

public sealed record PoeItemObtainInfo(
    string ItemTitle,
    string Category,
    string MinItemOrAreaLevel,
    string ObtainSourceUk,
    string ObtainSourceEn,
    string ObtainSourceRu,
    string StrategyTipUk,
    string StrategyTipEn,
    string StrategyTipRu
);

public sealed class TradeWhisperEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public bool IsIncoming { get; init; } = true;
    public string PlayerName { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string PriceText { get; init; } = "";
    public string League { get; init; } = "";
    public string StashTab { get; init; } = "";
    public int StashLeft { get; init; }
    public int StashTop { get; init; }
    public string RawMessage { get; init; } = "";
    public DateTime ReceivedUtc { get; init; } = DateTime.UtcNow;
}

public sealed class CampaignZoneTip
{
    public string ZonePattern { get; init; } = "";
    public string ActBadge { get; init; } = "";
    public string BossName { get; init; } = "";
    public string RewardUk { get; init; } = "";
    public string RewardEn { get; init; } = "";
    public string RewardRu { get; init; } = "";
    public string TipUk { get; init; } = "";
    public string TipEn { get; init; } = "";
    public string TipRu { get; init; } = "";
}

public sealed class StashScanResult
{
    public bool Ok { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string League { get; set; } = "";
    public int TabsScanned { get; set; }
    public int CurrencyItemsCount { get; set; }
    public double TotalExalted { get; set; }
    public double TotalDivine { get; set; }
    public double ExPerDivine { get; set; } = 120;
    public List<string> BreakdownLines { get; set; } = [];
    public DateTime ScannedUtc { get; set; } = DateTime.UtcNow;
}
