using System.Xml.Linq;
using Chummer.Contracts.Characters;
using D3185 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private readonly HashSet<string> inactiveResourceSettings3185 = new(StringComparer.Ordinal);
    internal IReadOnlySet<string> InactiveResourceSettings3185 => inactiveResourceSettings3185;

    // Funding and the retained resolver must both have executed on this exact
    // private invocation. Partial profile coverage can prove these leaves only;
    // it never discharges the profile source or OtherSettings aggregate.
    private IReadOnlyDictionary<string, string> CompleteResourceSettings3185(
        CharacterCreationCompleteAssessment authority, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationRuleSourceScopeResult> reports)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!MatchesProfileSettingInputs1203() || authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || authority.InputsSnapshotDigest != Inputs.SnapshotDigest || authority.RequestDigest != requestDigest
            || authority.PolicyDigest != Inputs.EffectivePolicy!.PolicyDigest
            || authority.SettingsObservationDigest != CharacterCreationFinalizationDigest.Compute(Inputs.EffectivePolicy.SettingObservations)
            || !invocation.TryGetCompletedConsumer("profile", out var consumer)) return result;
        var sources = Inputs.EffectivePolicy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
            && row.SourceId == "profile").ToArray();
        var profiles = reports.Where(row => row.Scope == "profile").ToArray();
        if (sources is not [var source] || profiles is not [var report]
            || report.ObligationId != $"source:{source.Kind}:{source.Order}" || report.SourceDigest != source.ContentDigest
            || report.ConsumerDigest != consumer || report.Operations.Count == 0
            || report.Operations[0].Disposition != D3185.Structural
            || !report.Operations.Select(row => row.Order).SequenceEqual(Enumerable.Range(0, report.Operations.Count))
            || report.Operations.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() != report.Operations.Count)
            return result;
        var profile = XElement.Parse(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml,
            LoadOptions.PreserveWhitespace);
        string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        var parents = report.Operations.Where(row => row.SourceDigest == profileDigest && row.Disposition == D3185.Structural).ToArray();
        if (parents is not [var parent]) return result;
        var resources = Inputs.Lifestyles.Gear.Resources.Authority;
        foreach (var row in authority.Obligations)
        {
            if (row.Kind != CharacterCreationCompleteObligationKind.Setting
                || row.Consumer != CharacterCreationSettingConsumer.Resources
                || row.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent
                || row.Setting is not { State: CharacterCreationSettingObservationState.Present } setting
                || Inputs.EffectivePolicy.SettingObservations!.Settings.Count(item => item == setting) != 1) continue;
            string? field = setting.Key switch
            {
                CharacterCreationObservedSetting.ResourceExpression => "chargenkarmatonuyenexpression",
                CharacterCreationObservedSetting.ResourceKarmaMaximum => "nuyenmaxbp",
                CharacterCreationObservedSetting.ResourceKarmaRate => "nuyenperbpwftm",
                CharacterCreationObservedSetting.UnrestrictedNuyen => "unrestrictednuyen",
                _ => null
            };
            if (field is null || profile.Elements(field).ToArray() is not [var leaf]
                || !CharacterCreationResourcesService.CompleteFundingExecution.TryReadSettingSource3185(
                    invocation, authority, resources, profileDigest, field, setting.SourceValueDigest!,
                    out string? fundingResult, out string? fundingDigest, out bool inactive)) continue;
            string path = parent.Path + "/" + field + "["
                + profile.Nodes().TakeWhile(node => !ReferenceEquals(node, leaf)).Count() + "]";
            var operations = report.Operations.Where(operation => operation.Path == path
                || operation.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
            if (operations.Length == 0 || operations[0].Path != path
                || operations[0].SourceDigest != setting.SourceValueDigest
                || operations.Any(operation => operation.Classification886 is not null
                    || operation.Disposition != (inactive ? D3185.NotSelected : D3185.Compiled)
                    || !CharacterCreationFinalizationDigest.IsCanonical(operation.ExecutionDigest)
                    || operation.OperationResult718 is null)
                || !resolution.HasExecutedProfileSetting1203(invocation, report, operations[0].Order)) continue;
            result.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "canonical-resource-setting-coverage3185", Authority = CharacterCreationFinalizationDigest.Compute(authority),
                row.Id, row.AuthorityDigest, Observation = setting, row.Consumer, Source = source,
                Profile = profileDigest, Funding = fundingDigest, Result = fundingResult, Inactive = inactive,
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                report.ObligationId, report.SourceDigest, report.ConsumerDigest, Operations = operations
            }));
            if (inactive) inactiveResourceSettings3185.Add(row.Id);
        }
        return result;
    }
}
