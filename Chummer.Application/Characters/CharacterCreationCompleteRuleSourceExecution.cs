using Q947 = Chummer.Application.Characters.CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947;
using Chummer.Contracts.Characters;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

/// <summary>Retained reconstruction plus actual typed execution. No receipt can
/// instantiate completed coverage. The graph still comes from the original compiler.</summary>
internal sealed partial class CharacterCreationCompleteRuleSourceExecution(
    CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
    CharacterCreationRuleSourceResolution resolution, CharacterCreationOriginalCharacterSource original)
{
    internal CharacterCreationCompletionReviewInputs Inputs { get; } = inputs;
    private readonly string requestDigest = CharacterCreationFinalizationDigest.Compute(request);

    internal IReadOnlyDictionary<string, string> Complete(CharacterCreationCompleteAssessment authority,
        CharacterCreationSkillsService.CompleteSkillTrace? skills, CharacterCreationSelectedEffectsConsumer? effects,
        CharacterCreationGearService.CompleteCostExecution? gear,
        CharacterCreationQualitiesCompletionEvaluation? qualities, CharacterCreationAttributeSkillsPreview actualSkills,
        CharacterCreationCompleteMagicExecution? magic = null,
        CharacterCreationResourcesService.CompleteFundingExecution? funding = null,
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles = null,
        CharacterCreationCompleteProfileSettingsExecution? profileSettings = null, Q947? qualityLimit947 = null,
        CharacterCreationCompleteConsumerExecution? consumers955 = null,
        CharacterCreationCompleteContactsExecution? contacts = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (skills is not { Completed: true } || effects is null
            || !original.MatchesInputs(Inputs.SnapshotDigest)
            || authority.InputsSnapshotDigest != Inputs.SnapshotDigest || authority.RequestDigest != requestDigest
            || authority.PolicyDigest != Inputs.EffectivePolicy?.PolicyDigest
            || effects.DigestFor(authority) is not { } effectsDigest) return result;
        IReadOnlyDictionary<string, string> consumed;
        try { consumed = resolution.ConsumeSkillLimits(skills.Scopes.ToDictionary(row => row.SettingId, row => row.Limit)); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OverflowException)
        { return result; }
        var graph = effects.CopyCompiledGraph();
        var deltas = effects.CopyDeltas();
        var purchases = ExecutePurchases(authority, gear, qualities, graph, deltas);
        var skillSources = ExecuteSkillsSource(actualSkills, qualities);
        // Execute once before profile accounting; retain the private typed result.
        var sourcebooks = ExecuteSourcebooks800(authority);
        var sourceScopes = ExecuteRemainingSourceScopes(authority, effects, magic, funding, actualSkills, lifestyles, sourcebooks, profileSettings, qualityLimit947, qualities, contacts);
        consumed = consumed.Concat(purchases).Concat(skillSources).Concat(sourceScopes)
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        string Bind(object execution) => CharacterCreationFinalizationDigest.Compute(new
        {
            Inputs.RuleSourceExecutionSchema, Inputs.RuleSourceCaptureDigest, authority.InputsSnapshotDigest,
            authority.RequestDigest, authority.RawCharacterXmlDigest, authority.PolicyDigest,
            Execution = execution, Scopes = skills.Scopes, Effects = effectsDigest,
            Graph = graph.ToString(SaveOptions.DisableFormatting), Deltas = deltas
        });
        foreach (var row in authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Source))
            if (row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent
                && consumed.TryGetValue(row.Id, out string? trace))
                result.Add(row.Id, Bind(new { row.AuthorityDigest, Trace = trace }));
        var amendments = authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Source
            && row.SourceKind is CharacterCreationPolicySourceKind.GlobalAmend or CharacterCreationPolicySourceKind.SelectedAmend).ToArray();
        // A nonempty, exhaustively executed inventory is required. Never infer inactivity.
        if (amendments.Length > 0 && amendments.All(row => result.ContainsKey(row.Id)))
        {
            string digest = Bind(amendments.Select(row => new { row.Id, Digest = result[row.Id] }).ToArray());
            // The policy consumer and active graph describe the same amendment
            // scope. Both require this exhaustive, privately bound source trace.
            foreach (var row in authority.Obligations.Where(row =>
                row.Kind is CharacterCreationCompleteObligationKind.ConsumerOperation or CharacterCreationCompleteObligationKind.ActiveGraph
                && row.Operation == CharacterCreationConsumerPolicyOperation.Amendments
                && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent))
                result.Add(row.Id, digest);
        }
        CompleteAmendmentAbsence954(authority, result);
        CompleteErrata(authority, effects, result);
        CompleteSourcebooks800(authority, result, sourcebooks);
        CompleteOtherSettings955(authority, consumers955, sourceScopes, result);
        return result;
    }
}
