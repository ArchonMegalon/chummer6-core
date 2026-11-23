using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // A source-version decision, NOT a legacy runtime/UI parity receipt. The imported
    // build target checks the complete reviewed loader/model/consumer source files.
    // A digest labels this decision; only BindRetainedSr5Compatibility1241 can produce
    // execution, after the canonical magic, skills and original-aware graph consumers.
    private const string RetainedSr5Decision1241 = "sr5-retained-character-settings-complex-form-compatibility/1241";
    private const string RetainedSr5Loader1241 = "5fae3d58aa0b0c30920bc4180430ab56250521e5b8db21097b0b9460f74ef943";

    private sealed record RetainedLeaf1241(string Field, string Path, string XmlDigest,
        string SettingId, int Order, string RawValue, string Reason);
    private sealed record BoundRetainedSr5Execution1241(string InvocationDigest, string MagicAuthorityDigest,
        string ProfileDigest, IReadOnlyDictionary<string, RetainedLeaf1241> Leaves,
        IReadOnlyDictionary<string, string> Results);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundRetainedSr5Execution1241>
        retainedSr5Invocations1241 = new();

    private static string? RetainedSr5Reason1241(string field) => field switch
    {
        "karmaimprovecomplexform" => "Both SR5 loaders assign only KarmaNewComplexForm for forms; the retained form model has no purchased rating or improve-price property.",
        "karmacomplexformoption" => "Both SR5 loaders omit this scalar; Extra is retained selected bonus data and is not a price input to ComplexFormKarmaCost.",
        "karmacomplexformskillsoft" => "Both SR5 loaders omit this scalar; UseSkill/Software, skill specialization and Threading improvements remain the dice-pool inputs.",
        _ => null
    };

    private static IReadOnlyList<RetainedLeaf1241> RetainedSr5Leaves1241(XElement profile,
        CharacterCreationSettingsObservation observations)
    {
        var result = new List<RetainedLeaf1241>();
        if (profile.Name != "setting" || profile.HasAttributes
            || !CharacterCreationSettingsObservation.TryCapture(profile.ToString(SaveOptions.DisableFormatting),
                observations.ProfileInputsDigest, observations.Schema, out var fresh)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(fresh, observations)) return result;
        // Exactly the existing observation traversal: no filtering, reordering, default
        // substitution or collapse of repeated XML before the occurrence is identified.
        string[] represented = ["id", "name", "buildmethod", "books", "customdatadirectorynames",
            "maxskillratingcreate", "maxskillrating", "maxknowledgeskillratingcreate", "maxknowledgeskillrating"];
        var leaves = profile.Elements().Where(node => node.Name.NamespaceName.Length != 0
            || !represented.Contains(node.Name.LocalName, StringComparer.Ordinal))
            .SelectMany(node => node.DescendantsAndSelf().Where(child => !child.HasElements)).ToArray();
        // The observation appends absent typed definitions after all XML leaves.
        if (leaves.Length > fresh!.Settings.Count) return result;
        for (int order = 0; order < leaves.Length; order++)
        {
            var leaf = leaves[order];
            if (RetainedSr5Reason1241(leaf.Name.LocalName) is not { } reason
                || !ReferenceEquals(PlainProfileLeaf1203(profile, leaf.Name.LocalName, "karmacost"), leaf)
                || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) continue;
            var observation = fresh.Settings[order];
            string digest = CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting));
            if (observation.Key != CharacterCreationObservedSetting.Opaque
                || observation.State != CharacterCreationSettingObservationState.Opaque
                || observation.SourceValueDigest != digest) continue;
            result.Add(new(leaf.Name.LocalName, RelativePath1203(profile, leaf), digest,
                observation.SettingId, order, leaf.Value, reason));
        }
        return result;
    }

    private bool TryRetainedSr5Basis1241(ICharacterSourceDataContext context,
        out CharacterCreationMagicResonanceAuthority? magic)
    {
        magic = null;
        if (!MatchesProfileSettingInputs1203()
            || Inputs.Skills.PrerequisiteDraft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || !context.TryResolveCreationMagicResonanceAuthority(out var resolved) || !resolved.IsAuthoritative
            || resolved.SettingsProfileId != Inputs.EffectivePolicy!.SettingsProfileId
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(resolved, Inputs.Magic.Authority)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(Inputs.EffectivePolicy,
                resolved.MysticAdeptPowerPointPolicy)) return false;
        magic = resolved;
        return true;
    }

    private void AddRetainedSr5Capabilities1241(Dictionary<string, ProfileSettingCapability1203> capabilities,
        ICharacterSourceDataContext context, XElement profile)
    {
        if (!TryRetainedSr5Basis1241(context, out var magic)) return;
        string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        foreach (var leaf in RetainedSr5Leaves1241(profile, Inputs.EffectivePolicy!.SettingObservations!))
            capabilities.Add(leaf.SettingId, new(leaf.Field, leaf.Path, leaf.XmlDigest, profileDigest,
                CharacterCreationFinalizationDigest.Compute(new { Decision = RetainedSr5Decision1241,
                    Loader = RetainedSr5Loader1241, Magic = magic, Leaf = leaf })));
    }

    private void BindRetainedSr5Compatibility1241(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, XElement graph, CharacterCreationAttributeSkillsPreview skills,
        CharacterCreationCompleteMagicExecution? magicExecution)
    {
        retainedSr5Invocations1241.Remove(invocation);
        if (!MatchesProfileSettingInputs1203() || Inputs.OriginalCharacter is not { } originalCharacter
            || authority is not { Schema: CharacterCreationCompleteAssessment.SchemaV4, Phase: CharacterCreationCompletePhase.Authority }
            || magicExecution?.DigestFor(authority) is not { } completedMagic
            || authority.InputsSnapshotDigest != Inputs.SnapshotDigest || authority.RequestDigest != requestDigest
            || authority.SelectionsDigest != CharacterCreationFinalizationDigest.Compute(request.Selections)
            || authority.RawCharacterXmlDigest != Inputs.Skills.Binding.RawCharacterXmlDigest
            || authority.PolicyDigest != Inputs.EffectivePolicy!.PolicyDigest
            || invocation.PolicyDigest != authority.PolicyDigest || invocation.RequestDigest != requestDigest
            || invocation.CaptureDigest != Inputs.RuleSourceCaptureDigest
            || invocation.WorkspaceId718 != originalCharacter.WorkspaceId
            || invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting))
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(invocation.CreationDraft718, Inputs.Skills.PrerequisiteDraft)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(invocation.Skills718, skills)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(invocation.Attributes718, skills.Attributes)
            || !invocation.TryGetCompletedConsumer("profile", out _)
            || !invocation.TryGetCompletedConsumer("complexforms", out var completedForms)
            || !MatchesMagicSelections(request.Selections.MagicSelections, invocation.CompiledEffects)) return;
        using var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var context = (scope ?? resolution.Resolver).TryCreateContext(originalCharacter.RawCharacterXml);
        if (context is null || !TryRetainedSr5Basis1241(context, out var magic)) return;
        var profile = XElement.Parse(magic!.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        var leaves = RetainedSr5Leaves1241(profile, Inputs.EffectivePolicy!.SettingObservations!)
            .ToDictionary(leaf => leaf.Path, StringComparer.Ordinal);
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var leaf in leaves.Values)
            results.Add(leaf.Path, JsonSerializer.Serialize(new
            {
                Operation = RetainedSr5Decision1241, LoaderSha256 = RetainedSr5Loader1241,
                LoaderDataflow = "Load/LoadAsync: explicit named scalar reads; no generic karmacost traversal or XML-property bag",
                leaf.Field, leaf.Path, leaf.SettingId, leaf.Order, leaf.XmlDigest, leaf.RawValue, leaf.Reason,
                Profile = profileDigest, MagicAuthority = CharacterCreationFinalizationDigest.Compute(magic),
                CompletedMagic = completedMagic, CompletedFormSources = completedForms,
                CompletedSkills = CharacterCreationFinalizationDigest.Compute(skills),
                // CompletedMagic binds the actual full/selected result, original selected
                // source, attributes, form budget and selections. It cannot be deserialized.
                Inputs.SnapshotDigest, Original = Inputs.Skills.Binding.RawCharacterXmlDigest,
                originalCharacter.WorkspaceId, originalCharacter.ContentRevision, originalCharacter.SavedRevision,
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest
            }));
        retainedSr5Invocations1241.Add(invocation, new(RetainedSr5InvocationDigest1241(invocation),
            CharacterCreationFinalizationDigest.Compute(magic), profileDigest, leaves, results));
    }

    internal static bool TryReadRetainedSr5Compatibility1241(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationMagicResonanceAuthority magic, string profileDigest, string relativePath,
        string field, string leafDigest, string rawValue, out string? result, out string? execution)
    {
        result = null; execution = null;
        if (!retainedSr5Invocations1241.TryGetValue(invocation, out var bound)
            || bound.InvocationDigest != RetainedSr5InvocationDigest1241(invocation)
            || bound.MagicAuthorityDigest != CharacterCreationFinalizationDigest.Compute(magic)
            || bound.ProfileDigest != profileDigest || !bound.Leaves.TryGetValue(relativePath, out var leaf)
            || leaf.Field != field || leaf.XmlDigest != leafDigest || leaf.RawValue != rawValue
            || !bound.Results.TryGetValue(relativePath, out var value)) return false;
        result = value;
        execution = CharacterCreationFinalizationDigest.Compute(value);
        return true;
    }

    private static string RetainedSr5InvocationDigest1241(CharacterCreationRuleSourceScopeInvocation invocation)
    {
        invocation.TryGetCompletedConsumer("complexforms", out var forms);
        return CharacterCreationFinalizationDigest.Compute(new
        {
            Invocation = CarryoverInvocationDigest995(invocation), Forms = forms
        });
    }
}
