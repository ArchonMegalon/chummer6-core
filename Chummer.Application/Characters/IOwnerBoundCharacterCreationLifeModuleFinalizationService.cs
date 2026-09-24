using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Use the original display-owner stamp through final allocation,
/// review and confirmation. This does not grant a generic character writer.</summary>
public interface IOwnerBoundCharacterCreationLifeModuleFinalizationService
{
    CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId);
    CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationFinalizationPreviewRequest request);
    CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationFinalizationConfirmRequest request);
}
