using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Qualities drafts admitted by the owner of the original display.</summary>
public interface IOwnerBoundCharacterCreationQualitiesService
{
    CharacterCreationFoundationResult<CharacterCreationQualitiesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesConfirmRequest request);
}
