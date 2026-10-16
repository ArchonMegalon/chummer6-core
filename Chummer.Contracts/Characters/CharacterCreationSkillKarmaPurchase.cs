namespace Chummer.Contracts.Characters;

/// <summary>An exact, source-bound purchase above the point/grant-funded rating.
/// A specialization is an addition, never a replacement of an existing choice.
/// Null purchase lists retain the historical point-only completion contract.</summary>
public sealed record CharacterCreationSkillKarmaPurchase(
    string SourceSkillId,
    string Kind,
    string SourceNodeDigest,
    int Rating,
    string? SpecializationOptionId = null);

public static class CharacterCreationSkillKarmaPurchaseBlockers
{
    public const string PolicyUnavailable = "creation-skill-karma-policy-unavailable";
    public const string PurchaseInvalid = "creation-skill-karma-purchase-invalid";
    public const string SourceConflict = "creation-skill-karma-source-conflict";
    public const string ModifiersUnsupported = "creation-skill-karma-modifiers-unsupported";
    public const string GroupCompensationUnsupported = "creation-skill-karma-group-compensation-unsupported";
    public const int MaximumPurchases = 256;
}
