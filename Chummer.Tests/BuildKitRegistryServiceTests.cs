#nullable enable annotations

using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class BuildKitRegistryServiceTests
{
    [TestMethod]
    public void Default_buildkit_registry_service_returns_built_in_catalog_for_supported_rulesets()
    {
        DefaultBuildKitRegistryService service = new();

        var entries = service.List(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5);

        Assert.IsTrue(entries.Count >= 2);
        Assert.IsTrue(entries.Any(entry => entry.Manifest.BuildKitId == "street-sam-starter"));
        Assert.IsTrue(entries.All(entry => entry.Manifest.Targets.Contains(RulesetDefaults.Sr5)));
    }

    [TestMethod]
    public void Default_buildkit_registry_service_returns_curated_preview_paths_for_sr4()
    {
        DefaultBuildKitRegistryService service = new();

        var entries = service.List(OwnerScope.LocalSingleUser, RulesetDefaults.Sr4);

        Assert.IsTrue(entries.Count >= 2);
        Assert.IsTrue(entries.Any(entry => entry.Manifest.BuildKitId == "sr4-street-sam-bp"));
        Assert.IsTrue(entries.Any(entry => entry.Manifest.BuildKitId == "sr4-matrix-infiltrator-bp"));
        Assert.IsTrue(entries.All(entry => entry.Manifest.Targets.Contains(RulesetDefaults.Sr4)));
        Assert.IsTrue(entries.All(entry => entry.Manifest.RuntimeRequirements.All(requirement => requirement.RulesetId == RulesetDefaults.Sr4)));
    }

    [TestMethod]
    public void Default_buildkit_registry_service_returns_multiple_preview_paths_for_sr6()
    {
        DefaultBuildKitRegistryService service = new();

        var entries = service.List(OwnerScope.LocalSingleUser, RulesetDefaults.Sr6);

        Assert.IsTrue(entries.Count >= 3);
        Assert.IsTrue(entries.Any(entry => entry.Manifest.BuildKitId == "edge-runner-starter"));
        Assert.IsTrue(entries.Any(entry => entry.Manifest.BuildKitId == "shadow-face-starter"));
        Assert.IsTrue(entries.Any(entry => entry.Manifest.BuildKitId == "arcane-scout-starter"));
        Assert.IsTrue(entries.All(entry => entry.Manifest.Targets.Contains(RulesetDefaults.Sr6)));
    }

    [TestMethod]
    public void Default_buildkit_registry_service_returns_known_buildkit_for_requested_ruleset()
    {
        DefaultBuildKitRegistryService service = new();

        var entry = service.Get(OwnerScope.LocalSingleUser, "street-sam-starter", RulesetDefaults.Sr5);

        Assert.IsNotNull(entry);
        Assert.AreEqual("Street Sam Starter", entry.Manifest.Title);
        Assert.AreEqual(BuildKitPublicationStatuses.Published, entry.PublicationStatus);
        Assert.AreEqual("sha256:core", entry.Manifest.RuntimeRequirements[0].RequiredRuntimeFingerprints[0]);
    }

    [TestMethod]
    public void Default_buildkit_registry_service_returns_null_for_unknown_buildkit()
    {
        DefaultBuildKitRegistryService service = new();

        var entry = service.Get(OwnerScope.LocalSingleUser, "missing-buildkit", RulesetDefaults.Sr5);

        Assert.IsNull(entry);
    }
}
