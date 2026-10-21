using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Workspaces;

/// <summary>Detached completion inputs from one fenced retained observation.</summary>
public sealed record WorkspaceContinuationCompletionResult(
    CharacterCreationCompletionReviewInputs? Inputs,
    WorkspaceContinuationCompletionIdentity? Identity,
    IReadOnlyList<string> AuthorityBlockers,
    IReadOnlyList<string> ReadinessBlockers)
{
    // Readiness can include missing persistence; this reader never grants it.
    public bool Success => Inputs is not null && Identity is not null && AuthorityBlockers.Count == 0;
    public bool HasPersistenceAuthority { get; }
    public bool CompletePolicyCoverage { get; }
}

/// <summary>
/// Content comparison identity, not provenance or permission to restore/apply.
/// ObservationDigest hashes this record with ObservationDigest empty.
/// </summary>
public sealed record WorkspaceContinuationCompletionIdentity(
    string OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string ContinuationDigest,
    string SourceDigest,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    string InputsDigest,
    string PolicyDigest,
    string ObservationDigest);
