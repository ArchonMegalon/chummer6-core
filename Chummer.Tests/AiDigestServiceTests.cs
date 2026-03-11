#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Application.Content;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.AI;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiDigestServiceTests
{
    [TestMethod]
    public void Default_ai_digest_service_projects_runtime_character_and_session_summaries()
    {
        CharacterFileSummary summary = new(
            Name: "Cipher",
            Alias: "Ghostwire",
            Metatype: "Human",
            BuildMethod: "Priority",
            CreatedVersion: "5",
            AppVersion: "10",
            Karma: 18m,
            Nuyen: 1500m,
            Created: true);
        WorkspaceListItem workspace = new(
            Id: new CharacterWorkspaceId("char-7"),
            Summary: summary,
            LastUpdatedUtc: new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero),
            RulesetId: "sr5",
            HasSavedWorkspace: true);
        RuntimeLockRegistryEntry runtimeLock = new(
            LockId: "sha256:coach",
            Owner: OwnerScope.LocalSingleUser,
            Title: "Street-Level Runtime Lock",
            Visibility: ArtifactVisibilityModes.LocalOnly,
            CatalogKind: RuntimeLockCatalogKinds.Saved,
            RuntimeLock: new ResolvedRuntimeLock(
                RulesetId: "sr5",
                ContentBundles:
                [
                    new ContentBundleDescriptor("official.sr5.core", "sr5", "1.0.0", "SR5 Core", "Official core bundle", ["data/core.xml"])
                ],
                RulePacks:
                [
                    new ArtifactVersionReference("campaign.street-level", "2.0.0")
                ],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["availability.item"] = "official.sr5.core:availability.item"
                },
                EngineApiVersion: "1.0.0",
                RuntimeFingerprint: "sha256:coach"),
            UpdatedAtUtc: new DateTimeOffset(2026, 3, 7, 11, 0, 0, TimeSpan.Zero),
            Description: "Street-level campaign runtime lock",
            Install: new ArtifactInstallState(ArtifactInstallStates.Pinned, RuntimeFingerprint: "sha256:coach"));
        SessionCharacterListItem sessionCharacter = new(
            CharacterId: "char-7",
            DisplayName: "Cipher (Ghostwire)",
            RulesetId: "sr5",
            RuntimeFingerprint: "sha256:coach");
        SessionRuntimeStatusProjection sessionRuntime = new(
            CharacterId: "char-7",
            SelectionState: SessionRuntimeSelectionStates.Selected,
            ProfileId: "official.sr5.core",
            ProfileTitle: "Official SR5 Core",
            RulesetId: "sr5",
            RuntimeFingerprint: "sha256:coach",
            SessionReady: true,
            BundleFreshness: SessionRuntimeBundleFreshnessStates.Current,
            RequiresBundleRefresh: false);
        DefaultAiDigestService service = new(
            new StubRuntimeLockRegistryService(runtimeLock),
            new StubWorkspaceService(workspace),
            new StubSessionService(sessionCharacter, sessionRuntime));

        AiRuntimeSummaryProjection? runtimeSummary = service.GetRuntimeSummary(OwnerScope.LocalSingleUser, "sha256:coach");
        AiCharacterDigestProjection? characterDigest = service.GetCharacterDigest(OwnerScope.LocalSingleUser, "char-7");
        AiSessionDigestProjection? sessionDigest = service.GetSessionDigest(OwnerScope.LocalSingleUser, "char-7");

        Assert.IsNotNull(runtimeSummary);
        Assert.AreEqual("sha256:coach", runtimeSummary.RuntimeFingerprint);
        Assert.AreEqual("sr5", runtimeSummary.RulesetId);
        CollectionAssert.Contains(runtimeSummary.ContentBundles.ToArray(), "official.sr5.core@1.0.0");
        CollectionAssert.Contains(runtimeSummary.RulePacks.ToArray(), "campaign.street-level@2.0.0");
        Assert.AreEqual("official.sr5.core:availability.item", runtimeSummary.ProviderBindings["availability.item"]);

        Assert.IsNotNull(characterDigest);
        Assert.AreEqual("char-7", characterDigest.CharacterId);
        Assert.AreEqual("Cipher (Ghostwire)", characterDigest.DisplayName);
        Assert.AreEqual("sha256:coach", characterDigest.RuntimeFingerprint);
        Assert.AreEqual(18m, characterDigest.Summary.Karma);
        Assert.IsTrue(characterDigest.HasSavedWorkspace);

        Assert.IsNotNull(sessionDigest);
        Assert.AreEqual("char-7", sessionDigest.CharacterId);
        Assert.AreEqual(SessionRuntimeSelectionStates.Selected, sessionDigest.SelectionState);
        Assert.AreEqual(SessionRuntimeBundleFreshnessStates.Current, sessionDigest.BundleFreshness);
        Assert.AreEqual("official.sr5.core", sessionDigest.ProfileId);
        Assert.IsTrue(sessionDigest.SessionReady);
    }

    [TestMethod]
    public void Default_ai_digest_service_returns_null_for_missing_runtime_character_and_session_ids()
    {
        DefaultAiDigestService service = new(
            new StubRuntimeLockRegistryService(null),
            new StubWorkspaceService(),
            new StubSessionService());

        Assert.IsNull(service.GetRuntimeSummary(OwnerScope.LocalSingleUser, "missing"));
        Assert.IsNull(service.GetCharacterDigest(OwnerScope.LocalSingleUser, "missing"));
        Assert.IsNull(service.GetSessionDigest(OwnerScope.LocalSingleUser, "missing"));
    }

    private sealed class StubRuntimeLockRegistryService : IRuntimeLockRegistryService
    {
        private readonly RuntimeLockRegistryEntry? _entry;

        public StubRuntimeLockRegistryService(RuntimeLockRegistryEntry? entry)
        {
            _entry = entry;
        }

        public RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null)
            => _entry is null
                ? new RuntimeLockRegistryPage([], 0)
                : new RuntimeLockRegistryPage([_entry], 1);

        public RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null)
            => _entry is not null && string.Equals(_entry.LockId, lockId, StringComparison.Ordinal)
                ? _entry
                : null;

        public RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request)
            => throw new NotSupportedException();
    }

    private sealed class StubWorkspaceService : IWorkspaceService
    {
        private readonly WorkspaceListItem[] _workspaces;

        public StubWorkspaceService(params WorkspaceListItem[] workspaces)
        {
            _workspaces = workspaces;
        }

        public WorkspaceImportResult Import(WorkspaceImportDocument document) => throw new NotSupportedException();

        public WorkspaceImportResult Import(OwnerScope owner, WorkspaceImportDocument document) => throw new NotSupportedException();

        public IReadOnlyList<WorkspaceListItem> List(int? maxCount = null) => List(OwnerScope.LocalSingleUser, maxCount);

        public IReadOnlyList<WorkspaceListItem> List(OwnerScope owner, int? maxCount = null)
            => maxCount is > 0 ? _workspaces.Take(maxCount.Value).ToArray() : _workspaces;

        public bool Close(CharacterWorkspaceId id) => throw new NotSupportedException();

        public bool Close(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public object? GetSection(CharacterWorkspaceId id, string sectionId) => throw new NotSupportedException();

        public object? GetSection(OwnerScope owner, CharacterWorkspaceId id, string sectionId) => throw new NotSupportedException();

        public CharacterFileSummary? GetSummary(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterFileSummary? GetSummary(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterValidationResult? Validate(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterValidationResult? Validate(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProfileSection? GetProfile(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProfileSection? GetProfile(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProgressSection? GetProgress(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProgressSection? GetProgress(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterSkillsSection? GetSkills(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterSkillsSection? GetSkills(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterRulesSection? GetRules(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterRulesSection? GetRules(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterBuildSection? GetBuild(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterBuildSection? GetBuild(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterMovementSection? GetMovement(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterMovementSection? GetMovement(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterAwakeningSection? GetAwakening(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterAwakeningSection? GetAwakening(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<CharacterProfileSection> UpdateMetadata(CharacterWorkspaceId id, UpdateWorkspaceMetadata command) => throw new NotSupportedException();

        public CommandResult<CharacterProfileSection> UpdateMetadata(OwnerScope owner, CharacterWorkspaceId id, UpdateWorkspaceMetadata command) => throw new NotSupportedException();

        public CommandResult<WorkspaceSaveReceipt> Save(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceSaveReceipt> Save(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceDownloadReceipt> Download(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceDownloadReceipt> Download(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceExportReceipt> Export(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceExportReceipt> Export(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspacePrintReceipt> Print(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspacePrintReceipt> Print(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();
    }

    private sealed class StubSessionService : ISessionService
    {
        private readonly SessionCharacterListItem? _sessionCharacter;
        private readonly SessionRuntimeStatusProjection? _runtimeStatus;

        public StubSessionService(
            SessionCharacterListItem? sessionCharacter = null,
            SessionRuntimeStatusProjection? runtimeStatus = null)
        {
            _sessionCharacter = sessionCharacter;
            _runtimeStatus = runtimeStatus;
        }

        public SessionApiResult<SessionCharacterCatalog> ListCharacters(OwnerScope owner)
            => SessionApiResult<SessionCharacterCatalog>.Implemented(
                new SessionCharacterCatalog(_sessionCharacter is null ? [] : [_sessionCharacter]));

        public SessionApiResult<SessionDashboardProjection> GetCharacterProjection(OwnerScope owner, string characterId)
            => throw new NotSupportedException();

        public SessionApiResult<SessionOverlaySnapshot> ApplyCharacterPatches(OwnerScope owner, string characterId, SessionPatchRequest? request)
            => throw new NotSupportedException();

        public SessionApiResult<SessionSyncReceipt> SyncCharacterLedger(OwnerScope owner, string characterId, SessionSyncBatch? batch)
            => throw new NotSupportedException();

        public SessionApiResult<SessionProfileCatalog> ListProfiles(OwnerScope owner)
            => throw new NotSupportedException();

        public SessionApiResult<SessionRuntimeStatusProjection> GetRuntimeState(OwnerScope owner, string characterId)
            => _runtimeStatus is null
                ? SessionApiResult<SessionRuntimeStatusProjection>.Implemented(new SessionRuntimeStatusProjection(characterId, SessionRuntimeSelectionStates.Unselected))
                : SessionApiResult<SessionRuntimeStatusProjection>.Implemented(_runtimeStatus);

        public SessionApiResult<SessionRuntimeBundleIssueReceipt> GetRuntimeBundle(OwnerScope owner, string characterId)
            => throw new NotSupportedException();

        public SessionApiResult<SessionRuntimeBundleRefreshReceipt> RefreshRuntimeBundle(OwnerScope owner, string characterId)
            => throw new NotSupportedException();

        public SessionApiResult<SessionProfileSelectionReceipt> SelectProfile(OwnerScope owner, string characterId, SessionProfileSelectionRequest? request)
            => throw new NotSupportedException();

        public SessionApiResult<RulePackCatalog> ListRulePacks(OwnerScope owner)
            => throw new NotSupportedException();

        public SessionApiResult<SessionOverlaySnapshot> UpdatePins(OwnerScope owner, SessionPinUpdateRequest? request)
            => throw new NotSupportedException();
    }
}
