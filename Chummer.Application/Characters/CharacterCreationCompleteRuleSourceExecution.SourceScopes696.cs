using Q947 = Chummer.Application.Characters.CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // The historical V2 opt-in predates these consumers. Only an actual Complete
    // request enables them; saved non-Complete witnesses keep their original rows.
    internal static bool CanAttemptSourceScopes696(CharacterCreationCompletionReviewRequest request)
        => request.PolicyRequirement == CharacterCreationPolicyRequirement.Complete
            && request.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4);

    private IReadOnlyList<CharacterCreationRuleSourceScopeResult> sourceScopeReports = [];
    private readonly HashSet<string> executedSourceScopes955 = new(StringComparer.Ordinal);
    // Private diagnostics retain partial accounting too; they do not mint
    // assessment coverage or cross the public replay/witness boundary.
    internal IReadOnlyList<CharacterCreationRuleSourceScopeResult> SourceScopeReports => sourceScopeReports;

    private IReadOnlyDictionary<string, string> ExecuteRemainingSourceScopes(
        CharacterCreationCompleteAssessment authority, CharacterCreationSelectedEffectsConsumer effects,
        CharacterCreationCompleteMagicExecution? magic,
        CharacterCreationResourcesService.CompleteFundingExecution? funding,
        CharacterCreationAttributeSkillsPreview actualSkills,
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles, CompletedSourcebooks800? sourcebooks,
        CharacterCreationCompleteProfileSettingsExecution? profileSettings,
        Q947? qualityLimit947, CharacterCreationQualitiesCompletionEvaluation? qualities,
        CharacterCreationCompleteContactsExecution? contacts)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        sourceScopeReports = [];
        executedSourceScopes955.Clear();
        if (!CanAttemptSourceScopes696(request)
            || Inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || Inputs.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest
            || effects.DigestFor(authority) is null) return result;
        try
        {
            var graph = effects.CopyCompiledGraph();
            var compiled = effects.CopySourceConsumptions();
            var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (magic?.DigestFor(authority) is { } magicDigest
                && MatchesMagicSelections(request.Selections.MagicSelections, compiled))
            {
                var corrected = new List<string>();
                foreach (var effect in compiled)
                {
                    if (!TryReplayCorrectedSource(effect, out string replay)) return result;
                    corrected.Add(replay);
                }
                foreach (string scope in new[] { "traditions", "streams", "powers", "spells", "complexforms" })
                    consumers.Add(scope, CharacterCreationFinalizationDigest.Compute(new
                    {
                        Invocation = magicDigest, CorrectedSourceExecutions = corrected
                    }));
            }

            // Empty baskets are explicitly executed by the existing canonical
            // consumers, with their original graph checked by the compiler.
            // Neither null inputs nor an absent consumer constitute inactivity.
            // Lifestyles are intentionally absent here: an empty purchase basket
            // can still activate Street starting cash during Finish. The
            // lifestyle purchase invocation does not execute that source rule.
            if (request.Selections.Loadout is not null && funding?.LoadoutEvaluationDigest is { } loadout
                && EmptyGraph(graph, "weapons") && EmptyGraph(graph, "armors"))
            {
                consumers.Add("weapons", loadout);
                consumers.Add("armor", loadout);
            }
            var invocation = CreationInvocation718(authority, graph, consumers, compiled,
                actualSkills, lifestyles, funding, sourcebooks, profileSettings, qualityLimit947, qualities, contacts);
            var reports = resolution.ExecuteRemainingSourceScopes(invocation);
            if (reports.Select(report => report.ObligationId).Distinct(StringComparer.Ordinal).Count() != reports.Count)
                return result;
            sourceScopeReports = Array.AsReadOnly(reports.Select(report => report with
            {
                Operations = Array.AsReadOnly(report.Operations.ToArray())
            }).ToArray());
            foreach (var report in sourceScopeReports)
            {
                var sources = Inputs.EffectivePolicy!.Sources.Where(row =>
                    row.Kind == CharacterCreationPolicySourceKind.Rules && row.SourceId == report.Scope).ToArray();
                if (sources is not [var source] || report.ObligationId != $"source:{source.Kind}:{source.Order}"
                    || source.ContentDigest != report.SourceDigest
                    || !invocation.TryGetCompletedConsumer(report.Scope, out var consumer)
                    || report.ConsumerDigest != consumer || !report.IsComplete
                    || report.Operations.Any(row => row.Classification886 is { } classification
                        && (request.RuleSourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                            || report.Scope != "metatypes" || classification.ObligationId != report.ObligationId
                            || classification.PolicyDigest != invocation.PolicyDigest || classification.CaptureDigest != invocation.CaptureDigest
                            || classification.RequestDigest != invocation.RequestDigest || classification.GraphDigest != invocation.GraphDigest
                            || invocation.PhysicalAuthority886 is not { } physical
                            || classification.AuthorityDigest != CharacterCreationFinalizationDigest.Compute(physical)
                            || physical.MetatypesInputsDigest != report.SourceDigest))
                    || result.ContainsKey(report.ObligationId)) continue;
                result.Add(report.ObligationId, CharacterCreationFinalizationDigest.Compute(new
                {
                    invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest,
                    invocation.GraphDigest, Report = report
                }));
                if (CanAttemptOtherSettings955(request))
                {
                    var originalReport = reports.Single(row => row.ObligationId == report.ObligationId);
                    if (CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(report, originalReport)
                        && resolution.HasExecutedSourceScope955(invocation, originalReport))
                        executedSourceScopes955.Add(report.ObligationId);
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or ArgumentException or OverflowException or XmlException) { result.Clear(); }
        return result;
    }

    private static bool EmptyGraph(XElement root, string container)
    {
        var matches = root.Elements(container).ToArray();
        return matches.Length <= 1 && matches.All(node => !node.HasElements && !node.HasAttributes
            && string.IsNullOrWhiteSpace(node.Value));
    }

    private static bool MatchesMagicSelections(CharacterCreationMagicResonanceSelections selections,
        IReadOnlyList<CharacterCreationCompiledSourceEffect> compiled)
    {
        var selected = selections.Spells.Concat(selections.ComplexForms)
            .Concat(selections.AdeptPowers.Select(row => row.Identity))
            .Concat(selections.Tradition is null ? [] : new[] { selections.Tradition })
            .Concat(selections.Stream is null ? [] : new[] { selections.Stream }).ToArray();
        if (selected.Distinct().Count() != selected.Length || compiled.Count != selected.Length) return false;
        return selected.All(selection => compiled.Count(row =>
            row.SourceId == selection.SourceId && row.Scope == Scope(selection.Kind)) == 1);
    }

    private static string? Scope(string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition => "traditions",
        CharacterCreationMagicResonanceKinds.Stream => "streams",
        CharacterCreationMagicResonanceKinds.AdeptPower => "powers",
        CharacterCreationMagicResonanceKinds.Spell => "spells",
        CharacterCreationMagicResonanceKinds.ComplexForm => "complexforms",
        _ => null
    };
}
