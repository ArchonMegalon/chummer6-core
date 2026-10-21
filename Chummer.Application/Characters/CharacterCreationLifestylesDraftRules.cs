using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Source-bound lifestyle purchases over the confirmed Priority/Sum-to-Ten draft graph.</summary>
public static partial class CharacterCreationLifestylesDraftRules
{
    public static string Digest(CharacterCreationLifestylesDraft draft) =>
        CharacterCreationFinalizationDigest.Compute(draft with { DraftDigest = string.Empty });

    public static bool IsValidShape(CharacterWorkspaceId id, long revision, CharacterCreationLifestylesDraft? draft)
        => draft is { Schema: CharacterCreationLifestylesSchemas.DraftV1, BaseContentRevision: > 0 }
            && draft.WorkspaceId == id && draft.BaseContentRevision < revision
            && CharacterCreationContactsDraftRules.IsValidShape(id, revision, draft.InputAuthority)
            && draft.InputAuthority.BaseContentRevision == draft.BaseContentRevision
            && draft.InputAuthority.Contacts.Count == 0
            && CharacterCreationFinalizationDigest.IsCanonical(draft.SkillsDraftDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(draft.ResourcesDraftDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(draft.GearDraftDigest)
            && (draft.MagicDraftDigest is null || CharacterCreationFinalizationDigest.IsCanonical(draft.MagicDraftDigest))
            && CharacterCreationFinalizationDigest.IsCanonical(draft.SourceAuthorityDigest)
            && CharacterCreationLifestylesRules.IsValidAuthority(draft.ProjectionAuthority)
        && CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(draft.ProjectionAuthority.PurchasedQualityCosts)
            && CharacterCreationKarmaLifestylesRules.TryFreeze(draft.Lifestyles, out _)
            && draft.DraftDigest == Digest(draft);

    internal static bool TryLoad(WorkspaceStoredDocument workspace, ICharacterSourceDataResolver source,
        out CharacterCreationLifestylesDraft? draft, out WorkspaceDocument? projected,
        out CharacterCreationLifestylesAuthority authority)
    {
        draft = null; projected = null; authority = CharacterCreationLifestylesAuthority.Unavailable;
        try
        {
            // Reuse the existing complete, read-only purchase/effect projection.
            // Contact choices are deliberately excluded: editing a contact does
            // not change lifestyle prices or retire an otherwise current purchase.
            if (!CharacterCreationContactsDraftRules.TryLoad(workspace, source, out var inputs, out _)
                || inputs is null) return false;
            inputs = CharacterCreationContactsDraftRules.Create(workspace, inputs.Policy, inputs.CarryoverPolicy, []);
            if (inputs is null || !CharacterCreationContactsDraftRules.TryProject(workspace, inputs, out var basis)
                || basis is null) return false;
            var context = source.TryCreateContext(basis.Content);
            if (context is null || !context.TryResolveCreationLifestylesAuthority(out authority)
                || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
                || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(authority.PurchasedQualityCosts)) return false;
            var existing = workspace.Document.AuxiliaryState.CharacterCreationLifestylesDraft;
            draft = Create(workspace, inputs, authority, existing?.Lifestyles ?? []);
            if (draft is null || existing is not null &&
                (!IsValidShape(workspace.Id, workspace.ContentRevision, existing)
                    || !InputsMatch(existing, draft) && !CanRepriceConfirmedGear(workspace, source, existing, draft))
                || !TryProject(workspace, draft, out projected, out _, out _)) return false;
            return context.TryResolveCreationLifestylesAuthority(out var observed)
                && observed.AuthorityDigest == authority.AuthorityDigest;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or IOException or OverflowException)
        { draft = null; projected = null; authority = CharacterCreationLifestylesAuthority.Unavailable; return false; }
    }

    internal static CharacterCreationLifestylesDraft? Create(WorkspaceStoredDocument workspace,
        CharacterCreationContactsDraft inputs, CharacterCreationLifestylesAuthority authority,
        IReadOnlyList<CharacterCreationLifestyleConfiguration> selections)
    {
        var state = workspace.Document.AuxiliaryState;
        if (state.CharacterCreationCompletionDrafts is { Count: > 0 } composites)
        {
            var origin = composites[^1].Baseline.BaselineDigest;
            if (!CharacterCreationCompositeBaselineReader.TryRead(workspace, out var composite) || composite is null
                || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
                || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(authority.PurchasedQualityCosts)
                || !CharacterCreationKarmaLifestylesRules.TryFreeze(selections, out var selected)) return null;
            var compositeDraft = new CharacterCreationLifestylesDraft(CharacterCreationLifestylesSchemas.DraftV1,
                workspace.Id, workspace.ContentRevision, inputs,
                state.CharacterCreationSkillsDraft?.DraftDigest ?? origin,
                state.CharacterCreationResourcesDraft?.DraftDigest ?? origin,
                state.CharacterCreationGearDraft?.DraftDigest ?? origin, state.CharacterCreationMagicResonanceDraft?.DraftDigest,
                authority.AuthorityDigest, authority, selected, string.Empty);
            return compositeDraft with { DraftDigest = Digest(compositeDraft) };
        }
        if (state.CharacterCreationSkillsDraft is not { } skills
            || state.CharacterCreationResourcesDraft is not { } resources
            || state.CharacterCreationGearDraft is not { } gear
            || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
            || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(authority.PurchasedQualityCosts)
            || !CharacterCreationKarmaLifestylesRules.TryFreeze(selections, out var frozen)) return null;
        var candidate = new CharacterCreationLifestylesDraft(CharacterCreationLifestylesSchemas.DraftV1,
            workspace.Id, workspace.ContentRevision, inputs, skills.DraftDigest, resources.DraftDigest,
            gear.DraftDigest, state.CharacterCreationMagicResonanceDraft?.DraftDigest, authority.AuthorityDigest,
            CharacterCreationKarmaLifestylesRules.ProjectionAuthority(authority, frozen), frozen, string.Empty);
        return candidate with { DraftDigest = Digest(candidate) };
    }

    internal static bool AdmitsRetainedCompletionHistory(WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver source, CharacterCreationLifestylesDraft draft)
    {
        if (!CharacterCreationCompositeBaselineReader.RetainsLifestyles(workspace, draft)
            || !IsValidShape(workspace.Id, workspace.ContentRevision, draft)
            || source.TryCreateContext(workspace.Document.Content) is not { } context
            || !context.TryResolveCreationContactsPolicy(out var contacts)
            || !context.TryResolveCreationCarryoverPolicy(out var carryover)
            || !CharacterCreationContactsDraftRules.AdmitsRetainedCompletionHistory(
                workspace, draft.InputAuthority, contacts, carryover)
            || !context.TryResolveCreationLifestylesAuthority(out var current)
            || !CharacterCreationLifestylesRules.IsValidAuthority(current)
            || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(current.PurchasedQualityCosts))
            return false;
        // Match selected source rows, not historical upstream effects (for
        // example Trust Fund). The completion loader applies today's quality
        // effects and invokes the real pricing rules for every configuration.
        var selected = CharacterCreationKarmaLifestylesRules.ProjectionAuthority(current, draft.Lifestyles);
        return current.SettingsProfileId == draft.ProjectionAuthority.SettingsProfileId
            && current.ProfileDigest == draft.ProjectionAuthority.ProfileDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                selected.LifestyleOptions, CharacterCreationKarmaLifestylesRules.ProjectionAuthority(draft.ProjectionAuthority, draft.Lifestyles).LifestyleOptions)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                selected.QualityOptions, CharacterCreationKarmaLifestylesRules.ProjectionAuthority(draft.ProjectionAuthority, draft.Lifestyles).QualityOptions);
    }

    internal static bool InputsMatch(CharacterCreationLifestylesDraft left, CharacterCreationLifestylesDraft right)
        => left.GearDraftDigest == right.GearDraftDigest && InputsMatchExceptGear(left, right);

    private static bool InputsMatchExceptGear(CharacterCreationLifestylesDraft left, CharacterCreationLifestylesDraft right)
        => left.WorkspaceId == right.WorkspaceId
            && CharacterCreationContactsDraftRules.InputsMatch(left.InputAuthority, right.InputAuthority)
            && left.SkillsDraftDigest == right.SkillsDraftDigest && left.ResourcesDraftDigest == right.ResourcesDraftDigest
            && left.MagicDraftDigest == right.MagicDraftDigest
            && left.SourceAuthorityDigest == right.SourceAuthorityDigest;

    private static bool CanRepriceConfirmedGear(WorkspaceStoredDocument workspace, ICharacterSourceDataResolver source,
        CharacterCreationLifestylesDraft retained, CharacterCreationLifestylesDraft current)
    {
        if (!InputsMatchExceptGear(retained, current)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                retained.ProjectionAuthority, current.ProjectionAuthority)) return false;

        // The ordinary Gear reader re-admits the current basket, exact source and
        // funding over this same observation. This read does not renew either
        // receipt or change the saved Lifestyle choices.
        var loaded = new CharacterCreationGearService(new WorkspaceContinuationReadView(workspace), source)
            .Load(new(workspace.Id));
        return loaded.Value is { PendingDraft: { } gear } state && state.Blockers.Count == 0
            && gear.DraftDigest == current.GearDraftDigest && HasConfirmedGearSuccessor(workspace, retained);
    }

    // A later Gear receipt changes the remaining wallet, not the price/identity
    // of an existing Lifestyle. Both live repricing and selected replay must prove
    // the original Gear anchor in the same valid chain with unchanged funding and
    // source identity. Other Lifestyle inputs remain exact at each caller.
    internal static bool HasConfirmedGearSuccessor(WorkspaceStoredDocument workspace, CharacterCreationLifestylesDraft retained)
    {
        var state = workspace.Document.AuxiliaryState;
        if (!IsValidShape(workspace.Id, workspace.ContentRevision, retained)
            || state.CharacterCreationGearDraft is not { } gear
            || gear.ResourcesDraftDigest != retained.ResourcesDraftDigest
            || gear.BaseRawCharacterXmlDigest != retained.InputAuthority.RawCharacterXmlDigest
            || gear.BaseContentRevision <= retained.BaseContentRevision
            || !CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(workspace.Id, workspace.ContentRevision,
                gear, state.CharacterCreationGearReceipts)) return false;
        return (state.CharacterCreationGearReceipts ?? []).Any(entry =>
            entry.Receipt.DraftDigest == retained.GearDraftDigest
            && entry.Receipt.WorkspaceRevision <= retained.BaseContentRevision
            && entry.Receipt.DraftRevision < gear.DraftRevision
            && entry.Receipt.RawCharacterXmlDigest == gear.BaseRawCharacterXmlDigest
            && entry.Receipt.ResourcesDraftRevision == gear.ResourcesDraftRevision
            && entry.Receipt.ResourcesDraftDigest == gear.ResourcesDraftDigest
            && entry.Receipt.AuthorityDigest == gear.AuthorityDigest
            && entry.Receipt.SourceDigest == gear.SourceDigest
            && entry.Receipt.RulesDigest == gear.RulesDigest
            && entry.Receipt.RuntimeDigest == gear.RuntimeDigest);
    }

    internal static bool TryProjectComposite(WorkspaceStoredDocument workspace, CharacterCreationLifestylesDraft draft,
        out WorkspaceDocument? projected, out CharacterCreationLifestyleProjection[] rows,
        out CharacterCreationLifestyleBudget? budget)
    {
        projected = null; rows = []; budget = null;
        if (!CharacterCreationCompositeBaselineReader.TryRead(workspace, out var composite) || composite is null
            || draft.WorkspaceId != workspace.Id || draft.DraftDigest != Digest(draft)
            || !CharacterCreationLifestylesRules.IsValidAuthority(draft.ProjectionAuthority)
            || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(draft.ProjectionAuthority.PurchasedQualityCosts)
            || !CharacterCreationKarmaLifestylesRules.TryFreeze(draft.Lifestyles, out var selections)
            || !CharacterCreationContactsDraftRules.TryProjectComposite(workspace, draft.InputAuthority, out var basis)
            || basis is null) return false;
        if (!CharacterCreationCompositePurchaseEdit.TryLifestyleFunds(
                workspace.Document.AuxiliaryState, draft, out decimal total)) return false;
        var result = new List<CharacterCreationLifestyleProjection>();
        foreach (var selection in selections)
        {
            if (!CharacterCreationLifestylesRules.TryProject(selection, draft.ProjectionAuthority, out var row, out _))
                return false;
            result.Add(row);
        }
        decimal used = result.Sum(row => row.Economics.TotalCost);
        budget = new(total, used, Math.Max(0m, total - used), Math.Max(0m, used - total), total >= 0,
            used > total ? [CharacterCreationLifestylesBlockers.InsufficientFunds] : [], CharacterCreationLifestyleSourceAnchors.All);
        var document = XDocument.Parse(basis.Content);
        document.Root!.SetElementValue("startingnuyen", total.ToString(CultureInfo.InvariantCulture));
        document.Root.Element("lifestyles")?.Remove();
        document.Root.Add(new XElement("lifestyles", result.Select(row =>
            CharacterCreationLifestylesService.BuildLifestyleElement(row, null, draft.ProjectionAuthority))));
        rows = result.ToArray();
        projected = new WorkspaceDocument(document.ToString(SaveOptions.DisableFormatting), workspace.Document.RulesetId);
        return true;
    }

    internal static bool TryProject(WorkspaceStoredDocument workspace, CharacterCreationLifestylesDraft draft,
        out WorkspaceDocument? projected, out CharacterCreationLifestyleProjection[] rows,
        out CharacterCreationLifestyleBudget? budget)
    {
        projected = null; rows = []; budget = null;
        if (workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts is not null)
            return TryProjectComposite(workspace, draft, out projected, out rows, out budget);
        try
        {
            var state = workspace.Document.AuxiliaryState;
            if (draft.WorkspaceId != workspace.Id || draft.DraftDigest != Digest(draft)
                || draft.InputAuthority.Contacts.Count != 0
                || state.CharacterCreationSkillsDraft?.DraftDigest != draft.SkillsDraftDigest
                || state.CharacterCreationResourcesDraft?.DraftDigest != draft.ResourcesDraftDigest
                || state.CharacterCreationGearDraft is not { } gear
                || gear.DraftDigest != draft.GearDraftDigest && !HasConfirmedGearSuccessor(workspace, draft)
                || state.CharacterCreationMagicResonanceDraft?.DraftDigest != draft.MagicDraftDigest
                || !CharacterCreationLifestylesRules.IsValidAuthority(draft.ProjectionAuthority)
                || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(draft.ProjectionAuthority.PurchasedQualityCosts)
                || draft.ProjectionAuthority.SettingsProfileId != draft.InputAuthority.Policy.SettingsProfileId
                || draft.ProjectionAuthority.ProfileDigest != draft.InputAuthority.Policy.RawProfileInputsDigest
                || !CharacterCreationKarmaLifestylesRules.TryFreeze(draft.Lifestyles, out var selections)
                || !CharacterCreationContactsDraftRules.TryProject(workspace, draft.InputAuthority, out var basis)
                || basis is null) return false;
            var result = new List<CharacterCreationLifestyleProjection>();
            foreach (var selection in selections)
            {
                if (!CharacterCreationLifestylesRules.TryProject(selection, draft.ProjectionAuthority,
                        out var row, out _)) return false;
                result.Add(row);
            }
            decimal total = gear.Budget.RemainingNuyen - (state.CharacterCreationLoadoutDraft?.PurchaseCost ?? 0m);
            decimal used = result.Sum(row => row.Economics.TotalCost);
            if (total < 0 || used < 0) return false;
            budget = new(total, used, Math.Max(0, total - used), Math.Max(0, used - total), true, [],
                CharacterCreationLifestyleSourceAnchors.All);
            var document = XDocument.Parse(basis.Content, LoadOptions.PreserveWhitespace);
            var root = document.Root!;
            root.SetElementValue("startingnuyen", total.ToString(CultureInfo.InvariantCulture));
            root.Element("lifestyles")?.Remove();
            root.Add(new XElement("lifestyles", result.Select(row =>
                CharacterCreationLifestylesService.BuildLifestyleElement(row, null, draft.ProjectionAuthority))));
            rows = result.ToArray();
            projected = new WorkspaceDocument(document.ToString(SaveOptions.DisableFormatting), workspace.Document.RulesetId);
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or IOException or OverflowException) { return false; }
    }
}
