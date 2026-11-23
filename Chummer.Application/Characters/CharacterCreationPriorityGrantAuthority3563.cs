using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// Native factories require a confirmed workspace selection. The separate V9
// factory accepts actual Windows-persisted selection fields bound to a retained
// current context. Neither factory accepts saved effects or a serialized plan
// as authority; both replay selected sources on every use and final fence.
internal sealed partial class CharacterCreationPriorityGrantAuthority3563
{
    private readonly CharacterCreationPrerequisiteDraft prerequisite;
    private readonly CharacterWorkspaceId workspaceId;
    private readonly long revision;
    private readonly string rawDigest;
    private readonly string sourceDigest;

    private CharacterCreationPriorityGrantAuthority3563(CharacterCreationPrerequisiteDraft prerequisite,
        CharacterWorkspaceId workspaceId, long revision, string rawDigest, string sourceDigest)
    {
        this.prerequisite = prerequisite; this.workspaceId = workspaceId;
        this.revision = revision; this.rawDigest = rawDigest; this.sourceDigest = sourceDigest;
    }

    internal static CharacterCreationPriorityGrantAuthority3563? Capture(
        WorkspaceStoredDocument observation, ICharacterSourceDataResolver? resolver, ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        try
        {
            using var operation = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var context = (operation ?? resolver)?.TryCreateContext(observation.Document.Content);
            return Capture(observation, context, recovery);
        }
        catch (Exception error) when (Malformed(error)) { return Unavailable(); }
    }

    internal static CharacterCreationPriorityGrantAuthority3563? Capture(
        WorkspaceStoredDocument observation, ICharacterSourceDataContext? context, ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        try
        {
            if (!CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(observation,
                    out var history, out _, recovery)) return Unavailable();
            if (history.CharacterCreationPrerequisiteDraft is null) return null;
            // An invalid/missing proof for recorded grants must not become the
            // no-grants path, even if an attacker removes every saved grant row.
            // Reexecute even a missing/zero plan before concluding that this
            // selection grants nothing. Resealing a removed plan is insufficient.
            if (context is null || !TryCapture(observation, context, out var proof, recovery)) return Unavailable();
            // A confirmed talent still determines special-attribute ranges and
            // selection-only unlocks when it grants no positive skill ratings.
            // Null is reserved for the genuinely absent-prerequisite legacy path.
            return proof;
        }
        catch (Exception error) when (Malformed(error)) { return Unavailable(); }
    }

    private static CharacterCreationPriorityGrantAuthority3563 Unavailable()
        => new(null!, default, 0, "", "");

    internal static bool TryCapture(WorkspaceStoredDocument observation, ICharacterSourceDataContext context,
        out CharacterCreationPriorityGrantAuthority3563? evidence, ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        evidence = null;
        try
        {
            if (!CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(observation,
                    out var history, out long historyRevision, recovery)
                || history.CharacterCreationPrerequisiteDraft is not { } draft)
                return false;
            // Snapshot nested mutable collections before validation. Never retain caller DTOs.
            var copy = JsonSerializer.Deserialize<CharacterCreationPrerequisiteDraft>(JsonSerializer.Serialize(draft))!;
            string raw = observation.Document.AuxiliaryState.CharacterCreationFinalizationArchive is null
                ? CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(observation.Document.Content)
                : observation.Document.AuxiliaryState.CharacterCreationFinalizationReceipts![0].Receipt.PreviousRawCharacterXmlDigest;
            var candidate = new CharacterCreationPriorityGrantAuthority3563(copy, observation.Id,
                historyRevision, raw, "");
            if (!candidate.Read(context, out _, out _, out _, out string digest)) return false;
            evidence = new(copy, observation.Id, historyRevision, raw, digest);
            return true;
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    internal bool TryResolve(XElement root, ICharacterSourceDataContext context,
        out CharacterCreationTalentSkillGrants grants,
        out IReadOnlyList<CharacterCreationTalentQualitySource> qualities, out string digest)
        => TryResolveSelection3785(root, context, out grants, out _, out qualities, out digest);

    // Selection authority is independent of positive rating grants. Reexecute the
    // private snapshot against current sources before exposing the same complete
    // group choices used by native finalization; saved effects never select them.
    internal bool TryResolveSelection3785(XElement root, ICharacterSourceDataContext context,
        out CharacterCreationTalentSkillGrants grants, out IReadOnlyList<string> selectedGroupNames,
        out IReadOnlyList<CharacterCreationTalentQualitySource> qualities, out string digest)
    {
        grants = null!; selectedGroupNames = []; qualities = []; digest = "";
        try
        {
            if (imported5693 is not null)
                return imported5693.TryResolve(root, context, out grants, out selectedGroupNames,
                    out qualities, out _, out _, out digest);
            if (!Read(context, out var current, out grants, out qualities, out digest)
                || digest != sourceDigest || !MatchesSelection(root, current)) return false;
            selectedGroupNames = CharacterCreationSkillsAccessRules.SelectedGroups(current);
            return true;
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    // Current native Career consumers may project the verified historical
    // selection, but cannot manufacture choices from saved improvement rows.
    internal bool TryResolveNativeSelection9569(XElement root, ICharacterSourceDataContext context,
        out CharacterCreationPrerequisiteDraft selection)
    {
        selection = null!;
        if (imported5693 is not null) return false;
        try
        {
            if (!Read(context, out var current, out _, out _, out string digest)
                || digest != sourceDigest || !MatchesSelection(root, current)) return false;
            selection = current;
            return true;
        }
        catch (Exception error) when (Malformed(error)) { return false; }
    }

    private bool Read(ICharacterSourceDataContext context, out CharacterCreationPrerequisiteDraft current,
        out CharacterCreationTalentSkillGrants grants,
        out IReadOnlyList<CharacterCreationTalentQualitySource> qualities, out string digest)
    {
        current = null!; grants = null!; qualities = []; digest = "";
        if (prerequisite is null || !context.TryResolveCreationPrerequisiteAuthority(out var authority)
            || !CharacterCreationPrerequisiteDraftIntegrity.TryEvaluateCurrentPending3053(prerequisite,
                workspaceId, revision, rawDigest, authority, context, out var evaluated)
            || evaluated is null
            || !CharacterCreationPrerequisiteService.TryExecuteSourceSelection718(authority, evaluated, out var executed)
            || executed is null
            || !context.TryResolveCreationSkillsCatalog(out var catalog)
            || !CharacterCreationSkillsCatalogAuthority.IsValid(catalog)
            || catalog!.SettingsProfileId != authority.SettingsProfileId
            || catalog.RawProfileInputsDigest != authority.RawProfileInputsDigest
            || catalog.SkillsInputsDigest != authority.EffectiveSkillsInputsDigest
            || !context.TryResolveCareerSettingsProfile(out string settingsXml, out string settingsState)) return false;
        var settings = CharacterSheetOwnerGraph.Parse(settingsXml);
        if (!CharacterCreationStandardPrioritySkillsRules.TryReadCreationRatingLimit(settings,
                "maxskillratingcreate", "maxskillrating", out int maximum)
            || !CharacterCreationStandardPrioritySkillsRules.IsSupportedCreationRatingLimit(maximum)
            || !CharacterCreationTalentSkillGrants.TryResolveCatalog3563(executed, catalog, maximum, out grants)
            || !CharacterCreationTalentQualityGrants.TryResolveTalent(executed, context, out var magic, out var talent))
            return false;
        current = executed;
        qualities = talent.GrantedQualitySources!;
        digest = CharacterCreationFinalizationDigest.Compute(new
        {
            Semantics = "priority-skill-grant-source/v1", Workspace = workspaceId, Revision = revision, Raw = rawDigest,
            Selection = current, PrerequisiteSource = authority, SkillsSource = catalog,
            MagicSource = magic.AuthorityDigest, Talent = talent, Settings = settingsState
        });
        return true;
    }

    private static bool MatchesSelection(XElement root, CharacterCreationPrerequisiteDraft draft)
    {
        if (CharacterSheetOwnerGraph.Text(root, "settings") != draft.SettingsProfileId
            || CharacterSheetOwnerGraph.Text(root, "buildmethod") != draft.BuildMethod
            || CharacterSheetOwnerGraph.Text(root, "prioritytalent") != draft.TalentSelection!.Value
            || CharacterSheetOwnerGraph.Text(root, "metatype") != draft.HeritageSelection!.MetatypeName
            || CharacterSheetOwnerGraph.Text(root, "metavariant", "") != (draft.HeritageSelection.MetavariantName ?? "")) return false;
        foreach (var assignment in draft.Assignments)
        {
            string field = assignment.CategoryId switch
            {
                CharacterCreationPriorityCategoryIds.Heritage => "prioritymetatype",
                CharacterCreationPriorityCategoryIds.Talent => "priorityspecial",
                CharacterCreationPriorityCategoryIds.Attributes => "priorityattributes",
                CharacterCreationPriorityCategoryIds.Skills => "priorityskills",
                CharacterCreationPriorityCategoryIds.Resources => "priorityresources",
                _ => throw new InvalidDataException("Unknown Priority category.")
            };
            string value = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{assignment.Rank},{assignment.SumToTenValue}");
            if (CharacterSheetOwnerGraph.Text(root, field) != value) return false;
        }
        return true;
    }

    private static bool Malformed(Exception error) => error is IOException or XmlException or JsonException
        or ArgumentException or InvalidOperationException or FormatException or OverflowException or NullReferenceException;
}
