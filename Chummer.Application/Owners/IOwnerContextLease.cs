namespace Chummer.Application.Owners;

/// <summary>A live exclusion of transitions by the issuing owner authority.</summary>
public interface IOwnerContextLease : IDisposable
{
    /// <summary>The admitted stamp. Throws <see cref="ObjectDisposedException"/> after disposal.</summary>
    OwnerContextStamp Stamp { get; }

    // Dispose releases the authority exclusion and must be idempotent.
}
