using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationKarmaMetatypeService
{
    public CharacterCreationFoundationResult<CharacterCreationFinalizationReview> ReviewFinalization(
        CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int diceTotal)
    {
        var read = _workspaceStore.Get(binding.WorkspaceId);
        if (read.Value is not { } workspace || workspace.ContentRevision != binding.ContentRevision
            || workspace.SavedRevision != binding.SavedRevision || workspace.Document.AuxiliaryStateDigest != binding.AuxiliaryStateDigest)
            return Blocked<CharacterCreationFinalizationReview>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
        if (!CharacterCreationKarmaFinalizationTransaction.TryAdmit(OwnerScope.LocalSingleUser, workspace,
                _sourceDataResolver, diceTotal, out var authority, out var review, out _, out var blockers))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, blockers);
        if (authority!.Foundation.Binding != binding || authority.Foundation.QuoteDigest != foundationQuoteDigest
            || _workspaceStore.Get(binding.WorkspaceId).Value is not { } final
            || final.ContentRevision != binding.ContentRevision || final.SavedRevision != binding.SavedRevision
            || final.Document.AuxiliaryStateDigest != binding.AuxiliaryStateDigest
            || final.Document.Content != workspace.Document.Content)
            return Blocked<CharacterCreationFinalizationReview>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
        return new(CharacterCreationFoundationOutcomes.Success, review, []);
    }

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> ConfirmFinalization(
        CharacterCreationKarmaFinalizationConfirmRequest request)
    {
        if (!CharacterCreationKarmaFinalizationTransaction.IsConfirmed(request))
            return CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        return _workspaceStore is ICharacterCreationKarmaFinalizationAtomicCommitCapability capability
            ? capability.CommitKarmaFinalization(request, _sourceDataResolver)
            : CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.AtomicPersistenceRequired);
    }
}
