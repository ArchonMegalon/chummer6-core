using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Source-bound lifestyle purchases over the confirmed Priority/Sum-to-Ten draft graph.</summary>
public static class CharacterCreationLifestylesDraftRules
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
                || !CharacterCreationLifestylesRules.IsValidAuthority(authority)) return false;
            var existing = workspace.Document.AuxiliaryState.CharacterCreationLifestylesDraft;
            draft = Create(workspace, inputs, authority, existing?.Lifestyles ?? []);
            if (draft is null || existing is not null &&
                (!IsValidShape(workspace.Id, workspace.ContentRevision, existing) || !InputsMatch(existing, draft))
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
        if (state.CharacterCreationSkillsDraft is not { } skills
            || state.CharacterCreationResourcesDraft is not { } resources
            || state.CharacterCreationGearDraft is not { } gear
            || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
            || !CharacterCreationKarmaLifestylesRules.TryFreeze(selections, out var frozen)) return null;
        var candidate = new CharacterCreationLifestylesDraft(CharacterCreationLifestylesSchemas.DraftV1,
            workspace.Id, workspace.ContentRevision, inputs, skills.DraftDigest, resources.DraftDigest,
            gear.DraftDigest, state.CharacterCreationMagicResonanceDraft?.DraftDigest, authority.AuthorityDigest,
            CharacterCreationKarmaLifestylesRules.ProjectionAuthority(authority, frozen), frozen, string.Empty);
        return candidate with { DraftDigest = Digest(candidate) };
    }

    internal static bool InputsMatch(CharacterCreationLifestylesDraft left, CharacterCreationLifestylesDraft right)
        => left.WorkspaceId == right.WorkspaceId
            && CharacterCreationContactsDraftRules.InputsMatch(left.InputAuthority, right.InputAuthority)
            && left.SkillsDraftDigest == right.SkillsDraftDigest && left.ResourcesDraftDigest == right.ResourcesDraftDigest
            && left.GearDraftDigest == right.GearDraftDigest && left.MagicDraftDigest == right.MagicDraftDigest
            && left.SourceAuthorityDigest == right.SourceAuthorityDigest;

    internal static bool TryProject(WorkspaceStoredDocument workspace, CharacterCreationLifestylesDraft draft,
        out WorkspaceDocument? projected, out CharacterCreationLifestyleProjection[] rows,
        out CharacterCreationLifestyleBudget? budget)
    {
        projected = null; rows = []; budget = null;
        try
        {
            var state = workspace.Document.AuxiliaryState;
            if (draft.WorkspaceId != workspace.Id || draft.DraftDigest != Digest(draft)
                || draft.InputAuthority.Contacts.Count != 0
                || state.CharacterCreationSkillsDraft?.DraftDigest != draft.SkillsDraftDigest
                || state.CharacterCreationResourcesDraft?.DraftDigest != draft.ResourcesDraftDigest
                || state.CharacterCreationGearDraft is not { } gear || gear.DraftDigest != draft.GearDraftDigest
                || state.CharacterCreationMagicResonanceDraft?.DraftDigest != draft.MagicDraftDigest
                || !CharacterCreationLifestylesRules.IsValidAuthority(draft.ProjectionAuthority)
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
            decimal total = gear.Budget.RemainingNuyen;
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
