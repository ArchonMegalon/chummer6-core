using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Resolver/list/quote/commit authority for exact, side-effect-free,
/// top-level Career Bioware purchases. Every blocked operation returns the
/// original character XML and leaves external workspace CAS to its caller.
/// </summary>
public interface ICharacterBiowarePurchaseAuthority
{
    CharacterBiowarePurchasePreparation Prepare(string characterXml, long contentRevision);

    CharacterBiowarePurchaseQuote Quote(
        CharacterBiowarePurchasePreparation preparation,
        CharacterBiowarePurchaseSelection selection);

    CharacterBiowarePurchaseCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterBiowarePurchaseCommand command);

    CharacterBiowarePurchaseCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterBiowarePurchaseUndoCommand command);
}
