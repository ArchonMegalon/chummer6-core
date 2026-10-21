using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private readonly CompletedAttributeHouseRules993? attributeHouseRules993;

    // This is retained only after ExecuteSkills has reproduced the whole canonical
    // preview. Zero Karma levels still carry that evaluated attribute result.
    private sealed record CompletedAttributeHouseRules993(string ProfileXmlDigest,
        string PolicyContentDigest, string StateDigest, string SkillsDigest,
        CharacterCreationAttributePolicy Policy, CharacterCreationAttributesPreview Result,
        IReadOnlyDictionary<string, string> Leaves)
    {
        internal static CompletedAttributeHouseRules993? FromCanonical(CharacterCreationEffectivePolicyContext policy,
            CharacterCreationMysticAdeptPowerPointPolicy source, CharacterCreationSelectedConsumerPolicy selected,
            CharacterCreationAttributesState state, CharacterCreationAttributeSkillsPreview skills)
        {
            if (!SupportsHouseRules(policy, source, selected) || selected.Attributes.Spending is not { } spending
                || spending.SettingsProfileId != policy.SettingsProfileId
                || spending.RawProfileInputsDigest != policy.ProfileInputsDigest
                // The completion evaluator accepts an unchanged saved allocation;
                // retain its actual DraftDuplicate result, without inventing a new preview.
                || skills.Attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate)) return null;
            var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            var leaves = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string field in new[] { "alternatemetatypeattributekarma", "reverseattributepriorityorder" })
            {
                if (profile.Elements().Where(node => node.Name.LocalName == field).ToArray() is not [var leaf]
                    || leaf.Name != field || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)) return null;
                leaves.Add(field, CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)));
            }
            var result = skills.Attributes with
            {
                Attributes = Array.AsReadOnly(skills.Attributes.Attributes.Select(row => row with
                {
                    DisableReasons = Array.AsReadOnly(row.DisableReasons.ToArray()),
                    SourceAnchorIds = Array.AsReadOnly(row.SourceAnchorIds.ToArray())
                }).ToArray()),
                NormalPointBudget = skills.Attributes.NormalPointBudget with
                    { Blockers = Array.AsReadOnly(skills.Attributes.NormalPointBudget.Blockers.ToArray()) },
                SpecialPointBudget = skills.Attributes.SpecialPointBudget with
                    { Blockers = Array.AsReadOnly(skills.Attributes.SpecialPointBudget.Blockers.ToArray()) },
                CreationKarmaBudget = skills.Attributes.CreationKarmaBudget with
                    { Blockers = Array.AsReadOnly(skills.Attributes.CreationKarmaBudget.Blockers.ToArray()) },
                Blockers = Array.AsReadOnly(skills.Attributes.Blockers.ToArray()),
                PlayerChosenAttributeIds = skills.Attributes.PlayerChosenAttributeIds is { } choices
                    ? Array.AsReadOnly(choices.ToArray()) : null
            };
            return new(CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                CharacterCreationFinalizationDigest.Compute(policy), CharacterCreationFinalizationDigest.Compute(state),
                CharacterCreationFinalizationDigest.Compute(skills),
                spending with { SourceAnchorIds = Array.AsReadOnly(spending.SourceAnchorIds.ToArray()) }, result,
                new ReadOnlyDictionary<string, string>(leaves));
        }
    }

    private sealed record BoundAttributeHouseRules993(CompletedAttributeHouseRules993 Completed,
        string AuthorityDigest, string InputsDigest, string InvocationDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundAttributeHouseRules993>
        attributeHouseRuleInvocations993 = new();

    internal void BindAttributeHouseRules993(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationAttributeSkillsPreview skills)
    {
        if (attributeHouseRules993 is not { } completed || authority.Phase != CharacterCreationCompletePhase.Authority
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || authority.InputsSnapshotDigest != inputs.SnapshotDigest || request.InputsSnapshotDigest != inputs.SnapshotDigest
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || invocation.RequestDigest != authority.RequestDigest || invocation.PolicyDigest != authority.PolicyDigest
            || inputs.EffectivePolicy is not { } policy || authority.PolicyDigest != policy.PolicyDigest
            || CharacterCreationFinalizationDigest.Compute(policy) != completed.PolicyContentDigest
            || CharacterCreationFinalizationDigest.Compute(inputs.Skills.Attributes) != completed.StateDigest
            || CharacterCreationFinalizationDigest.Compute(inputs.AttributePolicy) != CharacterCreationFinalizationDigest.Compute(completed.Policy)
            || CharacterCreationFinalizationDigest.Compute(invocation.AttributePolicy718) != CharacterCreationFinalizationDigest.Compute(completed.Policy)
            || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || invocation.CreationDraft718?.SettingsProfileId != completed.Policy.SettingsProfileId
            || CharacterCreationFinalizationDigest.Compute(skills) != completed.SkillsDigest
            || CharacterCreationFinalizationDigest.Compute(invocation.Skills718) != completed.SkillsDigest
            || CharacterCreationFinalizationDigest.Compute(invocation.Attributes718)
                != CharacterCreationFinalizationDigest.Compute(completed.Result)
            || !invocation.TryGetCompletedConsumer("profile", out _)) return;
        // Reuse the existing full invocation binding, including all candidate projections.
        attributeHouseRuleInvocations993.Add(invocation, new(completed, authorityDigest, inputs.SnapshotDigest,
            SkillCostInvocationBinding972(invocation)));
    }

    internal static bool TryReadAttributeHouseRule993(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string field, string leafXmlDigest,
        out bool value, out bool noPaidTargets, out CharacterCreationAttributesPreview? result, out string? executionDigest)
    {
        value = false; noPaidTargets = false; result = null; executionDigest = null;
        if (!attributeHouseRuleInvocations993.TryGetValue(invocation, out var bound)) return false;
        var completed = bound.Completed;
        if (profileId != completed.Policy.SettingsProfileId || profileInputsDigest != completed.Policy.RawProfileInputsDigest
            || profileXmlDigest != completed.ProfileXmlDigest || !completed.Leaves.TryGetValue(field, out var leaf)
            || leaf != leafXmlDigest || bound.InvocationDigest != SkillCostInvocationBinding972(invocation)) return false;
        value = field == "alternatemetatypeattributekarma"
            ? completed.Policy.AlternateMetatypeAttributeKarma : completed.Policy.ReverseAttributePriorityOrder;
        result = completed.Result;
        noPaidTargets = !result.Attributes.Any(attribute => attribute.KarmaLevels > 0);
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-attribute-house-rule", bound.AuthorityDigest, bound.InputsDigest,
            bound.InvocationDigest, completed.Policy, completed.ProfileXmlDigest, Field = field, Leaf = leaf,
            Result = result, NoPaidTargets = noPaidTargets
        });
        return true;
    }
}
