using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterAfterRunSettlementService
{
    CharacterAfterRunSettlementQuoteResult Quote(
        CharacterAfterRunSettlementQuoteRequest request);

    CharacterAfterRunSettlementResult Settle(
        CharacterAfterRunSettlementCommand command);
}
