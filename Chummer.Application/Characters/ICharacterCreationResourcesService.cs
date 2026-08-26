using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Typed SR5 creation-resource allocation boundary. It records a finalization
/// contribution; it never grants generic character-XML write access.
/// </summary>
public interface ICharacterCreationResourcesService
{
    CharacterCreationResourcesResult<CharacterCreationResourcesState> Load(
        CharacterCreationResourcesLoadRequest request);

    CharacterCreationResourcesResult<CharacterCreationResourcesPreview> Preview(
        CharacterCreationResourcesPreviewRequest request);

    CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> Confirm(
        CharacterCreationResourcesConfirmRequest request);

    CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> LookupReceipt(
        CharacterCreationResourcesReceiptLookupRequest request);
}
