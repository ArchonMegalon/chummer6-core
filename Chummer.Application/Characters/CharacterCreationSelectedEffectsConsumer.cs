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
internal sealed class CharacterCreationSelectedEffectsConsumer
{
    private readonly string authorityDigest;
    private readonly string evaluationDigest;
    private readonly XElement compiledGraph;
    private readonly CharacterCreationFinalizationDelta[] compiledDeltas;
    private readonly IReadOnlyList<CharacterCreationCompiledSourceEffect> sourceConsumptions;

    private CharacterCreationSelectedEffectsConsumer(CharacterCreationCompleteAssessment authority,
        XElement graph, IReadOnlyList<CharacterCreationFinalizationDelta> deltas,
        IReadOnlyList<CharacterCreationCompiledSourceEffect> sourceConsumptions)
    {
        this.sourceConsumptions = Array.AsReadOnly(sourceConsumptions.ToArray());
        compiledGraph = new(graph);
        compiledDeltas = deltas.ToArray();
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        evaluationDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Authority = authorityDigest,
            Operation = CharacterCreationConsumerPolicyOperation.SelectedEffects,
            Graph = graph.ToString(SaveOptions.DisableFormatting),
            Deltas = deltas
        });
    }

    internal IReadOnlyList<CharacterCreationCompiledSourceEffect> CopySourceConsumptions() => sourceConsumptions;
    internal XElement CopyCompiledGraph() => new(compiledGraph);
    internal IReadOnlyList<CharacterCreationFinalizationDelta> CopyDeltas() => compiledDeltas.ToArray();

    internal string? DigestFor(CharacterCreationCompleteAssessment authority)
        => authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
            && authority.Phase == CharacterCreationCompletePhase.Authority
            && authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
                ? evaluationDigest : null;

    internal static CharacterCreationSelectedEffectsConsumer? FromFull(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            RuleSourceExecutionSchema = review.Request.RuleSourceExecutionSchema,
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
        CancellationToken cancellationToken = default, CharacterCreationOriginalCharacterSource? originalCharacter = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            RuleSourceExecutionSchema = request.RuleSourceExecutionSchema,
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
    private static string CompilationSeed(CharacterCreationCompleteAssessment authority)
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
            if (original is not null)
            {
                // Apply the same selected heritage preparation as finalization,
                // on a disposable copy; preserve the saved bootstrap and custom nodes.
                foreach (string name in new[] { "qualities", "gears", "contacts", "lifestyles" }
                    .Concat(inputs.Loadout is null ? [] : new[] { "weapons", "armors" }))
                {
                    var containers = root.Elements(name).ToArray();
                    if (containers.Length > 1 || containers.Any(node => node.HasElements || node.HasAttributes
                            || !string.IsNullOrWhiteSpace(node.Value))) return null;
                }
                _ = CharacterCreationCareerBaseline.InitializeMissing(root);
                CharacterCareerReputationProjector.ValidateSavedInputShape(root);
                root.SetElementValue("metatype", heritage.MetatypeName);
                root.SetElementValue("metavariant", heritage.MetavariantName ?? string.Empty);
                root.SetElementValue("buildmethod", inputs.Prerequisite.BuildMethod);
            }
            using var sourceTrace = captureRuleSources ? CharacterCreationCompiledSourceTrace.Begin() : null;
            var deltas = new List<CharacterCreationFinalizationDelta>();
            int order = 0;
            if (CompileSelectedGraph(document, inputs, deltas, ref order, cancellationToken) is not null) return null;
            return new(authority, root, deltas, sourceTrace?.Finish() ?? []);
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or FormatException
            or OverflowException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
