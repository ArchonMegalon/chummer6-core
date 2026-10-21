using Chummer.Contracts.Characters;
using K955 = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using C955 = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using E955 = Chummer.Contracts.Characters.CharacterCreationCompleteEvaluability;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    internal static bool CanAttemptOtherSettings955(CharacterCreationCompletionReviewRequest request)
        => request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && CanAttemptSourceScopes696(request)
            && CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request);

    private void CompleteOtherSettings955(CharacterCreationCompleteAssessment authority,
        CharacterCreationCompleteConsumerExecution? consumers,
        IReadOnlyDictionary<string, string> sourceScopes, IDictionary<string, string> completed)
    {
        if (!CanAttemptOtherSettings955(request)
            || Inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || Inputs.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest
            || consumers?.AssessProfileSettings955(authority) is not { } evaluated)
            return;
        var domain = authority.Obligations.SingleOrDefault(row => row.Kind == K955.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.OtherSettings);
        var profiles = Inputs.EffectivePolicy!.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
            && row.SourceId == "profile").ToArray();
        if (domain is not { Evaluability: E955.CandidateDependent }
            || profiles is not [var profile]
            || !sourceScopes.ContainsKey($"source:{profile.Kind}:{profile.Order}")
            || sourceScopes.Keys.Any(id => !executedSourceScopes955.Contains(id))) return;

        // The entire frozen inventory is retained. In particular, an unknown
        // profile node cannot disappear into the finite typed setting projection:
        // every source obligation must also have completed in this invocation.
        var required = authority.Obligations.Where(row => (row.Kind is K955.Source or K955.Sourcebook
            or K955.SkillLimit or K955.Setting or K955.ConsumerOperation or K955.ActiveGraph
            || row.Kind == K955.PolicyDomain && row.Domain is CharacterCreationPolicyDomain.Profile
                or CharacterCreationPolicyDomain.Books or CharacterCreationPolicyDomain.SkillLimits)
            // Errata is an independent aggregate, not a setting consumer.
            // Its obligations remain frozen and are completed only by CompleteErrata.
            && !(row.Kind is K955.ConsumerOperation or K955.ActiveGraph
                && row.Operation == CharacterCreationConsumerPolicyOperation.Errata)).ToArray();
        if (!required.Any(row => row.Kind == K955.Setting)
            || !required.Any(row => row.Kind == K955.PolicyDomain && row.Domain == CharacterCreationPolicyDomain.Profile)) return;
        var verdicts = evaluated.Obligations.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var executions = new List<object>();
        foreach (var row in required)
        {
            if (row.Evaluability is not (E955.Supported or E955.CandidateDependent)) return;
            string? digest;
            if (!completed.TryGetValue(row.Id, out digest))
            {
                // Source and book authority must come from this retained source
                // execution, never from a borrowed consumer assessment/report.
                if (row.Kind is K955.Source or K955.Sourcebook
                    || !verdicts.TryGetValue(row.Id, out var verdict)
                    || verdict.AuthorityDigest != row.AuthorityDigest
                    || verdict.Coverage is not (C955.Evaluated or C955.ProvenInactive)) return;
                digest = verdict.EvaluationDigest;
            }
            if (!CharacterCreationFinalizationDigest.IsCanonical(digest)) return;
            executions.Add(new { row.Id, row.AuthorityDigest, EvaluationDigest = digest });
        }
        completed.Add(domain.Id, CharacterCreationFinalizationDigest.Compute(new
        {
            domain.AuthorityDigest, authority.InputsSnapshotDigest, authority.RequestDigest,
            authority.PolicyDigest, authority.SettingsObservationDigest,
            Inputs.RuleSourceExecutionSchema, Inputs.RuleSourceCaptureDigest, Executions = executions
        }));
    }
}
