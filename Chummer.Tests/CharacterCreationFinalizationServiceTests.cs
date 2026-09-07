using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
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
    [DataRow("Human")]
    [DataRow("Elf")]
    public void Priority_projection_cannot_discard_a_life_module_foundation(string metatype)
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        WorkspaceStoredDocument original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationFinalizationProjector.TryProject(
            original, out _, out _, out _, out _, out _, out _, out _));
        WorkspaceStoredDocument mixed = WithPendingFoundation(original, metatype);
        string beforeDigest = mixed.Document.AuxiliaryStateDigest;

        bool projected = CharacterCreationFinalizationProjector.TryProject(
            mixed, out string xml, out var deltas, out var anchors,
            out _, out _, out _, out string[] blockers);

        Assert.IsFalse(projected,
            "A stale cross-method draft is not permission to discard or grant Life Module effects.");
        CollectionAssert.Contains(blockers, "creation-finalization-foundation-draft-not-applicable");
        Assert.AreEqual(string.Empty, xml);
        Assert.IsEmpty(deltas);
        Assert.IsEmpty(anchors);
        Assert.AreEqual(beforeDigest, mixed.Document.AuxiliaryStateDigest);
        Assert.AreEqual(original.ContentRevision, context.Store.Get(context.WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Cross_method_foundation_blocks_review_and_confirmation_without_a_write()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        WorkspaceStoredDocument before = context.Store.Get(context.WorkspaceId).Value!;
        var observedStore = new CommitThenReportUnavailableStore(context.Store)
        {
            ReadTransform = workspace => WithPendingFoundation(workspace, "Human")
        };
        ICharacterCreationFinalizationService finalizer = ReadyContext.BuildFinalizer(
            observedStore, context.Queries, context.Resolver);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> loaded =
            finalizer.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, loaded.Outcome);
        Assert.IsNotNull(loaded.Value);
        Assert.IsFalse(loaded.Value.CanReview);
        CollectionAssert.Contains(loaded.Blockers.ToList(),
            "creation-finalization-foundation-draft-not-applicable");
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> review =
            finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(review.Value);
        Assert.IsFalse(review.Value.CanConfirm);
        Assert.IsNull(review.Value.Plan);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> confirmation =
            finalizer.Confirm(new(
                loaded.Value.Binding,
                review.Value.PreviewDigest,
                CharacterCreationFinalizationDigest.ComputeUtf8("no-projectable-plan"),
                "cross-method-foundation-must-not-finalize",
                ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, confirmation.Outcome);
        Assert.IsNull(confirmation.Value);
        Assert.AreEqual(0, observedStore.AtomicCommitCount);
        WorkspaceStoredDocument after = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
    }

    private static WorkspaceStoredDocument WithPendingFoundation(
        WorkspaceStoredDocument workspace, string metatype)
    {
        // This is deliberately a stale/mixed-method input, not a claim that the
        // Foundation service lets a normal Priority user confirm a Life Module.
        var draft = new CharacterCreationFoundationDraftLedger(
            CharacterCreationFoundationSchemas.DraftLedgerV1,
            workspace.Id,
            DraftRevision: 1,
            BaseContentRevision: workspace.ContentRevision - 1,
            CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(workspace.Document.Content),
            CharacterCreationFinalizationDigest.ComputeUtf8("life-module-source-test"),
            metatype,
            new CharacterCreationFoundationSelection("nationality-test", null),
            [], [], new Dictionary<string, string>(), ["source:life-module-test"],
            CharacterCreationFoundationDraftStatuses.PendingFinalization,
            CharacterEffectsApplied: false,
            DraftDigest: string.Empty);
        draft = draft with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft) };
        return workspace with
        {
            Document = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    AuxiliaryState = workspace.Document.AuxiliaryState with
                    {
                        CharacterCreationFoundationDraft = draft
                    }
                }
            }
        };
    }

    [TestMethod]
    public void Priority_projection_cannot_discard_accepted_life_module_history()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        WorkspaceStoredDocument mixed = PersistAcceptedLifeModuleHistoryWithoutFoundation(context);
        IReadOnlyList<LifeModuleDecisionAcceptance> history = mixed.Document.AuxiliaryState
            .LifeModuleDecisionAcceptances!;
        Assert.IsTrue(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(
            mixed.Id,
            mixed.ContentRevision,
            history),
            "The regression input must be valid accepted history, not malformed foreign data.");

        bool projected = CharacterCreationFinalizationProjector.TryProject(
            mixed, out string xml, out var deltas, out var anchors,
            out _, out _, out _, out string[] blockers);

        Assert.IsFalse(projected);
        CollectionAssert.Contains(blockers,
            CharacterCreationFinalizationBlockers.LifeModuleDecisionHistoryNotApplicable);
        Assert.AreEqual(string.Empty, xml);
        Assert.IsEmpty(deltas);
        Assert.IsEmpty(anchors);
    }

    [TestMethod]
    public void Accepted_life_module_history_blocks_review_and_confirmation_without_a_write()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        WorkspaceStoredDocument before = PersistAcceptedLifeModuleHistoryWithoutFoundation(context);
        var observedStore = new CommitThenReportUnavailableStore(context.Store);
        ICharacterCreationFinalizationService finalizer = ReadyContext.BuildFinalizer(
            observedStore, context.Queries, context.Resolver);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> loaded =
            finalizer.Load(new(before.Id));
        Assert.IsNotNull(loaded.Value);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> review =
            finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(review.Value);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> confirmation =
            finalizer.Confirm(new(
                loaded.Value.Binding,
                review.Value.PreviewDigest,
                review.Value.Plan?.PlanDigest
                ?? CharacterCreationFinalizationDigest.ComputeUtf8("no-projectable-plan"),
                "accepted-life-module-history-must-not-finalize",
                ExplicitlyConfirmed: true));

        WorkspaceStoredDocument after = context.Store.Get(before.Id).Value!;
        Assert.IsNotNull(after.Document.AuxiliaryState.LifeModuleDecisionAcceptances,
            "Priority finalization must not erase accepted Life Module history.");
        Assert.HasCount(1, after.Document.AuxiliaryState.LifeModuleDecisionAcceptances);
        Assert.AreEqual(0, observedStore.AtomicCommitCount);
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, loaded.Outcome);
        Assert.IsFalse(loaded.Value.CanReview);
        CollectionAssert.Contains(loaded.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.LifeModuleDecisionHistoryNotApplicable);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, review.Outcome);
        Assert.IsFalse(review.Value.CanConfirm);
        Assert.IsNull(review.Value.Plan);
        CollectionAssert.Contains(review.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.LifeModuleDecisionHistoryNotApplicable);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Blocked, confirmation.Outcome);
        Assert.IsNull(confirmation.Value);
        CollectionAssert.Contains(confirmation.Blockers.ToList(),
            CharacterCreationFinalizationBlockers.LifeModuleDecisionHistoryNotApplicable);
    }

    private static WorkspaceStoredDocument PersistAcceptedLifeModuleHistoryWithoutFoundation(
        ReadyContext context)
    {
        WorkspaceStoredDocument current = context.Store.Get(context.WorkspaceId).Value!;
        CharacterCreationFoundationDraftLedger foundation = PendingFoundationForTransition(current);
        LifeModuleDecisionAcceptance acceptance = AcceptedLifeModuleHistory(
            current.Id,
            current.ContentRevision);
        WorkspaceDocument withHistory = current.Document with
        {
            State = current.Document.State with
            {
                AuxiliaryState = current.Document.AuxiliaryState with
                {
                    CharacterCreationFoundationDraft = foundation,
                    LifeModuleDecisionAcceptances = [acceptance]
                }
            }
        };
        WorkspaceStoreMutationResult accepted = context.Store
            .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                current.Id,
                current.ContentRevision,
                current.Document.AuxiliaryStateDigest,
                withHistory);
        Assert.IsTrue(accepted.Success, accepted.Error);

        WorkspaceStoredDocument persisted = context.Store.Get(current.Id).Value!;
        WorkspaceDocument withoutFoundation = persisted.Document with
        {
            State = persisted.Document.State with
            {
                AuxiliaryState = persisted.Document.AuxiliaryState with
                {
                    CharacterCreationFoundationDraft = null
                }
            }
        };
        WorkspaceStoreMutationResult cleared = context.Store
            .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                persisted.Id,
                persisted.ContentRevision,
                persisted.Document.AuxiliaryStateDigest,
                withoutFoundation);
        Assert.IsTrue(cleared.Success, cleared.Error);

        WorkspaceStoredDocument result = context.Store.Get(current.Id).Value!;
        Assert.IsNull(result.Document.AuxiliaryState.CharacterCreationFoundationDraft);
        return result;
    }

    private static CharacterCreationFoundationDraftLedger PendingFoundationForTransition(
        WorkspaceStoredDocument workspace)
    {
        var draft = new CharacterCreationFoundationDraftLedger(
            CharacterCreationFoundationSchemas.DraftLedgerV1,
            workspace.Id,
            DraftRevision: 1,
            BaseContentRevision: workspace.ContentRevision,
            CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(
                workspace.Document.Content),
            CharacterCreationFinalizationDigest.ComputeUtf8("life-module-source-transition-test"),
            "Human",
            new CharacterCreationFoundationSelection("nationality-transition-test", null),
            [], [], new Dictionary<string, string>(), ["source:life-module-transition-test"],
            CharacterCreationFoundationDraftStatuses.PendingFinalization,
            CharacterEffectsApplied: false,
            DraftDigest: string.Empty);
        return draft with
        {
            DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft)
        };
    }

    private static LifeModuleDecisionAcceptance AcceptedLifeModuleHistory(
        CharacterWorkspaceId workspaceId,
        long previousWorkspaceRevision)
    {
        const string decisionId = "accepted-life-module-decision";
        const string sourceAnchor = "lifemodules.xml#module:accepted-history-test";
        string Digest(string value) =>
            LifeModuleDecisionAcceptanceIntegrity.ComputeCanonicalDigest(value);
        var fact = new OriginCanonicalNarrativeFact(
            "accepted-life-module-fact",
            "accepted-life-module",
            "Accepted Life Module history.",
            decisionId,
            [sourceAnchor],
            string.Empty);
        fact = fact with
        {
            FactDigest = LifeModuleDecisionAcceptanceIntegrity.ComputeCanonicalDigest(
                fact with { FactDigest = string.Empty })
        };
        string contentDigest = Digest("accepted-life-module-content");
        string sourceDigest = Digest("accepted-life-module-source");
        string rulesDigest = Digest("accepted-life-module-rules");
        string runtimeDigest = Digest("accepted-life-module-runtime");
        string graphDigest = Digest("accepted-life-module-graph");
        string mechanicsDigest = Digest("accepted-life-module-mechanics");
        var terminal = new LifeModuleDecisionAuthorityStep(
            OriginDossierSchemas.DecisionAuthorityStepV1,
            RulesetDefaults.Sr5,
            workspaceId.Value,
            previousWorkspaceRevision + 1,
            "local-single-user",
            "runner-accepted-history",
            "Accepted History Runner",
            "en-US",
            "sr5-life-modules-foundation",
            "nationality-accepted",
            1,
            "turn-accepted-history",
            2,
            "Accepted Life Module history.",
            "Continue character creation.",
            [],
            [fact],
            [decisionId],
            Digest("accepted-life-module-previous-turn"),
            graphDigest,
            Digest("accepted-life-module-decision-step"),
            contentDigest,
            sourceDigest,
            rulesDigest,
            runtimeDigest,
            mechanicsDigest)
        {
            IsTerminal = true
        };
        var receipt = new LifeModuleAcceptedDecisionReceipt(
            OriginDossierSchemas.AcceptedDecisionReceiptV1,
            decisionId,
            "accepted-life-module-choice",
            Digest("accepted-life-module-command"),
            Digest("accepted-life-module-idempotency"),
            previousWorkspaceRevision,
            previousWorkspaceRevision + 1,
            Digest("accepted-life-module-previous-content"),
            contentDigest,
            sourceDigest,
            rulesDigest,
            runtimeDigest,
            Digest("accepted-life-module-previous-decision"),
            Digest("accepted-life-module-previous-mechanics"),
            graphDigest,
            mechanicsDigest,
            "Accepted Life Module history.",
            [fact],
            string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = LifeModuleDecisionAcceptanceIntegrity.ComputeReceiptDigest(receipt)
        };
        return new LifeModuleDecisionAcceptance(receipt, terminal);
    }

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
        public Func<WorkspaceStoredDocument, WorkspaceStoredDocument>? ReadTransform { get; init; }
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
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => Transform(_inner.Get(id));
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => Transform(_inner.Get(owner, id));
        private WorkspaceStoreReadResult Transform(WorkspaceStoreReadResult result) =>
            result.Value is { } value && ReadTransform is { } transform
                ? result with { Value = transform(value) }
                : result;
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
