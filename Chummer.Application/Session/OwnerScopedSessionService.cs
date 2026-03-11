using System.Security.Cryptography;
using System.Text;
using Chummer.Application.Content;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Chummer.Contracts.Trackers;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Session;

public sealed class OwnerScopedSessionService : ISessionService
{
    private const string BundleKeyId = "session-runtime-bundle-v1";
    private static readonly TimeSpan BundleLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan ExpiringSoonThreshold = TimeSpan.FromDays(1);

    private readonly IRulePackRegistryService _rulePackRegistryService;
    private readonly IRuleProfileApplicationService _ruleProfileApplicationService;
    private readonly IRuleProfileRegistryService _ruleProfileRegistryService;
    private readonly IRulesetSelectionPolicy _rulesetSelectionPolicy;
    private readonly ISessionProfileSelectionStore _profileSelectionStore;
    private readonly ISessionRuntimeBundleStore _runtimeBundleStore;
    private readonly IWorkspaceService _workspaceService;
    private readonly IActiveRuntimeStatusService _activeRuntimeStatusService;

    public OwnerScopedSessionService(
        IRuleProfileRegistryService ruleProfileRegistryService,
        IRuleProfileApplicationService ruleProfileApplicationService,
        IRulePackRegistryService rulePackRegistryService,
        IRulesetSelectionPolicy rulesetSelectionPolicy,
        ISessionProfileSelectionStore profileSelectionStore,
        ISessionRuntimeBundleStore runtimeBundleStore,
        IWorkspaceService workspaceService,
        IActiveRuntimeStatusService activeRuntimeStatusService)
    {
        _ruleProfileRegistryService = ruleProfileRegistryService;
        _ruleProfileApplicationService = ruleProfileApplicationService;
        _rulePackRegistryService = rulePackRegistryService;
        _rulesetSelectionPolicy = rulesetSelectionPolicy;
        _profileSelectionStore = profileSelectionStore;
        _runtimeBundleStore = runtimeBundleStore;
        _workspaceService = workspaceService;
        _activeRuntimeStatusService = activeRuntimeStatusService;
    }

    public SessionApiResult<SessionCharacterCatalog> ListCharacters(OwnerScope owner)
    {
        SessionProfileBinding[] bindings = _profileSelectionStore.List(owner)
            .OrderByDescending(binding => binding.SelectedAtUtc)
            .ToArray();
        Dictionary<string, SessionProfileBinding> bindingsByCharacter = bindings
            .GroupBy(binding => binding.CharacterId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, string> runtimeFingerprintByRuleset = new(StringComparer.Ordinal);
        IReadOnlyList<WorkspaceListItem> ownerWorkspaces = _workspaceService.List(owner);

        SessionCharacterListItem[] characters = ownerWorkspaces
            .Select(workspace => CreateCharacterListItem(owner, workspace, bindingsByCharacter, runtimeFingerprintByRuleset))
            .ToArray();

        return SessionApiResult<SessionCharacterCatalog>.Implemented(new SessionCharacterCatalog(characters));
    }

    public SessionApiResult<SessionDashboardProjection> GetCharacterProjection(OwnerScope owner, string characterId)
        => NotImplemented<SessionDashboardProjection>(owner, SessionApiOperations.GetCharacterProjection, characterId);

    public SessionApiResult<SessionOverlaySnapshot> ApplyCharacterPatches(OwnerScope owner, string characterId, SessionPatchRequest? request)
        => NotImplemented<SessionOverlaySnapshot>(owner, SessionApiOperations.ApplyCharacterPatches, characterId);

    public SessionApiResult<SessionSyncReceipt> SyncCharacterLedger(OwnerScope owner, string characterId, SessionSyncBatch? batch)
        => NotImplemented<SessionSyncReceipt>(owner, SessionApiOperations.SyncCharacterLedger, characterId);

    public SessionApiResult<SessionProfileCatalog> ListProfiles(OwnerScope owner)
    {
        string defaultProfileId = $"official.{_rulesetSelectionPolicy.GetDefaultRulesetId()}.core";
        SessionProfileBinding? activeBinding = _profileSelectionStore.List(owner)
            .OrderByDescending(binding => binding.SelectedAtUtc)
            .FirstOrDefault();
        SessionProfileListItem[] profiles = _ruleProfileRegistryService.List(owner)
            .Select(entry => CreateProfileListItem(owner, entry))
            .OrderBy(profile => profile.Title, StringComparer.Ordinal)
            .ToArray();

        return SessionApiResult<SessionProfileCatalog>.Implemented(
            new SessionProfileCatalog(
                Profiles: profiles,
                ActiveProfileId: activeBinding?.ProfileId ?? defaultProfileId));
    }

    public SessionApiResult<SessionRuntimeStatusProjection> GetRuntimeState(OwnerScope owner, string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        string normalizedCharacterId = characterId.Trim();
        SessionProfileBinding? binding = _profileSelectionStore.Get(owner, normalizedCharacterId);
        if (binding is null)
        {
            return SessionApiResult<SessionRuntimeStatusProjection>.Implemented(
                new SessionRuntimeStatusProjection(
                    CharacterId: normalizedCharacterId,
                    SelectionState: SessionRuntimeSelectionStates.Unselected,
                    RequiresBundleRefresh: true,
                    DeferredReason: "No session profile has been selected for this character yet."));
        }

        RuleProfileRegistryEntry? profile = _ruleProfileRegistryService.Get(owner, binding.ProfileId, binding.RulesetId);
        if (profile is null)
        {
            return SessionApiResult<SessionRuntimeStatusProjection>.Implemented(
                new SessionRuntimeStatusProjection(
                    CharacterId: normalizedCharacterId,
                    SelectionState: SessionRuntimeSelectionStates.Blocked,
                    ProfileId: binding.ProfileId,
                    RulesetId: binding.RulesetId,
                    RuntimeFingerprint: binding.RuntimeFingerprint,
                    RequiresBundleRefresh: true,
                    DeferredReason: $"Session profile '{binding.ProfileId}' is no longer available."));
        }

        bool sessionReady = IsSessionReady(owner, profile);
        SessionRuntimeBundleRecord? bundleRecord = _runtimeBundleStore.Get(owner, normalizedCharacterId);
        string bundleFreshness = ResolveBundleFreshness(bundleRecord, profile);
        SessionRuntimeBundleIssueReceipt? bundleReceipt = bundleRecord?.Receipt;
        bool requiresBundleRefresh = !string.Equals(bundleFreshness, SessionRuntimeBundleFreshnessStates.Current, StringComparison.Ordinal);

        return SessionApiResult<SessionRuntimeStatusProjection>.Implemented(
            new SessionRuntimeStatusProjection(
                CharacterId: normalizedCharacterId,
                SelectionState: sessionReady ? SessionRuntimeSelectionStates.Selected : SessionRuntimeSelectionStates.Blocked,
                ProfileId: profile.Manifest.ProfileId,
                ProfileTitle: profile.Manifest.Title,
                RulesetId: profile.Manifest.RulesetId,
                RuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                SessionReady: sessionReady,
                BundleFreshness: bundleFreshness,
                BundleId: bundleReceipt?.Bundle.BundleId,
                BundleDeliveryMode: bundleReceipt?.DeliveryMode,
                BundleTrustState: bundleReceipt?.Diagnostics.FirstOrDefault()?.State,
                BundleSignedAtUtc: bundleReceipt?.SignatureEnvelope.SignedAtUtc,
                BundleExpiresAtUtc: bundleReceipt?.SignatureEnvelope.ExpiresAtUtc,
                RequiresBundleRefresh: requiresBundleRefresh,
                DeferredReason: sessionReady
                    ? null
                    : $"Session profile '{profile.Manifest.ProfileId}' is not session-ready."));
    }

    public SessionApiResult<SessionRuntimeBundleIssueReceipt> GetRuntimeBundle(OwnerScope owner, string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        string normalizedCharacterId = characterId.Trim();
        RuleProfileRegistryEntry? profile = ResolveSelectedProfile(owner, normalizedCharacterId, out string? blockedReason);
        if (profile is null)
        {
            return SessionApiResult<SessionRuntimeBundleIssueReceipt>.Implemented(
                CreateBlockedBundleReceipt(normalizedCharacterId, blockedReason!));
        }

        SessionRuntimeBundleRecord? existingRecord = _runtimeBundleStore.Get(owner, normalizedCharacterId);
        SessionRuntimeBundleIssueReceipt receipt = IssueRuntimeBundle(owner, normalizedCharacterId, profile, existingRecord, allowCached: true);
        return SessionApiResult<SessionRuntimeBundleIssueReceipt>.Implemented(receipt);
    }

    public SessionApiResult<SessionRuntimeBundleRefreshReceipt> RefreshRuntimeBundle(OwnerScope owner, string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        string normalizedCharacterId = characterId.Trim();
        SessionRuntimeBundleRecord? existingRecord = _runtimeBundleStore.Get(owner, normalizedCharacterId);
        RuleProfileRegistryEntry? profile = ResolveSelectedProfile(owner, normalizedCharacterId, out string? blockedReason);
        if (profile is null)
        {
            return SessionApiResult<SessionRuntimeBundleRefreshReceipt>.Implemented(
                CreateBlockedBundleRefreshReceipt(normalizedCharacterId, existingRecord, profile, blockedReason!));
        }

        string bundleFreshness = ResolveBundleFreshness(existingRecord, profile);
        if (existingRecord is not null
            && string.Equals(bundleFreshness, SessionRuntimeBundleFreshnessStates.Current, StringComparison.Ordinal))
        {
            return SessionApiResult<SessionRuntimeBundleRefreshReceipt>.Implemented(
                new SessionRuntimeBundleRefreshReceipt(
                    PreviousBundleId: existingRecord.Receipt.Bundle.BundleId,
                    CurrentBundleId: existingRecord.Receipt.Bundle.BundleId,
                    Outcome: SessionRuntimeBundleRefreshOutcomes.Unchanged,
                    BaseCharacterVersion: existingRecord.Receipt.Bundle.BaseCharacterVersion,
                    RuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                    RefreshedAtUtc: DateTimeOffset.UtcNow));
        }

        SessionRuntimeBundleIssueReceipt refreshedReceipt = IssueRuntimeBundle(owner, normalizedCharacterId, profile, existingRecord, allowCached: false);
        return SessionApiResult<SessionRuntimeBundleRefreshReceipt>.Implemented(
            new SessionRuntimeBundleRefreshReceipt(
                PreviousBundleId: existingRecord?.Receipt.Bundle.BundleId ?? string.Empty,
                CurrentBundleId: refreshedReceipt.Bundle.BundleId,
                Outcome: RequiresBundleRebind(existingRecord, profile)
                    ? SessionRuntimeBundleRefreshOutcomes.Rebound
                    : SessionRuntimeBundleRefreshOutcomes.Refreshed,
                BaseCharacterVersion: refreshedReceipt.Bundle.BaseCharacterVersion,
                RuntimeFingerprint: refreshedReceipt.Bundle.BaseCharacterVersion.RuntimeFingerprint,
                RefreshedAtUtc: refreshedReceipt.SignatureEnvelope.SignedAtUtc,
                SignatureChanged: existingRecord is null
                    || !string.Equals(existingRecord.Receipt.SignatureEnvelope.Signature, refreshedReceipt.SignatureEnvelope.Signature, StringComparison.Ordinal)));
    }

    public SessionApiResult<SessionProfileSelectionReceipt> SelectProfile(OwnerScope owner, string characterId, SessionProfileSelectionRequest? request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        string? requestedProfileId = NormalizeOptional(request?.ProfileId);
        if (requestedProfileId is null)
        {
            return SessionApiResult<SessionProfileSelectionReceipt>.Implemented(
                new SessionProfileSelectionReceipt(
                    CharacterId: characterId.Trim(),
                    ProfileId: string.Empty,
                    RuntimeFingerprint: string.Empty,
                    Outcome: SessionProfileSelectionOutcomes.Blocked,
                    DeferredReason: "A session profile id is required."));
        }

        RuleProfileRegistryEntry? profile = _ruleProfileRegistryService.Get(owner, requestedProfileId);
        if (profile is null)
        {
            return SessionApiResult<SessionProfileSelectionReceipt>.Implemented(
                new SessionProfileSelectionReceipt(
                    CharacterId: characterId.Trim(),
                    ProfileId: requestedProfileId,
                    RuntimeFingerprint: string.Empty,
                    Outcome: SessionProfileSelectionOutcomes.Blocked,
                    DeferredReason: $"Session profile '{requestedProfileId}' was not found."));
        }

        if (!IsSessionReady(owner, profile))
        {
            return SessionApiResult<SessionProfileSelectionReceipt>.Implemented(
                new SessionProfileSelectionReceipt(
                    CharacterId: characterId.Trim(),
                    ProfileId: profile.Manifest.ProfileId,
                    RuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                    Outcome: SessionProfileSelectionOutcomes.Blocked,
                    DeferredReason: $"Session profile '{profile.Manifest.ProfileId}' is not session-ready."));
        }

        RuleProfileApplyReceipt? applyReceipt = _ruleProfileApplicationService.Apply(
            owner,
            profile.Manifest.ProfileId,
            new RuleProfileApplyTarget(
                TargetKind: RuleProfileApplyTargetKinds.SessionLedger,
                TargetId: characterId.Trim()),
            profile.Manifest.RulesetId);
        if (applyReceipt is null || string.Equals(applyReceipt.Outcome, RuleProfileApplyOutcomes.Blocked, StringComparison.Ordinal))
        {
            return SessionApiResult<SessionProfileSelectionReceipt>.Implemented(
                new SessionProfileSelectionReceipt(
                    CharacterId: characterId.Trim(),
                    ProfileId: profile.Manifest.ProfileId,
                    RuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                    Outcome: SessionProfileSelectionOutcomes.Blocked,
                    DeferredReason: $"Session profile '{profile.Manifest.ProfileId}' could not be applied."));
        }

        SessionProfileBinding? existingBinding = _profileSelectionStore.Get(owner, characterId);
        _profileSelectionStore.Upsert(
            owner,
            new SessionProfileBinding(
                CharacterId: characterId.Trim(),
                ProfileId: profile.Manifest.ProfileId,
                RulesetId: profile.Manifest.RulesetId,
                RuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                SelectedAtUtc: DateTimeOffset.UtcNow));

        bool requiresBundleRefresh = existingBinding is not null
            && !string.Equals(existingBinding.RuntimeFingerprint, profile.Manifest.RuntimeLock.RuntimeFingerprint, StringComparison.Ordinal);

        return SessionApiResult<SessionProfileSelectionReceipt>.Implemented(
            new SessionProfileSelectionReceipt(
                CharacterId: characterId.Trim(),
                ProfileId: profile.Manifest.ProfileId,
                RuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                Outcome: SessionProfileSelectionOutcomes.Selected,
                RequiresBundleRefresh: requiresBundleRefresh));
    }

    public SessionApiResult<RulePackCatalog> ListRulePacks(OwnerScope owner)
    {
        RulePackManifest[] sessionReadyPacks = _rulePackRegistryService.List(owner)
            .Where(entry => IsSessionReady(entry))
            .Select(entry => entry.Manifest)
            .OrderBy(manifest => manifest.Title, StringComparer.Ordinal)
            .ToArray();
        return SessionApiResult<RulePackCatalog>.Implemented(new RulePackCatalog(sessionReadyPacks));
    }

    public SessionApiResult<SessionOverlaySnapshot> UpdatePins(OwnerScope owner, SessionPinUpdateRequest? request)
        => NotImplemented<SessionOverlaySnapshot>(owner, SessionApiOperations.UpdatePins, request?.BaseCharacterVersion.CharacterId);

    private SessionCharacterListItem CreateCharacterListItem(
        OwnerScope owner,
        WorkspaceListItem workspace,
        IReadOnlyDictionary<string, SessionProfileBinding> bindingsByCharacter,
        IDictionary<string, string> runtimeFingerprintByRuleset)
    {
        string characterId = workspace.Id.Value;
        string normalizedRulesetId = RulesetDefaults.NormalizeOptional(workspace.RulesetId) ?? string.Empty;
        string runtimeFingerprint = ResolveRuntimeFingerprint(owner, characterId, normalizedRulesetId, bindingsByCharacter, runtimeFingerprintByRuleset);

        return new SessionCharacterListItem(
            CharacterId: characterId,
            DisplayName: BuildDisplayName(workspace),
            RulesetId: normalizedRulesetId,
            RuntimeFingerprint: runtimeFingerprint);
    }

    private SessionProfileListItem CreateProfileListItem(OwnerScope owner, RuleProfileRegistryEntry entry)
    {
        return new SessionProfileListItem(
            ProfileId: entry.Manifest.ProfileId,
            Title: entry.Manifest.Title,
            RulesetId: entry.Manifest.RulesetId,
            RuntimeFingerprint: entry.Manifest.RuntimeLock.RuntimeFingerprint,
            UpdateChannel: entry.Manifest.UpdateChannel,
            SessionReady: IsSessionReady(owner, entry),
            Audience: entry.Manifest.Audience);
    }

    private bool IsSessionReady(OwnerScope owner, RuleProfileRegistryEntry profile)
    {
        if (profile.Manifest.RulePacks.Count == 0)
        {
            return true;
        }

        return profile.Manifest.RulePacks.All(selection =>
        {
            RulePackRegistryEntry? rulePack = _rulePackRegistryService.Get(owner, selection.RulePack.Id, profile.Manifest.RulesetId);
            return rulePack is not null && IsSessionReady(rulePack);
        });
    }

    private static bool IsSessionReady(RulePackRegistryEntry entry)
    {
        if (entry.Manifest.ExecutionPolicies.Count == 0)
        {
            return false;
        }

        return entry.Manifest.ExecutionPolicies.Any(policy =>
            string.Equals(policy.Environment, RulePackExecutionEnvironments.SessionRuntimeBundle, StringComparison.Ordinal)
                && !string.Equals(policy.PolicyMode, RulePackExecutionPolicyModes.Deny, StringComparison.Ordinal));
    }

    private string ResolveRuntimeFingerprint(
        OwnerScope owner,
        string characterId,
        string rulesetId,
        IReadOnlyDictionary<string, SessionProfileBinding> bindingsByCharacter,
        IDictionary<string, string> runtimeFingerprintByRuleset)
    {
        if (bindingsByCharacter.TryGetValue(characterId, out SessionProfileBinding? binding)
            && !string.IsNullOrWhiteSpace(binding.RuntimeFingerprint))
        {
            return binding.RuntimeFingerprint;
        }

        if (runtimeFingerprintByRuleset.TryGetValue(rulesetId, out string? cachedRuntimeFingerprint))
        {
            return cachedRuntimeFingerprint;
        }

        ActiveRuntimeStatusProjection? activeRuntime = _activeRuntimeStatusService.GetActiveProfileStatus(
            owner,
            string.IsNullOrWhiteSpace(rulesetId) ? null : rulesetId);
        string runtimeFingerprint = activeRuntime?.RuntimeFingerprint ?? string.Empty;
        runtimeFingerprintByRuleset[rulesetId] = runtimeFingerprint;
        return runtimeFingerprint;
    }

    private static string BuildDisplayName(WorkspaceListItem workspace)
    {
        string name = workspace.Summary.Name.Trim();
        string alias = workspace.Summary.Alias.Trim();

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(alias))
        {
            return $"{name} ({alias})";
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias;
        }

        return workspace.Id.Value;
    }

    private RuleProfileRegistryEntry? ResolveSelectedProfile(OwnerScope owner, string characterId, out string? blockedReason)
    {
        SessionProfileBinding? binding = _profileSelectionStore.Get(owner, characterId);
        if (binding is null)
        {
            blockedReason = "No session profile has been selected for this character yet.";
            return null;
        }

        RuleProfileRegistryEntry? profile = _ruleProfileRegistryService.Get(owner, binding.ProfileId, binding.RulesetId);
        if (profile is null)
        {
            blockedReason = $"Session profile '{binding.ProfileId}' is no longer available.";
            return null;
        }

        if (!IsSessionReady(owner, profile))
        {
            blockedReason = $"Session profile '{profile.Manifest.ProfileId}' is not session-ready.";
            return null;
        }

        blockedReason = null;
        return profile;
    }

    private SessionRuntimeBundleIssueReceipt IssueRuntimeBundle(
        OwnerScope owner,
        string characterId,
        RuleProfileRegistryEntry profile,
        SessionRuntimeBundleRecord? existingRecord,
        bool allowCached)
    {
        if (allowCached && CanReuseBundle(existingRecord, profile))
        {
            SessionRuntimeBundleIssueReceipt cachedReceipt = existingRecord!.Receipt with
            {
                DeliveryMode = SessionRuntimeBundleDeliveryModes.Cached,
                Diagnostics = UpdateDiagnostics(existingRecord.Receipt.SignatureEnvelope, existingRecord.Receipt.Diagnostics)
            };
            _runtimeBundleStore.Upsert(owner, existingRecord with { Receipt = cachedReceipt });
            return cachedReceipt;
        }

        DateTimeOffset signedAtUtc = DateTimeOffset.UtcNow;
        SessionRuntimeBundle bundle = CreateBundle(characterId, profile, signedAtUtc);
        SessionRuntimeBundleSignatureEnvelope signatureEnvelope = CreateSignatureEnvelope(owner, characterId, profile, bundle, signedAtUtc);
        SessionRuntimeBundleIssueReceipt receipt = new(
            Outcome: existingRecord is null
                ? SessionRuntimeBundleIssueOutcomes.Issued
                : SessionRuntimeBundleIssueOutcomes.Rotated,
            Bundle: bundle,
            SignatureEnvelope: signatureEnvelope,
            DeliveryMode: SessionRuntimeBundleDeliveryModes.Inline,
            Diagnostics: UpdateDiagnostics(signatureEnvelope, []));
        _runtimeBundleStore.Upsert(
            owner,
            new SessionRuntimeBundleRecord(
                CharacterId: characterId,
                ProfileId: profile.Manifest.ProfileId,
                RulesetId: profile.Manifest.RulesetId,
                Receipt: receipt,
                IssuedAtUtc: signedAtUtc));
        return receipt;
    }

    private static bool CanReuseBundle(SessionRuntimeBundleRecord? existingRecord, RuleProfileRegistryEntry profile)
        => existingRecord is not null
            && string.Equals(existingRecord.ProfileId, profile.Manifest.ProfileId, StringComparison.Ordinal)
            && string.Equals(existingRecord.Receipt.Bundle.BaseCharacterVersion.RuntimeFingerprint, profile.Manifest.RuntimeLock.RuntimeFingerprint, StringComparison.Ordinal)
            && existingRecord.Receipt.SignatureEnvelope.ExpiresAtUtc > DateTimeOffset.UtcNow;

    private static bool RequiresBundleRebind(SessionRuntimeBundleRecord? existingRecord, RuleProfileRegistryEntry profile)
        => existingRecord is not null
            && (!string.Equals(existingRecord.ProfileId, profile.Manifest.ProfileId, StringComparison.Ordinal)
                || !string.Equals(existingRecord.Receipt.Bundle.BaseCharacterVersion.RuntimeFingerprint, profile.Manifest.RuntimeLock.RuntimeFingerprint, StringComparison.Ordinal));

    private static string ResolveBundleFreshness(SessionRuntimeBundleRecord? record, RuleProfileRegistryEntry profile)
    {
        if (record is null)
        {
            return SessionRuntimeBundleFreshnessStates.Missing;
        }

        if (!string.Equals(record.ProfileId, profile.Manifest.ProfileId, StringComparison.Ordinal)
            || !string.Equals(record.Receipt.Bundle.BaseCharacterVersion.RuntimeFingerprint, profile.Manifest.RuntimeLock.RuntimeFingerprint, StringComparison.Ordinal)
            || record.Receipt.SignatureEnvelope.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return SessionRuntimeBundleFreshnessStates.RefreshRequired;
        }

        return record.Receipt.SignatureEnvelope.ExpiresAtUtc - DateTimeOffset.UtcNow <= ExpiringSoonThreshold
            ? SessionRuntimeBundleFreshnessStates.ExpiringSoon
            : SessionRuntimeBundleFreshnessStates.Current;
    }

    private static SessionRuntimeBundleIssueReceipt CreateBlockedBundleReceipt(string characterId, string message)
    {
        CharacterVersionReference baseCharacterVersion = new(
            CharacterId: characterId.Trim(),
            VersionId: "unbound",
            RulesetId: string.Empty,
            RuntimeFingerprint: string.Empty);
        SessionRuntimeBundle bundle = new(
            BundleId: string.Empty,
            BaseCharacterVersion: baseCharacterVersion,
            EngineApiVersion: string.Empty,
            SignedAtUtc: DateTimeOffset.MinValue,
            Signature: string.Empty,
            QuickActions: [],
            Trackers: [],
            ReducerBindings: new Dictionary<string, string>(StringComparer.Ordinal));
        SessionRuntimeBundleSignatureEnvelope signatureEnvelope = new(
            BundleId: string.Empty,
            KeyId: string.Empty,
            Signature: string.Empty,
            SignedAtUtc: DateTimeOffset.MinValue,
            ExpiresAtUtc: DateTimeOffset.MinValue);
        return new SessionRuntimeBundleIssueReceipt(
            Outcome: SessionRuntimeBundleIssueOutcomes.Blocked,
            Bundle: bundle,
            SignatureEnvelope: signatureEnvelope,
            DeliveryMode: SessionRuntimeBundleDeliveryModes.Inline,
            Diagnostics:
            [
                new SessionRuntimeBundleTrustDiagnostic(
                    State: SessionRuntimeBundleTrustStates.MissingKey,
                    Message: message)
            ]);
    }

    private static SessionRuntimeBundleRefreshReceipt CreateBlockedBundleRefreshReceipt(
        string characterId,
        SessionRuntimeBundleRecord? existingRecord,
        RuleProfileRegistryEntry? profile,
        string message)
    {
        CharacterVersionReference baseCharacterVersion = existingRecord?.Receipt.Bundle.BaseCharacterVersion
            ?? new CharacterVersionReference(
                CharacterId: characterId.Trim(),
                VersionId: profile is null ? "unbound" : $"session:{profile.Manifest.ProfileId}",
                RulesetId: profile?.Manifest.RulesetId ?? string.Empty,
                RuntimeFingerprint: profile?.Manifest.RuntimeLock.RuntimeFingerprint ?? string.Empty);
        string existingBundleId = existingRecord?.Receipt.Bundle.BundleId ?? string.Empty;

        return new SessionRuntimeBundleRefreshReceipt(
            PreviousBundleId: existingBundleId,
            CurrentBundleId: existingBundleId,
            Outcome: SessionRuntimeBundleRefreshOutcomes.Blocked,
            BaseCharacterVersion: baseCharacterVersion,
            RuntimeFingerprint: baseCharacterVersion.RuntimeFingerprint,
            RefreshedAtUtc: DateTimeOffset.UtcNow,
            DeferredReason: message);
    }

    private static SessionRuntimeBundle CreateBundle(
        string characterId,
        RuleProfileRegistryEntry profile,
        DateTimeOffset signedAtUtc)
    {
        string bundleId = ComputeHash($"{characterId.Trim()}|{profile.Manifest.ProfileId}|{profile.Manifest.RuntimeLock.RuntimeFingerprint}");
        CharacterVersionReference baseCharacterVersion = new(
            CharacterId: characterId.Trim(),
            VersionId: $"session:{profile.Manifest.ProfileId}",
            RulesetId: profile.Manifest.RulesetId,
            RuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint);
        SessionQuickActionPin[] quickActions = profile.Manifest.RuntimeLock.ProviderBindings
            .Where(binding => string.Equals(binding.Key, RulePackCapabilityIds.SessionQuickActions, StringComparison.Ordinal))
            .Select(binding => new SessionQuickActionPin(
                ActionId: binding.Value,
                Label: binding.Value,
                CapabilityId: binding.Key))
            .ToArray();

        return new SessionRuntimeBundle(
            BundleId: bundleId,
            BaseCharacterVersion: baseCharacterVersion,
            EngineApiVersion: profile.Manifest.RuntimeLock.EngineApiVersion,
            SignedAtUtc: signedAtUtc,
            Signature: ComputeHash($"{bundleId}|{profile.Manifest.RuntimeLock.RuntimeFingerprint}|{signedAtUtc:O}"),
            QuickActions: quickActions,
            Trackers: Array.Empty<TrackerDefinition>(),
            ReducerBindings: new Dictionary<string, string>(profile.Manifest.RuntimeLock.ProviderBindings, StringComparer.Ordinal));
    }

    private static SessionRuntimeBundleSignatureEnvelope CreateSignatureEnvelope(
        OwnerScope owner,
        string characterId,
        RuleProfileRegistryEntry profile,
        SessionRuntimeBundle bundle,
        DateTimeOffset signedAtUtc)
    {
        return new SessionRuntimeBundleSignatureEnvelope(
            BundleId: bundle.BundleId,
            KeyId: BundleKeyId,
            Signature: ComputeHash($"{owner.NormalizedValue}|{characterId.Trim()}|{profile.Manifest.ProfileId}|{bundle.BundleId}|{bundle.Signature}"),
            SignedAtUtc: signedAtUtc,
            ExpiresAtUtc: signedAtUtc.Add(BundleLifetime));
    }

    private static IReadOnlyList<SessionRuntimeBundleTrustDiagnostic> UpdateDiagnostics(
        SessionRuntimeBundleSignatureEnvelope signatureEnvelope,
        IReadOnlyList<SessionRuntimeBundleTrustDiagnostic> existingDiagnostics)
    {
        List<SessionRuntimeBundleTrustDiagnostic> diagnostics =
        [
            new(
                State: SessionRuntimeBundleTrustStates.Trusted,
                Message: "Runtime bundle signature is valid for the current owner-scoped session profile selection.",
                KeyId: signatureEnvelope.KeyId,
                RuntimeFingerprint: null)
        ];

        if (signatureEnvelope.ExpiresAtUtc != DateTimeOffset.MinValue
            && signatureEnvelope.ExpiresAtUtc - DateTimeOffset.UtcNow <= ExpiringSoonThreshold)
        {
            diagnostics.Add(new SessionRuntimeBundleTrustDiagnostic(
                State: SessionRuntimeBundleTrustStates.ExpiringSoon,
                Message: "Runtime bundle signature is nearing expiry and should be refreshed soon.",
                KeyId: signatureEnvelope.KeyId,
                RuntimeFingerprint: null));
        }

        foreach (SessionRuntimeBundleTrustDiagnostic diagnostic in existingDiagnostics)
        {
            if (diagnostics.Any(current =>
                    string.Equals(current.State, diagnostic.State, StringComparison.Ordinal)
                    && string.Equals(current.Message, diagnostic.Message, StringComparison.Ordinal)))
            {
                continue;
            }

            diagnostics.Add(diagnostic);
        }

        return diagnostics;
    }

    private static string ComputeHash(string input)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static SessionApiResult<T> NotImplemented<T>(OwnerScope owner, string operation, string? characterId = null)
        => SessionApiResult<T>.FromNotImplemented(
            new SessionNotImplementedReceipt(
                Error: "session_not_implemented",
                Operation: operation,
                Message: "The dedicated session/mobile surface is not implemented yet.",
                CharacterId: string.IsNullOrWhiteSpace(characterId) ? null : characterId,
                OwnerId: owner.NormalizedValue));
}
