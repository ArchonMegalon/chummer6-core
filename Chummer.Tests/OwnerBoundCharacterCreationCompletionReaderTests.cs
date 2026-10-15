using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class OwnerBoundCharacterCreationCompletionReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_reader_uses_one_owner_observation_rejects_ABA_and_cancels_without_writes(bool includeLoadout)
    {
        using var fixture = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        string directory = Path.Combine(fixture.Directory, "completion-owner-only");
        var store = new FileWorkspaceStore(directory);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var ownerA = new OwnerScope("completion-owner-a");
        var ownerB = new OwnerScope("completion-owner-b");
        owners.Set(ownerA);
        OwnerContextStamp original = owners.Capture();
        var codec = new Sr5WorkspaceCodec(fixture.Queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(fixture.Resolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(
            new CharacterCreationBootstrapService(store, new RulesetWorkspaceCodecResolver([codec]),
                fixture.Queries, fixture.Resolver), owners);
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            CharacterCreationBuildMethods.Priority, out var settings));
        var created = bootstrap.Create(original, new(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
            "Completion owner test", "Completion", CharacterCreationBuildMethods.Priority, settings));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome, string.Join(",", created.Blockers));
        var id = created.Value!.WorkspaceId;
        var source = store.Get(ownerA, id).Value!.Document;
        Assert.IsTrue(store.CreateWorkspaceDocument(ownerB, id,
            new WorkspaceDocument(source.Content, source.RulesetId)).Success);
        var prerequisites = new OwnerBoundCharacterCreationPrerequisiteService(
            store, owners, fixture.Queries, fixture.Resolver);
        var state = prerequisites.Load(original, new(id)).Value!;
        var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
        var heritage = state.Authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage && option.Rank == "A")
            .HeritageOptions.First(option => option.IsEnabled && option.MetatypeName == "Human"
                && option.MetavariantSourceId is null);
        var talent = state.Authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent && option.Rank == "E")
            .TalentOptions.First(option => option.IsEnabled
                && option.Value.Equals("Mundane", StringComparison.OrdinalIgnoreCase));
        var preview = prerequisites.Preview(original, new(state.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            prerequisites.Confirm(original, new(preview.Binding, ranks, preview.PreviewDigest, true)
                { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Outcome);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, fixture.Resolver);
        var attributePreview = attributes.Preview(original,
            new(attributes.Load(original, new(id)).Value!.Binding, [])).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            attributes.Confirm(original, new(attributePreview.Binding, [], attributePreview.PreviewDigest, true)).Outcome);

        Assert.IsFalse(store.Get(id).Success, "No local copy may conceal an owner partition error.");
        var before = store.Get(ownerA, id).Value!;
        var other = store.Get(ownerB, id).Value!;
        var bytes = Snapshot(directory);
        var observed = new ObservedStore(store);
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            observed, owners, fixture.Resolver, fixture.Queries);
        CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> Load(
            OwnerContextStamp stamp, CancellationToken token = default)
            => includeLoadout ? reader.LoadCompletionInputsWithLoadout(stamp, new(id), token)
                : reader.LoadCompletionInputs(stamp, new(id), token);
        var first = Load(original);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, first.Outcome, string.Join(",", first.Blockers));
        Assert.IsNotNull(first.Value);
        Assert.AreEqual(includeLoadout, first.Value.Loadout is not null);
        Assert.AreEqual(includeLoadout ? CharacterCreationCompletionReviewSchemas.InputsV2
            : CharacterCreationCompletionReviewSchemas.InputsV1, first.Value.Schema);
        Assert.AreEqual(1, observed.OwnerReads, "All completion domains must reuse one original store observation.");
        Assert.AreEqual(0, observed.LocalReads);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, first.Value.Skills.Binding.AuxiliaryStateDigest);
        Assert.AreEqual(first.Value.Skills.Attributes.SnapshotDigest, first.Value.Magic.Attributes.SnapshotDigest);
        Assert.AreEqual(first.Value.Lifestyles.Qualities.SnapshotDigest, first.Value.Contacts.Qualities.SnapshotDigest);
        Assert.AreEqual(0, owners.ActiveLeases);

        foreach (var denied in new[] { default(OwnerContextStamp), original with { Owner = ownerB },
                     original with { AuthorityInstanceId = "foreign" } })
            Assert.IsNull(Load(denied).Value);
        Assert.AreEqual(1, observed.OwnerReads, "Rejected owner stamps must not read a partition.");
        owners.Set(ownerB);
        Assert.IsNull(Load(owners.Capture()).Value,
            "B's same workspace ID must not expose A's confirmed prerequisite or attribute state.");
        Assert.AreEqual(2, observed.OwnerReads);
        owners.Set(ownerA);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            Load(original).Outcome);
        Assert.AreEqual(2, observed.OwnerReads, "A -> B -> A must not revive the original stamp.");
        var current = owners.Capture();
        var fresh = Load(current);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, fresh.Outcome, string.Join(",", fresh.Blockers));
        Assert.AreEqual(first.Value.SnapshotDigest, fresh.Value!.SnapshotDigest);
        Assert.AreEqual(3, observed.OwnerReads);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AssertCanceled(() =>
            Load(current, cancelled.Token));
        Assert.AreEqual(3, observed.OwnerReads, "Pre-cancellation must not enter the store.");
        using var interrupted = new CancellationTokenSource();
        observed.AfterOwnerRead = interrupted.Cancel;
        AssertCanceled(() =>
            Load(current, interrupted.Token));
        Assert.AreEqual(4, observed.OwnerReads, "Cancellation after observation must not trigger a second read.");
        Assert.AreEqual(0, observed.LocalReads);
        Assert.AreEqual(0, owners.ActiveLeases, "Both cancellation paths must release owner admission.");
        Assert.AreEqual(before.ContentRevision, store.Get(ownerA, id).Value!.ContentRevision);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(ownerA, id).Value!.Document.AuxiliaryStateDigest);
        Assert.AreEqual(other.ContentRevision, store.Get(ownerB, id).Value!.ContentRevision);
        Assert.AreEqual(other.Document.AuxiliaryStateDigest, store.Get(ownerB, id).Value!.Document.AuxiliaryStateDigest);
        var after = Snapshot(directory);
        CollectionAssert.AreEquivalent(bytes.Keys.ToArray(), after.Keys.ToArray());
        foreach (var entry in bytes) CollectionAssert.AreEqual(entry.Value, after[entry.Key], entry.Key);
    }

    private static Dictionary<string, byte[]> Snapshot(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertCanceled(Action action)
    {
        bool canceled = false;
        try { action(); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled, "Cancellation must propagate without completing the read.");
    }

    // Real store observations and actual capabilities; every accidental write
    // fails the test before it can mutate the prepared synthetic partitions.
    private sealed class ObservedStore(FileWorkspaceStore inner) : IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability, IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public int OwnerReads { get; private set; }
        public int LocalReads { get; private set; }
        public Action? AfterOwnerRead { get; set; }
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit =>
            ((IWorkspaceAuxiliaryStateAtomicCommitCapability)inner).SupportsWorkspaceAuxiliaryStateAtomicCommit;
        public bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit =>
            ((IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability)inner).SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit;
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            LocalReads++;
            return inner.Get(id);
        }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
        {
            OwnerReads++;
            var result = inner.Get(owner, id);
            AfterOwnerRead?.Invoke();
            return result;
        }
        public IReadOnlyList<WorkspaceStoreEntry> List() => inner.List();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => inner.List(owner);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => DenyWrite();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => DenyWrite();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id,
            long expectedContentRevision, WorkspaceDocument document) => DenyWrite();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id,
            long expectedContentRevision, WorkspaceDocument document) => DenyWrite();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long expectedContentRevision, string expectedAuxiliaryStateDigest,
            WorkspaceDocument document) => DenyWrite();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision,
            string expectedAuxiliaryStateDigest, WorkspaceDocument document) => DenyWrite();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision) => DenyWrite();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id,
            long expectedContentRevision) => DenyWrite();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision) => DenyWrite();
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id,
            long expectedContentRevision) => DenyWrite();
        private static WorkspaceStoreMutationResult DenyWrite()
            => throw new InvalidOperationException("Completion input admission attempted a workspace write.");
    }
}
