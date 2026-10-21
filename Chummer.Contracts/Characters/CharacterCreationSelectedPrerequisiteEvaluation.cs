namespace Chummer.Contracts.Characters;

/// <summary>Bounded diagnostic output of canonical selected-grant execution; never admission authority.</summary>
public sealed record CharacterCreationSelectedPrerequisiteEvaluation(
    string Schema,
    string InputsSnapshotDigest,
    string SelectionsDigest,
    string OriginalAttributesDigest,
    string ProposedAttributesDigest,
    string PurchasedQualitiesDigest,
    string FinalQualitiesDigest,
    IReadOnlyList<string> EnabledFlags,
    IReadOnlyList<CharacterCreationSelectedPrerequisiteCheck> Checks)
{
    public const string SchemaV1 = "chummer.creation-selected-prerequisites.v1";
    public const int MaximumChecks = 64;
}

public sealed record CharacterCreationSelectedPrerequisiteCheck(
    string Origin, string SourceId, string SourceDigest);
