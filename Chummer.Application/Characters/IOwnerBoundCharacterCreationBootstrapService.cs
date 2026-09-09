using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Creation for a caller-retained owner stamp. Calls are synchronous: admission,
/// domain evaluation and storage use one same-thread lease of the actual host authority.
/// The stamp is transient and is never added to durable character or receipt data.
/// </summary>
public interface IOwnerBoundCharacterCreationBootstrapService
{
    CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> Create(
        OwnerContextStamp expectedOwner, CharacterCreationBootstrapRequest request);

    CharacterCreationBootstrapActivationAttempt CreateActivation(
        OwnerContextStamp expectedOwner, CharacterCreationBootstrapRequest request);

    bool TryValidateCurrent(OwnerContextStamp expectedOwner,
        CharacterCreationBootstrapActivationBundle activation, out IReadOnlyList<string> blockers);
}
