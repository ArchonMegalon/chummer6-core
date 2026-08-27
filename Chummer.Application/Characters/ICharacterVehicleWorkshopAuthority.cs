using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Typed SR5 vehicle/drone catalog, quote, CAS commit, recovery, and undo authority.
/// A blocked operation must return the input XML byte-for-byte.
/// </summary>
public interface ICharacterVehicleWorkshopAuthority
{
    CharacterVehicleWorkshopPreparation Prepare(
        string characterXml,
        long contentRevision,
        CharacterVehicleWorkshopCatalog catalog);

    CharacterVehicleWorkshopQuote Quote(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopSelection selection);

    CharacterVehicleWorkshopCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterVehicleWorkshopCatalog catalog,
        CharacterVehicleWorkshopCommitCommand command);

    CharacterVehicleWorkshopCommitResult Recover(
        string characterXml,
        long currentContentRevision,
        CharacterVehicleWorkshopCatalog catalog,
        CharacterVehicleWorkshopCommitCommand command);

    CharacterVehicleWorkshopCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterVehicleWorkshopCatalog catalog,
        CharacterVehicleWorkshopUndoCommand command);
}
