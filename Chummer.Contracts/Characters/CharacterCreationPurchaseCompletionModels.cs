using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationPurchaseCompletionBlockers
{
    public const string RequestInvalid = "creation-purchase-completion-request-invalid";
    public const string SnapshotConflict = "creation-purchase-completion-snapshot-conflict";
    public const string InvestmentDecrease = "creation-purchase-completion-investment-decrease";
    public const string ExistingSelectionLocked = "creation-purchase-completion-existing-selection-locked";
    public const string ReserveRequired = "creation-purchase-completion-reserve-required";
    public const string SpendLimit = "creation-purchase-completion-spend-limit";
    public const string WorkLimit = "creation-purchase-completion-work-limit";
    public const string TargetsUnfulfilled = "creation-purchase-completion-targets-unfulfilled";
}

public static class CharacterCreationPurchaseCompletionTargetOutcomes
{
    public const string Added = "added";
    public const string AlreadyPresent = "already-present";
    public const string ExistingSelectionLocked = "existing-selection-locked";
    public const string Unaffordable = "unaffordable";
    public const string Unavailable = "unavailable";
    public const string ReserveRequired = "reserve-required";
    public const string SpendLimit = "spend-limit";
    public const string WorkLimit = "work-limit";
}

public static class CharacterCreationAmmunitionCompletionBlockers
{
    public const string AssignmentInvalid = "creation-ammunition-assignment-invalid";
    public const string AssignmentConflict = "creation-ammunition-assignment-conflict";
    public const string WeaponNotSelected = "creation-ammunition-weapon-not-selected";
    public const string WeaponSourceChanged = "creation-ammunition-weapon-source-changed";
    public const string CaliberMismatch = "creation-ammunition-caliber-mismatch";
    public const string SourceChanged = "creation-ammunition-source-changed";
    public const string SourceMismatch = "creation-ammunition-source-mismatch";
    public const string UnsupportedSemantics = "creation-ammunition-unsupported-semantics";
}

public static class CharacterCreationAmmunitionCompletionGaps
{
    public const string WeaponCompatibility = "creation-ammunition-weapon-compatibility-not-evaluated";
    public const string LoadedState = "creation-ammunition-loaded-state-not-evaluated";
    public const string CombatEffects = "creation-ammunition-combat-effects-not-evaluated";
}

/// <summary>Explicit ownership of cartridges, not packages, weapon capacity or loaded
/// rounds. Optional assignment requires an exact selected weapon and canonical caliber.
/// The caller must identify an exact observed source.
/// Only plain, fixed-price gun ammunition is currently supported.</summary>
public sealed record CharacterCreationAmmunitionPurchase(string OptionId, int Cartridges, string SourceNodeDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationAmmunitionWeaponTarget? Weapon { get; init; }
}

/// <summary>An explicit zero-based instance within a selected weapon line. AmmoCategory
/// is the canonical legacy caliber token, not a display name or physical measurement.
/// The final reviewed graph supplies its actual instance GUID; this is not a clip.</summary>
public sealed record CharacterCreationAmmunitionWeaponTarget(
    string OptionId, string SourceNodeDigest, int InstanceOrdinal, string AmmoCategory);

/// <summary>Assigns the entire owned Gear line. It neither splits ownership nor loads rounds.</summary>
public sealed record CharacterCreationAmmunitionAssignment(
    string GearOptionId, string GearSourceNodeDigest, CharacterCreationAmmunitionWeaponTarget Weapon);

/// <summary>Evidence from the accepted canonical Gear line. RemainingGaps are
/// unevaluated mechanics, not permission to load or use a particular weapon.
/// This receipt neither grants current authority nor asserts Finish Character coverage.</summary>
public sealed record CharacterCreationAmmunitionAllocation(
    string OptionId, Guid SourceId, string SourceNodeDigest, int OwnedCartridges,
    int PackageQuantity, decimal PackageCost, decimal TotalCost, IReadOnlyList<string> RemainingGaps)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationAmmunitionWeaponTarget? Weapon { get; init; }
}

/// <summary>Exactly one payload. List order ranks Gear, Ammunition, Weapons, Armor and Lifestyles together.
/// Gear Quantity is the complete requested line, not an increment. Existing
/// lines/configurations cannot be replaced or increased by this planner.</summary>
public sealed record CharacterCreationPurchaseCompletionTarget(
    CharacterCreationGearSelection? Gear = null,
    CharacterCreationLifestyleConfiguration? Lifestyle = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationWeaponSelection? Weapon { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationArmorSelection? Armor { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationAmmunitionPurchase? Ammunition { get; init; }
}

/// <summary>Explicit purchase wishes and limits, not an instruction to spend all
/// funds. Zero spending is the safe default. Karma investment is a caller choice,
/// never inferred; an existing investment cannot be decreased. StartingLifestyleId
/// is also explicit; this planner never rolls or spends later starting cash.</summary>
public sealed record CharacterCreationPurchaseCompletionRequest(
    string LifestylesSnapshotDigest,
    string MagicSnapshotDigest,
    IReadOnlyList<CharacterCreationAttributeAllocation> AttributeAllocations,
    CharacterCreationMagicResonanceSelections MagicSelections,
    IReadOnlyList<string> QualityOptionIds,
    int TotalKarmaInvestment,
    IReadOnlyList<CharacterCreationPurchaseCompletionTarget> Targets,
    Guid? StartingLifestyleId = null,
    decimal NuyenToReserve = 0m,
    decimal MaximumAdditionalSpend = 0m,
    int MaximumEvaluations = 32)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LoadoutSnapshotDigest { get; init; }
}

public sealed record CharacterCreationPurchaseCompletionTargetResult(
    int TargetIndex,
    string Outcome,
    IReadOnlyList<string> Blockers)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationAmmunitionAllocation? Ammunition { get; init; }
}

/// <summary>Detached pure proposal only. IsPurchasePlanComplete means that these
/// explicit purchase wishes were satisfied; it is NOT whole-character readiness
/// or confirmation authority. Rejected wishes remain visible on partial plans.</summary>
public sealed record CharacterCreationPurchaseCompletionPlan(
    CharacterCreationPurchaseCompletionRequest Request,
    IReadOnlyList<CharacterCreationGearSelection> Gear,
    IReadOnlyList<CharacterCreationLifestyleConfiguration> Lifestyles,
    Guid? StartingLifestyleId,
    CharacterCreationLifestylesCompletionProjection Preview,
    IReadOnlyList<CharacterCreationPurchaseCompletionTargetResult> Targets,
    decimal AdditionalSpend,
    bool HasChanges,
    bool IsPurchasePlanComplete,
    int Evaluations,
    IReadOnlyList<string> Blockers,
    string PlanDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutSelections? Loadout { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationAmmunitionAssignment>? AmmunitionAssignments { get; init; }
}

public static class CharacterCreationPurchaseCompletionRules
{
    public static string ComputePlanDigest(CharacterCreationPurchaseCompletionPlan plan)
        => CharacterCreationFinalizationDigest.Compute(plan with { PlanDigest = string.Empty });
}
