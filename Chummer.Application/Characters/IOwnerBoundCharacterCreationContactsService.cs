using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Owner-bound Contacts reads, previews, atomic confirmation and historical receipt recovery.
/// The caller retains the original display/preview stamp rather than recapturing the current account.
/// </summary>
public interface IOwnerBoundCharacterCreationContactsService
{
    CharacterCreationContactResult<CharacterCreationContactsState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationContactsLoadRequest request);
    CharacterCreationContactResult<CharacterCreationContactPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationContactPreviewRequest request);
    CharacterCreationContactResult<CharacterCreationContactReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationContactConfirmRequest request);
    CharacterCreationContactResult<CharacterCreationContactReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationContactReceiptLookupRequest request);
}
