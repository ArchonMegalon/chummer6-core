using System.Xml;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private bool amendmentAbsenceExecuted954;
    private string? amendmentAbsence954;

    // The retained-only resolution is already matched to the original observation
    // by Resolve. Reuse its execution within this request, including admission and
    // both full/selected coverage reconstruction. No public receipt can supply it.
    internal string? AmendmentAbsence954For(string snapshot, string rawXml, string policy, string requested)
    {
        if (request.CompleteAssessmentSchema != CharacterCreationCompleteAssessment.SchemaV4
            || !CanAttemptSourceScopes696(request)
            || !original.MatchesInputs(Inputs.SnapshotDigest)
            || snapshot != Inputs.SnapshotDigest || rawXml != Inputs.Skills.Binding.RawCharacterXmlDigest
            || policy != Inputs.EffectivePolicy?.PolicyDigest || requested != requestDigest
            || Inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || Inputs.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest) return null;
        if (!amendmentAbsenceExecuted954)
        {
            amendmentAbsenceExecuted954 = true;
            try
            {
                string? executed = resolution.ExecuteAmendmentAbsence954();
                if (CharacterCreationFinalizationDigest.IsCanonical(executed))
                    amendmentAbsence954 = CharacterCreationFinalizationDigest.Compute(new
                    {
                        Inputs.RuleSourceExecutionSchema, Inputs.RuleSourceCaptureDigest,
                        Inputs.SnapshotDigest, RawCharacterXmlDigest = rawXml,
                        PolicyDigest = policy, RequestDigest = requestDigest, Execution = executed
                    });
            }
            catch (Exception error) when (error is IOException or InvalidOperationException
                or ArgumentException or OverflowException or XmlException) { }
        }
        return amendmentAbsence954;
    }

    private void CompleteAmendmentAbsence954(CharacterCreationCompleteAssessment authority,
        IDictionary<string, string> completed)
    {
        if (AmendmentAbsence954For(authority.InputsSnapshotDigest, authority.RawCharacterXmlDigest,
                authority.PolicyDigest, authority.RequestDigest) is not { } execution) return;
        foreach (var row in authority.Obligations.Where(row =>
            row.Kind == CharacterCreationCompleteObligationKind.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.Amendments
            && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent))
            completed.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                row.AuthorityDigest, AmendmentAbsenceExecution = execution
            }));
    }
}
