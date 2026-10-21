using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Bounded typed loadout observation and pure pricing. No store writes,
/// purchase grants, synthetic draft history, or caller-supplied price overrides.</summary>
public static class CharacterCreationLoadoutCompletionService
{
    internal static CharacterCreationFoundationResult<CharacterCreationLoadoutCompletionInputs> LoadFromObservation(
        WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver, bool requireAuthority = false)
    {
        try
        {
            XElement? root = XDocument.Parse(workspace.Document.Content, LoadOptions.None).Root;
            if (root?.Name != "character") return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.RequestInvalid);
            if (workspace.Document.RulesetId != "sr5" || bool.TryParse(root.Element("created")?.Value, out bool created) && created)
                return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.RequestInvalid);
            // Existing imported/purchased graphs are not safely representable by
            // this acquisition tranche. Never replace or silently omit their cost.
            foreach (string name in new[] { "weapons", "armors" })
            {
                XElement[] containers = root.Elements(name).ToArray();
                if (containers.Length > 1 || containers.Any(value => value.HasElements
                        || value.HasAttributes || !string.IsNullOrWhiteSpace(value.Value)))
                    return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.ExistingGraphUnsupported);
            }
            var context = resolver.TryCreateContext(workspace.Document.Content);
            if (context is null || !context.TryResolveCreationLoadoutAuthority(out var authority)
                || !CharacterCreationLoadoutRules.IsValidAuthority(authority))
                return requireAuthority ? RejectedInputs(CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable)
                    : new(CharacterCreationFoundationOutcomes.Success, null, []);
            var binding = new CharacterCreationLoadoutBinding(workspace.Id, workspace.ContentRevision,
                workspace.SavedRevision, CharacterCreationFinalizationDigest.ComputeUtf8(workspace.Document.Content),
                workspace.Document.AuxiliaryStateDigest);
            var state = workspace.Document.AuxiliaryState;
            var draft = state.CharacterCreationLoadoutDraft;
            if (draft?.CompositePurchase is not null
                && !CharacterCreationCompositePurchaseEdit.IsHistorical(workspace, draft.BaseContentRevision, draft.CompositePurchase,
                    draft.ResourcesDraftRevision, draft.ResourcesDraftDigest))
                return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
            if (!CharacterCreationLoadoutReceiptLedgerIntegrity.HasFunding(draft, state.CharacterCreationResourcesDraft,
                    state.CharacterCreationResourcesReceipts))
                return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
            var inputs = new CharacterCreationLoadoutCompletionInputs(CharacterCreationLoadoutCompletionSchemas.InputsV1,
                binding, authority, draft?.Selections ?? CharacterCreationLoadoutSelections.Empty, string.Empty)
            { PendingDraft = draft, Receipts = state.CharacterCreationLoadoutReceipts };
            inputs = inputs with { SnapshotDigest = CharacterCreationLoadoutCompletionRules.ComputeInputsDigest(inputs) };
            if (!IsValidInputs(inputs) || !context.TryResolveCreationLoadoutAuthority(out var after)
                || after.AuthorityDigest != authority.AuthorityDigest)
                return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
            return new(CharacterCreationFoundationOutcomes.Success, Detach(inputs), []);
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or ArgumentException or OverflowException)
        { return RejectedInputs(CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable); }
    }

    public static CharacterCreationFoundationResult<CharacterCreationLoadoutCompletionProjection> Preview(
        CharacterCreationLoadoutCompletionInputs inputs, CharacterCreationLoadoutSelections selections)
    {
        if (!IsValidInputs(inputs) || !IsValidSelections(selections))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationLoadoutCompletionSchemas.RequestInvalid]);
        inputs = Detach(inputs);
        selections = Detach(selections);
        if (!CharacterCreationLoadoutReceiptLedgerIntegrity.Preserves(inputs.OriginalSelections, selections))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationPurchaseCompletionBlockers.ExistingSelectionLocked]);
        if (!CharacterCreationLoadoutRules.TryProjectBasket(selections, inputs.Authority,
                out var lines, out decimal cost, out var blockers))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, blockers);
        return new(CharacterCreationFoundationOutcomes.Success, BuildProjection(selections, lines, cost, inputs.PendingDraft?.Instances), []);
    }

    internal static CharacterCreationFoundationResult<CharacterCreationLoadoutCompletionProjection> PreviewCompositeEdit(
        CharacterCreationLoadoutCompletionInputs inputs, CharacterCreationLoadoutSelections selections)
    {
        if (!IsValidInputs(inputs) || !IsValidSelections(selections))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationLoadoutCompletionSchemas.RequestInvalid]);
        if (!CharacterCreationLoadoutRules.TryProjectBasket(selections, inputs.Authority, out var lines, out var cost, out var blockers))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, blockers);
        return new(CharacterCreationFoundationOutcomes.Success, BuildProjection(selections, lines, cost,
            inputs.PendingDraft?.Instances?.Where(row => lines.Any(line => line.OptionId == row.OptionId))
                .Select(row => row with { InstanceIds = row.InstanceIds.Take(lines.Single(line => line.OptionId == row.OptionId).Quantity).ToArray() }).ToArray()), []);
    }

    internal static CompletionPreviewSession? PrepareCompletionPreview(CharacterCreationLoadoutCompletionInputs inputs)
    {
        var frozen = Detach(inputs);
        if (!IsValidInputs(frozen)) return null;
        var catalog = CharacterCreationLoadoutRules.CreateProjectionSession(frozen.Authority);
        return catalog is null ? null : new(frozen, catalog);
    }

    internal sealed class CompletionPreviewSession
    {
        private readonly CharacterCreationLoadoutCompletionInputs _inputs;
        private readonly CharacterCreationLoadoutRules.ProjectionSession _catalog;

        internal CompletionPreviewSession(CharacterCreationLoadoutCompletionInputs inputs,
            CharacterCreationLoadoutRules.ProjectionSession catalog)
        {
            _inputs = inputs;
            _catalog = catalog;
        }

        internal CharacterCreationFoundationResult<CharacterCreationLoadoutCompletionProjection> Preview(
            CharacterCreationLoadoutSelections selections)
        {
            selections = Detach(selections);
            if (!IsValidSelections(selections))
                return new(CharacterCreationFoundationOutcomes.Blocked, null,
                    [CharacterCreationLoadoutCompletionSchemas.RequestInvalid]);
            if (!CharacterCreationLoadoutReceiptLedgerIntegrity.Preserves(_inputs.OriginalSelections, selections))
                return new(CharacterCreationFoundationOutcomes.Blocked, null,
                    [CharacterCreationPurchaseCompletionBlockers.ExistingSelectionLocked]);
            if (!_catalog.TryProjectBasket(selections, out var lines, out decimal cost, out var blockers))
                return new(CharacterCreationFoundationOutcomes.Blocked, null, blockers);
            return new(CharacterCreationFoundationOutcomes.Success,
                Detach(BuildProjection(selections, lines, cost, _inputs.PendingDraft?.Instances)), []);
        }
    }

    internal static CharacterCreationLoadoutCompletionProjection BuildProjection(
        CharacterCreationLoadoutSelections selections, IReadOnlyList<CharacterCreationLoadoutLine> lines, decimal cost,
        IReadOnlyList<CharacterCreationLoadoutInstance>? retainedInstances = null)
    {
        var projection = new CharacterCreationLoadoutCompletionProjection(selections, lines, cost, [], string.Empty)
        { RetainedInstances = retainedInstances };
        return projection with { ProjectionDigest = CharacterCreationLoadoutCompletionRules.ComputeProjectionDigest(projection) };
    }

    internal static bool IsValidInputs(CharacterCreationLoadoutCompletionInputs? inputs)
        => inputs is { Schema: CharacterCreationLoadoutCompletionSchemas.InputsV1, Binding: { } binding,
                Authority: { } authority, OriginalSelections: { } original }
            && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            && binding.ContentRevision > 0 && binding.SavedRevision == binding.ContentRevision
            && CharacterCreationFinalizationDigest.IsCanonical(binding.RawCharacterXmlDigest)
            && CharacterCareerReputationTransaction.IsDigest(binding.AuxiliaryStateDigest)
            && CharacterCreationLoadoutRules.IsValidAuthority(authority)
            && CharacterCreationLoadoutReceiptLedgerIntegrity.IsValidLedger(binding.WorkspaceId, binding.ContentRevision,
                inputs.PendingDraft, inputs.Receipts)
            && CharacterCreationLoadoutReceiptLedgerIntegrity.Same(original,
                inputs.PendingDraft?.Selections ?? CharacterCreationLoadoutSelections.Empty)
            && CharacterCreationLoadoutReceiptLedgerIntegrity.MatchesLive(inputs.PendingDraft, authority, binding.RawCharacterXmlDigest)
            && inputs.SnapshotDigest == CharacterCreationLoadoutCompletionRules.ComputeInputsDigest(inputs);

    internal static bool IsValidSelections(CharacterCreationLoadoutSelections? selections)
        => selections is { Weapons: not null, Armor: not null }
            && selections.Weapons.Count <= 128 && selections.Armor.Count <= 128
            && selections.Weapons.All(row => row is not null && !string.IsNullOrWhiteSpace(row.OptionId)
                && row.Quantity is > 0 and <= 20)
            && selections.Armor.All(row => row is not null && !string.IsNullOrWhiteSpace(row.OptionId)
                && row.Quantity is > 0 and <= 20)
            && selections.Weapons.Select(row => row.OptionId).Distinct(StringComparer.Ordinal).Count() == selections.Weapons.Count
            && selections.Armor.Select(row => row.OptionId).Distinct(StringComparer.Ordinal).Count() == selections.Armor.Count;

    private static CharacterCreationFoundationResult<CharacterCreationLoadoutCompletionInputs> RejectedInputs(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);

    private static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
