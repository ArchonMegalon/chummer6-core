using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Called only after CompleteProfileSettings1203 verifies the complete input,
    // authority, owner/revision, request, source report and original invocation.
    private void CompleteAttributeKarmaSetting3201(CharacterCreationCompleteAssessment authority,
        CharacterCreationRuleSourceScopeInvocation invocation, CharacterCreationRuleSourceScopeResult report,
        IDictionary<string, string> evaluations)
    {
        var policy = Inputs.EffectivePolicy!;
        var profile = XElement.Parse(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml,
            LoadOptions.PreserveWhitespace);
        var leaf = PlainProfileLeaf1203(profile, "karmaattribute", "karmacost");
        if (leaf is null || !invocation.TryGetCompletedConsumer("profile", out var consumer)) return;
        string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        string leafDigest = CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting));
        if (!invocation.TryGetExecutedProfileAttributeKarma3201(policy.SettingsProfileId, policy.ProfileInputsDigest,
                profileDigest, leafDigest, out int value, out var attributes, out var execution)
            || attributes is null || !ScalarMatches1203(leaf, value)
            || authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Setting
                && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent
                && row.Consumer == CharacterCreationSettingConsumer.Attributes
                && row.Setting is { Key: CharacterCreationObservedSetting.AttributeKarma,
                    State: CharacterCreationSettingObservationState.Present }
                && row.Setting.SourceValueDigest == leafDigest).ToArray() is not [var obligation]
            || policy.SettingObservations!.Settings.Count(row => row == obligation.Setting) != 1) return;
        var definition = CharacterCreationSettingDefinitions.ForSchema(policy.SettingObservations.Schema)!
            .Single(row => row.Key == CharacterCreationObservedSetting.AttributeKarma);
        if (obligation.Setting!.ValueDigest != CharacterCreationSettingDefinitions.ValueDigest(definition.Kind,
                value.ToString(CultureInfo.InvariantCulture))
            || report.Operations.Where(row => row.SourceDigest == profileDigest
                && row.Disposition == CharacterCreationSourceOperationDisposition.Structural).ToArray() is not [var parent]) return;
        string path = parent.Path + RelativePath1203(profile, leaf);
        var operations = report.Operations.Where(row => row.Path == path
            || row.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
        if (operations is not [var element, var text] || element.Path != path || element.SourceDigest != leafDigest
            || text.Path != path + "/text()[0]" || text.Order != element.Order + 1) return;
        var output = new { Operation = "canonical-attribute-Karma-price", Field = "karmaattribute",
            Value = value, Result = attributes, Execution = execution };
        string expected = CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            Consumer = consumer, Disposition = CharacterCreationSourceOperationDisposition.Compiled, Result = output
        });
        if (operations.Any(row => row.Classification886 is not null
                || row.Disposition != CharacterCreationSourceOperationDisposition.Compiled || row.ExecutionDigest != expected)
            || !resolution.HasExecutedProfileSetting1203(invocation, report, element.Order)) return;
        evaluations.Add(obligation.Id, CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "private-attribute-Karma-setting-bridge3201", Authority = CharacterCreationFinalizationDigest.Compute(authority),
            obligation.Id, obligation.AuthorityDigest, Observation = obligation.Setting, obligation.Consumer,
            Inputs.SnapshotDigest, Inputs.OriginalCharacter!.WorkspaceId, Inputs.OriginalCharacter.ContentRevision,
            Inputs.OriginalCharacter.SavedRevision, authority.RawCharacterXmlDigest,
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            report.ObligationId, report.SourceDigest, report.ConsumerDigest, Execution = execution,
            Operations = operations.Select(row => new { Operation = row, row.ConsumedValue718, row.OperationResult718 }).ToArray()
        }));
    }
}
