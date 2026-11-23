using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Canonical preparation and materialization only. The caller authenticates owner/workspace,
/// holds the existing workspace lease across materialization and one durable replacement,
/// and atomically persists the returned XML including its operation receipt.
/// </summary>
public interface ICharacterAugmentationPurchaseAuthority
{
    CharacterAugmentationPurchasePreparation PrepareAugmentation(
        string characterXml, long contentRevision, CharacterAugmentationKind kind);

    CharacterAugmentationPurchaseQuote QuoteAugmentation(
        CharacterAugmentationPurchasePreparation preparation, CharacterAugmentationPurchaseSelection selection);

    CharacterAugmentationPurchaseResult MaterializeAugmentation(
        string characterXml, long currentContentRevision, CharacterAugmentationPurchaseCommand command);

    CharacterAugmentationPurchaseResult LookupAugmentation(
        string characterXml, CharacterAugmentationPurchaseCommand command);

    CharacterAugmentationPurchaseResult LookupAugmentation(
        string characterXml, CharacterAugmentationPurchaseBinding binding);
}
