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
using System.Xml.Linq;

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
    public void Nonempty_quality_and_gear_are_source_bound_atomically_finalized_and_reopened()
    {
        using ReadyContext context = ReadyContext.Create(
            includeGearReview: true,
            includeNonEmptyPurchases: true);
        WorkspaceStoredDocument before = context.Store.Get(context.WorkspaceId).Value!;
        CharacterCreationQualitiesDraft qualityDraft = before.Document.AuxiliaryState
            .CharacterCreationQualitiesDraft!;
        CharacterCreationGearDraft gearDraft = before.Document.AuxiliaryState
            .CharacterCreationGearDraft!;
        Assert.HasCount(1, qualityDraft.Selections);
        Assert.HasCount(1, gearDraft.Lines);
        CharacterCreationQualitySelection selectedQuality = qualityDraft.Selections.Single();
        CharacterCreationGearLine selectedGear = gearDraft.Lines.Single();

        CharacterCreationFinalizationState state = AssertAvailable(
            context.Finalizer.Load(new(context.WorkspaceId)));
        CharacterCreationFinalizationReview review = AssertAvailable(
            context.Finalizer.Review(new(state.Binding)));
        Assert.IsTrue(review.OrderedDeltas.Any(delta =>
            delta.Kind == CharacterCreationFinalizationDeltaKinds.Quality
            && delta.TargetId == selectedQuality.SourceId.ToString("D")));
        Assert.IsTrue(review.OrderedDeltas.Any(delta =>
            delta.Kind == CharacterCreationFinalizationDeltaKinds.Gear
            && delta.TargetId == selectedGear.SourceId.ToString("D")));

        CharacterCreationQualitySelection tamperedSelection = selectedQuality with
        {
            SourceNodeXml = selectedQuality.SourceNodeXml.Replace(
                selectedQuality.Name,
                selectedQuality.Name + " tampered",
                StringComparison.Ordinal)
        };
        WorkspaceStoredDocument tamperedWorkspace = before with
        {
            Document = before.Document with
            {
                State = before.Document.State with
                {
                    AuxiliaryState = before.Document.AuxiliaryState with
                    {
                        CharacterCreationQualitiesDraft = qualityDraft with
                        {
                            Selections = [tamperedSelection]
                        }
                    }
                }
            }
        };
        Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(
            tamperedWorkspace,
            out _, out _, out _, out _, out _, out _, out string[] tamperBlockers));
        CollectionAssert.Contains(
            tamperBlockers.ToList(),
            CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        CharacterCreationGearLine tamperedGear = selectedGear with
        {
            SourceNodeXml = selectedGear.SourceNodeXml.Replace(
                selectedGear.Name,
                selectedGear.Name + " tampered",
                StringComparison.Ordinal)
        };
        WorkspaceStoredDocument gearTamperedWorkspace = before with
        {
            Document = before.Document with
            {
                State = before.Document.State with
                {
                    AuxiliaryState = before.Document.AuxiliaryState with
                    {
                        CharacterCreationGearDraft = gearDraft with
                        {
                            Lines = [tamperedGear],
                            FinalizationContribution = gearDraft.FinalizationContribution with
                            {
                                Lines = [tamperedGear]
                            }
                        }
                    }
                }
            }
        };
        Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(
            gearTamperedWorkspace,
            out _, out _, out _, out _, out _, out _, out string[] gearTamperBlockers));
        CollectionAssert.Contains(
            gearTamperBlockers.ToList(),
            CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);

        const string key = "finalize-priority-nonempty-quality-gear-0001";
        CharacterCreationFinalizationConfirmRequest command = new(
            state.Binding,
            review.PreviewDigest,
            review.Plan!.PlanDigest,
            key,
            ExplicitlyConfirmed: true);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> applied =
            context.Finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome,
            string.Join(",", applied.Blockers));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed,
            context.Finalizer.Confirm(command).Outcome);

        using ReadyContext restarted = context.Restart();
        WorkspaceStoredDocument reopened = restarted.Store.Get(restarted.WorkspaceId).Value!;
        XDocument document = XDocument.Parse(reopened.Document.Content, LoadOptions.None);
        XElement root = document.Root!;
        XElement[] qualities = root.Element("qualities")!.Elements("quality").ToArray();
        XElement[] gears = root.Element("gears")!.Elements("gear").ToArray();
        Assert.HasCount(1, qualities);
        Assert.HasCount(1, gears);
        XElement quality = qualities.Single();
        XElement gear = gears.Single();
        Assert.AreEqual(selectedQuality.SourceId.ToString("D"), quality.Element("sourceid")!.Value);
        Assert.AreEqual(selectedQuality.Name, quality.Element("name")!.Value);
        Assert.IsNotNull(quality.Element("bonus"));
        Assert.IsNotNull(quality.Element("firstlevelbonus"));
        XElement[] qualityImprovements = (root.Element("improvements")
                ?.Elements("improvement") ?? Enumerable.Empty<XElement>())
            .Where(item => item.Element("sourcename")?.Value == quality.Element("guid")!.Value)
            .ToArray();
        Assert.IsTrue(qualityImprovements.Length <= 1);
        Assert.AreEqual(selectedGear.SourceId.ToString("D"), gear.Element("sourceid")!.Value);
        Assert.AreEqual(selectedGear.Name, gear.Element("name")!.Value);
        Assert.AreEqual(selectedGear.Quantity, int.Parse(gear.Element("qty")!.Value));
        Assert.IsNotNull(gear.Element("children"));
        Assert.IsNotNull(gear.Element("wirelessbonus"));
        Assert.IsTrue(restarted.Queries.ParseSummary(new(reopened.Document.Content)).Created);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed,
            restarted.Finalizer.LookupReceipt(new(restarted.WorkspaceId, key)).Outcome);
    }

    [TestMethod]
    public void Supported_quality_effect_projects_the_complete_legacy_quality_and_improvement_graph()
    {
        Guid sourceId = Guid.Parse("68cfe94a-fa7e-4129-a9b9-b5d73e3ced99");
        string sourceNodeXml = $"<quality><id>{sourceId:D}</id><name>Exact Ambidextrous</name><karma>4</karma><category>Positive</category><implemented>True</implemented><contributetobp>True</contributetobp><contributetolimit>True</contributetolimit><doublecareer>True</doublecareer><bonus><ambidextrous /></bonus><firstlevelbonus /><source>SR5</source><page>71</page><notes>source note</notes><notesColor>#010203</notesColor></quality>";
        string sourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(
            sourceNodeXml);
        var selection = new CharacterCreationQualitySelection(
            "quality:exact-ambidextrous:rating:1",
            sourceId,
            sourceId.ToString("D"),
            "Exact Ambidextrous",
            CharacterCreationQualityType.Positive,
            Rating: 1,
            KarmaCost: 4,
            IsMetagenic: false,
            CountsAgainstQualityLimit: true,
            CountsAgainstKarma: true,
            IsFreeOrGranted: false,
            FollowUpChoiceId: null,
            FollowUpChoiceLabel: null,
            SourceAnchorIds: [$"qualities.xml#quality:{sourceId:D}"],
            SourceNodeXml: sourceNodeXml,
            SourceNodeDigest: sourceNodeDigest,
            OptionDigest: CharacterCreationFinalizationDigest.ComputeUtf8("option"));
        string draftDigest = CharacterCreationFinalizationDigest.ComputeUtf8("quality-draft");

        Assert.IsTrue(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(
            sourceNodeXml));
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            selection,
            draftDigest,
            out XElement[] qualities,
            out XElement[] improvements));
        Assert.HasCount(1, qualities);
        Assert.HasCount(1, improvements);
        XElement quality = qualities.Single();
        XElement improvement = improvements.Single();
        Assert.AreEqual(sourceId.ToString("D"), quality.Element("sourceid")!.Value);
        Assert.AreEqual("4", quality.Element("bp")!.Value);
        Assert.AreEqual("Selected", quality.Element("qualitysource")!.Value);
        Assert.AreEqual("source note", quality.Element("notes")!.Value);
        Assert.AreEqual("#010203", quality.Element("notesColor")!.Value);
        Assert.IsNotNull(quality.Element("bonus")!.Element("ambidextrous"));
        Assert.AreEqual("Ambidextrous", improvement.Element("improvementttype")!.Value);
        Assert.AreEqual("Quality", improvement.Element("improvementsource")!.Value);
        Assert.AreEqual(quality.Element("guid")!.Value, improvement.Element("sourcename")!.Value);
        Assert.AreEqual("1", improvement.Element("enabled")!.Value);
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            selection with { SourceNodeDigest = CharacterCreationFinalizationDigest.ComputeUtf8("tampered") },
            draftDigest,
            out _,
            out _));
    }

    [TestMethod]
    public void Creation_custom_drug_is_CAS_queued_revalidated_finalized_and_reopened_without_Career_commit()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        var customDrugAuthority = new TestCreationCustomDrugAuthority(context.Resolver);
        var queue = new CharacterCreationCustomDrugContributionService(
            context.Store,
            customDrugAuthority);
        CharacterCreationCustomDrugQueueRequest request = context.BuildCustomDrugQueueRequest(
            customDrugAuthority,
            "creation-custom-drug-queue-0001");
        WorkspaceStoredDocument before = context.Store.Get(context.WorkspaceId).Value!;

        CharacterCreationCustomDrugResult staleRevision = queue.Queue(request with
        {
            ExpectedContentRevision = request.ExpectedContentRevision - 1
        });
        Assert.AreEqual(CharacterCreationCustomDrugOutcomes.Conflict, staleRevision.Outcome);
        CollectionAssert.Contains(staleRevision.Blockers.ToList(),
            CharacterCreationCustomDrugBlockers.StaleWorkspaceRevision);
        CharacterCreationCustomDrugResult staleCatalog = queue.Queue(request with
        {
            VerificationCommand = request.VerificationCommand with
            {
                ExpectedCatalogDigest = new string('a', 64)
            },
            IdempotencyKey = "creation-custom-drug-stale-catalog-0001"
        });
        CollectionAssert.Contains(staleCatalog.Blockers.ToList(),
            CharacterCreationCustomDrugBlockers.StaleCatalogDigest);
        CharacterCreationCustomDrugResult staleRules = queue.Queue(request with
        {
            VerificationCommand = request.VerificationCommand with
            {
                ExpectedRulesDigest = new string('b', 64)
            },
            IdempotencyKey = "creation-custom-drug-stale-rules-0001"
        });
        CollectionAssert.Contains(staleRules.Blockers.ToList(),
            CharacterCreationCustomDrugBlockers.StaleRulesDigest);
        CharacterCreationCustomDrugResult staleQuote = queue.Queue(request with
        {
            VerificationCommand = request.VerificationCommand with
            {
                ExpectedQuoteDigest = new string('c', 64)
            },
            IdempotencyKey = "creation-custom-drug-stale-quote-0001"
        });
        CollectionAssert.Contains(staleQuote.Blockers.ToList(),
            CharacterCreationCustomDrugBlockers.StaleQuoteDigest);
        CharacterCreationCustomDrugResult collidingIdentity = queue.Queue(request with
        {
            VerificationCommand = request.VerificationCommand with
            {
                NewComponentInstanceIds =
                [request.VerificationCommand.Selection.Components[0].ComponentId.Value]
            },
            IdempotencyKey = "creation-custom-drug-colliding-id-0001"
        });
        CollectionAssert.Contains(collidingIdentity.Blockers.ToList(),
            CharacterCreationCustomDrugBlockers.ProjectionRejected);
        Assert.AreEqual(before.ContentRevision,
            context.Store.Get(context.WorkspaceId).Value!.ContentRevision);

        CharacterCreationCustomDrugResult queued = queue.Queue(request);
        Assert.AreEqual(CharacterCreationCustomDrugOutcomes.Applied, queued.Outcome,
            string.Join(",", queued.Blockers));
        CharacterCreationCustomDrugFinalizationContribution contribution = queued.Contribution!;
        Assert.AreEqual(contribution.ExpectedContentRevision, before.ContentRevision + 1);
        WorkspaceStoredDocument afterQueue = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.Document.Content, afterQueue.Document.Content);
        Assert.AreEqual(CharacterCreationCustomDrugOutcomes.Replayed, queue.Queue(request).Outcome);

        WorkspaceStoredDocument tamperedContribution = afterQueue with
        {
            Document = afterQueue.Document with
            {
                State = afterQueue.Document.State with
                {
                    AuxiliaryState = afterQueue.Document.AuxiliaryState with
                    {
                        CharacterCreationCustomDrugContribution = contribution with
                        {
                            ProjectedDrugXmlDigest = new string('d', 64)
                        }
                    }
                }
            }
        };
        Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(
            tamperedContribution,
            out _, out _, out _, out _, out _, out _, out string[] contributionBlockers));
        CollectionAssert.Contains(contributionBlockers.ToList(),
            CharacterCreationFinalizationBlockers.CustomDrugContributionInvalid);

        var restartedStore = new FileWorkspaceStore(context.Directory);
        var restartedQueue = new CharacterCreationCustomDrugContributionService(
            restartedStore,
            customDrugAuthority);
        Assert.AreEqual(CharacterCreationCustomDrugOutcomes.Available,
            restartedQueue.Load(new(context.WorkspaceId)).Outcome);
        var recordingAuthority = new RecordingCustomDrugAuthority(customDrugAuthority);
        ICharacterCreationFinalizationService finalizer = ReadyContext.BuildFinalizer(
            restartedStore,
            context.Queries,
            context.Resolver,
            recordingAuthority);
        CharacterCreationFinalizationState state = AssertAvailable(
            finalizer.Load(new(context.WorkspaceId)));
        CharacterCreationFinalizationReview review = AssertAvailable(
            finalizer.Review(new(state.Binding)));
        Assert.IsTrue(review.OrderedDeltas.Any(delta =>
            delta.Kind == CharacterCreationFinalizationDeltaKinds.CustomDrug
            && delta.TargetId == contribution.NewDrugInstanceId.Value.ToString("D")));

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> finalized =
            finalizer.Confirm(new(
                state.Binding,
                review.PreviewDigest,
                review.Plan!.PlanDigest,
                "finalize-with-custom-drug-0001",
                ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, finalized.Outcome,
            string.Join(",", finalized.Blockers));
        Assert.AreEqual(0, recordingAuthority.CareerCommitCalls,
            "Creation finalization must never route through the Career Commit command.");
        Assert.IsTrue(recordingAuthority.PrepareContexts.All(
            static context => context == CharacterCustomDrugContext.Creation));

        var freshStore = new FileWorkspaceStore(context.Directory);
        WorkspaceStoredDocument reopened = freshStore.Get(context.WorkspaceId).Value!;
        Assert.IsNull(reopened.Document.AuxiliaryState.CharacterCreationCustomDrugContribution);
        XDocument character = XDocument.Parse(reopened.Document.Content, LoadOptions.None);
        XElement drug = character.Root!.Element("drugs")!.Elements("drug").Single();
        Assert.AreEqual(contribution.NewDrugInstanceId.Value.ToString("D"),
            drug.Element("guid")!.Value);
        Assert.AreEqual(contribution.Quote.Name, drug.Element("name")!.Value);
        Assert.IsTrue(context.Queries.ParseSummary(new(reopened.Document.Content)).Created);
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

        public static ReadyContext Create(
            bool includeGearReview,
            bool includeNonEmptyPurchases = false)
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
            CompleteDrafts(
                store,
                workspaceId,
                queries,
                resolver,
                includeGearReview,
                includeNonEmptyPurchases);
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
            bool includeGearReview,
            bool includeNonEmptyPurchases)
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
            string[] selectedQualityIds = includeNonEmptyPurchases
                ?
                [qualityState.Authority.Options
                    .Where(static option => option.IsSelectable)
                    .Where(static option => option.KarmaCost is >= 0 and <= 25)
                    .OrderBy(static option => option.KarmaCost)
                    .ThenBy(static option => option.OptionId, StringComparer.Ordinal)
                    .First().OptionId]
                : [];
            CharacterCreationQualitiesPreview qualityPreview = qualities.Preview(new(
                qualityState.Binding,
                selectedQualityIds)).Value!;
            CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> qualityReceipt =
                qualities.Confirm(new(
                    qualityPreview.Binding,
                    selectedQualityIds,
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
            CharacterCreationGearSelection[] basket = includeNonEmptyPurchases
                ?
                [new CharacterCreationGearSelection(
                    gearState.Authority.Options
                        .Where(static option => option.IsSelectable)
                        .Where(option => option.PackageQuantity == 1
                                         && option.PackageCost > 0m
                                         && option.PackageCost <= gearState.Budget.TotalStartingNuyen)
                        .OrderBy(static option => option.PackageCost)
                        .ThenBy(static option => option.OptionId, StringComparer.Ordinal)
                        .First().OptionId,
                    Quantity: 1)]
                : [];
            CharacterCreationGearPreview gearPreview = gear.Preview(new(
                gearState.Binding,
                basket)).Value!;
            Assert.AreEqual(CharacterCreationGearOutcomes.Applied,
                gear.Confirm(new(
                    gearPreview.Binding,
                    basket,
                    gearPreview.PreviewDigest,
                    "gear-finalization-test",
                    ExplicitlyConfirmed: true)).Outcome);
        }

        internal static ICharacterCreationFinalizationService BuildFinalizer(
            IWorkspaceStore store,
            ICharacterFileQueries queries,
            ICharacterSourceDataResolver resolver,
            ICharacterCustomDrugAuthority? customDrugs = null)
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
                new CharacterCreationGearService(store, resolver),
                customDrugs ?? new FileSystemCharacterCustomDrugAuthority(resolver));
        }

        public CharacterCreationCustomDrugQueueRequest BuildCustomDrugQueueRequest(
            ICharacterCustomDrugAuthority authority,
            string idempotencyKey)
        {
            WorkspaceStoredDocument workspace = Store.Get(WorkspaceId).Value!;
            CharacterCustomDrugPreparation preparation = authority.Prepare(
                workspace.Document.Content,
                workspace.ContentRevision,
                CharacterCustomDrugContext.Creation);
            Assert.IsTrue(preparation.Exact, string.Join(",", preparation.Blockers));
            CharacterCustomDrugComponentSource foundation = preparation.Components
                .Where(static component =>
                    component.Category == CharacterCustomDrugComponentCategory.Foundation)
                .OrderBy(static component => component.Id.Value)
                .First();
            int level = foundation.Effects.OrderBy(static effect => effect.Level).First().Level;
            CharacterCustomDrugGrade grade = preparation.Grades
                .OrderBy(static item => item.Id.Value)
                .First();
            var selection = new CharacterCustomDrugSelection(
                "Finalizer Redline",
                grade.Id,
                Quantity: 1m,
                Stolen: false,
                FreeCost: false,
                MarkupPercent: 0m,
                Components: [new CharacterCustomDrugComponentSelection(foundation.Id, level)]);
            CharacterCustomDrugQuote quote = authority.Quote(preparation, selection);
            Assert.IsTrue(quote.Exact, quote.BlockReason);
            var command = new CharacterCustomDrugCommitCommand(
                workspace.ContentRevision,
                preparation.CharacterDigest,
                preparation.CatalogDigest,
                preparation.RulesDigest,
                quote.QuoteDigest,
                "custom-drug:creation:verification:0001",
                selection,
                new CharacterCustomDrugInstanceId(
                    Guid.Parse("81111111-1111-4111-8111-111111111111")),
                [Guid.Parse("82222222-2222-4222-8222-222222222222")]);
            return new CharacterCreationCustomDrugQueueRequest(
                WorkspaceId,
                workspace.ContentRevision,
                workspace.SavedRevision,
                workspace.Document.AuxiliaryStateDigest,
                command,
                idempotencyKey,
                ExplicitlyConfirmed: true);
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

    private sealed class RecordingCustomDrugAuthority(ICharacterCustomDrugAuthority inner)
        : ICharacterCustomDrugAuthority
    {
        public int CareerCommitCalls { get; private set; }
        public List<CharacterCustomDrugContext> PrepareContexts { get; } = [];

        public CharacterCustomDrugPreparation Prepare(
            string characterXml,
            long contentRevision,
            CharacterCustomDrugContext context)
        {
            PrepareContexts.Add(context);
            return inner.Prepare(characterXml, contentRevision, context);
        }

        public CharacterCustomDrugQuote Quote(
            CharacterCustomDrugPreparation preparation,
            CharacterCustomDrugSelection selection) => inner.Quote(preparation, selection);

        public CharacterCustomDrugCreationProjection ProjectCreation(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugCommitCommand command) => inner.ProjectCreation(
            characterXml,
            currentContentRevision,
            command);

        public CharacterCustomDrugCommitResult Commit(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugCommitCommand command)
        {
            if (context == CharacterCustomDrugContext.Career)
                CareerCommitCalls++;
            return inner.Commit(characterXml, currentContentRevision, context, command);
        }

        public CharacterCustomDrugCommitResult LookupReceipt(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugCommitCommand command) => inner.LookupReceipt(
            characterXml,
            currentContentRevision,
            context,
            command);

        public CharacterCustomDrugCommitResult Undo(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugUndoCommand command) => inner.Undo(
            characterXml,
            currentContentRevision,
            context,
            command);
    }

    /// <summary>
    /// Uses the real Full House SR5 source catalog and real legacy projector while
    /// keeping the test's canonical Priority bootstrap profile unchanged. The
    /// production file authority remains fail-closed when Chrome Flesh is disabled.
    /// </summary>
    private sealed class TestCreationCustomDrugAuthority : ICharacterCustomDrugAuthority
    {
        private const string FullHouseProfileId = "67e25032-2a4e-42ca-97fa-69f7f608236c";
        private readonly CharacterCustomDrugCatalogAuthority _catalog;
        private readonly FileSystemCharacterCustomDrugAuthority _projector;

        public TestCreationCustomDrugAuthority(ICharacterSourceDataResolver resolver)
        {
            _projector = new FileSystemCharacterCustomDrugAuthority(resolver);
            ICharacterSourceDataContext context = resolver.TryCreateContext(
                $"<character><settings>{FullHouseProfileId}</settings>"
                + "<customdatadirectorynames>"
                + "<directoryname>Chrome Flesh Stealth Errata</directoryname>"
                + "<directoryname>Dark Terrors Stealth Errata</directoryname>"
                + "<directoryname>Forbidden Arcana Stealth Errata</directoryname>"
                + "<directoryname>No Future Stealth Errata</directoryname>"
                + "</customdatadirectorynames></character>")!;
            Assert.IsTrue(context.TryResolveCustomDrugCatalog(out _catalog));
        }

        public CharacterCustomDrugPreparation Prepare(
            string characterXml,
            long contentRevision,
            CharacterCustomDrugContext context)
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            decimal nuyen = 0m;
            bool exactCreation = context == CharacterCustomDrugContext.Creation
                                 && bool.TryParse(root.Element("created")?.Value, out bool created)
                                 && !created
                                 && decimal.TryParse(
                                     root.Element("nuyen")?.Value,
                                     System.Globalization.NumberStyles.Number,
                                     System.Globalization.CultureInfo.InvariantCulture,
                                     out nuyen)
                                 && nuyen >= 0m;
            CharacterCustomDrugPreparation preparation = CharacterCustomDrugRules.BindPreparation(
                _catalog,
                context,
                CharacterCustomDrugQuotePurpose.RecipeDefinition,
                contentRevision,
                CharacterCustomDrugRules.ComputeCharacterDigest(characterXml),
                exactCreation ? nuyen : 0m);
            return exactCreation
                ? preparation
                : preparation with
                {
                    Exact = false,
                    Blockers = [CharacterCustomDrugBlockers.NotCreation]
                };
        }

        public CharacterCustomDrugQuote Quote(
            CharacterCustomDrugPreparation preparation,
            CharacterCustomDrugSelection selection) =>
            CharacterCustomDrugRules.Quote(preparation, selection);

        public CharacterCustomDrugCreationProjection ProjectCreation(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugCommitCommand command)
        {
            string sourceEnabledXml = EnableFullHouseProfile(characterXml);
            CharacterCustomDrugPreparation sourcePreparation = _projector.Prepare(
                sourceEnabledXml,
                currentContentRevision,
                CharacterCustomDrugContext.Creation);
            CharacterCustomDrugQuote sourceQuote = _projector.Quote(
                sourcePreparation,
                command.Selection);
            CharacterCustomDrugCreationProjection projected = _projector.ProjectCreation(
                sourceEnabledXml,
                currentContentRevision,
                command with
                {
                    ExpectedCharacterDigest = sourcePreparation.CharacterDigest,
                    ExpectedCatalogDigest = sourcePreparation.CatalogDigest,
                    ExpectedRulesDigest = sourcePreparation.RulesDigest,
                    ExpectedQuoteDigest = sourceQuote.QuoteDigest
                });
            return projected.Exact
                ? projected with { QuoteDigest = command.ExpectedQuoteDigest }
                : projected;
        }

        public CharacterCustomDrugCommitResult Commit(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugCommitCommand command) => throw new InvalidOperationException(
            "The Creation finalizer must not call custom-drug Commit.");

        public CharacterCustomDrugCommitResult LookupReceipt(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugCommitCommand command) => throw new InvalidOperationException(
            "The Creation finalizer must not call custom-drug Career receipt lookup.");

        public CharacterCustomDrugCommitResult Undo(
            string characterXml,
            long currentContentRevision,
            CharacterCustomDrugContext context,
            CharacterCustomDrugUndoCommand command) => throw new InvalidOperationException(
            "The Creation finalizer must not call custom-drug Career undo.");

        private static string EnableFullHouseProfile(string characterXml)
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            root.Element("settings")!.Value = FullHouseProfileId;
            root.Elements("customdatadirectorynames").Remove();
            root.Element("settings")!.AddAfterSelf(new XElement(
                "customdatadirectorynames",
                new XElement("directoryname", "Chrome Flesh Stealth Errata"),
                new XElement("directoryname", "Dark Terrors Stealth Errata"),
                new XElement("directoryname", "Forbidden Arcana Stealth Errata"),
                new XElement("directoryname", "No Future Stealth Errata")));
            return document.ToString(SaveOptions.DisableFormatting);
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
