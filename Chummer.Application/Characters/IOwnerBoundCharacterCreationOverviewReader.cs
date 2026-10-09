using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Reads the existing creation-domain results in one synchronous owner admission.
/// This is a display observation, not a review, mutation grant or portable receipt.
/// A rejected owner returns null; callers must not fall back to an ambient owner.
/// </summary>
public interface IOwnerBoundCharacterCreationOverviewReader
{
    CharacterCreationOverviewRead? LoadOverview(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId, bool includePriorityDrafts);
}

/// <summary>Existing canonical results; no source context or lease escapes the read.</summary>
public sealed record CharacterCreationOverviewRead(
    CharacterCreationContactResult<CharacterCreationContactsState> Contacts,
    CharacterCreationFoundationResult<CharacterCreationQualitiesState>? Qualities,
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceState>? MagicResonance,
    CharacterCreationLifestyleResult<CharacterCreationLifestylesState> Lifestyles,
    CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Finalization);
