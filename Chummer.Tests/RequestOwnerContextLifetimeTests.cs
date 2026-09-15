using System.Runtime.ExceptionServices;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RequestAuthority = Chummer.Infrastructure.Owners.RequestOwnerContextAccessor;

namespace Chummer.Tests;

[TestClass]
public sealed class RequestOwnerContextLifetimeTests
{
    private static readonly OwnerScope Owner = new("owner-a");
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void Capture_and_lease_preserve_the_exact_fixed_nonlocal_owner_and_live_issuer()
    {
        using RequestAuthority authority = new(Owner);
        IOwnerContextLeaseAccessor capability = authority;
        OwnerContextStamp stamp = capability.Capture();

        Assert.AreEqual(Owner, capability.Current);
        Assert.AreEqual(stamp, capability.Capture());
        Assert.AreEqual(0L, stamp.TransitionRevision);
        Assert.IsTrue(stamp.IsValid);
        Assert.IsFalse(stamp.Owner.IsLocalSingleUser);
        Assert.IsFalse(stamp.Owner.UsesLocalSingleUserValue);
        Assert.IsFalse(string.IsNullOrWhiteSpace(stamp.AuthorityInstanceId));
        using IOwnerContextLease lease = Acquire(authority, stamp);
        Assert.AreEqual(stamp, lease.Stamp);
    }

    [TestMethod]
    public void Identical_owner_values_in_distinct_lifetimes_never_share_issuer_or_accept_old_stamps()
    {
        RequestAuthority first = new(Owner);
        OwnerContextStamp old = first.Capture();
        first.Dispose();
        using RequestAuthority next = new(Owner);
        OwnerContextStamp current = next.Capture();

        Assert.AreEqual(old.Owner, current.Owner);
        Assert.AreNotEqual(old.AuthorityInstanceId, current.AuthorityInstanceId);
        AssertDenied(next, old);
        AssertDenied(first, current);
        using RequestAuthority concurrent = new(Owner);
        Assert.AreNotEqual(current.AuthorityInstanceId, concurrent.Capture().AuthorityInstanceId);
        AssertDenied(next, concurrent.Capture());
        using IOwnerContextLease lease = Acquire(next, current);
        Assert.AreEqual(current, lease.Stamp);
    }

    [TestMethod]
    public void Admission_compares_every_stamp_field_and_rejects_malformed_or_foreign_values()
    {
        using RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        OwnerContextStamp[] invalid =
        [
            default,
            stamp with { Owner = default },
            stamp with { Owner = new OwnerScope("owner-b") },
            stamp with { Owner = new OwnerScope("OWNER-A") },
            stamp with { Owner = new OwnerScope("owner-a ") },
            stamp with { Owner = OwnerScope.LocalSingleUser },
            stamp with { Owner = new OwnerScope("local-single-user") },
            stamp with { Owner = OwnerScope.LocalSingleUser with { Value = Owner.Value } },
            stamp with { AuthorityInstanceId = null! },
            stamp with { AuthorityInstanceId = " " },
            stamp with { AuthorityInstanceId = stamp.AuthorityInstanceId + "x" },
            stamp with { TransitionRevision = -1 },
            stamp with { TransitionRevision = 1 },
            stamp with { TransitionRevision = long.MaxValue }
        ];
        foreach (OwnerContextStamp candidate in invalid)
        {
            AssertDenied(authority, candidate);
        }
        Assert.AreEqual(stamp, authority.Capture());
        using IOwnerContextLease lease = Acquire(authority, stamp);
        Assert.AreEqual(stamp, lease.Stamp);
    }

    [TestMethod]
    public void Construction_rejects_noncanonical_malformed_and_any_trusted_or_reserved_local_owner()
    {
        OwnerScope[] invalid =
        [
            default,
            new(null!),
            new(""),
            new(" \t"),
            new(" owner-a"),
            new("owner-a "),
            new("OWNER-A"),
            new("owner\n-a"),
            new("owner\0-a"),
            new("owner-\ud800"),
            new("owner-\udfff"),
            OwnerScope.LocalSingleUser,
            new("local-single-user"),
            new(" LOCAL-SINGLE-USER "),
            OwnerScope.LocalSingleUser with { Value = Owner.Value }
        ];
        foreach (OwnerScope owner in invalid)
        {
            Assert.ThrowsExactly<ArgumentException>(() => new RequestAuthority(owner));
        }
    }

    [TestMethod]
    public void Canonical_generic_owners_keep_interior_spaces_punctuation_unicode_and_long_values()
    {
        string[] values =
        [
            "tenant owner",
            "account:alice+team@example.com/path?key=value&v=1",
            "rúnner-東京-😀",
            new string('x', 16_384)
        ];
        foreach (string value in values)
        {
            OwnerScope owner = new(value);
            Assert.AreEqual(value, owner.NormalizedValue);
            using RequestAuthority authority = new(owner);
            OwnerContextStamp stamp = authority.Capture();
            Assert.AreEqual(owner, authority.Current);
            using IOwnerContextLease lease = Acquire(authority, stamp);
            Assert.AreEqual(owner, lease.Stamp.Owner);
        }
    }

    [TestMethod]
    public void Ending_is_permanent_and_idempotent_and_does_not_reissue_a_stamp()
    {
        RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        authority.Dispose();
        authority.Dispose();

        AssertEnded(authority, stamp);
        AssertDenied(authority, default);
        authority.Dispose();
    }

    [TestMethod]
    public void Same_thread_authority_disposal_with_a_live_lease_throws_without_changing_admission()
    {
        using RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        using (IOwnerContextLease lease = Acquire(authority, stamp))
        {
            Assert.ThrowsExactly<InvalidOperationException>(authority.Dispose);
            Assert.AreEqual(stamp, lease.Stamp);
            Assert.AreEqual(stamp, authority.Capture());
            Assert.AreEqual(Owner, authority.Current);
            using IOwnerContextLease nested = Acquire(authority, stamp);
            Assert.AreEqual(stamp, nested.Stamp);
            Assert.ThrowsExactly<InvalidOperationException>(authority.Dispose);
        }
        Assert.AreEqual(stamp, authority.Capture());
    }

    [TestMethod]
    public void Wrong_thread_live_lease_access_and_disposal_fail_without_poisoning_the_real_release()
    {
        RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        IOwnerContextLease lease = Acquire(authority, stamp);
        Worker? misuse = null;
        try
        {
            misuse = new Worker(() =>
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => _ = lease.Stamp);
                Assert.ThrowsExactly<InvalidOperationException>(lease.Dispose);
            });
            misuse.Join();
            Assert.AreEqual(stamp, lease.Stamp);
            using IOwnerContextLease nested = Acquire(authority, stamp);
            Assert.AreEqual(stamp, nested.Stamp);
        }
        finally
        {
            ReleaseAndJoin([lease], misuse);
        }
        Worker? afterRelease = null;
        try
        {
            afterRelease = new Worker(() =>
            {
                lease.Dispose();
                lease.Dispose();
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = lease.Stamp);
                authority.Dispose();
            });
        }
        finally
        {
            JoinAll(afterRelease);
        }
        AssertEnded(authority, stamp);
    }

    [TestMethod]
    public void Nested_leases_are_balanced_and_duplicate_release_cannot_release_the_other_live_lease()
    {
        RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        IOwnerContextLease outer = Acquire(authority, stamp);
        IOwnerContextLease inner = Acquire(authority, stamp);
        Worker? disposer = null;
        try
        {
            disposer = new Worker(authority.Dispose);
            WaitUntilClosing(authority, stamp);
            WaitUntilBlocked(disposer);
            Assert.AreEqual(stamp, outer.Stamp);
            Assert.AreEqual(stamp, inner.Stamp);
            inner.Dispose();
            inner.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = inner.Stamp);
            Assert.AreEqual(stamp, outer.Stamp);
            Assert.IsFalse(disposer.IsCompleted);
            WaitUntilBlocked(disposer);
        }
        finally
        {
            ReleaseAndJoin([inner, outer], disposer);
        }
        AssertEnded(authority, stamp);
    }

    [TestMethod]
    public void External_disposal_closes_new_admission_before_waiting_for_the_live_lease()
    {
        RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        IOwnerContextLease held = Acquire(authority, stamp);
        Worker? disposer = null;
        try
        {
            disposer = new Worker(authority.Dispose);
            WaitUntilClosing(authority, stamp);
            WaitUntilBlocked(disposer);
            AssertEnded(authority, stamp);
            Assert.ThrowsExactly<InvalidOperationException>(authority.Dispose);
            Assert.AreEqual(stamp, held.Stamp);
            Assert.IsFalse(disposer.IsCompleted);
        }
        finally
        {
            ReleaseAndJoin([held], disposer);
        }
        AssertEnded(authority, stamp);
    }

    [TestMethod]
    public void Every_concurrent_disposer_waits_for_the_existing_lease_to_release()
    {
        RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        IOwnerContextLease held = Acquire(authority, stamp);
        Worker? first = null;
        Worker? second = null;
        Worker? third = null;
        try
        {
            first = new Worker(authority.Dispose);
            WaitUntilClosing(authority, stamp);
            second = new Worker(authority.Dispose);
            third = new Worker(authority.Dispose);
            WaitUntilBlocked(first);
            WaitUntilBlocked(second);
            WaitUntilBlocked(third);
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted);
            Assert.IsFalse(third.IsCompleted);
            Assert.AreEqual(stamp, held.Stamp);
        }
        finally
        {
            ReleaseAndJoin([held], first, second, third);
        }
        AssertEnded(authority, stamp);
        authority.Dispose();
    }

    [TestMethod]
    public void An_acquire_already_waiting_before_closing_cannot_reopen_admission_after_gate_release()
    {
        RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        IOwnerContextLease held = Acquire(authority, stamp);
        int enteringAcquire = 0;
        Worker? waiter = null;
        Worker? disposer = null;
        try
        {
            waiter = new Worker(() =>
            {
                Volatile.Write(ref enteringAcquire, 1);
                bool acquired = authority.TryAcquire(stamp, out IOwnerContextLease? lease);
                try
                {
                    Assert.IsFalse(acquired);
                    Assert.IsNull(lease);
                }
                finally
                {
                    lease?.Dispose();
                }
            });
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref enteringAcquire) != 0, Bound));
            // This worker has no waits between its volatile signal and TryAcquire. Observing it
            // blocked proves an actual pre-closing monitor waiter, not just scheduling.
            WaitUntilBlocked(waiter);
            disposer = new Worker(authority.Dispose);
            WaitUntilClosing(authority, stamp);
            Assert.IsFalse(waiter.IsCompleted);
        }
        finally
        {
            ReleaseAndJoin([held], waiter, disposer);
        }
        AssertEnded(authority, stamp);
    }

    [TestMethod]
    public void A_capture_already_waiting_before_closing_throws_when_the_gate_is_released()
    {
        RequestAuthority authority = new(Owner);
        OwnerContextStamp stamp = authority.Capture();
        IOwnerContextLease held = Acquire(authority, stamp);
        int enteringCapture = 0;
        Worker? waiter = null;
        Worker? disposer = null;
        try
        {
            waiter = new Worker(() =>
            {
                Volatile.Write(ref enteringCapture, 1);
                try
                {
                    _ = authority.Capture();
                    Assert.Fail("A queued capture must not return a stamp after closing.");
                }
                catch (ObjectDisposedException)
                {
                }
            });
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref enteringCapture) != 0, Bound));
            WaitUntilBlocked(waiter);
            disposer = new Worker(authority.Dispose);
            WaitUntilClosing(authority, stamp);
            Assert.IsFalse(waiter.IsCompleted);
        }
        finally
        {
            ReleaseAndJoin([held], waiter, disposer);
        }
        AssertEnded(authority, stamp);
    }

    [TestMethod]
    public void Capture_racing_lifetime_end_returns_only_the_exact_prior_stamp_or_disposed()
    {
        for (int iteration = 0; iteration < 16; iteration++)
        {
            RequestAuthority authority = new(Owner);
            OwnerContextStamp stamp = authority.Capture();
            using Barrier start = new(3);
            Worker? capture = null;
            Worker? disposer = null;
            try
            {
                capture = new Worker(() =>
                {
                    Assert.IsTrue(start.SignalAndWait(Bound));
                    try
                    {
                        Assert.AreEqual(stamp, authority.Capture());
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                });
                disposer = new Worker(() =>
                {
                    Assert.IsTrue(start.SignalAndWait(Bound));
                    authority.Dispose();
                });
                Assert.IsTrue(start.SignalAndWait(Bound));
            }
            finally
            {
                JoinAll(capture, disposer);
            }
            AssertEnded(authority, stamp);
        }
    }

    [TestMethod]
    public void Acquisition_racing_lifetime_end_is_either_denied_or_an_exact_balanced_live_lease()
    {
        for (int iteration = 0; iteration < 16; iteration++)
        {
            RequestAuthority authority = new(Owner);
            OwnerContextStamp stamp = authority.Capture();
            using Barrier start = new(3);
            Worker? acquire = null;
            Worker? disposer = null;
            try
            {
                acquire = new Worker(() =>
                {
                    Assert.IsTrue(start.SignalAndWait(Bound));
                    bool acquired = authority.TryAcquire(stamp, out IOwnerContextLease? lease);
                    try
                    {
                        if (acquired)
                        {
                            Assert.IsNotNull(lease);
                            Assert.AreEqual(stamp, lease.Stamp);
                        }
                        else
                        {
                            Assert.IsNull(lease);
                        }
                    }
                    finally
                    {
                        lease?.Dispose();
                    }
                });
                disposer = new Worker(() =>
                {
                    Assert.IsTrue(start.SignalAndWait(Bound));
                    authority.Dispose();
                });
                Assert.IsTrue(start.SignalAndWait(Bound));
            }
            finally
            {
                JoinAll(acquire, disposer);
            }
            AssertEnded(authority, stamp);
        }
    }

    private static IOwnerContextLease Acquire(RequestAuthority authority, OwnerContextStamp stamp)
    {
        Assert.IsTrue(authority.TryAcquire(stamp, out IOwnerContextLease? lease));
        Assert.IsNotNull(lease);
        return lease;
    }

    private static void AssertDenied(RequestAuthority authority, OwnerContextStamp stamp)
    {
        bool acquired = authority.TryAcquire(stamp, out IOwnerContextLease? lease);
        try
        {
            Assert.IsFalse(acquired);
            Assert.IsNull(lease);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private static void AssertEnded(RequestAuthority authority, OwnerContextStamp stamp)
    {
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = authority.Current);
        Assert.ThrowsExactly<ObjectDisposedException>(() => authority.Capture());
        AssertDenied(authority, stamp);
    }

    private static void WaitUntilClosing(RequestAuthority authority, OwnerContextStamp stamp)
    {
        // Called only by the original lease holder, so successful probes are reentrant
        // and immediately release their own monitor entry. No probe can block that holder.
        Assert.IsTrue(SpinWait.SpinUntil(() =>
        {
            if (!authority.TryAcquire(stamp, out IOwnerContextLease? probe))
            {
                Assert.IsNull(probe);
                return true;
            }
            probe!.Dispose();
            return false;
        }, Bound), "The disposer did not publish closing within the bounded observation window.");
    }

    private static void WaitUntilBlocked(Worker worker)
    {
        Assert.IsTrue(SpinWait.SpinUntil(
            () => worker.IsCompleted || (worker.ThreadState & ThreadState.WaitSleepJoin) != 0,
            Bound), "The dedicated worker did not reach its monitor wait.");
        Assert.IsFalse(worker.IsCompleted, "The worker completed instead of waiting for the live lease.");
    }

    private static void ReleaseAndJoin(IOwnerContextLease[] leases, params Worker?[] workers)
    {
        Exception? firstFailure = null;
        foreach (IOwnerContextLease lease in leases)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception failure)
            {
                firstFailure ??= failure;
            }
        }
        try
        {
            JoinAll(workers);
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
        }
        if (firstFailure is not null)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    private static void JoinAll(params Worker?[] workers)
    {
        Exception? firstFailure = null;
        foreach (Worker? worker in workers)
        {
            try
            {
                worker?.Join();
            }
            catch (Exception failure)
            {
                firstFailure ??= failure;
            }
        }
        if (firstFailure is not null)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    private sealed class Worker
    {
        private readonly Thread _thread;
        private Exception? _failure;
        private int _completed;

        public Worker(Action action)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception failure)
                {
                    _failure = failure;
                }
                finally
                {
                    Volatile.Write(ref _completed, 1);
                }
            }) { IsBackground = true, Name = "request-owner-lifetime-test" };
            _thread.Start();
        }

        public bool IsCompleted => Volatile.Read(ref _completed) != 0;
        public ThreadState ThreadState => _thread.ThreadState;

        public void Join()
        {
            Assert.IsTrue(_thread.Join(Bound), "A lifetime worker failed to finish after leases were released.");
            if (_failure is not null)
            {
                ExceptionDispatchInfo.Capture(_failure).Throw();
            }
        }
    }
}
