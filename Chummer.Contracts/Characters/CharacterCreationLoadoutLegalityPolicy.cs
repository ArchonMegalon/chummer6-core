using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

/// <summary>Observed career purchase switches. Creation uses source base prices;
/// a captured lifecycle permits reporting that the career operation is inapplicable.</summary>
public sealed record CharacterCreationLoadoutLegalityPolicy(
    string SettingsProfileId, string RawProfileInputsDigest,
    bool MultiplyRestrictedCost, bool MultiplyForbiddenCost, string PolicyDigest)
{
    // Omit on historical policies and on the default false/false path. Neither
    // rates nor an evaluated creation multiplier are introduced by this record.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutLifecycle? Lifecycle { get; init; }

    public bool HasCreationLifecycle(string rawCharacterXmlDigest)
        => Lifecycle is { Created: false } source
            && CharacterCreationFinalizationDigest.IsCanonical(source.RawCharacterXmlDigest)
            && source.RawCharacterXmlDigest == rawCharacterXmlDigest;

    public static string ComputeDigest(CharacterCreationLoadoutLegalityPolicy policy)
        => CharacterCreationFinalizationDigest.Compute(policy with { PolicyDigest = string.Empty });

    // Historical catalog reconstruction only; never a Creation purchase rule.
    public bool RequiresUnsupportedPrice(string legality)
        => MultiplyRestrictedCost && legality == CharacterCreationGearLegality.Restricted
            || MultiplyForbiddenCost && legality == CharacterCreationGearLegality.Forbidden;

    /// <summary>Historical wire identity, retained byte-for-byte for saved drafts.
    /// UnsupportedLegalityMultipliersExcluded is a legacy digest member name, not
    /// a claim that Creation now excludes or multiplies these source prices.</summary>
    public string ComputeRulesDigest(int maximumAvailability)
        => CharacterCreationLoadoutRules.Compute(new
        {
            Schema = CharacterCreationLoadoutSchemas.RulesV1, maximumAvailability,
            FixedNumericCostAndAvailability = true,
            multiplyRestricted = MultiplyRestrictedCost, multiplyForbidden = MultiplyForbiddenCost,
            UnsupportedLegalityMultipliersExcluded = true, SelectedCustomCategoryMutationsExcluded = true,
            PlainUnloadedFirearms = true, SinglePlainArmor = true, SourceCategoriesCaptured = true
        });
}

/// <summary>Observed from the immutable character XML in the selected source context.</summary>
public sealed record CharacterCreationLoadoutLifecycle(bool Created, string RawCharacterXmlDigest);
