using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private CharacterCreationRuleSourceScopeInvocation? metadataInvocation1209;
    private string? metadataAuthority1209;

    // Called only from CreationInvocation718, after original-aware compilation.
    private void BindProfileMetadata1209(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, XElement graph)
    {
        metadataInvocation1209 = null;
        metadataAuthority1209 = null;
        if (!CanAttemptMetadata1209(request) || Inputs.EffectivePolicy is not { } policy
            || authority.RequestDigest != requestDigest || authority.InputsSnapshotDigest != Inputs.SnapshotDigest
            || authority.PolicyDigest != policy.PolicyDigest
            || Inputs.RuleSourceCaptureDigest != invocation.CaptureDigest
            || Inputs.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest
            || Inputs.OriginalCharacter is not { } originalInput
            || invocation.WorkspaceId718 != originalInput.WorkspaceId
            || !original.MatchesInputs(Inputs.SnapshotDigest)
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy,
                Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, CharacterCreationPolicyDomain.Profile)) return;
        string xml = graph.ToString(SaveOptions.DisableFormatting);
        if (invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(xml)) return;
        var source = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        CharacterCreationRuleSourceScopeInvocation.BindMetadata1209(invocation, xml, request,
            policy.SettingsProfileId, policy.ProfileInputsDigest,
            CharacterCreationFinalizationDigest.Compute(source.CanonicalSourceXml));
        metadataInvocation1209 = invocation;
        metadataAuthority1209 = CharacterCreationFinalizationDigest.Compute(authority);
    }

    internal static bool CanAttemptMetadata1209(CharacterCreationCompletionReviewRequest request)
        => request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            && CanAttemptSourceScopes696(request) && CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request);

    internal static bool SupportsMetadata1209(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation setting,
        CharacterCreationSettingConsumer? consumer)
        => MetadataField1209(policy, source, setting, consumer) is "gameplayoptionname" or "nuyenformat";

    internal bool CanExecuteMetadata1209(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation setting,
        CharacterCreationSettingConsumer? consumer, CharacterCreationCompletionReviewRequest proposed)
        // Authority visits every setting. Reject other settings before hashing
        // the full request or comparing catalogs; supported leaves retain every binding.
        => CanAttemptMetadata1209(proposed) && SupportsMetadata1209(policy, source, setting, consumer)
            && requestDigest == CharacterCreationFinalizationDigest.Compute(proposed)
            && original.MatchesInputs(Inputs.SnapshotDigest) && proposed.InputsSnapshotDigest == Inputs.SnapshotDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy with { SelectedConsumption = null }, Inputs.EffectivePolicy)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(source, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy);

    // Use the ORIGINAL observation sequence from the canonical capture. No new
    // key, row, consumer, order or value digest is projected for an opaque leaf.
    private static string? MetadataField1209(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation setting,
        CharacterCreationSettingConsumer? consumer)
    {
        if (consumer != CharacterCreationSettingConsumer.None
            || setting.Key != CharacterCreationObservedSetting.Opaque
            || setting.State != CharacterCreationSettingObservationState.Opaque || setting.ValueDigest is not null
            || policy.SettingObservations is not { } observed
            || !TryReadProfileSettingCandidate3320(source, out var profile)) return null;
        foreach (string field in new[] { "gameplayoptionname", "nuyenformat", "essenceformat" })
        {
            var leaves = profile!.Elements().Where(row => row.Name.LocalName == field).ToArray();
            if (leaves is [var leaf] && leaf.Name == field && !leaf.HasAttributes
                && leaf.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText)
                && !string.IsNullOrWhiteSpace(text.Value)
                && CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting))
                    == setting.SourceValueDigest)
            {
                // Candidate parsing grants no authority. Matching leaves still
                // validate the current source/domain and the complete fresh inventory.
                return CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
                    && CharacterCreationSettingsObservation.TryCapture(source!.CanonicalSourceXml, policy.ProfileInputsDigest,
                        observed.Schema, out var fresh)
                    && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(observed, fresh)
                    && observed.Settings.Count(row => row == setting) == 1 ? field : null;
            }
        }
        return null;
    }

    // Negative classification only: avoid full source-policy reconstruction for
    // unrelated leaves. Parsing must remain bounded and DTD-free before admission.
    // Every matching caller still runs SupportsDomain and fresh inventory checks.
    private static bool TryReadProfileSettingCandidate3320(CharacterCreationMysticAdeptPowerPointPolicy? source,
        out XElement? profile)
    {
        profile = null;
        if (source is null || string.IsNullOrWhiteSpace(source.CanonicalSourceXml)
            || source.CanonicalSourceXml.Length > 262144) return false;
        try
        {
            using var input = new StringReader(source.CanonicalSourceXml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
            profile = XElement.Load(reader, LoadOptions.PreserveWhitespace);
            return profile.Name == "setting";
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    private void CompleteProfileMetadata1209(CharacterCreationCompleteAssessment authority,
        IDictionary<string, string> completed)
    {
        if (metadataInvocation1209 is not { } invocation
            || metadataAuthority1209 != CharacterCreationFinalizationDigest.Compute(authority)
            || !CanAttemptMetadata1209(request)) return;
        foreach (var row in authority.Obligations)
        {
            if (row.Kind != CharacterCreationCompleteObligationKind.Setting || row.Setting is null
                || row.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent) continue;
            string? field = MetadataField1209(Inputs.EffectivePolicy!, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
                row.Setting, row.Consumer);
            // Essence has no Complete consumer for source-bound special-attribute
            // loss/minimum validation yet. Do not admit it from purchase rounding.
            if (field is not ("gameplayoptionname" or "nuyenformat")
                || !resolution.TryGetExecutedProfileMetadata1209(invocation, field, out var execution)
                || !CharacterCreationFinalizationDigest.IsCanonical(execution)) continue;
            completed.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                Authority = metadataAuthority1209, row.Id, row.AuthorityDigest, row.Setting,
                authority.SettingsObservationDigest, Inputs.RuleSourceCaptureDigest,
                authority.RequestDigest, invocation.GraphDigest, Execution = execution
            }));
        }
    }
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    private sealed record MetadataCandidate1209(string Binding, string Graph, string Request,
        string ProfileId, string ProfileDigest, string ProfileXmlDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, MetadataCandidate1209>
        metadataCandidates1209 = new();

    internal static void BindMetadata1209(CharacterCreationRuleSourceScopeInvocation invocation, string graph,
        CharacterCreationCompletionReviewRequest request, string profileId, string profileDigest, string profileXmlDigest)
    {
        if (invocation.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(graph)) return;
        metadataCandidates1209.Add(invocation, new(MetadataBinding1209(invocation), graph,
            JsonSerializer.Serialize(request), profileId, profileDigest, profileXmlDigest));
    }

    // Detached inputs, never a registration API or a transferable execution.
    public bool TryGetProfileMetadataCandidate1209(string profileId, string profileDigest, string profileXmlDigest,
        out string? graph, out CharacterCreationCompletionReviewRequest? request)
    {
        graph = null; request = null;
        if (!metadataCandidates1209.TryGetValue(this, out var bound)
            || bound.Binding != MetadataBinding1209(this) || bound.ProfileId != profileId
            || bound.ProfileDigest != profileDigest || bound.ProfileXmlDigest != profileXmlDigest) return false;
        graph = bound.Graph;
        request = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(bound.Request);
        return request is not null && CharacterCreationFinalizationDigest.Compute(request) == RequestDigest;
    }

    private static string MetadataBinding1209(CharacterCreationRuleSourceScopeInvocation value)
    {
        value.TryGetCompletedConsumer("profile", out var consumer);
        return CharacterCreationFinalizationDigest.Compute(new
        {
            value.PolicyDigest, value.CaptureDigest, value.RequestDigest, value.GraphDigest, Consumer = consumer,
            value.CreationDraft718, value.WorkspaceId718, value.Attributes718, value.AttributePolicy718,
            value.Skills718, value.CompiledEffects, value.ResourcesExecution718, value.Lifestyles718,
            value.LifestyleAuthority718, value.StartingLifestyleId718, value.SourceExecution832,
            value.PhysicalAuthority886, value.PhysicalInitiative886
        });
    }
}
