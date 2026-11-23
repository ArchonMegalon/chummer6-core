using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private void AddNativeContactSpecialtyCapabilities(XElement profile, ICharacterSourceDataContext context,
        CharacterCreationPrerequisiteDraft draft, Action<string, string?, object, object?> add)
    {
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || !IsCreationProfile1217(XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml, LoadOptions.PreserveWhitespace), draft.BuildMethod)
            || CharacterCreationNativeContactSpecialty.ReadPolicy(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, draft) is not { } policy
            || policy.ProfileInputsDigest != Inputs.EffectivePolicy!.ProfileInputsDigest
            || policy.ProfileXmlDigest != CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting))) return;
        foreach (var definition in CharacterCreationNativeContactSpecialtyDefinitions.All)
        {
            if (policy.Value(definition.Element) is not bool value) continue;
            if (definition.Key == CharacterCreationObservedSetting.AllowPointBuySpecializationsOnKarmaSkills
                && Inputs.Skills.KarmaPurchasePolicy is { } boundSkills
                && (!context.TryResolveCreationPrioritySkillKarmaPolicy(out var skills) || skills is null
                    || skills.SettingsProfileId != policy.SettingsProfileId || skills.RawProfileInputsDigest != policy.ProfileInputsDigest
                    || skills.AllowPointBuySpecializationsOnKarmaSkills != value
                    || skills.SpecializationsBreakSkillGroups != policy.SpecializationsBreakSkillGroups
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(skills, boundSkills))) continue;
            add(definition.Element, null, policy, value);
        }
    }

    private bool MatchesNativeContactSpecialtyResult(string field, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationSourceOperation> operations)
    {
        if (!CharacterCreationNativeContactSpecialtyDefinitions.All.Any(row => row.Element == field)) return true;
        var source = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
        if (source is null || !invocation.TryGetExecutedNativeContactSpecialty(source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(source.CanonicalSourceXml), out var native)
            || native!.Policy.Value(field) is not bool value) return false;
        try
        {
            // Tree emits both the scalar element and its single XText occurrence.
            // Both operations must carry the same privately executed result.
            return operations.Count == 2 && operations[1].Path == operations[0].Path + "/text()[0]"
                && operations.All(operation => operation.Disposition == CharacterCreationSourceOperationDisposition.Compiled
                    && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                        JsonSerializer.Deserialize<CharacterCreationNativeContactSpecialtyProjection>(operation.OperationResult718!), native))
                && PlainProfileLeaf1203(XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace), field, null) is { } leaf
                && ScalarMatches1203(leaf, value);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return false; }
    }
}
