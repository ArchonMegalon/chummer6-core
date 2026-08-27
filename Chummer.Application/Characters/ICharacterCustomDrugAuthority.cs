using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Exact recipe-definition authority for the SR5 custom-drug designer. The
/// recipe mutation is separate from later Career quantity purchases so the
/// pinned Chummer5 free initial dose cannot accidentally create an expense.
/// </summary>
public interface ICharacterCustomDrugAuthority
{
    CharacterCustomDrugPreparation Prepare(
        string characterXml,
        long contentRevision,
        CharacterCustomDrugContext context);

    CharacterCustomDrugQuote Quote(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection);

    CharacterCustomDrugCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugCommitCommand command);

    CharacterCustomDrugCommitResult LookupReceipt(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugCommitCommand command);

    CharacterCustomDrugCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterCustomDrugContext context,
        CharacterCustomDrugUndoCommand command);
}
