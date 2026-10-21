using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private readonly CompletedKnowledge971? knowledge971;

    // FromCanonical's full and selected callers already ran ExecuteSkills and
    // matched the entire result, including proposed attributes and all budgets.
    // The canonical authority/evaluator owns expression support and arithmetic.
    // This records that execution; it neither parses nor limits expressions.
    private sealed record CompletedKnowledge971(string ProfileId, string ProfileInputsDigest,
        string ProfileXmlDigest, string LeafXmlDigest, string PolicyDigest, string PolicyContentDigest,
        CharacterCreationCompletionSkillConsumerPolicy SkillsPolicy, string SkillsDigest, string AttributesDigest,
        CharacterCreationBudgetState Budget, int OverflowToActive)
    {
        internal static CompletedKnowledge971? FromCanonical(CharacterCreationEffectivePolicyContext policy,
            CharacterCreationMysticAdeptPowerPointPolicy source, CharacterCreationSelectedConsumerPolicy selected,
            CharacterCreationSkillsService.CompleteSkillTrace trace, CharacterCreationAttributeSkillsPreview result)
        {
            var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            var leaves = profile.Elements().Where(node => node.Name.LocalName == "knowledgepointsexpression").ToArray();
            if (!trace.Completed || result.Blockers.Count != 0 || !result.KnowledgeSkillPointBudget.IsExact
                || profile.HasAttributes || leaves is not [var leaf] || leaf.Name != "knowledgepointsexpression"
                || leaf.HasAttributes || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || leaf.Value != selected.Skills.KnowledgePointsExpression
                || selected.Skills.ProfileInputsDigest != policy.ProfileInputsDigest) return null;
            return new(policy.SettingsProfileId, policy.ProfileInputsDigest, Xml971(profile), Xml971(leaf),
                policy.PolicyDigest, CharacterCreationFinalizationDigest.Compute(policy), Detach971(selected.Skills),
                CharacterCreationFinalizationDigest.Compute(result), CharacterCreationFinalizationDigest.Compute(result.Attributes),
                Detach971(result.KnowledgeSkillPointBudget), result.KnowledgePointOverflowToActive);
        }
    }

    private sealed record BoundKnowledge971(CompletedKnowledge971 Completed, string AuthorityDigest,
        string InputsDigest, string InvocationDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundKnowledge971>
        knowledgeInvocations971 = new();

    internal void BindKnowledge971(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationAttributeSkillsPreview skills)
    {
        if (knowledge971 is not { } completed
            || authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || !CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || authority.InputsSnapshotDigest != inputs.SnapshotDigest
            || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
            || request.InputsSnapshotDigest != inputs.SnapshotDigest
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || invocation.RequestDigest != authority.RequestDigest
            || invocation.PolicyDigest != completed.PolicyDigest || authority.PolicyDigest != completed.PolicyDigest
            || CharacterCreationFinalizationDigest.Compute(inputs.EffectivePolicy) != completed.PolicyContentDigest
            || inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest
            || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || invocation.WorkspaceId718 is null || invocation.WorkspaceId718 != inputs.OriginalCharacter?.WorkspaceId
            || invocation.CreationDraft718?.SettingsProfileId != completed.ProfileId
            || inputs.Skills.Authority.AuthorityDigest != completed.SkillsPolicy.AuthorityDigest
            || inputs.Skills.Authority.KnowledgePointsExpression != completed.SkillsPolicy.KnowledgePointsExpression
            || CharacterCreationFinalizationDigest.Compute(skills) != completed.SkillsDigest
            || CharacterCreationFinalizationDigest.Compute(invocation.Skills718) != completed.SkillsDigest
            || CharacterCreationFinalizationDigest.Compute(invocation.Attributes718) != completed.AttributesDigest
            || !invocation.TryGetCompletedConsumer("profile", out _)) return;
        knowledgeInvocations971.Add(invocation, new(completed, authorityDigest, inputs.SnapshotDigest,
            InvocationBinding971(invocation)));
    }

    internal static bool TryReadKnowledge971(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string expression, string profileXmlDigest, string leafXmlDigest,
        out CharacterCreationBudgetState? budget, out int overflowToActive, out string? executionDigest)
    {
        budget = null; overflowToActive = 0; executionDigest = null;
        if (!knowledgeInvocations971.TryGetValue(invocation, out var bound)) return false;
        var completed = bound.Completed;
        if (profileId != completed.ProfileId || profileInputsDigest != completed.ProfileInputsDigest
            || expression != completed.SkillsPolicy.KnowledgePointsExpression
            || profileXmlDigest != completed.ProfileXmlDigest || leafXmlDigest != completed.LeafXmlDigest
            || bound.InvocationDigest != InvocationBinding971(invocation)) return false;
        budget = Detach971(completed.Budget);
        overflowToActive = completed.OverflowToActive;
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-knowledge-point-expression", bound.AuthorityDigest, bound.InputsDigest,
            bound.InvocationDigest, Completed = completed
        });
        return true;
    }

    private static string InvocationBinding971(CharacterCreationRuleSourceScopeInvocation invocation)
    {
        invocation.TryGetCompletedConsumer("profile", out var consumer);
        return CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            Consumer = consumer, invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718,
            invocation.Attributes718, invocation.AttributePolicy718, invocation.Skills718,
            invocation.ResourcesExecution718, invocation.LifestyleAuthority718, invocation.Lifestyles718,
            invocation.StartingLifestyleId718, invocation.SourceExecution832, invocation.CompiledMovement832,
            invocation.PhysicalAuthority886, invocation.PhysicalInitiative886
        });
    }

    private static string Xml971(XElement node)
        => CharacterCreationFinalizationDigest.Compute(node.ToString(SaveOptions.DisableFormatting));
    private static T Detach971<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Detached diagnostics require this exact privately registered invocation.
    // Copying/deserializing a result or invocation cannot register execution.
    public bool TryGetExecutedKnowledgePoints971(string profileId, string profileInputsDigest, string expression,
        string profileXmlDigest, string leafXmlDigest, out CharacterCreationBudgetState? budget,
        out int overflowToActive, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadKnowledge971(this, profileId, profileInputsDigest,
            expression, profileXmlDigest, leafXmlDigest, out budget, out overflowToActive, out executionDigest);
}
