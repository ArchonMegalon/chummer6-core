#nullable enable annotations

using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class NpcVaultRegistryServiceTests
{
    [TestMethod]
    public void Default_npc_vault_registry_service_returns_seeded_sr5_entries_packs_and_encounters()
    {
        DefaultNpcVaultRegistryService service = new();

        var entries = service.ListEntries(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5);
        var packs = service.ListPacks(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5);
        var encounters = service.ListEncounterPacks(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5);

        Assert.IsGreaterThanOrEqualTo(entries.Count, 2);
        Assert.IsTrue(entries.Any(entry => entry.Manifest.EntryId == "red-samurai"));
        Assert.IsTrue(entries.Any(entry => entry.Manifest.EntryId == "renraku-spider"));
        Assert.IsTrue(packs.Any(pack => pack.Manifest.PackId == "renraku-security"));
        Assert.IsTrue(encounters.Any(pack => pack.Manifest.EncounterPackId == "renraku-checkpoint"));
    }

    [TestMethod]
    public void Default_npc_vault_registry_service_returns_seeded_sr6_entries_packs_and_encounters()
    {
        DefaultNpcVaultRegistryService service = new();

        var entries = service.ListEntries(OwnerScope.LocalSingleUser, RulesetDefaults.Sr6);
        var packs = service.ListPacks(OwnerScope.LocalSingleUser, RulesetDefaults.Sr6);
        var encounters = service.ListEncounterPacks(OwnerScope.LocalSingleUser, RulesetDefaults.Sr6);

        Assert.IsGreaterThanOrEqualTo(entries.Count, 2);
        Assert.IsTrue(entries.Any(entry => entry.Manifest.EntryId == "neon-razor-biker"));
        Assert.IsTrue(entries.Any(entry => entry.Manifest.EntryId == "hex-lantern-mage"));
        Assert.IsTrue(packs.Any(pack => pack.Manifest.PackId == "ancients-hit-squad"));
        Assert.IsTrue(encounters.Any(pack => pack.Manifest.EncounterPackId == "ancients-smash-and-grab"));
    }

    [TestMethod]
    public void Default_npc_vault_registry_service_returns_known_seeded_entry_by_ruleset()
    {
        DefaultNpcVaultRegistryService service = new();

        var entry = service.GetEntry(OwnerScope.LocalSingleUser, "red-samurai", RulesetDefaults.Sr5);

        Assert.IsNotNull(entry);
        Assert.AreEqual("Red Samurai", entry.Manifest.Title);
        Assert.AreEqual("sha256:core", entry.Manifest.RuntimeFingerprint);
    }

    [TestMethod]
    public void Default_npc_vault_registry_service_returns_null_for_unknown_entry_pack_and_encounter()
    {
        DefaultNpcVaultRegistryService service = new();

        Assert.IsNull(service.GetEntry(OwnerScope.LocalSingleUser, "missing-entry", RulesetDefaults.Sr5));
        Assert.IsNull(service.GetPack(OwnerScope.LocalSingleUser, "missing-pack", RulesetDefaults.Sr5));
        Assert.IsNull(service.GetEncounterPack(OwnerScope.LocalSingleUser, "missing-encounter", RulesetDefaults.Sr5));
    }
}
