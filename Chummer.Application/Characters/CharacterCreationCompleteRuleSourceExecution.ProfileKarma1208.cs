using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Four independently executable branches. All other inventory costs remain
    // unresolved until their actual creation consumers exist. In particular,
    // focus, spirit, program, initiation and joining costs also apply in creation.
    private static readonly string[] karmaFields1208 =
        ["karmaspell", "karmanewcomplexform", "karmaenhancement", "karmaleavegroup"];

    private sealed record KarmaLeaf1208(string Field, int Price, string ProfileXmlDigest,
        string LeafXmlDigest, string RelativePath, CharacterCreationSettingObservation Observation);
    private sealed record ExecutedKarma1208(KarmaLeaf1208 Leaf, string Result, string Digest);
    private sealed record BoundKarma1208(string Authority, string Invocation, string ProfileId,
        string RawProfileInputsDigest, IReadOnlyList<ExecutedKarma1208> Leaves);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundKarma1208>
        karmaInvocations1208 = new();
    private CharacterCreationRuleSourceScopeInvocation? karmaInvocation1208;
    private bool karmaSourceReturned1208;
    // Keep producer objects separate from detached diagnostic report clones.
    private IReadOnlyList<CharacterCreationRuleSourceScopeResult> karmaReports1208 = [];

    internal static bool SupportsKarmaSetting1208(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation observation,
        CharacterCreationSettingConsumer? consumer)
        => consumer == CharacterCreationSettingConsumer.None
            && TryKarmaSource1208(policy, source, out var profile)
            && karmaFields1208.Any(field => TryKarmaLeaf1208(policy, profile!, field, out var leaf)
                && leaf!.Observation == observation);

    private static bool TryKarmaSource1208(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, out XElement? profile)
    {
        profile = null;
        if (policy.SettingObservations is null
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, source)) return false;
        profile = XElement.Parse(source!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        return profile.Name == "setting" && !profile.HasAttributes;
    }

    private static bool TryKarmaLeaf1208(CharacterCreationEffectivePolicyContext policy,
        XElement profile, string field, out KarmaLeaf1208? result)
    {
        result = null;
        if (!karmaFields1208.Contains(field, StringComparer.Ordinal)
            || profile.Descendants().Where(node => node.Name.LocalName == "karmacost").ToArray() is not [var costs]
            || costs.Parent != profile || costs.Name != "karmacost" || costs.HasAttributes
            // Includes namespace and nested aliases: none may hide a duplicate.
            || profile.Descendants().Where(node => node.Name.LocalName == field).ToArray() is not [var leaf]
            || leaf.Parent != costs || leaf.Name != field || leaf.HasAttributes
            || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
            || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int price)
            || price < 0) return false;
        string leafDigest = CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting));
        // Reuse the already validated, frozen observation occurrence. Do not
        // rebuild IDs/order, add definitions, replace None, or recapture a subset.
        if (policy.SettingObservations!.Settings.Where(row => row.SourceValueDigest == leafDigest).ToArray()
            is not [var observation] || observation.Key != CharacterCreationObservedSetting.Opaque
            || observation.State != CharacterCreationSettingObservationState.Opaque) return false;
        result = new(field, price, CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
            leafDigest, RelativePath1203(profile, leaf), observation);
        return true;
    }

    private void BindProfileKarma1208(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, XElement graph, CharacterCreationCompleteMagicExecution? magic)
    {
        karmaInvocation1208 = null;
        karmaSourceReturned1208 = false;
        karmaReports1208 = [];
        if (!CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
            || request.RuleSourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            || !CanAttemptSourceScopes696(request) || !MatchesKarmaAuthority1208(authority)
            || original.CreationLifecycleDigestFor(authority) is not { } lifecycle
            || invocation.PolicyDigest != authority.PolicyDigest || invocation.RequestDigest != requestDigest
            || invocation.CaptureDigest != Inputs.RuleSourceCaptureDigest
            || invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting))
            || invocation.WorkspaceId718 is null || invocation.WorkspaceId718 != Inputs.OriginalCharacter!.WorkspaceId
            || invocation.CreationDraft718?.SettingsProfileId != Inputs.EffectivePolicy!.SettingsProfileId
            || !invocation.TryGetCompletedConsumer("profile", out _)
            || !CharacterCreationOriginalCharacterSource.HasSingleCreationField(new XDocument(new XElement(graph)))
            || !TryKarmaSource1208(Inputs.EffectivePolicy!, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, out var profile)) return;

        var leaves = new List<ExecutedKarma1208>();
        string method = invocation.CreationDraft718!.BuildMethod;
        foreach (string field in karmaFields1208)
        {
            if (!TryKarmaLeaf1208(Inputs.EffectivePolicy!, profile!, field, out var leaf)) continue;
            string? result;
            switch (field)
            {
                case "karmaspell":
                case "karmanewcomplexform":
                    if (magic is null || !magic.TryExecutePriorityKarma1208(authority,
                        Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!, method, field, leaf!.Price, out result)) continue;
                    break;
                case "karmaenhancement":
                    // Legacy creation charges 2 per enhancement, independently
                    // of this career-only setting (CharacterCreate:14563-14567).
                    result = JsonSerializer.Serialize(new { Operation = "creation-enhancement-price-branch",
                        ConfiguredCareerPrice = leaf!.Price, CreationPrice = 2, Lifecycle = lifecycle, BuildMethod = method });
                    break;
                case "karmaleavegroup":
                    // Complete is creation -> career, never a career leave-group
                    // command. Membership/join pricing is a separate obligation.
                    result = JsonSerializer.Serialize(new { Operation = "creation-not-career-leave-group",
                        ConfiguredCareerPrice = leaf!.Price, Lifecycle = lifecycle, BuildMethod = method });
                    break;
                default: continue;
            }
            string digest = CharacterCreationFinalizationDigest.Compute(new
            {
                Authority = CharacterCreationFinalizationDigest.Compute(authority), Inputs.SnapshotDigest,
                Original = Inputs.OriginalCharacter, Request = request, Lifecycle = lifecycle,
                Invocation = KarmaInvocationDigest1208(invocation), Source = leaf, Result = result
            });
            leaves.Add(new(leaf!, result!, digest));
        }
        if (leaves.Count == 0) return;
        karmaInvocations1208.Add(invocation, new(CharacterCreationFinalizationDigest.Compute(authority),
            KarmaInvocationDigest1208(invocation), Inputs.EffectivePolicy!.SettingsProfileId,
            Inputs.EffectivePolicy.ProfileInputsDigest, Array.AsReadOnly(leaves.ToArray())));
        karmaInvocation1208 = invocation;
    }

    private bool MatchesKarmaAuthority1208(CharacterCreationCompleteAssessment authority)
        => authority is { Schema: CharacterCreationCompleteAssessment.SchemaV4, Phase: CharacterCreationCompletePhase.Authority }
            && original.MatchesInputs(Inputs.SnapshotDigest)
            && CharacterCreationProfileInputRead8576.Matches(Inputs)
            && request.InputsSnapshotDigest == Inputs.SnapshotDigest && authority.InputsSnapshotDigest == Inputs.SnapshotDigest
            && requestDigest == CharacterCreationFinalizationDigest.Compute(request) && authority.RequestDigest == requestDigest
            && authority.SelectionsDigest == CharacterCreationFinalizationDigest.Compute(request.Selections)
            && authority.RawCharacterXmlDigest == Inputs.Skills.Binding.RawCharacterXmlDigest
            && authority.PolicyDigest == Inputs.EffectivePolicy?.PolicyDigest
            && request.RuleSourceCaptureDigest == Inputs.RuleSourceCaptureDigest
            && Inputs.RuleSourceExecutionSchema == request.RuleSourceExecutionSchema
            && CharacterCreationFinalizationDigest.IsCanonical(Inputs.RuleSourceCaptureDigest);

    private void CompleteKarmaSettings1208(CharacterCreationCompleteAssessment authority, Dictionary<string, string> result)
    {
        if (!karmaSourceReturned1208 || !MatchesKarmaAuthority1208(authority) || karmaInvocation1208 is not { } invocation
            || !karmaInvocations1208.TryGetValue(invocation, out var bound)
            || bound.Authority != CharacterCreationFinalizationDigest.Compute(authority)
            || bound.Invocation != KarmaInvocationDigest1208(invocation)) return;
        foreach (var executed in bound.Leaves)
        {
            // The diagnostic cannot create execution: the private bound leaf
            // above is mandatory. Also require that this invocation's real
            // resolver actually accounted this leaf, even if siblings remain
            // unresolved. An empty/failed source execution cannot bridge it.
            if (!KarmaWasAccounted1208(invocation, executed)) continue;
            var matches = authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Setting
                && row.Consumer == CharacterCreationSettingConsumer.None && row.Setting == executed.Leaf.Observation
                && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent).ToArray();
            if (matches is not [var row]) continue;
            result.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                Authority = bound.Authority, row.Id, row.AuthorityDigest, Execution = executed.Digest
            }));
        }
    }

    private bool KarmaWasAccounted1208(CharacterCreationRuleSourceScopeInvocation invocation, ExecutedKarma1208 executed)
    {
        if (!invocation.TryGetCompletedConsumer("profile", out var consumer)
            || Inputs.EffectivePolicy!.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
                && row.SourceId == "profile").ToArray() is not [var source]
            || karmaReports1208.Where(row => row.Scope == "profile"
                && row.ObligationId == $"source:{source.Kind}:{source.Order}" && row.SourceDigest == source.ContentDigest
                && row.ConsumerDigest == consumer).ToArray() is not [var report]
            || report.Operations.Count == 0 || report.Operations[0].Path != "/chummer[0]"
            || report.Operations[0].Disposition != CharacterCreationSourceOperationDisposition.Structural
            || !report.Operations.Select(row => row.Order).SequenceEqual(Enumerable.Range(0, report.Operations.Count))
            || report.Operations.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() != report.Operations.Count
            || report.Operations.Where(row => row.SourceDigest == executed.Leaf.ProfileXmlDigest
                && row.Disposition == CharacterCreationSourceOperationDisposition.Structural).ToArray() is not [var parent]) return false;
        string path = parent.Path + executed.Leaf.RelativePath;
        var operations = report.Operations.Where(row => row.Path == path
            || row.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
        // The admitted leaf is exactly one plain text node. Check its element
        // and text occurrence, then ask this resolution about the original report.
        if (operations is not [var leaf, var text] || leaf.Path != path
            || leaf.SourceDigest != executed.Leaf.LeafXmlDigest || text.Path != path + "/text()[0]"
            || text.Order != leaf.Order + 1) return false;
        var output = new
        {
            Operation = "selected-profile-creation-karma-branch", Field = executed.Leaf.Field,
            Result = executed.Result, Execution = executed.Digest
        };
        string expected = CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            Consumer = consumer, Disposition = CharacterCreationSourceOperationDisposition.Compiled, Result = output
        });
        return operations.All(row => row.Classification886 is null
                && row.Disposition == CharacterCreationSourceOperationDisposition.Compiled && row.ExecutionDigest == expected)
            && resolution.HasExecutedProfileSetting1203(invocation, report, leaf.Order);
    }

    internal static bool TryReadKarma1208(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string rawProfileInputsDigest, string profileXmlDigest, string field, string leafXmlDigest,
        out string? result, out string? executionDigest)
    {
        result = executionDigest = null;
        if (!karmaInvocations1208.TryGetValue(invocation, out var bound)
            || bound.ProfileId != profileId || bound.RawProfileInputsDigest != rawProfileInputsDigest
            || bound.Invocation != KarmaInvocationDigest1208(invocation)
            || bound.Leaves.Where(row => row.Leaf.Field == field && row.Leaf.ProfileXmlDigest == profileXmlDigest
                && row.Leaf.LeafXmlDigest == leafXmlDigest).ToArray() is not [var executed]) return false;
        result = executed.Result;
        executionDigest = executed.Digest;
        return true;
    }

    private static string KarmaInvocationDigest1208(CharacterCreationRuleSourceScopeInvocation invocation)
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
    // Detached diagnostics; only the private canonical execution above can bind.
    public bool TryGetExecutedProfileKarma1208(string profileId, string rawProfileInputsDigest,
        string profileXmlDigest, string field, string leafXmlDigest, out string? result, out string? executionDigest)
        => CharacterCreationCompleteRuleSourceExecution.TryReadKarma1208(this, profileId, rawProfileInputsDigest,
            profileXmlDigest, field, leafXmlDigest, out result, out executionDigest);
}
