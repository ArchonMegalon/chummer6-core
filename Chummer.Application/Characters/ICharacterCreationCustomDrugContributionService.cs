using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Governed Creation-only queue. It persists one exact custom-drug contribution
/// in workspace auxiliary state and never calls the Career mutation command.
/// </summary>
public interface ICharacterCreationCustomDrugContributionService
{
    CharacterCreationCustomDrugResult Load(CharacterCreationCustomDrugLoadRequest request);

    CharacterCreationCustomDrugResult Queue(CharacterCreationCustomDrugQueueRequest request);
}
