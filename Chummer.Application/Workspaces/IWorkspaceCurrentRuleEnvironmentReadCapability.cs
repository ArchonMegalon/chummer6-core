using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Closed synchronous display reads using the caller's already-held actual owner lease.
/// No source resolver, execution authority or mutation capability escapes this boundary.
/// The caller owns the lease and must not dispose it until the read returns.
/// </summary>
public interface IWorkspaceCurrentRuleEnvironmentReadCapability
{
    // Optional, bounded display-only reuse. No lease is acquired here; each read
    // must supply the original live lease and repeat all source/record fences.
    IWorkspaceCurrentDisplayObservation? BeginCurrentDisplayObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id)
        => null;

    CommandResult<WorkspaceDocumentSnapshot> GetCurrentDisplayWorkspace(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, CancellationToken cancellationToken = default);

    CommandResult<WorkspaceOverviewProjection> GetCurrentDisplayOverview(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store-owned observation, admission and projection under one workspace lease.
/// The typed codec dependency is used only for a proven unbound document during that
/// observation. Bound documents always use the exact retained replay sources instead.
/// Unsupported, malformed or refused observations never authorize an ambient retry.
/// </summary>
public interface IWorkspaceCurrentDisplayStoreCapability
{
    IWorkspaceCurrentDisplayObservation? BeginCurrentDisplayObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id,
        IRulesetWorkspaceCodecResolver unboundCodecs)
        => null;

    CommandResult<WorkspaceDocumentSnapshot> GetCurrentDisplayWorkspace(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, CancellationToken cancellationToken = default);

    CommandResult<WorkspaceOverviewProjection> GetCurrentDisplayOverview(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, IRulesetWorkspaceCodecResolver unboundCodecs,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Display-only roster observation under the caller's existing owner lease.
/// Retained runners must pass the same closed admission as individual display reads;
/// refusal never authorizes an ordinary-list or ambient-source retry.
/// </summary>
public interface IWorkspaceCurrentDisplayRosterCapability
{
    // Explicit saved-workspace refresh: the first display read, rather than
    // a startup roster, may bind the evaluation. Every read still admits live bytes.
    IWorkspaceStartupDisplayObservation? BeginWorkspaceRefreshObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id)
        => null;

    IWorkspaceStartupDisplayObservation? BeginStartupDisplayObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId? preferredId = null)
        => null;

    CommandResult<IReadOnlyList<WorkspaceListItem>> ListCurrentDisplayWorkspaces(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        int? maxCount = null, CancellationToken cancellationToken = default);
}

public interface IWorkspaceCurrentDisplayRosterStoreCapability
{
    IWorkspaceStartupDisplayObservation? BeginWorkspaceRefreshObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IRulesetWorkspaceCodecResolver unboundCodecs, CharacterWorkspaceId id)
        => null;

    IWorkspaceStartupDisplayObservation? BeginStartupDisplayObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IRulesetWorkspaceCodecResolver unboundCodecs, CharacterWorkspaceId? preferredId = null)
        => null;

    CommandResult<IReadOnlyList<WorkspaceListItem>> ListCurrentDisplayWorkspaces(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        IRulesetWorkspaceCodecResolver unboundCodecs, int? maxCount = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One exact display observation, retired when its caller finishes. It carries
/// no mutation authority or source resolver. Each read acquires the workspace
/// operation under the original live owner lease and validates the live record,
/// continuation and retained source bytes before returning detached data. Any
/// failed read permanently retires the observation; no stale-data retry is allowed.
/// </summary>
public interface IWorkspaceCurrentDisplayObservation : IDisposable
{
    CommandResult<WorkspaceDocumentSnapshot> GetWorkspace(
        IOwnerContextLease ownerLease, CancellationToken cancellationToken = default);
    CommandResult<WorkspaceOverviewProjection> GetOverview(
        IOwnerContextLease ownerLease, CancellationToken cancellationToken = default);
}

/// <summary>
/// One startup invocation, with at most one admitted workspace evaluation.
/// Every roster/display read retains its live owner, record and source checks.
/// Borrowed views end independently; the caller disposes this root after startup.
/// A failed read of an already shared workspace permanently retires the root.
/// No owner lease is retained between synchronous calls, and no mutation uses it.
/// </summary>
public interface IWorkspaceStartupDisplayObservation : IDisposable
{
    CommandResult<IReadOnlyList<WorkspaceListItem>> ListWorkspaces(
        IOwnerContextLease ownerLease, int? maxCount = null, CancellationToken cancellationToken = default);
    IWorkspaceCurrentDisplayObservation? BorrowWorkspace(CharacterWorkspaceId id);
}
