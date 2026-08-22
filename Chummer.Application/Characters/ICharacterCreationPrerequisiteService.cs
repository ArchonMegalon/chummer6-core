using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Draft-only authority for the Priority/Sum-to-Ten prerequisite assignment.
/// It persists wizard state atomically and never writes legacy character XML.
/// </summary>
public interface ICharacterCreationPrerequisiteService
{
    CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> Load(
        CharacterCreationPrerequisiteLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> Preview(
        CharacterCreationPrerequisitePreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> Confirm(
        CharacterCreationPrerequisiteConfirmRequest request);
}
