using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Trace1015 = Chummer.Application.Characters.CharacterCreationSkillsService.CompleteSkillTrace;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    internal sealed partial class CompleteSkillTrace
    {
        internal sealed record CompensationExecution1015(string ProfileId, string ProfileInputsDigest,
            string PolicyDigest, string PurchasesDigest, string ResultDigest, bool Enabled, bool ProjectGroups);
        internal CompensationExecution1015? CompensationPolicy1015 { get; private set; }

        internal void FinishCompensationPolicy1015(CharacterCreationKarmaSkillsPolicy policy,
            IReadOnlyList<CharacterCreationSkillKarmaPurchase> purchases, CharacterCreationAttributeSkillsPreview result,
            bool projectGroups)
        {
            // PreviewPurchases actually consulted this flag when choosing group
            // projection, then completed every selected interval and compensation.
            // The nullable gate must have opened; a point-only preview cannot attest it.
            if (!Completed || policy.AllowSkillRegrouping is null || result.Blockers.Count != 0
                || PurchasesDigest is null || PurchasesDigest != CharacterCreationFinalizationDigest.Compute(new
                { Policy = policy, Purchases = purchases, Result = result })) return;
            CompensationPolicy1015 = new(policy.SettingsProfileId, policy.RawProfileInputsDigest,
                policy.AuthorityDigest, PurchasesDigest, CharacterCreationFinalizationDigest.Compute(result),
                policy.CompensateSkillGroupKarmaDifference, projectGroups);
        }
    }
}

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private readonly CompletedCompensation1015? compensation1015;
    private const string CompensationField1015 = "compensateskillgroupkarmadifference";

    private sealed record CompletedCompensation1015(string ProfileXmlDigest, string LeafXmlDigest,
        string PolicyDigest, string PolicyContentDigest, Trace1015.CompensationExecution1015 Execution)
    {
        internal static CompletedCompensation1015? FromCanonical(CharacterCreationEffectivePolicyContext policy,
            CharacterCreationMysticAdeptPowerPointPolicy source, Trace1015 trace, CharacterCreationAttributeSkillsPreview result)
        {
            if (trace is not { Completed: true, CompensationPolicy1015: { } execution }
                || trace.PurchasesDigest != execution.PurchasesDigest
                || policy.SettingsProfileId != execution.ProfileId || policy.ProfileInputsDigest != execution.ProfileInputsDigest
                || execution.ResultDigest != CharacterCreationFinalizationDigest.Compute(result)) return null;
            var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            if (profile.HasAttributes
                || profile.Elements().Where(node => node.Name.LocalName == CompensationField1015).ToArray() is not [var leaf]
                || leaf.Name != CompensationField1015 || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || !bool.TryParse(leaf.Value, out bool value) || value != execution.Enabled) return null;
            return new(Xml971(profile), Xml971(leaf), policy.PolicyDigest,
                CharacterCreationFinalizationDigest.Compute(policy), execution);
        }
    }

    private sealed record BoundCompensation1015(CompletedCompensation1015 Completed, string AuthorityDigest,
        string InputsDigest, string InvocationDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundCompensation1015>
        compensationInvocations1015 = new();

    internal void BindCompensation1015(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationAttributeSkillsPreview skills)
    {
        if (compensation1015 is not { } completed || !CanAttempt(request)
            || authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || !CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || authority.InputsSnapshotDigest != inputs.SnapshotDigest
            || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
            || request.InputsSnapshotDigest != inputs.SnapshotDigest
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || invocation.RequestDigest != authority.RequestDigest || invocation.PolicyDigest != completed.PolicyDigest
            || authority.PolicyDigest != completed.PolicyDigest
            || CharacterCreationFinalizationDigest.Compute(inputs.EffectivePolicy) != completed.PolicyContentDigest
            || inputs.Skills.KarmaPurchasePolicy?.AuthorityDigest != completed.Execution.PolicyDigest
            || inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || invocation.WorkspaceId718 is null || invocation.WorkspaceId718 != inputs.OriginalCharacter?.WorkspaceId
            || invocation.CreationDraft718?.SettingsProfileId != completed.Execution.ProfileId
            || CharacterCreationFinalizationDigest.Compute(skills) != completed.Execution.ResultDigest
            || CharacterCreationFinalizationDigest.Compute(invocation.Skills718) != completed.Execution.ResultDigest
            || !invocation.TryGetCompletedConsumer("profile", out _)) return;
        compensationInvocations1015.Add(invocation, new(completed, authorityDigest, inputs.SnapshotDigest,
            InvocationBinding971(invocation)));
    }

    internal static bool TryReadCompensation1015(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string leafXmlDigest, string policyDigest,
        out bool enabled, out bool projectGroups, out string? executionDigest)
    {
        enabled = false; projectGroups = false; executionDigest = null;
        if (!compensationInvocations1015.TryGetValue(invocation, out var bound)) return false;
        var completed = bound.Completed;
        if (profileId != completed.Execution.ProfileId || profileInputsDigest != completed.Execution.ProfileInputsDigest
            || profileXmlDigest != completed.ProfileXmlDigest || leafXmlDigest != completed.LeafXmlDigest
            || policyDigest != completed.Execution.PolicyDigest || bound.InvocationDigest != InvocationBinding971(invocation)) return false;
        enabled = completed.Execution.Enabled;
        projectGroups = completed.Execution.ProjectGroups;
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-skill-group-compensation-policy", bound.AuthorityDigest, bound.InputsDigest,
            bound.InvocationDigest, Completed = completed
        });
        return true;
    }
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    public bool TryGetExecutedProfileCompensation1015(string profileId, string profileInputsDigest,
        string profileXmlDigest, string leafXmlDigest, string policyDigest,
        out bool enabled, out bool projectGroups, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadCompensation1015(this, profileId, profileInputsDigest,
            profileXmlDigest, leafXmlDigest, policyDigest, out enabled, out projectGroups, out executionDigest);
}
