using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Apply the retained effective payload through its real source consumer,
    // including canonical power caps, tradition spirit-form handling and
    // spell/form saved fields. A parse, merge, or matching cap cannot enter
    // here without the original private compilation observation.
    private static bool TryReplayCorrectedSource(CharacterCreationCompiledSourceEffect effect,
        out string executionDigest)
    {
        executionDigest = string.Empty;
        try
        {
            var root = new XElement("character");
            var deltas = new List<CharacterCreationFinalizationDelta>();
            int order = 0;
            using var trace = CharacterCreationCompiledSourceTrace.Begin();
            if (effect.Scope is "traditions" or "streams")
                CharacterCreationAwakenedLegacyProjector.ApplyTradition(root, effect.Source,
                    effect.CompilerSeed, deltas, ref order);
            else
            {
                string? element = effect.Scope switch
                {
                    "powers" => "power", "spells" => "spell", "complexforms" => "complexform", _ => null
                };
                if (element is null) return false;
                CharacterCreationAwakenedLegacyProjector.ApplyOptions(root, effect.Scope, element,
                    [effect.Source], [effect.Source.Identity], effect.CompilerSeed, deltas, ref order);
            }
            var observed = trace.Finish();
            if (observed is not [var replayed] || replayed.SourceXml != effect.SourceXml
                || replayed.EffectXml != effect.EffectXml || replayed.ProjectionDigest != effect.ProjectionDigest
                || deltas.Count != 1) return false;
            executionDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                effect.Scope, effect.SourceId, effect.ProjectionDigest,
                Source = effect.SourceXml, Compiled = replayed.EffectXml, Deltas = deltas
            });
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or ArgumentException or OverflowException or XmlException or FormatException) { return false; }
    }

    private void CompleteErrata(CharacterCreationCompleteAssessment authority,
        CharacterCreationSelectedEffectsConsumer effects, IDictionary<string, string> completed)
    {
        if (!CanAttemptSourceScopes696(request)
            || Inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || effects.DigestFor(authority) is not { } compiler) return;
        var sources = authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Source).ToArray();
        // All frozen sources stay required, including profile, prerequisites,
        // amendments and scopes outside the bounded compiler adapters above.
        // This deliberately cannot turn a partial source report into Errata.
        if (sources.Length == 0 || sources.Any(row => !completed.ContainsKey(row.Id))) return;
        var executions = new List<string>();
        foreach (var effect in effects.CopySourceConsumptions())
        {
            if (!TryReplayCorrectedSource(effect, out string execution)) return;
            executions.Add(execution);
        }
        string digest = CharacterCreationFinalizationDigest.Compute(new
        {
            Inputs.RuleSourceCaptureDigest, authority.InputsSnapshotDigest, authority.RequestDigest,
            authority.PolicyDigest, Compiler = compiler, Executions = executions,
            Sources = sources.Select(row => new { row.Id, row.AuthorityDigest, Execution = completed[row.Id] }).ToArray()
        });
        foreach (var row in authority.Obligations.Where(row =>
            row.Kind is CharacterCreationCompleteObligationKind.ConsumerOperation or CharacterCreationCompleteObligationKind.ActiveGraph
            && row.Operation == CharacterCreationConsumerPolicyOperation.Errata
            && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent))
            completed.Add(row.Id, digest);
        // This domain has exactly the same exhaustive corrected-source boundary
        // as the graph above. Partial ledgers, absent sources and failed compiler
        // replay returned before this point; none can become domain coverage.
        foreach (var row in authority.Obligations.Where(row =>
            row.Kind == CharacterCreationCompleteObligationKind.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.Errata
            && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent))
            completed.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                row.AuthorityDigest, CorrectedSourceExecution = digest
            }));
    }
}
