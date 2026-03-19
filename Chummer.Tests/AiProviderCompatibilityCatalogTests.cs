#nullable enable annotations

using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiProviderCompatibilityCatalogTests
{
    [TestMethod]
    public void Empty_credential_catalog_reports_known_providers_as_unconfigured()
    {
        EmptyAiProviderCredentialCatalog catalog = new();

        Assert.AreEqual(0, catalog.GetConfiguredCredentialCounts()[AiProviderIds.AiMagicx].PrimaryCredentialCount);
        Assert.AreEqual(0, catalog.GetConfiguredCredentialCounts()[AiProviderIds.OneMinAi].FallbackCredentialCount);
        Assert.AreEqual(0, catalog.GetConfiguredCredentialSets()[AiProviderIds.AiMagicx].PrimaryCredentials.Count);
        Assert.AreEqual(0, catalog.GetConfiguredCredentialSets()[AiProviderIds.OneMinAi].FallbackCredentials.Count);
    }

    [TestMethod]
    public void Empty_transport_options_catalog_reports_known_providers_as_not_configured()
    {
        EmptyAiProviderTransportOptionsCatalog catalog = new();

        Assert.IsFalse(catalog.GetConfiguredTransportOptions()[AiProviderIds.AiMagicx].TransportConfigured);
        Assert.IsFalse(catalog.GetConfiguredTransportOptions()[AiProviderIds.AiMagicx].RemoteExecutionEnabled);
        Assert.IsFalse(catalog.GetConfiguredTransportOptions()[AiProviderIds.OneMinAi].TransportConfigured);
        Assert.IsFalse(catalog.GetConfiguredTransportOptions()[AiProviderIds.OneMinAi].RemoteExecutionEnabled);
    }
}
