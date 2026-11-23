using System.Diagnostics.CodeAnalysis;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Chummer.Tests;
// Exact nested helper extracted from the pinned existing MSTest source.
public sealed class CharacterCreationAttributesServiceTests
{
    internal sealed class AllocationOwners : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private OwnerContextStamp _stamp = new(OwnerScope.LocalSingleUser, Guid.NewGuid().ToString("N"), 0);
        public int ActiveLeases { get; private set; }
        public OwnerScope Current => Capture().Owner;
        public OwnerContextStamp Capture() { lock (_gate) return _stamp; }
        public void Set(OwnerScope owner)
        {
            lock (_gate)
            {
                Assert.AreEqual(0, ActiveLeases);
                _stamp = _stamp with { Owner = owner, TransitionRevision = checked(_stamp.TransitionRevision + 1) };
            }
        }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            lease = null;
            if (expected != _stamp || !expected.IsValid || ActiveLeases != 0)
            { Monitor.Exit(_gate); return false; }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }
        private sealed class Lease(AllocationOwners owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp { get { ObjectDisposedException.ThrowIf(_disposed, this); return stamp; } }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.ActiveLeases--;
                Monitor.Exit(owner._gate);
            }
        }
    }
}
