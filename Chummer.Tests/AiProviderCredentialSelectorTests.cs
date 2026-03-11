#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Infrastructure.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiProviderCredentialSelectorTests
{
    private static readonly string[] MagicxPrimaryArray = ["magicx-primary"];
    private static readonly string[] MagicxFallbackArray = ["magicx-fallback"];
    private static readonly string[] OneMinPrimaryArray = ["one-primary-a", "one-primary-b"];
    private static readonly string[] OneMinFallbackArray = ["one-fallback-a"];
    private static readonly string[] OneMinTrimmedPrimaryArray = ["one-primary-a", "one-primary-b"];

    [TestMethod]
    public void Environment_credential_catalog_parses_primary_and_fallback_sets()
    {
        string? originalAiMagicxPrimary = Environment.GetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.AiMagicxPrimaryApiKeyEnvironmentVariable);
        string? originalAiMagicxFallback = Environment.GetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.AiMagicxFallbackApiKeyEnvironmentVariable);
        string? originalOneMinPrimary = Environment.GetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.OneMinAiPrimaryApiKeyEnvironmentVariable);
        string? originalOneMinFallback = Environment.GetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.OneMinAiFallbackApiKeyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.AiMagicxPrimaryApiKeyEnvironmentVariable, "magicx-primary");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.AiMagicxFallbackApiKeyEnvironmentVariable, "magicx-fallback");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.OneMinAiPrimaryApiKeyEnvironmentVariable, "one-primary-a,one-primary-b");
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.OneMinAiFallbackApiKeyEnvironmentVariable, "one-fallback-a");

            EnvironmentAiProviderCredentialCatalog catalog = new();
            IReadOnlyDictionary<string, AiProviderCredentialSet> configuredSets = catalog.GetConfiguredCredentialSets();
            IReadOnlyDictionary<string, AiProviderCredentialCounts> configuredCounts = catalog.GetConfiguredCredentialCounts();

            CollectionAssert.AreEqual(MagicxPrimaryArray, configuredSets[AiProviderIds.AiMagicx].PrimaryCredentials.ToArray());
            CollectionAssert.AreEqual(MagicxFallbackArray, configuredSets[AiProviderIds.AiMagicx].FallbackCredentials.ToArray());
            CollectionAssert.AreEqual(OneMinPrimaryArray, configuredSets[AiProviderIds.OneMinAi].PrimaryCredentials.ToArray());
            CollectionAssert.AreEqual(OneMinFallbackArray, configuredSets[AiProviderIds.OneMinAi].FallbackCredentials.ToArray());
            Assert.AreEqual(2, configuredCounts[AiProviderIds.OneMinAi].PrimaryCredentialCount);
            Assert.AreEqual(1, configuredCounts[AiProviderIds.OneMinAi].FallbackCredentialCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.AiMagicxPrimaryApiKeyEnvironmentVariable, originalAiMagicxPrimary);
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.AiMagicxFallbackApiKeyEnvironmentVariable, originalAiMagicxFallback);
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.OneMinAiPrimaryApiKeyEnvironmentVariable, originalOneMinPrimary);
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.OneMinAiFallbackApiKeyEnvironmentVariable, originalOneMinFallback);
        }
    }

    [TestMethod]
    public void Environment_credential_catalog_trims_quotes_and_trailing_marker_characters()
    {
        string? originalOneMinPrimary = Environment.GetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.OneMinAiPrimaryApiKeyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                EnvironmentAiProviderCredentialCatalog.OneMinAiPrimaryApiKeyEnvironmentVariable,
                " \"one-primary-a*\" , 'one-primary-b' ");

            EnvironmentAiProviderCredentialCatalog catalog = new();
            IReadOnlyDictionary<string, AiProviderCredentialSet> configuredSets = catalog.GetConfiguredCredentialSets();

            CollectionAssert.AreEqual(
                OneMinTrimmedPrimaryArray,
                configuredSets[AiProviderIds.OneMinAi].PrimaryCredentials.ToArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentAiProviderCredentialCatalog.OneMinAiPrimaryApiKeyEnvironmentVariable, originalOneMinPrimary);
        }
    }

    [TestMethod]
    public void Round_robin_selector_rotates_across_primary_keys_before_reusing_slots()
    {
        RoundRobinAiProviderCredentialSelector selector = new(new InMemoryAiProviderCredentialCatalog(
            new Dictionary<string, AiProviderCredentialSet>(StringComparer.Ordinal)
            {
                [AiProviderIds.OneMinAi] = new(
                    PrimaryCredentials: ["one-primary-a", "one-primary-b"],
                    FallbackCredentials: ["one-fallback-a"])
            }));

        AiProviderCredentialSelection? first = selector.SelectCredential(AiProviderIds.OneMinAi);
        AiProviderCredentialSelection? second = selector.SelectCredential(AiProviderIds.OneMinAi);
        AiProviderCredentialSelection? third = selector.SelectCredential(AiProviderIds.OneMinAi);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsNotNull(third);
        Assert.AreEqual(AiProviderCredentialTiers.Primary, first.CredentialTier);
        Assert.AreEqual(0, first.SlotIndex);
        Assert.AreEqual("one-primary-a", first.ApiKey);
        Assert.AreEqual(AiProviderCredentialTiers.Primary, second.CredentialTier);
        Assert.AreEqual(1, second.SlotIndex);
        Assert.AreEqual("one-primary-b", second.ApiKey);
        Assert.AreEqual(AiProviderCredentialTiers.Primary, third.CredentialTier);
        Assert.AreEqual(0, third.SlotIndex);
        Assert.AreEqual("one-primary-a", third.ApiKey);
    }

    [TestMethod]
    public void Round_robin_selector_uses_fallback_keys_when_no_primary_keys_exist()
    {
        RoundRobinAiProviderCredentialSelector selector = new(new InMemoryAiProviderCredentialCatalog(
            new Dictionary<string, AiProviderCredentialSet>(StringComparer.Ordinal)
            {
                [AiProviderIds.AiMagicx] = new(
                    PrimaryCredentials: Array.Empty<string>(),
                    FallbackCredentials: ["magicx-fallback-a", "magicx-fallback-b"])
            }));

        AiProviderCredentialSelection? first = selector.SelectCredential(AiProviderIds.AiMagicx);
        AiProviderCredentialSelection? second = selector.SelectCredential(AiProviderIds.AiMagicx);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(AiProviderCredentialTiers.Fallback, first.CredentialTier);
        Assert.AreEqual(0, first.SlotIndex);
        Assert.AreEqual("magicx-fallback-a", first.ApiKey);
        Assert.AreEqual(AiProviderCredentialTiers.Fallback, second.CredentialTier);
        Assert.AreEqual(1, second.SlotIndex);
        Assert.AreEqual("magicx-fallback-b", second.ApiKey);
    }

    private sealed class InMemoryAiProviderCredentialCatalog(
        IReadOnlyDictionary<string, AiProviderCredentialSet> providerCredentials)
        : IAiProviderCredentialCatalog
    {
        public IReadOnlyDictionary<string, AiProviderCredentialCounts> GetConfiguredCredentialCounts()
            => providerCredentials.ToDictionary(
                static pair => pair.Key,
                static pair => new AiProviderCredentialCounts(
                    PrimaryCredentialCount: pair.Value.PrimaryCredentials.Count,
                    FallbackCredentialCount: pair.Value.FallbackCredentials.Count),
                StringComparer.Ordinal);

        public IReadOnlyDictionary<string, AiProviderCredentialSet> GetConfiguredCredentialSets()
            => providerCredentials;
    }
}
