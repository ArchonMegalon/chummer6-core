using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

/// <summary>
/// Looks up an exact persisted draft receipt in the admitted owner's partition.
/// The store owns the read lease and any retained-source recovery; neither the
/// lookup identity nor the returned receipt grants mutation or recovery authority.
/// A supported lookup failure must not fall back to an ordinary workspace read.
/// </summary>
public interface ICharacterCreationCompletionDraftReceiptLookupCapability
{
    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> LookupCompletionDraftReceipt(
        OwnerScope owner, CharacterCreationCompletionDraftReceiptLookup request,
        CancellationToken cancellationToken = default);
}
