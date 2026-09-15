using System.Diagnostics.CodeAnalysis;
using System.Text;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Owners;

/// <summary>
/// A fixed, non-local owner authority with one explicit in-process lifetime.
/// The host must already have authorized the supplied owner; construction does
/// not authenticate an account, authorize a workspace, or prove ongoing consent.
/// </summary>
/// <remarks>
/// Share this exact instance with every consumer of this lifetime. Do not install
/// it as a globally switched owner or reconstruct it from a captured stamp.
/// Leases must be acquired, used and disposed on the same thread, never across an
/// await. Balanced nested leases on that thread are supported.
/// </remarks>
public sealed class RequestOwnerContextAccessor : IOwnerContextLeaseAccessor, IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object _gate = new();
    private readonly OwnerContextStamp _stamp;
    private int _closing;

    public RequestOwnerContextAccessor(OwnerScope owner)
    {
        if (string.IsNullOrWhiteSpace(owner.Value)
            || owner.IsLocalSingleUser || owner.UsesLocalSingleUserValue
            || owner != new OwnerScope(owner.Value)
            || !string.Equals(owner.Value, owner.NormalizedValue, StringComparison.Ordinal)
            || owner.Value.Any(char.IsControl))
        {
            throw InvalidOwner();
        }

        try
        {
            // Validate lossless representation without imposing a new account-id
            // alphabet or length limit on the existing OwnerScope contract.
            _ = StrictUtf8.GetByteCount(owner.Value);
        }
        catch (EncoderFallbackException)
        {
            throw InvalidOwner();
        }

        // The owner never changes. Ending this lifetime does not issue another
        // stamp, and a later request always receives a different issuer identity.
        _stamp = new(owner, Guid.NewGuid().ToString("N"), TransitionRevision: 0);
    }

    /// <summary>Returns the fixed owner, or throws once lifetime termination begins.</summary>
    public OwnerScope Current => Capture().Owner;

    /// <summary>Captures this live issuer; the returned value is not authorization.</summary>
    public OwnerContextStamp Capture()
    {
        ThrowIfClosing();
        lock (_gate)
        {
            ThrowIfClosing();
            return _stamp;
        }
    }

    public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
    {
        lease = null;
        if (!expected.IsValid || expected != _stamp || Volatile.Read(ref _closing) != 0)
            return false;

        bool lockTaken = false;
        try
        {
            Monitor.Enter(_gate, ref lockTaken);
            // A matching request may have queued before another thread began
            // ending the lifetime. Acquiring the gate cannot reopen admission.
            if (Volatile.Read(ref _closing) != 0)
                return false;

            lease = new RequestOwnerContextLease(this, Thread.CurrentThread);
            lockTaken = false; // This lease now owns exactly one Monitor entry.
            return true;
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(_gate);
        }
    }

    /// <summary>
    /// Stops new admissions and waits for admitted leases to finish. After this
    /// returns, the authority can never issue or admit a lease again.
    /// </summary>
    /// <remarks>
    /// Ending from a thread holding one of this authority's leases is invalid
    /// and throws before changing state. End the lifetime outside all its lease
    /// scopes. A live lease on another thread must finish; this method cannot
    /// preempt that synchronous work and has no hard wall-clock deadline.
    /// Concurrent disposers all wait for lease release, not only the first one.
    /// </remarks>
    public void Dispose()
    {
        if (Monitor.IsEntered(_gate))
            throw new InvalidOperationException("An owner lifetime cannot end while this thread holds its lease.");

        Interlocked.Exchange(ref _closing, 1);
        lock (_gate)
        {
            // Crossing this gate after publishing closing is the terminal
            // barrier. No new lease can enter, and every prior lease has left.
        }
    }

    private void ThrowIfClosing()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing) != 0, this);

    private static ArgumentException InvalidOwner()
        => new("A canonical, non-local owner identity is required.", "owner");

    private sealed class RequestOwnerContextLease(RequestOwnerContextAccessor authority, Thread acquiringThread)
        : IOwnerContextLease
    {
        private int _disposed;

        public OwnerContextStamp Stamp
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                RequireAcquiringThread();
                // Closing only prevents new admission. The existing lease
                // excludes successful authority disposal until it is released.
                return authority._stamp;
            }
        }

        public void Dispose()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            // Check before touching state so mistaken cross-thread disposal
            // cannot strand the real owner's Monitor entry.
            RequireAcquiringThread();
            Volatile.Write(ref _disposed, 1);
            Monitor.Exit(authority._gate);
        }

        private void RequireAcquiringThread()
        {
            if (!ReferenceEquals(Thread.CurrentThread, acquiringThread))
                throw new InvalidOperationException("An owner lease must be used and disposed on its acquiring thread.");
        }
    }
}
