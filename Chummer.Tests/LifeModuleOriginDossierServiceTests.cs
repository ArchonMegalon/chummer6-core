using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.LifeModules;
using Chummer.Contracts.LifeModules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class LifeModuleOriginDossierServiceTests
{
    [TestMethod]
    public void Project_is_deterministic_and_orders_only_core_legal_choices()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-b", "B choice"),
            CreateChoice("choice-a", "A choice")));
        var service = new LifeModuleOriginDossierService(authority);

        LifeModuleOriginDossierResult<OriginStoryArcSeed> first = service.Project("workspace-1");
        LifeModuleOriginDossierResult<OriginStoryArcSeed> second = service.Project("workspace-1");

        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, first.Outcome);
        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, second.Outcome);
        Assert.IsNotNull(first.Value);
        Assert.IsNotNull(second.Value);
        Assert.AreEqual(
            JsonSerializer.Serialize(first.Value),
            JsonSerializer.Serialize(second.Value));
        CollectionAssert.AreEqual(
            new[] { "choice-a", "choice-b" },
            first.Value.CurrentTurn.LegalChoices.Select(static choice => choice.ChoiceId).ToArray());
        Assert.IsTrue(first.Value.CurrentTurn.StoryEndsAtDecisionPoint);
        Assert.IsTrue(first.Value.CurrentTurn.VisibleStoryMarkdown.EndsWith(
            first.Value.CurrentTurn.DecisionPrompt,
            StringComparison.Ordinal));
        Assert.IsTrue(first.Value.CurrentTurn.LegalChoices.All(static choice =>
            choice.IsLegal
            && choice.Blockers.Count == 0
            && choice.WithholdsContinuationUntilAccepted
            && choice.MechanicsPreview.Items.Count > 0
            && choice.SourceAnchorIds.Count > 0));
    }

    [TestMethod]
    public void Project_rejects_a_step_that_contains_a_nonlegal_choice()
    {
        LifeModuleDecisionAuthorityChoice illegal = CreateChoice("choice-illegal", "Illegal") with
        {
            IsLegal = false,
            Blockers = ["requires-something"]
        };
        var service = new LifeModuleOriginDossierService(
            new FakeDecisionAuthority(CreateInitialStep(illegal)));

        LifeModuleOriginDossierResult<OriginStoryArcSeed> result = service.Project("workspace-1");

        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToArray(),
            LifeModuleOriginDossierBlockers.AuthorityInvalid);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public void Accept_withholds_continuation_until_acceptance_then_appends_one_canonical_chapter_and_next_turn()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var service = new LifeModuleOriginDossierService(authority);
        OriginStoryArcSeed initial = AssertSuccess(service.Project("workspace-1"));

        Assert.HasCount(0, initial.VisibleChapters);
        Assert.IsFalse(initial.CurrentTurn.VisibleStoryMarkdown.Contains(
            "Accepted consequence",
            StringComparison.Ordinal));

        LifeModuleOriginDossierAdvance advanced = AssertSuccess(service.Accept(
            initial,
            "choice-a",
            "origin-turn-1",
            explicitlyAccepted: true));

        Assert.HasCount(1, advanced.Projection.VisibleChapters);
        OriginNarrativeChapterProjection chapter = advanced.Projection.VisibleChapters[0];
        Assert.AreEqual("decision-1", chapter.ThroughAcceptedDecisionId);
        Assert.IsTrue(chapter.VisibleMarkdown.Contains(
            "Accepted consequence 1.",
            StringComparison.Ordinal));
        Assert.AreEqual(
            initial.CurrentTurn.SeedDigest,
            advanced.Projection.CurrentTurn.PreviousTurnDigest);
        Assert.AreEqual(initial.CurrentTurn.TurnSequence + 1, advanced.Projection.CurrentTurn.TurnSequence);
        CollectionAssert.AreEqual(
            new[] { "decision-1" },
            advanced.Projection.CurrentTurn.AcceptedDecisionIds.ToArray());
        Assert.AreEqual(1, authority.MechanicsMutationCount);
    }

    [TestMethod]
    public void Accept_rejects_an_invented_choice_without_calling_the_mechanics_command()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var service = new LifeModuleOriginDossierService(authority);
        OriginStoryArcSeed initial = AssertSuccess(service.Project("workspace-1"));

        LifeModuleOriginDossierResult<LifeModuleOriginDossierAdvance> result = service.Accept(
            initial,
            "choice-invented",
            "origin-turn-1",
            explicitlyAccepted: true);

        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToArray(),
            LifeModuleOriginDossierBlockers.IllegalChoice);
        Assert.AreEqual(0, authority.MechanicsMutationCount);
        Assert.AreEqual(0, authority.AcceptCallCount);
    }

    [TestMethod]
    public void Accept_fails_closed_for_stale_workspace_source_rules_runtime_and_decision_digests()
    {
        (string Name, Func<LifeModuleDecisionAuthorityStep, LifeModuleDecisionAuthorityStep> Mutate, string Blocker)[] cases =
        [
            ("workspace", step => step with
                {
                    WorkspaceRevision = step.WorkspaceRevision + 1,
                    ContentDigest = Digest("content-stale")
                }, LifeModuleOriginDossierBlockers.WorkspaceStale),
            ("source", step => step with
                {
                    SourceDigest = Digest("source-stale")
                }, LifeModuleOriginDossierBlockers.SourceStale),
            ("rules", step => step with
                {
                    RulesDigest = Digest("rules-stale")
                }, LifeModuleOriginDossierBlockers.RulesStale),
            ("runtime", step => step with
                {
                    RuntimeDigest = Digest("runtime-stale")
                }, LifeModuleOriginDossierBlockers.RuntimeStale),
            ("decision", step => step with
                {
                    DecisionDigest = Digest("decision-stale")
                }, LifeModuleOriginDossierBlockers.DecisionStale),
            ("decision-graph", step => step with
                {
                    DecisionGraphDigest = Digest("decision-graph-stale")
                }, LifeModuleOriginDossierBlockers.DecisionStale),
            ("mechanics-snapshot", step => step with
                {
                    MechanicsSnapshotDigest = Digest("mechanics-stale")
                }, LifeModuleOriginDossierBlockers.DecisionStale)
        ];

        foreach ((string name,
                     Func<LifeModuleDecisionAuthorityStep, LifeModuleDecisionAuthorityStep> mutate,
                     string blocker) in cases)
        {
            var authority = new FakeDecisionAuthority(CreateInitialStep(
                CreateChoice("choice-a", "Take the street path")));
            var service = new LifeModuleOriginDossierService(authority);
            OriginStoryArcSeed initial = AssertSuccess(service.Project("workspace-1"));
            authority.Current = mutate(authority.Current);

            LifeModuleOriginDossierResult<LifeModuleOriginDossierAdvance> result = service.Accept(
                initial,
                "choice-a",
                $"origin-turn-1-{name}",
                explicitlyAccepted: true);

            Assert.AreEqual(
                LifeModuleOriginDossierOutcomes.Conflict,
                result.Outcome,
                $"wrong outcome for {name}");
            CollectionAssert.Contains(result.Blockers.ToArray(), blocker, $"wrong blocker for {name}");
            Assert.AreEqual(0, authority.MechanicsMutationCount, $"mechanics changed for {name}");
        }
    }

    [TestMethod]
    public void Accept_retry_is_idempotent_after_the_workspace_has_advanced()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var service = new LifeModuleOriginDossierService(authority);
        OriginStoryArcSeed initial = AssertSuccess(service.Project("workspace-1"));

        LifeModuleOriginDossierAdvance first = AssertSuccess(service.Accept(
            initial,
            "choice-a",
            "origin-turn-1",
            explicitlyAccepted: true));
        LifeModuleOriginDossierAdvance replay = AssertSuccess(service.Accept(
            initial,
            "choice-a",
            "origin-turn-1",
            explicitlyAccepted: true));

        Assert.AreEqual(first.Projection.SeedDigest, replay.Projection.SeedDigest);
        Assert.AreEqual(
            first.Projection.VisibleChapters[0].ChapterDigest,
            replay.Projection.VisibleChapters[0].ChapterDigest);
        Assert.AreEqual(first.AcceptedDecision.ReceiptDigest, replay.AcceptedDecision.ReceiptDigest);
        Assert.AreEqual(1, authority.MechanicsMutationCount);
        Assert.AreEqual(1, authority.AcceptCallCount);
    }

    [TestMethod]
    public void Accepted_turns_append_chapters_without_rewriting_prior_chapter_digests()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var service = new LifeModuleOriginDossierService(authority);
        OriginStoryArcSeed initial = AssertSuccess(service.Project("workspace-1"));
        LifeModuleOriginDossierAdvance first = AssertSuccess(service.Accept(
            initial,
            "choice-a",
            "origin-turn-1",
            explicitlyAccepted: true));
        string firstChapterDigest = first.Projection.VisibleChapters[0].ChapterDigest;
        string nextChoiceId = first.Projection.CurrentTurn.LegalChoices[0].ChoiceId;

        LifeModuleOriginDossierAdvance second = AssertSuccess(service.Accept(
            first.Projection,
            nextChoiceId,
            "origin-turn-2",
            explicitlyAccepted: true));

        Assert.HasCount(2, second.Projection.VisibleChapters);
        Assert.AreEqual(firstChapterDigest, second.Projection.VisibleChapters[0].ChapterDigest);
        Assert.AreEqual("decision-2", second.Projection.VisibleChapters[1].ThroughAcceptedDecisionId);
        CollectionAssert.AreEqual(
            new[] { "decision-1", "decision-2" },
            second.Projection.CurrentTurn.AcceptedDecisionIds.ToArray());
        Assert.AreEqual(2, authority.MechanicsMutationCount);
    }

    [TestMethod]
    public void Canonical_projection_rejects_player_or_provider_layer_tampering_before_mechanics()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var service = new LifeModuleOriginDossierService(authority);
        OriginStoryArcSeed initial = AssertSuccess(service.Project("workspace-1"));
        LifeModuleOriginDossierAdvance first = AssertSuccess(service.Accept(
            initial,
            "choice-a",
            "origin-turn-1",
            explicitlyAccepted: true));
        OriginNarrativeChapterProjection tamperedChapter = first.Projection.VisibleChapters[0] with
        {
            ProviderLayerDigest = Digest("provider-prose")
        };
        OriginStoryArcSeed tampered = first.Projection with
        {
            VisibleChapters = [tamperedChapter]
        };

        LifeModuleOriginDossierResult<LifeModuleOriginDossierAdvance> result = service.Accept(
            tampered,
            first.Projection.CurrentTurn.LegalChoices[0].ChoiceId,
            "origin-turn-2",
            explicitlyAccepted: true);

        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToArray(),
            LifeModuleOriginDossierBlockers.ProjectionInvalid);
        Assert.AreEqual(1, authority.MechanicsMutationCount);
        Assert.AreEqual(
            LifeModuleOriginDossierService.EmptyPlayerLayerDigest,
            first.Projection.VisibleChapters[0].PlayerLayerDigest);
        Assert.AreEqual(
            LifeModuleOriginDossierService.EmptyProviderLayerDigest,
            first.Projection.VisibleChapters[0].ProviderLayerDigest);
    }

    private static LifeModuleDecisionAuthorityStep CreateInitialStep(
        params LifeModuleDecisionAuthorityChoice[] choices)
        => new(
            Schema: OriginDossierSchemas.DecisionAuthorityStepV1,
            RulesetId: "sr5",
            WorkspaceId: "workspace-1",
            WorkspaceRevision: 1,
            OwnerId: "owner-1",
            RunnerId: "runner-1",
            RunnerDisplayName: "Neon Jack",
            Locale: "en-US",
            JourneyId: "journey-1",
            StageId: "nationality",
            StageOrder: 1,
            TurnId: "turn-1",
            TurnSequence: 1,
            DecisionLeadInMarkdown: "Neon Jack reaches the first fork.",
            DecisionPrompt: "Where did Neon Jack grow up?",
            LegalChoices: choices,
            CanonicalFacts: [],
            AcceptedDecisionIds: [],
            PreviousTurnDigest: LifeModuleOriginDossierService.TurnLedgerRootDigest,
            DecisionGraphDigest: Digest("decision-graph-1"),
            DecisionDigest: Digest("decision-step-1"),
            ContentDigest: Digest("content-1"),
            SourceDigest: Digest("source-1"),
            RulesDigest: Digest("rules-1"),
            RuntimeDigest: Digest("runtime-1"),
            MechanicsSnapshotDigest: Digest("mechanics-0"));

    private static LifeModuleDecisionAuthorityChoice CreateChoice(string id, string label)
    {
        string anchor = $"lifemodules.xml#module:{id}";
        var item = new LifeModuleMechanicsPreviewItem(
            EffectId: $"{id}:effect:1",
            Domain: "active-skill",
            TargetId: "Etiquette",
            BeforeValue: "0",
            AfterValue: "1",
            BudgetDelta: 0,
            SourceAnchorIds: [anchor],
            ItemDigest: string.Empty);
        var preview = new LifeModuleMechanicsPreview(
            KarmaCost: 15,
            KarmaRaw: "15",
            KarmaIsExact: true,
            Items: [item],
            PendingFollowUpIds: [],
            SourceAnchorIds: [anchor],
            PreviewDigest: string.Empty);
        return new LifeModuleDecisionAuthorityChoice(
            ChoiceId: id,
            Label: label,
            Source: "RF",
            PageReference: "66",
            DecisionCommandDigest: Digest($"command-{id}"),
            MechanicsPreview: preview,
            SourceAnchorIds: [anchor],
            Blockers: [],
            IsLegal: true);
    }

    private static T AssertSuccess<T>(LifeModuleOriginDossierResult<T> result)
    {
        Assert.AreEqual(
            LifeModuleOriginDossierOutcomes.Success,
            result.Outcome,
            string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FakeDecisionAuthority : ILifeModuleDecisionAuthority
    {
        private readonly Dictionary<string, LifeModuleDecisionAcceptance> _accepted =
            new(StringComparer.Ordinal);

        public FakeDecisionAuthority(LifeModuleDecisionAuthorityStep current)
        {
            Current = current;
        }

        public LifeModuleDecisionAuthorityStep Current { get; set; }

        public int AcceptCallCount { get; private set; }

        public int MechanicsMutationCount { get; private set; }

        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> Load(
            string workspaceId)
            => string.Equals(Current.WorkspaceId, workspaceId, StringComparison.Ordinal)
                ? Success(Current)
                : Missing<LifeModuleDecisionAuthorityStep>();

        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> FindAcceptance(
            string workspaceId,
            string idempotencyKeyDigest)
            => string.Equals(Current.WorkspaceId, workspaceId, StringComparison.Ordinal)
               && _accepted.TryGetValue(idempotencyKeyDigest, out LifeModuleDecisionAcceptance? acceptance)
                ? Success(acceptance)
                : Missing<LifeModuleDecisionAcceptance>();

        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(
            LifeModuleDecisionAcceptanceCommand command)
        {
            AcceptCallCount++;
            if (_accepted.TryGetValue(command.IdempotencyKeyDigest, out LifeModuleDecisionAcceptance? replay))
                return string.Equals(
                    replay.Receipt.DecisionCommandDigest,
                    command.DecisionCommandDigest,
                    StringComparison.Ordinal)
                    ? Success(replay)
                    : new(
                        LifeModuleOriginDossierOutcomes.Conflict,
                        null,
                        [LifeModuleOriginDossierBlockers.IdempotencyConflict]);
            if (!string.Equals(Current.WorkspaceId, command.WorkspaceId, StringComparison.Ordinal)
                || !string.Equals(
                    command.Schema,
                    OriginDossierSchemas.DecisionAcceptanceCommandV1,
                    StringComparison.Ordinal)
                || Current.WorkspaceRevision != command.WorkspaceRevision
                || !string.Equals(Current.ContentDigest, command.ExpectedContentDigest, StringComparison.Ordinal)
                || !string.Equals(Current.SourceDigest, command.ExpectedSourceDigest, StringComparison.Ordinal)
                || !string.Equals(Current.RulesDigest, command.ExpectedRulesDigest, StringComparison.Ordinal)
                || !string.Equals(Current.RuntimeDigest, command.ExpectedRuntimeDigest, StringComparison.Ordinal)
                || !string.Equals(Current.DecisionGraphDigest, command.ExpectedDecisionGraphDigest, StringComparison.Ordinal)
                || !string.Equals(Current.DecisionDigest, command.ExpectedDecisionDigest, StringComparison.Ordinal)
                || !string.Equals(
                    Current.MechanicsSnapshotDigest,
                    command.ExpectedMechanicsSnapshotDigest,
                    StringComparison.Ordinal)
                || !Current.LegalChoices.Any(choice =>
                    choice.IsLegal
                    && choice.Blockers.Count == 0
                    && string.Equals(choice.ChoiceId, command.ChoiceId, StringComparison.Ordinal)
                    && string.Equals(
                        choice.DecisionCommandDigest,
                        command.DecisionCommandDigest,
                        StringComparison.Ordinal)))
            {
                return new(
                    LifeModuleOriginDossierOutcomes.Conflict,
                    null,
                    [LifeModuleOriginDossierBlockers.DecisionStale]);
            }

            MechanicsMutationCount++;
            int number = MechanicsMutationCount;
            string decisionId = $"decision-{number}";
            string nextGraphDigest = Digest($"decision-graph-{number + 1}");
            string nextContentDigest = Digest($"content-{number + 1}");
            string mechanicsDigest = Digest($"mechanics-{number}");
            string anchor = $"lifemodules.xml#accepted:{decisionId}";
            var fact = new OriginCanonicalNarrativeFact(
                FactId: $"fact-{number}",
                FactKind: "accepted-life-module",
                LocalizedSummary: $"Accepted fact {number}.",
                AcceptedDecisionId: decisionId,
                SourceAnchorIds: [anchor],
                FactDigest: string.Empty);
            var receipt = new LifeModuleAcceptedDecisionReceipt(
                Schema: OriginDossierSchemas.AcceptedDecisionReceiptV1,
                DecisionId: decisionId,
                ChoiceId: command.ChoiceId,
                DecisionCommandDigest: command.DecisionCommandDigest,
                IdempotencyKeyDigest: command.IdempotencyKeyDigest,
                PreviousWorkspaceRevision: Current.WorkspaceRevision,
                WorkspaceRevision: Current.WorkspaceRevision + 1,
                PreviousContentDigest: Current.ContentDigest,
                ContentDigest: nextContentDigest,
                SourceDigest: Current.SourceDigest,
                RulesDigest: Current.RulesDigest,
                RuntimeDigest: Current.RuntimeDigest,
                PreviousDecisionDigest: Current.DecisionDigest,
                PreviousMechanicsSnapshotDigest: Current.MechanicsSnapshotDigest,
                AcceptedDecisionGraphDigest: nextGraphDigest,
                MechanicsSnapshotDigest: mechanicsDigest,
                ConsequenceMarkdown: $"Accepted consequence {number}.",
                CanonicalFacts: [fact],
                ReceiptDigest: Digest($"receipt-{number}-{command.IdempotencyKeyDigest}"));
            LifeModuleDecisionAuthorityStep next = Current with
            {
                WorkspaceRevision = receipt.WorkspaceRevision,
                StageId = $"stage-{number + 1}",
                StageOrder = Current.StageOrder + 1,
                TurnId = $"turn-{number + 1}",
                TurnSequence = Current.TurnSequence + 1,
                DecisionLeadInMarkdown = $"The next scene {number + 1} begins.",
                DecisionPrompt = $"What happens at decision {number + 1}?",
                LegalChoices = [CreateChoice($"choice-{number + 1}", $"Choice {number + 1}")],
                CanonicalFacts = [.. Current.CanonicalFacts, fact],
                AcceptedDecisionIds = [.. Current.AcceptedDecisionIds, decisionId],
                PreviousTurnDigest = command.ExpectedTurnSeedDigest,
                DecisionGraphDigest = nextGraphDigest,
                DecisionDigest = Digest($"decision-step-{number + 1}"),
                ContentDigest = nextContentDigest,
                MechanicsSnapshotDigest = mechanicsDigest
            };
            var acceptance = new LifeModuleDecisionAcceptance(receipt, next);
            _accepted.Add(command.IdempotencyKeyDigest, acceptance);
            Current = next;
            return Success(acceptance);
        }

        private static LifeModuleDecisionAuthorityResult<T> Success<T>(T value)
            => new(LifeModuleOriginDossierOutcomes.Success, value, []);

        private static LifeModuleDecisionAuthorityResult<T> Missing<T>()
            => new(LifeModuleOriginDossierOutcomes.Missing, default, []);
    }
}
