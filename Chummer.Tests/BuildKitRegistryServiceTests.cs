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
    public void Default_buildkit_registry_service_returns_empty_catalog_until_real_sources_are_registered()
    {
        DefaultBuildKitRegistryService service = new();

        var entries = service.List(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5);

        Assert.IsEmpty(entries);
    }

    [TestMethod]
    public void Default_buildkit_registry_service_returns_null_for_unknown_buildkit()
    {
        DefaultBuildKitRegistryService service = new();

        var entry = service.Get(OwnerScope.LocalSingleUser, "missing-buildkit", RulesetDefaults.Sr5);

        Assert.IsNull(entry);
    }
}
