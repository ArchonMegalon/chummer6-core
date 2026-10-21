using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Selects history, never rule authority. Every caller must price the returned
/// choices using its current source observation. No archive becomes a resolver.
/// </summary>
internal static class CharacterCreationCompositeBaselineReader
{
    internal const string Invalid = "creation-composite-baseline-invalid";
    internal const string RebaseRequired = "creation-composite-stage-rebase-required";

    internal static bool TryRead(WorkspaceStoredDocument workspace,
        out CharacterCreationCompositeBaseline? projection)
    {
        projection = null;
        var state = workspace.Document.AuxiliaryState;
        if (state.CharacterCreationCompletionDrafts is not { } entries) return true;
        if (entries.Count == 0
            || !CharacterCreationCompletionDraftTransaction.IsValidCurrentDocument(workspace.Document, workspace.ContentRevision)
            || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(
                workspace.Id, workspace.ContentRevision, state)) return false;
        var last = entries[^1];
        var choices = last.Baseline.Review.Review.Request.Selections;
        // This is actual later ordinary history, not a fabricated stage commit.
        if (state.CharacterCreationAttributesDraft is { } attributes
            && attributes.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with
            {
                AttributeAllocations = attributes.Allocations,
                PlayerChosenAttributeIds = choices.PlayerChosenAttributeIds is null ? null
                    : attributes.PlayerChosenAttributeIds ?? choices.PlayerChosenAttributeIds
            };
        if (state.CharacterCreationSkillsDraft is { } skills
            && skills.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with
            {
                SkillAllocations = skills.Allocations,
                GroupAllocations = skills.GroupAllocations
            };
        if (state.CharacterCreationMagicResonanceDraft is { } magic
            && magic.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with { MagicSelections = magic.Selections };
        if (state.CharacterCreationQualitiesDraft is { } qualities
            && qualities.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with { QualityOptionIds = qualities.SelectedOptionIds };
        if (state.CharacterCreationResourcesDraft is { } resources
            && resources.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with { TotalKarmaInvestment = resources.KarmaInvestment };
        if (state.CharacterCreationGearDraft is { } gear
            && gear.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with { Gear = gear.Lines.Select(line =>
                new CharacterCreationGearSelection(line.OptionId, line.Quantity)).ToArray() };
        if (state.CharacterCreationLoadoutDraft is { } loadout
            && loadout.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with { Loadout = loadout.Selections };
        if (state.CharacterCreationContactsDraft is { } contacts
            && contacts.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with { Contacts = contacts.Contacts };
        if (state.CharacterCreationLifestylesDraft is { } lifestyles
            && lifestyles.BaseContentRevision >= last.Receipt.ContentRevision)
            choices = choices with
            {
                Lifestyles = lifestyles.Lifestyles,
                StartingLifestyleId = ResolveStartingLifestyle(choices.StartingLifestyleId, lifestyles.Lifestyles)
            };
        projection = new(last.Baseline.BaselineDigest, last.Receipt.ContentRevision,
            choices);
        projection = JsonSerializer.Deserialize<CharacterCreationCompositeBaseline>(JsonSerializer.Serialize(projection))!;
        return true;
    }

    // A current input is not the historical draft's input. The fully replayed
    // composite origin proves the retained evidence; today's rules price its
    // exact selections separately. No historical digest is replaced or resealed.
    internal static bool RetainsContacts(WorkspaceStoredDocument workspace,
        CharacterCreationContactsDraft draft)
        => TryRead(workspace, out var baseline) && baseline is not null
            && workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1]
                .Baseline.OriginalState is { } origin
            && (Same(draft, origin.CharacterCreationContactsDraft)
                || Same(draft, origin.CharacterCreationLifestylesDraft?.InputAuthority)
            || workspace.Document.AuxiliaryState.CharacterCreationContactsDraft is { } current
                && current.BaseContentRevision >= baseline.ContentRevision && Same(draft, current)
            || workspace.Document.AuxiliaryState.CharacterCreationLifestylesDraft is { } lifestyle
                && lifestyle.BaseContentRevision >= baseline.ContentRevision && Same(draft, lifestyle.InputAuthority));

    internal static bool RetainsLifestyles(WorkspaceStoredDocument workspace,
        CharacterCreationLifestylesDraft draft)
        => TryRead(workspace, out var baseline) && baseline is not null
            && (Same(draft, workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1]
                .Baseline.OriginalState.CharacterCreationLifestylesDraft)
                || workspace.Document.AuxiliaryState.CharacterCreationLifestylesDraft is { } current
                    && current.BaseContentRevision >= baseline.ContentRevision && Same(draft, current));

    internal static Guid? ResolveStartingLifestyle(Guid? current,
        IReadOnlyList<CharacterCreationLifestyleConfiguration> lifestyles)
        => current is Guid selected && lifestyles.Any(row => row.LifestyleId == selected) ? selected
            : lifestyles.OrderBy(row => row.LifestyleId).Select(row => (Guid?)row.LifestyleId).FirstOrDefault();

    internal static bool TryReadRetainedMagic(WorkspaceStoredDocument workspace,
        out WorkspaceStoredDocument? original)
        => TryReadRetainedOrigin(workspace, true, out original);

    internal static bool TryReadRetainedQualities(WorkspaceStoredDocument workspace,
        out WorkspaceStoredDocument? original)
        => TryReadRetainedOrigin(workspace, false, out original);

    private static bool TryReadRetainedOrigin(WorkspaceStoredDocument workspace, bool magic,
        out WorkspaceStoredDocument? original)
    {
        original = null;
        if (!TryRead(workspace, out var baseline) || baseline is null) return false;
        var entries = workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        var entry = entries[^1];
        var origin = entry.Baseline.OriginalState;
        var state = workspace.Document.AuxiliaryState;
        if (magic
            ? state.CharacterCreationMagicResonanceDraft is null
                || !Same(state.CharacterCreationMagicResonanceDraft, origin.CharacterCreationMagicResonanceDraft)
                || !Same(state.CharacterCreationMagicResonanceReceipts, origin.CharacterCreationMagicResonanceReceipts)
            : state.CharacterCreationQualitiesDraft is null
                || !Same(state.CharacterCreationQualitiesDraft, origin.CharacterCreationQualitiesDraft)
                || !Same(state.CharacterCreationQualitiesReceipts, origin.CharacterCreationQualitiesReceipts))
            return false;
        // Reconstruct exactly the admitted origin, including its earlier composite
        // prefix. Loading it with live sources admits the original Attributes and
        // receipts; it does not manufacture a current draft or reseal history.
        var document = workspace.Document with
        {
            State = workspace.Document.State with { AuxiliaryState = origin with
            { CharacterCreationCompletionDrafts = entries.Count == 1 ? null : entries.Take(entries.Count - 1).ToArray() } }
        };
        original = new(workspace.Id, document, entry.Receipt.PreviousContentRevision,
            entry.Receipt.PreviousSavedRevision, workspace.LastUpdatedUtc);
        return true;
    }

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);

    internal static bool MatchesSkillsSource(WorkspaceStoredDocument workspace,
        CharacterCreationSkillsAuthority authority, CharacterCreationPrerequisiteDraft prerequisite)
    {
        var entry = workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1];
        return CharacterCreationCompletionReplayCodec.TryDecode(entry.Baseline.SelectedReplay, out _, out var historical)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(historical.Skills.Authority, authority)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(historical.Skills.PrerequisiteDraft, prerequisite);
    }

}
