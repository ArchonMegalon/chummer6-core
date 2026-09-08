using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Local Career wizard authority. No run proposal or GM identity is synthesized.</summary>
public interface ICharacterCareerReputationService
{
    CharacterCareerReputationReadResult Read(CharacterWorkspaceId workspaceId);
    CharacterCareerReputationPreviewResult Preview(CharacterCareerReputationRequest request);
    CharacterCareerReputationResult Commit(CharacterCareerReputationCommand command, CancellationToken cancellationToken = default);
    CharacterCareerReputationResult Lookup(CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest);
}

/// <summary>
/// Dedicated trusted store seam, not a general replacement-document channel.
/// The Core-composed resolver is an executable dependency, never request/model
/// data. Implementations must reload, quote and revalidate it under the same
/// workspace lease used by all other writes, and append only their own receipt.
/// Generic auxiliary replacement is not permission to modify this ledger.
/// </summary>
public interface ICharacterCareerReputationAtomicCommitCapability
{
    string CareerReputationRuntimeDigest { get; }
    CharacterCareerReputationResult CommitCareerReputation(
        CharacterCareerReputationCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default);
}
