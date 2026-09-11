using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Existing prerequisite mechanics admitted by the original display/preview owner.
/// Retain the complete stamp across waits; never recapture a replacement account
/// for an old preview. The stamp is not a portable authorization token.
/// </summary>
public interface IOwnerBoundCharacterCreationPrerequisiteService
{
    CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationPrerequisiteLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationPrerequisitePreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationPrerequisiteConfirmRequest request);
}
