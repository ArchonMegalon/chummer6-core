using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;

namespace Chummer.Application.Characters;

/// <summary>
/// Compiles the complete admitted selection on each evaluation route, below the
/// review/finalization boundary. The explicit original-character variant compiles
/// into a validated copy of the observation. Neither variant discharges CharacterMechanics,
/// amendments, campaign policy, cash, lifecycle or persistence obligations.
/// </summary>
internal sealed partial class CharacterCreationSelectedEffectsConsumer
{
    private readonly string authorityDigest;
    private readonly string evaluationDigest;
    private readonly XElement compiledGraph;
    private readonly string? inventoryAdmission1253;
    private readonly CharacterCreationOriginalInventory3475? originalLoadout3511;
    private readonly CharacterCreationFinalizationDelta[] compiledDeltas;
    private readonly IReadOnlyList<CharacterCreationCompiledSourceEffect> sourceConsumptions;
    private readonly CharacterCreationIdentityEvaluation? identity;

    private CharacterCreationSelectedEffectsConsumer(CharacterCreationCompleteAssessment authority,
        XElement graph, IReadOnlyList<CharacterCreationFinalizationDelta> deltas,
        IReadOnlyList<CharacterCreationCompiledSourceEffect> sourceConsumptions, string? inventoryAdmission1253,
        CharacterCreationOriginalInventory3475? originalLoadout3511,
        CharacterCreationOriginalCharacterSource? original, string? lifecycleResultSchema)
    {
        this.originalLoadout3511 = originalLoadout3511;
        this.inventoryAdmission1253 = inventoryAdmission1253;
        this.sourceConsumptions = Array.AsReadOnly(sourceConsumptions.ToArray());
        compiledGraph = CharacterCreationAwakenedLegacyProjector.CopyPhysicalTalentGraph1279(graph);
        compiledDeltas = deltas.ToArray();
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        evaluationDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Authority = authorityDigest,
            Operation = CharacterCreationConsumerPolicyOperation.SelectedEffects,
            Graph = graph.ToString(SaveOptions.DisableFormatting),
            Deltas = deltas
        });
        if (lifecycleResultSchema == CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            && original?.CreationLifecycleDigestFor(authority) is not null)
            identity = CharacterCreationIdentityEvaluator.Evaluate(authority, original.CopyDocument().Root!,
                compiledGraph, evaluationDigest);
    }

    internal CharacterCreationIdentityEvaluation? IdentityFor(CharacterCreationCompleteAssessment authority)
        => identity is not null && DigestFor(authority) is not null
            && identity.InputsSnapshotDigest == authority.InputsSnapshotDigest
            && identity.RequestDigest == authority.RequestDigest
            && identity.RawCharacterXmlDigest == authority.RawCharacterXmlDigest
            && identity.CompilationDigest == evaluationDigest ? identity : null;

    internal IReadOnlyList<CharacterCreationCompiledSourceEffect> CopySourceConsumptions() => sourceConsumptions;
    internal XElement CopyCompiledGraph()
        => CharacterCreationAwakenedLegacyProjector.CopyPhysicalTalentGraph1279(compiledGraph);

    // Carry the actual compiler's private proof, never capability-discovery or
    // serialized evidence. Revalidate whole owners/effects and current sources
    // against this assessment's proposal and real compiler instance seed.
    internal CharacterCreationOriginalInventory3475? OriginalLoadout3511(
        CharacterCreationCompleteAssessment authority, XElement graph,
        CharacterCreationLoadoutCompletionProjection? proposal)
    {
        if (DigestFor(authority) is null
            || originalLoadout3511 is not null
                && !originalLoadout3511.MatchesCombinedLoadout3497(graph, proposal, CompilationSeed(authority)))
            throw new InvalidDataException(CharacterCreationPhysicalInitiative886.Blocker);
        return originalLoadout3511;
    }
    internal bool TryPriceFocus1253(XElement profile, out CharacterCreationFocusKarma1220.Calculation? calculation)
    {
        calculation = null;
        return inventoryAdmission1253 is not null
            && CharacterCreationFocusKarma1220.TryCalculate(profile, compiledGraph, out calculation, inventoryAdmission1253);
    }
    internal IReadOnlyList<CharacterCreationFinalizationDelta> CopyDeltas() => compiledDeltas.ToArray();

    internal string? DigestFor(CharacterCreationCompleteAssessment authority)
        => authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
            && authority.Phase == CharacterCreationCompletePhase.Authority
            && authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
                ? evaluationDigest : null;

    internal static CharacterCreationSelectedEffectsConsumer? FromFull(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority, CancellationToken cancellationToken = default,
        CharacterCreationCompleteRuleSourceExecution? essence1222 = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CharacterCreationCompleteRuleSourceExecution.WantsEssence1222(review.Request) && essence1222 is null) return null;
        if (!CanEvaluate(authority, inputs.SnapshotDigest, review.Request)
            || review.Blockers.Count != 0 || review.Skills is null || review.UpstreamBudget is null
            || review.Lifestyles is null || review.Contacts is null) return null;
        CharacterCreationOriginalCharacterSource? original = null;
        if ((inputs.OriginalCharacter is null) != (review.Request.OriginalCharacterExecutionSchema is null)
            || review.Request.OriginalCharacterExecutionSchema is not null
                && !CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out original, cancellationToken))
            return null;
        var qualities = review.UpstreamBudget.Qualities.Qualities;
        var lifestyles = review.Lifestyles;
        string seed = CompilationSeed(authority);
        return Compile(authority, new(inputs.Skills.PrerequisiteDraft, review.Skills,
            inputs.Skills.SelectedActiveSkillPoints, inputs.Skills.SelectedSkillGroupPoints,
            new(qualities.Selections, qualities.KarmaRemaining, qualities.SourceAnchorIds),
            new(lifestyles.Gear.Lines, lifestyles.Gear.Budget, lifestyles.Gear.SourceAnchorIds),
            new(lifestyles.Authority, lifestyles.Lifestyles, lifestyles.StartingLifestyleId, lifestyles.SourceAnchorIds),
            new(inputs.Contacts.Policy, review.Request.Selections.Contacts,
                inputs.Contacts.Qualities.Authority.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
                inputs.Contacts.Qualities.Authority.QualityKarmaLimit,
                inputs.Contacts.Qualities.Authority.MayExceedPositiveQualityLimit, review.Contacts.GroupKarma),
            new FullAwakenedGraphInputs(inputs.Magic, review.UpstreamBudget.Magic), seed, seed)
        {
            LifecycleResultSchema = review.Request.LifecycleResultSchema, RuleSourceExecutionSchema = review.Request.RuleSourceExecutionSchema,
            Essence1222 = CharacterCreationCompleteRuleSourceExecution.WantsEssence1222(review.Request) ? essence1222 : null,
            PhysicalInitiative886 = inputs.PhysicalInitiative886,
            Loadout = review.Loadout,
            AmmunitionAssignments = review.Request.Selections.AmmunitionAssignments,
            OriginalLoadoutInstances = inputs.Loadout?.PendingDraft?.Instances
        }, cancellationToken, original,
            CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(review.Request));
    }

    internal static CharacterCreationSelectedEffectsConsumer? FromSelected(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationSelectedCompletionEvaluation result, CharacterCreationCompleteAssessment authority,
        IReadOnlyList<CharacterCreationLoadoutInstance>? originalLoadoutInstances,
        CancellationToken cancellationToken = default, CharacterCreationOriginalCharacterSource? originalCharacter = null,
        CharacterCreationCompleteRuleSourceExecution? essence1222 = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CharacterCreationCompleteRuleSourceExecution.WantsEssence1222(request) && essence1222 is null) return null;
        if (!CanEvaluate(authority, witness.ProvenanceInputsSnapshotDigest, request)) return null;
        if (witness.OriginalCharacterExecutionSchema != request.OriginalCharacterExecutionSchema
            || (request.OriginalCharacterExecutionSchema is null) != (originalCharacter is null)
            || originalCharacter is not null && !originalCharacter.MatchesInputs(witness.ProvenanceInputsSnapshotDigest))
            return null;
        string seed = CompilationSeed(authority);
        return Compile(authority, new(witness.Skills.PrerequisiteDraft, result.Skills,
            witness.Skills.SelectedActiveSkillPoints, witness.Skills.SelectedSkillGroupPoints,
            new(result.Qualities.Selections, result.Qualities.KarmaRemaining, result.Qualities.SourceAnchorIds),
            new(result.Gear, result.GearBudget, result.GearSourceAnchorIds),
            new(result.Lifestyles.Authority, result.Lifestyles.Lifestyles,
                result.Lifestyles.StartingLifestyleId, result.Lifestyles.SourceAnchorIds),
            new(witness.ContactsPolicy, request.Selections.Contacts,
                witness.Qualities.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
                witness.Qualities.QualityKarmaLimit, witness.Qualities.MayExceedPositiveQualityLimit,
                result.Contacts.GroupKarma),
            new SelectedAwakenedGraphInputs(witness.Magic, witness.Skills.Attributes.Attributes,
                result.Magic.Proposed.SourceAnchorIds), seed, seed)
        {
            LifecycleResultSchema = request.LifecycleResultSchema, RuleSourceExecutionSchema = request.RuleSourceExecutionSchema,
            Essence1222 = CharacterCreationCompleteRuleSourceExecution.WantsEssence1222(request) ? essence1222 : null,
            PhysicalInitiative886 = witness.PhysicalInitiative886,
            Loadout = result.Loadout,
            AmmunitionAssignments = request.Selections.AmmunitionAssignments,
            OriginalLoadoutInstances = originalLoadoutInstances
        }, cancellationToken, originalCharacter,
            CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request));
    }

    private static bool CanEvaluate(CharacterCreationCompleteAssessment authority, string snapshot,
        CharacterCreationCompletionReviewRequest request)
        => authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
            && authority.Phase == CharacterCreationCompletePhase.Authority
            && authority.InputsSnapshotDigest == snapshot && snapshot == request.InputsSnapshotDigest
            && authority.SelectionsDigest == CharacterCreationFinalizationDigest.Compute(request.Selections)
            && CharacterCreationCompletionReviewRules.HasValidOriginalCharacterExecution(request)
            && CharacterCreationFinalizationDigest.IsCanonical(snapshot);

    // ReviewDigest includes the resulting assessment: using it here would be
    // circular. These are temporary compiler IDs, never persisted native IDs.
    internal static string CompilationSeed(CharacterCreationCompleteAssessment authority)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            authority.Schema, authority.InputsSnapshotDigest, authority.SelectionsDigest
        });

    private static CharacterCreationSelectedEffectsConsumer? Compile(
        CharacterCreationCompleteAssessment authority, SelectedGraphInputs inputs, CancellationToken cancellationToken,
        CharacterCreationOriginalCharacterSource? original,
        bool captureRuleSources)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (inputs.Prerequisite.HeritageSelection is not { } heritage) return null;
            var document = original?.CopyDocument() ?? new XDocument(new XElement("character", new XElement("created", "False"),
                new XElement("metatype", heritage.MetatypeName),
                new XElement("metavariant", heritage.MetavariantName ?? string.Empty),
                new XElement("buildmethod", inputs.Prerequisite.BuildMethod)));
            var root = document.Root!;
            CharacterCreationOwnedGearGraph1233? ownedGear1233 = null;
            if (original is not null || inputs.Gear.Lines.Any(line => line.Focus is not null))
            {
                if (original is null
                    || !CharacterCreationOwnedGearGraph1233.TryPrepare(root, original.WorkspaceId1233, inputs.Gear.Lines, out ownedGear1233,
                        inputs.Essence1222?.OriginalInventorySources3475,
                        original.PriorityGrants3563(inputs.Essence1222?.OriginalInventorySources3475))) return null;
            }
            inputs = inputs with { OwnedGear1233 = ownedGear1233 };
            if (original is not null)
            {
                // Apply the same selected heritage preparation as finalization,
                // on a disposable copy; preserve the saved bootstrap and custom nodes.
                foreach (string name in new[] { "qualities", "gears", "contacts", "lifestyles" }
                    .Concat(inputs.Loadout is null ? [] : new[] { "weapons", "armors" }))
                {
                    if (name == "gears" && ownedGear1233 is not null) continue;
                    if (name is "weapons" or "armors" && ownedGear1233?.KeepsOriginalLoadout3475 == true) continue;
                    var containers = root.Elements(name).ToArray();
                    if (containers.Length > 1 || containers.Any(node => node.HasElements || node.HasAttributes
                            || !string.IsNullOrWhiteSpace(node.Value))) return null;
                }
                _ = CharacterCreationCareerBaseline.InitializeMissing(root);
                CharacterCareerReputationProjector.ValidateSavedInputShape(root);
                CompilePrerequisiteSelection(root, inputs.Prerequisite);
            }
            using var sourceTrace = captureRuleSources ? CharacterCreationCompiledSourceTrace.Begin() : null;
            var deltas = new List<CharacterCreationFinalizationDelta>();
            int order = 0;
            if (CompileSelectedGraph(document, inputs, deltas, ref order, cancellationToken) is not null) return null;
            string? inventoryAdmission1253 = null;
            if (ownedGear1233 is not null && !ownedGear1233.TryInventoryDigest1253(root, out inventoryAdmission1253)) return null;
            return new(authority, root, deltas, sourceTrace?.Finish() ?? [], inventoryAdmission1253,
                ownedGear1233?.OriginalLoadout3478, original, inputs.LifecycleResultSchema)
            {
                LifecycleResultSchema1272 = inputs.LifecycleResultSchema,
                nativeLoadout1432 = inputs.Essence1222?.EvaluateSourceProvenPhysicalLoadout3847(root, inputs.Prerequisite)
                    ?? CharacterCreationNativeLoadout1432.Evaluate(root,
                    inputs.LifecycleResultSchema, inputs.Awakened, inputs.Prerequisite,
                    inputs.Loadout, ownedGear1233?.OriginalLoadout3478, inputs.OriginalReviewDigest),
                nativeMagic1430 = CharacterCreationNativeMagic1430.Evaluate(root, inputs.LifecycleResultSchema,
                    inputs.Awakened, inputs.Prerequisite, inputs.Essence1222?.AppliedEssence1222),
                matrixInitiative = CharacterCreationMatrixInitiative.Evaluate(root,
                    inputs.LifecycleResultSchema, inputs.Awakened, inputs.Prerequisite),
                nativeContactSpecialty = CharacterCreationNativeContactSpecialty.Evaluate(root,
                    inputs.LifecycleResultSchema, inputs.Awakened, inputs.Prerequisite, inputs.Skills),
                nativeSettings1420 = CharacterCreationNativeSettings1420.Evaluate(root,
                    inputs.LifecycleResultSchema, inputs.Awakened, inputs.Prerequisite),
                astralInitiative1249 = CharacterCreationAstralInitiative1249.Evaluate(root,
                    inputs.LifecycleResultSchema, inputs.Awakened, inputs.Prerequisite)
            };
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or FormatException
            or OverflowException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
