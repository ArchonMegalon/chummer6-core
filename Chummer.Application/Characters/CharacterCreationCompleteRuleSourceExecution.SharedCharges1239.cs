using System.Xml;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private sealed record SharedCharges1239(string Authority, string Graph, string Effects, string Lifecycle,
        CharacterCreationCompletionPoolBalances Before, CharacterCreationCompletionPoolBalances After,
        FocusBudget1220 Focus, Budget1221 Magic, string Digest);
    private SharedCharges1239? sharedCharges1239;

    internal CharacterCreationCompletionReview ApplySharedChargesFull1239(CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority, CancellationToken cancellationToken)
    {
        if (!CanExecuteSharedCharges1339 || review.Blockers.Count != 0) return review;
        var effects = CharacterCreationSelectedEffectsConsumer.FromFull(Inputs, review, authority, cancellationToken,
            WantsEssence1222(request) && CharacterCreationCompletionReviewRules.HasValidEssenceResult(request) ? this : null);
        if (!TryExecuteSharedCharges1239(authority, effects, review.Balances, out var after, out var blocker))
            return review with { Balances = after, Blockers = [.. review.Blockers, blocker] };
        return review with { Balances = after };
    }

    internal bool ApplySharedChargesSelected1239(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationSelectedCompletionEvaluation selected, CharacterCreationCompleteAssessment authority,
        IReadOnlyList<CharacterCreationLoadoutInstance>? originalLoadoutInstances,
        CharacterCreationOriginalCharacterSource? originalCharacter, CancellationToken cancellationToken,
        out CharacterCreationSelectedCompletionEvaluation result, out string blocker)
    {
        result = selected;
        blocker = string.Empty;
        if (!CanExecuteSharedCharges1339) return true;
        var effects = CharacterCreationSelectedEffectsConsumer.FromSelected(witness, request, selected,
            authority, originalLoadoutInstances, cancellationToken, originalCharacter,
            WantsEssence1222(request) && CharacterCreationCompletionReviewRules.HasValidEssenceResult(request) ? this : null);
        if (!TryExecuteSharedCharges1239(authority, effects, selected.Balances, out var after, out blocker)) return false;
        result = selected with { Balances = after };
        return true;
    }

    private bool TryExecuteSharedCharges1239(CharacterCreationCompleteAssessment authority,
        CharacterCreationSelectedEffectsConsumer? effects, CharacterCreationCompletionPoolBalances before,
        out CharacterCreationCompletionPoolBalances after, out string blocker)
    {
        after = before with { RemainingCreationKarma = null, KarmaOverspend = null };
        blocker = CharacterCreationFocusKarma1220.Unavailable;
        try
        {
            if (!MatchesFocusAuthority1220(authority) || !MatchesRetainedAuthority1221(authority)
                || request.Selections.Gear is null || request.Selections.Loadout is null
                || original.CreationLifecycleDigestFor(authority) is not { } lifecycle
                || effects?.DigestFor(authority) is not { } effectsDigest || before.RemainingCreationKarma is null
                || !TryFocusProfile1220(Inputs.EffectivePolicy!, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
                    out var profile, out _)) return false;
            var graph = effects.CopyCompiledGraph();
            string authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
            string graphDigest = XmlDigest1221(graph);
            // No public balance can install or overwrite a charge result. Repeated
            // entry with the same original pool reuses the private operation;
            // entering with an already charged pool is rejected, never debited twice.
            if (sharedCharges1239 is { } existing)
            {
                if (existing.Authority != authorityDigest || existing.Graph != graphDigest
                    || existing.Effects != effectsDigest || existing.Lifecycle != lifecycle
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(existing.Before, before)) return false;
                after = existing.After;
                blocker = string.Empty;
                return true;
            }
            if (!effects!.TryPriceFocus1253(profile!, out var focus)) return false;
            blocker = CharacterCreationRetainedMagicKarma1221.Unavailable;
            if (!CharacterCreationRetainedMagicKarma1221.TryEvaluate(profile!, graph, out var magic)) return false;
            var postFocus = CharacterCreationFocusKarma1220.Spend(before, focus!);
            int remaining = checked(postFocus.RemainingCreationKarma!.Value - magic!.Cost);
            after = postFocus with { RemainingCreationKarma = remaining, KarmaOverspend = remaining < 0 ? checked(-remaining) : 0 };
            if (remaining < 0) { blocker = CharacterCreationFinalizationBlockers.GlobalKarmaExceeded; return false; }
            if (request.Selections.KarmaToReserve < 0 || remaining < request.Selections.KarmaToReserve)
            { blocker = CharacterCreationCompletionPlanBlockers.KarmaReserveRequired; return false; }
            string digest = CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "canonical-focus-and-retained-magic-wallet1239", Authority = authorityDigest,
                Inputs.SnapshotDigest, Inputs.OriginalCharacter, Request = request, Lifecycle = lifecycle,
                Effects = effectsDigest, Graph = graphDigest, Focus = focus, Magic = magic,
                Before = before, PostFocus = postFocus, After = after
            });
            focusBudget1220 = new(authorityDigest, effectsDigest, lifecycle, focus!, before, postFocus, digest);
            retainedBudget1221 = new(authorityDigest, graphDigest, effectsDigest, XmlDigest1221(profile!),
                Inputs.EffectivePolicy!.ProfileInputsDigest, postFocus, after, magic);
            sharedCharges1239 = new(authorityDigest, graphDigest, effectsDigest, lifecycle,
                before, after, focusBudget1220, retainedBudget1221, digest);
            blocker = string.Empty;
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or OverflowException or XmlException)
        { return false; }
    }

    private bool ApplySharedWallet1239(CharacterCreationCompleteAssessment authority,
        CharacterCreationRuleSourceScopeInvocation invocation, ref int remaining, out string? execution)
    {
        execution = null;
        if (!CanExecuteSharedCharges1339) return true;
        if (sharedCharges1239 is not { } wallet || !MatchesFocusAuthority1220(authority)
            || wallet.Authority != CharacterCreationFinalizationDigest.Compute(authority)
            || wallet.Graph != invocation.GraphDigest || invocation.RequestDigest != requestDigest
            || invocation.PolicyDigest != authority.PolicyDigest || invocation.CaptureDigest != Inputs.RuleSourceCaptureDigest
            || wallet.Lifecycle != original.CreationLifecycleDigestFor(authority)
            || !ReferenceEquals(wallet.Focus, focusBudget1220) || !ReferenceEquals(wallet.Magic, retainedBudget1221)
            || remaining != wallet.Before.RemainingCreationKarma || wallet.After.RemainingCreationKarma is not int after) return false;
        remaining = after;
        execution = wallet.Digest;
        return true;
    }
}
