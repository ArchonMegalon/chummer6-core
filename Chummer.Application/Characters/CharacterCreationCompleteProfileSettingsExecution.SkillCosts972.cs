using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Trace972 = Chummer.Application.Characters.CharacterCreationSkillsService.CompleteSkillTrace;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private readonly CompletedSkillCosts972? skillCosts972;
    private sealed record SkillCostLeaf972(int Value, string XmlDigest);
    private sealed record CompletedSkillCosts972(string ProfileId, string ProfileInputsDigest,
        string ProfileXmlDigest, string PolicyDigest, string PolicyContentDigest,
        Trace972.SkillCostExecution972 Execution, IReadOnlyDictionary<string, SkillCostLeaf972> Leaves)
    {
        internal static CompletedSkillCosts972? FromCanonical(CharacterCreationEffectivePolicyContext policy,
            CharacterCreationMysticAdeptPowerPointPolicy source, Trace972 trace, CharacterCreationAttributeSkillsPreview result)
        {
            if (trace is not { Completed: true, SkillCosts972: { } costs }
                || trace.PurchasesDigest != costs.PurchasesDigest
                || policy.SettingsProfileId != costs.ProfileId || policy.ProfileInputsDigest != costs.ProfileInputsDigest
                || costs.ResultDigest != CharacterCreationFinalizationDigest.Compute(result)) return null;
            var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            if (profile.Elements("karmacost").ToArray() is not [var container] || container.HasAttributes) return null;
            var leaves = new Dictionary<string, SkillCostLeaf972>(StringComparer.Ordinal);
            foreach (var pair in costs.Leaves)
            {
                if (container.Elements().Where(node => node.Name.LocalName == pair.Key).ToArray() is not [var leaf]
                    || leaf.Name != pair.Key || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)) continue;
                // The source resolver reuses TryReadKarmaCost before accounting this leaf.
                leaves.Add(pair.Key, new(pair.Value, CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting))));
            }
            if (leaves.Count == 0) return null;
            return new(policy.SettingsProfileId, policy.ProfileInputsDigest,
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                policy.PolicyDigest, CharacterCreationFinalizationDigest.Compute(policy), costs, leaves);
        }
    }

    private sealed record BoundSkillCosts972(CompletedSkillCosts972 Completed, string AuthorityDigest,
        string InputsDigest, string InvocationDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundSkillCosts972>
        skillCostInvocations972 = new();

    internal void BindSkillCosts972(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationAttributeSkillsPreview skills)
    {
        if (skillCosts972 is not { } completed || authority.Phase != CharacterCreationCompletePhase.Authority
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || authority.InputsSnapshotDigest != inputs.SnapshotDigest || request.InputsSnapshotDigest != inputs.SnapshotDigest
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || invocation.RequestDigest != authority.RequestDigest || invocation.PolicyDigest != completed.PolicyDigest
            || authority.PolicyDigest != completed.PolicyDigest || inputs.EffectivePolicy is not { } policy
            || CharacterCreationFinalizationDigest.Compute(policy) != completed.PolicyContentDigest
            || inputs.Skills.KarmaPurchasePolicy?.AuthorityDigest != completed.Execution.PolicyDigest
            || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || invocation.CreationDraft718?.SettingsProfileId != completed.ProfileId
            || CharacterCreationFinalizationDigest.Compute(skills) != completed.Execution.ResultDigest
            || CharacterCreationFinalizationDigest.Compute(invocation.Skills718) != completed.Execution.ResultDigest
            || !invocation.TryGetCompletedConsumer("profile", out _)) return;
        skillCostInvocations972.Add(invocation, new(completed, authorityDigest, inputs.SnapshotDigest,
            SkillCostInvocationBinding972(invocation)));
    }

    private static string SkillCostInvocationBinding972(CharacterCreationRuleSourceScopeInvocation invocation)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            Core = InvocationBinding940(invocation), invocation.ResourcesExecution718,
            invocation.LifestyleAuthority718, invocation.Lifestyles718, invocation.StartingLifestyleId718
        });

    internal static bool TryReadSkillCost972(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string field, string leafXmlDigest,
        out int value, out string? executionDigest)
    {
        value = 0; executionDigest = null;
        if (!skillCostInvocations972.TryGetValue(invocation, out var bound)) return false;
        var completed = bound.Completed;
        if (profileId != completed.ProfileId || profileInputsDigest != completed.ProfileInputsDigest
            || profileXmlDigest != completed.ProfileXmlDigest || !completed.Leaves.TryGetValue(field, out var leaf)
            || leaf.XmlDigest != leafXmlDigest || bound.InvocationDigest != SkillCostInvocationBinding972(invocation)) return false;
        value = leaf.Value;
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-skill-Karma-price", bound.AuthorityDigest, bound.InputsDigest,
            bound.InvocationDigest, completed.ProfileId, completed.ProfileInputsDigest, completed.ProfileXmlDigest,
            completed.PolicyDigest, completed.Execution, Field = field, Leaf = leaf
        });
        return true;
    }
}
