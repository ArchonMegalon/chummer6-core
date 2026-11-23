using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionReviewService
{
    private static CharacterCreationCompletionPoolBalances EvaluateSharedCharges1239(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompleteAssessment? authority, CharacterCreationCompleteRuleSourceExecution? execution,
        CharacterCreationAttributeSkillsPreview? skills, CharacterCreationCompletionBudgetPreview? upstream,
        CharacterCreationLifestylesCompletionProjection? lifestyles, CharacterCreationContactsCompletionProjection? contacts,
        CharacterCreationLoadoutCompletionProjection? loadout, CharacterCreationCompletionPoolBalances balances,
        List<string> blockers, CancellationToken cancellationToken)
    {
        if (authority is null || execution is null || !execution.CanExecuteSharedCharges1339
            || blockers.Any(value => value != CharacterCreationFinalizationBlockers.GlobalKarmaExceeded
                && value != CharacterCreationCompletionPlanBlockers.KarmaReserveRequired)) return balances;
        // Compilation needs a mechanically shaped candidate. The wallet verdict
        // is deliberately deferred until after focus prices (including negative
        // legacy adjustments) have been applied to that same wallet.
        var candidate = new CharacterCreationCompletionReview(CharacterCreationCompletionReviewSchemas.ReviewV3,
            inputs.SnapshotDigest, request, skills, upstream, lifestyles, contacts, balances, [], string.Empty)
            { Loadout = loadout };
        var priced = execution.ApplySharedChargesFull1239(candidate, authority, cancellationToken);
        blockers.RemoveAll(value => value == CharacterCreationFinalizationBlockers.GlobalKarmaExceeded
            || value == CharacterCreationCompletionPlanBlockers.KarmaReserveRequired);
        blockers.AddRange(priced.Blockers);
        if (priced.Balances.RemainingCreationKarma < 0)
            blockers.Add(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
        return priced.Balances;
    }
}
