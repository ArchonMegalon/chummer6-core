using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private void AddNativeSettingsCapabilities1420(XElement profile, CharacterCreationPrerequisiteDraft draft,
        Action<string, string?, object, object?> add)
    {
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || CharacterCreationNativeSettings1420.Evaluate(XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml,
                LoadOptions.PreserveWhitespace), Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, draft)
                is not { Created: false } native
            || native.ProfileInputsDigest != Inputs.EffectivePolicy!.ProfileInputsDigest
            || native.ProfileXmlDigest != CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting))) return;
        foreach (var definition in CharacterCreationSettingDefinitions.NativeLifecycle1420)
            if (CharacterCreationNativeSettings1420.Value(native, definition.Element) is bool value)
                add(definition.Element, null, native, value);
    }

    private bool MatchesNativeSettingsResult1420(string field, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationSourceOperation> operations)
    {
        if (!CharacterCreationSettingDefinitions.NativeLifecycle1420.Any(row => row.Element == field)) return true;
        var source = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
        if (source is null || !invocation.TryGetExecutedNativeSettings1420(source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(source.CanonicalSourceXml), out var native)
            || CharacterCreationNativeSettings1420.Value(native!, field) is not bool value) return false;
        try
        {
            // Tree records the scalar element and its single XText occurrence.
            // Both diagnostics must retain the same privately executed native result.
            return operations.Count == 2 && operations[1].Path == operations[0].Path + "/text()[0]"
                && operations.All(operation => operation.Disposition == CharacterCreationSourceOperationDisposition.Compiled
                    && JsonSerializer.Deserialize<CharacterCreationNativeSettingsProjection>(operation.OperationResult718!) == native)
                && PlainProfileLeaf1203(XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace), field, null)
                    is { } leaf && ScalarMatches1203(leaf, value);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return false; }
    }
}
