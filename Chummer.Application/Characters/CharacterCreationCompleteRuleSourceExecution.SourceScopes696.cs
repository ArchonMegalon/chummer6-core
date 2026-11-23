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
        CharacterCreationCompleteContactsExecution? contacts, CharacterCreationGearService.CompleteCostExecution? gear,
        out IReadOnlyDictionary<string, string> profileSettingExecutions1203)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        profileSettingExecutions1203 = new Dictionary<string, string>(StringComparer.Ordinal);
        sourceScopeReports = [];
        inactiveResourceSettings3185.Clear();
        inactiveRegroupingSettings3207.Clear();
        inactiveEconomicSettings3211.Clear();
        essenceSettingReports = [];
        executedSourceScopes955.Clear();
        if (!CanAttemptSourceScopes696(request)
            || Inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || Inputs.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest
            || effects.DigestFor(authority) is null) return result;
        try
        {
            using var currentProfileInputs8576 = CharacterCreationProfileInputRead8576.Begin();
            var graph = effects.CopyCompiledGraph();
            var compiled = effects.CopySourceConsumptions();
            var magicCompiled = compiled.Where(row => row.LoadoutLine is null).ToArray();
            var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (magic?.DigestFor(authority) is { } magicDigest
                && MatchesMagicSelections(request.Selections.MagicSelections, magicCompiled))
            {
                var corrected = new List<string>();
                foreach (var effect in magicCompiled)
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

            // V4 loadout coverage binds actual source compilation/replay to the
            // complete emitted owner set. Historical requests retain their
            // existing empty-basket path. Missing consumers prove nothing.
            // Lifestyles are intentionally absent here: an empty purchase basket
            // can still activate Street starting cash during Finish. The
            // lifestyle purchase invocation does not execute that source rule.
            if (request.Selections.Loadout is not null && funding?.LoadoutEvaluationDigest is { } loadout)
            {
                if (request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    && request.LifecycleResultSchema == CharacterCreationCompletionReviewSchemas.LifecycleResultV1)
                {
                    foreach (string scope in new[] { "weapons", "armor" })
                        if (TryCompiledLoadoutConsumer(scope, graph, compiled, loadout, out var execution))
                            consumers.Add(scope, execution);
                }
                else if (EmptyGraph(graph, "weapons") && EmptyGraph(graph, "armors"))
                {
                    consumers.Add("weapons", loadout);
                    consumers.Add("armor", loadout);
                }
            }
            var invocation = CreationInvocation718(authority, graph, consumers, compiled,
                actualSkills, lifestyles, funding, sourcebooks, profileSettings, qualityLimit947, qualities, contacts, effects);
            CharacterCreationCompletionCost3340.Log.Checkpoint(64);
            BindRetainedSr5Compatibility1241(invocation, authority, graph, actualSkills, magic);
            BindRetainedCompatibility1430(invocation, authority, graph, actualSkills, magic, effects);
            BindProfileKarma1208(invocation, authority, graph, magic);
            BindRetainedMagic1221(invocation, authority, graph, effects);
            BindFocusPricing1220(invocation, authority, effects, graph);
            BindEconomicPolicy3211(invocation, authority, graph, gear, lifestyles, funding, contacts);
            profileSettings?.BindMagic3200(invocation, authority, Inputs, request, magic);
            BindProfileInvocation3212(invocation, authority, graph);
            CharacterCreationCompletionCost3340.Log.Checkpoint(65);
            var reports = resolution.ExecuteRemainingSourceScopes(invocation);
            CharacterCreationCompletionCost3340.Log.Checkpoint(66);
            if (!reports.Any(report => report.Scope == "profile")) ResetProfileInvocation3212();
            retainedReports1221 = reports;
            essenceSettingReports = reports;
            focusReports1239 = reports;
            if (reports.Select(report => report.ObligationId).Distinct(StringComparer.Ordinal).Count() != reports.Count)
            {
                ResetProfileInvocation3212();
                return result;
            }
            sourceScopeReports = Array.AsReadOnly(reports.Select(report => report with
            {
                Operations = Array.AsReadOnly(report.Operations.ToArray())
            }).ToArray());
            karmaReports1208 = Array.AsReadOnly(reports.ToArray());
            karmaSourceReturned1208 = true;
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
            CharacterCreationCompletionCost3340.Log.Checkpoint(68);
            var profileSettingRows = CompleteProfileSettings1203(authority, invocation, reports);
            CharacterCreationCompletionCost3340.Log.Checkpoint(69);
            var resourceSettingRows = CompleteResourceSettings3185(authority, invocation, reports);
            CharacterCreationCompletionCost3340.Log.Checkpoint(70);
            var economicSettingRows = CompleteEconomicPolicy3211(authority, invocation, reports);
            CharacterCreationCompletionCost3340.Log.Checkpoint(71);
            profileSettingExecutions1203 = profileSettingRows
                .Concat(resourceSettingRows)
                .Concat(economicSettingRows)
                .Concat(CompleteQualitySettings3197(authority, invocation, reports))
                .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
            CharacterCreationCompletionCost3340.Log.Checkpoint(67);
            profileSettings?.CompleteMagic3200(authority, invocation, reports, resolution, Inputs.EffectivePolicy!);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or ArgumentException or OverflowException or XmlException) { ResetProfileInvocation3212(); result.Clear(); inactiveResourceSettings3185.Clear(); inactiveRegroupingSettings3207.Clear(); inactiveEconomicSettings3211.Clear(); }
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
        compiled = compiled.Where(row => row.LoadoutLine is null).ToArray();
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
