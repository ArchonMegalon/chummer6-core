using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Fail-closed default because Core does not own authoritative run proposals,
/// GM approvals or owner approvals.
/// </summary>
public sealed class UnavailableCharacterAfterRunSettlementProposalProjectionSource :
    ICharacterAfterRunSettlementProposalProjectionSource
{
    private const string Error =
        "An authoritative After Run proposal projection source is not configured.";

    public CharacterAfterRunSettlementProposalProjectionResult Read(
        CharacterAfterRunSettlementProposalProjectionRequest request)
        => new(
            CharacterAfterRunSettlementProposalProjectionOutcome.Unavailable,
            request.WorkspaceId,
            request.WorkspaceRevision,
            request.CharacterProjectionDigest,
            Error: Error);
}
