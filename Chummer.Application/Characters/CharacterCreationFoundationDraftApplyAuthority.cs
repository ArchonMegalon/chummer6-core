using System.Globalization;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Persists only the reviewable foundation draft. It deliberately does not compile
/// ImprovementManager effects or change the canonical character payload.
/// </summary>
public sealed class CharacterCreationFoundationDraftApplyAuthority :
    ICharacterCreationFoundationApplyAuthority,
    ICharacterCreationFoundationDraftPersistenceCapability
{
    private readonly IWorkspaceStore _workspaceStore;

    public CharacterCreationFoundationDraftApplyAuthority(IWorkspaceStore workspaceStore)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
    }

    public bool CanPersistFoundationDrafts =>
        _workspaceStore is IWorkspaceAuxiliaryStateAtomicCommitCapability
        {
            SupportsWorkspaceAuxiliaryStateAtomicCommit: true
        };

    public CharacterCreationFoundationAuthorityPreview Preview(
        CharacterCreationFoundationAuthorityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var blockers = new List<string>();

        if (!CanPersistFoundationDrafts)
            blockers.Add(CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);
        if (!CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(context.SourceDigest))
            blockers.Add(CharacterCreationFoundationBlockers.SourceDigestConflict);
        if (!context.LifeModuleBudgetBefore.IsExact || !context.LifeModuleBudgetAfter.IsExact)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired);
        blockers.AddRange(context.LifeModuleBudgetAfter.Blockers);

        if (!IsAuthoritativeMetatypeSelection(
                context.SelectedMetatype,
                context.RequestedMetatype))
            blockers.Add(CharacterCreationFoundationBlockers.MetatypeLegalityAuthorityRequired);

        if (!string.Equals(
                context.Nationality.ModuleId,
                context.Selection.ModuleId,
                StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationFoundationBlockers.NationalityModuleNotFound);
        }
        if (context.NationalityVersion is null
            ? !string.IsNullOrWhiteSpace(context.Selection.VersionId)
            : !string.Equals(
                context.NationalityVersion.VersionId,
                context.Selection.VersionId,
                StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationFoundationBlockers.NationalityVersionNotFound);
        }

        blockers.AddRange(context.RequirementEvaluations
            .Where(requirement => !requirement.IsMet)
            .Select(requirement => requirement.DisableReasonKey
                                   ?? CharacterCreationFoundationBlockers.LifeModuleRequirementNotMet));

        CharacterCreationFoundationDraftLedger proposed = BuildProposedLedger(context);
        CharacterCreationFoundationDraftLedger? current =
            context.Workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft;
        if (current is not null)
        {
            if (current.DraftRevision == long.MaxValue)
                blockers.Add(CharacterCreationFoundationBlockers.PendingDraftConflict);
            string currentRawDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeRawCharacterXmlDigest(context.Workspace.Document.Content);
            if (!CharacterCreationFoundationDraftLedgerIntegrity.IsValidPending(
                    current,
                    context.Workspace.Id,
                    context.Workspace.ContentRevision,
                    currentRawDigest,
                    context.SourceDigest))
            {
                blockers.Add(CharacterCreationFoundationBlockers.PendingDraftInvalid);
            }
            else if (CharacterCreationFoundationDraftLedgerIntegrity.HasSameLogicalPayload(
                         current,
                         proposed))
            {
                blockers.Add(CharacterCreationFoundationBlockers.PendingDraftDuplicate);
            }
        }

        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        bool canApply = normalizedBlockers.Length == 0;
        return new CharacterCreationFoundationAuthorityPreview(
            Diff: BuildDiff(context, canApply, normalizedBlockers),
            Blockers: normalizedBlockers,
            CanApply: canApply,
            AuthorityPlanDigest: proposed.DraftDigest);
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> ApplyAndCheckpoint(
        CharacterCreationFoundationAuthorityContext context,
        string previewDigest)
    {
        ArgumentNullException.ThrowIfNull(context);
        CharacterCreationFoundationAuthorityPreview preview = Preview(context);
        if (!CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(previewDigest))
        {
            return Blocked(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.PreviewDigestMismatch);
        }
        if (!preview.CanApply || preview.Blockers.Count > 0)
        {
            return new CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                preview.Blockers);
        }
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            } atomicStore)
        {
            return Blocked(
                CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);
        }

        CharacterCreationFoundationDraftLedger proposed = BuildProposedLedger(context);
        WorkspaceDocument currentDocument = context.Workspace.Document;
        WorkspaceDocument replacement = currentDocument with
        {
            State = currentDocument.State with
            {
                AuxiliaryState = currentDocument.AuxiliaryState with
                {
                    CharacterCreationFoundationDraft = proposed
                }
            }
        };
        WorkspaceStoreMutationResult mutation =
            atomicStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                context.Workspace.Id,
                context.Workspace.ContentRevision,
                currentDocument.AuxiliaryStateDigest,
                replacement);
        if (!mutation.Success || mutation.Entry is not WorkspaceStoreEntry entry)
            return MutationFailure(mutation);

        return new CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt>(
            Outcome: CharacterCreationFoundationOutcomes.Success,
            Value: new CharacterCreationFoundationApplyReceipt(
                WorkspaceId: context.Workspace.Id,
                PreviousContentRevision: context.Workspace.ContentRevision,
                ContentRevision: entry.ContentRevision,
                SavedRevision: entry.SavedRevision,
                RawCharacterXmlDigest: proposed.BaseRawCharacterXmlDigest,
                SourceDigest: proposed.SourceDigest,
                PreviewDigest: previewDigest,
                Selection: proposed.Selection,
                Metatype: proposed.RequestedMetatype,
                DraftRevision: proposed.DraftRevision,
                DraftDigest: proposed.DraftDigest,
                CharacterEffectsApplied: false),
            Blockers: []);
    }

    internal static CharacterCreationFoundationDraftLedger BuildProposedLedger(
        CharacterCreationFoundationAuthorityContext context)
    {
        CharacterCreationFoundationDraftLedger? current =
            context.Workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft;
        long nextDraftRevision = current is null
            ? 1
            : current.DraftRevision == long.MaxValue
                ? long.MaxValue
                : current.DraftRevision + 1;
        LifeModuleEffectProjectionDto[] effects =
        [
            .. context.NationalityVersion?.Effects ?? [],
            .. context.Nationality.Effects
        ];
        string[] sourceAnchors = context.SelectedMetatype.SourceAnchorIds
            .Concat(context.NationalityVersion?.SourceAnchorIds ?? [])
            .Concat(context.Nationality.SourceAnchorIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var ledger = new CharacterCreationFoundationDraftLedger(
            Schema: CharacterCreationFoundationSchemas.DraftLedgerV1,
            WorkspaceId: context.Workspace.Id,
            DraftRevision: nextDraftRevision,
            BaseContentRevision: context.Workspace.ContentRevision,
            BaseRawCharacterXmlDigest: CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeRawCharacterXmlDigest(context.Workspace.Document.Content),
            SourceDigest: context.SourceDigest,
            RequestedMetatype: context.SelectedMetatype.Label,
            Selection: new CharacterCreationFoundationSelection(
                context.Selection.ModuleId.Trim(),
                context.Selection.VersionId?.Trim()),
            RequirementEvaluations: context.RequirementEvaluations.ToArray(),
            ProjectedEffects: effects,
            FollowUpValues: context.FollowUpValues
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            SourceAnchorIds: sourceAnchors,
            CompilationStatus: CharacterCreationFoundationDraftStatuses.PendingFinalization,
            CharacterEffectsApplied: false,
            DraftDigest: string.Empty);
        return ledger with
        {
            DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(ledger)
        };
    }

    private static IReadOnlyList<CharacterCreationFoundationDiffEntry> BuildDiff(
        CharacterCreationFoundationAuthorityContext context,
        bool canApply,
        IReadOnlyList<string> blockers)
    {
        string[] entryBlockers = canApply
            ? []
            : blockers.ToArray();
        var diff = new List<CharacterCreationFoundationDiffEntry>
        {
            new(
                DiffId: "foundation:requested-metatype",
                Domain: "metatype-choice",
                TargetId: context.SelectedMetatype.OptionId,
                BeforeValue: context.Summary.Metatype,
                AfterValue: context.SelectedMetatype.Label,
                Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
                AppliesToCharacterDocument: false,
                IsAuthoritative: IsAuthoritativeMetatypeSelection(
                    context.SelectedMetatype,
                    context.RequestedMetatype),
                CanApply: canApply,
                Blockers: entryBlockers,
                SourceAnchorIds: context.SelectedMetatype.SourceAnchorIds),
            new(
                DiffId: "foundation:metatype-cost",
                Domain: "choice-cost",
                TargetId: CharacterCreationBudgetIds.LifeModules,
                BeforeValue: null,
                AfterValue: ResolveMetatypeCost(context.SelectedMetatype)?.ToString(
                    CultureInfo.InvariantCulture),
                Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
                AppliesToCharacterDocument: false,
                IsAuthoritative: ResolveMetatypeCost(context.SelectedMetatype).HasValue,
                CanApply: canApply,
                Blockers: entryBlockers,
                SourceAnchorIds: context.SelectedMetatype.SourceAnchorIds),
            new(
                DiffId: "life-modules:karma-budget",
                Domain: "budget",
                TargetId: CharacterCreationBudgetIds.LifeModules,
                BeforeValue: context.LifeModuleBudgetBefore.Remaining.ToString(
                    CultureInfo.InvariantCulture),
                AfterValue: context.LifeModuleBudgetAfter.Remaining.ToString(
                    CultureInfo.InvariantCulture),
                Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
                AppliesToCharacterDocument: false,
                IsAuthoritative: context.LifeModuleBudgetBefore.IsExact
                                 && context.LifeModuleBudgetAfter.IsExact,
                CanApply: canApply,
                Blockers: entryBlockers,
                SourceAnchorIds: context.SelectedMetatype.SourceAnchorIds
                    .Concat(context.Nationality.SourceAnchorIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()),
            new(
                DiffId: "life-modules:nationality-selection",
                Domain: "life-module-selection",
                TargetId: CharacterCreationLifeModuleStageIds.Nationality,
                BeforeValue: context.Workspace.Document.AuxiliaryState
                    .CharacterCreationFoundationDraft is { } current
                    ? SelectionValue(current.Selection)
                    : null,
                AfterValue: SelectionValue(context.Selection),
                Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
                AppliesToCharacterDocument: false,
                IsAuthoritative: true,
                CanApply: canApply,
                Blockers: entryBlockers,
                SourceAnchorIds: context.Nationality.SourceAnchorIds)
        };

        diff.AddRange(context.RequirementEvaluations.Select(requirement =>
            new CharacterCreationFoundationDiffEntry(
                DiffId: $"requirement:{requirement.RequirementId}",
                Domain: "life-module-requirement",
                TargetId: requirement.RequirementId,
                BeforeValue: null,
                AfterValue: requirement.IsMet ? "met" : "not-met",
                Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
                AppliesToCharacterDocument: false,
                IsAuthoritative: !requirement.RequiresCharacterAuthority,
                CanApply: canApply,
                Blockers: entryBlockers,
                SourceAnchorIds: requirement.SourceAnchorIds)));

        LifeModuleFollowUpPromptDto[] followUps = context.Nationality.FollowUps
            .Concat(context.NationalityVersion?.FollowUps ?? [])
            .ToArray();
        diff.AddRange(followUps
            .Where(prompt => context.FollowUpValues.ContainsKey(prompt.PromptId))
            .Select(prompt => new CharacterCreationFoundationDiffEntry(
                DiffId: $"follow-up:{prompt.PromptId}",
                Domain: "life-module-follow-up",
                TargetId: prompt.PromptId,
                BeforeValue: null,
                AfterValue: context.FollowUpValues[prompt.PromptId],
                Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
                AppliesToCharacterDocument: false,
                IsAuthoritative: true,
                CanApply: canApply,
                Blockers: entryBlockers,
                SourceAnchorIds: prompt.SourceAnchorIds)));

        LifeModuleEffectProjectionDto[] effects =
        [
            .. context.NationalityVersion?.Effects ?? [],
            .. context.Nationality.Effects
        ];
        diff.AddRange(effects.Select(effect => new CharacterCreationFoundationDiffEntry(
            DiffId: effect.EffectId,
            Domain: effect.Domain,
            TargetId: effect.TargetId,
            BeforeValue: effect.BeforeValue,
            AfterValue: effect.AfterValue,
            Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
            AppliesToCharacterDocument: false,
            IsAuthoritative: effect.IsFullyTyped,
            CanApply: canApply,
            Blockers: entryBlockers,
            SourceAnchorIds: effect.SourceAnchorIds)));
        return diff;
    }

    private static bool IsAuthoritativeMetatypeSelection(
        CharacterCreationLegalOption? option,
        string requestedMetatype)
    {
        return option is not null
               && option.IsEnabled
               && option.DisableReasonKey is null
               && Guid.TryParseExact(option.OptionId, "D", out Guid id)
               && id != Guid.Empty
               && string.Equals(option.OptionId, id.ToString("D"), StringComparison.Ordinal)
               && string.Equals(option.Label, requestedMetatype, StringComparison.Ordinal)
               && option.SourceAnchorIds.Count > 0
               && option.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
               && ResolveMetatypeCost(option).HasValue;
    }

    private static decimal? ResolveMetatypeCost(CharacterCreationLegalOption option)
    {
        CharacterCreationChoiceCost[] costs = option.Costs
            .Where(cost => string.Equals(
                               cost.BudgetId,
                               CharacterCreationBudgetIds.LifeModules,
                               StringComparison.Ordinal)
                           && string.Equals(cost.Unit, "karma", StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return costs.Length == 1 && costs[0].Delta >= 0
            ? costs[0].Delta
            : null;
    }

    private static string SelectionValue(CharacterCreationFoundationSelection selection)
    {
        return string.IsNullOrWhiteSpace(selection.VersionId)
            ? selection.ModuleId
            : $"{selection.ModuleId}/{selection.VersionId}";
    }

    private static CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt>
        MutationFailure(WorkspaceStoreMutationResult mutation)
    {
        return mutation.Outcome switch
        {
            WorkspaceOperationOutcome.Conflict => Blocked(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.PendingDraftConflict),
            WorkspaceOperationOutcome.Corrupt => Blocked(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.PendingDraftInvalid),
            _ => Blocked(
                CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.WorkspaceUnavailable)
        };
    }

    private static CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt>
        Blocked(string outcome, params string[] blockers)
    {
        return new CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt>(
            outcome,
            null,
            blockers);
    }
}
