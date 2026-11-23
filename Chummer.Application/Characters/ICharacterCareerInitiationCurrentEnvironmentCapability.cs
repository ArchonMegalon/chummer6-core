using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Typed store operations under the caller's live owner lease. No supplied totals or source proof.</summary>
public interface ICharacterCareerInitiationCurrentEnvironmentCapability
{
    WorkspaceStoreReadResult ReadCareerInitiationWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId workspace);
    CharacterCareerInitiationQuote? QuoteCareerInitiation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId workspace, CharacterCareerInitiationSelection selection,
        ICharacterCareerInitiationAuthorityResolver authority);
    CharacterCareerInitiationResult CommitCareerInitiation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerInitiationPlan plan, ICharacterCareerInitiationAuthorityResolver authority,
        CancellationToken cancellationToken = default);
    // false means the ordinary non-retained read may run; true/null is a closed refusal.
    bool TryReadCurrentCareerMagic5722(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId workspace, ICharacterSourceDataResolver sources, out CharacterCareerMagicRead? read);
}
