using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    internal const string EssenceValidationBlocker1222 = "creation-essence-source-validation1222";
    // Requested semantics start unresolved. A skipped/failed graph compiler must
    // not turn a requested result into optional coverage, even for None policy.
    internal bool EssenceValidationFailed1222 { get; private set; } = WantsEssence1222(request);
    internal string EssenceFailureBlocker1222 { get; private set; } = EssenceValidationBlocker1222;
    internal CharacterCreationEssenceExecution1222? AppliedEssence1222 => essenceExecution1222;
    internal void RejectEssence1222(string blocker = EssenceValidationBlocker1222)
    {
        EssenceValidationFailed1222 = true;
        EssenceFailureBlocker1222 = blocker;
        essenceInvocation1222 = null;
        essenceExecution1222 = null;
        essenceScope1222 = null;
    }
    private CharacterCreationEssenceInvocation1222? essenceInvocation1222;
    private CharacterCreationEssenceExecution1222? essenceExecution1222;
    private CharacterCreationRuleSourceScopeInvocation? essenceScope1222;

    internal static bool WantsEssence1222(CharacterCreationCompletionReviewRequest proposal)
        => proposal.EssenceResultSchema is not null;

    // Called inside CompileSelectedGraph, before its success return. Failure is a
    // real candidate/finalization blocker, not merely an unfilled receipt slot.
    internal bool ApplyEssence1222(XElement graph)
    {
        essenceInvocation1222 = null; essenceExecution1222 = null; essenceScope1222 = null;
        if (!WantsEssence1222(request)) return true;
        EssenceValidationFailed1222 = true;
        EssenceFailureBlocker1222 = EssenceValidationBlocker1222;
        if (!CharacterCreationCompletionReviewRules.HasValidEssenceResult(request)
            || !original.MatchesInputs(Inputs.SnapshotDigest) || Inputs.OriginalCharacter is null
            || request.InputsSnapshotDigest != Inputs.SnapshotDigest
            || request.RuleSourceCaptureDigest != Inputs.RuleSourceCaptureDigest
            || !CharacterCreationFinalizationDigest.IsCanonical(capturePolicyDigest1222)
            || Inputs.EffectivePolicy is not { } policy
            || request.EffectivePolicyDigest != policy.PolicyDigest
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy,
                Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, CharacterCreationPolicyDomain.Profile)
            || !CharacterCreationAwakenedLegacyProjector.TryReadPhysicalTalent1279(graph, out _)) return false;
        var invocation = new CharacterCreationEssenceInvocation1222(Inputs, request, graph, capturePolicyDigest1222);
        var executed = resolution.ExecuteEssence1222(invocation);
        if (executed is null || !executed.Result.Valid
            || executed.CapturePolicyDigest != invocation.CapturePolicyDigest
            || executed.PolicyDigest != invocation.PolicyDigest
            || !resolution.HasExecutedEssence1222(invocation, executed)
            || CharacterCreationFinalizationDigest.Compute(executed.GraphXml) != executed.GraphDigest) return false;
        var projected = XElement.Parse(executed.GraphXml, LoadOptions.PreserveWhitespace);
        if (!CharacterCreationAwakenedLegacyProjector.ApplyEssenceGraph1291(
                graph, projected, invocation, executed, resolution)) return false;
        essenceInvocation1222 = invocation; essenceExecution1222 = executed;
        EssenceValidationFailed1222 = false;
        return true;
    }

    // Reusable exact consumer for worker1217. It owns no lifecycle Setting rows.
    internal bool TryGetEssenceExecution1222(CharacterCreationRuleSourceScopeInvocation scope,
        out CharacterCreationEssenceExecution1222? result)
    {
        result = null;
        if (!ReferenceEquals(scope, essenceScope1222) || essenceInvocation1222 is not { } invocation
            || essenceExecution1222 is not { } execution || !execution.Result.Valid
            || scope.GraphDigest != execution.GraphDigest || scope.RequestDigest != invocation.RequestDigest
            || scope.PolicyDigest != invocation.PolicyDigest || scope.CaptureDigest != invocation.CaptureDigest
            || !resolution.HasExecutedEssence1222(invocation, execution)) return false;
        result = execution; return true;
    }

    private void BindEssenceScope1222(CharacterCreationRuleSourceScopeInvocation invocation)
    {
        essenceScope1222 = null;
        if (essenceExecution1222 is { } execution && essenceInvocation1222 is { } originalInvocation
            && invocation.GraphDigest == execution.GraphDigest && invocation.RequestDigest == originalInvocation.RequestDigest
            && invocation.CaptureDigest == originalInvocation.CaptureDigest && invocation.PolicyDigest == originalInvocation.PolicyDigest
            && resolution.HasExecutedEssence1222(originalInvocation, execution))
        {
            essenceScope1222 = invocation;
            CharacterCreationRuleSourceScopeInvocation.BindEssence1222(invocation, execution);
        }
    }

    private static bool IsEssenceSetting1222(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation setting,
        CharacterCreationSettingConsumer? consumer)
    {
        if (consumer != CharacterCreationSettingConsumer.None || setting.Key != CharacterCreationObservedSetting.Opaque
            || setting.State != CharacterCreationSettingObservationState.Opaque || setting.ValueDigest is not null
            || policy.SettingObservations is not { } observations
            || !TryReadProfileSettingCandidate3320(source, out var profile)) return false;
        var leaves = profile!.Elements().Where(row => row.Name.LocalName == "essenceformat").ToArray();
        if (leaves is not [var leaf] || leaf.Name != "essenceformat" || leaf.HasAttributes
            || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
            || CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)) != setting.SourceValueDigest)
            return false;
        // Candidate parsing grants no authority; retain full current-source and
        // observation admission after rejecting unrelated format leaves.
        return CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
            && CharacterCreationSettingsObservation.TryCapture(source!.CanonicalSourceXml, policy.ProfileInputsDigest,
                observations.Schema, out var fresh)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(observations, fresh)
            && observations.Settings.Count(row => row == setting) == 1;
    }

    internal bool CanExecuteEssence1222(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation setting,
        CharacterCreationSettingConsumer? consumer, CharacterCreationCompletionReviewRequest proposal)
        // This predicate is queried for the whole setting inventory, including
        // unrelated leaves. Only supported leaves need the full request hash and
        // catalog comparison; no successful admission or mutable value is cached.
        => WantsEssence1222(proposal) && CharacterCreationCompletionReviewRules.HasValidEssenceResult(proposal)
            && (IsEssenceSetting1222(policy, source, setting, consumer)
                || IsEssenceControlSetting(policy, source, setting, consumer))
            && requestDigest == CharacterCreationFinalizationDigest.Compute(proposal)
            && original.MatchesInputs(Inputs.SnapshotDigest)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy with { SelectedConsumption = null }, Inputs.EffectivePolicy)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(source, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy);

    private void CompleteEssence1222(CharacterCreationCompleteAssessment authority, IDictionary<string, string> completed)
    {
        CompleteEssenceSettings(authority, completed);
        if (essenceScope1222 is not { } invocation || !TryGetEssenceExecution1222(invocation, out var execution)
            || authority.RequestDigest != requestDigest || authority.InputsSnapshotDigest != Inputs.SnapshotDigest) return;
        foreach (var row in authority.Obligations)
            if (row.Kind == CharacterCreationCompleteObligationKind.Setting && row.Setting is not null
                && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent
                && IsEssenceSetting1222(Inputs.EffectivePolicy!, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, row.Setting, row.Consumer))
                completed.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
                {
                    Authority = CharacterCreationFinalizationDigest.Compute(authority), row.Id, row.AuthorityDigest,
                    row.Setting, authority.SettingsObservationDigest, invocation.CaptureDigest,
                    invocation.RequestDigest, invocation.GraphDigest, execution!.ExecutionDigest
                }));
    }
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    private sealed record EssenceBinding1222(string Binding, CharacterCreationEssenceExecution1222 Execution, CharacterCreationEssenceSettingEvidence? Settings, string SettingsDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, EssenceBinding1222> essence1222 = new();
    internal static void BindEssence1222(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationEssenceExecution1222 execution)
        => essence1222.Add(invocation, new(EssenceBindingDigest1222(invocation), execution, execution.SettingsEvidence, CharacterCreationFinalizationDigest.Compute(execution.SettingsEvidence)));
    public bool TryGetEssence1222(out CharacterCreationEssenceExecution1222? execution)
    {
        execution = null;
        if (!essence1222.TryGetValue(this, out var bound) || bound.Binding != EssenceBindingDigest1222(this)
            || !ReferenceEquals(bound.Settings, bound.Execution.SettingsEvidence)
            || bound.SettingsDigest != CharacterCreationFinalizationDigest.Compute(bound.Execution.SettingsEvidence)) return false;
        execution = bound.Execution; return true;
    }
    private static string EssenceBindingDigest1222(CharacterCreationRuleSourceScopeInvocation invocation)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            invocation.CreationDraft718, invocation.WorkspaceId718, invocation.Attributes718,
            invocation.AttributePolicy718, invocation.Skills718, invocation.CompiledEffects,
            Profile = invocation.TryGetCompletedConsumer("profile", out var consumer) ? consumer : null
        });
}
