using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Attribute allocation admitted by the exact owner stamp of the original
/// display. An old preview must never be rebound to the current account.
/// </summary>
public interface IOwnerBoundCharacterCreationAttributesService
{
    CharacterCreationFoundationResult<CharacterCreationAttributesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesConfirmRequest request);
}
