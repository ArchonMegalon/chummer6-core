using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using S3211 = Chummer.Contracts.Characters.CharacterCreationObservedSetting;
using C3211 = Chummer.Contracts.Characters.CharacterCreationSettingConsumer;
using D3211 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private sealed record EconomicUse3211(CharacterCreationSettingObservation Setting, C3211 Consumer,
        string Path, object SourceAuthority, string Result, bool Inactive);
    private sealed record BoundEconomic3211(string InvocationDigest, string AuthorityDigest,
        string ProfileXmlDigest, string ProfileId, string ProfileInputsDigest, IReadOnlyList<EconomicUse3211> Uses);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundEconomic3211>
        economicInvocations3211 = new();
    private readonly HashSet<string> inactiveEconomicSettings3211 = new(StringComparer.Ordinal);
    internal IReadOnlySet<string> InactiveEconomicSettings3211 => inactiveEconomicSettings3211;

    private static string EconomicInvocationDigest3211(CharacterCreationRuleSourceScopeInvocation invocation)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            Basis = LoadoutInvocationDigest1215(invocation), invocation.LifestyleAuthority718,
            invocation.Lifestyles718, invocation.StartingLifestyleId718
        });

    private void BindEconomicPolicy3211(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, XElement graph,
        CharacterCreationGearService.CompleteCostExecution? gear,
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles,
        CharacterCreationResourcesService.CompleteFundingExecution? funding,
        CharacterCreationCompleteContactsExecution? contacts)
    {
        economicInvocations3211.Remove(invocation);
        if (!MatchesProfileSettingInputs1203() || authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || authority.InputsSnapshotDigest != Inputs.SnapshotDigest || authority.RequestDigest != requestDigest
            || authority.PolicyDigest != Inputs.EffectivePolicy!.PolicyDigest
            || authority.RawCharacterXmlDigest != Inputs.Skills.Binding.RawCharacterXmlDigest
            || authority.SelectionsDigest != CharacterCreationFinalizationDigest.Compute(request.Selections)
            || authority.SettingsObservationDigest != CharacterCreationFinalizationDigest.Compute(Inputs.EffectivePolicy.SettingObservations)
            || invocation.PolicyDigest != authority.PolicyDigest || invocation.RequestDigest != requestDigest
            || invocation.CaptureDigest != Inputs.RuleSourceCaptureDigest
            || invocation.WorkspaceId718 != Inputs.OriginalCharacter!.WorkspaceId
            || invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting))
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(invocation.CreationDraft718, Inputs.Skills.PrerequisiteDraft)
            || !invocation.TryGetCompletedConsumer("profile", out _)) return;
        var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var context = (scope ?? resolution.Resolver).TryCreateContext(Inputs.OriginalCharacter!.RawCharacterXml);
        if (context is null) return;
        var policy = Inputs.EffectivePolicy!;
        var settings = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        var profile = XElement.Parse(settings.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        var uses = new List<EconomicUse3211>();

        if (contacts is not null && context.TryResolveCreationContactsPolicy(out var contactSource)
            && contactSource is not null && contactSource.SettingsProfileId == policy.SettingsProfileId
            && contactSource.RawProfileInputsDigest == policy.ProfileInputsDigest
            && contacts.TryReadEconomic3211(authority, contactSource, out var contactResult, out bool noPaidGroup))
            Add(S3211.GroupContactKarma, C3211.Contacts, "karmacontact", "karmacost",
                contactSource.GroupContactKarmaMultiplier.ToString(CultureInfo.InvariantCulture),
                contactSource, contactResult!, noPaidGroup);

        if (lifestyles is not null && context.TryResolveCreationLifestylesAuthority(out var lifestyleSource)
            && CharacterCreationSelectedPolicyProjection.FreeGridsMatchesSource(policy, lifestyleSource, settings)
            && lifestyles.TryReadEconomic3211(authority, lifestyleSource, out var lifestyleResult))
            Add(S3211.FreeGrids, C3211.Lifestyles, "allowfreegrids", null,
                lifestyleSource.FreeGridsActivation!.RawSettingValue, lifestyleSource, lifestyleResult!, false);

        if (gear is not null && request.Selections.Gear is { } basket
            && context.TryResolveCreationGearAuthority(out var gearSource)
            && gearSource.SettingsProfileId == policy.SettingsProfileId && gearSource.ProfileDigest == policy.ProfileInputsDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(gearSource, Inputs.Lifestyles.Gear.State.Authority)
            && CharacterCreationGearRules.TryProjectBasket(basket, gearSource, gear.EvaluatedStartingNuyen,
                out var lines, out var budget, out var blockers) && blockers.Length == 0
            && gear.MatchesSourceResult(basket, lines, budget, authority))
            Add(S3211.MaximumAvailability, C3211.Gear, "availability", null,
                gearSource.MaximumAvailability.ToString(CultureInfo.InvariantCulture), gearSource,
                JsonSerializer.Serialize(new { Operation = "canonical-gear-availability", gear.EvaluationDigest, Lines = lines, Budget = budget }), false);

        if (funding?.LoadoutEvaluationDigest is { } loadoutDigest
            && funding.TryGetCarryoverBudget995(authority, out _, out _, out _)
            && TryLoadoutBasis1215(context, out var loadoutSource, out var legality, out var loadout)
            && CreationLifecycle1215(XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml, LoadOptions.PreserveWhitespace))
            && CreationLifecycle1215(graph))
            Add(S3211.MaximumAvailability, C3211.Loadout, "availability", null,
                loadoutSource!.MaximumAvailability.ToString(CultureInfo.InvariantCulture), loadoutSource,
                JsonSerializer.Serialize(new { Operation = "canonical-loadout-availability", loadoutDigest, Legality = legality, Result = loadout }), false);

        if (uses.Count != 0)
            economicInvocations3211.Add(invocation, new(EconomicInvocationDigest3211(invocation),
                CharacterCreationFinalizationDigest.Compute(authority),
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                policy.SettingsProfileId, policy.ProfileInputsDigest, uses.ToArray()));

        void Add(S3211 key, C3211 consumer, string field, string? container, string? value,
            object sourceAuthority, string result, bool inactive)
        {
            // Match identity and exact path, not just coincident integer/boolean values.
            if (value is null || PlainProfileLeaf1203(profile, field, container) is not { } leaf
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)) return;
            var observations = policy.SettingObservations!.Settings.Where(row => row.Key == key).ToArray();
            var definition = CharacterCreationSettingDefinitions.ForSchema(policy.SettingObservations.Schema)!
                .SingleOrDefault(row => row.Key == key);
            if (observations is not [var observation] || definition is null
                || observation.State != CharacterCreationSettingObservationState.Present
                || observation.SourceValueDigest != CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting))
                || observation.ValueDigest != CharacterCreationSettingDefinitions.ValueDigest(definition.Kind, value)) return;
            uses.Add(new(observation, consumer, RelativePath1203(profile, leaf), sourceAuthority, result, inactive));
        }
    }

    // Called by the retained source resolver with its independently resolved typed
    // authority. Public invocation properties and diagnostic JSON cannot bind a use.
    internal static bool TryReadEconomicPolicy3211(CharacterCreationRuleSourceScopeInvocation invocation,
        S3211 key, C3211 consumer, string profileId, string profileInputsDigest, string profileXmlDigest,
        string leafXmlDigest, object sourceAuthority, out string? result, out bool inactive)
    {
        result = null; inactive = false;
        if (!economicInvocations3211.TryGetValue(invocation, out var bound)
            || bound.InvocationDigest != EconomicInvocationDigest3211(invocation)
            || bound.ProfileId != profileId || bound.ProfileInputsDigest != profileInputsDigest
            || bound.ProfileXmlDigest != profileXmlDigest) return false;
        var matches = bound.Uses.Where(use => use.Setting.Key == key && use.Consumer == consumer).ToArray();
        if (matches is not [var use] || use.Setting.SourceValueDigest != leafXmlDigest
            || sourceAuthority is null || use.SourceAuthority.GetType() != sourceAuthority.GetType()
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(use.SourceAuthority, sourceAuthority)) return false;
        inactive = use.Inactive;
        result = JsonSerializer.Serialize(new
        {
            Operation = "canonical-economic-setting3211", bound.AuthorityDigest, bound.InvocationDigest,
            bound.ProfileId, bound.ProfileInputsDigest, bound.ProfileXmlDigest,
            use.Setting, use.Consumer, use.Path, use.SourceAuthority, use.Result, use.Inactive
        });
        return true;
    }

    private IReadOnlyDictionary<string, string> CompleteEconomicPolicy3211(CharacterCreationCompleteAssessment authority,
        CharacterCreationRuleSourceScopeInvocation invocation, IReadOnlyList<CharacterCreationRuleSourceScopeResult> reports)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!MatchesProfileSettingInputs1203() || !economicInvocations3211.TryGetValue(invocation, out var bound)
            || bound.AuthorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || bound.InvocationDigest != EconomicInvocationDigest3211(invocation)
            || !invocation.TryGetCompletedConsumer("profile", out var consumer)) return result;
        var sources = Inputs.EffectivePolicy!.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules && row.SourceId == "profile").ToArray();
        var profiles = reports.Where(row => row.Scope == "profile").ToArray();
        if (sources is not [var source] || profiles is not [var report]
            || report.ObligationId != $"source:{source.Kind}:{source.Order}" || report.SourceDigest != source.ContentDigest
            || report.ConsumerDigest != consumer || report.Operations.Count == 0
            || report.Operations[0].Disposition != D3211.Structural
            || !report.Operations.Select(row => row.Order).SequenceEqual(Enumerable.Range(0, report.Operations.Count))
            || report.Operations.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() != report.Operations.Count) return result;
        var parents = report.Operations.Where(row => row.SourceDigest == bound.ProfileXmlDigest && row.Disposition == D3211.Structural).ToArray();
        if (parents is not [var parent]) return result;
        foreach (var row in authority.Obligations)
        {
            if (row.Kind != CharacterCreationCompleteObligationKind.Setting
                || row.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent
                || row.Setting is not { State: CharacterCreationSettingObservationState.Present } setting) continue;
            var uses = bound.Uses.Where(use => use.Setting == setting && use.Consumer == row.Consumer).ToArray();
            if (uses is not [var use] || !TryReadEconomicPolicy3211(invocation, setting.Key, use.Consumer,
                bound.ProfileId, bound.ProfileInputsDigest, bound.ProfileXmlDigest, setting.SourceValueDigest!,
                use.SourceAuthority, out var proof, out bool inactive)) continue;
            string path = parent.Path + use.Path;
            var operations = report.Operations.Where(operation => operation.Path == path || operation.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
            if (operations.Length != 2 || operations[0].Path != path || operations[0].SourceDigest != setting.SourceValueDigest
                || operations.Any(operation => operation.Classification886 is not null
                    || operation.Disposition != (inactive ? D3211.NotSelected : D3211.Compiled)
                    || !CharacterCreationFinalizationDigest.IsCanonical(operation.ExecutionDigest)
                    || !HasEconomicProof3211(operation.OperationResult718, proof!))
                || !resolution.HasExecutedProfileSetting1203(invocation, report, operations[0].Order)) continue;
            result.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "canonical-economic-setting-coverage3211", AssessmentAuthorityDigest = bound.AuthorityDigest,
                row.Id, ObligationAuthorityDigest = row.AuthorityDigest, Setting = setting, row.Consumer, Source = source,
                Proof = proof, Inactive = inactive, report.ObligationId, report.SourceDigest, report.ConsumerDigest, Operations = operations
            }));
            if (inactive) inactiveEconomicSettings3211.Add(row.Id);
        }
        return result;
    }

    private static bool HasEconomicProof3211(string? result, string proof)
    {
        if (result is null) return false;
        try
        {
            using var json = JsonDocument.Parse(result);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("Operation", out var operation)
                && operation.ValueKind == JsonValueKind.String && operation.GetString() == "canonical-economic-profile3211"
                && json.RootElement.TryGetProperty("Proofs", out var proofs) && proofs.ValueKind == JsonValueKind.Array
                && proofs.EnumerateArray().Count(item => item.ValueKind == JsonValueKind.String && item.GetString() == proof) == 1;
        }
        catch (JsonException) { return false; }
    }
}
