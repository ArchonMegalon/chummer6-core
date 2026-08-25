namespace Chummer.Contracts.Workspaces;

public enum RunnerLibraryLifecycle
{
    Active = 0,
    Archived = 1,
    Deleted = 2
}

[Flags]
public enum RunnerLibraryLifecycleFilter
{
    None = 0,
    Active = 1,
    Archived = 2,
    Deleted = 4,
    Available = Active | Archived,
    All = Active | Archived | Deleted
}

public enum RunnerLibraryMutationKind
{
    Rename = 0,
    Duplicate = 1,
    Archive = 2,
    RestoreArchived = 3,
    Delete = 4,
    RestoreDeleted = 5
}

public enum RunnerLibraryOperationOutcome
{
    Success = 0,
    Applied = 1,
    Replayed = 2,
    Missing = 3,
    Conflict = 4,
    Invalid = 5,
    Corrupt = 6,
    Unavailable = 7
}

public sealed record RunnerLibraryProvenance(
    CharacterWorkspaceId SourceRunnerId,
    long SourceContentRevision,
    string SourceContentDigestSha256);

public sealed record RunnerLibraryItem(
    CharacterWorkspaceId Id,
    string DisplayName,
    RunnerLibraryLifecycle Lifecycle,
    RunnerLibraryLifecycle? LifecycleBeforeDelete,
    long LifecycleRevision,
    long ContentRevision,
    long SavedRevision,
    string ContentDigestSha256,
    DateTimeOffset LastContentUpdatedUtc,
    DateTimeOffset LastLifecycleUpdatedUtc,
    RunnerLibraryProvenance? Provenance = null);

public sealed record RunnerLibraryListQuery(
    RunnerLibraryLifecycleFilter Lifecycles = RunnerLibraryLifecycleFilter.Available,
    string? NameContains = null);

public sealed record RunnerLibraryListResult(
    RunnerLibraryOperationOutcome Outcome,
    IReadOnlyList<RunnerLibraryItem> Items,
    string? Error = null)
{
    public bool Success => Outcome is RunnerLibraryOperationOutcome.Success;
}

public sealed record RenameRunnerCommand(
    CharacterWorkspaceId RunnerId,
    long ExpectedLifecycleRevision,
    long ExpectedContentRevision,
    string ExpectedContentDigestSha256,
    string DisplayName,
    string IdempotencyKey);

public sealed record DuplicateRunnerCommand(
    CharacterWorkspaceId SourceRunnerId,
    CharacterWorkspaceId NewRunnerId,
    long ExpectedSourceLifecycleRevision,
    long ExpectedSourceContentRevision,
    string ExpectedSourceContentDigestSha256,
    string DisplayName,
    string IdempotencyKey);

public sealed record ArchiveRunnerCommand(
    CharacterWorkspaceId RunnerId,
    long ExpectedLifecycleRevision,
    long ExpectedContentRevision,
    string ExpectedContentDigestSha256,
    string IdempotencyKey);

public sealed record RestoreArchivedRunnerCommand(
    CharacterWorkspaceId RunnerId,
    long ExpectedLifecycleRevision,
    long ExpectedContentRevision,
    string ExpectedContentDigestSha256,
    string IdempotencyKey);

public sealed record DeleteRunnerCommand(
    CharacterWorkspaceId RunnerId,
    long ExpectedLifecycleRevision,
    long ExpectedContentRevision,
    string ExpectedContentDigestSha256,
    string IdempotencyKey);

public sealed record RestoreDeletedRunnerCommand(
    CharacterWorkspaceId RunnerId,
    long ExpectedLifecycleRevision,
    long ExpectedContentRevision,
    string ExpectedContentDigestSha256,
    string IdempotencyKey);

public sealed record RunnerLibraryMutationReceipt(
    string Schema,
    RunnerLibraryMutationKind Kind,
    CharacterWorkspaceId RunnerId,
    CharacterWorkspaceId? SourceRunnerId,
    string IdempotencyKeyDigestSha256,
    string CommandDigestSha256,
    string BeforeStateDigestSha256,
    string AfterStateDigestSha256,
    string BeforeDisplayName,
    string AfterDisplayName,
    RunnerLibraryLifecycle BeforeLifecycle,
    RunnerLibraryLifecycle AfterLifecycle,
    RunnerLibraryLifecycle? BeforeLifecycleBeforeDelete,
    RunnerLibraryLifecycle? AfterLifecycleBeforeDelete,
    long BeforeLifecycleRevision,
    long AfterLifecycleRevision,
    RunnerLibraryProvenance? BeforeProvenance,
    RunnerLibraryProvenance? AfterProvenance,
    long ContentRevision,
    string ContentDigestSha256,
    DateTimeOffset CommittedAtUtc,
    string ReceiptDigestSha256);

public sealed record RunnerLibraryMutationResult(
    RunnerLibraryOperationOutcome Outcome,
    RunnerLibraryItem? Item = null,
    RunnerLibraryMutationReceipt? Receipt = null,
    long? CurrentLifecycleRevision = null,
    string? Error = null)
{
    public bool Success => Outcome is RunnerLibraryOperationOutcome.Applied
        or RunnerLibraryOperationOutcome.Replayed;
}
