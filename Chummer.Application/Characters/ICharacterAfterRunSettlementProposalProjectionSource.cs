using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public enum CharacterAfterRunSettlementProposalProjectionOutcome
{
    Available,
    NotFound,
    Conflict,
    Corrupt,
    Unavailable
}

/// <summary>
/// Exact, immutable run-proposal facts supplied by the host that owns runs,
/// approvals and GM policy. Current saved-character values deliberately do not
/// appear here: the workspace adapter projects those from the saved Chummer file.
/// </summary>
public sealed record CharacterAfterRunSettlementProposalProjection(
    CharacterAfterRunSettlementIdentity Identity,
    bool TargetOwnedByCharacter,
    bool ProjectionIsExact,
    bool RunCompleted,
    string ExpectedGmActorId,
    string ExpectedOwnerActorId,
    int CurrentHeat,
    int HeatDelta,
    int StreetCredDelta,
    int NotorietyDelta,
    int PublicAwarenessDelta,
    CharacterAfterRunSettlementSettings Settings,
    IReadOnlyList<CharacterAfterRunContactProposal> ContactProposals,
    CharacterAfterRunReview? GmReview,
    CharacterAfterRunReview? OwnerReview,
    string RawSourceState,
    string RawCustomDataState,
    string RawGmPolicyState,
    string RawRuntimeState);

public sealed record CharacterAfterRunSettlementProposalProjectionRequest(
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    CharacterAfterRunSettlementIdentity Identity,
    string CharacterProjectionDigest);

public sealed record CharacterAfterRunSettlementProposalProjectionResult(
    CharacterAfterRunSettlementProposalProjectionOutcome Outcome,
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    string CharacterProjectionDigest,
    CharacterAfterRunSettlementProposalProjection? Projection = null,
    string? Error = null);

/// <summary>
/// Bounded host seam for authoritative run proposals and their reviews. Core has
/// no run-proposal persistence of its own, so the default implementation is
/// unavailable. An available response must echo the exact workspace revision and
/// ledger-free saved-character digest from the request.
/// </summary>
public interface ICharacterAfterRunSettlementProposalProjectionSource
{
    CharacterAfterRunSettlementProposalProjectionResult Read(
        CharacterAfterRunSettlementProposalProjectionRequest request);
}
