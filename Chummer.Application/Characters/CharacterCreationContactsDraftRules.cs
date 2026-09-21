using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Calculates Contacts from a confirmed creation graph without changing its XML.</summary>
public static class CharacterCreationContactsDraftRules
{
    internal const string InputMarker = "creationcontactinputs";

    public static string Digest(CharacterCreationContactsDraft draft)
        => CharacterCreationFinalizationDigest.Compute(draft with { DraftDigest = string.Empty });

    public static bool IsValidShape(CharacterWorkspaceId id, long revision, CharacterCreationContactsDraft? draft)
        => draft is { Schema: CharacterCreationContactsSchemas.DraftV1, BaseContentRevision: > 0 }
            && draft.WorkspaceId == id && draft.BaseContentRevision < revision
            && CharacterCreationFinalizationDigest.IsCanonical(draft.RawCharacterXmlDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(draft.PrerequisiteDraftDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(draft.AttributesDraftDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(draft.QualitiesDraftDigest)
            && CharacterCreationKarmaContactsRules.IsValidPolicy(draft.Policy)
            && CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(draft.CarryoverPolicy)
            && CharacterCreationKarmaContactsRules.TryFreeze(draft.Contacts, out _)
            && draft.DraftDigest == Digest(draft);

    internal static bool TryLoad(WorkspaceStoredDocument workspace, ICharacterSourceDataResolver source,
        out CharacterCreationContactsDraft? draft, out WorkspaceDocument? projected)
    {
        draft = null;
        projected = null;
        try
        {
            // Contacts follow these confirmed drafts. Avoid re-opening the
            // source/quality catalog during every earlier dashboard refresh
            // when this calculation cannot yet succeed anyway.
            var state = workspace.Document.AuxiliaryState;
            if (state.CharacterCreationPrerequisiteDraft is null
                || state.CharacterCreationAttributesDraft is null
                || state.CharacterCreationSkillsDraft is null
                || state.CharacterCreationQualitiesDraft is null
                || state.CharacterCreationResourcesDraft is null
                || state.CharacterCreationGearDraft is null) return false;
            if (!CharacterCreationBootstrapAuthority.TryValidatePending(workspace, source, out _)) return false;
            var context = source.TryCreateContext(workspace.Document.Content);
            if (context is null || !context.TryResolveCreationContactsPolicy(out var policy) || policy is null
                || !context.TryResolveCreationCarryoverPolicy(out var carryover) || carryover is null
                || !HasCurrentQualityAuthority(workspace, context)) return false;
            var existing = workspace.Document.AuxiliaryState.CharacterCreationContactsDraft;
            if (existing is not null && (!IsValidShape(workspace.Id, workspace.ContentRevision, existing)
                || existing.Policy != policy && !Equal(existing.Policy, policy)
                || !Equal(existing.CarryoverPolicy, carryover))) return false;
            draft = Create(workspace, policy, carryover, existing?.Contacts ?? []);
            if (draft is null || existing is not null && !InputsMatch(existing, draft)
                || !TryProject(workspace, draft, out projected)) return false;
            // The source context fences file, overlay and directory changes.
            return context.TryResolveCreationContactsPolicy(out var afterPolicy) && Equal(policy, afterPolicy)
                && context.TryResolveCreationCarryoverPolicy(out var afterCarryover) && Equal(carryover, afterCarryover)
                && HasCurrentQualityAuthority(workspace, context);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or IOException or OverflowException) { draft = null; projected = null; return false; }
    }

    internal static CharacterCreationContactsDraft? Create(WorkspaceStoredDocument workspace,
        CharacterCreationKarmaContactsPolicy policy, CharacterCreationKarmaCarryoverPolicy carryover,
        IReadOnlyList<CharacterCreationKarmaContactSelection> contacts)
    {
        var state = workspace.Document.AuxiliaryState;
        if (state.CharacterCreationPrerequisiteDraft is not { } prerequisite
            || state.CharacterCreationAttributesDraft is not { } attributes
            || state.CharacterCreationQualitiesDraft is not { } qualities
            || !CharacterCreationKarmaContactsRules.TryFreeze(contacts, out var frozen)) return null;
        var result = new CharacterCreationContactsDraft(CharacterCreationContactsSchemas.DraftV1,
            workspace.Id, workspace.ContentRevision, RawDigest(workspace.Document), prerequisite.DraftDigest,
            attributes.DraftDigest, qualities.DraftDigest, policy, carryover, frozen, string.Empty);
        return result with { DraftDigest = Digest(result) };
    }

    internal static bool TryProject(WorkspaceStoredDocument workspace, CharacterCreationContactsDraft draft,
        out WorkspaceDocument? projected)
    {
        projected = null;
        try
        {
            var state = workspace.Document.AuxiliaryState;
            if (draft.WorkspaceId != workspace.Id || draft.RawCharacterXmlDigest != RawDigest(workspace.Document)
                || draft.DraftDigest != Digest(draft)
                || state.CharacterCreationBootstrapBinding is not { } bootstrap
                || bootstrap.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                || bootstrap.RawProfileInputsDigest != draft.Policy.RawProfileInputsDigest
                || bootstrap.SettingsProfileId != draft.Policy.SettingsProfileId
                || !CharacterCreationKarmaContactsRules.IsValidPolicy(draft.Policy)
                || state.CharacterCreationPrerequisiteDraft is not { } prerequisite
                || state.CharacterCreationAttributesDraft is not { } attributes
                || state.CharacterCreationQualitiesDraft is not { } qualities
                || draft.PrerequisiteDraftDigest != prerequisite.DraftDigest
                || draft.AttributesDraftDigest != attributes.DraftDigest
                || draft.QualitiesDraftDigest != qualities.DraftDigest
                || prerequisite.BaseRawCharacterXmlDigest != draft.RawCharacterXmlDigest
                || prerequisite.DraftDigest != CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(prerequisite)
                || !CharacterCreationAttributesDraftIntegrity.IsStructurallyValidPending(attributes, workspace.Id,
                    workspace.ContentRevision, draft.RawCharacterXmlDigest, prerequisite)
                || qualities.BaseRawCharacterXmlDigest != draft.RawCharacterXmlDigest
                || qualities.PrerequisiteDraftDigest != prerequisite.DraftDigest
                || qualities.AttributesDraftDigest != attributes.DraftDigest
                || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(workspace.Id, workspace.ContentRevision, state)
                || !CharacterCreationKarmaContactsRules.TryFreeze(draft.Contacts, out var contacts)
                || !CharacterCreationKarmaContactsRules.TryContactPoints(draft.Policy,
                    attributes.Attributes.ToDictionary(item => item.AttributeId, item => item.Current), out int allowance)
                || !CharacterCreationFinalizationProjector.TryProjectContactInputs(workspace, draft.CarryoverPolicy,
                    out string input, out _)) return false;
            XElement root = XDocument.Parse(input).Root!;
            if (root.Elements("contacts").Count() > 1
                || root.Element("contacts")?.Elements().Any() == true) return false;
            root.SetElementValue("contactpoints", allowance.ToString(CultureInfo.InvariantCulture));
            root.Element("contacts")?.Remove();
            root.Add(new XElement("contacts", contacts.Select(CharacterCreationKarmaContactsRules.BuildContactElement)));
            root.SetElementValue(InputMarker, CharacterCreationFinalizationDigest.Compute(new
            {
                draft.RawCharacterXmlDigest, draft.PrerequisiteDraftDigest, draft.AttributesDraftDigest,
                draft.QualitiesDraftDigest, draft.Policy.AuthorityDigest, Carryover = draft.CarryoverPolicy.AuthorityDigest
            }));
            projected = new WorkspaceDocument(root.ToString(SaveOptions.DisableFormatting), workspace.Document.RulesetId);
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or IOException or OverflowException) { return false; }
    }

    internal static bool InputsMatch(CharacterCreationContactsDraft left, CharacterCreationContactsDraft right)
        => left.WorkspaceId == right.WorkspaceId && left.RawCharacterXmlDigest == right.RawCharacterXmlDigest
            && left.PrerequisiteDraftDigest == right.PrerequisiteDraftDigest
            && left.AttributesDraftDigest == right.AttributesDraftDigest
            && left.QualitiesDraftDigest == right.QualitiesDraftDigest
            && Equal(left.Policy, right.Policy) && Equal(left.CarryoverPolicy, right.CarryoverPolicy);

    private static bool HasCurrentQualityAuthority(WorkspaceStoredDocument workspace, ICharacterSourceDataContext context)
        => workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft is { } prerequisite
            && workspace.Document.AuxiliaryState.CharacterCreationQualitiesDraft is { } qualities
            && context.TryResolveCreationQualitiesAuthority(out var authority)
            && authority.IsAuthoritative && authority.Blockers.Count == 0
            && CharacterCreationTalentQualityGrants.TryBind(prerequisite, context, authority, out authority)
            && authority.AuthorityDigest == qualities.AuthorityDigest;

    internal static bool TryReadSelections(XElement root, out CharacterCreationKarmaContactSelection[] selections)
    {
        selections = [];
        var result = new List<CharacterCreationKarmaContactSelection>();
        if (root.Elements("contacts").Count() != 1) return false;
        foreach (var contact in root.Element("contacts")!.Elements())
        {
            if (contact.Name != "contact" || contact.Element("type")?.Value != "Contact"
                || !Guid.TryParseExact(contact.Element("guid")?.Value, "D", out var id)
                || !CharacterContactEditSemanticsResolver.TryResolve(root, contact, out var semantics)) return false;
            result.Add(new(id, CharacterCreationContactsService.ReadIdentity(contact), semantics.Connection,
                semantics.Loyalty, semantics.IsGroup, semantics.Free, semantics.Family, semantics.Blackmail));
        }
        return CharacterCreationKarmaContactsRules.TryFreeze(result, out selections);
    }

    internal static string[] ValidatePointOnlySelection(WorkspaceDocument projected)
    {
        var authority = CharacterCreationContactsAuthorityEvaluator.Evaluate(projected);
        var blockers = authority.AuthorityBlockers.Concat(authority.ContactBudget.Blockers)
            .Concat(authority.HighPlacesBudget.Blockers).ToList();
        XElement root = XDocument.Parse(projected.Content).Root!;
        if (!TryReadSelections(root, out var selections))
            blockers.Add(CharacterCreationContactsBlockers.ContactInvalid);
        else
        {
            // A paid group is a positive-quality Karma contribution, not a
            // zero-cost contact. Do not admit it before that shared-budget
            // contribution is integrated into this pending-draft lane.
            if (selections.Any(selection => selection.IsGroup && !selection.Free))
                blockers.Add("creation-contacts-karma-contribution-unavailable");
            if (authority.Contacts.Any(contact => contact.ContactPointCost > 7
                    && !contact.CountsAgainstHighPlacesBudget))
                blockers.Add(CharacterCreationKarmaContactsRules.ContactLimitExceeded);
        }
        return blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal static bool Equal<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    internal static string RawDigest(WorkspaceDocument document)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(document.Content);
}
