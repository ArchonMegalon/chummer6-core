using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CharacterCreationCompletionFinalizationProjectorTests
{
    [TestMethod]
    public void Whole_projection_replays_real_saved_choices_preserves_grants_and_reports_unused_pools_without_writing()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var files = Files(context);
        string original = JsonSerializer.Serialize(fixture.Original);
        var result = Project(fixture);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var projection = result.Value!;
        var root = XDocument.Parse(projection.CharacterXml).Root!;
        Assert.AreEqual("True", root.Element("created")!.Value);
        Assert.IsNull(root.Element(CharacterCreationBootstrapXml.MarkerElement));
        Assert.AreEqual(fixture.Review.Balances.RemainingCreationKarma!.Value, projection.KarmaBeforeCarryover);
        Assert.AreEqual(fixture.Review.Balances.RemainingNuyen!.Value, projection.NuyenBeforeCarryover);
        Assert.AreEqual(projection.CarriedNuyen + fixture.Cash.Dice * 3 * fixture.Cash.Multiplier, projection.NuyenRemaining);
        Assert.AreEqual(projection.NuyenRemaining, decimal.Parse(root.Element("nuyen")!.Value, CultureInfo.InvariantCulture));
        Assert.AreEqual(fixture.Review.Lifestyles!.Gear.Resources.Budget.TotalStartingNuyen,
            decimal.Parse(root.Element("startingnuyen")!.Value, CultureInfo.InvariantCulture));
        Assert.AreEqual(fixture.Review.Request.Selections.TotalKarmaInvestment,
            int.Parse(root.Element("nuyenbp")!.Value, CultureInfo.InvariantCulture));
        Assert.AreEqual(1, root.Element("contacts")!.Elements("contact").Count());
        Assert.AreEqual(1, root.Element("lifestyles")!.Elements("lifestyle").Count());
        Assert.IsTrue(root.Element("gears")!.HasElements);
        Assert.IsTrue(root.Element("qualities")!.HasElements);
        Assert.IsTrue(root.Element("spells")!.HasElements);
        Assert.IsTrue(root.Element("powers")!.HasElements);
        Assert.AreEqual(1, projection.OrderedDeltas.Count(row => row.DeltaId == "starting-cash:nuyen"));
        Assert.AreEqual(fixture.Review.Skills!.Attributes.NormalPointBudget.Remaining,
            (decimal)projection.UnusedPoints.NormalAttributes);
        Assert.IsTrue(projection.UnusedPoints.NormalAttributes > 0,
            "Legally unused pools must be disclosed, not turned into a new finalization blocker.");
        foreach (var skill in fixture.Review.Skills.Skills)
        {
            string container = skill.Kind == CharacterCreationSkillKinds.Knowledge ? "knoskills" : "skills";
            var saved = root.Element("newskills")!.Element(container)!.Elements("skill")
                .Single(row => row.Element("suid")!.Value == skill.SourceSkillId);
            Assert.AreEqual(skill.IsNativeLanguage ? 0 : skill.EffectiveRating.GetValueOrDefault() - skill.GrantedRating,
                int.Parse(saved.Element("base")!.Value, CultureInfo.InvariantCulture));
        }
        Assert.AreEqual(projection.ExpectedResultRawCharacterXmlDigest,
            CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(projection.CharacterXml));
        Assert.AreEqual(CharacterCreationCompletionFinalizationRules.ComputeProjectionDigest(projection), projection.ProjectionDigest);
        var repeated = Project(fixture);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, repeated.Outcome, string.Join(",", repeated.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(projection), JsonSerializer.Serialize(repeated.Value));
        int fullReviews = 0;
        using var listener = ListenForFullReviews(() => fullReviews++);
        // The internal prepared path must produce the identical projection, not
        // accept a caller's evaluated rows or manufacture a different authority.
        Assert.IsTrue(CharacterCreationCompletionTransaction.TryAdmit(context.Store, OwnerScope.LocalSingleUser,
            fixture.Original, context.Resolver, context.Queries, fixture.Review.Request,
            new(fixture.Cash.AuthorityDigest, fixture.Cash.Dice * 3), default,
            out var authority, out var prepared, out var blockers), string.Join(",", blockers));
        Assert.AreEqual(1, fullReviews, "One admission must not repeat its full review during witness capture.");
        Assert.AreEqual(JsonSerializer.Serialize(projection), JsonSerializer.Serialize(prepared));
        Assert.IsNotNull(authority);
        Assert.AreEqual(CharacterCreationCompletionAuthority.SchemaV3, authority.Schema);
        Assert.IsNull(authority.Inputs);
        Assert.IsNull(authority.BootstrapSourceSnapshot);
        Assert.IsNotNull(authority.SelectedReplay);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(authority.SelectedReplay,
            out var archivedBootstrap, out var witness));
        Assert.IsNotNull(archivedBootstrap);
        Assert.IsNotNull(witness);
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Bootstrap), JsonSerializer.Serialize(archivedBootstrap));
        Assert.AreEqual(fixture.Inputs.SnapshotDigest, witness.ProvenanceInputsSnapshotDigest);
        Assert.AreEqual(fixture.Review.ReviewDigest, witness.ProvenanceReviewDigest);
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Inputs.Skills), JsonSerializer.Serialize(witness.Skills));
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Review.Request), JsonSerializer.Serialize(authority.Proposal));
        var independent = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Review.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, independent.Outcome, string.Join(",", independent.Blockers));
        Assert.AreEqual(2, fullReviews, "Standalone Capture must still perform its own full review.");
        Assert.AreEqual(JsonSerializer.Serialize(independent.Value), JsonSerializer.Serialize(witness));
        Assert.IsTrue(CharacterCreationCompletionTransaction.TryAdmit(context.Store, OwnerScope.LocalSingleUser,
            fixture.Original, context.Resolver, context.Queries, fixture.Review.Request,
            new(fixture.Cash.AuthorityDigest, fixture.Cash.Dice * 3), default,
            out var freshAuthority, out var freshProjection, out var freshBlockers), string.Join(",", freshBlockers));
        Assert.AreEqual(3, fullReviews, "A later admission must review afresh even with identical inputs.");
        Assert.AreEqual(JsonSerializer.Serialize(authority), JsonSerializer.Serialize(freshAuthority));
        Assert.AreEqual(JsonSerializer.Serialize(prepared), JsonSerializer.Serialize(freshProjection));
        // Reprice decoded historical inputs independently. These are selected
        // witnesses, not a fabricated complete input/review envelope.
        var historical = CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original,
            archivedBootstrap, witness, authority.Proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, historical.Outcome,
            string.Join(",", historical.Blockers));
        Assert.IsNotNull(historical.Value);
        var replayed = ProjectSelected(fixture.Original, archivedBootstrap, witness,
            historical.Value, authority.Proposal, authority.StartingCashSource, authority.StartingCash);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replayed.Outcome,
            string.Join(",", replayed.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(projection), JsonSerializer.Serialize(replayed.Value));
        Assert.IsFalse(CharacterCreationCompletionTransaction.TryAdmit(context.Store, OwnerScope.LocalSingleUser,
            fixture.Original, context.Resolver, context.Queries,
            fixture.Review.Request with { InputsSnapshotDigest = "sha256:" + new string('0', 64) },
            new(fixture.Cash.AuthorityDigest, fixture.Cash.Dice * 3), default,
            out _, out var staleProjection, out var staleBlockers));
        Assert.IsNull(staleProjection);
        Assert.AreEqual(4, fullReviews);
        CollectionAssert.Contains(staleBlockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        Assert.AreEqual(original, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        AssertFiles(context, files);
    }

    [TestMethod]
    public void Standalone_capture_revalidates_malformed_stale_and_blocked_proposals_and_cancellation()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var files = Files(context);
        int fullReviews = 0;
        using var listener = ListenForFullReviews(() => fullReviews++);
        var invalidDigest = "sha256:" + new string('0', 64);
        foreach (var (inputs, request) in new[]
        {
            (fixture.Inputs, fixture.Review.Request with { Selections = null! }),
            (fixture.Inputs, fixture.Review.Request with { InputsSnapshotDigest = invalidDigest }),
            (fixture.Inputs with { SnapshotDigest = invalidDigest }, fixture.Review.Request),
            (fixture.Inputs, fixture.Review.Request with
            {
                Selections = fixture.Review.Request.Selections with { TotalKarmaInvestment = int.MaxValue }
            }),
            (fixture.Inputs, fixture.Review.Request with
            {
                Selections = fixture.Review.Request.Selections with
                {
                    MagicSelections = fixture.Review.Request.Selections.MagicSelections with
                    { MysticAdeptPowerPoints = int.MaxValue }
                }
            })
        })
        {
            var expected = CharacterCreationCompletionReviewService.Review(inputs, request);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, expected.Outcome);
            var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
                inputs, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, captured.Outcome);
            Assert.IsNull(captured.Value);
            CollectionAssert.AreEqual(expected.Blockers.ToArray(), captured.Blockers.ToArray());
        }
        Assert.AreEqual(5, fullReviews);
        Assert.Throws<OperationCanceledException>(() => CharacterCreationSelectedCompletionEvaluator.Capture(
            fixture.Original, fixture.Bootstrap, fixture.Inputs, fixture.Review.Request, new CancellationToken(true)));
        Assert.AreEqual(5, fullReviews, "Pre-canceled capture must not start a review.");
        var valid = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Review.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, valid.Outcome, string.Join(",", valid.Blockers));
        Assert.AreEqual(6, fullReviews);
        AssertFiles(context, files);
    }

    [TestMethod]
    public void Reviewed_capture_owns_its_review_and_still_rejects_a_different_original()
    {
        using var context = ReadyContext.Create(true);
        var fixture = Capture(context);
        var files = Files(context);
        var evaluated = CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.Evaluate(
            fixture.Inputs, fixture.Review.Request, default);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, evaluated.Outcome, string.Join(",", evaluated.Blockers));
        Assert.IsNotNull(evaluated.Value);
        var exposed = evaluated.Value.ReviewSnapshot;
        ((ICollection<string>)exposed.Blockers).Add("mutated-review-copy");
        Assert.AreEqual(0, evaluated.Value.ReviewSnapshot.Blockers.Count);
        var wrongOriginal = fixture.Original with { ContentRevision = fixture.Original.ContentRevision + 1 };
        var rejected = evaluated.Value.Capture(wrongOriginal, fixture.Bootstrap, default);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationSelectedCompletionEvaluator.HistoryMismatch);
        var captured = evaluated.Value.Capture(fixture.Original, fixture.Bootstrap, default);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var independent = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Review.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, independent.Outcome);
        Assert.AreEqual(JsonSerializer.Serialize(independent.Value), JsonSerializer.Serialize(captured.Value));
        AssertFiles(context, files);
    }

    private static ActivityListener ListenForFullReviews(Action onReview)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                // The same source also emits archive metrics; count only full reviews.
                if (activity.OperationName != "Review") return;
                Assert.IsFalse(activity.TagObjects.Any(), "Review diagnostics must not contain character or source data.");
                onReview();
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [TestMethod]
    public void Completion_archive_retains_genuine_v2_replay_and_rejects_resealed_mixed_schema_shapes()
    {
        using var context = ReadyContext.Create(true);
        var fixture = Capture(context);
        var files = Files(context);
        var fullEnvelope = CharacterCreationCompletionFinalizationProjector.ReviewAndProject(
            fixture.Original, fixture.Bootstrap, fixture.Inputs, fixture.Review.Request,
            _ => fixture.Cash, new(fixture.Cash.AuthorityDigest, fixture.Cash.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, fullEnvelope.Outcome,
            string.Join(",", fullEnvelope.Blockers));
        Assert.IsNotNull(fullEnvelope.Value);
        Assert.IsNull(fullEnvelope.Value.Witness,
            "Historical V2 full envelopes must not acquire selected-witness validation requirements.");
        var projected = Project(fixture);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, projected.Outcome,
            string.Join(",", projected.Blockers));
        Assert.IsNotNull(projected.Value);
        var projection = projected.Value;
        Assert.AreEqual(CharacterCreationCompletionFinalizationSchemas.ProjectionV2, projection.Schema);
        Assert.IsNull(projection.SkillGroupOrderAuthority);
        Assert.IsNull(fixture.Inputs.Skills.KarmaCompensationSourceOrder);
        Assert.IsFalse(JsonSerializer.SerializeToElement(projection).TryGetProperty("SkillGroupOrderAuthority", out _));
        Assert.IsFalse(JsonSerializer.SerializeToElement(fixture.Inputs.Skills).TryGetProperty("KarmaCompensationSourceOrder", out _));
        Assert.AreEqual(JsonSerializer.Serialize(projection), JsonSerializer.Serialize(fullEnvelope.Value.Projection));
        var authority = new CharacterCreationCompletionAuthority(CharacterCreationCompletionAuthority.SchemaV2,
            fixture.Original.Document.Content, JsonSerializer.SerializeToElement(fixture.Bootstrap), fixture.Inputs,
            fixture.Review.Request, fixture.Cash, projection.StartingCash.Choice, string.Empty);
        authority = authority with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(authority) };
        // Retain the real pre-V3 envelope through a cold typed round trip. Its
        // catalog authorities are genuine captures, never selected stand-ins.
        string serialized = JsonSerializer.Serialize(authority);
        var cold = JsonSerializer.Deserialize<CharacterCreationCompletionAuthority>(serialized);
        Assert.IsNotNull(cold);
        Assert.AreEqual(serialized, JsonSerializer.Serialize(cold));
        Assert.IsNull(cold.SelectedReplay);
        Assert.IsNotNull(cold.Inputs);
        Assert.IsNotNull(cold.BootstrapSourceSnapshot);
        var receipt = Receipt(fixture, projection, cold);
        var archive = new CharacterCreationFinalizationArchive(fixture.Original.Document.AuxiliaryState)
            { CompletionAuthority = cold };
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(fixture.Original.Id, archive, receipt));
        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(fixture.Original.Id,
            receipt.ContentRevision, archive, [new(receipt.IdempotencyKeyDigest, receipt.CommandDigest, receipt)]));

        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Review.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome,
            string.Join(",", captured.Blockers));
        Assert.IsNotNull(captured.Value);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(fixture.Bootstrap, captured.Value, out var replay));
        var selectedAuthority = cold with { Schema = CharacterCreationCompletionAuthority.SchemaV3,
            Inputs = null, BootstrapSourceSnapshot = null, SelectedReplay = replay };
        selectedAuthority = selectedAuthority with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(selectedAuthority) };
        var selectedReceipt = Receipt(fixture, projection, selectedAuthority);
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(fixture.Original.Id,
            archive with { CompletionAuthority = selectedAuthority }, selectedReceipt));
        var selectedProjection = CharacterCreationCompletionTransaction.ProjectSelected(fixture.Original,
            fixture.Bootstrap, captured.Value, fixture.Review.Request, fixture.Cash, projection.StartingCash.Choice);
        Assert.AreEqual(JsonSerializer.Serialize(projection), JsonSerializer.Serialize(selectedProjection.Value));
        // Historical no-order envelopes keep both their old byte shape and later
        // revision admission. Do not retrofit the V4 native identity requirement.
        foreach (var historical in new[] { (Authority: cold, Receipt: receipt),
                     (Authority: selectedAuthority, Receipt: selectedReceipt) })
        {
            var state = new WorkspaceDocumentAuxiliaryState(
                CharacterCreationFinalizationReceipts: [new(historical.Receipt.IdempotencyKeyDigest,
                    historical.Receipt.CommandDigest, historical.Receipt)],
                CharacterCreationFinalizationArchive: archive with { CompletionAuthority = historical.Authority });
            var native = new WorkspaceDocument(projection.CharacterXml, "sr5");
            native = native with { State = native.State with { AuxiliaryState = state } };
            Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidCurrentDocument(native, historical.Receipt.ContentRevision));
            var edited = XDocument.Parse(native.Content);
            foreach (var id in edited.Root!.Element("newskills")!.Descendants()
                         .Where(row => row.Name == "guid" || row.Name == "id"))
                id.Value = Guid.NewGuid().ToString("D");
            var later = native with { State = native.State with { Payload = edited.ToString(SaveOptions.DisableFormatting) } };
            Assert.IsFalse(CharacterCreationCompletionTransaction.IsValidCurrentDocument(later, historical.Receipt.ContentRevision));
            Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidCurrentDocument(later, historical.Receipt.ContentRevision + 1));
            Assert.AreEqual(JsonSerializer.Serialize(state), JsonSerializer.Serialize(later.AuxiliaryState));
        }
        // V4 cannot reinterpret genuine no-order history under new result IDs.
        var missingOrder = selectedAuthority with { Schema = CharacterCreationCompletionAuthority.SchemaV4 };
        missingOrder = missingOrder with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(missingOrder) };
        Assert.IsFalse(CharacterCreationCompletionTransaction.IsValidArchive(fixture.Original.Id,
            archive with { CompletionAuthority = missingOrder }, Receipt(fixture, projection, missingOrder)));
        foreach (var mixed in new[]
        {
            cold with { SelectedReplay = replay },
            cold with { Schema = CharacterCreationCompletionAuthority.SchemaV3, SelectedReplay = replay }
        })
        {
            var resealed = mixed with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(mixed) };
            var changedReceipt = Receipt(fixture, projection, resealed);
            var changedArchive = archive with { CompletionAuthority = resealed };
            Assert.IsFalse(CharacterCreationCompletionTransaction.IsValidArchive(fixture.Original.Id,
                changedArchive, changedReceipt), mixed.Schema);
            Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(fixture.Original.Id,
                changedReceipt.ContentRevision, changedArchive,
                [new(changedReceipt.IdempotencyKeyDigest, changedReceipt.CommandDigest, changedReceipt)]), mixed.Schema);
        }
        AssertFiles(context, files);
    }

    [TestMethod]
    public void Whole_projection_rejects_resealed_result_wrong_original_and_wrong_bootstrap_without_writing()
    {
        using var context = ReadyContext.Create(true);
        var fixture = Capture(context);
        var files = Files(context);
        var changedReview = fixture.Review with
        {
            Balances = fixture.Review.Balances with { RemainingCreationKarma = fixture.Review.Balances.RemainingCreationKarma + 100 }
        };
        changedReview = changedReview with { ReviewDigest = CharacterCreationCompletionReviewRules.ComputeReviewDigest(changedReview) };
        var forged = Project(fixture with { Review = changedReview });
        Assert.IsNull(forged.Value);
        CollectionAssert.Contains(forged.Blockers.ToArray(), CharacterCreationCompletionFinalizationSchemas.ReviewConflict);
        var wrongRevision = Project(fixture with
        {
            Original = fixture.Original with { ContentRevision = fixture.Original.ContentRevision + 1 }
        });
        Assert.IsNull(wrongRevision.Value);
        CollectionAssert.Contains(wrongRevision.Blockers.ToArray(), CharacterCreationCompletionFinalizationSchemas.ObservationConflict);
        var changedDocument = fixture.Original.Document with
        {
            State = fixture.Original.Document.State with { Payload = fixture.Original.Document.Content + " " }
        };
        Assert.IsNull(Project(fixture with { Original = fixture.Original with { Document = changedDocument } }).Value);
        var wrongBootstrap = fixture.Bootstrap with { RawCharacterXmlDigest = "sha256:" + new string('0', 64) };
        wrongBootstrap = wrongBootstrap with { SnapshotDigest = CharacterCreationCompletionBootstrapSnapshot.ComputeDigest(wrongBootstrap) };
        Assert.IsNull(Project(fixture with { Bootstrap = wrongBootstrap }).Value);
        AssertFiles(context, files);
    }

    [TestMethod]
    public void Whole_projection_requires_source_bound_cash_and_preserves_cancellation_and_free_street_fallback()
    {
        using var context = ReadyContext.Create(true);
        var fixture = Capture(context);
        var files = Files(context);
        var missing = CharacterCreationCompletionFinalizationProjector.Project(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Review, fixture.Cash, null);
        CollectionAssert.Contains(missing.Blockers.ToArray(), CharacterCreationFinalizationBlockers.StartingCashChoiceRequired);
        foreach (var choice in new[]
        {
            new CharacterCreationStartingCashChoice(fixture.Cash.AuthorityDigest, fixture.Cash.Dice * 6 + 1),
            new CharacterCreationStartingCashChoice("sha256:" + new string('0', 64), fixture.Cash.Dice * 3)
        })
        {
            var rejected = CharacterCreationCompletionFinalizationProjector.Project(fixture.Original, fixture.Bootstrap,
                fixture.Inputs, fixture.Review, fixture.Cash, choice);
            Assert.IsNull(rejected.Value);
            CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid);
        }
        var valid = Project(fixture);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, valid.Outcome, string.Join(",", valid.Blockers));
        Assert.AreEqual("Street", valid.Value!.StartingCash.Source.Name);
        Assert.AreEqual(1, XDocument.Parse(valid.Value.CharacterXml).Root!.Element("lifestyles")!.Elements("lifestyle").Count());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => CharacterCreationCompletionFinalizationProjector.Project(
            fixture.Original, fixture.Bootstrap, fixture.Inputs, fixture.Review, fixture.Cash,
            new(fixture.Cash.AuthorityDigest, fixture.Cash.Dice * 3), canceled.Token));
        AssertFiles(context, files);
    }


    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete373_Selected_graph_matches_canonical_effects_without_cash_save_or_career(bool savedChoices)
    {
        using var context = savedChoices
            ? CharacterCreationCompletionReviewServiceTests.CreateSavedContext()
            : ReadyContext.Create(true);
        var fixture = Capture(context);
        var files = Files(context);
        string original = JsonSerializer.Serialize(fixture.Original);
        var finalized = Project(fixture);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, finalized.Outcome,
            string.Join(",", finalized.Blockers));
        var projection = finalized.Value!;
        var expected = XDocument.Parse(projection.CharacterXml).Root!;
        Assert.IsTrue(CharacterCreationSkillsService.TryCaptureCompletionGroupOrderAuthority(
            fixture.Inputs.Skills, out var groupOrder));
        var graph = SelectedGraph373(fixture,
            groupOrder is null ? fixture.Review.ReviewDigest : projection.Binding.AuthorityDigest);
        var document = XDocument.Parse(fixture.Original.Document.Content);
        var before = new XDocument(document);
        var deltas = new List<CharacterCreationFinalizationDelta>();
        int order = 0;
        Assert.IsNull(CharacterCreationCompletionFinalizationProjector.CompileSelectedGraph(
            document, graph, deltas, ref order));
        Assert.IsFalse(bool.Parse(document.Root!.Element("created")!.Value));
        foreach (string name in new[] { "karma", "nuyen", "startingnuyen", CharacterCreationBootstrapXml.MarkerElement })
            Assert.IsTrue(XNode.DeepEquals(before.Root!.Element(name), document.Root.Element(name)), name);
        foreach (string name in new[] { "attributes", "skills", "qualities", "gears",
                     "contacts", "spells", "powers", "improvements" })
            Assert.IsTrue(XNode.DeepEquals(expected.Element(name), document.Root.Element(name)), name);
        if (savedChoices)
        {
            Assert.IsTrue(document.Root.Element("lifestyles")!.HasElements);
            Assert.IsTrue(XNode.DeepEquals(expected.Element("lifestyles"), document.Root.Element("lifestyles")));
        }
        else
            Assert.IsFalse(document.Root.Element("lifestyles")?.HasElements == true,
                "Assessment must not invent the finalization-only starting-cash lifestyle.");
        Assert.IsTrue(deltas.Count > 0);
        Assert.AreEqual(deltas.Count, order);
        foreach (var delta in deltas)
        {
            Assert.AreNotEqual(CharacterCreationFinalizationDeltaKinds.Lifecycle, delta.Kind);
            Assert.IsFalse(delta.DeltaId.StartsWith("starting-cash:", StringComparison.Ordinal));
            Assert.IsFalse(delta.DeltaId.StartsWith("carryover:", StringComparison.Ordinal));
            var canonical = projection.OrderedDeltas.Single(row => row.DeltaId == delta.DeltaId);
            Assert.AreEqual(JsonSerializer.Serialize(canonical),
                JsonSerializer.Serialize(delta with { Order = canonical.Order }), delta.DeltaId);
        }
        Assert.AreEqual(original, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        AssertFiles(context, files);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete373_Selected_graph_rejects_created_or_precanceled_input_without_mutation(bool cancel)
    {
        using var context = ReadyContext.Create(true);
        var fixture = Capture(context);
        var files = Files(context);
        var document = XDocument.Parse(fixture.Original.Document.Content);
        if (!cancel) document.Root!.SetElementValue("created", "True");
        string before = document.ToString(SaveOptions.DisableFormatting);
        var graph = SelectedGraph373(fixture, fixture.Review.ReviewDigest);
        var deltas = new List<CharacterCreationFinalizationDelta>();
        int order = 0;
        if (cancel)
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                CharacterCreationCompletionFinalizationProjector.CompileSelectedGraph(
                    document, graph, deltas, ref order, canceled.Token));
        }
        else
            Assert.AreEqual(CharacterCreationFinalizationBlockers.CharacterAlreadyCreated,
                CharacterCreationCompletionFinalizationProjector.CompileSelectedGraph(
                    document, graph, deltas, ref order));
        Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting));
        Assert.AreEqual(0, deltas.Count);
        Assert.AreEqual(0, order);
        AssertFiles(context, files);
    }

    private static CharacterCreationCompletionFinalizationProjector.SelectedGraphInputs SelectedGraph373(
        Fixture fixture, string instanceSeed)
    {
        var inputs = fixture.Inputs;
        var review = fixture.Review;
        var lifestyles = review.Lifestyles!;
        var gear = lifestyles.Gear;
        var qualities = review.UpstreamBudget!.Qualities.Qualities;
        return new(inputs.Skills.PrerequisiteDraft, review.Skills!,
            inputs.Skills.SelectedActiveSkillPoints, inputs.Skills.SelectedSkillGroupPoints,
            new(qualities.Selections, qualities.KarmaRemaining, qualities.SourceAnchorIds),
            new(gear.Lines, gear.Budget, gear.SourceAnchorIds),
            new(lifestyles.Authority, lifestyles.Lifestyles, lifestyles.StartingLifestyleId, lifestyles.SourceAnchorIds),
            new(inputs.Contacts.Policy, review.Request.Selections.Contacts,
                inputs.Contacts.Qualities.Authority.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
                inputs.Contacts.Qualities.Authority.QualityKarmaLimit,
                inputs.Contacts.Qualities.Authority.MayExceedPositiveQualityLimit, review.Contacts!.GroupKarma),
            new CharacterCreationCompletionFinalizationProjector.FullAwakenedGraphInputs(
                inputs.Magic, review.UpstreamBudget.Magic),
            review.ReviewDigest, instanceSeed)
        {
            Loadout = review.Loadout,
            AmmunitionAssignments = review.Request.Selections.AmmunitionAssignments,
            OriginalLoadoutInstances = fixture.Original.Document.AuxiliaryState.CharacterCreationLoadoutDraft?.Instances
        };
    }

    private sealed record Fixture(WorkspaceStoredDocument Original, CharacterCreationCompletionBootstrapSnapshot Bootstrap,
        CharacterCreationCompletionReviewInputs Inputs, CharacterCreationCompletionReview Review,
        CharacterCreationStartingNuyenSource Cash);

    [TestMethod]
    public void Completion_bootstrap_captures_only_binding_authorities_with_a_distinct_schema_and_digest()
    {
        using var context = ReadyContext.Create(true);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(CharacterCreationBootstrapSourceSnapshot.TryCapture(source, original.Document.Content, out var activation));
        var bindingOnly = new BindingOnlyContext(source);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(bindingOnly, original.Document.Content, out var completion));
        Assert.IsTrue(completion.IsValid());
        Assert.AreNotEqual(activation.SnapshotDigest, completion.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(activation.SourceProfile), JsonSerializer.Serialize(completion.SourceProfile));
        Assert.AreEqual(JsonSerializer.Serialize(activation.Metatypes), JsonSerializer.Serialize(completion.Metatypes));
        Assert.AreEqual(JsonSerializer.Serialize(activation.Prerequisite), JsonSerializer.Serialize(completion.Prerequisite));
        Assert.IsTrue(CharacterCreationBootstrapAuthority.TryPrepareBinding(original.Id, original.Document,
            completion.CreateFrozenContext(), out var binding, out _, out _));
        Assert.AreEqual(JsonSerializer.Serialize(original.Document.AuxiliaryState.CharacterCreationBootstrapBinding),
            JsonSerializer.Serialize(binding));
        Assert.IsFalse(completion.CreateFrozenContext().TryResolveCreationQualitiesAuthority(out _));
        Assert.IsFalse(completion.CreateFrozenContext().TryResolveCreationMagicResonanceAuthority(out _));
        var element = JsonSerializer.SerializeToElement(completion);
        Assert.IsFalse(element.TryGetProperty("Qualities", out _));
        Assert.IsFalse(element.TryGetProperty("MagicResonance", out _));
        Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(completion).Length
            < JsonSerializer.SerializeToUtf8Bytes(activation).Length / 2);
        Assert.IsFalse(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            new BindingOnlyContext(source, rejectPrerequisite: true), original.Document.Content, out _));
    }

    [TestMethod]
    public void Completion_bootstrap_rejects_schema_digest_and_resealed_profile_drift_before_projection()
    {
        using var context = ReadyContext.Create(true);
        var fixture = Capture(context);
        var files = Files(context);
        var wrongSchema = fixture.Bootstrap with { Schema = "unknown" };
        wrongSchema = wrongSchema with { SnapshotDigest = CharacterCreationCompletionBootstrapSnapshot.ComputeDigest(wrongSchema) };
        Assert.IsFalse(wrongSchema.IsValid());
        Assert.IsNull(Project(fixture with { Bootstrap = wrongSchema }).Value);
        Assert.IsNull(Project(fixture with { Bootstrap = fixture.Bootstrap with
        { SnapshotDigest = "sha256:" + new string('0', 64) } }).Value);
        var changedProfile = fixture.Bootstrap with { SourceProfile = fixture.Bootstrap.SourceProfile with
        { RawProfileInputsDigest = "sha256:" + new string('0', 64) } };
        changedProfile = changedProfile with { SnapshotDigest = CharacterCreationCompletionBootstrapSnapshot.ComputeDigest(changedProfile) };
        Assert.IsTrue(changedProfile.IsValid(), "A self-consistent envelope is not proof of source or binding admission.");
        Assert.IsNull(Project(fixture with { Bootstrap = changedProfile }).Value);
        AssertFiles(context, files);
    }

    private sealed class BindingOnlyContext(ICharacterSourceDataContext source, bool rejectPrerequisite = false)
        : ICharacterSourceDataContext
    {
        public bool TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority authority)
            => source.TryResolveCreationSourceProfile(out authority);
        public bool TryResolveCreationMetatypeCatalog(out CharacterCreationMetatypeCatalogAuthority authority)
            => source.TryResolveCreationMetatypeCatalog(out authority);
        public bool TryResolveCreationPrerequisiteAuthority(out CharacterCreationPrerequisiteAuthority authority)
        {
            bool resolved = source.TryResolveCreationPrerequisiteAuthority(out authority);
            return resolved && !rejectPrerequisite;
        }
        public bool TryResolveCreationQualitiesAuthority(out CharacterCreationQualitiesAuthority authority)
            => throw new AssertFailedException("Binding capture must not load activation-only qualities.");
        public bool TryResolveCreationMagicResonanceAuthority(out CharacterCreationMagicResonanceAuthority authority)
            => throw new AssertFailedException("Binding capture must not load activation-only magic.");
        public bool TryResolveCyberwareGradeDeviceRating(string gradeName, string improvementSource, out int deviceRating)
        { deviceRating = 0; return false; }
        public bool TryResolveVehicleModBonuses(string sourceId, string name, out CharacterVehicleModSourceBonuses bonuses)
        { bonuses = CharacterVehicleModSourceBonuses.Empty; return false; }
    }

    private static Fixture Capture(ReadyContext context)
    {
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(source, original.Document.Content, out var bootstrap));
        CharacterCreationStartingNuyenSource? cash;
        if (choices.StartingLifestyleId is Guid selected)
        {
            var home = choices.Lifestyles.Single(row => row.LifestyleId == selected);
            var option = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId);
            Assert.IsTrue(source.TryResolveCreationPriorityStartingNuyen(option.SourceId, out cash));
        }
        else Assert.IsTrue(source.TryResolveCreationDefaultStartingNuyen(out cash));
        Assert.IsNotNull(cash);
        return new(original, bootstrap, inputs, review.Value!, cash!);
    }
    private static CharacterCreationFoundationResult<CharacterCreationCompletionFinalizationProjection> Project(Fixture value)
        => CharacterCreationCompletionFinalizationProjector.Project(value.Original, value.Bootstrap, value.Inputs,
            value.Review, value.Cash, new(value.Cash.AuthorityDigest, value.Cash.Dice * 3));

    private static CharacterCreationFoundationResult<CharacterCreationCompletionFinalizationProjection> ProjectSelected(
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationSelectedCompletionEvaluation evaluated,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingNuyenSource cashSource,
        CharacterCreationStartingCashChoice cashChoice)
    {
        Assert.IsNotNull(evaluated.Balances.RemainingCreationKarma);
        Assert.IsNotNull(evaluated.Balances.RemainingNuyen);
        var graph = new CharacterCreationCompletionFinalizationProjector.EvaluatedGraphInputs(
            original, witness.ProvenanceInputsSnapshotDigest, witness.ProvenanceReviewDigest,
            bootstrap.SnapshotDigest, witness.Skills.PrerequisiteDraft, evaluated.Skills,
            witness.Skills.SelectedActiveSkillPoints, witness.Skills.SelectedSkillGroupPoints,
            witness.Skills.Authority.SourceAnchorIds,
            new(evaluated.Qualities.Selections, evaluated.Qualities.KarmaRemaining, evaluated.Qualities.SourceAnchorIds),
            new(evaluated.Resources.SelectedOption, evaluated.Resources.Budget),
            new(evaluated.Gear, evaluated.GearBudget, evaluated.GearSourceAnchorIds),
            new(evaluated.Lifestyles.Authority, evaluated.Lifestyles.Lifestyles,
                evaluated.Lifestyles.StartingLifestyleId, evaluated.Lifestyles.SourceAnchorIds),
            new(witness.ContactsPolicy, proposal.Selections.Contacts,
                witness.Qualities.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
                witness.Qualities.QualityKarmaLimit, witness.Qualities.MayExceedPositiveQualityLimit,
                evaluated.Contacts.GroupKarma), witness.CarryoverPolicy,
            evaluated.Balances.RemainingCreationKarma.Value, evaluated.Balances.RemainingNuyen.Value,
            new CharacterCreationCompletionFinalizationProjector.SelectedAwakenedGraphInputs(
                witness.Magic, witness.Skills.Attributes.Attributes, evaluated.Magic.Proposed.SourceAnchorIds));
        return CharacterCreationCompletionFinalizationProjector.ProjectEvaluated(graph, cashSource, cashChoice);
    }

    private static CharacterCreationFinalizationReceipt Receipt(Fixture fixture,
        CharacterCreationCompletionFinalizationProjection projection, CharacterCreationCompletionAuthority authority)
    {
        var original = fixture.Original;
        var request = new CharacterCreationCompletionApplyRequest(projection.Binding, fixture.Review.Request,
            projection.ReviewDigest, projection.StartingCash.Choice, projection.ProjectionDigest, "legacy-v2-retained", true);
        string key = CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey);
        string command = CharacterCreationCompletionTransaction.CommandDigest(request);
        string receiptId = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = CharacterCreationFinalizationSchemas.ReceiptV1,
            Id = original.Id,
            idempotencyDigest = key,
            commandDigest = command
        });
        var receipt = new CharacterCreationFinalizationReceipt(CharacterCreationFinalizationSchemas.ReceiptV1,
            receiptId, original.Id, key, command, original.ContentRevision, original.ContentRevision + 1,
            original.SavedRevision, original.ContentRevision + 1, projection.Binding.RawCharacterXmlDigest,
            projection.ExpectedResultRawCharacterXmlDigest, original.Document.AuxiliaryStateDigest,
            projection.Binding.AuthorityDigest, projection.ReviewDigest, projection.ProjectionDigest,
            projection.Binding.BuildMethod, true, true, CharacterCreationFinalizationDigest.ReceiptLedgerRootDigest,
            string.Empty)
        {
            CarryoverPolicy = projection.CarryoverPolicy,
            StartingCash = projection.StartingCash.Choice,
            StartingCashAuthorityDigest = projection.StartingCash.AuthorityDigest,
            CompletionAuthorityDigest = authority.AuthorityDigest
        };
        return receipt with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt) };
    }

    private static Dictionary<string, byte[]> Files(ReadyContext context)
        => Directory.GetFiles(context.Directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertFiles(ReadyContext context, Dictionary<string, byte[]> before)
    {
        var after = Files(context);
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var file in before) CollectionAssert.AreEqual(file.Value, after[file.Key], file.Key);
    }
}
