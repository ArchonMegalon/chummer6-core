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
    public void Preview_scratch_preserves_legacy_hashes_after_large_unicode_and_short_items()
    {
        var basis = CreateChoice("choice-a", "Street path").MechanicsPreview;
        var item = basis.Items[0];
        var preview = basis with
        {
            Items = [item with { EffectId = "z", AfterValue = string.Concat(Enumerable.Repeat("ä雪\"\n<&", 12_000)) },
                item with { EffectId = "a", AfterValue = "2" },
                item with { EffectId = "a", AfterValue = "3" }, item],
            PendingFollowUpIds = ["answer"],
            KarmaRaw = "15\"<&雪"
        };
        var result = LifeModuleOriginDossierService.SealPreview(preview);
        foreach (var effect in result.Items)
        {
            Assert.AreEqual(LegacyDigest(writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("budgetDelta", effect.BudgetDelta);
                writer.WriteString("domain", effect.Domain);
                writer.WriteString("effectId", effect.EffectId);
                writer.WriteString("targetId", effect.TargetId);
                writer.WriteString("beforeValue", effect.BeforeValue);
                writer.WriteString("afterValue", effect.AfterValue);
                Strings(writer, "sourceAnchorIds", effect.SourceAnchorIds);
                writer.WriteEndObject();
            }), effect.ItemDigest);
        }
        Assert.AreEqual(LegacyDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("karmaCost", result.KarmaCost);
            writer.WriteString("karmaRaw", result.KarmaRaw);
            writer.WriteBoolean("karmaIsExact", result.KarmaIsExact);
            Strings(writer, "itemDigests", result.Items.Select(value => value.ItemDigest));
            Strings(writer, "pendingFollowUpIds", result.PendingFollowUpIds);
            Strings(writer, "sourceAnchorIds", result.SourceAnchorIds);
            writer.WriteEndObject();
        }), result.PreviewDigest);
        CollectionAssert.AreEqual(new[] { "a", "a", item.EffectId, "z" },
            result.Items.Select(value => value.EffectId).ToArray());
        Assert.IsTrue(StringComparer.Ordinal.Compare(result.Items[0].ItemDigest, result.Items[1].ItemDigest) <= 0);

        static string LegacyDigest(Action<Utf8JsonWriter> write)
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes)) { write(writer); writer.Flush(); }
            return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
        }
        static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
        {
            writer.WriteStartArray(name);
            foreach (string value in values) writer.WriteStringValue(value);
            writer.WriteEndArray();
        }
    }

    [TestMethod]
    public void Turn_scratch_is_isolated_for_nested_parallel_and_failed_projections()
    {
        var steps = Enumerable.Range(0, 8).Select(index =>
            CreateInitialStep(CreateChoice($"choice-{index}", $"Path {index}")) with
            { OwnerId = $"owner-{index}", RunnerDisplayName = $"Runner {index}" }).ToArray();
        string Project(LifeModuleDecisionAuthorityStep step)
        {
            Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out var turn));
            return JsonSerializer.Serialize(turn);
        }
        string[] expected = steps.Select(Project).ToArray();
        var choice = steps[0].LegalChoices[0];
        LifeModuleFollowUpPromptDto[] prompts =
            [new("city", new string('x', 10_000), "text", true, [], ["source"], "effect", "city")];
        var plain = steps[0] with { LegalChoices = [choice with { FollowUps = prompts }] };
        string expectedNested = Project(plain);
        int nestedCalls = 0;
        var nested = plain with { LegalChoices = [choice with
        {
            FollowUps = new CallbackPrompts(prompts, () =>
            {
                nestedCalls++;
                Assert.AreEqual(expected[1], Project(steps[1]));
            })
        }] };
        Assert.AreEqual(expectedNested, Project(nested));
        Assert.IsTrue(nestedCalls > 0);

        // Fail after writing the large first prompt, not before buffer use.
        var broken = plain with { LegalChoices = [choice with
        {
            FollowUps = new CallbackPrompts(prompts, () => throw new InvalidOperationException("synthetic"))
        }] };
        Assert.ThrowsExactly<InvalidOperationException>(() => Project(broken));
        Parallel.For(0, 32, index => Assert.AreEqual(expected[index % steps.Length], Project(steps[index % steps.Length])));
        Assert.AreEqual(expectedNested, Project(plain));
    }

    private sealed class CallbackPrompts(
        IReadOnlyList<LifeModuleFollowUpPromptDto> values, Action afterRead)
        : IReadOnlyList<LifeModuleFollowUpPromptDto>
    {
        public int Count => values.Count;
        public LifeModuleFollowUpPromptDto this[int index] => values[index];
        public IEnumerator<LifeModuleFollowUpPromptDto> GetEnumerator()
        {
            foreach (var value in values) yield return value;
            afterRead();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestMethod]
    public void Historical_anchor_normalization_keeps_ordinal_digests_and_detaches_each_snapshot()
    {
        string[][] inputs =
        [
            [" source "], [null!, "", " \t", "source", " source ", "source"],
            ["z", "a", "z", " A ", "ä", "a", "\u00a0β\u00a0", "A"],
            Enumerable.Range(0, 128).Select(index => $" anchor-{127 - index % 32:D3} ").ToArray()
        ];
        foreach (string[] input in inputs)
        {
            string[] canonical = input.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()).Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var choice = CreateChoice("choice-a", "Street path");
            LifeModuleDecisionAuthorityStep Step(string[] anchors) => CreateInitialStep(choice with
            {
                SourceAnchorIds = anchors,
                MechanicsPreview = choice.MechanicsPreview with
                {
                    SourceAnchorIds = anchors,
                    Items = [choice.MechanicsPreview.Items[0] with { SourceAnchorIds = anchors }]
                }
            }) with
            {
                AcceptedDecisionIds = ["decision-1"],
                CanonicalFacts = [new("fact", "background", "An event.", "decision-1", anchors, string.Empty)]
            };
            Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(Step(canonical), out var expected));
            var step = Step(input);
            Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out var actual));
            string snapshot = JsonSerializer.Serialize(actual);
            Assert.AreEqual(JsonSerializer.Serialize(expected), snapshot,
                "Every preview, fact, choice and turn digest must preserve the old normalization semantics.");
            input[0] = "changed-source";
            Assert.AreEqual(snapshot, JsonSerializer.Serialize(actual), "Projection borrowed a caller array.");
            Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out var changed));
            Assert.AreNotEqual(snapshot, JsonSerializer.Serialize(changed), "A later read reused stale inputs.");
        }
    }

    [TestMethod]
    public void Fresh_turn_projection_bounds_allocations_and_preserves_canonical_output()
    {
        var choices = Enumerable.Range(0, 128).Select(index =>
        {
            var choice = CreateChoice($"choice-{index:D3}", $"Path {index}");
            var item = choice.MechanicsPreview.Items[0];
            return choice with { MechanicsPreview = choice.MechanicsPreview with
            {
                Items = Enumerable.Range(0, 12).Select(effect => item with
                {
                    EffectId = $"{choice.ChoiceId}:effect:{effect}",
                    AfterValue = effect.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }).ToArray()
            } };
        }).ToArray();
        var step = CreateInitialStep(choices);
        Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out var first));
        string expected = JsonSerializer.Serialize(first);
        // Captured against the unmodified dc513e29 runtime before this fix.
        Assert.AreEqual("5fa2c27ee8f09336c1aad321a453b857177459573883675b43d8432e98dba08e", Digest(expected));
        for (int repeat = 0; repeat < 2; repeat++)
            Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out _));
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out var measured));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"Fresh turn projection allocated bytes: {allocated}");
        Assert.AreEqual(expected, JsonSerializer.Serialize(measured));
        Assert.IsTrue(allocated < 1_250_000,
            $"Projection must reuse local digest scratch instead of a writer per effect; allocated {allocated:N0} bytes.");
        // Public restore still independently revalidates every constructed digest.
        var service = new LifeModuleOriginDossierService(new FakeDecisionAuthority(step));
        var projection = AssertSuccess(service.Project("workspace-1"));
        Assert.AreEqual(JsonSerializer.Serialize(projection),
            JsonSerializer.Serialize(AssertSuccess(service.Resume(projection))));
    }

    [TestMethod]
    public void Fresh_turn_projection_rejects_invalid_choice_and_fact_shapes()
    {
        var valid = CreateChoice("choice-a", "Street path");
        LifeModuleDecisionAuthorityChoice[] invalidChoices =
        [
            valid with { ChoiceId = " " }, valid with { Label = " " },
            valid with { Source = " " }, valid with { IsLegal = false },
            valid with { DecisionCommandDigest = "not-a-digest" },
            valid with { SourceAnchorIds = [] }, valid with { SourceAnchorIds = null! },
            valid with { SourceAnchorIds = [null!, "", " \t"] }, valid with { Blockers = ["unavailable"] },
            valid with { MechanicsPreview = valid.MechanicsPreview with { SourceAnchorIds = [] } },
            valid with { FollowUps = [] }
        ];
        foreach (var choice in invalidChoices)
            Assert.IsFalse(LifeModuleOriginDossierService.TryCreateTurn(CreateInitialStep(choice), out _));
        Assert.IsFalse(LifeModuleOriginDossierService.TryCreateTurn(CreateInitialStep(valid, valid), out _));
        var fact = new OriginCanonicalNarrativeFact("fact", "background", "A remembered event.",
            "decision-1", ["source"], string.Empty);
        var step = CreateInitialStep(valid) with { AcceptedDecisionIds = ["decision-1"], CanonicalFacts = [fact] };
        Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out _));
        foreach (var invalid in new[] { fact with { FactId = " " }, fact with { FactKind = " " },
            fact with { LocalizedSummary = " " }, fact with { AcceptedDecisionId = "missing" },
            fact with { SourceAnchorIds = [] } })
            Assert.IsFalse(LifeModuleOriginDossierService.TryCreateTurn(step with { CanonicalFacts = [invalid] }, out _));
        Assert.IsFalse(LifeModuleOriginDossierService.TryCreateTurn(step with { CanonicalFacts = [fact, fact] }, out _));
    }

    [TestMethod]
    public void Restored_projection_still_rejects_changed_choice_preview_and_effect_digests()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(CreateChoice("choice-a", "Street path")));
        var service = new LifeModuleOriginDossierService(authority);
        var projection = AssertSuccess(service.Project("workspace-1"));
        var choice = projection.CurrentTurn.LegalChoices.Single();
        var preview = choice.MechanicsPreview;
        var item = preview.Items.Single();
        foreach (var changed in new[]
        {
            choice with { Label = "Changed path" },
            choice with { ChoiceDigest = Digest("changed-choice") },
            choice with { MechanicsPreviewDigest = Digest("changed-preview-binding") },
            choice with { MechanicsPreview = preview with { KarmaCost = preview.KarmaCost + 1 } },
            choice with { MechanicsPreview = preview with { PreviewDigest = Digest("changed-preview") } },
            choice with { MechanicsPreview = preview with { Items = [item with { AfterValue = "99" }] } },
            choice with { MechanicsPreview = preview with { Items = [item with { ItemDigest = Digest("changed-effect") }] } }
        })
        {
            var tampered = projection with { CurrentTurn = projection.CurrentTurn with { LegalChoices = [changed] } };
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, service.Resume(tampered).Outcome);
        }
        Assert.AreEqual(0, authority.MechanicsMutationCount);
    }

    [TestMethod]
    public void Fresh_projection_detaches_preview_and_fact_arrays_from_authority_inputs()
    {
        var choice = CreateChoice("choice-a", "Street path");
        string[] anchors = ["source"];
        LifeModuleMechanicsPreviewItem[] items = [choice.MechanicsPreview.Items.Single() with
        {
            SourceAnchorIds = anchors
        }];
        var fact = new OriginCanonicalNarrativeFact("fact", "background", "A remembered event.",
            "decision-1", anchors, string.Empty);
        OriginCanonicalNarrativeFact[] facts = [fact];
        var step = CreateInitialStep(choice with
        {
            SourceAnchorIds = anchors,
            MechanicsPreview = choice.MechanicsPreview with { Items = items, SourceAnchorIds = anchors }
        }) with { AcceptedDecisionIds = ["decision-1"], CanonicalFacts = facts };
        Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out var projected));
        string original = JsonSerializer.Serialize(projected);

        anchors[0] = "changed-source";
        items[0] = items[0] with { AfterValue = "99" };
        facts[0] = fact with { LocalizedSummary = "Changed event." };

        Assert.AreEqual(original, JsonSerializer.Serialize(projected));
        Assert.IsTrue(LifeModuleOriginDossierService.TryCreateTurn(step, out var fresh));
        Assert.AreNotEqual(original, JsonSerializer.Serialize(fresh));
    }

    [TestMethod]
    public void Restored_projection_rejects_mutated_caller_owned_follow_ups()
    {
        LifeModuleFollowUpPromptDto[] prompts =
        [
            new("city", "Home city", "text", true, [], ["source"], "effect", "city")
        ];
        var choice = CreateChoice("choice-a", "Street path") with { FollowUps = prompts };
        var authority = new FakeDecisionAuthority(CreateInitialStep(choice));
        var service = new LifeModuleOriginDossierService(authority);
        var projection = AssertSuccess(service.Project("workspace-1"));
        AssertSuccess(service.Resume(projection));

        prompts[0] = prompts[0] with { Label = "Changed city question" };

        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, service.Resume(projection).Outcome);
        var fresh = AssertSuccess(service.Project("workspace-1"));
        Assert.AreNotEqual(projection.SeedDigest, fresh.SeedDigest);
        AssertSuccess(service.Resume(fresh));
        Assert.AreEqual(0, authority.MechanicsMutationCount);
    }

    [TestMethod]
    public void Restored_projection_rejects_changed_accepted_facts_without_replaying_mechanics()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(CreateChoice("choice-a", "Street path")));
        var service = new LifeModuleOriginDossierService(authority);
        var initial = AssertSuccess(service.Project("workspace-1"));
        var accepted = AssertSuccess(service.Accept(initial, "choice-a", "origin-turn-1", explicitlyAccepted: true));
        var projection = accepted.Projection;
        AssertSuccess(service.Resume(projection));
        var fact = projection.CurrentTurn.CanonicalFacts.Single();
        foreach (var changed in new[]
        {
            fact with { LocalizedSummary = "Changed event." },
            fact with { FactDigest = Digest("changed-fact") },
            fact with { SourceAnchorIds = ["changed-source"] }
        })
        {
            var tampered = projection with { CurrentTurn = projection.CurrentTurn with { CanonicalFacts = [changed] } };
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, service.Resume(tampered).Outcome);
        }
        Assert.AreEqual(1, authority.MechanicsMutationCount);
        Assert.AreEqual(1, authority.AcceptCallCount);
    }

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
    public void Accept_allows_a_source_bound_terminal_turn_after_the_atomic_foundation_decision()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")))
        {
            TerminalOnAccept = true
        };
        var interaction = new LifeModuleOriginDossierInteractionService(
            new LifeModuleOriginDossierService(authority));
        LifeModuleOriginDossierDraftCheckpoint prepared = AssertSuccess(
            interaction.Prepare(
                AssertSuccess(interaction.Start("workspace-1")),
                "choice-a"));

        LifeModuleOriginDossierInteractionAdvance result = AssertSuccess(
            interaction.Confirm(
                prepared,
                prepared.PendingPreview!.PreviewDigest,
                "terminal-origin-turn",
                explicitlyConfirmed: true));

        Assert.IsTrue(result.Checkpoint.Projection.CurrentTurn.IsTerminal);
        Assert.HasCount(0, result.Checkpoint.Projection.CurrentTurn.LegalChoices);
        Assert.HasCount(1, result.Checkpoint.Projection.VisibleChapters);
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

    [TestMethod]
    public void Resume_rebinds_an_accepted_timeline_after_restart_and_rejects_stale_authority()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var service = new LifeModuleOriginDossierService(authority);
        OriginStoryArcSeed initial = AssertSuccess(service.Project("workspace-1"));
        OriginStoryArcSeed accepted = AssertSuccess(service.Accept(
            initial,
            "choice-a",
            "resume-turn-1",
            explicitlyAccepted: true)).Projection;

        OriginStoryArcSeed resumed = AssertSuccess(service.Resume(accepted));

        Assert.AreEqual(accepted.SeedDigest, resumed.SeedDigest);
        CollectionAssert.AreEqual(
            accepted.VisibleChapters.Select(static chapter => chapter.ChapterDigest).ToArray(),
            resumed.VisibleChapters.Select(static chapter => chapter.ChapterDigest).ToArray());

        authority.Current = authority.Current with
        {
            RuntimeDigest = Digest("changed-runtime")
        };
        LifeModuleOriginDossierResult<OriginStoryArcSeed> stale = service.Resume(accepted);
        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Conflict, stale.Outcome);
        CollectionAssert.Contains(
            stale.Blockers.ToArray(),
            LifeModuleOriginDossierBlockers.RuntimeStale);
    }

    [TestMethod]
    public void Interaction_checkpoint_projects_story_source_effects_and_explicit_no_ltd_provenance()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var interaction = new LifeModuleOriginDossierInteractionService(
            new LifeModuleOriginDossierService(authority));

        LifeModuleOriginDossierDraftCheckpoint started = AssertSuccess(
            interaction.Start("workspace-1"));
        LifeModuleOriginDossierDraftCheckpoint prepared = AssertSuccess(
            interaction.Prepare(started, "choice-a"));

        Assert.IsNotNull(prepared.PendingPreview);
        Assert.IsTrue(prepared.PendingPreview.VisibleStoryMarkdown.EndsWith(
            prepared.PendingPreview.DecisionPrompt,
            StringComparison.Ordinal));
        Assert.AreEqual("RF", prepared.PendingPreview.SelectedChoice.Source);
        Assert.AreEqual("66", prepared.PendingPreview.SelectedChoice.PageReference);
        Assert.HasCount(1, prepared.PendingPreview.SelectedChoice.MechanicsPreview.Items);
        Assert.AreEqual(
            "Etiquette",
            prepared.PendingPreview.SelectedChoice.MechanicsPreview.Items[0].TargetId);
        Assert.AreEqual(
            OriginLtdProvenanceStates.NotRequested,
            prepared.LtdProvenance.State);
        Assert.IsFalse(prepared.LtdProvenance.IsVerified);
        Assert.AreEqual(string.Empty, prepared.LtdProvenance.ProviderId);
        Assert.IsFalse(prepared.PendingPreview.IncludesFutureBranchText);
        Assert.IsTrue(prepared.PendingPreview.RequiresExplicitConfirmation);
        Assert.IsTrue(prepared.IsUserOwnedDraft);
    }

    [TestMethod]
    public void Interaction_checkpoint_round_trips_and_confirm_is_preview_bound_and_idempotent()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var interaction = new LifeModuleOriginDossierInteractionService(
            new LifeModuleOriginDossierService(authority));
        LifeModuleOriginDossierDraftCheckpoint prepared = AssertSuccess(
            interaction.Prepare(
                AssertSuccess(interaction.Start("workspace-1")),
                "choice-a"));
        string json = JsonSerializer.Serialize(prepared);
        LifeModuleOriginDossierDraftCheckpoint restarted =
            JsonSerializer.Deserialize<LifeModuleOriginDossierDraftCheckpoint>(json)!;

        LifeModuleOriginDossierDraftCheckpoint restored = AssertSuccess(
            interaction.Restore(restarted));
        Assert.AreEqual(prepared.CheckpointDigest, restored.CheckpointDigest);

        LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> wrong =
            interaction.Confirm(
                restored,
                Digest("wrong-preview"),
                "interaction-turn-1",
                explicitlyConfirmed: true);
        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, wrong.Outcome);
        Assert.AreEqual(0, authority.MechanicsMutationCount);

        LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> notConfirmed =
            interaction.Confirm(
                restored,
                restored.PendingPreview!.PreviewDigest,
                "interaction-turn-1",
                explicitlyConfirmed: false);
        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, notConfirmed.Outcome);
        CollectionAssert.Contains(
            notConfirmed.Blockers.ToArray(),
            LifeModuleOriginDossierBlockers.ExplicitAcceptanceRequired);
        Assert.AreEqual(0, authority.MechanicsMutationCount);

        LifeModuleOriginDossierInteractionAdvance first = AssertSuccess(
            interaction.Confirm(
                restored,
                restored.PendingPreview.PreviewDigest,
                "interaction-turn-1",
                explicitlyConfirmed: true));
        LifeModuleOriginDossierInteractionAdvance replay = AssertSuccess(
            interaction.Confirm(
                restored,
                restored.PendingPreview.PreviewDigest,
                "interaction-turn-1",
                explicitlyConfirmed: true));

        Assert.AreEqual(1, authority.MechanicsMutationCount);
        Assert.AreEqual(first.Checkpoint.CheckpointDigest, replay.Checkpoint.CheckpointDigest);
        Assert.HasCount(1, first.Checkpoint.TimelineChapterDigests);
        Assert.IsNull(first.Checkpoint.PendingPreview);
        Assert.AreEqual(
            OriginLtdProvenanceStates.NotRequested,
            first.Checkpoint.LtdProvenance.State);
    }

    [TestMethod]
    public void Interaction_restore_rejects_timeline_or_ltd_provenance_tampering()
    {
        var authority = new FakeDecisionAuthority(CreateInitialStep(
            CreateChoice("choice-a", "Take the street path")));
        var interaction = new LifeModuleOriginDossierInteractionService(
            new LifeModuleOriginDossierService(authority));
        LifeModuleOriginDossierDraftCheckpoint prepared = AssertSuccess(
            interaction.Prepare(
                AssertSuccess(interaction.Start("workspace-1")),
                "choice-a"));

        LifeModuleOriginDossierDraftCheckpoint timelineTampered = prepared with
        {
            TimelineChapterDigests = [Digest("invented-chapter")]
        };
        LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> timelineResult =
            interaction.Restore(timelineTampered);
        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, timelineResult.Outcome);

        LifeModuleOriginDossierDraftCheckpoint ltdTampered = prepared with
        {
            LtdProvenance = prepared.LtdProvenance with
            {
                State = OriginLtdProvenanceStates.VerifiedProposal,
                ProviderId = "invented-provider",
                IsVerified = true
            }
        };
        LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> ltdResult =
            interaction.Restore(ltdTampered);
        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, ltdResult.Outcome);
        Assert.AreEqual(0, authority.MechanicsMutationCount);
    }

    [TestMethod]
    public void Restore_refreshes_display_hints_but_preserves_checkpoint_and_rejects_canonical_label_drift()
    {
        var choice = CreateChoice("choice-a", "Street path");
        var prompt = new LifeModuleFollowUpPromptDto("prompt-1", "Any", "text", true, [],
            choice.SourceAnchorIds, "choice-a:effect:1", "knowledgeskilllevel/name");
        choice = choice with { FollowUps = [prompt] };
        var authority = new FakeDecisionAuthority(CreateInitialStep(choice));
        var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
        var checkpoint = AssertSuccess(interaction.Start("workspace-1"));
        string persisted = JsonSerializer.Serialize(checkpoint);
        checkpoint = JsonSerializer.Deserialize<LifeModuleOriginDossierDraftCheckpoint>(persisted)!;
        authority.Current = authority.Current with
        { LegalChoices = [choice with { FollowUps = [prompt with { DisplayLabel = "Language · Any" }] }] };

        var restored = AssertSuccess(interaction.Restore(checkpoint));
        Assert.AreEqual(persisted, JsonSerializer.Serialize(restored));
        Assert.AreEqual("Language · Any", restored.Projection.CurrentTurn.LegalChoices.Single().FollowUps!.Single().DisplayLabel);
        Assert.AreEqual(checkpoint.CheckpointDigest, restored.CheckpointDigest);
        Assert.AreEqual(0, authority.MechanicsMutationCount);

        authority.Current = authority.Current with
        { LegalChoices = [choice with { FollowUps = [prompt with { Label = "Different canonical question" }] }] };
        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Conflict, interaction.Restore(checkpoint).Outcome);
        Assert.AreEqual(0, authority.MechanicsMutationCount);
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

        public bool TerminalOnAccept { get; init; }

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
            if (TerminalOnAccept)
            {
                next = next with
                {
                    StageId = "nationality-accepted",
                    StageOrder = Current.StageOrder,
                    DecisionLeadInMarkdown = receipt.ConsequenceMarkdown,
                    DecisionPrompt = "Continue character creation.",
                    LegalChoices = [],
                    IsTerminal = true
                };
            }
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
