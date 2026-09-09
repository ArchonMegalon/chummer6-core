using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationBootstrapService(
    CharacterCreationBootstrapService service, IOwnerContextAccessor ownerContext)
    : IOwnerBoundCharacterCreationBootstrapService
{
    public CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> Create(
        OwnerContextStamp expectedOwner, CharacterCreationBootstrapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationBootstrapOutcomes.Unavailable, null,
                [CharacterCreationBootstrapBlockers.AtomicCreateUnavailable]);
        using (lease)
            return service.Create(expectedOwner, request);
    }

    public CharacterCreationBootstrapActivationAttempt CreateActivation(
        OwnerContextStamp expectedOwner, CharacterCreationBootstrapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationBootstrapOutcomes.Unavailable, null, null,
                [CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable]);
        using (lease)
            return service.CreateActivation(expectedOwner, request);
    }

    public bool TryValidateCurrent(OwnerContextStamp expectedOwner,
        CharacterCreationBootstrapActivationBundle activation, out IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(activation);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
        {
            blockers = [CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable];
            return false;
        }
        using (lease)
            return service.TryValidateCurrent(expectedOwner, activation, out blockers);
    }
}
