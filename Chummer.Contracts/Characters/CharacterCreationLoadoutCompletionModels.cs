using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationLoadoutCompletionSchemas
{
    public const string InputsV1 = "chummer.creation-loadout-completion-inputs.v1";
    public const string SnapshotConflict = "creation-loadout-completion-snapshot-conflict";
    public const string ExistingGraphUnsupported = "creation-loadout-existing-graph-unsupported";
    public const string AuthorityUnavailable = "creation-loadout-completion-authority-unavailable";
    public const string RequestInvalid = "creation-loadout-completion-request-invalid";
}

public sealed record CharacterCreationLoadoutBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest);

/// <summary>One real original workspace/source observation. Neither this value
/// nor its catalog is an owner capability or permission to mutate a runner.</summary>
public sealed record CharacterCreationLoadoutCompletionInputs(
    string Schema,
    CharacterCreationLoadoutBinding Binding,
    CharacterCreationLoadoutAuthority Authority,
    CharacterCreationLoadoutSelections OriginalSelections,
    string SnapshotDigest);

/// <summary>Source-priced typed weapon and armor purchases, separate from Gear.
/// Funding is checked by the whole proposal, not by spending starting cash.</summary>
public sealed record CharacterCreationLoadoutCompletionProjection(
    CharacterCreationLoadoutSelections Selections,
    IReadOnlyList<CharacterCreationLoadoutLine> Lines,
    decimal PurchaseCost,
    IReadOnlyList<string> Blockers,
    string ProjectionDigest);

public static class CharacterCreationLoadoutCompletionRules
{
    public static string ComputeInputsDigest(CharacterCreationLoadoutCompletionInputs value)
        => CharacterCreationFinalizationDigest.Compute(value with { SnapshotDigest = string.Empty });

    public static string ComputeProjectionDigest(CharacterCreationLoadoutCompletionProjection value)
        => CharacterCreationFinalizationDigest.Compute(value with { ProjectionDigest = string.Empty });
}
