using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterCreationBootstrapService
{
    CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> Create(
        CharacterCreationBootstrapRequest request);
}
