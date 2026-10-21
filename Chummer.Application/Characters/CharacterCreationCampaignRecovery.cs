using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Private persistence input. This is a historical observation, never live
// campaign authority. Receipt identity is checked independently of the archive.
public sealed record CharacterCreationCommittedCampaign(
    string Schema, CharacterCreationCampaignBinding Binding,
    CharacterCreationCampaignState State, string ReceiptDigest)
{
    public const string SchemaV1 = "chummer.creation-committed-campaign.v1";
    public override string ToString() => nameof(CharacterCreationCommittedCampaign);
}

/// <summary>Composes live receipt construction, or private committed history
/// during cold reads, with the existing source recovery visitor.</summary>
public sealed class CharacterCreationCampaignRecovery(
    CharacterCreationCampaignExecutionContext campaign,
    ICharacterCreationRuleSourceRecovery? sources = null) : ICharacterCreationRuleSourceRecovery
{
    // Shape validation only, not permission. Private records still have to match
    // their owner/path/incarnation, exact receipt and independently replayed graph.
    public static bool IsWellFormed(CharacterCreationCommittedCampaign? committed)
        => committed is { Schema: CharacterCreationCommittedCampaign.SchemaV1 }
            && CharacterCreationCampaignExecutionContext.ValidBinding(committed.Binding)
            && CharacterCreationFinalizationDigest.IsCanonical(committed.ReceiptDigest)
            && committed.State is CharacterCreationCampaignState.AuthenticatedAbsence
                or CharacterCreationCampaignState.AdmittedUnchangedPolicy;

    // Trusted store composition only, after owner/path/incarnation/receipt
    // validation. No host or endpoint accepts this input from a request.
    public static ICharacterCreationRuleSourceRecovery FromCommitted(
        CharacterCreationCommittedCampaign committed, ICharacterCreationRuleSourceRecovery sources)
        => new CommittedRecovery(committed, sources);

    // Only live draft construction uses the new exact receipt plus earlier
    // private history. Ordinary FromCommitted keeps its single-receipt contract.
    internal static ICharacterCreationRuleSourceRecovery WithCommittedDraft(
        CharacterCreationCommittedCampaign committed, ICharacterCreationRuleSourceRecovery sources)
        => new CommittedRecovery(committed, sources, includeEarlierReceipts: true);

    private sealed class CommittedRecovery(CharacterCreationCommittedCampaign committed,
        ICharacterCreationRuleSourceRecovery sources, bool includeEarlierReceipts = false) : ICharacterCreationRuleSourceRecovery
    {
        public bool ValidateRetainedRuleSources() => sources.ValidateRetainedRuleSources();
        public bool TryRecoverRuleSources(string digest, out CharacterCreationRuleSourceExecutionContext? result)
            => sources.TryRecoverRuleSources(digest, out result);
        public bool TryRecoverCampaignAuthority(CharacterCreationCampaignBinding binding, string receiptDigest,
            out CharacterCreationCampaignExecutionContext? result)
        {
            result = null;
            if (includeEarlierReceipts && committed.ReceiptDigest != receiptDigest)
                return sources.TryRecoverCampaignAuthority(binding, receiptDigest, out result);
            if (!IsWellFormed(committed) || !ValidateRetainedRuleSources()
                || committed.Binding != binding || committed.ReceiptDigest != receiptDigest) return false;
            result = CharacterCreationCampaignExecutionContext.RecoverCommitted(committed);
            return true;
        }
    }

    public bool ValidateRetainedRuleSources() => sources?.ValidateRetainedRuleSources() ?? true;

    public bool TryRecoverRuleSources(string captureDigest, out CharacterCreationRuleSourceExecutionContext? execution)
    {
        execution = null;
        return sources?.TryRecoverRuleSources(captureDigest, out execution) ?? false;
    }

    public bool TryRecoverCampaignAuthority(CharacterCreationCampaignBinding binding, string receiptDigest,
        out CharacterCreationCampaignExecutionContext? execution)
    {
        execution = null;
        // A later Career commit may contain earlier campaign-bound drafts.
        // Their exact private observations must not be replaced by this live phase.
        if (sources?.TryRecoverCampaignAuthority(binding, receiptDigest, out execution) == true) return true;
        if (campaign is null || campaign.Phase == CharacterCreationCampaignPhase.Preparation
            || campaign.Revalidate(binding) is not
                { State: CharacterCreationCampaignState.AuthenticatedAbsence or CharacterCreationCampaignState.AdmittedUnchangedPolicy,
                    Failure: CharacterCreationCampaignFailure.None })
            return false;
        execution = campaign;
        return true;
    }
}
