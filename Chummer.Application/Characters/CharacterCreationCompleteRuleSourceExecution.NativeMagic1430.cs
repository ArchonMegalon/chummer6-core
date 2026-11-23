using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private void AddNativeMagicCapabilities1430(XElement profile, CharacterCreationPrerequisiteDraft draft,
        Action<string, string?, object, object?> add)
    {
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || !IsCreationProfile1217(XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml, LoadOptions.PreserveWhitespace), draft.BuildMethod)
            || CharacterCreationNativeMagic1430.ReadPolicy(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, draft) is not { } policy
            || policy.ProfileInputsDigest != Inputs.EffectivePolicy!.ProfileInputsDigest
            || policy.ProfileXmlDigest != CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting))) return;
        foreach (var definition in CharacterCreationNativeMagicDefinitions1430.All)
            if (policy.Value(definition.Element) is { } value) add(definition.Element, null, policy, value);
    }

    private bool MatchesNativeMagicResult1430(string field, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationSourceOperation> operations)
    {
        if (!CharacterCreationNativeMagicDefinitions1430.All.Any(row => row.Element == field)) return true;
        var source = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
        if (source is null || !invocation.TryGetExecutedNativeMagic1430(source.SettingsProfileId,
                CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
                CharacterCreationFinalizationDigest.Compute(source.CanonicalSourceXml), out var actual)
            || actual!.Policy.Value(field) is not { } value) return false;
        try
        {
            // Tree emits both the scalar element and its single XText occurrence.
            // Both operations must carry the same privately executed result.
            return operations.Count == 2 && operations[1].Path == operations[0].Path + "/text()[0]"
                && operations.All(operation => operation.Disposition == CharacterCreationSourceOperationDisposition.Compiled
                    && JsonSerializer.Deserialize<CharacterCreationNativeMagicProjection1430>(operation.OperationResult718!) == actual)
                && PlainProfileLeaf1203(XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace), field, null) is { } leaf
                && ScalarMatches1203(leaf, value);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return false; }
    }
}
