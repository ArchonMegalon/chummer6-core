using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public readonly record struct WorkspaceStoreEntry(
    CharacterWorkspaceId Id,
    DateTimeOffset LastUpdatedUtc);

public interface IWorkspaceStore
{
    CharacterWorkspaceId Create(WorkspaceDocument document);

    CharacterWorkspaceId Create(OwnerScope owner, WorkspaceDocument document);

    IReadOnlyList<WorkspaceStoreEntry> List();

    IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner);

    bool TryGet(CharacterWorkspaceId id, out WorkspaceDocument document);

    bool TryGet(OwnerScope owner, CharacterWorkspaceId id, out WorkspaceDocument document);

    void Save(CharacterWorkspaceId id, WorkspaceDocument document);

    void Save(OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document);

    bool Delete(CharacterWorkspaceId id);

    bool Delete(OwnerScope owner, CharacterWorkspaceId id);
}
