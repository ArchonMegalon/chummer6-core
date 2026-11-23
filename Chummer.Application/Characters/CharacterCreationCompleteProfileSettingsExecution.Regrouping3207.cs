using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Trace3207 = Chummer.Application.Characters.CharacterCreationSkillsService.CompleteSkillTrace;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private readonly CompletedSkillGroupSettings3207? skillGroupSettings3207;
    private sealed record GroupField3207(string LeafDigest, bool Enabled, bool Inactive, string TraceDigest);
    private sealed record CompletedSkillGroupSettings3207(string ProfileId, string ProfileInputsDigest,
        string ProfileXmlDigest, string PolicyDigest, string PolicyContentDigest, string SkillsDigest,
        string? KarmaPolicyDigest, IReadOnlyDictionary<string, GroupField3207> Fields)
    {
        internal static CompletedSkillGroupSettings3207? FromCanonical(CharacterCreationEffectivePolicyContext policy,
            CharacterCreationMysticAdeptPowerPointPolicy source, Trace3207 trace,
            CharacterCreationAttributeSkillsPreview result)
        {
            if (trace is not { Completed: true, GroupPolicy: { } group, EnumeratedGroups3207: { } inventory }) return null;
            var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            if (profile.HasAttributes) return null;
            var fields = new Dictionary<string, GroupField3207>(StringComparer.Ordinal);
            string resultDigest = CharacterCreationFinalizationDigest.Compute(result);
            if (PlainGroupField3207(profile, "breakskillgroupsincreatemode", out var strictLeaf, out bool strict)
                && CharacterCreationSkillGroupPolicyAuthority.TryResolve(profile, out _, out bool resolved, out _)
                && strict == resolved && strict == group.StrictGroups)
                fields.Add("breakskillgroupsincreatemode", new(Xml971(strictLeaf!), strict,
                    !group.Checks.Any(check => check.GroupRating > 0),
                    CharacterCreationFinalizationDigest.Compute(new { Group = group, Inventory = inventory, Result = resultDigest })));
            string? karmaPolicy = null;
            if (trace.Regrouping3207 is { } regrouping
                && regrouping.ProfileId == policy.SettingsProfileId
                && regrouping.ProfileInputsDigest == policy.ProfileInputsDigest
                && regrouping.PurchasesDigest == trace.PurchasesDigest && regrouping.ResultDigest == resultDigest
                && PlainGroupField3207(profile, "allowskillregrouping", out var regroupLeaf, out bool allow)
                && allow == regrouping.Enabled)
            {
                karmaPolicy = regrouping.PolicyDigest;
                fields.Add("allowskillregrouping", new(Xml971(regroupLeaf!), allow, regrouping.Inventory.Count == 0,
                    CharacterCreationFinalizationDigest.Compute(regrouping)));
            }
            return fields.Count == 0 ? null : new(policy.SettingsProfileId, policy.ProfileInputsDigest,
                Xml971(profile), policy.PolicyDigest, CharacterCreationFinalizationDigest.Compute(policy), resultDigest,
                karmaPolicy, new ReadOnlyDictionary<string, GroupField3207>(fields));
        }
    }

    private static bool PlainGroupField3207(XElement profile, string field, out XElement? leaf, out bool value)
    {
        leaf = null; value = false;
        if (profile.Elements().Where(row => row.Name.LocalName == field).ToArray() is not [var exact]
            || exact.Name != field || exact.HasAttributes || exact.Nodes().ToArray() is not [XText text]
            || text.GetType() != typeof(XText) || exact.Value != exact.Value.Trim()
            || !bool.TryParse(exact.Value, out value)) return false;
        leaf = exact;
        return true;
    }

    private sealed record BoundSkillGroupSettings3207(CompletedSkillGroupSettings3207 Completed,
        string AuthorityDigest, string InputsDigest, string InvocationDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundSkillGroupSettings3207>
        groupSettingsInvocations3207 = new();

    internal void BindSkillGroupSettings3207(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationAttributeSkillsPreview skills)
    {
        if (skillGroupSettings3207 is not { } completed || !CanAttempt(request)
            || authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || request.PolicyRequirement != CharacterCreationPolicyRequirement.Complete
            || request.RuleSourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || authority.InputsSnapshotDigest != inputs.SnapshotDigest
            || !CharacterCreationProfileInputRead8576.Matches(inputs)
            || request.InputsSnapshotDigest != inputs.SnapshotDigest
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || invocation.RequestDigest != authority.RequestDigest || invocation.PolicyDigest != completed.PolicyDigest
            || authority.PolicyDigest != completed.PolicyDigest
            || CharacterCreationFinalizationDigest.Compute(inputs.EffectivePolicy) != completed.PolicyContentDigest
            || completed.KarmaPolicyDigest is not null && inputs.Skills.KarmaPurchasePolicy?.AuthorityDigest != completed.KarmaPolicyDigest
            || inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || invocation.WorkspaceId718 is null || invocation.WorkspaceId718 != inputs.OriginalCharacter?.WorkspaceId
            || invocation.CreationDraft718?.SettingsProfileId != completed.ProfileId
            || CharacterCreationFinalizationDigest.Compute(skills) != completed.SkillsDigest
            || CharacterCreationFinalizationDigest.Compute(invocation.Skills718) != completed.SkillsDigest
            || !invocation.TryGetCompletedConsumer("profile", out _)) return;
        groupSettingsInvocations3207.Add(invocation, new(completed, authorityDigest, inputs.SnapshotDigest,
            InvocationBinding971(invocation)));
    }

    internal static bool TryReadSkillGroupSetting3207(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string field, string leafXmlDigest,
        out bool enabled, out bool inactive, out string? executionDigest)
    {
        enabled = false; inactive = false; executionDigest = null;
        if (!groupSettingsInvocations3207.TryGetValue(invocation, out var bound)) return false;
        var completed = bound.Completed;
        if (profileId != completed.ProfileId || profileInputsDigest != completed.ProfileInputsDigest
            || profileXmlDigest != completed.ProfileXmlDigest || !completed.Fields.TryGetValue(field, out var proof)
            || leafXmlDigest != proof.LeafDigest || bound.InvocationDigest != InvocationBinding971(invocation)) return false;
        enabled = proof.Enabled; inactive = proof.Inactive;
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-skill-group-setting3207", bound.AuthorityDigest, bound.InputsDigest,
            bound.InvocationDigest, completed.ProfileId, completed.ProfileInputsDigest, completed.ProfileXmlDigest,
            completed.PolicyDigest, completed.PolicyContentDigest, completed.SkillsDigest, Field = field, Proof = proof
        });
        return true;
    }

    internal static bool MatchesSkillGroupAuthority3207(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority)
        => groupSettingsInvocations3207.TryGetValue(invocation, out var bound)
            && bound.AuthorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
            && bound.InputsDigest == authority.InputsSnapshotDigest
            && bound.InvocationDigest == InvocationBinding971(invocation);
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // The returned scalars are diagnostics, never an importable proof.
    public bool TryGetExecutedSkillGroupSetting3207(string profileId, string profileInputsDigest,
        string profileXmlDigest, string field, string leafXmlDigest,
        out bool enabled, out bool inactive, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadSkillGroupSetting3207(this,
            profileId, profileInputsDigest, profileXmlDigest, field, leafXmlDigest, out enabled, out inactive,
            out executionDigest);
}
