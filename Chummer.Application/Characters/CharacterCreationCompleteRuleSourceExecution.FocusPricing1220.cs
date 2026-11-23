using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private sealed record FocusLeaf1220(CharacterCreationFocusKarma1220.Price Price, CharacterCreationSettingObservation Observation);
    private sealed record BoundFocus1220(string Authority, string Invocation, string ProfileId, string RawProfileInputsDigest,
        FocusBudget1220 Budget, IReadOnlyList<FocusLeaf1220> Leaves, string Result);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundFocus1220> focusInvocations1220 = new();
    private CharacterCreationRuleSourceScopeInvocation? focusInvocation1220;
    private IReadOnlyList<CharacterCreationRuleSourceScopeResult> focusReports1239 = [];

    private static bool TryFocusProfile1220(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, out XElement? profile,
        out IReadOnlyList<FocusLeaf1220> leaves)
    {
        profile = null;
        leaves = [];
        if (policy.SettingObservations is null
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, source)) return false;
        profile = XElement.Parse(source!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        if (!CharacterCreationFocusKarma1220.TryReadPrices(profile, out var prices)) return false;
        var result = new List<FocusLeaf1220>();
        foreach (var price in prices)
        {
            if (policy.SettingObservations.Settings.Where(row => row.SourceValueDigest == price.LeafDigest).ToArray()
                is not [var observation] || observation.Key != CharacterCreationObservedSetting.Opaque
                || observation.State != CharacterCreationSettingObservationState.Opaque) return false;
            result.Add(new(price, observation));
        }
        leaves = result.AsReadOnly();
        return true;
    }

    internal static bool SupportsFocusSetting1220(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation observation,
        CharacterCreationSettingConsumer? consumer)
        => consumer == CharacterCreationSettingConsumer.None
            && TryFocusProfile1220(policy, source, out _, out var leaves)
            && leaves.Any(leaf => leaf.Observation == observation);

    private void BindFocusPricing1220(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationSelectedEffectsConsumer effects, XElement graph)
    {
        focusInvocation1220 = null;
        focusReports1239 = [];
        if (!CanAttemptFocus1220(request) || !MatchesFocusAuthority1220(authority) || focusBudget1220 is not { } budget
            || budget.AuthorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || budget.EffectsDigest != effects.DigestFor(authority)
            || budget.Lifecycle != original.CreationLifecycleDigestFor(authority)
            || invocation.PolicyDigest != authority.PolicyDigest || invocation.RequestDigest != requestDigest
            || invocation.CaptureDigest != Inputs.RuleSourceCaptureDigest
            || invocation.GraphDigest != budget.Calculation.GraphDigest
            || invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting))
            || invocation.WorkspaceId718 != Inputs.OriginalCharacter!.WorkspaceId
            || invocation.CreationDraft718?.SettingsProfileId != Inputs.EffectivePolicy!.SettingsProfileId
            || !invocation.TryGetCompletedConsumer("profile", out _)
            || !TryReadProfileSourcebooks935(invocation, Inputs.EffectivePolicy!.SettingsProfileId,
                Inputs.EffectivePolicy.ProfileInputsDigest, Inputs.EffectivePolicy.BooksInputsDigest!, out _, out _)
            || !TryFocusProfile1220(Inputs.EffectivePolicy!, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
                out var profile, out var leaves)
            || budget.Calculation.ProfileXmlDigest != CharacterCreationFinalizationDigest.Compute(profile!.ToString(SaveOptions.DisableFormatting))) return;
        if (sharedCharges1239 is not { } wallet || !ReferenceEquals(wallet.Focus, budget)
            || budget.Calculation.Applicability.Count != leaves.Count
            || !budget.Calculation.Applicability.Select(row => row.Field).SequenceEqual(leaves.Select(row => row.Price.Field))) return;
        string output = JsonSerializer.Serialize(new { Operation = "compiled-focus-binding-and-shared-karma",
            budget.Calculation, budget.Before, PostFocus = budget.After, After = wallet.After, budget.ExecutionDigest });
        focusInvocations1220.Add(invocation, new(budget.AuthorityDigest, FocusInvocationDigest1220(invocation),
            Inputs.EffectivePolicy!.SettingsProfileId, Inputs.EffectivePolicy.ProfileInputsDigest, budget, leaves, output));
        focusInvocation1220 = invocation;
    }

    private void CompleteFocusSettings1220(CharacterCreationCompleteAssessment authority, Dictionary<string, string> result)
    {
        if (!MatchesFocusAuthority1220(authority) || focusInvocation1220 is not { } invocation
            || !focusInvocations1220.TryGetValue(invocation, out var bound)
            || bound.Authority != CharacterCreationFinalizationDigest.Compute(authority)
            || bound.Invocation != FocusInvocationDigest1220(invocation)
            || !ReferenceEquals(focusBudget1220, bound.Budget)
            || !invocation.TryGetCompletedConsumer("profile", out var consumer)
            || Inputs.EffectivePolicy!.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
                && row.SourceId == "profile").ToArray() is not [var source]
            || focusReports1239.Where(row => row.Scope == "profile").ToArray() is not [var report]
            || report.ObligationId != $"source:{source.Kind}:{source.Order}" || report.SourceDigest != source.ContentDigest
            || report.ConsumerDigest != consumer || report.Operations.Count == 0
            || report.Operations[0].Disposition != CharacterCreationSourceOperationDisposition.Structural
            || !report.Operations.Select(row => row.Order).SequenceEqual(Enumerable.Range(0, report.Operations.Count))
            || report.Operations.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() != report.Operations.Count
            || report.Operations.Where(row => row.SourceDigest == bound.Budget.Calculation.ProfileXmlDigest
                && row.Disposition == CharacterCreationSourceOperationDisposition.Structural).ToArray() is not [var parent]) return;
        foreach (var leaf in bound.Leaves)
        {
            string path = parent.Path + leaf.Price.RelativePath;
            var operations = report.Operations.Where(row => row.Path == path
                || row.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
            if (operations is not [var element, var text]
                || element.Order != parent.Order + leaf.Price.RelativeOrder || text.Order != element.Order + 1
                || element.Path != path || text.Path != path + "/text()[0]"
                || element.SourceDigest != leaf.Price.LeafDigest || text.SourceDigest != leaf.Price.TextDigest
                || operations.Any(row => row.Disposition != CharacterCreationSourceOperationDisposition.Compiled
                    || row.Classification886 is not null || row.OperationResult718 is null
                    || row.ConsumedValue718 is null || row.ExecutionDigest != element.ExecutionDigest
                    || row.OperationResult718 != element.OperationResult718 || row.ConsumedValue718 != element.ConsumedValue718)
                || !resolution.HasExecutedProfileSetting1203(invocation, report, element.Order)
                || !resolution.HasExecutedProfileSetting1203(invocation, report, text.Order)) continue;
            if (!MatchesFocusOperation1239(invocation, consumer, element, bound, leaf)) continue;
            if (authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Setting
                && row.Consumer == CharacterCreationSettingConsumer.None && row.Setting == leaf.Observation
                && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent).ToArray() is not [var obligation]) continue;
            result.Add(obligation.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                bound.Authority, obligation.Id, obligation.AuthorityDigest, Observation = leaf.Observation,
                Execution = bound.Budget.ExecutionDigest, leaf.Price, Source = source,
                report.ObligationId, report.SourceDigest, report.ConsumerDigest, Operations = operations
            }));
        }
    }

    private static bool MatchesFocusOperation1239(CharacterCreationRuleSourceScopeInvocation invocation,
        string consumer, CharacterCreationSourceOperation element, BoundFocus1220 bound, FocusLeaf1220 leaf)
    {
        try
        {
            using var diagnostic = JsonDocument.Parse(element.OperationResult718!);
            var output = diagnostic.RootElement;
            if (output.GetProperty("Operation").GetString() != "selected-profile-focus-binding-karma"
                || output.GetProperty("Field").GetString() != leaf.Price.Field
                || output.GetProperty("Result").GetString() != bound.Result
                || output.GetProperty("Execution").GetString() != bound.Budget.ExecutionDigest
                || !CharacterCreationFinalizationDigest.IsCanonical(output.GetProperty("Admission").GetString())) return false;
            return element.ExecutionDigest == CharacterCreationFinalizationDigest.Compute(new
            {
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                Consumer = consumer, Disposition = CharacterCreationSourceOperationDisposition.Compiled, Result = output
            });
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }

    internal static bool TryReadFocus1220(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string rawProfileInputsDigest, string profileXmlDigest, string field, string leafXmlDigest,
        out string? result, out string? executionDigest)
    {
        result = executionDigest = null;
        if (!focusInvocations1220.TryGetValue(invocation, out var bound)
            || bound.ProfileId != profileId || bound.RawProfileInputsDigest != rawProfileInputsDigest
            || bound.Invocation != FocusInvocationDigest1220(invocation)
            || bound.Budget.Calculation.ProfileXmlDigest != profileXmlDigest
            || bound.Leaves.Count(row => row.Price.Field == field && row.Price.LeafDigest == leafXmlDigest) != 1) return false;
        result = bound.Result;
        executionDigest = bound.Budget.ExecutionDigest;
        return true;
    }

    private static string FocusInvocationDigest1220(CharacterCreationRuleSourceScopeInvocation invocation)
    {
        invocation.TryGetCompletedConsumer("profile", out var profile);
        return CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            Profile = profile, invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718,
            invocation.Attributes718, invocation.AttributePolicy718, invocation.Skills718, invocation.ResourcesExecution718,
            invocation.LifestyleAuthority718, invocation.Lifestyles718, invocation.StartingLifestyleId718,
            invocation.SourceExecution832, invocation.CompiledMovement832, invocation.PhysicalAuthority886, invocation.PhysicalInitiative886
        });
    }
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Read-only private-execution adapter for the infrastructure source ledger.
    // There is deliberately no registration API, public quote or receipt DTO.
    public bool TryGetExecutedFocusPricing1220(string profileId, string rawProfileInputsDigest,
        string profileXmlDigest, string field, string leafXmlDigest, out string? result, out string? executionDigest)
        => CharacterCreationCompleteRuleSourceExecution.TryReadFocus1220(this, profileId, rawProfileInputsDigest,
            profileXmlDigest, field, leafXmlDigest, out result, out executionDigest);
}
