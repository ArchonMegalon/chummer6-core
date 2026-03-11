using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterMetadataCommands
{
    UpdateCharacterMetadataResult UpdateMetadata(UpdateCharacterMetadataCommand command);
}
