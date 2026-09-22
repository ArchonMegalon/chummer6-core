using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

public interface ICharacterCreationLifeModuleFinalizationAtomicCommitCapability
{
    CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> CommitLifeModuleFinalization(
        CharacterCreationFoundationFinalizationConfirmRequest request, ICharacterSourceDataResolver resolver,
        ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles);
    CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> CommitLifeModuleFinalization(
        OwnerScope owner, CharacterCreationFoundationFinalizationConfirmRequest request, ICharacterSourceDataResolver resolver,
        ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles);
}
