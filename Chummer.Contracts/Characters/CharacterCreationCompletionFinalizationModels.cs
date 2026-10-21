namespace Chummer.Contracts.Characters;

public static class CharacterCreationCompletionFinalizationSchemas
{
    public const string ProjectionV3 = "chummer.creation-completion-finalization-projection.v3";
    public const string ProjectionV2 = "chummer.creation-completion-finalization-projection.v2";
    public const string ObservationConflict = "creation-completion-finalization-observation-conflict";
    public const string ReviewConflict = "creation-completion-finalization-review-conflict";
}

/// <summary>Exact unused point pools, not converted to Karma or silently spent.
/// A legal final projection can retain these values for the player's review.</summary>
public sealed record CharacterCreationCompletionUnusedPoints(
    int NormalAttributes, int SpecialAttributes, int ActiveSkills, int SkillGroups, int KnowledgeSkills);

/// <summary>
/// Deterministic Core output from an actual original observation and independently
/// replayed choices. This is not a receipt, owner lease, current-source capability
/// or permission to save. The atomic consumer must reread and regenerate it.
/// CharacterXml is an internal transaction payload, never an editable client plan.
/// Original auxiliary state and receipt history are not replaced by this value.
/// </summary>
public sealed record CharacterCreationCompletionFinalizationProjection(
    string Schema,
    CharacterCreationFinalizationBinding Binding,
    string InputsSnapshotDigest,
    string ReviewDigest,
    string BootstrapSourceSnapshotDigest,
    CharacterCreationKarmaCarryoverPolicy CarryoverPolicy,
    CharacterCreationFinalizationStartingCash StartingCash,
    string CharacterXml,
    string ExpectedResultRawCharacterXmlDigest,
    IReadOnlyList<CharacterCreationFinalizationDelta> OrderedDeltas,
    IReadOnlyList<string> SourceAnchorIds,
    int KarmaBeforeCarryover,
    int KarmaRemaining,
    decimal NuyenBeforeCarryover,
    decimal CarriedNuyen,
    decimal StartingNuyen,
    decimal NuyenRemaining,
    CharacterCreationCompletionUnusedPoints UnusedPoints,
    string ProjectionDigest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterPhysicalInitiativeProjection? PhysicalInitiative886 { get; init; }

    // Historical V2 retains its exact serialized shape and native instance IDs.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSkillGroupOrderAuthority? SkillGroupOrderAuthority { get; init; }
}

/// <summary>Exact source order and validated policy/source scope. V3 projection
/// authority includes this value and seeds native IDs with that authority. Cold
/// history derives it again from retained inputs, never trusts a supplied digest.</summary>
public sealed record CharacterCreationSkillGroupOrderAuthority(
    string Schema,
    string SkillsAuthorityDigest,
    string PurchasePolicyDigest,
    IReadOnlyList<string> ActiveSkillSourceOrder)
{
    public const string SchemaV1 = "chummer.creation-skill-group-order-authority.v1";
}

public static class CharacterCreationCompletionFinalizationRules
{
    public static string ComputeProjectionDigest(CharacterCreationCompletionFinalizationProjection projection)
        => CharacterCreationFinalizationDigest.Compute(projection with { ProjectionDigest = string.Empty });
}
