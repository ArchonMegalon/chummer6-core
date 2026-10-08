using Chummer.Contracts.Owners;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Optional display-roster capability: project each fully validated store read
/// without retaining whole documents or reading them a second time. Results are
/// newest first; a positive maxCount limits returned rows, not integrity checks.
/// Projections are read-only, synchronous and may run for rows beyond that limit.
/// A projection failure is surfaced only if its row is selected for the result.
/// Every call is a fresh observation, not a cache, inventory or mutation grant.
/// Linked-owner implementations must never fall back to the local store lane.
/// </summary>
public interface IWorkspaceStoreProjection
{
    IReadOnlyList<T> ListProjected<T>(
        Func<WorkspaceStoredDocument, T> projection,
        int? maxCount = null);

    IReadOnlyList<T> ListProjected<T>(
        OwnerScope owner,
        Func<WorkspaceStoredDocument, T> projection,
        int? maxCount = null);
}
