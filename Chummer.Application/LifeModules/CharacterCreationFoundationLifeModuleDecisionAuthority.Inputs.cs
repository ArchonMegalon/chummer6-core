using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public sealed partial class CharacterCreationFoundationLifeModuleDecisionAuthority
{
    private LifeModuleDecisionAuthorityResult<LifeModuleDecisionInputResolution> ResolveFoundationInputs(
        LifeModuleDecisionAuthorityStep step, CharacterCreationFoundationState state,
        DecisionCandidate candidate, IReadOnlyDictionary<string, string>? values)
    {
        if (!LifeModuleDecisionInputIntegrity.TryNormalize(values, out var normalized))
            return Invalid<LifeModuleDecisionInputResolution>();
        var projected = _foundation.Preview(new(state.Binding, candidate.FoundationPreview.RequestedMetatype,
            candidate.Selection, normalized));
        if (projected.Value is not { CanConfirm: true, CanApply: true } preview || projected.Blockers.Count != 0)
            return FromFoundation<CharacterCreationFoundationPreview, LifeModuleDecisionInputResolution>(projected);
        var mechanics = candidate.Choice.MechanicsPreview with
        {
            PendingFollowUpIds = [],
            Items = [.. candidate.Choice.MechanicsPreview.Items,
                .. preview.Diff.Where(item => item.Domain == "life-module-follow-up").Select(item =>
                    new LifeModuleMechanicsPreviewItem(item.DiffId, item.Domain, item.TargetId,
                        item.BeforeValue ?? string.Empty, item.AfterValue ?? string.Empty,
                        0, item.SourceAnchorIds, string.Empty))]
        };
        return Success(LifeModuleDecisionInputIntegrity.Seal(new(step.WorkspaceId, step.WorkspaceRevision,
            candidate.Choice.ChoiceId, step.DecisionDigest, candidate.Choice.DecisionCommandDigest,
            preview.FollowUpValues, preview.PreviewDigest, mechanics, string.Empty)));
    }
}
