#nullable enable annotations

using Chummer.Application.Content;
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
