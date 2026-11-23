using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private sealed record Budget1221(string Authority, string Graph, string Effects,
        string Profile, string ProfileInputs, CharacterCreationCompletionPoolBalances Before,
        CharacterCreationCompletionPoolBalances After, CharacterCreationRetainedMagicKarma1221.Result Costs);
    private sealed record Leaf1221(string Field, string Source, CharacterCreationSettingObservation Observation,
        string Result, string Execution);
    private sealed record Invocation1221(string Binding, string ProfileId, string ProfileInputs, string Profile,
        IReadOnlyList<Leaf1221> Leaves);
    private Budget1221? retainedBudget1221;
    private CharacterCreationRuleSourceScopeInvocation? retainedInvocation1221;
    private IReadOnlyList<CharacterCreationRuleSourceScopeResult> retainedReports1221 = [];
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, Invocation1221> retainedInvocations1221 = new();

    // Compatibility entry points delegate to the single private composition.
    internal CharacterCreationCompletionReview ApplyRetainedMagicFull1221(CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority, CancellationToken cancellationToken)
        => ApplySharedChargesFull1239(review, authority, cancellationToken);

    internal bool ApplyRetainedMagicSelected1221(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationSelectedCompletionEvaluation selected, CharacterCreationCompleteAssessment authority,
        IReadOnlyList<CharacterCreationLoadoutInstance>? originalLoadoutInstances,
        CharacterCreationOriginalCharacterSource? originalCharacter, CancellationToken cancellationToken,
        out CharacterCreationSelectedCompletionEvaluation result, out string blocker)
        => ApplySharedChargesSelected1239(witness, selected, authority, originalLoadoutInstances,
            originalCharacter, cancellationToken, out result, out blocker);

    private static bool AttemptsRetainedMagic1221(CharacterCreationCompletionReviewRequest request)
        => CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
            && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            && CanAttemptSourceScopes696(request);

    private bool MatchesRetainedAuthority1221(CharacterCreationCompleteAssessment authority)
        => authority is { Schema: CharacterCreationCompleteAssessment.SchemaV4, Phase: CharacterCreationCompletePhase.Authority }
            && original.MatchesInputs(Inputs.SnapshotDigest)
            && CharacterCreationProfileInputRead8576.Matches(Inputs)
            && request.InputsSnapshotDigest == Inputs.SnapshotDigest && authority.InputsSnapshotDigest == Inputs.SnapshotDigest
            && requestDigest == CharacterCreationFinalizationDigest.Compute(request) && authority.RequestDigest == requestDigest
            && authority.SelectionsDigest == CharacterCreationFinalizationDigest.Compute(request.Selections)
            && authority.RawCharacterXmlDigest == Inputs.Skills.Binding.RawCharacterXmlDigest
            && authority.PolicyDigest == Inputs.EffectivePolicy?.PolicyDigest
            && request.RuleSourceCaptureDigest == Inputs.RuleSourceCaptureDigest
            && Inputs.RuleSourceExecutionSchema == request.RuleSourceExecutionSchema;

    private static bool TryRetainedProfile1221(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, out XElement? profile)
    {
        profile = null;
        if (policy.SettingObservations is null
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, source)) return false;
        profile = XElement.Parse(source!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        return profile.Name == "setting" && !profile.HasAttributes;
    }

    // This admits an attempt only. Coverage below additionally requires the actual
    // budget debit, identical compiled graph and private resolver invocation.
    internal static bool SupportsRetainedMagicSetting1221(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation observation,
        CharacterCreationSettingConsumer? consumer)
    {
        if (consumer != CharacterCreationSettingConsumer.None || !TryRetainedProfile1221(policy, source, out var profile)) return false;
        return RetainedFields1221.Any(field => TryRetainedLeaf1221(policy, profile!, field, out _, out var actual) && actual == observation);
    }

    private static readonly string[] RetainedFields1221 =
        ["karmaspirit", "karmaspiritfettering", "karmanewaiprogram", "karmanewaiadvancedprogram",
         "karmainitiationflat", "karmainitiation", "karmametamagic", "karmajoingroup", "karmamaneuver", "karmaenemy"];

    private static bool TryRetainedLeaf1221(CharacterCreationEffectivePolicyContext policy, XElement profile,
        string field, out string? sourceDigest, out CharacterCreationSettingObservation? observation)
    {
        sourceDigest = null;
        observation = null;
        if (profile.Descendants().Where(node => node.Name.LocalName == "karmacost").ToArray() is not [var costs]
            || costs.Name != "karmacost" || costs.Parent != profile || costs.HasAttributes
            || profile.Descendants().Where(node => node.Name.LocalName == field).ToArray() is not [var leaf]
            || leaf.Name != field || leaf.Parent != costs || leaf.HasAttributes
            || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
            || !int.TryParse(leaf.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
            || value < 0) return false;
        string digest = XmlDigest1221(leaf);
        if (policy.SettingObservations!.Settings.Where(row => row.SourceValueDigest == digest).ToArray() is not [var found]
            || found.Key != CharacterCreationObservedSetting.Opaque || found.State != CharacterCreationSettingObservationState.Opaque) return false;
        sourceDigest = digest;
        observation = found;
        return true;
    }

    private void BindRetainedMagic1221(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, XElement graph, CharacterCreationSelectedEffectsConsumer effects)
    {
        retainedInvocation1221 = null;
        retainedReports1221 = [];
        if (!AttemptsRetainedMagic1221(request) || !MatchesRetainedAuthority1221(authority)
            || retainedBudget1221 is not { } budget || budget.Authority != CharacterCreationFinalizationDigest.Compute(authority)
            || budget.Effects != effects.DigestFor(authority) || budget.Graph != XmlDigest1221(graph)
            || invocation.GraphDigest != budget.Graph || invocation.RequestDigest != requestDigest
            || invocation.PolicyDigest != authority.PolicyDigest || invocation.CaptureDigest != Inputs.RuleSourceCaptureDigest
            || invocation.WorkspaceId718 != Inputs.OriginalCharacter?.WorkspaceId
            || !invocation.TryGetCompletedConsumer("profile", out _)
            || !TryRetainedProfile1221(Inputs.EffectivePolicy!, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, out var profile)
            || budget.Profile != XmlDigest1221(profile!) || budget.ProfileInputs != Inputs.EffectivePolicy!.ProfileInputsDigest) return;
        var leaves = new List<Leaf1221>();
        foreach (var branch in budget.Costs.Branches)
        foreach (string field in branch.Fields)
        {
            if (!TryRetainedLeaf1221(Inputs.EffectivePolicy!, profile!, field, out var source, out var observation)) continue;
            string detail = JsonSerializer.Serialize(new { Branch = branch, TotalCost = budget.Costs.Cost, budget.Before, budget.After });
            string execution = CharacterCreationFinalizationDigest.Compute(new
            {
                budget, Inputs.OriginalCharacter, Inputs.SnapshotDigest, Request = request,
                Invocation = InvocationDigest1221(invocation), Field = field, Source = source, Result = detail
            });
            leaves.Add(new(field, source!, observation!, detail, execution));
        }
        retainedInvocations1221.Add(invocation, new(InvocationDigest1221(invocation), Inputs.EffectivePolicy!.SettingsProfileId,
            budget.ProfileInputs, budget.Profile, leaves.AsReadOnly()));
        retainedInvocation1221 = invocation;
    }

    private void CompleteRetainedMagicSettings1221(CharacterCreationCompleteAssessment authority, Dictionary<string, string> result)
    {
        if (!MatchesRetainedAuthority1221(authority) || retainedBudget1221?.Authority != CharacterCreationFinalizationDigest.Compute(authority)
            || retainedInvocation1221 is not { } invocation || !retainedInvocations1221.TryGetValue(invocation, out var bound)
            || bound.Binding != InvocationDigest1221(invocation) || !invocation.TryGetCompletedConsumer("profile", out var consumer)
            || Inputs.EffectivePolicy!.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules && row.SourceId == "profile")
                .ToArray() is not [var source]
            || retainedReports1221.Where(row => row.Scope == "profile" && row.ObligationId == $"source:{source.Kind}:{source.Order}"
                && row.SourceDigest == source.ContentDigest && row.ConsumerDigest == consumer).ToArray() is not [var report]) return;
        foreach (var leaf in bound.Leaves)
        {
            string expected = CharacterCreationFinalizationDigest.Compute(new
            {
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                Consumer = consumer, Disposition = CharacterCreationSourceOperationDisposition.Compiled,
                Result = new { Operation = "creation-retained-magic-karma1221", Field = leaf.Field, Result = leaf.Result, Execution = leaf.Execution }
            });
            var operations = report.Operations.Where(row => row.SourceDigest == leaf.Source
                && row.Disposition == CharacterCreationSourceOperationDisposition.Compiled && row.ExecutionDigest == expected).ToArray();
            if (operations is not [var operation]
                || !resolution.HasExecutedProfileSetting1203(invocation, report, operation.Order)) continue;
            var matches = authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Setting
                && row.Consumer == CharacterCreationSettingConsumer.None && row.Setting == leaf.Observation
                && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent).ToArray();
            if (matches is not [var obligation]) continue;
            result.Add(obligation.Id, CharacterCreationFinalizationDigest.Compute(new
                { Authority = retainedBudget1221.Authority, obligation.Id, obligation.AuthorityDigest, leaf.Execution }));
        }
    }

    internal static bool ReadRetainedMagic1221(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputs, string profileDigest, string field, string leafDigest,
        out string? result, out string? execution)
    {
        result = execution = null;
        if (!retainedInvocations1221.TryGetValue(invocation, out var bound) || bound.Binding != InvocationDigest1221(invocation)
            || bound.ProfileId != profileId || bound.ProfileInputs != profileInputs || bound.Profile != profileDigest
            || bound.Leaves.Where(row => row.Field == field && row.Source == leafDigest).ToArray() is not [var leaf]) return false;
        result = leaf.Result;
        execution = leaf.Execution;
        return true;
    }

    private static string InvocationDigest1221(CharacterCreationRuleSourceScopeInvocation invocation)
    {
        invocation.TryGetCompletedConsumer("profile", out var consumer);
        return CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest, consumer,
            invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718, invocation.Attributes718,
            invocation.AttributePolicy718, invocation.Skills718, invocation.ResourcesExecution718,
            invocation.LifestyleAuthority718, invocation.Lifestyles718, invocation.StartingLifestyleId718,
            invocation.SourceExecution832, invocation.CompiledMovement832, invocation.PhysicalAuthority886, invocation.PhysicalInitiative886
        });
    }
    private static string XmlDigest1221(XElement element) => CharacterCreationFinalizationDigest.Compute(element.ToString(SaveOptions.DisableFormatting));
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    public bool TryGetExecutedRetainedMagicKarma1221(string profileId, string profileInputs, string profileDigest,
        string field, string leafDigest, out string? result, out string? execution)
        => CharacterCreationCompleteRuleSourceExecution.ReadRetainedMagic1221(this, profileId, profileInputs,
            profileDigest, field, leafDigest, out result, out execution);
}
