using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCreationLoadoutService
{
    CharacterCreationFoundationResult<CharacterCreationLoadoutState> Prepare(CharacterWorkspaceId workspaceId, CancellationToken cancellationToken = default);
    CharacterCreationFoundationResult<CharacterCreationLoadoutPreview> Preview(CharacterCreationLoadoutPreviewRequest request, CancellationToken cancellationToken = default);
    CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> Confirm(CharacterCreationLoadoutConfirmRequest request, CancellationToken cancellationToken = default);
    CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> LookupReceipt(CharacterWorkspaceId workspaceId, string idempotencyKey);
}

public interface IOwnerBoundCharacterCreationLoadoutService
{
    CharacterCreationFoundationResult<CharacterCreationLoadoutState> Prepare(OwnerContextStamp owner, CharacterWorkspaceId workspaceId, CancellationToken cancellationToken = default);
    CharacterCreationFoundationResult<CharacterCreationLoadoutPreview> Preview(OwnerContextStamp owner, CharacterCreationLoadoutPreviewRequest request, CancellationToken cancellationToken = default);
    CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> Confirm(OwnerContextStamp owner, CharacterCreationLoadoutConfirmRequest request, CancellationToken cancellationToken = default);
    CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> LookupReceipt(OwnerContextStamp owner, CharacterWorkspaceId workspaceId, string idempotencyKey);
}

/// <summary>Resolves live purchase authority inside the actual owner/workspace commit lease.</summary>
public interface ICharacterCreationLoadoutAtomicCommitCapability
{
    CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> CommitLoadout(OwnerScope owner,
        CharacterCreationLoadoutConfirmRequest request, ICharacterSourceDataResolver resolver, CancellationToken cancellationToken = default);

    // Explicit shared rules dependency. Legacy stores still execute their existing
    // operation; composite preview fails closed there if they cannot supply it.
    CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> CommitLoadout(OwnerScope owner,
        CharacterCreationLoadoutConfirmRequest request, ICharacterSourceDataResolver resolver,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
        => CommitLoadout(owner, request, resolver, cancellationToken);
}
