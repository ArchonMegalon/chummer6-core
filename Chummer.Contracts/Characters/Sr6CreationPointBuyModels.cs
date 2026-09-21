using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

/// <summary>Purchased pools, not attribute ratings, Karma or character effects.</summary>
public sealed record Sr6CreationPointBuySelection(int AdditionalAttributePoints, int AdditionalSkillPoints,
    int AdditionalAdjustmentPoints, int ResourceUnits);

public sealed record Sr6CreationPointBuyLimits(int CharacterPoints, int FreeAttributePoints, int FreeSkillPoints,
    int FreeAdjustmentPoints, int MaximumAdditionalAttributePoints, int MaximumAdditionalSkillPoints,
    int MaximumAdditionalAdjustmentPoints, int MaximumResourceUnits, int AttributePointCost, int SkillPointCost,
    int AdjustmentPointCost, int ResourceUnitCost, int NuyenPerResourceUnit, int AwakenedOrResonanceCost,
    int CustomizationKarma);

/// <summary>Pool purchase review only. AllCharacterPointsSpent is not finalization permission.</summary>
public sealed record Sr6CreationPointBuyPreview(int CharacterPoints, int PointsSpent, int PointsRemaining,
    int TalentCost, int AttributeCost, int SkillCost, int AdjustmentCost, int ResourceCost, int CustomizationKarma,
    bool AllCharacterPointsSpent, int FreeSpells, int FreeComplexForms, int FreePowerPoints, string AuthorityDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PowerPointCost { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ComplexFormCost { get; init; }
}

public static class Sr6CreationPointBuyBlockers
{
    public const string InvalidSelection = "sr6-creation-point-buy-invalid-selection";
    public const string MethodMismatch = "sr6-creation-point-buy-method-mismatch";
    public const string SourceRequired = "sr6-creation-point-buy-source-required";
    public const string BudgetExceeded = "sr6-creation-point-buy-budget-exceeded";
}
