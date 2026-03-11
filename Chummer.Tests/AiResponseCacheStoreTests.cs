#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiResponseCacheStoreTests
{
    [TestMethod]
    public void File_response_cache_store_roundtrips_owner_scoped_cached_turns()
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            FileAiResponseCacheStore store = new(stateDirectory);
            AiResponseCacheLookup lookup = AiResponseCacheKeys.CreateLookup(
                AiRouteTypes.Coach,
                "What should I spend 18 Karma on next?",
                "sha256:runtime-cache",
                "char-7",
                workspaceId: "ws-7");
            string cacheKey = AiResponseCacheKeys.CreateCacheKey(lookup);
            DateTimeOffset cachedAtUtc = DateTimeOffset.UtcNow;
            AiCachedConversationTurn cachedTurn = new(
                CacheKey: cacheKey,
                RouteType: lookup.RouteType,
                NormalizedPrompt: lookup.NormalizedPrompt,
                RuntimeFingerprint: lookup.RuntimeFingerprint,
                CharacterId: lookup.CharacterId,
                AttachmentKey: lookup.AttachmentKey,
                CachedAtUtc: cachedAtUtc,
                Response: new AiConversationTurnResponse(
                    ConversationId: "conv-cache",
                    RouteType: AiRouteTypes.Coach,
                    ProviderId: AiProviderIds.AiMagicx,
                    Answer: "Spend Karma on perception and initiative first.",
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: AiRouteTypes.Coach,
                        ProviderId: AiProviderIds.AiMagicx,
                        Reason: "primary provider",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        CredentialTier: AiProviderCredentialTiers.Primary,
                        CredentialSlotIndex: 0),
                    Grounding: new AiGroundingBundle(
                        RouteType: AiRouteTypes.Coach,
                        RuntimeFingerprint: "sha256:runtime-cache",
                        CharacterId: "char-7",
                        ConversationId: "conv-cache",
                        WorkspaceId: "ws-7",
                        RuntimeFacts: new Dictionary<string, string>(),
                        CharacterFacts: new Dictionary<string, string>(),
                        Constraints: [],
                        RetrievedItems: [],
                        AllowedTools: []),
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 10, 1, 4, 1, true),
                    Citations: [],
                    SuggestedActions: [],
                    ToolInvocations: [],
                    FlavorLine: "Signal's clean.",
                    StructuredAnswer: null,
                    Cache: new AiCacheMetadata(AiCacheStatuses.Miss, cacheKey, cachedAtUtc, lookup.NormalizedPrompt, lookup.RuntimeFingerprint, lookup.CharacterId, lookup.WorkspaceId)),
                WorkspaceId: lookup.WorkspaceId);

            store.Upsert(OwnerScope.LocalSingleUser, cachedTurn);

            AiCachedConversationTurn? stored = store.Get(OwnerScope.LocalSingleUser, lookup);

            Assert.IsNotNull(stored);
            Assert.AreEqual(cacheKey, stored.CacheKey);
            Assert.AreEqual(lookup.NormalizedPrompt, stored.NormalizedPrompt);
            Assert.AreEqual("sha256:runtime-cache", stored.RuntimeFingerprint);
            Assert.AreEqual("char-7", stored.CharacterId);
            Assert.AreEqual("ws-7", stored.WorkspaceId);
            Assert.AreEqual(AiProviderIds.AiMagicx, stored.Response.ProviderId);
            Assert.AreEqual(AiCacheStatuses.Miss, stored.Response.Cache?.Status);
            Assert.AreEqual("ws-7", stored.Response.Cache?.WorkspaceId);
        }
        finally
        {
            DeleteStateDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_response_cache_store_isolates_different_owners()
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            FileAiResponseCacheStore store = new(stateDirectory);
            AiResponseCacheLookup lookup = AiResponseCacheKeys.CreateLookup(AiRouteTypes.Docs, "How do I open the coach?", "sha256:docs-runtime", null);
            AiCachedConversationTurn entry = new(
                CacheKey: AiResponseCacheKeys.CreateCacheKey(lookup),
                RouteType: lookup.RouteType,
                NormalizedPrompt: lookup.NormalizedPrompt,
                RuntimeFingerprint: lookup.RuntimeFingerprint,
                CharacterId: lookup.CharacterId,
                AttachmentKey: lookup.AttachmentKey,
                CachedAtUtc: DateTimeOffset.UtcNow,
                Response: new AiConversationTurnResponse(
                    ConversationId: "conv-docs-cache",
                    RouteType: AiRouteTypes.Docs,
                    ProviderId: AiProviderIds.OneMinAi,
                    Answer: "Open /coach from the launcher.",
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: AiRouteTypes.Docs,
                        ProviderId: AiProviderIds.OneMinAi,
                        Reason: "primary provider",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        CredentialTier: AiProviderCredentialTiers.Primary,
                        CredentialSlotIndex: 0),
                    Grounding: new AiGroundingBundle(
                        RouteType: AiRouteTypes.Docs,
                        RuntimeFingerprint: "sha256:docs-runtime",
                        CharacterId: null,
                        ConversationId: "conv-docs-cache",
                        RuntimeFacts: new Dictionary<string, string>(),
                        CharacterFacts: new Dictionary<string, string>(),
                        Constraints: [],
                        RetrievedItems: [],
                        AllowedTools: []),
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 10, 1, 4, 1, true),
                    Citations: [],
                    SuggestedActions: [],
                    ToolInvocations: []));

            store.Upsert(OwnerScope.LocalSingleUser, entry);

            Assert.IsNotNull(store.Get(OwnerScope.LocalSingleUser, lookup));
            Assert.IsNull(store.Get(new OwnerScope("other-owner"), lookup));
        }
        finally
        {
            DeleteStateDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void Response_cache_keys_include_workspace_scope()
    {
        AiResponseCacheLookup first = AiResponseCacheKeys.CreateLookup(
            AiRouteTypes.Coach,
            "What should I spend 18 Karma on next?",
            "sha256:runtime-cache",
            "char-7",
            workspaceId: "ws-7");
        AiResponseCacheLookup second = AiResponseCacheKeys.CreateLookup(
            AiRouteTypes.Coach,
            "What should I spend 18 Karma on next?",
            "sha256:runtime-cache",
            "char-7",
            workspaceId: "ws-8");

        Assert.AreNotEqual(AiResponseCacheKeys.CreateCacheKey(first), AiResponseCacheKeys.CreateCacheKey(second));
    }

    private static string CreateStateDirectory()
        => Path.Combine(Path.GetTempPath(), "chummer-ai-cache-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteStateDirectory(string stateDirectory)
    {
        if (Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }
}
