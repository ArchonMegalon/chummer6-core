using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Trace940 = Chummer.Application.Characters.CharacterCreationSkillsService.CompleteSkillTrace;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private readonly CompletedBrokenGroup940? brokenGroup940;

    // Constructed only from the successful canonical trace, after ExecuteSkills
    // matched the entire proposed result. This is never an imported receipt.
    private sealed record CompletedBrokenGroup940(string ProfileId, string ProfileInputsDigest,
        string ProfileXmlDigest, string LeafXmlDigest, string PolicyDigest, string PolicyContentDigest,
        string SkillsDigest, Trace940.GroupPolicyScope Group)
    {
        internal static CompletedBrokenGroup940? FromCanonical(CharacterCreationEffectivePolicyContext policy,
            CharacterCreationMysticAdeptPowerPointPolicy source, CharacterCreationSelectedConsumerPolicy selected,
            Trace940 trace, CharacterCreationAttributeSkillsPreview result)
        {
            var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            if (trace is not { Completed: true, GroupPolicy: { } group }
                || profile.Elements("usepointsonbrokengroups").ToArray() is not [var leaf]
                || leaf.HasAttributes || leaf.HasElements || leaf.Nodes().Any(node => node.NodeType != System.Xml.XmlNodeType.Text)
                || !CharacterCreationSkillGroupPolicyAuthority.TryResolve(profile, out bool broken,
                    out bool strict, out bool specializations)
                || broken != group.UsePointsOnBrokenGroups || strict != group.StrictGroups
                || specializations != group.SpecializationsBreakGroups
                || broken != selected.Skills.UsePointsOnBrokenGroups) return null;
            return new(policy.SettingsProfileId, policy.ProfileInputsDigest,
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                policy.PolicyDigest, CharacterCreationFinalizationDigest.Compute(policy),
                CharacterCreationFinalizationDigest.Compute(result),
                group with { Checks = Array.AsReadOnly(group.Checks.ToArray()) });
        }
    }

    private sealed record BoundBrokenGroup940(CompletedBrokenGroup940 Completed,
        string AuthorityDigest, string InputsDigest, string InvocationDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundBrokenGroup940>
        brokenGroupInvocations940 = new();

    // Complete's caller has matched canonical qualities and the compiled graph.
    // Bind all mutable projections too, so mutation after registration fails closed.
    internal void BindBrokenGroup940(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationAttributeSkillsPreview skills)
    {
        if (brokenGroup940 is not { } completed || authority.Phase != CharacterCreationCompletePhase.Authority
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || authority.InputsSnapshotDigest != inputs.SnapshotDigest
            || request.InputsSnapshotDigest != inputs.SnapshotDigest
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || invocation.RequestDigest != authority.RequestDigest
            || invocation.PolicyDigest != completed.PolicyDigest || authority.PolicyDigest != completed.PolicyDigest
            || inputs.EffectivePolicy is not { } policy
            || CharacterCreationFinalizationDigest.Compute(policy) != completed.PolicyContentDigest
            || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest
            || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || invocation.CreationDraft718?.SettingsProfileId != completed.ProfileId
            || CharacterCreationFinalizationDigest.Compute(skills) != completed.SkillsDigest
            || CharacterCreationFinalizationDigest.Compute(invocation.Skills718) != completed.SkillsDigest
            || !invocation.TryGetCompletedConsumer("profile", out _)) return;
        brokenGroupInvocations940.Add(invocation, new(completed, authorityDigest, inputs.SnapshotDigest,
            InvocationBinding940(invocation)));
    }

    private static string InvocationBinding940(CharacterCreationRuleSourceScopeInvocation invocation)
    {
        invocation.TryGetCompletedConsumer("profile", out var consumer);
        return CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            Consumer = consumer, invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718,
            invocation.Attributes718, invocation.AttributePolicy718, invocation.Skills718,
            invocation.SourceExecution832, invocation.CompiledMovement832,
            invocation.PhysicalAuthority886, invocation.PhysicalInitiative886
        });
    }

    internal static bool TryReadBrokenGroup940(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string leafXmlDigest,
        out bool usePoints, out bool noTarget,
        out IReadOnlyList<(string TargetId, int GroupRating, bool PointSpecialization)> checks,
        out string? executionDigest)
    {
        usePoints = false; noTarget = false; checks = []; executionDigest = null;
        if (!brokenGroupInvocations940.TryGetValue(invocation, out var bound)) return false;
        var completed = bound.Completed;
        if (profileId != completed.ProfileId || profileInputsDigest != completed.ProfileInputsDigest
            || profileXmlDigest != completed.ProfileXmlDigest || leafXmlDigest != completed.LeafXmlDigest
            || bound.InvocationDigest != InvocationBinding940(invocation)) return false;
        usePoints = completed.Group.UsePointsOnBrokenGroups;
        noTarget = !completed.Group.Checks.Any(check => check.GroupRating > 0);
        checks = Array.AsReadOnly(completed.Group.Checks.Select(check =>
            (check.TargetId, check.GroupRating, check.PointSpecialization)).ToArray());
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-broken-group-policy", bound.AuthorityDigest, bound.InputsDigest,
            bound.InvocationDigest, Completed = completed, NoTarget = noTarget
        });
        return true;
    }
}
