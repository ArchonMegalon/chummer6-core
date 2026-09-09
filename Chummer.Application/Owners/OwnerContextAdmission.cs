using System.Diagnostics.CodeAnalysis;

namespace Chummer.Application.Owners;

internal static class OwnerContextAdmission
{
    // Only the host's actual accessor can grant admission. Never poll Current or
    // manufacture an authority from a matching stable owner value.
    internal static bool TryAcquire(IOwnerContextAccessor accessor, OwnerContextStamp expected,
        [NotNullWhen(true)] out IOwnerContextLease? lease)
    {
        lease = null;
        if (!expected.IsValid || accessor is not IOwnerContextLeaseAccessor authority)
            return false;
        if (authority.TryAcquire(expected, out lease) && lease is not null && lease.Stamp == expected)
            return true;
        lease?.Dispose();
        lease = null;
        return false;
    }
}
