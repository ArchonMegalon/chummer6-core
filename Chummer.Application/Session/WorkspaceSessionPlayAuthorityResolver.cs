using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Application.Workspaces;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Chummer.Contracts.Trackers;
using Chummer.Contracts.Workspaces;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

/// <summary>Reads one owner-scoped immutable workspace. Never writes a canonical character.</summary>
public sealed class WorkspaceSessionPlayAuthorityResolver : ISessionPlayAuthorityResolver
{
    private readonly IWorkspaceService _workspaces;
    private readonly IActiveRuntimeStatusService _runtime;
    private readonly ISessionProfileSelectionStore _profiles;
    private readonly ICharacterSourceDataResolver _sources;
    private readonly ISessionPlayWorkspaceAuthoritySource? _observations;
    public WorkspaceSessionPlayAuthorityResolver(IWorkspaceService workspaces, IActiveRuntimeStatusService runtime,
        ISessionProfileSelectionStore profiles, ICharacterSourceDataResolver sources, ISessionPlayWorkspaceAuthoritySource? observations = null)
    { _workspaces = workspaces; _runtime = runtime; _profiles = profiles; _sources = sources; _observations = observations; }

    public SessionFocusCurrent7490 ResolveFocusCurrentWithOwnerLease7490(IOwnerContextLeaseAccessor owners,
        OwnerContextStamp expected, IOwnerContextLease lease, string characterId)
    {
        string? sourceEnvironment = null;
        var binding = _observations?.ReadContinuationCurrent5699(owners, expected, lease, characterId,
            (saved, sources, environment, current) => {
                sourceEnvironment = environment;
                return ResolveObserved5262(expected.Owner, characterId, saved, sources, true, environment, current);
            }) ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("focus-continuation-current-source-unavailable"));
        Conflict(!string.IsNullOrWhiteSpace(sourceEnvironment), "focus-continuation-current-source-unavailable");
        return new(binding, sourceEnvironment!);
    }
    public SessionFocusSourceObservation7490 ObserveFocusSource7490(OwnerScope owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver sources, string? environment, CharacterCurrentRuleEnvironmentContext? context)
    {
        Conflict(context is not null && ReferenceEquals(context.Current, saved) && context.VerifyRetainedArchive()
            && !string.IsNullOrWhiteSpace(environment), "focus-continuation-retained-source-required");
        var capture = context!.Capture;
        // IntegrityDigest includes character input. Compare the retained source closure metadata instead;
        // the canonical store's actual ReadmitProposedCurrentEnvironment owns same-closure recapture.
        string rules = Digest(new { capture.CompatibilityProfile, capture.EffectivePolicyDigest,
            capture.RequiredPackageSetDigest, capture.RequiredRulePacks, capture.RequiredAssets,
            capture.OrderedCustomSources, capture.CompatibilityDiagnostics, capture.SourcebookSelection,
            context.SemanticsProfile, context.SemanticsFingerprint });
        var value = ResolveObserved5262(owner, saved.Id.Value, saved, sources, true, environment,
            CurrentCharacterRuleSourceSemantics.IsImportedPriorityCareerProfile5667(context.SemanticsProfile,
                context.SemanticsFingerprint) ? context : null);
        Conflict(context.VerifyRetainedArchive(), "focus-continuation-source-changed");
        return new(value, rules);
    }

    public SessionConditionTransferBinding ResolveContinuationWithOwnerLease(IOwnerContextLeaseAccessor owners,
        OwnerContextStamp expected, IOwnerContextLease lease, string characterId)
        => _observations?.ReadContinuationCurrent5699(owners, expected, lease, characterId,
            (workspace, resolver, environment, current) => ResolveObserved5262(expected.Owner, characterId, workspace, resolver, true, environment, current))
            ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("session-continuation-custody-unavailable"));

    public SessionPlayAuthority ResolveWithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected, IOwnerContextLease lease, string characterId)
        => _observations?.ReadCurrent5699(owners, expected, lease, characterId,
            (workspace, sources, environment, current) => ResolveObserved5262(expected.Owner, characterId, workspace, sources, true, environment, current))
            ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("session-current-source-store-unavailable"));

    public SessionPlayAuthority Resolve(OwnerScope owner, string characterId)
    {
        Identity(owner.NormalizedValue, "owner-required");
        Require(!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser, "trusted-local-owner-required");
        Identity(characterId, "character-required");
        var read = _workspaces.GetWorkspaceObservation(owner, new CharacterWorkspaceId(characterId));
        var workspace = read.Success ? read.Value : null;
        if (workspace is null) throw new SessionPlayException(SessionPlayFailures.Unavailable("owner-workspace-unavailable"));
        return ResolveObserved5262(owner, characterId, workspace, _sources, false);
    }

    public SessionPlayAuthority ResolveObserved5262(OwnerScope owner, string characterId, WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver sourceResolver, bool sourceAdmission, string? environmentDigest = null,
        CharacterCurrentRuleEnvironmentContext? importedCurrent5699 = null)
    {
        Identity(owner.NormalizedValue, "owner-required"); Identity(characterId, "character-required");
        Require(!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser, "trusted-local-owner-required");
        var document = workspace.Document;
        Conflict(document.State.RulesetId == "sr5", "edition-not-supported");
        var active = _runtime.GetActiveProfileStatus(owner, "sr5");
        if (active is null || active.RulesetId != "sr5" || string.IsNullOrWhiteSpace(active.RuntimeFingerprint))
            throw new SessionPlayException(SessionPlayFailures.Unavailable("effective-runtime-unavailable"));
        var selected = _profiles.Get(owner, characterId);
        Conflict(selected is null || selected.RulesetId == "sr5" && selected.RuntimeFingerprint == active.RuntimeFingerprint,
            "selected-runtime-differs-from-character-runtime");
        string xml = document.State.Payload;
        var root = Parse(xml);
        Require(root.Name == "character" && Text(root, "gameedition") == "SR5" && Boolean(root, "created"), "sr5-career-required");
        var retainedResolver = importedCurrent5699 is null ? sourceResolver
            : importedCurrent5699.Capture.CreateCurrentEnvironmentResolution(
                importedCurrent5699.SemanticsProfile, importedCurrent5699.SemanticsFingerprint)?.Resolver
                ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("session-imported-retained-resolution-unavailable"));
        using var scope = (retainedResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var sources = scope ?? retainedResolver;
        if (importedCurrent5699 is not null)
            Conflict(sourceAdmission && !string.IsNullOrWhiteSpace(environmentDigest)
                && ReferenceEquals(importedCurrent5699.Current, workspace)
                && importedCurrent5699.VerifyRetainedArchive()
                && CurrentCharacterRuleSourceSemantics.IsImportedPriorityCareerProfile5667(
                    importedCurrent5699.SemanticsProfile, importedCurrent5699.SemanticsFingerprint),
                "session-imported-current-observation-changed");
        var diagnostics = new List<SessionPlayDiagnostic>();
        var ammunitionDiagnostics5701 = new List<SessionPlayDiagnostic>();
        var derived = CharacterSheetDerivedQuery.ParsePlayAmmunition5701(workspace, sources, ammunitionDiagnostics5701,
            out var weapons, out var ammunition, importedCurrent5699);
        var trackers = new List<TrackerDefinition>();
        int? edge = Attribute(derived, "EDG");
        int edgeUsed = Integer(root, "edgeused", 0);
        if (edge is >= 0 && edgeUsed >= 0 && edgeUsed <= edge &&
            CharacterCareerEdgeUseRules.TryProject(true, edgeUsed, edge.Value, out var edgeState))
            trackers.Add(new("edge", TrackerCategories.Resource, "Edge", edgeState!.AvailableEdge, 0, edge, []));
        else diagnostics.Add(new("edge-authority-unavailable", "edge", "Source-bound Edge total and saved use must both resolve."));

        bool retainedCondition5412 = sourceAdmission && workspace.ContentRevision == workspace.SavedRevision
            && !string.IsNullOrWhiteSpace(environmentDigest);
        if (retainedCondition5412) SessionPlayInitialConditionSource5412.ValidateScalars(root);
        var condition = derived.Condition;
        bool wounds = condition is not null && derived.UnavailableReason is null
            && !Boolean(root, "physicalcmiscorecm", false) && !Boolean(root, "stuncmismatrixcm", false)
            && Integer(root, "physicalcmoverflow", 0) == 0;
        if (wounds)
        {
            int physical = condition!.PhysicalMaximum, stun = condition.StunMaximum;
            int p = Integer(root, "physicalcmfilled", 0), s = Integer(root, "stuncmfilled", 0);
            Require(p >= 0 && (retainedCondition5412 || p <= physical) && s >= 0 && s <= stun, "condition-monitor-outside-supported-track");
            // Split cumulative damage explicitly; the immutable source condition retains every overflow box.
            trackers.Add(new("physical", TrackerCategories.Condition, "Physical damage", Math.Min(p, physical), 0, physical, []));
            trackers.Add(new("stun", TrackerCategories.Condition, "Stun damage", s, 0, stun, []));
        }
        else diagnostics.Add(new("condition-authority-unavailable", "damage", "Exact source-derived condition authority is required; matrix/core tracks and overflow remain unsupported."));
        SessionPlaySustainingAuthority? sustaining = null;
        SessionPlayCastingAuthority? casting = null;
        if (wounds)
        {
            // A clamped wounded pool cannot safely be healed by adding a delta. Capture an uninjured
            // source projection, then replay all wounds against it. This detached XML never reaches a store.
            var neutralImported = importedCurrent5699 is null ? null
                : CharacterSheetDerivedQuery.ParseImportedCareer5688(importedCurrent5699,
                    projectUninjured: true, retainOverflow5412: retainedCondition5412);
            var neutral = neutralImported is not null ? neutralImported.Sheet : retainedCondition5412
                ? CharacterSheetDerivedQuery.ParsePlayInitialCondition5412(workspace, sources, out sustaining, out casting)
                : sourceAdmission
                ? CharacterSheetDerivedQuery.ParsePlayCasting5296(workspace, sources, out sustaining, out casting)
                : CharacterSheetDerivedQuery.ParseUninjured3659(workspace, sources);
            Require(neutral.UnavailableReason is null && neutral.PhysicalInitiative is not null,
                "uninjured-derived-authority-unavailable");
            if (neutralImported is not null)
            {
                sustaining = neutralImported.Sustaining;
                casting = neutralImported.Casting;
                Conflict(neutralImported.IsCurrent, "session-imported-current-observation-changed");
            }
            derived = neutral;
        }
        diagnostics.Add(new("local-owner-overlay-only", "campaign-session",
            "This local owner overlay does not authorize campaign roles, lifecycle transitions or canonical settlement."));
        diagnostics.Add(new("portable-runtime-bundle-unavailable", "runtime-bundle",
            "The dashboard carries a projection-only descriptor, not a signed portable execution bundle."));
        if (retainedCondition5412 && document.AuxiliaryState.SessionCondition is { State.Stabilized: not null })
            diagnostics.Add(new("canonical-condition-facts-retained", "damage-overflow", "The canonical condition retains the reviewed stabilization fact and excess Stun carry."));
        else if (retainedCondition5412 && condition is not null && Integer(root, "physicalcmfilled", 0) > condition.PhysicalMaximum)
            diagnostics.Add(new("initial-overflow-table-fact-required", "damage-overflow", "Cumulative saved physical damage includes overflow. Stabilization is not stored by the canonical character; record the actual table fact before treatment. The actor cannot act."));
        else diagnostics.Add(new("overflow-conversion-unavailable", "damage-overflow", "This adapter admits only in-track damage; overflow and stun conversion require a typed transition."));
        if (derived.UnavailableReason is not null)
            diagnostics.Add(new("derived-authority-unavailable", "derived-pools", derived.UnavailableReason));
        if (derived.PhysicalInitiative is null || !wounds)
            diagnostics.Add(new("initiative-authority-unavailable", "initiative", "Physical initiative needs an exact source projection and supported wound authority."));
        diagnostics.Add(new("effects-annotation-only", "effects", "Effect labels are private annotations; they do not authorize mechanical dice or tracker changes."));
        diagnostics.Add(new("consumption-authority-unavailable", "consumables", "Gear quantity and purchase semantics do not authorize drug/equipment consumption effects."));
        diagnostics.Add(new("external-sync-unavailable", "external-sync", "Local replica reconciliation has no remote transport or server receipt."));
        ammunition = SessionAmmunitionSettlementRules5712.RetainFeed(ammunition, document.AuxiliaryState.SessionPlaySettlement5712);
        diagnostics.AddRange(ammunitionDiagnostics5701);
        var version = new CharacterVersionReference(characterId, workspace.ContentRevision.ToString(CultureInfo.InvariantCulture),
            "sr5", active.RuntimeFingerprint);
        var binding = new SessionPlayBinding(owner.NormalizedValue, version, Digest(document.State), string.Empty);
        var vehiclePolicy = SessionPlayVehiclePolicySource5400.TryProject(sources, xml, binding,
            sourceAdmission && workspace.SavedRevision == workspace.ContentRevision ? environmentDigest : null, diagnostics);
        var authority = new SessionPlayAuthority(binding, trackers, derived, wounds, weapons, diagnostics) {
            VehiclePolicy = vehiclePolicy,
            SettledOperations5712 = SessionAmmunitionSettlementRules5712.SettledOperations(document.AuxiliaryState.SessionPlaySettlement5712),
            InitialCondition5412 = retainedCondition5412 && wounds
                ? SessionPlayInitialConditionSource5412.Project(root, derived, binding, environmentDigest!, document.AuxiliaryState.SessionCondition) : null,
            CastingAuthority = casting is null || workspace.SavedRevision != workspace.ContentRevision ? null
                : SessionPlayCasting.Bind(casting, binding, Digest(new { owner.IsLocalSingleUser,
                    workspace.Id, workspace.ContentRevision, workspace.SavedRevision, workspace.Document, environmentDigest })),
            Ammunition = ammunition, SustainingAuthority = sustaining is null || workspace.SavedRevision != workspace.ContentRevision ? null
                : SessionPlaySustainingEffects.Bind(sustaining, binding, Digest(new { owner.IsLocalSingleUser,
                    workspace.Id, workspace.ContentRevision, workspace.SavedRevision, workspace.Document, environmentDigest }), environmentDigest) };
        if (importedCurrent5699 is not null)
            Conflict(importedCurrent5699.VerifyRetainedArchive(), "session-imported-current-observation-changed");
        return authority with { Binding = binding with { AuthorityDigest = Digest(authority) } };
    }

    private static int? Attribute(CharacterSheetDerivedSection sheet, string name)
    {
        var rows = sheet.Attributes.Where(a => a.Name == name).ToArray();
        return rows.Length == 1 ? rows[0].Total : null;
    }
    internal static XElement Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XElement.Load(reader);
    }
    internal static XElement? One(XElement parent, string name)
    {
        var rows = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
        Require(rows.Length <= 1 && rows.All(e => e.Name.NamespaceName.Length == 0), "ambiguous-source-field");
        return rows.SingleOrDefault();
    }
    internal static string Text(XElement parent, string name, string? absent = null)
    {
        var row = One(parent, name);
        if (row is null) return absent ?? throw new SessionPlayException(SessionPlayFailures.Invalid("missing-source-field"));
        Require(!row.HasAttributes && !row.HasElements, "invalid-source-field");
        return row.Value;
    }
    internal static int Integer(XElement parent, string name, int? absent = null)
    {
        Require(int.TryParse(Text(parent, name, absent?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value), "invalid-source-integer");
        return value;
    }
    internal static bool Boolean(XElement parent, string name, bool? absent = null)
    {
        Require(bool.TryParse(Text(parent, name, absent?.ToString()), out bool value), "invalid-source-boolean");
        return value;
    }
}
