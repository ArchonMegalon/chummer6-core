using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Called only by role assessment of its independently finalized baseline.
    // The review supplies paid/granted ranks and source-derived natural attributes;
    // the routine boundary rejects effects it cannot apply to the chosen action.
    private IReadOnlyList<CharacterCreationRoleFinding> AssessMatrixRoutine3470(
        CharacterCreationRoleRoutineObjective routine, CharacterCreationCompletionBaseline baseline)
    {
        var final = baseline.Finalization;
        var review = baseline.Plan.Review.Skills;
        if (review is null || review.Blockers.Count != 0)
            return [Unknown("matrix-routine-authority", "The finalized creation attribute/skill review is unavailable.")];
        var attributes = review.Attributes.Attributes.Where(a => a.IsEnabled)
            .ToDictionary(a => a.AttributeId, a => a.UnaugmentedCurrent, StringComparer.Ordinal);
        var selections = baseline.Plan.Selections.MagicSelections;
        var forms = Inputs.Magic.Authority.ComplexForms.Where(o => o.IsEnabled
            && o.Identity == routine.MagicIdentity && selections.ComplexForms.Contains(o.Identity)).ToArray();
        var streams = Inputs.Magic.Authority.Streams.Where(o => o.IsEnabled
            && o.Identity == selections.Stream).ToArray();
        using var operation = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var context = (operation ?? resolution.Resolver).TryCreateContext(final.CharacterXml);
        if (context is null)
            return [Unknown("matrix-routine-source", "The retained source context cannot resolve this finalized owner graph.")];
        return CharacterCreationMatrixRoutineRules3470.Evaluate(routine, RoleXml(final.CharacterXml),
            attributes, review.Skills, context, final.ProjectionDigest,
            forms.Length == 1 ? forms[0] : null, streams.Length == 1 ? streams[0] : null,
            original.PriorityGrants3563(context));
    }
}
