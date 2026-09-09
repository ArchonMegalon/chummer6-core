using System.Diagnostics.CodeAnalysis;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Owners;

public sealed class LocalOwnerContextAccessor : IOwnerContextLeaseAccessor
{
    // This authority is genuinely immutable. It has no mutable owner writer to exclude.
    private readonly OwnerContextStamp _stamp = new(
        OwnerScope.LocalSingleUser,
        Guid.NewGuid().ToString("N"),
        TransitionRevision: 0);

    public OwnerScope Current => OwnerScope.LocalSingleUser;

    public OwnerContextStamp Capture() => _stamp;

    public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
    {
        lease = null;
        if (!expected.IsValid || expected != _stamp)
            return false;

        lease = new LocalOwnerContextLease(_stamp);
        return true;
    }

    private sealed class LocalOwnerContextLease(OwnerContextStamp stamp) : IOwnerContextLease
    {
        private int _disposed;

        public OwnerContextStamp Stamp
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                return stamp;
            }
        }

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }
}
