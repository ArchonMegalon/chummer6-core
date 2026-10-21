using Q947 = Chummer.Application.Characters.CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private CharacterCreationRuleSourceScopeInvocation CreationInvocation718(
        CharacterCreationCompleteAssessment authority, XElement graph,
        Dictionary<string, string> consumers, IReadOnlyList<CharacterCreationCompiledSourceEffect> compiled,
        CharacterCreationAttributeSkillsPreview actualSkills,
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles,
        CharacterCreationResourcesService.CompleteFundingExecution? funding, CompletedSourcebooks800? sourcebooks,
        CharacterCreationCompleteProfileSettingsExecution? profileSettings,
        Q947? qualityLimit947, CharacterCreationQualitiesCompletionEvaluation? qualities,
        CharacterCreationCompleteContactsExecution? contacts)
    {
        // Complete already requires the private completed skill trace and
        // successful original-aware compilation, bound to this exact request.
        string creation = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-creation-selection-and-attribute-allocation",
            Inputs.Skills.PrerequisiteDraft, Result = actualSkills.Attributes,
            authority.PolicyDigest, authority.InputsSnapshotDigest, authority.RequestDigest
        });
        foreach (string scope in new[] { "profile", "metatypes", "priorities" })
            consumers.Add(scope, creation);
        if (lifestyles is not null) consumers.Add("lifestyles", lifestyles.EvaluationDigest);
        var invocation = new CharacterCreationRuleSourceScopeInvocation(
            authority.PolicyDigest, Inputs.RuleSourceCaptureDigest!, requestDigest,
            CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting)),
            consumers, compiled)
        {
            SourceExecution832 = Inputs.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4),
            CompiledMovement832 = Inputs.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                ? CharacterCreationHeritageGraph832.MatchingMovement(graph, Inputs.Skills.PrerequisiteDraft.HeritageSelection!) : null,
            PhysicalAuthority886 = Inputs.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                ? Inputs.PhysicalInitiative886 : null,
            PhysicalInitiative886 = Inputs.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                && Inputs.PhysicalInitiative886 is { } initiative
                ? CharacterCreationPhysicalInitiative886.Evaluate(graph, initiative) : null,
            CreationDraft718 = Inputs.Skills.PrerequisiteDraft,
            Attributes718 = actualSkills.Attributes,
            AttributePolicy718 = Inputs.AttributePolicy,
            Skills718 = actualSkills,
            ResourcesExecution718 = funding?.CopySourceResult718(),
            LifestyleAuthority718 = lifestyles?.CopySourceAuthority718(),
            Lifestyles718 = lifestyles?.CopySourceProjections718(),
            StartingLifestyleId718 = lifestyles?.SourceStartingLifestyleId718,
            WorkspaceId718 = Inputs.OriginalCharacter?.WorkspaceId
        };
        if (sourcebooks is not null) BindProfileSourcebooks935(invocation, sourcebooks);
        profileSettings?.BindBrokenGroup940(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindKnowledge971(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindSkillCosts972(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindCompensation1015(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindAttributeHouseRules993(invocation, authority, Inputs, request, actualSkills);
        qualityLimit947?.Bind947(invocation, authority, Inputs, request, qualities);
        contacts?.BindSourceInvocation962(invocation, authority, Inputs, request);
        funding?.BindSourceInvocation994(invocation, authority, Inputs, request);
        BindProfileCarryover995(invocation, authority, actualSkills, funding, contacts);
        return invocation;
    }
}
