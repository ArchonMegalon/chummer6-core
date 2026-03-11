#nullable enable annotations

using System;
using System.IO;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiProviderHealthStoreTests
{
    [TestMethod]
    public void File_provider_health_store_roundtrips_success_and_failure_state()
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            FileAiProviderHealthStore store = new(stateDirectory);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            store.RecordFailure(AiProviderIds.AiMagicx, "rate limited", now.AddMinutes(-2), AiRouteTypes.Coach, AiProviderCredentialTiers.Primary, 0);
            store.RecordFailure(AiProviderIds.AiMagicx, "rate limited", now.AddMinutes(-1), AiRouteTypes.Coach, AiProviderCredentialTiers.Primary, 0);
            store.RecordSuccess(AiProviderIds.AiMagicx, now, AiRouteTypes.Coach, AiProviderCredentialTiers.Fallback, 1);

            AiProviderHealthSnapshot snapshot = store.Get(AiProviderIds.AiMagicx);

            Assert.AreEqual(AiProviderIds.AiMagicx, snapshot.ProviderId);
            Assert.AreEqual(0, snapshot.ConsecutiveFailureCount);
            Assert.AreEqual(AiProviderCircuitStates.Closed, snapshot.CircuitState);
            Assert.AreEqual(now, snapshot.LastSuccessAtUtc);
            Assert.AreEqual(AiRouteTypes.Coach, snapshot.LastRouteType);
            Assert.AreEqual(AiProviderCredentialTiers.Fallback, snapshot.LastCredentialTier);
            Assert.AreEqual(1, snapshot.LastCredentialSlotIndex);
        }
        finally
        {
            DeleteStateDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_provider_health_store_opens_circuit_after_three_failures()
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            FileAiProviderHealthStore store = new(stateDirectory);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            store.RecordFailure(AiProviderIds.OneMinAi, "timeout", now.AddMinutes(-3), AiRouteTypes.Docs, AiProviderCredentialTiers.None, null);
            store.RecordFailure(AiProviderIds.OneMinAi, "timeout", now.AddMinutes(-2), AiRouteTypes.Docs, AiProviderCredentialTiers.None, null);
            store.RecordFailure(AiProviderIds.OneMinAi, "timeout", now.AddMinutes(-1), AiRouteTypes.Docs, AiProviderCredentialTiers.None, null);

            AiProviderHealthSnapshot snapshot = store.Get(AiProviderIds.OneMinAi);

            Assert.AreEqual(3, snapshot.ConsecutiveFailureCount);
            Assert.AreEqual(AiProviderCircuitStates.Open, snapshot.CircuitState);
            Assert.AreEqual("timeout", snapshot.LastFailureMessage);
            Assert.AreEqual(now.AddMinutes(-1), snapshot.LastFailureAtUtc);
            Assert.AreEqual(AiRouteTypes.Docs, snapshot.LastRouteType);
            Assert.AreEqual(AiProviderCredentialTiers.None, snapshot.LastCredentialTier);
            Assert.IsNull(snapshot.LastCredentialSlotIndex);
        }
        finally
        {
            DeleteStateDirectory(stateDirectory);
        }
    }

    private static string CreateStateDirectory()
        => Path.Combine(Path.GetTempPath(), "chummer-ai-provider-health-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteStateDirectory(string stateDirectory)
    {
        if (Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }
}
