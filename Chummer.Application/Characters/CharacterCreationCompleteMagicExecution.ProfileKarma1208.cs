using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteMagicExecution
{
    // Set only by the existing private constructor, after full or selected replay.
    // Keep detached bytes, not a mutable/public result as an execution capability.
    private string? karmaSource1208;
    private string? karmaResult1208;
    private string? profileMagicSource3200;

    private void RetainKarma1208(CharacterCreationSelectedMagicAuthority source,
        CharacterCreationSelectedMagicEvaluation result)
    {
        karmaSource1208 = JsonSerializer.Serialize(source.MysticAdeptPowerPointPolicy);
        karmaResult1208 = JsonSerializer.Serialize(result);
        profileMagicSource3200 = JsonSerializer.Serialize(source);
    }

    // Both entry points reach RetainKarma1208 only after genuine full/selected
    // execution. Talent identity is retained with that execution, never inferred
    // from a zero price, an empty request or the compiled graph's flags.
    internal bool TryReadProfileMagic3200(CharacterCreationCompleteAssessment authority,
        CharacterCreationMagicResonanceCompletionInputs inputs,
        CharacterCreationMagicResonanceSelections selections,
        out CharacterCreationSelectedMagicEvaluation? result)
    {
        result = null;
        if (DigestFor(authority) is null || profileMagicSource3200 is null || karmaResult1208 is null
            || JsonSerializer.Deserialize<CharacterCreationSelectedMagicAuthority>(profileMagicSource3200) is not { } source
            || source.OriginalInputsSnapshotDigest != inputs.SnapshotDigest
            || source.ProvenanceAuthorityDigest != inputs.Authority.AuthorityDigest
            || source.SettingsProfileId != inputs.Authority.SettingsProfileId
            || source.SourceInputsDigest != inputs.Authority.SourceInputsDigest
            || source.CustomDataInputsDigest != inputs.Authority.CustomDataInputsDigest
            || source.GmPolicyDigest != inputs.Authority.GmPolicyDigest || source.RuntimeDigest != inputs.Authority.RuntimeDigest
            || source.PrerequisiteAuthorityDigest != inputs.Authority.PrerequisiteAuthorityDigest
            || source.PrerequisiteDraftRevision != inputs.PrerequisiteDraft.DraftRevision
            || source.PrerequisiteDraftDigest != inputs.PrerequisiteDraft.DraftDigest
            || source.OriginalAttributesDigest != CharacterCreationSelectedMagicRules.ComputeAttributesDigest(inputs.Attributes.Attributes)
            || !Same(source.Talent, inputs.SelectedTalent)
            || !Same(source.MysticAdeptPowerPointPolicy, inputs.Authority.MysticAdeptPowerPointPolicy)
            || !Same(source.ProposedSelections, NormalizeMagic(selections))
            || JsonSerializer.Deserialize<CharacterCreationSelectedMagicEvaluation>(karmaResult1208)
                is not { Blockers.Count: 0 } completed) return false;
        result = completed;
        return true;
    }

    internal bool TryExecutePriorityKarma1208(CharacterCreationCompleteAssessment authority,
        CharacterCreationMysticAdeptPowerPointPolicy source, string method, string field,
        int configuredPrice, out string? result)
    {
        result = null;
        if (DigestFor(authority) is not { } execution || configuredPrice < 0
            || method is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || karmaSource1208 != JsonSerializer.Serialize(source) || karmaResult1208 is null
            || JsonSerializer.Deserialize<CharacterCreationSelectedMagicEvaluation>(karmaResult1208)
                is not { Blockers.Count: 0 } magic) return false;
        var budget = field switch
        {
            "karmaspell" => magic.SpellBudget,
            "karmanewcomplexform" => magic.ComplexFormBudget,
            _ => null
        };
        if (budget is null || budget.Blockers.Count != 0 || budget.Total < 0 || budget.Used < 0
            || budget.Used > budget.Total || budget.Remaining != budget.Total - budget.Used) return false;
        // Canonical Priority/Sum-to-Ten completion has already executed source
        // selection, talent access, grants, exchange and the entire slot budget.
        // Purchased overflow is not admitted by that consumer. Never infer this
        // branch from the character's metatype, talent name or empty selections.
        result = JsonSerializer.Serialize(new
        {
            Operation = "completed-priority-magic-allocation", BuildMethod = method, Field = field,
            ConfiguredPurchasePrice = configuredPrice, MagicExecution = execution,
            Source = source, Magic = magic, Budget = budget,
            PurchaseBranchSelected = false
        });
        return true;
    }
}
