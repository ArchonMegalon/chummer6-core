using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceImportedDraftReplayTests
{
    [TestMethod]
    [DataRow("skills", false)]
    [DataRow("skills", true)]
    [DataRow("magic", false)]
    [DataRow("magic", true)]
    [DataRow("qualities", false)]
    [DataRow("qualities", true)]
    public void Real_draft_receipt_cannot_be_reused_from_imported_early_or_recovery_observation(
        string domain, bool duringRecovery)
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true,
            talentValue: "Mystic Adept", mysticPowerPoints: 2);
        ReplayCommand command = Prepare(context, domain);
        long beforeRevision = context.Store.Get(context.WorkspaceId).Value!.ContentRevision;
        void RequireNativeReplay()
        {
            ReplayResult native = command.Confirm(new FileWorkspaceStore(context.Directory));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, native.Outcome,
                string.Join(",", native.Blockers));
            Assert.IsNotNull(native.ReceiptJson);
        }
        var recoveryStore = new ReplayRecoveryStore(context.Store, context.Directory, RequireNativeReplay);

        if (!duringRecovery)
        {
            ReplayResult applied = command.Confirm(context.Store);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome,
                string.Join(",", applied.Blockers));
            Assert.IsNotNull(applied.ReceiptJson);
            RequireNativeReplay();
            WorkspaceImportedHistoryTestFixture.MarkImported(context.Directory, context.WorkspaceId);
        }

        ReplayResult refused = command.Confirm(duringRecovery ? recoveryStore : new FileWorkspaceStore(context.Directory));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, refused.Outcome,
            string.Join(",", refused.Blockers));
        CollectionAssert.Contains(refused.Blockers.ToArray(), command.ConflictBlocker);
        Assert.IsNull(refused.ReceiptJson, "Imported execution history is not a successful local receipt result.");
        Assert.AreEqual(duringRecovery ? 1 : 0, recoveryStore.CommitCount,
            "The recovery case must reach the actual atomic writer; the early case must not write.");

        WorkspaceStoredDocument durable = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.AreEqual(beforeRevision + 1, durable.ContentRevision,
            "The receipt was created by exactly one real production commit, not by a forged fixture ledger.");
        Assert.IsFalse(durable.CanReplayReceipt(durable.ContentRevision));
        Assert.AreEqual(durable.ContentRevision, durable.LocalHistory!.ImportedThroughRevision);
        string durableJson = JsonSerializer.Serialize(durable);

        ReplayResult restarted = command.Confirm(new FileWorkspaceStore(context.Directory));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, restarted.Outcome);
        Assert.IsNull(restarted.ReceiptJson);
        Assert.AreEqual(durableJson, JsonSerializer.Serialize(
            new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value));

        // Perform another actual rules-authoritative edit with a fresh key.
        // The imported prefix remains intact, while the new local receipt can
        // be replayed after reopening the durable store.
        ReplayCommand laterCommand = Prepare(context, domain);
        ReplayResult localCommit = laterCommand.Confirm(context.Store);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, localCommit.Outcome,
            string.Join(",", localCommit.Blockers));
        ReplayResult laterLocalReplay = laterCommand.Confirm(new FileWorkspaceStore(context.Directory));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, laterLocalReplay.Outcome,
            string.Join(",", laterLocalReplay.Blockers));
        Assert.AreEqual(localCommit.ReceiptJson, laterLocalReplay.ReceiptJson);
        WorkspaceStoredDocument later = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.AreEqual(durable.ContentRevision + 1, later.ContentRevision);
        Assert.AreEqual(durable.LocalHistory, later.LocalHistory);
        Assert.IsTrue(later.CanReplayReceipt(later.ContentRevision));
        Assert.IsFalse(later.CanReplayReceipt(durable.ContentRevision));
        string ledgerName = domain switch
        {
            "skills" => "CharacterCreationSkillsReceipts",
            "magic" => "CharacterCreationMagicResonanceReceipts",
            _ => "CharacterCreationQualitiesReceipts"
        };
        JsonElement priorLedger = JsonSerializer.SerializeToElement(durable.Document.AuxiliaryState).GetProperty(ledgerName);
        JsonElement laterLedger = JsonSerializer.SerializeToElement(later.Document.AuxiliaryState).GetProperty(ledgerName);
        Assert.AreEqual(priorLedger.GetArrayLength() + 1, laterLedger.GetArrayLength());
        Assert.AreEqual(priorLedger.GetRawText(), JsonSerializer.Serialize(
            laterLedger.EnumerateArray().Take(priorLedger.GetArrayLength()).ToArray()));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            command.Confirm(new FileWorkspaceStore(context.Directory)).Outcome);
    }

    private static ReplayCommand Prepare(ReadyContext context, string domain)
    {
        if (domain == "skills")
        {
            var service = new CharacterCreationSkillsService(context.Store, context.Resolver);
            var loaded = service.Load(new(context.WorkspaceId));
            Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
            var state = loaded.Value;
            var perception = state.Authority.ActiveSkills.Single(option => option.Name == "Perception");
            int rating = (state.PendingDraft!.Allocations.FirstOrDefault(
                item => item.SourceSkillId == perception.SourceSkillId)?.Rating ?? 0) + 1;
            CharacterCreationSkillAllocation[] allocations =
            [
                .. state.PendingDraft.Allocations.Where(item => item.SourceSkillId != perception.SourceSkillId),
                new(perception.SourceSkillId, CharacterCreationSkillKinds.Active, rating, null, false)
            ];
            var preview = service.Preview(new(state.Binding, allocations, state.PendingDraft.GroupAllocations));
            Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
            Assert.IsTrue(preview.Value.CanConfirm, string.Join(",", preview.Blockers));
            var request = new CharacterCreationSkillsConfirmRequest(preview.Value.Binding,
                allocations, state.PendingDraft.GroupAllocations, preview.Value.PreviewDigest,
                $"imported-skills-replay-review-{Guid.NewGuid():N}", ExplicitlyConfirmed: true);
            return new(store => Project(new CharacterCreationSkillsService(store, context.Resolver).Confirm(request)),
                CharacterCreationSkillsBlockers.IdempotencyConflict);
        }
        if (domain == "magic")
        {
            var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
            var loaded = service.Load(new(context.WorkspaceId));
            Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
            var state = loaded.Value;
            var shamanic = state.Authority.Traditions.Single(option => option.IsEnabled && option.Name == "Shamanic");
            var tradition = state.PendingDraft!.Selections.Tradition == shamanic.Identity
                ? state.Authority.Traditions.Single(option => option.IsEnabled && option.Name == "Hermetic")
                : shamanic;
            var selections = state.PendingDraft.Selections with { Tradition = tradition.Identity };
            var preview = service.Preview(new(state.Binding, selections));
            Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
            Assert.IsTrue(preview.Value.CanConfirm, string.Join(",", preview.Blockers));
            var request = new CharacterCreationMagicResonanceConfirmRequest(preview.Value.Binding,
                selections, preview.Value.PreviewDigest, $"imported-magic-replay-review-{Guid.NewGuid():N}", ExplicitlyConfirmed: true);
            return new(store => Project(new CharacterCreationMagicResonanceService(store, context.Resolver).Confirm(request)),
                CharacterCreationMagicResonanceBlockers.IdempotencyConflict);
        }
        Assert.AreEqual("qualities", domain);
        var qualities = Qualities(context, context.Store);
        var qualityLoaded = qualities.Load(new(context.WorkspaceId));
        Assert.IsNotNull(qualityLoaded.Value, string.Join(",", qualityLoaded.Blockers));
        var qualityState = qualityLoaded.Value;
        string[] selected = qualityState.PendingDraft!.SelectedOptionIds.Count > 0 ? [] :
        [
            qualityState.Authority.Options.Where(option => option.IsSelectable && option.KarmaCost is >= 0 and <= 25)
                .OrderBy(option => option.KarmaCost).ThenBy(option => option.OptionId, StringComparer.Ordinal)
                .First().OptionId
        ];
        var qualityPreview = qualities.Preview(new(qualityState.Binding, selected));
        Assert.IsNotNull(qualityPreview.Value, string.Join(",", qualityPreview.Blockers));
        Assert.IsTrue(qualityPreview.Value.CanConfirm, string.Join(",", qualityPreview.Blockers));
        var qualityRequest = new CharacterCreationQualitiesConfirmRequest(qualityPreview.Value.Binding,
            selected, qualityPreview.Value.PreviewDigest, $"imported-qualities-replay-review-{Guid.NewGuid():N}", Guid.NewGuid(),
            ExplicitlyConfirmed: true);
        return new(store => Project(Qualities(context, store).Confirm(qualityRequest)),
            CharacterCreationQualitiesBlockers.IdempotencyConflict);
    }

    private static CharacterCreationQualitiesService Qualities(ReadyContext context, IWorkspaceStore store) => new(
        store, context.Resolver, new CharacterCreationPrerequisiteService(store, context.Queries, context.Resolver),
        new CharacterCreationAttributesService(store, context.Resolver));

    private static ReplayResult Project<T>(CharacterCreationFoundationResult<T> result) where T : class =>
        new(result.Outcome, result.Value is null ? null : JsonSerializer.Serialize(result.Value), result.Blockers);

    private sealed record ReplayResult(string Outcome, string? ReceiptJson, IReadOnlyList<string> Blockers);
    private sealed record ReplayCommand(Func<IWorkspaceStore, ReplayResult> Confirm, string ConflictBlocker);

    private sealed class ReplayRecoveryStore(FileWorkspaceStore inner, string directory,
        Action requireNativeReplay) : IWorkspaceStore, IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public int CommitCount { get; private set; }
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => inner.Get(id);
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => inner.Get(owner, id);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long expectedContentRevision, string expectedAuxiliaryStateDigest,
            WorkspaceDocument document)
        {
            Assert.AreEqual(0, CommitCount, "Imported replay must never reach a writer twice.");
            CommitCount++;
            var committed = inner.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                id, expectedContentRevision, expectedAuxiliaryStateDigest, document);
            Assert.IsTrue(committed.Success, committed.Error);
            requireNativeReplay();
            WorkspaceImportedHistoryTestFixture.MarkImported(directory, id);
            return new(WorkspaceOperationOutcome.Conflict, committed.Entry,
                "Simulated restore observation after an otherwise genuine atomic commit.");
        }

        public IReadOnlyList<WorkspaceStoreEntry> List() => inner.List();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => inner.List(owner);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => UnexpectedWrite();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => UnexpectedWrite();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => UnexpectedWrite();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document) => UnexpectedWrite();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => UnexpectedWrite();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision) => UnexpectedWrite();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => UnexpectedWrite();
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long revision) => UnexpectedWrite();
        private static WorkspaceStoreMutationResult UnexpectedWrite() => throw new AssertFailedException("Unexpected workspace mutation.");
    }
}
