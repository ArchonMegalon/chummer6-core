using System.Collections.Concurrent;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class InMemoryWorkspaceStore : IWorkspaceStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WorkspaceEntry>> _documentsByOwner = new(StringComparer.Ordinal);

    public CharacterWorkspaceId Create(WorkspaceDocument document)
    {
        return Create(OwnerScope.LocalSingleUser, document);
    }

    public CharacterWorkspaceId Create(OwnerScope owner, WorkspaceDocument document)
    {
        string key = Guid.NewGuid().ToString("N");
        GetOwnerDocuments(owner)[key] = new WorkspaceEntry(document, DateTimeOffset.UtcNow);
        return new CharacterWorkspaceId(key);
    }

    public IReadOnlyList<WorkspaceStoreEntry> List()
    {
        return List(OwnerScope.LocalSingleUser);
    }

    public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner)
    {
        return GetOwnerDocuments(owner)
            .OrderByDescending(pair => pair.Value.LastUpdatedUtc)
            .Select(pair => new WorkspaceStoreEntry(
                Id: new CharacterWorkspaceId(pair.Key),
                LastUpdatedUtc: pair.Value.LastUpdatedUtc))
            .ToArray();
    }

    public bool TryGet(CharacterWorkspaceId id, out WorkspaceDocument document)
    {
        return TryGet(OwnerScope.LocalSingleUser, id, out document);
    }

    public bool TryGet(OwnerScope owner, CharacterWorkspaceId id, out WorkspaceDocument document)
    {
        if (GetOwnerDocuments(owner).TryGetValue(id.Value, out WorkspaceEntry? entry))
        {
            document = entry.Document;
            return true;
        }

        document = null!;
        return false;
    }

    public void Save(CharacterWorkspaceId id, WorkspaceDocument document)
    {
        Save(OwnerScope.LocalSingleUser, id, document);
    }

    public void Save(OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document)
    {
        GetOwnerDocuments(owner)[id.Value] = new WorkspaceEntry(document, DateTimeOffset.UtcNow);
    }

    public bool Delete(CharacterWorkspaceId id)
    {
        return Delete(OwnerScope.LocalSingleUser, id);
    }

    public bool Delete(OwnerScope owner, CharacterWorkspaceId id)
    {
        return GetOwnerDocuments(owner).TryRemove(id.Value, out _);
    }

    private sealed record WorkspaceEntry(WorkspaceDocument Document, DateTimeOffset LastUpdatedUtc);

    private ConcurrentDictionary<string, WorkspaceEntry> GetOwnerDocuments(OwnerScope owner)
    {
        return _documentsByOwner.GetOrAdd(
            owner.NormalizedValue,
            static _ => new ConcurrentDictionary<string, WorkspaceEntry>(StringComparer.Ordinal));
    }
}
