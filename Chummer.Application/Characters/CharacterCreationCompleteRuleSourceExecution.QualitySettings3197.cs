using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Q947 = Chummer.Application.Characters.CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947;
using D3197 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;
using S3197 = Chummer.Contracts.Characters.CharacterCreationObservedSetting;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Typed quality observations already have candidate-dependent obligations.
    // Join only their completed canonical evaluator and exact private source-leaf
    // receipts. Partial profile execution never covers its unresolved siblings.
    private IReadOnlyDictionary<string, string> CompleteQualitySettings3197(
        CharacterCreationCompleteAssessment authority, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationRuleSourceScopeResult> reports)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!MatchesProfileSettingInputs1203() || authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || authority.InputsSnapshotDigest != Inputs.SnapshotDigest || authority.RequestDigest != requestDigest
            || authority.RawCharacterXmlDigest != Inputs.Skills.Binding.RawCharacterXmlDigest
            || authority.SelectionsDigest != CharacterCreationFinalizationDigest.Compute(request.Selections)
            || authority.PolicyDigest != Inputs.EffectivePolicy!.PolicyDigest
            || authority.SettingsObservationDigest != CharacterCreationFinalizationDigest.Compute(Inputs.EffectivePolicy.SettingObservations)
            || !Q947.MatchesAuthority3197(invocation, authority)
            || !invocation.TryGetCompletedConsumer("profile", out var consumer)) return result;
        var sources = Inputs.EffectivePolicy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
            && row.SourceId == "profile").ToArray();
        var profiles = reports.Where(row => row.Scope == "profile").ToArray();
        if (sources is not [var source] || profiles is not [var report]
            || report.ObligationId != $"source:{source.Kind}:{source.Order}" || report.SourceDigest != source.ContentDigest
            || report.ConsumerDigest != consumer || report.Operations.Count == 0
            || report.Operations[0].Disposition != D3197.Structural
            || !report.Operations.Select(row => row.Order).SequenceEqual(Enumerable.Range(0, report.Operations.Count))
            || report.Operations.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() != report.Operations.Count)
            return result;
        var profile = XElement.Parse(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml,
            LoadOptions.PreserveWhitespace);
        string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        var parents = report.Operations.Where(row => row.SourceDigest == profileDigest
            && row.Disposition == D3197.Structural).ToArray();
        if (parents is not [var parent]) return result;
        foreach (var row in authority.Obligations)
        {
            if (row.Kind != CharacterCreationCompleteObligationKind.Setting
                || row.Consumer != CharacterCreationSettingConsumer.Qualities
                || row.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent
                || row.Setting is not { State: CharacterCreationSettingObservationState.Present } setting
                || Inputs.EffectivePolicy.SettingObservations!.Settings.Count(item => item == setting) != 1) continue;
            string? field = setting.Key switch
            {
                S3197.QualityKarma => "karmaquality",
                S3197.QualityLimit => "qualitykarmalimit",
                S3197.ExceedPositiveQualities => "exceedpositivequalities",
                S3197.ExceedNegativeQualities => "exceednegativequalities",
                _ => null
            };
            if (field is null || PlainProfileLeaf1203(profile, field,
                    setting.Key == S3197.QualityKarma ? "karmacost" : null) is not { } leaf
                || setting.SourceValueDigest != CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting))
                || !TryReadQualitySettingResult3197(invocation, Inputs.EffectivePolicy, profileDigest, setting,
                    field, out string? canonicalResult)) continue;
            string path = parent.Path + RelativePath1203(profile, leaf);
            var operations = report.Operations.Where(operation => operation.Path == path
                || operation.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
            if (operations.Length == 0 || operations[0].Path != path
                || operations[0].SourceDigest != setting.SourceValueDigest
                || operations.Any(operation => operation.Classification886 is not null
                    || operation.Disposition != D3197.Compiled
                    || !CharacterCreationFinalizationDigest.IsCanonical(operation.ExecutionDigest)
                    || operation.OperationResult718 != canonicalResult)
                || !resolution.HasExecutedProfileSetting1203(invocation, report, operations[0].Order)) continue;
            result.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "canonical-quality-setting-coverage3197", Authority = CharacterCreationFinalizationDigest.Compute(authority),
                row.Id, row.AuthorityDigest, Observation = setting, row.Consumer, Source = source, Profile = profileDigest,
                Inputs.SnapshotDigest, Inputs.OriginalCharacter!.WorkspaceId, Inputs.OriginalCharacter.ContentRevision,
                Inputs.OriginalCharacter.SavedRevision, authority.RawCharacterXmlDigest,
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                report.ObligationId, report.SourceDigest, report.ConsumerDigest, Operations = operations
            }));
            // EvaluateSelectionCore calls TryCalculate and emits both budgets even
            // for an empty inventory. These are Evaluated, never ProvenInactive.
        }
        return result;
    }

    private static bool TryReadQualitySettingResult3197(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationEffectivePolicyContext policy, string profileDigest, CharacterCreationSettingObservation setting,
        string field, out string? canonicalResult)
    {
        canonicalResult = null;
        string id = policy.SettingsProfileId, inputs = policy.ProfileInputsDigest, leaf = setting.SourceValueDigest!;
        if (setting.Key == S3197.QualityLimit
            && Q947.TryRead947(invocation, id, inputs, profileDigest, leaf, out var limitResult, out bool limitEmpty, out var limitExecution)
            && limitResult is not null)
            canonicalResult = JsonSerializer.Serialize(new
            {
                Operation = "canonical-quality-karma-limit", Limit = limitResult.PositiveQualityBudget.Total,
                Result = limitResult, NoTarget = limitEmpty, Execution = limitExecution
            });
        else if (setting.Key == S3197.QualityKarma
            && Q947.TryReadCost973(invocation, id, inputs, profileDigest, leaf, out var costResult, out var cost,
                out bool costEmpty, out var costExecution) && costResult is not null && cost is not null)
            canonicalResult = JsonSerializer.Serialize(new
            {
                Operation = "canonical-quality-karma-cost", CostPolicy = cost, Result = costResult,
                NoTarget = costEmpty, Execution = costExecution
            });
        else if (setting.Key is S3197.ExceedPositiveQualities or S3197.ExceedNegativeQualities
            && Q947.TryReadQualityPolicy1004(invocation, id, inputs, profileDigest, field, leaf, out var policyResult,
                out var costPolicy, out bool value, out bool policyEmpty, out var policyExecution)
            && policyResult is not null && costPolicy is not null)
            canonicalResult = JsonSerializer.Serialize(new
            {
                Operation = "canonical-quality-limit-policy", Field = field, Value = value,
                CostPolicy = costPolicy, Result = policyResult, NoTarget = policyEmpty, Execution = policyExecution
            });
        return canonicalResult is not null;
    }
}
