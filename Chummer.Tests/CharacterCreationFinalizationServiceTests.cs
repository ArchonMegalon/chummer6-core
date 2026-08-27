using Chummer.Application.Characters;
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

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationFinalizationServiceTests
{
    [TestMethod]
    public void Missing_required_step_and_partial_composite_write_fail_closed()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: false);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> missing =
            context.Finalizer.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, missing.Outcome);
        CollectionAssert.Contains(
            missing.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.GearDraftRequired);

        WorkspaceStoredDocument current = context.Store.Get(context.WorkspaceId).Value!;
        WorkspaceDocument partial = current.Document with
        {
            State = current.Document.State with
            {
                AuxiliaryState = current.Document.AuxiliaryState with
                {
                    CharacterCreationPrerequisiteDraft = null,
                    CharacterCreationAttributesDraft = null
                }
            }
        };
        WorkspaceStoreMutationResult rejected =
            ((IWorkspaceAuxiliaryStateAtomicCommitCapability)context.Store)
            .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                context.WorkspaceId,
                current.ContentRevision,
                current.Document.AuxiliaryStateDigest,
                partial);
        Assert.IsFalse(rejected.Success, "A partial whole-build clear must never commit.");
        WorkspaceStoredDocument unchanged = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(current.ContentRevision, unchanged.ContentRevision);
        Assert.IsNotNull(unchanged.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        Assert.IsNotNull(unchanged.Document.AuxiliaryState.CharacterCreationAttributesDraft);
    }

    [TestMethod]
    public void Finalization_is_digest_bound_idempotent_restart_recoverable_and_reopens_in_career()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        CharacterCreationFinalizationState state = AssertAvailable(
            context.Finalizer.Load(new(context.WorkspaceId)));
        Assert.IsTrue(state.CanReview);
        Assert.IsTrue(state.Steps.All(static step => step.IsComplete));

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> stale =
            context.Finalizer.Review(new(state.Binding with
            {
                ContentRevision = state.Binding.ContentRevision - 1
            }));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, stale.Outcome);
        CollectionAssert.Contains(
            stale.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.StaleWorkspaceRevision);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> staleDigest =
            context.Finalizer.Review(new(state.Binding with
            {
                RawCharacterXmlDigest = CharacterCreationFinalizationDigest.ComputeUtf8("stale")
            }));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, staleDigest.Outcome);
        CollectionAssert.Contains(
            staleDigest.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.StaleRawCharacterXmlDigest);

        CharacterCreationFinalizationReview review = AssertAvailable(
            context.Finalizer.Review(new(state.Binding)));
        Assert.IsTrue(review.CanConfirm);
        Assert.IsNotNull(review.Plan);
        Assert.IsTrue(review.OrderedDeltas.Count > 3);
        Assert.IsTrue(review.OrderedDeltas.Select(static delta => delta.Order)
            .SequenceEqual(Enumerable.Range(1, review.OrderedDeltas.Count)));

        const string idempotencyKey = "finalize-priority-mundane-test-0001";
        CharacterCreationFinalizationConfirmRequest command = new(
            state.Binding,
            review.PreviewDigest,
            review.Plan!.PlanDigest,
            idempotencyKey,
            ExplicitlyConfirmed: true);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> applied =
            context.Finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome,
            string.Join(",", applied.Blockers));
        CharacterCreationFinalizationReceipt receipt = applied.Value!;
        Assert.IsTrue(receipt.CharacterCreated);
        Assert.IsTrue(receipt.RequiresFreshCareerReopen);
        Assert.AreEqual(receipt.ContentRevision, receipt.SavedRevision);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> duplicate =
            context.Finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, duplicate.Outcome);
        Assert.AreEqual(receipt.ReceiptDigest, duplicate.Value!.ReceiptDigest);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> conflicting =
            context.Finalizer.Confirm(command with
            {
                PlanDigest = CharacterCreationFinalizationDigest.ComputeUtf8("different-plan")
            });
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, conflicting.Outcome);
        CollectionAssert.Contains(
            conflicting.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.IdempotencyConflict);

        ReadyContext restarted = context.Restart();
        using (restarted)
        {
            CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> recovered =
                restarted.Finalizer.LookupReceipt(new(restarted.WorkspaceId, idempotencyKey));
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, recovered.Outcome);
            Assert.AreEqual(receipt.ReceiptDigest, recovered.Value!.ReceiptDigest);

            WorkspaceStoredDocument reopened = restarted.Store.Get(restarted.WorkspaceId).Value!;
            CharacterFileSummary summary = restarted.Queries.ParseSummary(
                new CharacterDocument(reopened.Document.Content));
            Assert.IsTrue(summary.Created, "A fresh process must reopen the finalized runner in Career.");
            Assert.AreEqual(CharacterCreationBuildMethods.Priority, summary.BuildMethod);
            Assert.AreEqual(receipt.ContentRevision, reopened.ContentRevision);
            Assert.AreEqual(receipt.SavedRevision, reopened.SavedRevision);
            Assert.IsNull(reopened.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
            Assert.IsNull(reopened.Document.AuxiliaryState.CharacterCreationGearDraft);
            Assert.HasCount(1,
                reopened.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
        }
    }

    [TestMethod]
    public void Unknown_commit_outcome_recovers_the_exact_durable_receipt()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        var ambiguousStore = new CommitThenReportUnavailableStore(context.Store);
        ICharacterCreationFinalizationService finalizer = ReadyContext.BuildFinalizer(
            ambiguousStore,
            context.Queries,
            context.Resolver);
        CharacterCreationFinalizationState state = AssertAvailable(finalizer.Load(new(context.WorkspaceId)));
        CharacterCreationFinalizationReview review = AssertAvailable(finalizer.Review(new(state.Binding)));
        const string idempotencyKey = "finalize-priority-unknown-outcome-0001";

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> recovered =
            finalizer.Confirm(new(
                state.Binding,
                review.PreviewDigest,
                review.Plan!.PlanDigest,
                idempotencyKey,
                ExplicitlyConfirmed: true));

        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, recovered.Outcome);
        Assert.IsNotNull(recovered.Value);
        Assert.IsTrue(recovered.Value.CharacterCreated);
        Assert.AreEqual(1, ambiguousStore.AtomicCommitCount);
        CharacterFileSummary reopened = context.Queries.ParseSummary(new(
            context.Store.Get(context.WorkspaceId).Value!.Document.Content));
        Assert.IsTrue(reopened.Created);
    }

    [TestMethod]
    public void SumToTen_whole_build_finalization_remains_fail_closed_until_its_typed_lanes_exist()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(
            CharacterCreationBuildMethods.SumToTen);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> result =
            context.Finalizer.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.BuildMethodNotReady);
        Assert.IsFalse(result.Value!.CanReview);
    }

    private static T AssertAvailable<T>(CharacterCreationFinalizationResult<T> result)
        where T : class
    {
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, result.Outcome,
            string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private sealed class ReadyContext : IDisposable
    {
        private readonly bool _ownsDirectory;
        private readonly ICharacterSourceDataResolver _resolver;

        private ReadyContext(
            string directory,
            FileWorkspaceStore store,
            CharacterWorkspaceId workspaceId,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver,
            bool ownsDirectory)
        {
            Directory = directory;
            Store = store;
            WorkspaceId = workspaceId;
            Queries = queries;
            _resolver = resolver;
            _ownsDirectory = ownsDirectory;
            Finalizer = BuildFinalizer(store, queries, resolver);
        }

        public string Directory { get; }
        public FileWorkspaceStore Store { get; }
        public CharacterWorkspaceId WorkspaceId { get; }
        public ICharacterFileQueries Queries { get; }
        public ICharacterSourceDataResolver Resolver => _resolver;
        public ICharacterCreationFinalizationService Finalizer { get; }

        public static ReadyContext Create(bool includeGearReview)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"chummer-creation-finalization-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            string coreRoot = FindCoreRoot();
            ICharacterSourceDataResolver resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
            ICharacterFileQueries queries = new XmlCharacterFileQueries(new CharacterFileService());
            var store = new FileWorkspaceStore(directory);
            CharacterWorkspaceId workspaceId = Bootstrap(store, queries, resolver);
            CompleteDrafts(store, workspaceId, queries, resolver, includeGearReview);
            return new ReadyContext(
                directory,
                store,
                workspaceId,
                queries,
                resolver,
                ownsDirectory: true);
        }

        public static ReadyContext CreateUnprepared(string buildMethod)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"chummer-creation-finalization-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            string coreRoot = FindCoreRoot();
            ICharacterSourceDataResolver resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
            ICharacterFileQueries queries = new XmlCharacterFileQueries(new CharacterFileService());
            var store = new FileWorkspaceStore(directory);
            CharacterWorkspaceId workspaceId = Bootstrap(store, queries, resolver, buildMethod);
            return new ReadyContext(
                directory,
                store,
                workspaceId,
                queries,
                resolver,
                ownsDirectory: true);
        }

        public ReadyContext Restart() => new(
            Directory,
            new FileWorkspaceStore(Directory),
            WorkspaceId,
            Queries,
            _resolver,
            ownsDirectory: false);

        public void Dispose()
        {
            if (_ownsDirectory && System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }

        private static CharacterWorkspaceId Bootstrap(
            IWorkspaceStore store,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver,
            string buildMethod = CharacterCreationBuildMethods.Priority)
        {
            var codec = new Sr5WorkspaceCodec(
                queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var service = new CharacterCreationBootstrapService(
                store,
                new RulesetWorkspaceCodecResolver([codec]),
                queries,
                resolver);
            Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
                buildMethod,
                out string settingsProfileId));
            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result = service.Create(new(
                CharacterCreationBootstrapSchemas.RequestV1,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                RulesetDefaults.Sr5,
                "Finalization Runner",
                "Finalizer",
                buildMethod,
                settingsProfileId));
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, result.Outcome,
                string.Join(",", result.Blockers));
            return result.Value!.WorkspaceId;
        }

        private static void CompleteDrafts(
            IWorkspaceStore store,
            CharacterWorkspaceId workspaceId,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver,
            bool includeGearReview)
        {
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            CharacterCreationPrerequisiteState prerequisite = prerequisites.Load(new(workspaceId)).Value!;
            IReadOnlyDictionary<string, string> ranks = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                [CharacterCreationPriorityCategoryIds.Heritage] = "A",
                [CharacterCreationPriorityCategoryIds.Talent] = "E",
                [CharacterCreationPriorityCategoryIds.Attributes] = "B",
                [CharacterCreationPriorityCategoryIds.Skills] = "C",
                [CharacterCreationPriorityCategoryIds.Resources] = "D"
            };
            CharacterCreationPriorityOptionProjection heritageRank = prerequisite.Authority.Options.Single(
                option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                          && option.Rank == "A");
            CharacterCreationPriorityHeritageOptionProjection heritage = heritageRank.HeritageOptions.First(
                static option => option.IsEnabled
                                 && option.MetavariantSourceId is null
                                 && option.MetatypeName == "Human");
            CharacterCreationPriorityOptionProjection talentRank = prerequisite.Authority.Options.Single(
                option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                          && option.Rank == "E");
            CharacterCreationPriorityTalentOptionProjection talent = talentRank.TalentOptions.First(
                static option => option.IsEnabled
                                 && string.Equals(option.Value,
                                     CharacterCreationMagicResonanceKinds.Mundane,
                                     StringComparison.OrdinalIgnoreCase)
                                 && option.Magic is null
                                 && option.Resonance is null
                                 && option.Depth is null
                                 && option.ActiveSkillGrant is null
                                 && option.SkillGroupGrant is null);
            var prerequisiteRequest = new CharacterCreationPrerequisitePreviewRequest(
                prerequisite.Binding,
                ranks)
            {
                HeritageSelectionId = heritage.SelectionId,
                TalentSelectionId = talent.SelectionId
            };
            CharacterCreationPrerequisitePreview prerequisitePreview =
                prerequisites.Preview(prerequisiteRequest).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                prerequisites.Confirm(new(
                    prerequisitePreview.Binding,
                    ranks,
                    prerequisitePreview.PreviewDigest,
                    ExplicitlyConfirmed: true)
                {
                    HeritageSelectionId = heritage.SelectionId,
                    TalentSelectionId = talent.SelectionId
                }).Outcome);

            var attributes = new CharacterCreationAttributesService(store, resolver);
            CharacterCreationAttributesState attributeState = attributes.Load(new(workspaceId)).Value!;
            CharacterCreationAttributesPreview attributePreview = attributes.Preview(new(
                attributeState.Binding,
                [])).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                attributes.Confirm(new(
                    attributePreview.Binding,
                    [],
                    attributePreview.PreviewDigest,
                    ExplicitlyConfirmed: true)).Outcome);

            var skills = new CharacterCreationSkillsService(store, resolver);
            CharacterCreationSkillsState skillsState = skills.Load(new(workspaceId)).Value!;
            CharacterCreationSkillCatalogEntry native = skillsState.Authority.KnowledgeSkills.First(
                static option => option.CanBeNativeLanguage);
            CharacterCreationSkillAllocation[] skillAllocations =
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            CharacterCreationSkillsPreview skillsPreview = skills.Preview(new(
                skillsState.Binding,
                skillAllocations,
                [])).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                skills.Confirm(new(
                    skillsPreview.Binding,
                    skillAllocations,
                    [],
                    skillsPreview.PreviewDigest,
                    "skills-finalization-test",
                    ExplicitlyConfirmed: true)).Outcome);

            var qualities = new CharacterCreationQualitiesService(
                store, resolver, prerequisites, attributes);
            CharacterCreationQualitiesState qualityState = qualities.Load(new(workspaceId)).Value!;
            CharacterCreationQualitiesPreview qualityPreview = qualities.Preview(new(
                qualityState.Binding,
                [])).Value!;
            CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> qualityReceipt =
                qualities.Confirm(new(
                    qualityPreview.Binding,
                    [],
                    qualityPreview.PreviewDigest,
                    "qualities-finalization-test",
                    Guid.NewGuid(),
                    ExplicitlyConfirmed: true));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                qualityReceipt.Outcome,
                string.Join(",", qualityReceipt.Blockers));

            var resources = new CharacterCreationResourcesService(store, resolver);
            CharacterCreationResourcesState resourcesState = resources.Load(new(workspaceId)).Value!;
            CharacterCreationResourceAllocationOption zeroKarma = resourcesState.Options.First(
                static option => option.IsEnabled && option.KarmaInvestment == 0);
            CharacterCreationResourcesPreview resourcePreview = resources.Preview(new(
                resourcesState.Binding,
                zeroKarma.OptionId)).Value!;
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied,
                resources.Confirm(new(
                    resourcePreview.Binding,
                    zeroKarma.OptionId,
                    resourcePreview.PreviewDigest,
                    "resources-finalization-test",
                    ExplicitlyConfirmed: true)).Outcome);

            if (!includeGearReview)
                return;
            var gear = new CharacterCreationGearService(store, resolver);
            CharacterCreationGearState gearState = gear.Load(new(workspaceId)).Value!;
            CharacterCreationGearPreview gearPreview = gear.Preview(new(
                gearState.Binding,
                [])).Value!;
            Assert.AreEqual(CharacterCreationGearOutcomes.Applied,
                gear.Confirm(new(
                    gearPreview.Binding,
                    [],
                    gearPreview.PreviewDigest,
                    "gear-finalization-test",
                    ExplicitlyConfirmed: true)).Outcome);
        }

        internal static ICharacterCreationFinalizationService BuildFinalizer(
            IWorkspaceStore store,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver)
        {
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            var attributes = new CharacterCreationAttributesService(store, resolver);
            return new CharacterCreationFinalizationService(
                store,
                queries,
                prerequisites,
                attributes,
                new CharacterCreationSkillsService(store, resolver),
                new CharacterCreationQualitiesService(store, resolver, prerequisites, attributes),
                new CharacterCreationMagicResonanceService(store, resolver),
                new CharacterCreationResourcesService(store, resolver),
                new CharacterCreationGearService(store, resolver));
        }

        private static string FindCoreRoot()
        {
            DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate canonical Chummer data.");
        }
    }

    private sealed class CommitThenReportUnavailableStore :
        IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        private readonly FileWorkspaceStore _inner;

        public CommitThenReportUnavailableStore(FileWorkspaceStore inner) => _inner = inner;

        public int AtomicCommitCount { get; private set; }
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            string expectedAuxiliaryStateDigest,
            WorkspaceDocument document)
        {
            AtomicCommitCount++;
            WorkspaceStoreMutationResult committed =
                ((IWorkspaceAuxiliaryStateAtomicCommitCapability)_inner)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision,
                    expectedAuxiliaryStateDigest,
                    document);
            return committed.Success
                ? new WorkspaceStoreMutationResult(
                    WorkspaceOperationOutcome.Unavailable,
                    Error: "Simulated lost acknowledgement after durable commit.")
                : committed;
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(owner, document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(CharacterWorkspaceId id, WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(id, document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(owner, id, document);
        public IReadOnlyList<WorkspaceStoreEntry> List() => _inner.List();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => _inner.List(owner);
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => _inner.Get(id);
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => _inner.Get(owner, id);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) =>
            _inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) =>
            _inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);
        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id, long expectedContentRevision) =>
            _inner.SaveCheckpoint(id, expectedContentRevision);
        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) =>
            _inner.SaveCheckpoint(owner, id, expectedContentRevision);
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision) =>
            _inner.Delete(id, expectedContentRevision);
        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) =>
            _inner.Delete(owner, id, expectedContentRevision);
    }
}
