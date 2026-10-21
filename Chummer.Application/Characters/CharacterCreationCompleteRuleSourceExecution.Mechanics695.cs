using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Contracts.Characters.CharacterCreationActionPoolRules695;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // No resolver receipt or public DTO can invoke this through the review API.
    // Both full and selected replay reach this instance only after reconstructing
    // the exact original character and full inputs from the retained source capture.
    internal Mechanics695Execution? ExecuteMechanics695(
        CharacterCreationCompleteAssessment authority,
        CharacterCreationSelectedEffectsConsumer? effects,
        CharacterCreationAttributeSkillsPreview actualSkills,
        CharacterCreationQualitiesCompletionEvaluation actualQualities,
        CharacterCreationSkillsService.CompleteSkillTrace? skills,
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles,
        CharacterCreationResourcesService.CompleteFundingExecution? funding,
        CharacterCreationGearService.CompleteCostExecution? gear,
        CharacterCreationCompleteMagicExecution? magic,
        CharacterCreationCompleteContactsExecution? contacts)
    {
        if (!Mechanics695Execution.CanAttempt(request)
            || Inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || Inputs.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest
            || authority.InputsSnapshotDigest != Inputs.SnapshotDigest
            || authority.RequestDigest != requestDigest
            || requestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || Inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(Inputs)
            || authority.PolicyDigest != Inputs.EffectivePolicy?.PolicyDigest
            || !original.MatchesInputs(Inputs.SnapshotDigest)
            || original.CreationLifecycleDigestFor(authority) is not { } lifecycle
            || effects?.DigestFor(authority) is not { } effectsDigest
            || skills is not { Completed: true }
            || lifestyles is null || funding is null || gear is null
            || magic?.DigestFor(authority) is not { } magicDigest
            || contacts?.DigestFor(authority) is not { } contactsDigest) return null;
        try
        {
            // Do not substitute a selected projection for retained source authority.
            // The canonical point/purchase evaluator executes the full candidate
            // again, including exact grant and quality sources and category prices.
            var replay = CharacterCreationCompleteConsumerExecution.ExecuteSkills(
                Inputs.Skills, request.Selections, Inputs.EffectivePolicy!, actualSkills, actualQualities,
                (Inputs.Magic.SelectedTalent.GrantedQualitySources ?? []).Select(row => row.CanonicalSourceXml),
                default, allowDiceOnlyGrants: true);
            if (replay is not { Completed: true }
                || request.Selections.SkillKarmaPurchases is not null && replay.PurchasesDigest is null)
                return null;
            var quality = CharacterCreationQualitiesService.PreviewWithProposedAttributes(
                Inputs.Lifestyles.Qualities, request.Selections.AttributeAllocations, request.Selections.QualityOptionIds);
            if (quality.Outcome != CharacterCreationFoundationOutcomes.Success || quality.Blockers.Count != 0
                || quality.Value?.Qualities is not { Blockers.Count: 0 } evaluatedQualities
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    PurchaseQualityResult(evaluatedQualities), PurchaseQualityResult(actualQualities))) return null;

            var graph = effects.CopyCompiledGraph();
            var deltas = effects.CopyDeltas();
            // CopyDeltas has nested lists. Recompute the compiler binding before
            // using either payload, as the existing reputation consumer does.
            if (effectsDigest != CharacterCreationFinalizationDigest.Compute(new
                {
                    Authority = CharacterCreationFinalizationDigest.Compute(authority),
                    Operation = CharacterCreationConsumerPolicyOperation.SelectedEffects,
                    Graph = graph.ToString(SaveOptions.DisableFormatting), Deltas = deltas
                })) return null;
            var magicReplay = CharacterCreationMagicResonanceService.PreviewCompletion(Inputs.Magic,
                request.Selections.AttributeAllocations, request.Selections.MagicSelections);
            if (magicReplay.Outcome != CharacterCreationFoundationOutcomes.Success || magicReplay.Blockers.Count != 0
                || magicReplay.Value is not { Blockers.Count: 0 } magicResult
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    magicResult.Attributes.Attributes, actualSkills.Attributes.Attributes)
                || !CharacterCreationSelectedPrerequisiteConsumer.TryCheckFinalGraph(graph,
                    Inputs.Skills.PrerequisiteDraft, Inputs.Magic.SelectedTalent.GrantedQualitySources ?? [],
                    actualSkills.Attributes.Attributes, out var prerequisiteChecks)) return null;
            if (!CharacterCreationLegacySourceProjector.TryProjectCompletionQualityEffects(
                    evaluatedQualities.Selections, out var pricedSources)) return null;
            CharacterCreationLifestylesCompletionProjection? lifestyleResult = null;
            if (pricedSources.Any(row => row.ImprovementType is "TrustFund" or "LifestyleCost" or "BasicLifestyleCost"))
            {
                CharacterCreationLoadoutCompletionProjection? loadout = null;
                if (request.Selections.Loadout is not null)
                {
                    if (Inputs.Loadout is null) return null;
                    var projected = CharacterCreationLoadoutCompletionService.Preview(Inputs.Loadout, request.Selections.Loadout);
                    if (projected.Blockers.Count != 0 || projected.Value is null) return null;
                    loadout = projected.Value;
                }
                CharacterCreationLifestylesService.CompleteGraphExecution? replayedLifestyles = null;
                var priced = CharacterCreationLifestylesService.PreviewCompletion(Inputs.Lifestyles, Inputs.Magic,
                    request.Selections.AttributeAllocations, request.Selections.MagicSelections,
                    request.Selections.QualityOptionIds, request.Selections.TotalKarmaInvestment,
                    request.Selections.Gear, request.Selections.Lifestyles, request.Selections.StartingLifestyleId,
                    loadout, observeCompleteGraph: observed => replayedLifestyles = observed, completeAuthority: authority);
                if (priced.Blockers.Count != 0 || priced.Value is not { Blockers.Count: 0 } pricedLifestyles
                    || replayedLifestyles?.EvaluationDigest != lifestyles.EvaluationDigest) return null;
                lifestyleResult = pricedLifestyles;
            }
            CharacterCreationContactsCompletionProjection? contactResult = null;
            if (pricedSources.Any(row => row.ImprovementType == "FriendsInHighPlaces"))
            {
                var priced = CharacterCreationContactsService.PreviewCompletion(Inputs.Contacts,
                    request.Selections.AttributeAllocations, request.Selections.QualityOptionIds, request.Selections.Contacts);
                if (priced.Blockers.Count != 0 || priced.Value is not { Blockers.Count: 0 } pricedContacts) return null;
                contactResult = pricedContacts;
            }
            using var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var context = (scope ?? resolution.Resolver).TryCreateContext(Inputs.OriginalCharacter!.RawCharacterXml);
            if (context is null || !context.TryResolveCareerReputationSettings(out var reputationSettings, out var ruleState)
                || reputationSettings is null || string.IsNullOrEmpty(ruleState)
                || !CharacterCareerReputationProjector.TryEvaluateGraph(graph, reputationSettings,
                    CharacterCareerReputationProjector.CharacterReputationPhase.Creation,
                    out var reputation, out _)) return null;

            string compilationSeed = CharacterCreationFinalizationDigest.Compute(new
                { authority.Schema, authority.InputsSnapshotDigest, authority.SelectionsDigest });
            if (!Mechanics695Execution.TryExecute711(graph, Inputs, actualSkills, magicResult,
                    evaluatedQualities, compilationSeed, context, out var extension711)) return null;
            var consumers = new Mechanics695Execution.DomainExecutions(
                replay.PurchasesDigest, lifestyles.EvaluationDigest, funding.EvaluationDigest,
                gear.EvaluationDigest, magicDigest, contactsDigest, reputation!, magicResult, prerequisiteChecks,
                Inputs.Skills.MovementCapability, lifestyleResult, contactResult, extension711);
            if (!Mechanics695Execution.TryEvaluateGraph(graph, actualSkills, Inputs.Skills.Authority,
                    evaluatedQualities, consumers, compilationSeed, out var calculation)) return null;
            // The private source scope must still resolve the same rule state.
            if (!context.TryResolveCareerReputationSettings(out var finalSettings, out var finalRuleState)
                || reputationSettings != finalSettings || ruleState != finalRuleState
                || !Mechanics695Execution.TryExecute711(graph, Inputs, actualSkills, magicResult,
                    evaluatedQualities, compilationSeed, context, out var final711)
                || final711!.SourceState != extension711!.SourceState
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(final711.Results, extension711.Results)) return null;
            return Mechanics695Execution.Completed(authority, calculation!, new
            {
                Inputs.RuleSourceCaptureDigest, Inputs.RuleSourceExecutionSchema,
                SourceAuthority = Inputs.Skills.Authority.AuthorityDigest,
                Original = lifecycle, Compiled = effectsDigest,
                Graph = graph.ToString(SaveOptions.DisableFormatting), Deltas = deltas,
                Skills = replay.Scopes, Purchases = replay.PurchasesDigest,
                Qualities = PurchaseQualityResult(evaluatedQualities),
                ReputationSettings = reputationSettings, ReputationRuleState = ruleState,
                Mechanics711SourceState = extension711.SourceState,
                Consumers = consumers
            });
        }
        catch (Exception error) when (error is IOException or XmlException or InvalidOperationException
            or ArgumentException or OverflowException or FormatException or NullReferenceException)
        { return null; }
    }
}

/// <summary>
/// Actual action/rating execution over the compiled original-character graph.
/// Pricing, magic, contacts and reputation use their existing canonical consumers.
/// Unknown or unexecuted active constructs never earn CharacterMechanics coverage.
/// </summary>
internal sealed partial class Mechanics695Execution
{
    internal sealed record DomainExecutions(string? Purchases, string Lifestyles, string Funding,
        string Gear, string Magic, string Contacts, CharacterCareerReputationProjector.GraphEvaluation Reputation,
        CharacterCreationMagicResonanceCompletionProjection MagicResult,
        IReadOnlyList<CharacterCreationSelectedPrerequisiteCheck> Prerequisites,
        CharacterCreationMovementCapability Movement,
        CharacterCreationLifestylesCompletionProjection? LifestyleResult,
        CharacterCreationContactsCompletionProjection? ContactResult, Extension711? Extension = null);
    internal sealed record SkillResult(string SourceId, string Kind, string Name,
        int LearnedRating, string Attribute, CharacterCreationActionPoolRules695.Result Action);
    internal sealed record EffectResult(int Index, string Type, string NodeDigest, string Consumer, object Result);
    internal sealed record Calculation(Handedness Hands, IReadOnlyList<SkillResult> Skills,
        IReadOnlyList<EffectResult> Effects);
    private readonly string authorityDigest;
    private readonly string evaluationDigest;
    internal Calculation Result { get; }
    private Mechanics695Execution(CharacterCreationCompleteAssessment authority, Calculation result, object execution)
    {
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        Result = result;
        evaluationDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.creation-character-mechanics-execution.695.v1",
            Authority = authorityDigest, Execution = execution, Result = result
        });
    }
    internal static Mechanics695Execution Completed(CharacterCreationCompleteAssessment authority,
        Calculation result, object execution) => new(authority, result, execution);
    internal string? DigestFor(CharacterCreationCompleteAssessment authority)
        => authorityDigest == CharacterCreationFinalizationDigest.Compute(authority) ? evaluationDigest : null;

    internal static bool CanAttempt(CharacterCreationCompletionReviewRequest request)
        => request.PolicyRequirement == CharacterCreationPolicyRequirement.Complete
            && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && request.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            && request.OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
            && request.AdditionalConsumerExecutionSchema == CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
            && CharacterCreationFinalizationDigest.IsCanonical(request.RuleSourceCaptureDigest);

    internal static bool TryEvaluateGraph(XElement graph, CharacterCreationAttributeSkillsPreview actual,
        CharacterCreationSkillsAuthority authority, CharacterCreationQualitiesCompletionEvaluation qualities,
        DomainExecutions consumers, string compilationSeed, out Calculation? result)
    {
        result = null;
        try
        {
            if (graph.Name != "character" || graph.HasAttributes
                || graph.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
                || Scalar(graph, "created") is not ("False" or "false")
                || !authority.IsAuthoritative || authority.Blockers.Count != 0) return false;
            // These affect action pools but have no creation consumer here. Never
            // silently price an augmented/wounded/sustaining character as ordinary.
            foreach (string container in new[] { "cyberwares", "vehicles", "martialarts", "sustainedobjects",
                "drugcomponents", "drugs", "improvementgroups" })
                if (graph.Element(container) is { } node && (node.HasAttributes || node.HasElements
                    || !string.IsNullOrWhiteSpace(node.Value))) return false;
            foreach (string scalar in new[] { "physicalcmfilled", "stuncmfilled" })
                if (graph.Element(scalar) is not null && Integer(graph, scalar) != 0) return false;
            int order = 0;
            var ignoredDeltas = new List<CharacterCreationFinalizationDelta>();
            if (!XNode.DeepEquals(graph.Element("attributes"),
                CharacterCreationFinalizationProjector.BuildAttributes(actual.Attributes.Attributes, ignoredDeltas, ref order)))
                return false;
            var attributes = actual.Attributes.Attributes.ToDictionary(row => row.AttributeId, row => row.Current, StringComparer.Ordinal);
            XElement[] improvements = Rows(graph, "improvements", "improvement");
            if (improvements.Length > 4096 || !CharacterCreationLegacySourceProjector.TryProjectCompletionQualityEffects(
                qualities.Selections, out var qualityEffects)) return false;
            var purchasedImprovements = new List<XElement>();
            var purchasedQualityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quality in qualities.Selections.OrderBy(row => row.OptionId, StringComparer.Ordinal))
            {
                if (!CharacterCreationLegacySourceProjector.TryBuildQualityGraph(quality, compilationSeed,
                        out var nodes, out var compiled)) return false;
                foreach (var node in nodes)
                {
                    purchasedQualityIds.Add(Scalar(node, "guid"));
                    if (graph.Element("qualities")!.Elements("quality").Count(saved => XNode.DeepEquals(node, saved)) != 1)
                        return false;
                }
                purchasedImprovements.AddRange(compiled);
            }
            // Exact occurrence reconciliation prevents a preserved original or a
            // duplicate paid effect from borrowing another source's execution.
            var actualPurchased = improvements.Where(node => purchasedQualityIds.Contains(Scalar(node, "sourcename"))).ToArray();
            if (actualPurchased.Length != purchasedImprovements.Count
                || !actualPurchased.Select((node, index) => XNode.DeepEquals(node, purchasedImprovements[index])).All(same => same))
                return false;
            var effectResults = new List<EffectResult>();
            var ambidextrous = new List<bool>();
            var dice = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < improvements.Length; index++)
            {
                XElement effect = improvements[index];
                ValidateImprovement(effect);
                string type = Scalar(effect, "improvementttype");
                string name = Scalar(effect, "improvedname");
                bool enabled = Integer(effect, "enabled") == 1;
                // Recognition alone never consumes a row. Each branch below
                // invokes a rule or requires an already completed exact consumer.
                switch (type)
                {
                    case "Ambidextrous":
                        Marker(effect);
                        if (!purchasedQualityIds.Contains(Scalar(effect, "sourcename"))) return false;
                        ambidextrous.Add(enabled);
                        break; // Handedness is executed after all occurrences.
                    case "Skill":
                        if (!enabled || Scalar(effect, "condition") != "" || Integer(effect, "addtorating") != 0
                            || !actual.Skills.Any(row => row.Name == name && row.IsEnabled && !row.IsNativeLanguage)
                                && !authority.ActiveSkills.Any(row => row.Name == name
                                    && CharacterCreationSkillsService.IsActionSkillAvailable695(authority, row, consumers.Movement)))
                            return false;
                        dice[name] = checked(dice.GetValueOrDefault(name) + Integer(effect, "val"));
                        break; // Each row is bound to its resulting action below.
                    case "SkillBase":
                    case "SkillGroupBase":
                        if (!enabled || Scalar(effect, "condition") != "" || Integer(effect, "addtorating") != 0)
                            return false;
                        break; // Canonical learned-rating consumer below.
                    case "Attribute":
                        if (!enabled || Integer(effect, "val") != 0 || Integer(effect, "rating") != 0
                            || effect.Element("unique")?.Value != "enableattribute") return false;
                        var enabledAttribute = actual.Attributes.Attributes.Single(row => row.AttributeId == name);
                        int effective = name switch
                        {
                            "MAG" => consumers.MagicResult.EffectiveAttributes.Magic,
                            "RES" => consumers.MagicResult.EffectiveAttributes.Resonance,
                            "DEP" => consumers.MagicResult.EffectiveAttributes.Depth,
                            _ => -1
                        };
                        string flag = name switch { "MAG" => "magenabled", "RES" => "resenabled", "DEP" => "depenabled", _ => "" };
                        if (flag.Length == 0 || !enabledAttribute.IsEnabled || !bool.Parse(Scalar(graph, flag))
                            || effective != enabledAttribute.Current) return false;
                        Add(index, effect, "magic-attribute", new { enabledAttribute, Effective = effective, consumers.Magic });
                        break;
                    case "SpecialTab":
                        if (!enabled || Integer(effect, "rating") != 0 || Integer(effect, "val") != 0
                            || effect.Element("unique")?.Value != "enabletab"
                            || name is not ("Magician" or "Adept" or "Technomancer")
                            || !bool.Parse(Scalar(graph, name.ToLowerInvariant()))) return false;
                        Add(index, effect, "magic-access", new { Tab = name, consumers.MagicResult.Talent,
                            consumers.MagicResult.EffectiveAttributes, consumers.Prerequisites });
                        break;
                    case "SpecialSkills":
                        MarkerTarget(effect);
                        var unlocked = authority.ActiveSkills.Where(row => CharacterCreationSkillsAccessRules.Matches(row, name))
                            .Select(row => new { row.SourceSkillId,
                                Available = CharacterCreationSkillsAccessRules.IsSkillAvailable(authority, row.SourceSkillId) }).ToArray();
                        if (!enabled || unlocked.Length == 0 || unlocked.Any(row => !row.Available)) return false;
                        Add(index, effect, "skill-access", unlocked);
                        break;
                    case "Gear":
                        MarkerTarget(effect);
                        var granted = Rows(graph, "gears", "gear").SingleOrDefault(row => Scalar(row, "guid") == name);
                        if (!enabled || granted is null || Scalar(granted, "parentid") != Scalar(effect, "sourcename")
                            || decimal.Parse(Scalar(granted, "qty"), CultureInfo.InvariantCulture) <= 0) return false;
                        Add(index, effect, "granted-gear", new { consumers.Gear, consumers.Funding,
                            SourceId = Scalar(granted, "sourceid"), Quantity = decimal.Parse(Scalar(granted, "qty"), CultureInfo.InvariantCulture) });
                        break;
                    case "PathogenContactResist":
                    case "PathogenIngestionResist":
                    case "PathogenInhalationResist":
                    case "PathogenInjectionResist":
                    case "ToxinContactResist":
                    case "ToxinIngestionResist":
                    case "ToxinInhalationResist":
                    case "ToxinInjectionResist":
                        if (name != "" || Scalar(effect, "condition") != "" || !enabled) return false;
                        var modifiers = improvements.Where(row => Scalar(row, "improvementttype") == type
                            && Integer(row, "enabled") == 1).Select(row => Integer(row, "val")).ToArray();
                        if (!TryEvaluateResistance(attributes["BOD"], attributes["WIL"], modifiers, out var resistance))
                            return false;
                        Add(index, effect, "resistance", resistance!);
                        break;
                    case "Erased":
                    case "StreetCred":
                    case "StreetCredMultiplier":
                    case "Notoriety":
                    case "PublicAwareness":
                        var contribution = consumers.Reputation.Improvements.SingleOrDefault(row => row.ImprovementIndex == index);
                        if (contribution is null) return false;
                        Add(index, effect, "reputation", new { contribution, consumers.Reputation.Reputation });
                        break;
                    case "Overclocker":
                    case "LimitSpellDescriptor":
                    case "BlockSpellDescriptor":
                    case "LimitSpellCategory":
                    case "LimitSpiritCategory":
                        if (consumers.Extension is null || !consumers.Extension.TryGet(index, effect, out var extended711)) return false;
                        Add(index, effect, "mechanics711", extended711!);
                        break;
                    default:
                        // Cost effects must match exact selected source occurrences,
                        // including level, condition and interval; a type whitelist
                        // plus an unrelated successful price is insufficient.
                        if (!purchasedQualityIds.Contains(Scalar(effect, "sourcename"))
                            || !TryConsumePricedEffect(effect, qualityEffects, actual, authority, consumers, out var execution)) return false;
                        Add(index, effect, "creation-price", execution!);
                        break;
                }
            }
            string primaryArm = graph.Element("primaryarm") is null ? "Right" : Scalar(graph, "primaryarm");
            if (string.IsNullOrWhiteSpace(primaryArm)) primaryArm = "Right";
            if (!TryEvaluateHandedness(primaryArm, ambidextrous, out var hands)) return false;
            for (int index = 0; index < improvements.Length; index++)
                if (Scalar(improvements[index], "improvementttype") == "Ambidextrous")
                    Add(index, improvements[index], "handedness", hands!);

            var skillResults = new List<SkillResult>();
            var catalogs = authority.ActiveSkills.Concat(authority.KnowledgeSkills)
                .ToDictionary(row => (row.Kind, row.SourceSkillId));
            var newSkills = graph.Element("newskills") ?? throw new InvalidDataException();
            var savedSkills = Rows(newSkills, "skills", "skill").Concat(Rows(newSkills, "knoskills", "skill")).ToArray();
            if (savedSkills.Length != actual.Skills.Count) return false;
            var groups = Rows(newSkills, "groups", "group");
            if (groups.Length != actual.SkillGroups.Count) return false;
            foreach (var skill in actual.Skills)
            {
                if (!catalogs.TryGetValue((skill.Kind, skill.SourceSkillId), out var source)
                    || source.Name != skill.Name || source.DefaultAttribute != skill.DefaultAttribute) return false;
                var saved = savedSkills.Single(row => Scalar(row, "suid") == skill.SourceSkillId
                    && bool.Parse(Scalar(row, "isknowledge")) == (skill.Kind == CharacterCreationSkillKinds.Knowledge));
                if (!CharacterCareerSkillRatingRules.TryReadFreePoints(graph.Element("improvements"), skill.Name,
                    false, out var free) || free.Levels != 0 || free.Base != skill.GrantedRating) return false;
                CharacterCareerSkillGroupRating? groupRating = null;
                if (skill.SkillGroup is { Length: > 0 } groupName && actual.SkillGroups.Any(row => row.Name == groupName))
                {
                    var savedGroup = groups.Single(row => Scalar(row, "name") == groupName);
                    if (!CharacterCareerSkillRatingRules.TryReadFreePoints(graph.Element("improvements"), groupName,
                        true, out var groupFree) || groupFree.Levels != 0) return false;
                    var groupMembers = actual.Skills.Where(row => row.SkillGroup == groupName)
                        .Select(row => new CharacterCareerSkillRatingPoints(
                            Math.Max(0, row.EffectiveRating.GetValueOrDefault() - row.GrantedRating - row.GroupRating - row.KarmaLevels),
                            row.KarmaLevels, row.GrantedRating, 0)).ToArray();
                    if (!CharacterCareerSkillRatingRules.TryProjectGroup(new(Integer(savedGroup, "base"),
                        Integer(savedGroup, "karma"), groupFree.Base, 0), groupMembers, false,
                        100, out var projectedGroup) || projectedGroup.Rating != skill.GroupRating) return false;
                    groupRating = projectedGroup;
                }
                if (!CharacterCareerSkillRatingRules.TryProjectSkill(new(Integer(saved, "base"), Integer(saved, "karma"),
                        free.Base, 0), groupRating, authority.UsePointsOnBrokenGroups, 100, out var rating)) return false;
                if (!skill.IsNativeLanguage && rating.TotalBaseRating != skill.EffectiveRating) return false;
                if (skill.IsNativeLanguage && Scalar(saved, "isnativelanguage") != "True") return false;
                if (!attributes.TryGetValue(skill.DefaultAttribute, out int attribute)) return false;
                if (!TryEvaluate(new(attribute, rating.TotalBaseRating, skill.IsEnabled, skill.IsNativeLanguage,
                        source.CanDefault, skill.SpecializationName is not null, dice.GetValueOrDefault(skill.Name)), out var pool))
                    return false;
                skillResults.Add(new(skill.SourceSkillId, skill.Kind, skill.Name, rating.TotalBaseRating, skill.DefaultAttribute, pool!));
            }
            // Unallocated active skills still have real defaulting/action rules.
            // Execute those rules against the candidate's attributes and the
            // canonical availability gates; do not call catalogue parsing coverage.
            foreach (var source in authority.ActiveSkills.Where(source => !actual.Skills.Any(
                row => row.Kind == source.Kind && row.SourceSkillId == source.SourceSkillId)))
            {
                if (!attributes.TryGetValue(source.DefaultAttribute, out int attribute)) return false;
                bool enabled = CharacterCreationSkillsService.IsActionSkillAvailable695(authority, source, consumers.Movement);
                if (!TryEvaluate(new(attribute, 0, enabled, false, source.CanDefault, false,
                        dice.GetValueOrDefault(source.Name)), out var pool)) return false;
                skillResults.Add(new(source.SourceSkillId, source.Kind, source.Name, 0, source.DefaultAttribute, pool!));
            }
            for (int index = 0; index < improvements.Length; index++)
            {
                string type = Scalar(improvements[index], "improvementttype");
                string name = Scalar(improvements[index], "improvedname");
                if (type is not ("Skill" or "SkillBase" or "SkillGroupBase")) continue;
                var affected = skillResults.Where(row => type == "SkillGroupBase"
                    ? actual.Skills.Any(skill => skill.SourceSkillId == row.SourceId && skill.Kind == row.Kind && skill.SkillGroup == name)
                    : row.Name == name).ToArray();
                if (affected.Length == 0) return false;
                Add(index, improvements[index], type == "Skill" ? "action-pool" : "learned-rating", affected);
            }
            if (effectResults.Count != improvements.Length
                || effectResults.Select(row => row.Index).Distinct().Count() != improvements.Length) return false;
            result = new(hands!, skillResults.ToArray(), effectResults.OrderBy(row => row.Index).ToArray());
            return true;

            void Add(int index, XElement node, string consumer, object calculation)
                => effectResults.Add(new(index, Scalar(node, "improvementttype"),
                    CharacterCreationFinalizationDigest.ComputeUtf8(node.ToString(SaveOptions.DisableFormatting)), consumer, calculation));
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException
            or OverflowException or FormatException or NullReferenceException) { return false; }
    }

    private static bool TryConsumePricedEffect(XElement effect,
        IReadOnlyList<CharacterCreationLegacySourceProjector.SourceQualityEffect> sources,
        CharacterCreationAttributeSkillsPreview skills, CharacterCreationSkillsAuthority authority, DomainExecutions consumers, out object? execution)
    {
        execution = null;
        string type = Scalar(effect, "improvementttype");
        string? consumer = type switch
        {
            "TrustFund" or "LifestyleCost" or "BasicLifestyleCost" => consumers.Lifestyles,
            "FriendsInHighPlaces" => consumers.Contacts,
            "SkillCategoryKarmaCost" or "SkillCategoryKarmaCostMultiplier"
                or "SkillCategoryPointCostMultiplier" or "SkillCategorySpecializationKarmaCostMultiplier" => consumers.Purchases,
            _ => null
        };
        if (consumer is null || Integer(effect, "enabled") != 1 || Scalar(effect, "improvementsource") != "Quality")
            return false;
        var matching = sources.Where(source => source.ImprovementType == type
            && source.ImprovedName == Scalar(effect, "improvedname")
            && source.Value == decimal.Parse(Scalar(effect, "val"), CultureInfo.InvariantCulture)
            && source.Minimum == Integer(effect, "min") && source.Maximum == Integer(effect, "max")
            && CharacterCreationKnowledgeCategoryRules.TryLowerNativeCondition(source.Condition, out string condition)
            && condition == Scalar(effect, "condition")).ToArray();
        if (matching.Length == 0) return false;
        object? result = type switch
        {
            "TrustFund" or "LifestyleCost" or "BasicLifestyleCost" =>
                TryLifestylePrice711(consumers.LifestyleResult, out var lifestylePrice711) ? lifestylePrice711 : null,
            "FriendsInHighPlaces" => consumers.ContactResult is { } contacts
                ? new { contacts.ContactBudget, contacts.HighPlacesBudget, contacts.Contacts, contacts.GroupKarma } : null,
            _ => null
        };
        if (CharacterCreationKnowledgeCategoryRules.IsSupportedType(type))
        {
            var prices = skills.Skills.Where(row => row.KnowledgeCategoryPrice is not null)
                .Select(row => row.KnowledgeCategoryPrice!).Where(price => matching.All(source =>
                    price.Effects.Any(applied => applied.SourceId == source.SourceId
                        && applied.SourceNodeDigest == source.SourceNodeDigest && applied.OptionId == source.OptionId
                        && applied.Level == source.Level && applied.Container == source.Container
                        && applied.Position == source.Position && applied.ImprovementType == source.ImprovementType
                        && applied.Category == source.ImprovedName && applied.Value == source.Value
                        && applied.Minimum == source.Minimum && applied.Maximum == source.Maximum
                        && applied.Condition == source.Condition))).ToArray();
            if (prices.Length == 0)
            {
                if (!TryKnowledgeInactivity711(skills, authority, matching, out result)) return false;
            }
            else result = prices;
        }
        if (result is null) return false;
        execution = new { Consumer = consumer, Sources = matching, Result = result };
        return true;
    }

    private static XElement[] Rows(XElement root, string container, string element)
    {
        var node = root.Element(container);
        if (node is null) return [];
        if (node.HasAttributes || node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            || node.Elements().Any(child => child.Name != element)) throw new InvalidDataException();
        return node.Elements().ToArray();
    }
    private static string Scalar(XElement node, string name)
    {
        var rows = node.Elements(name).ToArray();
        if (rows.Length != 1 || rows[0].HasAttributes || rows[0].HasElements) throw new InvalidDataException();
        return rows[0].Value;
    }
    private static int Integer(XElement node, string name)
        => int.Parse(Scalar(node, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static void Marker(XElement node)
    {
        if (Scalar(node, "improvedname") != "" || Scalar(node, "condition") != ""
            || Integer(node, "val") != 0 || Integer(node, "addtorating") != 0
            || Integer(node, "min") != 0 || Integer(node, "max") != 0) throw new InvalidDataException();
    }
    private static void MarkerTarget(XElement node)
    {
        if (Scalar(node, "improvedname") == "" || Scalar(node, "condition") != ""
            || Integer(node, "val") != 0 || Integer(node, "addtorating") != 0
            || Integer(node, "min") != 0 || Integer(node, "max") != 0) throw new InvalidDataException();
    }
    private static void ValidateImprovement(XElement node)
    {
        string[] fields = ["target", "unique", "improvedname", "sourcename", "min", "max", "aug", "augmax",
            "val", "rating", "exclude", "condition", "improvementttype", "improvementsource", "custom",
            "customname", "customid", "customgroup", "addtorating", "enabled", "order", "notes", "notesColor"];
        if (node.Name != "improvement" || node.HasAttributes
            || node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            || node.Elements().Any(child => !fields.Contains(child.Name.LocalName, StringComparer.Ordinal)
                || child.Name.Namespace != XNamespace.None || child.HasAttributes || child.HasElements)
            || node.Elements().GroupBy(child => child.Name).Any(group => group.Count() != 1)
            || Integer(node, "enabled") is not (0 or 1)
            || Scalar(node, "custom") != "False" || Scalar(node, "target") != ""
            || Scalar(node, "exclude") != ""
            || (node.Element("unique")?.Value ?? "") is not ("" or "enableattribute" or "enabletab")
            || Integer(node, "aug") != 0 || Integer(node, "augmax") != 0
            || Integer(node, "order") != 0 || Integer(node, "rating") is not (0 or 1))
            throw new InvalidDataException();
        string type = Scalar(node, "improvementttype");
        if (type is not ("Attribute" or "SpecialTab")
            && (Integer(node, "rating") != 1 || (node.Element("unique")?.Value ?? "") != "")) throw new InvalidDataException();
        if (!CharacterCreationKnowledgeCategoryRules.IsSupportedType(type)
            && (Integer(node, "min") != 0 || Integer(node, "max") != 0 || Integer(node, "addtorating") != 0))
            throw new InvalidDataException();
    }
}
