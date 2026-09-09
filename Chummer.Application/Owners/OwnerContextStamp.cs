using Chummer.Contracts.Owners;

namespace Chummer.Application.Owners;

/// <summary>
/// An in-process snapshot of one owner authority, not an authorization token or a persisted receipt.
/// </summary>
/// <remarks>
/// Authority identity must remain stable for the lifetime of its transition history. The revision
/// must advance on every owner transition, including a switch away and back, and must never wrap
/// or be reused within that authority. Compare the complete stamp, including trusted-local identity.
/// </remarks>
public readonly record struct OwnerContextStamp(
    OwnerScope Owner,
    string AuthorityInstanceId,
    long TransitionRevision)
{
    /// <summary>Structural validity only; only the issuing authority can admit a live lease.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Owner.Value)
        && !string.IsNullOrWhiteSpace(AuthorityInstanceId)
        && TransitionRevision >= 0;
}
