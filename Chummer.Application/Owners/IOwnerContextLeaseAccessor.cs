using System.Diagnostics.CodeAnalysis;

namespace Chummer.Application.Owners;

/// <summary>
/// Optional capability implemented by the actual owner authority, shared with every owner writer.
/// </summary>
/// <remarks>
/// There is deliberately no default adapter over <see cref="IOwnerContextAccessor.Current"/>:
/// polling an owner value cannot detect a switch away and back or exclude a concurrent writer.
/// Consumers must use the capability on their existing accessor instance, not another authority.
/// </remarks>
public interface IOwnerContextLeaseAccessor : IOwnerContextAccessor
{
    /// <summary>Atomically captures the current owner and its authority transition history.</summary>
    OwnerContextStamp Capture();

    /// <summary>
    /// Atomically compares the complete expected stamp and excludes owner transitions until the
    /// returned lease is disposed. Invalid, foreign, or stale stamps return false and a null lease.
    /// </summary>
    /// <remarks>
    /// Acquire after asynchronous queue admission; acquire, use, and dispose on the same thread.
    /// Do not hold a lease across an await. A lease does not establish workspace operation attribution.
    /// </remarks>
    bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease);
}
