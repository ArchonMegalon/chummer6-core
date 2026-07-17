using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public readonly record struct WorkspaceStoreEntry(
    CharacterWorkspaceId Id,
    DateTimeOffset LastUpdatedUtc,
    long ContentRevision,
    long SavedRevision);

public sealed record WorkspaceStoredDocument(
    CharacterWorkspaceId Id,
    WorkspaceDocument Document,
    long ContentRevision,
    long SavedRevision,
    DateTimeOffset LastUpdatedUtc);

public sealed record WorkspaceStoreReadResult(
    WorkspaceOperationOutcome Outcome,
    WorkspaceStoredDocument? Value = null,
    string? Error = null)
{
    public bool Success => Outcome == WorkspaceOperationOutcome.Success && Value is not null;
}

public sealed record WorkspaceStoreMutationResult(
    WorkspaceOperationOutcome Outcome,
    WorkspaceStoreEntry? Entry = null,
    string? Error = null)
{
    public bool Success => Outcome == WorkspaceOperationOutcome.Success && Entry is not null;
}

public interface IWorkspaceStore
{
    WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document);

    WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document);

    WorkspaceStoreMutationResult CreateWorkspaceDocument(
        CharacterWorkspaceId id,
        WorkspaceDocument document)
        => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Conditional workspace creation is unavailable.");

    WorkspaceStoreMutationResult CreateWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        WorkspaceDocument document)
        => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Conditional workspace creation is unavailable.");

    IReadOnlyList<WorkspaceStoreEntry> List();

    IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner);

    WorkspaceStoreReadResult Get(CharacterWorkspaceId id);

    WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id);

    WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document);

    WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document);

    WorkspaceStoreMutationResult SaveCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision);

    WorkspaceStoreMutationResult SaveCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision);

    WorkspaceStoreMutationResult Delete(
        CharacterWorkspaceId id,
        long expectedContentRevision);

    WorkspaceStoreMutationResult Delete(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision);
}

/// <summary>
/// Verifies that the configured workspace store can complete an owner-scoped,
/// durable write/read/delete cycle. Implementations must not create a user
/// workspace or expose the probe owner through public state.
/// </summary>
public interface IWorkspaceStoreReadinessProbe
{
    void Probe(OwnerScope owner);
}
