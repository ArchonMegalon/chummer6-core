using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private IReadOnlyList<CharacterCreationRuleSourceScopeResult> essenceSettingReports = [];

    private static bool IsEssenceControlSetting(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation setting,
        CharacterCreationSettingConsumer? consumer)
    {
        if (consumer != CharacterCreationSettingConsumer.None || setting.Key != CharacterCreationObservedSetting.Opaque
            || setting.State != CharacterCreationSettingObservationState.Opaque || setting.ValueDigest is not null
            || policy.SettingObservations is not { } observations
            || !TryReadProfileSettingCandidate3320(source, out var profile)) return false;
        if (!CharacterCreationEssenceSettingDefinitions.TryRead(profile!, out var typed)
            || typed!.Settings.Count(row => row.SourceValueDigest == setting.SourceValueDigest) != 1) return false;
        // Typed candidate matching is not source/domain or inventory admission.
        return CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
            && CharacterCreationSettingsObservation.TryCapture(source!.CanonicalSourceXml, policy.ProfileInputsDigest,
                observations.Schema, out var fresh)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(observations, fresh)
            && observations.Settings.Count(row => row == setting) == 1;
    }

    private void CompleteEssenceSettings(CharacterCreationCompleteAssessment authority, IDictionary<string, string> completed)
    {
        if (essenceScope1222 is not { } invocation || !TryGetEssenceExecution1222(invocation, out var execution)
            || authority.RequestDigest != requestDigest || authority.InputsSnapshotDigest != Inputs.SnapshotDigest
            || authority.PolicyDigest != Inputs.EffectivePolicy?.PolicyDigest
            || !invocation.TryGetCompletedConsumer("profile", out var consumer)) return;
        var profile = XElement.Parse(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml,
            LoadOptions.PreserveWhitespace);
        if (!CharacterCreationEssenceSettingEvidenceRules.Matches(profile, execution!, out var evidence)) return;
        var sources = Inputs.EffectivePolicy!.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
            && row.SourceId == "profile").ToArray();
        var reports = essenceSettingReports.Where(row => row.Scope == "profile").ToArray();
        if (sources is not [var source] || reports is not [var report]
            || report.ObligationId != $"source:{source.Kind}:{source.Order}" || report.SourceDigest != source.ContentDigest
            || report.ConsumerDigest != consumer) return;
        var parents = report.Operations.Where(row => row.SourceDigest == evidence!.Policy.ProfileXmlDigest
            && row.Disposition == CharacterCreationSourceOperationDisposition.Structural).ToArray();
        if (parents is not [var parent]) return;
        foreach (var row in authority.Obligations)
        {
            if (row.Kind != CharacterCreationCompleteObligationKind.Setting || row.Setting is null
                || row.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent
                || !evidence!.Settings.Any(item => item.Setting.SourceValueDigest == row.Setting.SourceValueDigest)
                || !IsEssenceControlSetting(Inputs.EffectivePolicy, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
                    row.Setting, row.Consumer)) continue;
            var consumed = evidence!.Settings.Single(item => item.Setting.SourceValueDigest == row.Setting.SourceValueDigest);
            string field = CharacterCreationEssenceSettingDefinitions.All.Single(item => item.Control == consumed.Setting.Control).Element;
            string path = parent.Path + RelativePath1203(profile, profile.Element(field)!);
            var operations = report.Operations.Where(item => item.Path == path).ToArray();
            if (operations is not [var operation] || operation.SourceDigest != consumed.Setting.SourceValueDigest
                || operation.Disposition != CharacterCreationSourceOperationDisposition.Compiled
                || operation.Classification886 is not null || operation.OperationResult718 is null
                || !resolution.HasExecutedProfileSetting1203(invocation, report, operation.Order)
                || !MatchesEssenceSettingOperation(operation.OperationResult718, execution!, consumed)) continue;
            completed.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "private-Essence-setting", Authority = CharacterCreationFinalizationDigest.Compute(authority),
                row.Id, row.AuthorityDigest, row.Setting, authority.SettingsObservationDigest,
                invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest, execution!.ExecutionDigest,
                consumed, Source = source, report.ConsumerDigest, SourceOperation = operation, operation.OperationResult718
            }));
        }
    }

    private static bool MatchesEssenceSettingOperation(string json, CharacterCreationEssenceExecution1222 execution,
        CharacterCreationEssenceSettingConsumption consumed)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.GetProperty("Operation").GetString() == "Finish-character-Essence-setting"
                && root.GetProperty("ExecutionDigest").GetString() == execution.ExecutionDigest
                && root.GetProperty("SourceDigest").GetString() == execution.SourceDigest
                && root.GetProperty("GraphDigest").GetString() == execution.GraphDigest
                && root.GetProperty("Consumption").Deserialize<CharacterCreationEssenceSettingConsumption>() == consumed;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }
}
