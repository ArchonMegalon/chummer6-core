using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Rules-authoritative mutation boundary for the Contacts/Lifestyles creation
/// step. Presentation supplies typed intent; only Core emits and applies a write plan.
/// </summary>
public interface ICharacterCreationContactsService
{
    CharacterCreationContactResult<CharacterCreationContactsState> Load(
        CharacterCreationContactsLoadRequest request);

    CharacterCreationContactResult<CharacterCreationContactPreview> Preview(
        CharacterCreationContactPreviewRequest request);

    CharacterCreationContactResult<CharacterCreationContactReceipt> Confirm(
        CharacterCreationContactConfirmRequest request);

    CharacterCreationContactResult<CharacterCreationContactReceipt> LookupReceipt(
        CharacterCreationContactReceiptLookupRequest request);
}
