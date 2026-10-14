using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationMagicResonanceService
{
    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceCompletionInputs> LoadCompletionInputs(
        CharacterCreationMagicResonanceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = _store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationMagicResonanceCompletionInputs>(
                read.Outcome == WorkspaceOperationOutcome.Missing
                    ? CharacterCreationFoundationOutcomes.Missing : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceBlockers.WorkspaceUnavailable);

        MagicSourceInputs source = ReadSourceInputs(workspace);
        string[] sourceBlockers = Normalize(source.Blockers.Concat(source.Authority.Blockers));
        if (sourceBlockers.Length != 0 || source.Prerequisite is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, sourceBlockers);
        var attributesResult = new CharacterCreationAttributesService(_store, _resolver).Load(new(workspace.Id));
        if (attributesResult.Value is not { } attributes
            || CharacterCreationAttributesService.CreateDraftSession(attributes) is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                attributesResult.Blockers.Count == 0
                    ? [CharacterCreationAttributesBlockers.AuthorityUnavailable] : attributesResult.Blockers);

        var blockers = new List<string>();
        var talent = ResolveSelectedTalent(source.Prerequisite, source.Authority, blockers);
        if (talent is null || blockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, Normalize(blockers));
        var binding = new CharacterCreationMagicResonanceBinding(workspace.Id,
            workspace.ContentRevision, workspace.SavedRevision, source.RawDigest,
            workspace.Document.AuxiliaryStateDigest, source.Prerequisite.DraftRevision,
            source.Prerequisite.DraftDigest, source.Prerequisite.AuthorityDigest,
            attributes.PendingDraft?.DraftRevision ?? 0, attributes.PendingDraft?.DraftDigest ?? string.Empty,
            source.Authority.AuthorityDigest, source.Authority.SourceInputsDigest,
            source.Authority.CustomDataInputsDigest, source.Authority.GmPolicyDigest, source.Authority.RuntimeDigest);
        if (!MatchesCompletionAttributes(binding, source.Prerequisite, attributes))
            return Blocked<CharacterCreationMagicResonanceCompletionInputs>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationMagicResonanceCompletionSchemas.SnapshotConflict);

        CharacterCreationMagicResonanceState? saved = null;
        CharacterCreationMagicResonanceSelections selections = new(null, null, [], [], []);
        if (workspace.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft is not null
            || workspace.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts is not null)
        {
            // Existing history still needs ordinary current-source admission. A
            // stale draft cannot be hidden by projecting different Attributes.
            var existing = BuildState(workspace);
            if (existing.Value is not { CanEdit: true, Blockers.Count: 0, PendingDraft: not null } current)
                return new(CharacterCreationFoundationOutcomes.Blocked, null,
                    existing.Blockers.Count == 0
                        ? [CharacterCreationMagicResonanceBlockers.DraftInvalid] : existing.Blockers);
            if (current.Binding != binding
                || !Same(current.Authority, source.Authority)
                || !Same(current.PrerequisiteDraft, source.Prerequisite)
                || !Same(current.AttributesDraft, attributes.PendingDraft)
                || !Same(current.SelectedTalent, talent))
                return Blocked<CharacterCreationMagicResonanceCompletionInputs>(CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationMagicResonanceCompletionSchemas.SnapshotConflict);
            saved = current;
            selections = current.PendingDraft.Selections;
        }

        var inputs = new CharacterCreationMagicResonanceCompletionInputs(
            CharacterCreationMagicResonanceCompletionSchemas.InputsV1, binding, attributes,
            source.Authority, source.Prerequisite, talent, selections, SnapshotDigest: string.Empty)
            { SavedMagicState = saved };
        inputs = inputs with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(inputs) };
        return new(CharacterCreationFoundationOutcomes.Success, Detach(inputs), []);
    }

    /// <summary>
    /// Evaluates explicit hypothetical choices from admitted, detached original
    /// inputs, including before the first saved Attributes draft. Fresh source and
    /// owner admission is still required for any later mutation.
    /// </summary>
    public static CharacterCreationFoundationResult<CharacterCreationMagicResonanceCompletionProjection> PreviewCompletion(
        CharacterCreationMagicResonanceCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationMagicResonanceSelections selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(selections);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = Detach(inputs);
        attributeAllocations = attributeAllocations.ToArray();
        selections = Detach(selections);
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CharacterCreationMagicResonanceCompletionProjection>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
        if (!MatchesCompletionAttributes(inputs.Binding, inputs.PrerequisiteDraft, inputs.Attributes))
            return Blocked<CharacterCreationMagicResonanceCompletionProjection>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationMagicResonanceCompletionSchemas.SnapshotConflict);
        if (!HasCompletionSelectionShape(selections))
            return Blocked<CharacterCreationMagicResonanceCompletionProjection>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceBlockers.OptionInvalid);

        var attributesSession = CharacterCreationAttributesService.CreateDraftSession(inputs.Attributes);
        if (attributesSession is null)
            return Blocked<CharacterCreationMagicResonanceCompletionProjection>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        cancellationToken.ThrowIfCancellationRequested();
        var attributesResult = attributesSession.Preview(attributeAllocations);
        if (attributesResult.Value is not { } attributes
            || attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, attributesResult.Blockers);
        // Attributes accepts sparse requests and supplies authoritative zero
        // allocations for omitted values. Use that evaluated normalization.
        var evaluatedAllocations = attributes.Attributes.Select(attribute => new CharacterCreationAttributeAllocation(
            attribute.AttributeId, attribute.PriorityPointsSpent, attribute.KarmaLevels)).ToArray();
        if (!CharacterCreationMagicResonanceFinalizationRules.TryResolveEffectiveAttributes(
                inputs.SelectedTalent, attributes.Attributes, evaluatedAllocations, out var effective))
            return Blocked<CharacterCreationMagicResonanceCompletionProjection>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationMagicResonanceBlockers.AttributesDraftInvalid);

        var blockers = new List<string>();
        var projected = EvaluateSelections(inputs.Authority, inputs.SelectedTalent, effective, selections, blockers);
        cancellationToken.ThrowIfCancellationRequested();
        string[] normalized = Normalize(blockers);
        var projection = new CharacterCreationMagicResonanceCompletionProjection(
            CharacterCreationMagicResonanceCompletionSchemas.ProjectionV1,
            inputs.Attributes.SnapshotDigest, inputs.SnapshotDigest, inputs.Binding,
            attributes, effective, inputs.SelectedTalent, projected.Selections,
            projected.TraditionBudget, projected.StreamBudget, projected.PowerBudget,
            projected.SpellBudget, projected.FormBudget, projected.MysticAdeptPowerPoints,
            projected.SourceAnchorIds, normalized, ProposalDigest: string.Empty);
        projection = projection with { ProposalDigest = CharacterCreationMagicResonanceDigest.Compute(projection) };
        return new(normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, Detach(projection), normalized);
    }

    private static bool IsValidCompletionInputs(CharacterCreationMagicResonanceCompletionInputs inputs)
    {
        if (inputs.Schema != CharacterCreationMagicResonanceCompletionSchemas.InputsV1
            || inputs.Binding is null || inputs.Attributes?.Binding is null
            || inputs.PrerequisiteDraft?.Assignments is null || inputs.SelectedTalent is null
            || !HasCompletionSelectionShape(inputs.Selections)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(inputs.SnapshotDigest,
                CharacterCreationMagicResonanceDigest.Compute(inputs with { SnapshotDigest = string.Empty }))
            || !CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(inputs.Authority)
            || inputs.Binding.AuthorityDigest != inputs.Authority.AuthorityDigest
            || inputs.Binding.SourceInputsDigest != inputs.Authority.SourceInputsDigest
            || inputs.Binding.CustomDataInputsDigest != inputs.Authority.CustomDataInputsDigest
            || inputs.Binding.GmPolicyDigest != inputs.Authority.GmPolicyDigest
            || inputs.Binding.RuntimeDigest != inputs.Authority.RuntimeDigest
            || inputs.Binding.PrerequisiteAuthorityDigest != inputs.Authority.PrerequisiteAuthorityDigest
            || inputs.PrerequisiteDraft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen))
            return false;
        var blockers = new List<string>();
        var talent = ResolveSelectedTalent(inputs.PrerequisiteDraft, inputs.Authority, blockers);
        if (talent is null || blockers.Count != 0 || !Same(talent, inputs.SelectedTalent))
            return false;
        if (inputs.SavedMagicState is not { } saved)
            return Same(inputs.Selections, new CharacterCreationMagicResonanceSelections(null, null, [], [], []));

        if (saved is not { CanEdit: true, Blockers.Count: 0, PendingDraft: not null }
            || saved.Schema != CharacterCreationMagicResonanceSchemas.SnapshotV1
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(saved.SnapshotDigest,
                CharacterCreationMagicResonanceDigest.Compute(saved with { SnapshotDigest = string.Empty }))
            || saved.Binding != inputs.Binding || !Same(saved.Authority, inputs.Authority)
            || !Same(saved.PrerequisiteDraft, inputs.PrerequisiteDraft)
            || !Same(saved.SelectedTalent, talent)
            || !Same(saved.AttributesDraft, inputs.Attributes.PendingDraft)
            || !Same(saved.PendingDraft.Selections, inputs.Selections)
            || inputs.Attributes.PendingDraft is not { } attributes
            || !CharacterCreationMagicResonanceDraftIntegrity.IsStructurallyValidPending(saved.PendingDraft,
                inputs.Binding.WorkspaceId, inputs.Binding.ContentRevision, inputs.Binding.RawCharacterXmlDigest,
                inputs.PrerequisiteDraft, attributes, inputs.Authority))
            return false;
        var projected = EvaluateSelections(inputs.Authority, talent, attributes, inputs.Selections, blockers);
        return blockers.Count == 0 && MatchesPendingProjection(saved.PendingDraft, talent, projected)
            && Same(saved.MysticAdeptPowerPoints, projected.MysticAdeptPowerPoints)
            && Same(new[] { saved.TraditionBudget, saved.StreamBudget, saved.AdeptPowerPointBudget,
                    saved.SpellBudget, saved.ComplexFormBudget },
                new[] { projected.TraditionBudget, projected.StreamBudget, projected.PowerBudget,
                    projected.SpellBudget, projected.FormBudget });
    }

    private static bool MatchesCompletionAttributes(CharacterCreationMagicResonanceBinding binding,
        CharacterCreationPrerequisiteDraft prerequisite, CharacterCreationAttributesState attributes)
    {
        var original = attributes.Binding;
        return binding.WorkspaceId == original.WorkspaceId
            && binding.ContentRevision > 0 && binding.ContentRevision == binding.SavedRevision
            && binding.ContentRevision == original.ContentRevision && binding.SavedRevision == original.SavedRevision
            && binding.RawCharacterXmlDigest == original.RawCharacterXmlDigest
            && binding.AuxiliaryStateDigest == original.AuxiliaryStateDigest
            && binding.PrerequisiteDraftRevision == original.PrerequisiteDraftRevision
            && binding.PrerequisiteDraftDigest == original.PrerequisiteDraftDigest
            && binding.PrerequisiteAuthorityDigest == original.PrerequisiteAuthorityDigest
            && binding.AttributesDraftRevision == (attributes.PendingDraft?.DraftRevision ?? 0)
            && binding.AttributesDraftDigest == (attributes.PendingDraft?.DraftDigest ?? string.Empty)
            && Same(prerequisite, attributes.PrerequisiteDraft);
    }

    private static bool HasCompletionSelectionShape(CharacterCreationMagicResonanceSelections? selections) =>
        selections is { AdeptPowers: not null, Spells: not null, ComplexForms: not null }
        && selections.AdeptPowers.All(item => item?.Identity is not null)
        && selections.Spells.All(item => item is not null)
        && selections.ComplexForms.All(item => item is not null);

    private static bool MatchesPendingProjection(CharacterCreationMagicResonanceDraft pending,
        CharacterCreationMagicResonanceTalentOption talent, SelectionEvaluation projected) =>
        pending.TalentIdentity == talent.Identity
        && string.Equals(pending.TalentKind, talent.Kind, StringComparison.Ordinal)
        && pending.AssignedMagic == talent.Magic
        && pending.AssignedResonance == talent.Resonance
        && pending.AssignedDepth == talent.Depth
        && Same(pending.Selections, projected.Selections)
        && Same(new[] { pending.TraditionBudget, pending.StreamBudget, pending.AdeptPowerPointBudget,
                pending.SpellBudget, pending.ComplexFormBudget },
            new[] { projected.TraditionBudget, projected.StreamBudget, projected.PowerBudget,
                projected.SpellBudget, projected.FormBudget })
        && Same(pending.SourceAnchorIds, projected.SourceAnchorIds);

    private static bool Same<T>(T left, T right) => CharacterCreationMagicResonanceDigest.EqualsFixedTime(
        CharacterCreationMagicResonanceDigest.Compute(left), CharacterCreationMagicResonanceDigest.Compute(right));

    private static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;

    // Only actual workspace/source observation enters here. Ordinary editing and
    // completion share this admission without relaxing ordinary draft gates.
    private MagicSourceInputs ReadSourceInputs(WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        ICharacterSourceDataContext? context = _resolver.TryCreateContext(workspace.Document.Content);
        CharacterCreationPrerequisiteAuthority prerequisiteAuthority = CharacterCreationPrerequisiteAuthority.Unavailable;
        CharacterCreationMagicResonanceAuthority authority = CharacterCreationMagicResonanceAuthority.Unavailable;
        bool authorityReady = context is not null
            && context.TryResolveCreationPrerequisiteAuthority(out prerequisiteAuthority)
            && context.TryResolveCreationMagicResonanceAuthority(out authority)
            && CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(authority);
        if (!authorityReady)
            blockers.Add(CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
        else if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                     authority.PrerequisiteAuthorityDigest, prerequisiteAuthority.AuthorityDigest))
            blockers.Add(CharacterCreationMagicResonanceBlockers.PrerequisiteSourceDrift);
        CharacterCreationPrerequisiteDraft? prerequisite = workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft;
        if (prerequisite is null)
            blockers.Add(CharacterCreationMagicResonanceBlockers.PrerequisiteDraftRequired);
        else if (!CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(prerequisite,
                     workspace.Id, workspace.ContentRevision, rawDigest, prerequisiteAuthority))
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.PrerequisiteDraftInvalid);
            prerequisite = null;
        }
        return new(rawDigest, authority, prerequisite, blockers);
    }

    private sealed record MagicSourceInputs(string RawDigest,
        CharacterCreationMagicResonanceAuthority Authority,
        CharacterCreationPrerequisiteDraft? Prerequisite,
        IReadOnlyList<string> Blockers);
}
