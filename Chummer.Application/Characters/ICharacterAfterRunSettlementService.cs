using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Original-command observation only. An absent receipt never authorizes redispatch.</summary>
public interface ICharacterAfterRunSettlementRecoveryService
{
    CharacterAfterRunSettlementResult Lookup(CharacterAfterRunSettlementCommand original);
}

public interface ICharacterAfterRunSettlementService
{
    CharacterAfterRunSettlementQuoteResult Quote(
        CharacterAfterRunSettlementQuoteRequest request);

    CharacterAfterRunSettlementResult Settle(
        CharacterAfterRunSettlementCommand command);
}
