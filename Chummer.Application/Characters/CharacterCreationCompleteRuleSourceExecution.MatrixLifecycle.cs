using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private void AddMatrixLifecycleCapabilities(XElement profile, CharacterCreationPrerequisiteDraft draft,
        Action<string, string?, object, object?> add)
    {
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || !IsCreationProfile1217(XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml,
                LoadOptions.PreserveWhitespace), draft.BuildMethod)
            || !CharacterCreationMatrixInitiative.TryPolicy(profile, out var policy)) return;
        // CandidateDependent only: native graph and exact source execution must
        // still succeed. A parsed policy is never an evaluation receipt.
        foreach (var definition in CharacterCreationSettingDefinitions.MatrixLifecycle)
            add(definition.Element, null, policy!, policy!.Value(definition.Key));
    }

    private bool MatchesMatrixLifecycleResult(string field, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationSourceOperation> operations)
    {
        var definition = CharacterCreationSettingDefinitions.MatrixLifecycle.SingleOrDefault(row => row.Element == field);
        if (definition is null) return true;
        var source = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
        if (source is null || !invocation.TryGetExecutedMatrixInitiative(source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(source.CanonicalSourceXml), out var native)) return false;
        try
        {
            // Tree emits both the scalar element and its single XText occurrence.
            // Both operations must carry the same privately executed result.
            return operations.Count == 2 && operations[1].Path == operations[0].Path + "/text()[0]"
                && operations.All(operation => operation.Disposition == CharacterCreationSourceOperationDisposition.Compiled
                    && JsonSerializer.Deserialize<CharacterMatrixInitiativeDiceProjection>(operation.OperationResult718!) == native)
                && PlainProfileLeaf1203(XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace), field, null)
                    is { } leaf && ScalarMatches1203(leaf, native!.Policy.Value(definition.Key));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return false; }
    }
}
