using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private void AddNativeLoadoutCapabilities1432(XElement profile, CharacterCreationPrerequisiteDraft draft,
        Action<string, string?, object, object?> add)
    {
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || EvaluateOriginalNativeLoadout3478(draft)
                is not { Created: false } native
            || native.ProfileInputsDigest != Inputs.EffectivePolicy!.ProfileInputsDigest
            || native.ProfileXmlDigest != CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting))) return;
        foreach (var definition in CharacterCreationNativeLoadoutDefinitions1432.All)
            if (native.Supports(definition.Element))
                add(definition.Element, null, native, native.Policy.Value(definition.Element));
    }

    private bool MatchesNativeLoadoutResult1432(string field, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationSourceOperation> operations)
    {
        if (!CharacterCreationNativeLoadoutDefinitions1432.All.Any(row => row.Element == field)) return true;
        var source = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
        if (source is null || !invocation.TryGetExecutedNativeLoadout1432(source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(source.CanonicalSourceXml), out var native)
            || !native!.Supports(field)) return false;
        try
        {
            // Tree records the scalar element and its single XText occurrence.
            // Both must retain the exact privately executed loadout result.
            return operations.Count == 2 && operations[1].Path == operations[0].Path + "/text()[0]"
                && operations.All(operation => operation.Disposition == CharacterCreationSourceOperationDisposition.Compiled
                    && JsonSerializer.Deserialize<CharacterCreationNativeLoadoutProjection1432>(operation.OperationResult718!) == native)
                && PlainProfileLeaf1203(XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace), field, null)
                    is { } leaf && ScalarMatches1203(leaf, native.Policy.Value(field)!);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return false; }
    }
}
