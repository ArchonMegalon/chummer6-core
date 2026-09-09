using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Shared persisted-shape and receipt-consistency checks. This is not current
/// source/rule validation, proof of historical execution, or a restore grant.
/// </summary>
public static class WorkspaceAuxiliaryStateIntegrity
{
    public static bool IsValidShape(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        WorkspaceDocumentAuxiliaryState state)
    {
        if (state.CharacterCreationFinalizationArchive is { } archive
            && (!CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(
                    workspaceId, currentContentRevision, archive, state.CharacterCreationFinalizationReceipts)
                || !IsValidShape(workspaceId,
                    state.CharacterCreationFinalizationReceipts![0].Receipt.PreviousContentRevision,
                    archive.State)))
        {
            // IsValidArchive forbids nesting before this single historical
            // validation. Do not weaken active draft/receipt pairing rules.
            return false;
        }
        CharacterCreationFoundationDraftLedger? draft = state.CharacterCreationFoundationDraft;
        bool foundationValid = draft is null || string.Equals(
                   draft.Schema,
                   CharacterCreationFoundationSchemas.DraftLedgerV1,
                   StringComparison.Ordinal)
               && draft.WorkspaceId == workspaceId
               && draft.DraftRevision > 0
               && draft.BaseContentRevision > 0
               && draft.BaseContentRevision < currentContentRevision
               && IsFoundationSha256(draft.BaseRawCharacterXmlDigest)
               && IsFoundationSha256(draft.SourceDigest)
               && !string.IsNullOrWhiteSpace(draft.RequestedMetatype)
               && draft.Selection is not null
               && !string.IsNullOrWhiteSpace(draft.Selection.ModuleId)
               && draft.RequirementEvaluations is not null
               && draft.ProjectedEffects is not null
               && draft.FollowUpValues is not null
               && draft.SourceAnchorIds is not null
               && string.Equals(
                   draft.CompilationStatus,
                   CharacterCreationFoundationDraftStatuses.PendingFinalization,
                   StringComparison.Ordinal)
               && !draft.CharacterEffectsApplied
               && IsFoundationSha256(draft.DraftDigest);
        CharacterCreationPrerequisiteDraft? prerequisite =
            state.CharacterCreationPrerequisiteDraft;
        bool prerequisiteValid = prerequisite is null
            || string.Equals(
                prerequisite.Schema,
                CharacterCreationPrerequisiteSchemas.DraftV1,
                StringComparison.Ordinal)
            && prerequisite.WorkspaceId == workspaceId
            && prerequisite.DraftRevision > 0
            && prerequisite.BaseContentRevision > 0
            && prerequisite.BaseContentRevision < currentContentRevision
            && IsFoundationSha256(prerequisite.BaseRawCharacterXmlDigest)
            && IsFoundationSha256(prerequisite.AuthorityDigest)
            && (prerequisite.BuildMethod is CharacterCreationBuildMethods.Priority
                or CharacterCreationBuildMethods.SumToTen)
            && !string.IsNullOrWhiteSpace(prerequisite.SettingsProfileId)
            && !string.IsNullOrWhiteSpace(prerequisite.PriorityTable)
            && prerequisite.PriorityArray is { Count: 5 }
            && prerequisite.Assignments is { Count: 5 }
            && prerequisite.HeritageSelection is not null
            && prerequisite.TalentSelection is not null
            && prerequisite.EffectiveNormalAttributePoints >= 0
            && prerequisite.TotalSpecialAttributePoints >= 0
            && prerequisite.CreationKarmaTotal >= 0
            && prerequisite.CreationKarmaUsed >= 0
            && prerequisite.CreationKarmaUsed <= prerequisite.CreationKarmaTotal
            && prerequisite.SourceAnchorIds is not null
            && IsFoundationSha256(prerequisite.DraftDigest);
        CharacterCreationAttributesDraft? attributes = state.CharacterCreationAttributesDraft;
        bool attributesValid = attributes is null
            || string.Equals(attributes.Schema, CharacterCreationAttributesSchemas.DraftV1, StringComparison.Ordinal)
            && attributes.WorkspaceId == workspaceId
            && attributes.DraftRevision > 0
            && attributes.BaseContentRevision > 0
            && attributes.BaseContentRevision < currentContentRevision
            && IsFoundationSha256(attributes.BaseRawCharacterXmlDigest)
            && attributes.PrerequisiteDraftRevision > 0
            && IsFoundationSha256(attributes.PrerequisiteDraftDigest)
            && IsFoundationSha256(attributes.PrerequisiteAuthorityDigest)
            && Guid.TryParseExact(attributes.MetatypeSourceId, "D", out Guid metatypeSourceId)
            && metatypeSourceId != Guid.Empty
            && IsFoundationSha256(attributes.MetatypeSourceNodeDigest)
            && attributes.NormalPointTotal >= 0
            && attributes.NormalPointUsed >= 0
            && attributes.NormalPointUsed <= attributes.NormalPointTotal
            && attributes.SpecialPointTotal >= 0
            && attributes.SpecialPointUsed >= 0
            && attributes.SpecialPointUsed <= attributes.SpecialPointTotal
            && attributes.CreationKarmaTotal >= 0
            && attributes.CreationKarmaUsed >= 0
            && attributes.CreationKarmaUsed <= attributes.CreationKarmaTotal
            && attributes.Allocations is not null
            && attributes.Attributes is not null
            && attributes.SourceAnchorIds is { Count: > 0 }
            && !attributes.CharacterEffectsApplied
            && IsFoundationSha256(attributes.DraftDigest);
        CharacterCreationSkillsDraft? skills = state.CharacterCreationSkillsDraft;
        IReadOnlyList<CharacterCreationSkillsReceipt>? skillReceipts = state.CharacterCreationSkillsReceipts;
        bool skillsValid = skills is null
            ? skillReceipts is null
            : skillReceipts is { Count: > 0 }
              && CharacterCreationSkillsDigest.IsCanonical(skills.DraftDigest)
              && CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                  skillReceipts,
                  workspaceId,
                  currentContentRevision)
              && skillReceipts[^1].DraftRevision == skills.DraftRevision
              && CharacterCreationSkillsDigest.EqualsFixedTime(skillReceipts[^1].DraftDigest, skills.DraftDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].IdempotencyKeyDigest,
                  skills.LastIdempotencyKeyDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].PreviewDigest,
                  skills.LastPreviewDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].CommandDigest,
                  skills.LastCommandDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].SkillsAuthorityDigest,
                  skills.SkillsAuthorityDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].RuntimeDigest,
                  skills.RuntimeDigest);
        CharacterCreationMagicResonanceDraft? magicResonance =
            state.CharacterCreationMagicResonanceDraft;
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? magicResonanceReceipts =
            state.CharacterCreationMagicResonanceReceipts;
        bool magicResonanceValid = magicResonance is null
            ? magicResonanceReceipts is null
            : magicResonanceReceipts is { Count: > 0 }
              && CharacterCreationMagicResonanceDigest.IsCanonical(magicResonance.DraftDigest)
              && CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
                  magicResonanceReceipts,
                  workspaceId,
                  currentContentRevision)
              && magicResonanceReceipts[^1].DraftRevision == magicResonance.DraftRevision
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].DraftDigest, magicResonance.DraftDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].IdempotencyKeyDigest,
                  magicResonance.LastIdempotencyKeyDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].PreviewDigest,
                  magicResonance.LastPreviewDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].CommandDigest,
                  magicResonance.LastCommandDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].AuthorityDigest,
                  magicResonance.AuthorityDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].SourceInputsDigest,
                  magicResonance.SourceInputsDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].CustomDataInputsDigest,
                  magicResonance.CustomDataInputsDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].GmPolicyDigest,
                  magicResonance.GmPolicyDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].RuntimeDigest,
                  magicResonance.RuntimeDigest)
              && magicResonanceReceipts[^1].AdeptPowerPointsRemaining
                  == magicResonance.AdeptPowerPointBudget.Remaining
              && magicResonanceReceipts[^1].SpellsRemaining
                  == magicResonance.SpellBudget.Remaining
              && magicResonanceReceipts[^1].ComplexFormsRemaining
                  == magicResonance.ComplexFormBudget.Remaining;
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? contactReceipts =
            state.CharacterCreationContactReceipts;
        bool contactReceiptsValid = contactReceipts is null
            || CharacterCreationContactReceiptLedgerIntegrity.IsValidLedger(
                workspaceId,
                currentContentRevision,
                contactReceipts);
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? lifestyleReceipts =
            state.CharacterCreationLifestyleReceipts;
        bool lifestyleReceiptsValid = lifestyleReceipts is null
            || CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(
                workspaceId,
                currentContentRevision,
                lifestyleReceipts);
        CharacterCreationResourcesDraft? resourcesDraft =
            state.CharacterCreationResourcesDraft;
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? resourcesReceipts =
            state.CharacterCreationResourcesReceipts;
        bool resourcesValid = CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
            workspaceId,
            currentContentRevision,
            resourcesDraft,
            resourcesReceipts);
        CharacterCreationGearDraft? gearDraft = state.CharacterCreationGearDraft;
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? gearReceipts =
            state.CharacterCreationGearReceipts;
        bool gearValid = CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(
            workspaceId,
            currentContentRevision,
            gearDraft,
            gearReceipts);
        CharacterCreationQualitiesDraft? qualitiesDraft =
            state.CharacterCreationQualitiesDraft;
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? qualitiesReceipts =
            state.CharacterCreationQualitiesReceipts;
        bool qualitiesValid = CharacterCreationQualitiesReceiptLedgerIntegrity.IsValidLedger(
            workspaceId,
            currentContentRevision,
            qualitiesDraft,
            qualitiesReceipts);
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? afterRunReceipts =
            state.CharacterAfterRunSettlementReceipts;
        bool afterRunReceiptsValid = afterRunReceipts is null
            || CharacterAfterRunSettlementReceiptLedgerIntegrity.IsValidLedger(
                workspaceId,
                currentContentRevision,
                afterRunReceipts);
        bool afterRunRewardReceiptsValid =
            CharacterAfterRunRewardReceiptLedgerIntegrity.IsValidLedger(
                workspaceId,
                currentContentRevision,
                state.CharacterAfterRunRewardReceipts);
        CharacterCreationBootstrapBinding? bootstrap =
            state.CharacterCreationBootstrapBinding;
        bool bootstrapValid = bootstrap is null
            || CharacterCreationBootstrapStoreIntegrity.IsValidBinding(workspaceId, bootstrap);
        IReadOnlyList<LifeModuleDecisionAcceptance>? lifeModuleAcceptances =
            state.LifeModuleDecisionAcceptances;
        bool lifeModuleAcceptancesValid = lifeModuleAcceptances is null
            || LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(
                workspaceId,
                currentContentRevision,
                lifeModuleAcceptances);
        IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry>? finalizationReceipts =
            state.CharacterCreationFinalizationReceipts;
        bool finalizationReceiptsValid =
            CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidLedger(
                workspaceId,
                currentContentRevision,
                finalizationReceipts);
        return foundationValid
               && prerequisiteValid
               && attributesValid
               && skillsValid
               && magicResonanceValid
               && contactReceiptsValid
               && lifestyleReceiptsValid
               && resourcesValid
               && gearValid
               && qualitiesValid
               && afterRunReceiptsValid
               && afterRunRewardReceiptsValid
               && CharacterCareerReputationTransaction.IsValidLedger(
                   workspaceId, currentContentRevision, state.CharacterCareerReputationReceipts)
               && bootstrapValid
               && lifeModuleAcceptancesValid
               && finalizationReceiptsValid;
    }

    private static bool IsFoundationSha256(string? value)
    {
        const string prefix = "sha256:";
        return value is { Length: 71 }
               && value.StartsWith(prefix, StringComparison.Ordinal)
               && DelegatedGmCharacterEditLedgerValidator.IsSha256(value[prefix.Length..]);
    }
}
