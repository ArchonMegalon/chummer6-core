using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Full creation-mode lifestyle mutation boundary. Presentation sends stable catalog
/// option ids and typed intent; Core alone prices, validates, plans, and persists.
/// </summary>
public interface ICharacterCreationLifestylesService
{
    CharacterCreationLifestyleResult<CharacterCreationLifestylesState> Load(
        CharacterCreationLifestylesLoadRequest request);

    CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> Preview(
        CharacterCreationLifestylePreviewRequest request);

    CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> Confirm(
        CharacterCreationLifestyleConfirmRequest request);

    CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> LookupReceipt(
        CharacterCreationLifestyleReceiptLookupRequest request);
}
