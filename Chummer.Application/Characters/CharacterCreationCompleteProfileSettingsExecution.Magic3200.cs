using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using D3200 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;
using S = Chummer.Contracts.Characters.CharacterCreationObservedSetting;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private sealed record MagicLeaf3200(S Key, string Field, string Path, string XmlDigest,
        string Input, string Value, bool Inactive);
    private sealed record BoundMagic3200(string AuthorityDigest, string InputsDigest, string PolicyDigest, string PolicyContentDigest,
        string ProfileId, string ProfileInputsDigest, string ProfileXmlDigest, string InvocationDigest,
        string MagicDigest, string TalentKind, string ResultJson, IReadOnlyList<MagicLeaf3200> Leaves);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundMagic3200>
        magicInvocations3200 = new();
    private readonly Dictionary<string, string> magicEvaluations3200 = new(StringComparer.Ordinal);
    private readonly HashSet<string> magicInactive3200 = new(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, string> WithMagicEvaluations3200()
        => evaluations.Concat(magicEvaluations3200).ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);

    internal void BindMagic3200(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationCompleteMagicExecution? magic)
    {
        if (!CanAttempt(request) || request.CompleteAssessmentSchema != CharacterCreationCompleteAssessment.SchemaV4
            || request.RuleSourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || authority.InputsSnapshotDigest != inputs.SnapshotDigest || request.InputsSnapshotDigest != inputs.SnapshotDigest
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || invocation.RequestDigest != authority.RequestDigest || invocation.PolicyDigest != authority.PolicyDigest
            || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest || invocation.CaptureDigest != request.RuleSourceCaptureDigest
            || inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || inputs.EffectivePolicy is not { SettingObservations: not null } policy || policy.PolicyDigest != authority.PolicyDigest
            || inputs.OriginalCharacter is not { } original || invocation.WorkspaceId718 != original.WorkspaceId
            || original.WorkspaceId != inputs.Magic.Binding.WorkspaceId
            || original.ContentRevision != inputs.Magic.Binding.ContentRevision || original.SavedRevision != inputs.Magic.Binding.SavedRevision
            || authority.RawCharacterXmlDigest != inputs.Skills.Binding.RawCharacterXmlDigest
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(invocation.CreationDraft718, inputs.Skills.PrerequisiteDraft)
            || !invocation.TryGetCompletedConsumer("profile", out _)
            || !TrySource(policy, inputs.Magic.Authority.MysticAdeptPowerPointPolicy, out var profile)
            || magic?.DigestFor(authority) is not { } magicDigest
            || !magic.TryReadProfileMagic3200(authority, inputs.Magic, request.Selections.MagicSelections, out var result)) return;
        var source = inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        if (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            || invocation.CreationDraft718!.SettingsProfileId != source.SettingsProfileId) return;

        // These are the exact branches executed by TryResolveEffectiveAttributes
        // and TryEvaluate. A combined Mystic Adept executes BOTH purchase settings
        // even when selected PP, the multiplier or the remaining charge is zero.
        bool mystic = inputs.Magic.SelectedTalent.Kind == CharacterCreationMagicResonanceKinds.MysticAdept;
        bool separate = mystic && source.UsesSeparateMagicAttribute;
        if (mystic && !separate
            ? result!.MysticAdeptPowerPoints is not { } allocation
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(allocation.Policy, source)
                || result.EffectiveAttributes.MagicAdept is not null
            : result!.MysticAdeptPowerPoints is not null
                || (result.EffectiveAttributes.MagicAdept is not null) != separate) return;

        if (profile!.Elements("karmacost").ToArray() is not [var costs] || costs.HasAttributes) return;
        var leaves = new List<MagicLeaf3200>();
        bool Add(XElement container, S key, string field, string value, bool inactive)
        {
            if (container.Elements().Where(node => node.Name.LocalName == field).ToArray() is not [var leaf]
                || leaf.Name != field || leaf.HasAttributes || leaf.Nodes().ToArray() is not [XText text]
                || text.GetType() != typeof(XText)) return false;
            var definition = CharacterCreationSettingDefinitions.ForSchema(policy.SettingObservations!.Schema)
                ?.SingleOrDefault(row => row.Key == key);
            var observations = policy.SettingObservations.Settings.Where(row => row.Key == key).ToArray();
            string xmlDigest = CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting));
            if (definition is null || observations is not [var observation]
                || observation.State != CharacterCreationSettingObservationState.Present
                || observation.SourceValueDigest != xmlDigest
                || observation.ValueDigest != CharacterCreationSettingDefinitions.ValueDigest(definition.Kind, value)
                || observation.ValueDigest != CharacterCreationSettingDefinitions.ValueDigest(definition.Kind, leaf.Value)) return false;
            leaves.Add(new(key, field, RelativeMagicPath3200(leaf, profile), xmlDigest, leaf.Value, value, inactive));
            return true;
        }
        if (!Add(costs, S.MysticAdeptKarma, "karmamysadpp", source.KarmaPerPowerPoint.ToString(CultureInfo.InvariantCulture), !mystic || separate)
            || !Add(profile, S.MysticAdeptSpellExchange, "priorityspellsasadeptpowers", source.PrioritySpellsAsPowerPoints ? "true" : "false", !mystic || separate)
            || !Add(profile, S.SeparateAdeptMagic, "mysadeptsecondmagattribute", source.UsesSeparateMagicAttribute ? "true" : "false", !mystic)) return;
        magicInvocations3200.Add(invocation, new(authorityDigest, inputs.SnapshotDigest, policy.PolicyDigest,
            CharacterCreationFinalizationDigest.Compute(policy),
            source.SettingsProfileId, policy.ProfileInputsDigest,
            CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
            SkillCostInvocationBinding972(invocation), magicDigest, inputs.Magic.SelectedTalent.Kind,
            JsonSerializer.Serialize(result), leaves.AsReadOnly()));
    }

    private static string RelativeMagicPath3200(XElement leaf, XElement profile)
        => ReferenceEquals(leaf, profile) ? string.Empty
            : RelativeMagicPath3200(leaf.Parent!, profile) + "/" + leaf.Name + "["
                + leaf.Parent!.Nodes().TakeWhile(node => !ReferenceEquals(node, leaf)).Count() + "]";

    internal static bool TryReadMagic3200(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string field, string leafXmlDigest,
        out string? value, out bool inactive, out string? talentKind, out string? executionDigest)
    {
        value = null; inactive = false; talentKind = null; executionDigest = null;
        if (!magicInvocations3200.TryGetValue(invocation, out var bound)
            || bound.InvocationDigest != SkillCostInvocationBinding972(invocation)
            || profileId != bound.ProfileId || profileInputsDigest != bound.ProfileInputsDigest
            || profileXmlDigest != bound.ProfileXmlDigest
            || bound.Leaves.SingleOrDefault(row => row.Field == field) is not { } leaf || leaf.XmlDigest != leafXmlDigest) return false;
        value = leaf.Value; inactive = leaf.Inactive; talentKind = bound.TalentKind;
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-profile-magic3200", bound.AuthorityDigest, bound.InputsDigest, bound.PolicyDigest, bound.PolicyContentDigest,
            bound.ProfileId, bound.ProfileInputsDigest, bound.ProfileXmlDigest, bound.InvocationDigest,
            bound.MagicDigest, bound.TalentKind, bound.ResultJson, Leaf = leaf
        });
        return true;
    }

    // Called only with the returned resolver reports. A valid invocation alone
    // does not account for a leaf; copied, stale and foreign reports default-deny.
    internal void CompleteMagic3200(CharacterCreationCompleteAssessment authority,
        CharacterCreationRuleSourceScopeInvocation invocation, IReadOnlyList<CharacterCreationRuleSourceScopeResult> reports,
        CharacterCreationRuleSourceResolution resolution, CharacterCreationEffectivePolicyContext policy)
    {
        magicEvaluations3200.Clear(); magicInactive3200.Clear();
        if (!magicInvocations3200.TryGetValue(invocation, out var bound)
            || authorityDigest != bound.AuthorityDigest || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            || bound.InvocationDigest != SkillCostInvocationBinding972(invocation) || policy.PolicyDigest != bound.PolicyDigest
            || CharacterCreationFinalizationDigest.Compute(policy) != bound.PolicyContentDigest
            || !invocation.TryGetCompletedConsumer("profile", out var consumer)
            || policy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules && row.SourceId == "profile").ToArray() is not [var source]
            || reports.Where(row => row.Scope == "profile").ToArray() is not [var report]
            || report.SourceDigest != source.ContentDigest || report.ObligationId != $"source:{source.Kind}:{source.Order}"
            || report.ConsumerDigest != consumer || report.Operations.Count == 0
            || report.Operations[0].Disposition != D3200.Structural
            || !report.Operations.Select(row => row.Order).SequenceEqual(Enumerable.Range(0, report.Operations.Count))
            || report.Operations.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() != report.Operations.Count
            || report.Operations.Where(row => row.SourceDigest == bound.ProfileXmlDigest && row.Disposition == D3200.Structural).ToArray() is not [var parent]) return;
        foreach (var leaf in bound.Leaves)
        {
            string path = parent.Path + leaf.Path;
            var operations = report.Operations.Where(row => row.Path == path || row.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
            if (operations is not [var element, var text] || element.Path != path || element.SourceDigest != leaf.XmlDigest
                || text.Path != path + "/text()[0]"
                || !TryReadMagic3200(invocation, bound.ProfileId, bound.ProfileInputsDigest, bound.ProfileXmlDigest,
                    leaf.Field, leaf.XmlDigest, out var value, out bool inactive, out var kind, out var execution)) continue;
            string output = JsonSerializer.Serialize(new
            {
                Operation = "canonical-profile-magic3200", Field = leaf.Field, Input = leaf.Input,
                Result = value, TalentKind = kind, Inactive = inactive, CanonicalExecution = execution
            });
            if (operations.Any(row => row.Disposition != (inactive ? D3200.NotSelected : D3200.Compiled)
                || !CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest) || row.OperationResult718 != output
                || !resolution.HasExecutedProfileSetting1203(invocation, report, row.Order))) continue;
            var obligations = authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Setting
                && row.Consumer == CharacterCreationSettingConsumer.Magic && row.Setting?.Key == leaf.Key).ToArray();
            if (obligations is not [var obligation] || obligation.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent
                || obligation.Setting!.State != CharacterCreationSettingObservationState.Present
                || obligation.Setting.SourceValueDigest != leaf.XmlDigest
                || policy.SettingObservations!.Settings.Count(row => row == obligation.Setting) != 1) continue;
            magicEvaluations3200.Add(obligation.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                BoundAuthorityDigest = bound.AuthorityDigest, obligation.Id, ObligationAuthorityDigest = obligation.AuthorityDigest, execution,
                report.ObligationId, report.SourceDigest, report.ConsumerDigest, Operations = operations, Output = output
            }));
            if (inactive) magicInactive3200.Add(obligation.Id);
        }
    }
}
