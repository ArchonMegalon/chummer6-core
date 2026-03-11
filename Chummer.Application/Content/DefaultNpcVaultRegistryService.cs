using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public sealed class DefaultNpcVaultRegistryService : INpcVaultRegistryService
{
    public IReadOnlyList<NpcEntryRegistryEntry> ListEntries(OwnerScope owner, string? rulesetId = null) => [];

    public NpcEntryRegistryEntry? GetEntry(OwnerScope owner, string entryId, string? rulesetId = null) => null;

    public IReadOnlyList<NpcPackRegistryEntry> ListPacks(OwnerScope owner, string? rulesetId = null) => [];

    public NpcPackRegistryEntry? GetPack(OwnerScope owner, string packId, string? rulesetId = null) => null;

    public IReadOnlyList<EncounterPackRegistryEntry> ListEncounterPacks(OwnerScope owner, string? rulesetId = null) => [];

    public EncounterPackRegistryEntry? GetEncounterPack(OwnerScope owner, string encounterPackId, string? rulesetId = null) => null;
}
