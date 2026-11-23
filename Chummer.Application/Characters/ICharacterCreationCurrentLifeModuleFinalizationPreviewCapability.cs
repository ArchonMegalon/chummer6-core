using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Only positively validated absence permits an ordinary native read.</summary>
public enum CurrentLifeModulePreviewDisposition
{
    Refused = 0,
    ProvenUnbound,
    Preview
}

/// <summary>
/// Synchronous, owner-bound observation of a retained current Life Modules request.
/// Refusal is terminal. ProvenUnbound requires validated absence of both current
/// and private custody bindings and returns no result. Preview supplies the native
/// preview, including blockers; neither disposition grants mutation authority.
/// </summary>
public interface ICharacterCreationCurrentLifeModuleFinalizationPreviewCapability
{
    CurrentLifeModulePreviewDisposition PreviewCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationPreviewRequest request,
        out CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>? result,
        CancellationToken cancellationToken = default);
}
