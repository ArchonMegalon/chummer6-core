#nullable enable annotations

using System;
using System.IO;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiConversationStoreTests
{
    [TestMethod]
    public void File_conversation_store_roundtrips_owner_scoped_snapshots()
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            FileAiConversationStore store = new(stateDirectory);
            AiConversationSnapshot snapshot = CreateSnapshot("conv-roundtrip", AiRouteTypes.Coach, "sha256:roundtrip", "char-1", "ws-1");

            store.Upsert(OwnerScope.LocalSingleUser, snapshot);

            AiConversationCatalogPage page = store.List(OwnerScope.LocalSingleUser, new AiConversationCatalogQuery());
            Assert.AreEqual(1, page.TotalCount);
            Assert.HasCount(1, page.Items);
            AiConversationSnapshot? stored = store.Get(OwnerScope.LocalSingleUser, "conv-roundtrip");
            Assert.IsNotNull(stored);
            Assert.AreEqual(AiRouteTypes.Coach, stored.RouteType);
            Assert.AreEqual("sha256:roundtrip", stored.RuntimeFingerprint);
            Assert.AreEqual("char-1", stored.CharacterId);
            Assert.AreEqual("ws-1", stored.WorkspaceId);
            Assert.HasCount(2, stored.Messages);
            Assert.AreEqual(1, stored.Turns?.Count);
            Assert.AreEqual(AiProviderIds.AiMagicx, stored.Turns?[0].ProviderId);
            Assert.AreEqual(AiToolIds.ExplainDerivedValue, stored.Turns?[0].ToolInvocations[0].ToolId);
            Assert.AreEqual("ws-1", stored.Turns?[0].WorkspaceId);
            Assert.AreEqual(AiSuggestedActionIds.OpenRuntimeInspector, stored.Turns?[0].SuggestedActions?[0].ActionId);
            Assert.AreEqual("Hold up, chummer. Here's the replay-safe line.", stored.Turns?[0].FlavorLine);
            Assert.AreEqual(7, stored.Turns?[0].Budget?.CurrentBurstConsumed);
            Assert.IsNotNull(stored.Turns?[0].RouteDecision);
            Assert.AreEqual(AiProviderIds.AiMagicx, stored.Turns?[0].RouteDecision?.ProviderId);
            Assert.AreEqual(AiProviderCredentialTiers.Primary, stored.Turns?[0].RouteDecision?.CredentialTier);
            Assert.IsNotNull(stored.Turns?[0].GroundingCoverage);
            Assert.AreEqual(100, stored.Turns?[0].GroundingCoverage?.ScorePercent);
        }
        finally
        {
            DeleteStateDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_conversation_store_isolates_different_owners()
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            FileAiConversationStore store = new(stateDirectory);
            OwnerScope ownerA = OwnerScope.LocalSingleUser;
            OwnerScope ownerB = new("other-owner");

            store.Upsert(ownerA, CreateSnapshot("conv-shared", AiRouteTypes.Build, "sha256:a", "char-a", "ws-a"));

            Assert.HasCount(1, store.List(ownerA, new AiConversationCatalogQuery()).Items);
            Assert.HasCount(0, store.List(ownerB, new AiConversationCatalogQuery()).Items);
            Assert.IsNotNull(store.Get(ownerA, "conv-shared"));
            Assert.IsNull(store.Get(ownerB, "conv-shared"));
        }
        finally
        {
            DeleteStateDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_conversation_store_filters_catalog_by_route_character_and_runtime()
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            FileAiConversationStore store = new(stateDirectory);

            store.Upsert(OwnerScope.LocalSingleUser, CreateSnapshot("conv-coach", AiRouteTypes.Coach, "sha256:coach", "char-1", "ws-coach"));
            store.Upsert(OwnerScope.LocalSingleUser, CreateSnapshot("conv-build", AiRouteTypes.Build, "sha256:build", "char-2", "ws-build"));

            AiConversationCatalogPage filtered = store.List(
                OwnerScope.LocalSingleUser,
                new AiConversationCatalogQuery(
                    ConversationId: "conv-coach",
                    RouteType: AiRouteTypes.Coach,
                    CharacterId: "char-1",
                    RuntimeFingerprint: "sha256:coach",
                    MaxCount: 5,
                    WorkspaceId: "ws-coach"));

            Assert.AreEqual(1, filtered.TotalCount);
            Assert.HasCount(1, filtered.Items);
            Assert.AreEqual("conv-coach", filtered.Items[0].ConversationId);
        }
        finally
        {
            DeleteStateDirectory(stateDirectory);
        }
    }

    private static AiConversationSnapshot CreateSnapshot(string conversationId, string routeType, string runtimeFingerprint, string characterId, string workspaceId)
        => new(
            ConversationId: conversationId,
            RouteType: routeType,
            Messages:
            [
                new AiConversationMessage("system-1", AiConversationRoles.System, "Structured Chummer data first.", DateTimeOffset.UtcNow, AiProviderIds.AiMagicx),
                new AiConversationMessage("user-2", AiConversationRoles.User, "What should I spend 18 Karma on?", DateTimeOffset.UtcNow, AiProviderIds.AiMagicx)
            ],
            RuntimeFingerprint: runtimeFingerprint,
            CharacterId: characterId,
            WorkspaceId: workspaceId,
            Turns:
            [
                new AiConversationTurnRecord(
                    TurnId: "turn-1",
                    RouteType: routeType,
                    ProviderId: AiProviderIds.AiMagicx,
                    CreatedAtUtc: DateTimeOffset.UtcNow,
                    UserMessage: "What should I spend 18 Karma on?",
                    AssistantAnswer: "Tighten initiative and perception first.",
                    ToolInvocations:
                    [
                        new AiToolInvocation(AiToolIds.ExplainDerivedValue, AiToolInvocationStatuses.Prepared, "Prepared initiative explanation", "initiative")
                    ],
                    Citations:
                    [
                        new AiCitation(AiCitationKinds.Runtime, "Runtime lock", runtimeFingerprint, "runtime")
                    ],
                    RuntimeFingerprint: runtimeFingerprint,
                    CharacterId: characterId,
                    WorkspaceId: workspaceId,
                    SuggestedActions:
                    [
                        new AiSuggestedAction(AiSuggestedActionIds.OpenRuntimeInspector, "Open Runtime Inspector", "Review the active runtime summary.", RuntimeFingerprint: runtimeFingerprint, CharacterId: characterId, WorkspaceId: workspaceId)
                    ],
                    FlavorLine: "Hold up, chummer. Here's the replay-safe line.",
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 38, 12, 7),
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: routeType,
                        ProviderId: AiProviderIds.AiMagicx,
                        Reason: "Stored route decision.",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        ToolingEnabled: true,
                        CredentialTier: AiProviderCredentialTiers.Primary,
                        CredentialSlotIndex: 0),
                    GroundingCoverage: new AiGroundingCoverage(
                        ScorePercent: 100,
                        Summary: "coverage 100: runtime, character, constraints, and retrieved evidence present.",
                        PresentSignals: ["runtime", "character", "constraints", "retrieved"],
                        MissingSignals: [],
                        RetrievedCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community]))
            ]);

    private static string CreateStateDirectory()
        => Path.Combine(Path.GetTempPath(), "chummer-ai-store-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteStateDirectory(string stateDirectory)
    {
        if (Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }
}
