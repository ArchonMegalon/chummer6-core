using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationDraftValidationTests
{
    [TestMethod]
    public void Canonical_confirmed_Foundation_draft_is_validated_without_store_rereads_or_effect_reapplication()
    {
        using FoundationContext context = new();
        WorkspaceStoredDocument workspace = context.Store.Get(context.Id).Value!;
        var draft = workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
        Assert.IsNotNull(draft);
        Assert.IsTrue(draft.ProjectedEffects.Count > 0);
        Assert.IsTrue(draft.RequirementEvaluations.Count > 0);
        Assert.IsTrue(draft.RequirementEvaluations.All(requirement => requirement.IsMet));
        Assert.IsFalse(draft.CharacterEffectsApplied);
        Assert.AreEqual(context.OriginalXml, workspace.Document.Content);
        Assert.AreEqual(2L, workspace.ContentRevision);
        Assert.AreEqual(workspace.ContentRevision, workspace.SavedRevision);
        string before = JsonSerializer.Serialize(workspace);
        byte[] durableBefore = File.ReadAllBytes(context.RecordPath);
        ForbiddenStore store = new();
        var service = context.Validator(store);

        var blockers = service.ValidateContinuationDraft(workspace);

        Assert.IsEmpty(blockers, string.Join(",", blockers));
        Assert.AreEqual(0, store.Calls);
        Assert.AreEqual(before, JsonSerializer.Serialize(workspace));
        CollectionAssert.AreEqual(durableBefore, File.ReadAllBytes(context.RecordPath));
    }

    [TestMethod]
    public void Rehashed_Foundation_effects_are_rejected_even_when_the_pending_draft_integrity_check_passes()
    {
        using FoundationContext context = new();
        WorkspaceStoredDocument original = context.Store.Get(context.Id).Value!;
        var originalDraft = original.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
        Assert.IsTrue(originalDraft.ProjectedEffects.Count > 0);
        ForbiddenStore store = new();
        var validator = context.Validator(store);
        byte[] durableBefore = File.ReadAllBytes(context.RecordPath);

        var redirected = originalDraft.ProjectedEffects.ToArray();
        redirected[0] = redirected[0] with { TargetId = redirected[0].TargetId + "-forged-grant" };
        CharacterCreationFoundationDraftLedger[] forgeries =
        [
            originalDraft with { ProjectedEffects = redirected },
            originalDraft with { ProjectedEffects = originalDraft.ProjectedEffects.Skip(1).ToArray() }
        ];
        foreach (var unsigned in forgeries)
        {
            var forgedDraft = unsigned with
            {
                DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(unsigned)
            };
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.IsValidPending(
                forgedDraft, original.Id, original.ContentRevision,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(original.Document.Content),
                originalDraft.SourceDigest), "The attack must pass the old shape/hash check.");
            var forged = WithAuxiliary(original, original.Document.AuxiliaryState with
            {
                CharacterCreationFoundationDraft = forgedDraft
            });
            var blockers = validator.ValidateContinuationDraft(forged);
            CollectionAssert.Contains(blockers.ToList(), CharacterCreationFoundationBlockers.PendingDraftInvalid);
        }
        Assert.AreEqual(0, store.Calls);
        CollectionAssert.AreEqual(durableBefore, File.ReadAllBytes(context.RecordPath));
    }

    [TestMethod]
    public void Real_Resources_grant_and_later_nonempty_Gear_costs_validate_without_rereading_the_store()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: false, includeNonEmptyPurchases: true);
        WorkspaceStoredDocument beforeGear = context.Store.Get(context.WorkspaceId).Value!;
        var originalDraft = beforeGear.Document.AuxiliaryState.CharacterCreationResourcesDraft!;
        Assert.IsNotNull(originalDraft);
        Assert.IsTrue(originalDraft.Budget.TotalStartingNuyen > 0m);
        Assert.AreEqual(0m, originalDraft.Budget.KnownPurchaseCost);
        Assert.HasCount(1, beforeGear.Document.AuxiliaryState.CharacterCreationResourcesReceipts!);
        ForbiddenStore store = new();
        var validator = new CharacterCreationResourcesService(store, context.Resolver);
        var initialBlockers = validator.ValidateContinuationDraft(beforeGear);
        Assert.IsEmpty(initialBlockers, string.Join(",", initialBlockers));

        // Use an actual catalog option and the governed Gear confirmation after
        // Resources. Its new basket must not rewrite Resources' historical cost.
        var gear = new CharacterCreationGearService(context.Store, context.Resolver);
        var loaded = gear.Load(new(context.WorkspaceId));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        var selected = loaded.Value.Authority.Options
            .Where(option => option.IsSelectable && option.PackageQuantity == 1
                && option.PackageCost > 0m && option.PackageCost <= loaded.Value.Budget.TotalStartingNuyen)
            .OrderBy(option => option.PackageCost)
            .ThenBy(option => option.OptionId, StringComparer.Ordinal)
            .First();
        CharacterCreationGearSelection[] basket = [new(selected.OptionId, Quantity: 1)];
        var preview = gear.Preview(new(loaded.Value.Binding, basket));
        Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
        var confirmed = gear.Confirm(new(preview.Value.Binding, basket, preview.Value.PreviewDigest,
            "continuation-later-gear", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationGearOutcomes.Applied, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        WorkspaceStoredDocument afterGear = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.IsTrue(afterGear.Document.AuxiliaryState.CharacterCreationGearDraft!.Budget.BasketCost > 0m);
        Assert.AreEqual(JsonSerializer.Serialize(originalDraft),
            JsonSerializer.Serialize(afterGear.Document.AuxiliaryState.CharacterCreationResourcesDraft));
        Assert.AreEqual(beforeGear.Document.Content, afterGear.Document.Content);
        Assert.AreEqual(beforeGear.ContentRevision + 1, afterGear.ContentRevision);
        string beforeValidation = JsonSerializer.Serialize(afterGear);

        var laterBlockers = validator.ValidateContinuationDraft(afterGear);

        Assert.IsEmpty(laterBlockers, string.Join(",", laterBlockers));
        Assert.AreEqual(beforeValidation, JsonSerializer.Serialize(afterGear));
        Assert.AreEqual(0, store.Calls);
    }

    [TestMethod]
    public void Rehashed_Resources_grants_and_finalization_contributions_are_rejected_after_structural_ledger_validation()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: false, includeNonEmptyPurchases: true);
        WorkspaceStoredDocument original = context.Store.Get(context.WorkspaceId).Value!;
        var draft = original.Document.AuxiliaryState.CharacterCreationResourcesDraft!;
        Assert.IsNotNull(draft);
        Assert.HasCount(1, original.Document.AuxiliaryState.CharacterCreationResourcesReceipts!);
        ForbiddenStore store = new();
        var validator = new CharacterCreationResourcesService(store, context.Resolver);
        const decimal unearnedNuyen = 1000m;
        CharacterCreationResourcesDraft[] forgeries =
        [
            draft with
            {
                Budget = draft.Budget with
                {
                    PriorityNuyen = draft.Budget.PriorityNuyen + unearnedNuyen,
                    TotalStartingNuyen = draft.Budget.TotalStartingNuyen + unearnedNuyen,
                    RemainingNuyen = draft.Budget.RemainingNuyen + unearnedNuyen,
                    CarryoverExcess = Math.Max(0m, draft.Budget.RemainingNuyen + unearnedNuyen - draft.Budget.CarryoverLimit)
                },
                FinalizationContribution = draft.FinalizationContribution with
                {
                    StartingNuyen = draft.FinalizationContribution.StartingNuyen + unearnedNuyen
                }
            },
            draft with
            {
                FinalizationContribution = draft.FinalizationContribution with
                {
                    PriorityRank = draft.FinalizationContribution.PriorityRank == "A" ? "B" : "A"
                }
            }
        ];
        string originalState = JsonSerializer.Serialize(original);
        foreach (var unsigned in forgeries)
        {
            WorkspaceStoredDocument forged = RehashResources(original, unsigned);
            Assert.IsTrue(CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
                forged.Id, forged.ContentRevision,
                forged.Document.AuxiliaryState.CharacterCreationResourcesDraft,
                forged.Document.AuxiliaryState.CharacterCreationResourcesReceipts),
                "Rehash the complete draft/contribution/receipt graph so rejection must use source semantics.");
            var blockers = validator.ValidateContinuationDraft(forged);
            CollectionAssert.Contains(blockers.ToList(), CharacterCreationResourcesBlockers.ReceiptLedgerCorrupt);
        }
        Assert.AreEqual(0, store.Calls);
        Assert.AreEqual(originalState, JsonSerializer.Serialize(original));
        Assert.AreEqual(originalState,
            JsonSerializer.Serialize(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value));
    }

    private static WorkspaceStoredDocument RehashResources(WorkspaceStoredDocument original, CharacterCreationResourcesDraft unsigned)
    {
        var contribution = unsigned.FinalizationContribution with
        {
            ContributionDigest = CharacterCreationResourcesRules.ComputeContributionDigest(unsigned.FinalizationContribution)
        };
        var draft = unsigned with { FinalizationContribution = contribution };
        draft = draft with { DraftDigest = CharacterCreationResourcesRules.ComputeDraftDigest(draft) };
        var entry = original.Document.AuxiliaryState.CharacterCreationResourcesReceipts!.Single();
        var receipt = entry.Receipt with
        {
            DraftDigest = draft.DraftDigest,
            TotalStartingNuyen = draft.Budget.TotalStartingNuyen,
            RemainingNuyen = draft.Budget.RemainingNuyen
        };
        receipt = receipt with { ReceiptDigest = CharacterCreationResourcesRules.ComputeReceiptDigest(receipt) };
        return WithAuxiliary(original, original.Document.AuxiliaryState with
        {
            CharacterCreationResourcesDraft = draft,
            CharacterCreationResourcesReceipts = [entry with { Receipt = receipt }]
        });
    }

    private static WorkspaceStoredDocument WithAuxiliary(WorkspaceStoredDocument workspace, WorkspaceDocumentAuxiliaryState auxiliary) =>
        workspace with { Document = workspace.Document with { State = workspace.Document.State with { AuxiliaryState = auxiliary } } };

    private sealed class FoundationContext : IDisposable
    {
        private const string TirModuleId = "83c132b5-fcf5-4a43-b9de-6c8ab206a586";
        private const string TirVersionId = "604831d9-0fdc-4579-aa7e-bc5d99bcee5d";
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"chummer-continuation-foundation-{Guid.NewGuid():N}");
        public CharacterWorkspaceId Id { get; } = new("continuation-foundation");
        public string OriginalXml { get; } = $"<character><name>Foundation Runner</name><alias>Foundation</alias><metatype>Human</metatype><buildmethod>{CharacterCreationBuildMethods.LifeModules}</buildmethod><createdversion>5.225.0</createdversion><appversion>5.225.0</appversion><karma>25</karma><nuyen>0</nuyen><created>False</created><settings>8a31af6d-7137-4284-872b-7d8087e156c6</settings></character>";
        public string RecordPath => Path.Combine(Directory, "workspaces", Id.Value + ".json");
        public FileWorkspaceStore Store { get; }
        private readonly FileSystemCharacterSourceDataResolver _source;
        private readonly XmlLifeModulesCatalogService _catalog;

        public FoundationContext()
        {
            System.IO.Directory.CreateDirectory(Directory);
            try
            {
                string coreRoot = FindCoreRoot();
                var overlays = new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null);
                _source = new(overlays);
                _catalog = new(Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml"));
                Store = new(Directory);
                Assert.IsTrue(Store.CreateWorkspaceDocument(Id, new WorkspaceDocument(OriginalXml, "sr5")).Success);
                var service = Validator(Store);
                var loaded = service.Load(new(Id));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
                Assert.IsNotNull(loaded.Value);
                var nationality = _catalog.GetOptionProjections("Nationality", ["RF"])
                    .Single(option => option.ModuleId == TirModuleId);
                var version = nationality.Versions.Single(option => option.VersionId == TirVersionId);
                var followUps = nationality.FollowUps.Concat(version.FollowUps).ToDictionary(
                    prompt => prompt.PromptId,
                    prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Confirmed",
                    StringComparer.Ordinal);
                var preview = service.Preview(new(loaded.Value.Binding, "Human", new(TirModuleId, TirVersionId), followUps));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
                Assert.IsNotNull(preview.Value);
                Assert.IsTrue(preview.Value.CanConfirm);
                var confirmed = service.Confirm(new(preview.Value.Binding, preview.Value.RequestedMetatype,
                    preview.Value.Selection, preview.Value.PreviewDigest, ExplicitlyConfirmed: true, preview.Value.FollowUpValues));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));
            }
            catch
            {
                System.IO.Directory.Delete(Directory, recursive: true);
                throw;
            }
        }

        public CharacterCreationFoundationService Validator(IWorkspaceStore store) => new(store,
            new XmlCharacterFileQueries(new CharacterFileService()), _source, _catalog,
            new CharacterCreationFoundationDraftApplyAuthority(store));

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static string FindCoreRoot()
    {
        for (DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate canonical Chummer source data.");
    }

    private sealed class ForbiddenStore : IWorkspaceStore
    {
        public int Calls { get; private set; }
        private Exception Unexpected() { Calls++; return new AssertFailedException("Continuation validation must use only the supplied workspace, without store access."); }
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => throw Unexpected();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => throw Unexpected();
        public IReadOnlyList<WorkspaceStoreEntry> List() => throw Unexpected();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => throw Unexpected();
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => throw Unexpected();
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => throw Unexpected();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) => throw Unexpected();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) => throw Unexpected();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision) => throw Unexpected();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) => throw Unexpected();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision) => throw Unexpected();
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) => throw Unexpected();
    }
}
