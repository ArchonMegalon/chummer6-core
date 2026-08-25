using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public sealed class RunnerLibraryService : IRunnerLibraryService
{
    private readonly IRunnerLibraryStore? _store;

    public RunnerLibraryService(IWorkspaceStore workspaceStore)
    {
        ArgumentNullException.ThrowIfNull(workspaceStore);
        _store = workspaceStore as IRunnerLibraryStore;
    }

    public RunnerLibraryListResult List(OwnerScope owner, RunnerLibraryListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_store is null)
        {
            return UnavailableList();
        }

        if (!IsValidOwner(owner)
            || query.Lifecycles == RunnerLibraryLifecycleFilter.None
            || (query.Lifecycles & ~RunnerLibraryLifecycleFilter.All) != 0)
        {
            return new RunnerLibraryListResult(
                RunnerLibraryOperationOutcome.Invalid,
                [],
                "Runner Library query is invalid.");
        }

        string? nameContains = string.IsNullOrWhiteSpace(query.NameContains)
            ? null
            : query.NameContains.Trim().Normalize(System.Text.NormalizationForm.FormC);
        return _store.ListRunners(owner, query with { NameContains = nameContains });
    }

    public RunnerLibraryMutationResult Rename(OwnerScope owner, RenameRunnerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!RunnerLibraryCanonical.TryNormalizeDisplayName(command.DisplayName, out string displayName))
        {
            return InvalidMutation("Runner display name is invalid.");
        }

        return Apply(
            owner,
            RunnerLibraryMutationKind.Rename,
            command.RunnerId,
            null,
            command.ExpectedLifecycleRevision,
            command.ExpectedContentRevision,
            command.ExpectedContentDigestSha256,
            displayName,
            command.IdempotencyKey);
    }

    public RunnerLibraryMutationResult Duplicate(OwnerScope owner, DuplicateRunnerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!RunnerLibraryCanonical.TryNormalizeDisplayName(command.DisplayName, out string displayName))
        {
            return InvalidMutation("Runner display name is invalid.");
        }

        if (command.SourceRunnerId == command.NewRunnerId)
        {
            return InvalidMutation("A duplicate must use a new runner id.");
        }

        return Apply(
            owner,
            RunnerLibraryMutationKind.Duplicate,
            command.SourceRunnerId,
            command.NewRunnerId,
            command.ExpectedSourceLifecycleRevision,
            command.ExpectedSourceContentRevision,
            command.ExpectedSourceContentDigestSha256,
            displayName,
            command.IdempotencyKey);
    }

    public RunnerLibraryMutationResult Archive(OwnerScope owner, ArchiveRunnerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Apply(owner, RunnerLibraryMutationKind.Archive, command.RunnerId, null,
            command.ExpectedLifecycleRevision, command.ExpectedContentRevision,
            command.ExpectedContentDigestSha256, null, command.IdempotencyKey);
    }

    public RunnerLibraryMutationResult RestoreArchived(
        OwnerScope owner,
        RestoreArchivedRunnerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Apply(owner, RunnerLibraryMutationKind.RestoreArchived, command.RunnerId, null,
            command.ExpectedLifecycleRevision, command.ExpectedContentRevision,
            command.ExpectedContentDigestSha256, null, command.IdempotencyKey);
    }

    public RunnerLibraryMutationResult Delete(OwnerScope owner, DeleteRunnerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Apply(owner, RunnerLibraryMutationKind.Delete, command.RunnerId, null,
            command.ExpectedLifecycleRevision, command.ExpectedContentRevision,
            command.ExpectedContentDigestSha256, null, command.IdempotencyKey);
    }

    public RunnerLibraryMutationResult RestoreDeleted(
        OwnerScope owner,
        RestoreDeletedRunnerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Apply(owner, RunnerLibraryMutationKind.RestoreDeleted, command.RunnerId, null,
            command.ExpectedLifecycleRevision, command.ExpectedContentRevision,
            command.ExpectedContentDigestSha256, null, command.IdempotencyKey);
    }

    private RunnerLibraryMutationResult Apply(
        OwnerScope owner,
        RunnerLibraryMutationKind kind,
        CharacterWorkspaceId runnerId,
        CharacterWorkspaceId? newRunnerId,
        long expectedLifecycleRevision,
        long expectedContentRevision,
        string expectedContentDigestSha256,
        string? displayName,
        string idempotencyKey)
    {
        if (_store is null)
        {
            return UnavailableMutation();
        }

        if (!IsValidOwner(owner)
            || !RunnerLibraryCanonical.IsSupportedRunnerId(runnerId)
            || (newRunnerId is CharacterWorkspaceId target
                && !RunnerLibraryCanonical.IsSupportedRunnerId(target))
            || expectedLifecycleRevision <= 0
            || expectedContentRevision <= 0
            || !RunnerLibraryCanonical.IsSha256(expectedContentDigestSha256)
            || !RunnerLibraryCanonical.TryNormalizeIdempotencyKey(
                idempotencyKey,
                out string normalizedIdempotencyKey))
        {
            return InvalidMutation("Runner Library mutation is invalid.");
        }

        string keyDigest = RunnerLibraryCanonical.ComputeIdempotencyKeyDigest(
            normalizedIdempotencyKey);
        string commandDigest = RunnerLibraryCanonical.ComputeCommandDigest(
            kind,
            runnerId,
            newRunnerId,
            expectedLifecycleRevision,
            expectedContentRevision,
            expectedContentDigestSha256,
            displayName,
            keyDigest);
        return _store.ApplyRunnerLibraryMutation(
            owner,
            new RunnerLibraryStoreMutation(
                kind,
                runnerId,
                newRunnerId,
                expectedLifecycleRevision,
                expectedContentRevision,
                expectedContentDigestSha256,
                displayName,
                keyDigest,
                commandDigest));
    }

    private static bool IsValidOwner(OwnerScope owner)
    {
        return owner.IsLocalSingleUser
               || (!string.IsNullOrWhiteSpace(owner.NormalizedValue)
                   && !owner.UsesLocalSingleUserValue);
    }

    private static RunnerLibraryMutationResult InvalidMutation(string error)
    {
        return new RunnerLibraryMutationResult(
            RunnerLibraryOperationOutcome.Invalid,
            Error: error);
    }

    private static RunnerLibraryMutationResult UnavailableMutation()
    {
        return new RunnerLibraryMutationResult(
            RunnerLibraryOperationOutcome.Unavailable,
            Error: "Runner Library lifecycle authority is unavailable.");
    }

    private static RunnerLibraryListResult UnavailableList()
    {
        return new RunnerLibraryListResult(
            RunnerLibraryOperationOutcome.Unavailable,
            [],
            "Runner Library lifecycle authority is unavailable.");
    }
}
