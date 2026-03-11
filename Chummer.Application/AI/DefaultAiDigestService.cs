using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Content;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.AI;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.AI;

public sealed class DefaultAiDigestService : IAiDigestService
{
    private readonly IRuntimeLockRegistryService _runtimeLockRegistryService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ISessionService _sessionService;

    public DefaultAiDigestService(
        IRuntimeLockRegistryService runtimeLockRegistryService,
        IWorkspaceService workspaceService,
        ISessionService sessionService)
    {
        _runtimeLockRegistryService = runtimeLockRegistryService;
        _workspaceService = workspaceService;
        _sessionService = sessionService;
    }

    public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        string? normalizedFingerprint = NormalizeOptional(runtimeFingerprint);
        if (normalizedFingerprint is null)
        {
            return null;
        }

        RuntimeLockRegistryEntry? entry = _runtimeLockRegistryService.Get(owner, normalizedFingerprint, normalizedRulesetId);
        if (entry is null)
        {
            return null;
        }

        return new AiRuntimeSummaryProjection(
            RuntimeFingerprint: entry.RuntimeLock.RuntimeFingerprint,
            RulesetId: entry.RuntimeLock.RulesetId,
            Title: entry.Title,
            CatalogKind: entry.CatalogKind,
            EngineApiVersion: entry.RuntimeLock.EngineApiVersion,
            ContentBundles: entry.RuntimeLock.ContentBundles
                .Select(bundle => $"{bundle.BundleId}@{bundle.Version}")
                .ToArray(),
            RulePacks: entry.RuntimeLock.RulePacks
                .Select(pack => $"{pack.Id}@{pack.Version}")
                .ToArray(),
            ProviderBindings: new Dictionary<string, string>(entry.RuntimeLock.ProviderBindings, StringComparer.Ordinal),
            Visibility: entry.Visibility,
            Description: entry.Description);
    }

    public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
    {
        WorkspaceListItem? workspace = ResolveWorkspace(owner, characterId);
        if (workspace is null)
        {
            return null;
        }

        SessionCharacterListItem? sessionCharacter = ResolveSessionCharacter(owner, workspace.Id.Value);
        string rulesetId = RulesetDefaults.NormalizeOptional(workspace.RulesetId) ?? string.Empty;

        return new AiCharacterDigestProjection(
            CharacterId: workspace.Id.Value,
            DisplayName: sessionCharacter?.DisplayName ?? BuildDisplayName(workspace.Summary),
            RulesetId: rulesetId,
            RuntimeFingerprint: sessionCharacter?.RuntimeFingerprint ?? string.Empty,
            Summary: workspace.Summary,
            LastUpdatedUtc: workspace.LastUpdatedUtc,
            HasSavedWorkspace: workspace.HasSavedWorkspace);
    }

    public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId)
    {
        SessionCharacterListItem? sessionCharacter = ResolveSessionCharacter(owner, characterId);
        if (sessionCharacter is null)
        {
            return null;
        }

        SessionRuntimeStatusProjection? runtimeStatus = _sessionService.GetRuntimeState(owner, sessionCharacter.CharacterId).Payload;
        if (runtimeStatus is null)
        {
            return null;
        }

        return new AiSessionDigestProjection(
            CharacterId: sessionCharacter.CharacterId,
            DisplayName: sessionCharacter.DisplayName,
            RulesetId: runtimeStatus.RulesetId ?? sessionCharacter.RulesetId,
            RuntimeFingerprint: runtimeStatus.RuntimeFingerprint ?? sessionCharacter.RuntimeFingerprint,
            SelectionState: runtimeStatus.SelectionState,
            SessionReady: runtimeStatus.SessionReady,
            BundleFreshness: runtimeStatus.BundleFreshness,
            RequiresBundleRefresh: runtimeStatus.RequiresBundleRefresh,
            ProfileId: runtimeStatus.ProfileId,
            ProfileTitle: runtimeStatus.ProfileTitle,
            DeferredReason: runtimeStatus.DeferredReason);
    }

    private WorkspaceListItem? ResolveWorkspace(OwnerScope owner, string characterId)
    {
        string? normalizedCharacterId = NormalizeOptional(characterId);
        if (normalizedCharacterId is null)
        {
            return null;
        }

        return _workspaceService.List(owner)
            .FirstOrDefault(workspace => string.Equals(workspace.Id.Value, normalizedCharacterId, StringComparison.Ordinal));
    }

    private SessionCharacterListItem? ResolveSessionCharacter(OwnerScope owner, string characterId)
    {
        string? normalizedCharacterId = NormalizeOptional(characterId);
        if (normalizedCharacterId is null)
        {
            return null;
        }

        return _sessionService.ListCharacters(owner).Payload?.Characters
            .FirstOrDefault(character => string.Equals(character.CharacterId, normalizedCharacterId, StringComparison.Ordinal));
    }

    private static string BuildDisplayName(CharacterFileSummary summary)
    {
        return string.IsNullOrWhiteSpace(summary.Alias)
            ? summary.Name
            : $"{summary.Name} ({summary.Alias})";
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
