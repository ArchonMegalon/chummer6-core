using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // A source-version decision, NOT a legacy runtime/UI parity receipt. The imported
    // build target checks the complete reviewed loader/model/consumer source files.
    // A digest labels this decision; only BindRetainedCompatibility1430 can produce
    // execution, after the canonical magic, skills and original-aware graph consumers.
    private const string RetainedCompatibilityDecision1430 = "sr5-retained-native-compatibility/1430";
    private const string RetainedCompatibilityLoader1430 = CharacterCreationNativeCompatibility1430.SourceVersion;

    private sealed record RetainedLeaf1430(string Field, string Path, string XmlDigest,
        string SettingId, int Order, string RawValue, string Reason);
    private sealed record BoundRetainedCompatibilityExecution1430(string InvocationDigest, string MagicAuthorityDigest,
        string ProfileDigest, IReadOnlyDictionary<string, RetainedLeaf1430> Leaves,
        IReadOnlyDictionary<string, string> Results, CharacterCreationNativeCompatibilityProjection1430 Native);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundRetainedCompatibilityExecution1430>
        retainedCompatibilityInvocations1430 = new();

    private static string? RetainedCompatibilityReason1430(string field) => field switch
    {
        "nosinglearmorencumbrance" => "Reviewed CharacterSettings retains a private field without an accessor; Character.ArmorEncumbrance reads NoArmorEncumbrance instead. Neither Boolean value changes that dataflow.",
        "extendanydetectionspell" => "SelectSpell permits a custom extension only for Detection without Extended Area; Finish executes the standard-spell applicability branch and preserves the control for later selection. No custom extension or drain is executed.",
        "allowskilldicerolling" => "CharacterSettings omits this profile scalar. GlobalSettings reads its own registry value and DicePoolControl consumes that global value; this retained profile occurrence grants no global/UI authority.",
        "allowtechnomancerschooling" => "Both CharacterCreate and CharacterCareer enable the schooling control from this policy in the RESEnabled branch. The bound uninitiated graph has no grade purchase; a saved grade's Schooling independently controls its cost.",
        _ => null
    };

    private static IReadOnlyList<RetainedLeaf1430> RetainedCompatibilityLeaves1430(XElement profile,
        CharacterCreationSettingsObservation observations)
    {
        var result = new List<RetainedLeaf1430>();
        if (CharacterCreationNativeCompatibility1430.ReadPolicy(profile) is null
            || profile.Name != "setting" || profile.HasAttributes
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
            if (RetainedCompatibilityReason1430(leaf.Name.LocalName) is not { } reason
                || !ReferenceEquals(PlainProfileLeaf1203(profile, leaf.Name.LocalName, null), leaf)
                || !bool.TryParse(leaf.Value, out _)) continue;
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

    private bool TryRetainedCompatibilityBasis1430(ICharacterSourceDataContext context,
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

    private void AddRetainedCompatibilityCapabilities1430(Dictionary<string, ProfileSettingCapability1203> capabilities,
        ICharacterSourceDataContext context, XElement profile)
    {
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || !IsCreationProfile1217(XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml,
                LoadOptions.PreserveWhitespace), Inputs.Skills.PrerequisiteDraft.BuildMethod)
            || !TryRetainedCompatibilityBasis1430(context, out var magic)) return;
        string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        foreach (var leaf in RetainedCompatibilityLeaves1430(profile, Inputs.EffectivePolicy!.SettingObservations!))
            capabilities.Add(leaf.SettingId, new(leaf.Field, leaf.Path, leaf.XmlDigest, profileDigest,
                CharacterCreationFinalizationDigest.Compute(new { Decision = RetainedCompatibilityDecision1430,
                    Loader = RetainedCompatibilityLoader1430, Magic = magic, Leaf = leaf })));
    }

    private void BindRetainedCompatibility1430(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompleteAssessment authority, XElement graph, CharacterCreationAttributeSkillsPreview skills,
        CharacterCreationCompleteMagicExecution? magicExecution, CharacterCreationSelectedEffectsConsumer effects)
    {
        retainedCompatibilityInvocations1430.Remove(invocation);
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || !invocation.HasLifecycleResultV1
            || !MatchesProfileSettingInputs1203() || Inputs.OriginalCharacter is not { } originalCharacter
            || authority is not { Schema: CharacterCreationCompleteAssessment.SchemaV4, Phase: CharacterCreationCompletePhase.Authority }
            || magicExecution?.DigestFor(authority) is not { } completedMagic
            || effects.DigestFor(authority) is not { } completedEffects
            || invocation.GraphDigest != CharacterCreationFinalizationDigest.Compute(
                effects.CopyCompiledGraph().ToString(SaveOptions.DisableFormatting))
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
            || !invocation.TryGetCompletedConsumer("spells", out var completedSpells)
            || !MatchesMagicSelections(request.Selections.MagicSelections, invocation.CompiledEffects)) return;
        using var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var context = (scope ?? resolution.Resolver).TryCreateContext(originalCharacter.RawCharacterXml);
        if (context is null || !TryRetainedCompatibilityBasis1430(context, out var magic)
            || CharacterCreationNativeCompatibility1430.Evaluate(graph, magic!.MysticAdeptPowerPointPolicy,
                Inputs.Skills.PrerequisiteDraft) is not { Created: false } native
            || native.ProfileInputsDigest != Inputs.EffectivePolicy!.ProfileInputsDigest) return;
        var profile = XElement.Parse(magic!.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
        var leaves = RetainedCompatibilityLeaves1430(profile, Inputs.EffectivePolicy!.SettingObservations!)
            .ToDictionary(leaf => leaf.Path, StringComparer.Ordinal);
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var leaf in leaves.Values)
            results.Add(leaf.Path, JsonSerializer.Serialize(new
            {
                // Preserve the v1430 field name and compatibility identity. This
                // labels the four-policy decision, not ReviewedSourceFingerprint.
                Operation = RetainedCompatibilityDecision1430, LoaderSha256 = RetainedCompatibilityLoader1430,
                LoaderDataflow = "Load/LoadAsync: explicit named scalar reads; no generic settings property bag",
                Native = native, CompletedEffects = completedEffects,
                leaf.Field, leaf.Path, leaf.SettingId, leaf.Order, leaf.XmlDigest, leaf.RawValue, leaf.Reason,
                Profile = profileDigest, MagicAuthority = CharacterCreationFinalizationDigest.Compute(magic),
                CompletedMagic = completedMagic, CompletedSpellSources = completedSpells,
                CompletedSkills = CharacterCreationFinalizationDigest.Compute(skills),
                // CompletedMagic binds the actual full/selected result, original selected
                // source, attributes, form budget and selections. It cannot be deserialized.
                Inputs.SnapshotDigest, Original = Inputs.Skills.Binding.RawCharacterXmlDigest,
                originalCharacter.WorkspaceId, originalCharacter.ContentRevision, originalCharacter.SavedRevision,
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest
            }));
        retainedCompatibilityInvocations1430.Add(invocation, new(RetainedCompatibilityInvocationDigest1430(invocation),
            CharacterCreationFinalizationDigest.Compute(magic), profileDigest, leaves, results, native));
    }

    internal static bool TryReadRetainedCompatibility1430(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationMagicResonanceAuthority magic, string profileDigest, string relativePath,
        string field, string leafDigest, string rawValue, out string? result, out string? execution)
    {
        result = null; execution = null;
        if (!retainedCompatibilityInvocations1430.TryGetValue(invocation, out var bound)
            || bound.InvocationDigest != RetainedCompatibilityInvocationDigest1430(invocation)
            || bound.MagicAuthorityDigest != CharacterCreationFinalizationDigest.Compute(magic)
            || bound.ProfileDigest != profileDigest || !bound.Leaves.TryGetValue(relativePath, out var leaf)
            || leaf.Field != field || leaf.XmlDigest != leafDigest || leaf.RawValue != rawValue
            || !bound.Results.TryGetValue(relativePath, out var value)) return false;
        result = value;
        execution = CharacterCreationFinalizationDigest.Compute(value);
        return true;
    }

    internal static bool TryReadNativeCompatibility1430(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationMagicResonanceAuthority magic, string profileDigest,
        out CharacterCreationNativeCompatibilityProjection1430? native)
    {
        native = null;
        if (!retainedCompatibilityInvocations1430.TryGetValue(invocation, out var bound)
            || bound.InvocationDigest != RetainedCompatibilityInvocationDigest1430(invocation)
            || bound.MagicAuthorityDigest != CharacterCreationFinalizationDigest.Compute(magic)
            || bound.ProfileDigest != profileDigest) return false;
        native = bound.Native;
        return true;
    }

    private bool MatchesRetainedCompatibilityResult1430(string field,
        CharacterCreationRuleSourceScopeInvocation invocation, IReadOnlyList<CharacterCreationSourceOperation> operations)
    {
        if (RetainedCompatibilityReason1430(field) is null) return true;
        var magic = Inputs.Magic.Authority;
        var profile = XElement.Parse(magic.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        if (PlainProfileLeaf1203(profile, field, null) is not { } leaf
            || !TryReadRetainedCompatibility1430(invocation, magic,
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                RelativePath1203(profile, leaf), field,
                CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)), leaf.Value,
                out var result, out var execution)) return false;
        try
        {
            foreach (var operation in operations)
            {
                using var document = JsonDocument.Parse(operation.OperationResult718!);
                var root = document.RootElement;
                if (root.GetProperty("Operation").GetString() != "source-grounded-retained-native-compatibility1430"
                    || root.GetProperty("Result").GetString() != result
                    || root.GetProperty("Execution").GetString() != execution) return false;
            }
            return operations.Count > 0;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }

    private static string RetainedCompatibilityInvocationDigest1430(CharacterCreationRuleSourceScopeInvocation invocation)
    {
        invocation.TryGetCompletedConsumer("spells", out var spells);
        return CharacterCreationFinalizationDigest.Compute(new
        {
            Invocation = CarryoverInvocationDigest995(invocation), Spells = spells, invocation.HasLifecycleResultV1
        });
    }
}
