using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Pure combined planning, not a source reader or persistence service.</summary>
public static class CharacterCreationCompletionBudgetService
{
    public static CharacterCreationFoundationResult<CharacterCreationCompletionBudgetPreview> Preview(
        CharacterCreationMagicResonanceCompletionInputs magicInputs,
        CharacterCreationQualitiesCompletionInputs qualitiesInputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationMagicResonanceSelections magicSelections,
        IReadOnlyList<string> qualityOptionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(magicInputs);
        ArgumentNullException.ThrowIfNull(qualitiesInputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(magicSelections);
        ArgumentNullException.ThrowIfNull(qualityOptionIds);
        cancellationToken.ThrowIfCancellationRequested();
        magicInputs = Detach(magicInputs);
        qualitiesInputs = Detach(qualitiesInputs);
        attributeAllocations = attributeAllocations.ToArray();
        magicSelections = Detach(magicSelections);
        qualityOptionIds = qualityOptionIds.ToArray();
        if (magicInputs.Attributes is null || qualitiesInputs.Attributes is null
            || magicInputs.Attributes.Binding != qualitiesInputs.Attributes.Binding
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                magicInputs.Attributes.SnapshotDigest, qualitiesInputs.Attributes.SnapshotDigest)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                magicInputs.PrerequisiteDraft, qualitiesInputs.PrerequisiteDraft))
            return Conflict();

        // Re-evaluate original admitted inputs. Never accept client-provided
        // output budgets, or a fabricated saved draft for an intermediate step.
        var magicResult = CharacterCreationMagicResonanceService.PreviewCompletion(
            magicInputs, attributeAllocations, magicSelections, cancellationToken);
        var qualitiesResult = CharacterCreationQualitiesService.PreviewWithProposedAttributes(
            qualitiesInputs, attributeAllocations, qualityOptionIds, cancellationToken);
        string[] blockers = Normalize(magicResult.Blockers.Concat(qualitiesResult.Blockers));
        if (magicResult.Value is not { } magic || qualitiesResult.Value is not { } qualities)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, blockers);
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(
                magic.Attributes.PreviewDigest, qualities.Attributes.PreviewDigest))
            return Conflict();

        int? available = null;
        if (qualities.Blockers.Count == 0
            && !magic.Blockers.Contains(CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported))
        {
            if (CharacterCreationPriorityKarmaBudget.TryEvaluate(
                    qualities.Qualities.KarmaRemaining, 0, magic.MysticAdeptPowerPoints, out int balance))
                available = balance;
            else
                blockers = Normalize(blockers.Append(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded));
        }
        var preview = new CharacterCreationCompletionBudgetPreview(CharacterCreationCompletionBudgetSchemas.PreviewV1,
            magic, qualities, available, blockers, string.Empty);
        preview = preview with { ProposalDigest = CharacterCreationSkillsDigest.Compute(preview) };
        cancellationToken.ThrowIfCancellationRequested();
        return new(blockers.Length == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, Detach(preview), blockers);
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionBudgetPreview> Conflict()
        => new(CharacterCreationFoundationOutcomes.Conflict, null,
            [CharacterCreationCompletionBudgetSchemas.SnapshotConflict]);

    private static T Detach<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;

    private static string[] Normalize(IEnumerable<string> values)
        => values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
}
