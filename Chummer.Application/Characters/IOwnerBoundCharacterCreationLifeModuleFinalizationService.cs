using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Use the original display-owner stamp through final allocation,
/// review and confirmation. This does not grant a generic character writer.</summary>
public interface IOwnerBoundCharacterCreationLifeModuleFinalizationService
{
    // Advisory projection only. Implementations must freshly admit the same
    // owner/workspace/source binding used by Load/Preview/Confirm.
    CharacterCreationLifeModuleCatalogProjection? LoadSuggestionCatalog(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationState expectedFoundation,
        string? expectedEnvironmentDigest = null)
        => null;

    CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId);
    CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationFinalizationPreviewRequest request);
    CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationFinalizationConfirmRequest request);

    CurrentLifeModuleFinalizationReview? Review(OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationPreviewRequest request) => null;
    CurrentLifeModuleFinalizationResult Confirm(OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request, CurrentLifeModuleFinalizationReview review)
        => new(CurrentLifeModuleFinalizationDisposition.Refused);
    CurrentLifeModuleFinalizationResult LookupOriginal(OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request, CurrentLifeModuleFinalizationReview review)
        => new(CurrentLifeModuleFinalizationDisposition.Refused);
}
