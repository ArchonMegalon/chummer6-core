using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    public CharacterCreationFoundationResult<CharacterCreationSkillsCompletionInputs> LoadCompletionInputs(
        CharacterCreationSkillsLoadRequest request)
    {
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        ArgumentNullException.ThrowIfNull(request);
        var read = _store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationSkillsCompletionInputs>(read.Outcome == WorkspaceOperationOutcome.Missing
                ? CharacterCreationFoundationOutcomes.Missing : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.WorkspaceUnavailable);

        var source = ReadSourceInputs(workspace);
        if (source.Blockers.Count != 0 || source.Prerequisite is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, source.Blockers);
        var attributesResult = new CharacterCreationAttributesService(_store, _resolver).Load(new(workspace.Id));
        if (attributesResult.Value is not { } attributes
            || CharacterCreationAttributesService.CreateDraftSession(attributes) is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                attributesResult.Blockers.Count == 0 ? [CharacterCreationAttributesBlockers.AuthorityUnavailable] : attributesResult.Blockers);

        var binding = new CharacterCreationSkillsBinding(workspace.Id, workspace.ContentRevision, workspace.SavedRevision,
            source.RawDigest, workspace.Document.AuxiliaryStateDigest, source.Prerequisite.DraftRevision,
            source.Prerequisite.DraftDigest, source.Prerequisite.AuthorityDigest,
            attributes.PendingDraft?.DraftRevision ?? 0, attributes.PendingDraft?.DraftDigest ?? string.Empty,
            source.Authority.AuthorityDigest, source.Authority.RuntimeDigest, source.ContributionDigest);
        if (!MatchesCompletionAttributes(binding, source.Prerequisite, attributes))
            return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationAttributeSkillsPreviewSchemas.SnapshotConflict);

        if (!CharacterCreationCompositeBaselineReader.TryRead(workspace, out var composite)
            || composite is not null && !CharacterCreationCompositeBaselineReader.MatchesSkillsSource(
                workspace, source.Authority, source.Prerequisite))
            return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationCompositeBaselineReader.Invalid);
        CharacterCreationSkillsState? savedSkills = null;
        IReadOnlyList<CharacterCreationSkillAllocation> allocations;
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups;
        if (composite is not null)
        {
            allocations = composite.Selections.SkillAllocations;
            groups = composite.Selections.GroupAllocations;
        }
        else if (workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft is not null
            || workspace.Document.AuxiliaryState.CharacterCreationSkillsReceipts is not null)
        {
            // No bypass for stale saved skills after an attribute/source edit.
            // Ordinary history/re-review requirements remain the authority.
            var existing = BuildState(workspace, out _, forContinuation: true);
            if (existing.Value is not { PendingDraft: not null } current || CreateDraftSession(current) is null)
                return new(CharacterCreationFoundationOutcomes.Blocked, null,
                    existing.Blockers.Count == 0 ? [CharacterCreationSkillsBlockers.DraftInvalid] : existing.Blockers);
            if (current.Binding != binding || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    current.AttributesDraft, attributes.PendingDraft))
                return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationAttributeSkillsPreviewSchemas.SnapshotConflict);
            savedSkills = current;
            allocations = current.PendingDraft.Allocations;
            groups = current.PendingDraft.GroupAllocations;
        }
        else
        {
            if (!CharacterCreationTalentSkillGrants.TryResolve(source.Prerequisite, source.Authority, out var grants))
                return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                    CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
            allocations = grants.InitialSkills(source.Authority).Select(skill => new CharacterCreationSkillAllocation(
                skill.SourceSkillId, skill.Kind, skill.Rating, skill.SpecializationOptionId, skill.IsNativeLanguage)).ToArray();
            groups = grants.InitialGroups(source.Authority).Select(group =>
                new CharacterCreationSkillGroupAllocation(group.GroupId, group.Rating)).ToArray();
        }
        CharacterCreationKarmaSkillsPolicy? karmaPolicy = null;
        IReadOnlyList<string>? compensationOrder = null;
        IReadOnlyList<CharacterCreationQualitySelection>? modifierSelections = null;
        if (request.IncludeKarmaPurchasePolicy || composite?.Selections.SkillKarmaPurchases is not null)
        {
            var context = _resolver.TryCreateContext(workspace.Document.Content);
            if (context is null || !context.TryResolveCreationPrioritySkillKarmaPolicy(out karmaPolicy)
                || !IsCompletionKarmaPolicyValid(karmaPolicy, source.Authority))
                return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                    CharacterCreationSkillKarmaPurchaseBlockers.PolicyUnavailable);
            if (karmaPolicy!.CompensateSkillGroupKarmaDifference)
            {
                // Reuse the existing source resolver and its canonical order.
                // Both projections must describe this very same effective source.
                if (!context.TryResolveCreationSkillsCatalog(out var catalog)
                    || !CharacterCreationSkillsCatalogAuthority.IsValid(catalog)
                    || catalog!.SettingsProfileId != source.Authority.SettingsProfileId
                    || catalog.RawProfileInputsDigest != source.Authority.RawProfileInputsDigest
                    || catalog.SkillsInputsDigest != source.Authority.EffectiveSkillsInputsDigest
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                        catalog.ActiveSkills, source.Authority.ActiveSkills)
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                        catalog.SkillGroups, source.Authority.SkillGroups))
                    return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                        CharacterCreationSkillKarmaPurchaseBlockers.PolicyUnavailable);
                compensationOrder = catalog.ActiveSkillSourceOrder.ToArray();
            }
            if (composite is not null && composite.Selections.QualityOptionIds.Count > 0)
            {
                if (!context.TryResolveCreationQualitiesAuthority(source.Prerequisite, out var qualityAuthority)
                    || qualityAuthority.SettingsProfileId != source.Authority.SettingsProfileId
                    || qualityAuthority.RulesetId != workspace.Document.RulesetId)
                    return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                        CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
                qualityAuthority = CharacterCreationLegacySourceProjector.AdmitSkillKarmaQualityAuthority(qualityAuthority);
                var qualityBinding = new CharacterCreationQualitiesBinding(workspace.Id, workspace.ContentRevision,
                    workspace.SavedRevision, source.RawDigest, workspace.Document.AuxiliaryStateDigest,
                    source.Prerequisite.DraftRevision, source.Prerequisite.DraftDigest,
                    attributes.PendingDraft?.DraftRevision ?? 0, attributes.PendingDraft?.DraftDigest ?? string.Empty,
                    workspace.Document.RulesetId, source.Prerequisite.BuildMethod, false,
                    source.Prerequisite.CreationKarmaTotal, attributes.PendingDraft?.CreationKarmaUsed ?? 0,
                    qualityAuthority.AuthorityDigest, qualityAuthority.RuntimeDigest);
                var qualities = CharacterCreationQualitiesRules.EvaluateCompletion(new(qualityBinding, qualityAuthority,
                    composite.Selections.QualityOptionIds), attributes.PendingDraft?.CreationKarmaUsed ?? 0);
                if (qualities.Blockers.Count != 0
                    || !CharacterCreationLegacySourceProjector.TrySkillKarmaModifiers(qualities.Selections, source.Authority, out _))
                    return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                        CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
                modifierSelections = qualities.Selections;
            }
        }
        var inputs = new CharacterCreationSkillsCompletionInputs(CharacterCreationSkillsCompletionSchemas.InputsV1,
            binding, attributes, source.Authority, source.Prerequisite, allocations, groups, source.Contributions,
            source.ActiveTotal, source.GroupTotal, source.Movement, SnapshotDigest: string.Empty)
            { SavedSkillsState = savedSkills, KarmaPurchasePolicy = karmaPolicy,
                KarmaCompensationSourceOrder = compensationOrder, SkillKarmaQualitySelections = modifierSelections };
        inputs = inputs with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(inputs) };
        if (composite is not null && !ValidateCompositePending(workspace, inputs))
            return Blocked<CharacterCreationSkillsCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillsBlockers.DraftInvalid);
        return new(CharacterCreationFoundationOutcomes.Success,
            JsonSerializer.Deserialize<CharacterCreationSkillsCompletionInputs>(JsonSerializer.SerializeToUtf8Bytes(inputs))!, []);
    }

    private static bool MatchesCompletionAttributes(CharacterCreationSkillsBinding binding,
        CharacterCreationPrerequisiteDraft prerequisite, CharacterCreationAttributesState attributes)
    {
        var original = attributes.Binding;
        return binding.WorkspaceId == original.WorkspaceId
            && binding.ContentRevision > 0 && binding.ContentRevision == binding.SavedRevision
            && binding.ContentRevision == original.ContentRevision && binding.SavedRevision == original.SavedRevision
            && CharacterCreationSkillsDigest.EqualsFixedTime(binding.RawCharacterXmlDigest, original.RawCharacterXmlDigest)
            && CharacterCreationSkillsDigest.EqualsFixedTime(binding.AuxiliaryStateDigest, original.AuxiliaryStateDigest)
            && binding.PrerequisiteDraftRevision == original.PrerequisiteDraftRevision
            && CharacterCreationSkillsDigest.EqualsFixedTime(binding.PrerequisiteDraftDigest, original.PrerequisiteDraftDigest)
            && CharacterCreationSkillsDigest.EqualsFixedTime(binding.PrerequisiteAuthorityDigest, original.PrerequisiteAuthorityDigest)
            && binding.AttributesDraftRevision == (attributes.PendingDraft?.DraftRevision ?? 0)
            && string.Equals(binding.AttributesDraftDigest, attributes.PendingDraft?.DraftDigest ?? string.Empty, StringComparison.Ordinal)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(prerequisite, attributes.PrerequisiteDraft);
    }
}
