#nullable enable annotations

using System;
using System.Linq;
using Chummer.Contracts.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class HubCatalogItemKindsTests
{
    [TestMethod]
    public void All_contains_each_supported_kind_once()
    {
        string[] expected =
        [
            HubCatalogItemKinds.RulePack,
            HubCatalogItemKinds.RuleProfile,
            HubCatalogItemKinds.BuildKit,
            HubCatalogItemKinds.NpcEntry,
            HubCatalogItemKinds.NpcPack,
            HubCatalogItemKinds.EncounterPack,
            HubCatalogItemKinds.RuntimeLock
        ];

        CollectionAssert.AreEquivalent(expected, HubCatalogItemKinds.All.ToArray());
    }

    [TestMethod]
    public void Normalize_required_accepts_known_kinds_case_insensitively()
    {
        string normalized = HubCatalogItemKinds.NormalizeRequired(" RuleProfile ", "kind");

        Assert.AreEqual(HubCatalogItemKinds.RuleProfile, normalized);
        Assert.IsTrue(HubCatalogItemKinds.IsDefined("RULEPROFILE"));
    }

    [TestMethod]
    public void Normalize_optional_returns_null_for_blank_and_throws_for_unknown_kind()
    {
        Assert.IsNull(HubCatalogItemKinds.NormalizeOptional(" "));

        ArgumentOutOfRangeException ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            HubCatalogItemKinds.NormalizeOptional("unknown-kind"));

        StringAssert.Contains(ex.Message, "Unsupported hub project kind 'unknown-kind'");
    }
}
