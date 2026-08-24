using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Resolver/list/quote/commit authority for the bounded Career top-level
/// Cyberware purchase lane. Implementations must return the original XML for
/// every blocked commit or undo.
/// </summary>
public interface ICharacterCyberwarePurchaseAuthority
{
    CharacterCyberwarePurchasePreparation Prepare(string characterXml, long contentRevision);

    CharacterCyberwarePurchaseQuote Quote(
        CharacterCyberwarePurchasePreparation preparation,
        CharacterCyberwarePurchaseSelection selection);

    CharacterCyberwarePurchaseCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterCyberwarePurchaseCommand command);

    CharacterCyberwarePurchaseCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterCyberwarePurchaseUndoCommand command);
}
