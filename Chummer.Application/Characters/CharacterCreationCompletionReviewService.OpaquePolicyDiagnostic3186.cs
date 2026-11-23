#if CHUMMER_PRIVATE_OPAQUE_POLICY3186
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionReviewService
{
    // Private synthetic-fixture observation only. Never include this symbol in
    // an application/release build. No execution or coverage is manufactured.
    private static void ObserveOpaquePolicy3186(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReview review, CharacterCreationCompleteRuleSourceExecution? execution)
    {
        if (inputs.EffectivePolicy is not { SettingObservations: { } observations } policy
            || policy.SettingsProfileId != CharacterCreationBootstrapProfiles.PrioritySettingsProfileId
            || review.CompleteAssessment is not { } assessment) return;

        var gaps = assessment.Obligations.Where(row => row.Coverage
            is not (CharacterCreationCompleteCoverage.Evaluated or CharacterCreationCompleteCoverage.ProvenInactive))
            .Where(row => row.Setting?.Key == CharacterCreationObservedSetting.Opaque
                || row.Kind == CharacterCreationCompleteObligationKind.Source
                    && row.SourceKind == CharacterCreationPolicySourceKind.Rules
                || row.Domain == CharacterCreationPolicyDomain.OtherSettings
                || row.Operation == CharacterCreationConsumerPolicyOperation.UnclassifiedSettings).ToArray();
        if (gaps.Length == 0) return;

        // Reproduce the frozen observation's leaf order, and label it only when
        // fresh canonical capture equals the actual retained observation.
        var fields = new Dictionary<string, object>(StringComparer.Ordinal);
        string? xml = inputs.Magic.Authority.MysticAdeptPowerPointPolicy?.CanonicalSourceXml;
        bool exactObservation = xml is not null
            && CharacterCreationSettingsObservation.TryCapture(xml, policy.ProfileInputsDigest,
                observations.Schema, out var fresh)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(observations, fresh);
        if (exactObservation)
        {
            var profile = XElement.Parse(xml!, LoadOptions.PreserveWhitespace);
            string[] represented = ["id", "name", "buildmethod", "books", "customdatadirectorynames",
                "maxskillratingcreate", "maxskillrating", "maxknowledgeskillratingcreate", "maxknowledgeskillrating"];
            var leaves = profile.Elements().Where(node => node.Name.NamespaceName.Length != 0
                    || !represented.Contains(node.Name.LocalName, StringComparer.Ordinal))
                .SelectMany(node => node.DescendantsAndSelf().Where(child => !child.HasElements)).ToArray();
            for (int index = 0; index < leaves.Length && index < observations.Settings.Count; index++)
            {
                var leaf = leaves[index];
                fields.Add(observations.Settings[index].SettingId,
                    new { Order = index, Name = leaf.Name.ToString(), Parent = leaf.Parent?.Name.ToString() });
            }
        }
        var opaqueDigests = gaps.Where(row => row.Setting?.SourceValueDigest is not null)
            .Select(row => row.Setting!.SourceValueDigest!).ToHashSet(StringComparer.Ordinal);
        var sources = (execution?.SourceScopeReports ?? []).Where(report => report.Scope == "profile"
            || gaps.Any(row => row.Kind == CharacterCreationCompleteObligationKind.Source && row.Id == report.ObligationId));
        bool RelevantOperation(CharacterCreationSourceOperation operation) =>
            operation.Disposition == CharacterCreationSourceOperationDisposition.Unconsumed
            || opaqueDigests.Contains(operation.SourceDigest);
        var payload = new
        {
            Schema = "private-opaque-policy-observation3186/v1", ExactObservation = exactObservation,
            assessment.InputsSnapshotDigest, assessment.RequestDigest, assessment.PolicyDigest,
            policy.SettingsProfileId, policy.ProfileInputsDigest, ObservationSchema = observations.Schema,
            Gaps = gaps.Select(row => new
            {
                row.Id, Kind = row.Kind.ToString(), Evaluability = row.Evaluability.ToString(),
                Coverage = row.Coverage.ToString(), Reason = row.Reason.ToString(),
                ConsumptionReason = row.ConsumptionReason?.ToString(), row.AuthorityDigest, row.Setting,
                Field = row.Setting is { } setting && fields.TryGetValue(setting.SettingId, out var field) ? field : null,
                Source = policy.Sources.SingleOrDefault(source => source.Kind == row.SourceKind && source.Order == row.SourceOrder)
            }).ToArray(),
            Reports = sources.Select(report => new
            {
                report.ObligationId, report.Scope, report.SourceDigest, report.ConsumerDigest,
                report.IsComplete, OperationCount = report.Operations.Count,
                OmittedOperationCount = Math.Max(0, report.Operations.Count(RelevantOperation) - 512),
                Operations = report.Operations.Where(RelevantOperation).Take(512).Select(op => new
                    {
                        op.Order, op.Path, op.SourceDigest, Disposition = op.Disposition.ToString(),
                        op.ExecutionDigest, HasOperationResult = op.OperationResult718 is not null,
                        HasConsumedValue = op.ConsumedValue718 is not null
                    }).ToArray()
            }).ToArray()
        };
        System.Console.Error.WriteLine("OPAQUE_POLICY3186 " + JsonSerializer.Serialize(payload));
    }
}
#endif
