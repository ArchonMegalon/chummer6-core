using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;

namespace Chummer.Application.Tools;

public sealed class ShellSessionService : IShellSessionService
{
    private readonly IShellSessionStore _store;

    public ShellSessionService(IShellSessionStore store)
    {
        _store = store;
    }

    public ShellSessionState Load()
    {
        return Load(OwnerScope.LocalSingleUser);
    }

    public ShellSessionState Load(OwnerScope owner)
    {
        ShellSessionState stored = _store.Load(owner);
        return new ShellSessionState(
            ActiveWorkspaceId: NormalizeWorkspaceId(stored.ActiveWorkspaceId),
            ActiveTabId: NormalizeTabId(stored.ActiveTabId),
            ActiveTabsByWorkspace: NormalizeWorkspaceTabMap(stored.ActiveTabsByWorkspace));
    }

    public void Save(ShellSessionState session)
    {
        Save(OwnerScope.LocalSingleUser, session);
    }

    public void Save(OwnerScope owner, ShellSessionState session)
    {
        ShellSessionState normalized = new(
            ActiveWorkspaceId: NormalizeWorkspaceId(session.ActiveWorkspaceId),
            ActiveTabId: NormalizeTabId(session.ActiveTabId),
            ActiveTabsByWorkspace: NormalizeWorkspaceTabMap(session.ActiveTabsByWorkspace));
        _store.Save(owner, normalized);
    }

    private static string? NormalizeWorkspaceId(string? workspaceId)
    {
        return string.IsNullOrWhiteSpace(workspaceId)
            ? null
            : workspaceId.Trim();
    }

    private static string? NormalizeTabId(string? tabId)
    {
        return string.IsNullOrWhiteSpace(tabId)
            ? null
            : tabId.Trim();
    }

    private static IReadOnlyDictionary<string, string>? NormalizeWorkspaceTabMap(IReadOnlyDictionary<string, string>? rawMap)
    {
        if (rawMap is null || rawMap.Count == 0)
        {
            return null;
        }

        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in rawMap)
        {
            string? workspaceId = NormalizeWorkspaceId(entry.Key);
            string? tabId = NormalizeTabId(entry.Value);
            if (workspaceId is null || tabId is null)
            {
                continue;
            }

            normalized[workspaceId] = tabId;
        }

        return normalized.Count == 0
            ? null
            : normalized;
    }
}
