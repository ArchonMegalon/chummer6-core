using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Draft-only Priority/Sum-to-Ten Attribute authority. Confirmation advances
/// auxiliary wizard state and never mutates canonical character XML.
/// </summary>
public interface ICharacterCreationAttributesService
{
    CharacterCreationFoundationResult<CharacterCreationAttributesState> Load(
        CharacterCreationAttributesLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Preview(
        CharacterCreationAttributesPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> Confirm(
        CharacterCreationAttributesConfirmRequest request);
}
