using System.Xml;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLifestylesDraftRules
{
    // Historical reads prove the original compiler on the same source/profile
    // and the exact confirmed draft. They do not renew the draft or its receipt.
    internal static bool TryLoadHistoricalForReview(WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver source, out CharacterCreationLifestylesDraft? historical,
        out WorkspaceDocument? projected, out CharacterCreationLifestylesAuthority authority,
        out CharacterCreationLifestyleReceipt? receipt, out CharacterCreationLifestylesAuthority? reviewAuthority)
    {
        historical = null; projected = null; receipt = null; reviewAuthority = null;
        authority = CharacterCreationLifestylesAuthority.Unavailable;
        try
        {
            var state = workspace.Document.AuxiliaryState;
            if (state.CharacterCreationCompletionDrafts is not null
                || state.CharacterCreationLifestylesDraft is not { } saved
                || !IsValidShape(workspace.Id, workspace.ContentRevision, saved)
                || state.CharacterCreationLifestyleReceipts is not { Count: > 0 } ledger
                || !CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(workspace.Id,
                    workspace.ContentRevision, ledger)
                || !EqualReview(saved, ledger[^1].Receipt.WritePlan.PendingDraft)
                || !CharacterCreationContactsDraftRules.TryLoad(workspace, source, out var contacts, out _)
                || contacts is null) return false;
            var inputs = CharacterCreationContactsDraftRules.Create(workspace, contacts.Policy, contacts.CarryoverPolicy, []);
            if (inputs is null || !CharacterCreationContactsDraftRules.TryProject(workspace, inputs, out var basis)
                || basis is null || source.TryCreateContext(basis.Content) is not { } context
                || !context.TryResolveCreationLifestylesPre1008Authority(out var oldAuthority)
                || !CharacterCreationLifestylesRules.IsValidAuthority(oldAuthority)) return false;
            var reproduced = Create(workspace, inputs, oldAuthority, saved.Lifestyles);
            if (reproduced is null || !InputsMatch(saved, reproduced)
                || !EqualReview(saved.ProjectionAuthority, reproduced.ProjectionAuthority)
                || !TryProject(workspace, saved, out projected, out _, out var budget)
                || projected is null || budget is null || budget.Overspend != 0
                || ledger[^1].Receipt.LifestyleCostAfter != budget.Used
                || ledger[^1].Receipt.LifestyleBudgetRemaining != budget.Remaining
                || ledger[^1].Receipt.SourceDigest != oldAuthority.SourceDigest
                || ledger[^1].Receipt.RuntimeDigest != oldAuthority.RuntimeDigest
                || !context.TryResolveCreationLifestylesPre1008Authority(out var after)
                || !EqualReview(oldAuthority, after)) return false;
            // Expose new options for a deliberate review even when the old
            // selections need correction under the new compiler. No choices are applied.
            try
            {
                if (context.TryResolveCreationLifestylesAuthority(out var current)
                    && CharacterCreationLifestylesRules.IsValidAuthority(current)
                    && SameReviewSource(oldAuthority, current) && current.AuthorityDigest != oldAuthority.AuthorityDigest)
                    reviewAuthority = current;
            }
            catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
                or IOException or OverflowException)
            {
                // New grammar may reject old sources. Keep the verified historical
                // view, but offer no new authority; re-admit old source bytes below.
                reviewAuthority = null;
            }
            if (!context.TryResolveCreationLifestylesPre1008Authority(out after)
                || !EqualReview(oldAuthority, after)) return false;
            historical = saved; authority = oldAuthority; receipt = ledger[^1].Receipt;
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or IOException or OverflowException)
        { historical = null; projected = null; receipt = null; reviewAuthority = null; authority = CharacterCreationLifestylesAuthority.Unavailable; return false; }
    }

    internal static bool TryPrepareAuthorityReview(WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver source, CharacterCreationLifestyleAuthorityReviewRequest request,
        out CharacterCreationLifestylesDraft? next, out CharacterCreationLifestyleAuthorityTransition? transition,
        out CharacterCreationLifestyleBudget? beforeBudget, out CharacterCreationLifestyleBudget? afterBudget)
    {
        next = null; transition = null; beforeBudget = null; afterBudget = null;
        if (!CharacterCreationLifestylesRules.IsCanonicalDigest(request.HistoricalReceiptDigest)
            || !CharacterCreationKarmaLifestylesRules.TryFreeze(request.Lifestyles, out var selections)
            || !TryLoadHistoricalForReview(workspace, source, out var old, out _, out var oldAuthority, out var receipt, out _)
            || old is null || receipt is null || receipt.ReceiptDigest != request.HistoricalReceiptDigest
            || !CharacterCreationContactsDraftRules.TryLoad(workspace, source, out var contacts, out _)
            || contacts is null) return false;
        var inputs = CharacterCreationContactsDraftRules.Create(workspace, contacts.Policy, contacts.CarryoverPolicy, []);
        if (inputs is null || !CharacterCreationContactsDraftRules.TryProject(workspace, inputs, out var basis)
            || basis is null || source.TryCreateContext(basis.Content) is not { } context
            || !context.TryResolveCreationLifestylesPre1008Authority(out var observedOld)
            || !EqualReview(oldAuthority, observedOld)
            || !context.TryResolveCreationLifestylesAuthority(out var current)
            || !CharacterCreationLifestylesRules.IsValidAuthority(current)
            || !SameReviewSource(oldAuthority, current)
            || old.SourceAuthorityDigest == current.AuthorityDigest) return false;
        next = Create(workspace, inputs, current, selections);
        if (next is null || !SameReviewUpstream(old, next)
            || !TryProject(workspace, old, out _, out _, out beforeBudget)
            || !TryProject(workspace, next, out _, out _, out afterBudget)
            || beforeBudget is null || afterBudget is null || !beforeBudget.IsExact || !afterBudget.IsExact
            || beforeBudget.Total != afterBudget.Total || afterBudget.Overspend != 0
            || !context.TryResolveCreationLifestylesAuthority(out var observedCurrent)
            || !EqualReview(current, observedCurrent)
            || !context.TryResolveCreationLifestylesPre1008Authority(out observedOld)
            || !EqualReview(oldAuthority, observedOld)) return false;
        transition = new(CharacterCreationLifestyleCompilerVersions.PreLocations1008,
            CharacterCreationLifestyleCompilerVersions.Locations1008, receipt.ReceiptDigest, old, current);
        return true;
    }

    // Deliberate compiler transition only. Ordinary InputsMatch remains exact.
    internal static bool SameReviewUpstream(CharacterCreationLifestylesDraft old, CharacterCreationLifestylesDraft next)
        => old.WorkspaceId == next.WorkspaceId
            && CharacterCreationContactsDraftRules.InputsMatch(old.InputAuthority, next.InputAuthority)
            && old.SkillsDraftDigest == next.SkillsDraftDigest && old.ResourcesDraftDigest == next.ResourcesDraftDigest
            && old.GearDraftDigest == next.GearDraftDigest && old.MagicDraftDigest == next.MagicDraftDigest;

    internal static bool SameReviewSource(CharacterCreationLifestylesAuthority old, CharacterCreationLifestylesAuthority current)
        => old.RulesetId == current.RulesetId && old.SettingsProfileId == current.SettingsProfileId
            && old.SourceDigest == current.SourceDigest && old.ProfileDigest == current.ProfileDigest
            && old.GmPolicyDigest == current.GmPolicyDigest && old.RuntimeDigest == current.RuntimeDigest;

    internal static bool EqualReview<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}
