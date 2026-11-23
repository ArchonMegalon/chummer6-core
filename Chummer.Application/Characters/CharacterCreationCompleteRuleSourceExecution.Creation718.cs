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
        CharacterCreationCompleteContactsExecution? contacts, CharacterCreationSelectedEffectsConsumer effects)
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
        var physicalLoadout3511 = Inputs.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            && Inputs.PhysicalInitiative886 is not null && Inputs.Loadout is { } loadout
            && request.Selections.Loadout is { } selections
            ? CharacterCreationLoadoutCompletionService.Preview(loadout, selections).Value : null;
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
                ? CharacterCreationPhysicalInitiative886.Evaluate(graph, initiative,
                    physicalLoadout3511,
                    CharacterCreationSelectedEffectsConsumer.CompilationSeed(authority),
                    request.LifecycleResultSchema is null ? null : CharacterCreationInitiativeNonPhysical1249.Execute(graph, Inputs.Skills.PrerequisiteDraft,
                        Inputs.Magic.SelectedTalent.GrantedQualitySources ?? [],
                        CharacterCreationSelectedEffectsConsumer.CompilationSeed(authority)),
                    effects.OriginalLoadout3511(authority, graph, physicalLoadout3511)) : null,
            NativeMagic1430 = effects.NativeMagic1430(authority),
            AstralInitiative1249 = effects.AstralInitiative1249(authority),
            MatrixInitiative = effects.MatrixInitiative(authority),
            NativeSettings1420 = effects.NativeSettings1420(authority),
            NativeContactSpecialty = effects.NativeContactSpecialty(authority),
            NativeLoadout1432 = effects.NativeLoadout1432(authority),
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
        invocation.BindNativeMagic1430(effects, authority);
        invocation.BindAstralInitiative1249(effects, authority);
        invocation.BindMatrixInitiative(effects, authority);
        invocation.BindNativeSettings1420(effects, authority);
        invocation.BindNativeContactSpecialty(effects, authority);
        invocation.BindNativeLoadout1432(effects, authority);
        if (sourcebooks is not null) BindProfileSourcebooks935(invocation, sourcebooks);
        profileSettings?.BindBrokenGroup940(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindSkillGroupSettings3207(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindKnowledge971(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindSkillCosts972(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindCompensation1015(invocation, authority, Inputs, request, actualSkills);
        profileSettings?.BindAttributeHouseRules993(invocation, authority, Inputs, request, actualSkills);
        qualityLimit947?.Bind947(invocation, authority, Inputs, request, qualities);
        contacts?.BindSourceInvocation962(invocation, authority, Inputs, request);
        funding?.BindSourceInvocation994(invocation, authority, Inputs, request);
        BindProfileCarryover995(invocation, authority, actualSkills, funding, contacts);
        BindEssenceScope1222(invocation);
        BindProfileMetadata1209(invocation, authority, graph);
        BindProfileLoadout1215(invocation, authority, graph, funding);
        return invocation;
    }
}
