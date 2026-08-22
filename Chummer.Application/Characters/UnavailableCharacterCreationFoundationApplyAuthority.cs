using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

/// <summary>
/// Honest boundary used until the canonical Chummer5 ImprovementManager and an
/// atomic document-plus-checkpoint store transaction are available headlessly.
/// It emits a reviewable structural diff but never mutates a workspace.
/// </summary>
public sealed class UnavailableCharacterCreationFoundationApplyAuthority
    : ICharacterCreationFoundationApplyAuthority
{
    public CharacterCreationFoundationAuthorityPreview Preview(
        CharacterCreationFoundationAuthorityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var blockers = new List<string>
        {
            CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired
        };
        if (!context.LifeModuleBudgetBefore.IsExact
            || !context.LifeModuleBudgetAfter.IsExact)
        {
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired);
        }
        var diff = new List<CharacterCreationFoundationDiffEntry>();

        string[] budgetDiffBlockers = context.LifeModuleBudgetAfter.Blockers
            .Append(CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired)
            .Concat(context.LifeModuleBudgetAfter.IsExact
                ? []
                : [CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        diff.Add(new CharacterCreationFoundationDiffEntry(
            DiffId: "life-modules:karma-budget",
            Domain: "budget",
            TargetId: CharacterCreationBudgetIds.LifeModules,
            BeforeValue: context.LifeModuleBudgetBefore.Remaining.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            AfterValue: context.LifeModuleBudgetAfter.Remaining.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
            AppliesToCharacterDocument: false,
            IsAuthoritative: context.LifeModuleBudgetBefore.IsExact
                             && context.LifeModuleBudgetAfter.IsExact,
            CanApply: false,
            Blockers: budgetDiffBlockers,
            SourceAnchorIds: context.Nationality.SourceAnchorIds));

        if (!string.Equals(
                context.Summary.Metatype,
                context.RequestedMetatype,
                StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(CharacterCreationFoundationBlockers.MetatypeLegalityAuthorityRequired);
            diff.Add(new CharacterCreationFoundationDiffEntry(
                DiffId: "foundation:metatype",
                Domain: "metatype",
                TargetId: "metatype",
                BeforeValue: context.Summary.Metatype,
                AfterValue: context.RequestedMetatype,
                Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
                AppliesToCharacterDocument: false,
                IsAuthoritative: false,
                CanApply: false,
                Blockers: [CharacterCreationFoundationBlockers.MetatypeLegalityAuthorityRequired],
                SourceAnchorIds: []));
        }

        string selectionValue = string.IsNullOrWhiteSpace(context.Selection.VersionId)
            ? context.Selection.ModuleId
            : $"{context.Selection.ModuleId}/{context.Selection.VersionId}";
        diff.Add(new CharacterCreationFoundationDiffEntry(
            DiffId: "life-modules:nationality-selection",
            Domain: "life-module-selection",
            TargetId: CharacterCreationLifeModuleStageIds.Nationality,
            BeforeValue: null,
            AfterValue: selectionValue,
            Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
            AppliesToCharacterDocument: false,
            IsAuthoritative: true,
            CanApply: false,
            Blockers: [CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired],
            SourceAnchorIds: context.Nationality.SourceAnchorIds));

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
                CanApply: false,
                Blockers: [CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired],
                SourceAnchorIds: prompt.SourceAnchorIds)));

        LifeModuleEffectProjectionDto[] effects = context.Nationality.Effects
            .Concat(context.NationalityVersion?.Effects ?? [])
            .ToArray();
        if (effects.Length > 0)
        {
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleEffectApplicationAuthorityRequired);
        }

        diff.AddRange(effects.Select(effect => new CharacterCreationFoundationDiffEntry(
            DiffId: effect.EffectId,
            Domain: effect.Domain,
            TargetId: effect.TargetId,
            BeforeValue: effect.BeforeValue,
            AfterValue: effect.AfterValue,
            Phase: CharacterCreationFoundationDiffPhases.DraftLedger,
            AppliesToCharacterDocument: false,
            IsAuthoritative: effect.IsFullyTyped,
            CanApply: false,
            Blockers: [CharacterCreationFoundationBlockers.LifeModuleEffectApplicationAuthorityRequired],
            SourceAnchorIds: effect.SourceAnchorIds)));

        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        CharacterCreationFoundationDiffEntry[] normalizedDiff = diff.ToArray();
        string authorityPlanDigest = Digest(new
        {
            context.Workspace.Id.Value,
            context.Workspace.ContentRevision,
            context.RequestedMetatype,
            context.Selection,
            context.FollowUpValues,
            context.LifeModuleBudgetBefore,
            context.SelectionCost,
            context.LifeModuleBudgetAfter,
            context.SourceDigest,
            Diff = normalizedDiff,
            Blockers = normalizedBlockers
        });

        return new CharacterCreationFoundationAuthorityPreview(
            Diff: normalizedDiff,
            Blockers: normalizedBlockers,
            CanApply: false,
            AuthorityPlanDigest: authorityPlanDigest);
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> ApplyAndCheckpoint(
        CharacterCreationFoundationAuthorityContext context,
        string previewDigest)
    {
        CharacterCreationFoundationAuthorityPreview preview = Preview(context);
        return new CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt>(
            Outcome: CharacterCreationFoundationOutcomes.Blocked,
            Value: null,
            Blockers: preview.Blockers);
    }

    private static string Digest<T>(T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
