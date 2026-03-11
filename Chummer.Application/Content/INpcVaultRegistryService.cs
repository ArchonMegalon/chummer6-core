using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface INpcVaultRegistryService
{
    IReadOnlyList<NpcEntryRegistryEntry> ListEntries(OwnerScope owner, string? rulesetId = null);

    NpcEntryRegistryEntry? GetEntry(OwnerScope owner, string entryId, string? rulesetId = null);

    IReadOnlyList<NpcPackRegistryEntry> ListPacks(OwnerScope owner, string? rulesetId = null);

    NpcPackRegistryEntry? GetPack(OwnerScope owner, string packId, string? rulesetId = null);

    IReadOnlyList<EncounterPackRegistryEntry> ListEncounterPacks(OwnerScope owner, string? rulesetId = null);

    EncounterPackRegistryEntry? GetEncounterPack(OwnerScope owner, string encounterPackId, string? rulesetId = null);
}
