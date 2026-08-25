using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public interface IRunnerLibraryService
{
    RunnerLibraryListResult List(OwnerScope owner, RunnerLibraryListQuery query);

    RunnerLibraryMutationResult Rename(OwnerScope owner, RenameRunnerCommand command);

    RunnerLibraryMutationResult Duplicate(OwnerScope owner, DuplicateRunnerCommand command);

    RunnerLibraryMutationResult Archive(OwnerScope owner, ArchiveRunnerCommand command);

    RunnerLibraryMutationResult RestoreArchived(
        OwnerScope owner,
        RestoreArchivedRunnerCommand command);

    /// <summary>
    /// Performs the only user-facing Runner Library delete: a recoverable, revision- and
    /// content-digest-bound tombstone transition. UI callers must not substitute
    /// <see cref="WorkspaceService.Close(CharacterWorkspaceId, long)"/> or low-level
    /// <see cref="IWorkspaceStore.Delete(CharacterWorkspaceId, long)"/>, which are destructive
    /// workspace teardown primitives retained for explicit administrative and legacy flows.
    /// </summary>
    RunnerLibraryMutationResult Delete(OwnerScope owner, DeleteRunnerCommand command);

    RunnerLibraryMutationResult RestoreDeleted(
        OwnerScope owner,
        RestoreDeletedRunnerCommand command);
}

public interface IRunnerLibraryStore
{
    RunnerLibraryListResult ListRunners(OwnerScope owner, RunnerLibraryListQuery query);

    RunnerLibraryMutationResult ApplyRunnerLibraryMutation(
        OwnerScope owner,
        RunnerLibraryStoreMutation mutation);
}

public sealed record RunnerLibraryStoreMutation(
    RunnerLibraryMutationKind Kind,
    CharacterWorkspaceId RunnerId,
    CharacterWorkspaceId? NewRunnerId,
    long ExpectedLifecycleRevision,
    long ExpectedContentRevision,
    string ExpectedContentDigestSha256,
    string? DisplayName,
    string IdempotencyKeyDigestSha256,
    string CommandDigestSha256);
