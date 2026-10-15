using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationSelectedQualitiesTests
{
    [TestMethod]
    public void Real_saved_choices_and_all_racial_grants_reprice_without_resealing_history()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        PrepareElf(context);
        var attributes = new CharacterCreationAttributesService(context.Store, context.Resolver);
        var attributeState = attributes.Load(new(context.WorkspaceId)).Value!;
        var attributePreview = attributes.Preview(new(attributeState.Binding, [])).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            attributes.Confirm(new(attributePreview.Binding, [], attributePreview.PreviewDigest, true)).Outcome);
        var service = Qualities(context);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        string selected = state.Authority.Options.First(option => option.Name == "Ambidextrous" && option.IsSelectable).OptionId;
        var preview = service.Preview(new(state.Binding, [selected])).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            service.Confirm(new(preview.Binding, [selected], preview.PreviewDigest,
                "selected-quality-original", Guid.NewGuid(), true)).Outcome);

        var before = context.Store.Get(context.WorkspaceId).Value!;
        using var cold = context.Restart();
        var loaded = Qualities(cold).LoadCompletionInputs(new(cold.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        Assert.IsNotNull(inputs.SavedQualitiesState?.PendingDraft);
        Assert.IsTrue(inputs.Authority.GrantedQualities.Any(grant => grant.Name == "Low-Light Vision"));
        string additional = inputs.Authority.Options.First(option => option.Name == "Guts" && option.IsSelectable).OptionId;
        string[] proposed = [additional, selected];
        var witness = Capture(inputs.Authority, inputs.SelectedOptionIds, proposed);
        Assert.AreEqual(2, witness.Options.Count);
        Assert.IsTrue(inputs.Authority.Options.Count > witness.Options.Count);
        Assert.AreEqual(inputs.Authority.AuthorityDigest, witness.ProvenanceAuthorityDigest);
        Assert.AreEqual(inputs.Authority.SourceDigest, witness.SourceDigest);
        Assert.AreEqual(inputs.Authority.ProfileDigest, witness.ProfileDigest);
        Assert.AreEqual(inputs.Authority.GmPolicyDigest, witness.GmPolicyDigest);
        Assert.AreEqual(inputs.Authority.RuntimeDigest, witness.RuntimeDigest);
        Assert.AreNotEqual(witness.ProvenanceAuthorityDigest, witness.WitnessDigest);
        Assert.AreEqual(JsonSerializer.Serialize(inputs.Authority.GrantedQualities), JsonSerializer.Serialize(witness.GrantedQualities));
        AssertEquivalent(inputs.Binding, inputs.Authority, witness, inputs.SelectedOptionIds, 0);
        AssertEquivalent(inputs.Binding, inputs.Authority, witness, proposed, 5);

        // An empty purchased selection still carries every grant and policy.
        var empty = Capture(inputs.Authority, [], []);
        Assert.AreEqual(0, empty.Options.Count);
        Assert.AreEqual(inputs.Authority.GrantedQualities.Count, empty.GrantedQualities.Count);
        AssertEquivalent(inputs.Binding, inputs.Authority, empty, [], 0);
        var roundTrip = JsonSerializer.Deserialize<CharacterCreationSelectedQualitiesAuthority>(JsonSerializer.Serialize(witness))!;
        Assert.IsTrue(CharacterCreationQualitiesRules.IsValidSelectedAuthority(roundTrip));
        AssertEquivalent(inputs.Binding, inputs.Authority, roundTrip, proposed, 5);

        var after = cold.Store.Get(cold.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        Assert.AreEqual(JsonSerializer.Serialize(before.Document.AuxiliaryState), JsonSerializer.Serialize(after.Document.AuxiliaryState));
        Assert.AreEqual(inputs.Authority.AuthorityDigest, inputs.SavedQualitiesState!.PendingDraft!.AuthorityDigest);
        Assert.IsNull(typeof(CharacterCreationSelectedQualitiesAuthority).GetProperty("CanConfirm"));
    }

    [TestMethod]
    public void Capture_rejects_unavailable_corrupt_and_duplicate_unselected_full_rows()
    {
        var chosen = Option("chosen", 5);
        var unused = Option("unused", 3);
        var full = Authority(chosen, unused);
        CharacterCreationQualitiesAuthority[] invalid =
        [
            full with { IsAuthoritative = false },
            Seal(full with { Blockers = ["unavailable"] }),
            Seal(full with { Options = [chosen, unused, unused] }),
            Seal(full with { Options = [chosen, unused with { KarmaCost = 99 }] }),
            full with { SourceDigest = Digest('f') },
            Seal(full with { CostPolicy = new(-1, false, false) }),
            full with { Options = [chosen, null!] },
            full with { GrantedQualities = null! },
            full with { Options = [chosen, unused with { SourceAnchorIds = null! }] }
        ];
        foreach (var authority in invalid)
        {
            Assert.IsFalse(CharacterCreationQualitiesRules.TryCaptureSelectedAuthority(authority, [], [chosen.OptionId], out var witness));
            Assert.IsNull(witness);
        }
        Assert.IsFalse(CharacterCreationQualitiesRules.TryCaptureSelectedAuthority(full, [], ["absent"], out _));
        Assert.IsFalse(CharacterCreationQualitiesRules.TryCaptureSelectedAuthority(full, ["chosen", "chosen"], [], out _));
        Assert.IsFalse(CharacterCreationQualitiesRules.TryCaptureSelectedAuthority(full, [], [null!], out _));
    }

    [TestMethod]
    public void Witness_rejects_forged_bytes_wrong_union_duplicate_rows_and_malformed_shapes()
    {
        var option = Option("chosen", 5);
        var other = Option("other", 3);
        var witness = Capture(Authority(option, other), [], ["chosen"]);
        CharacterCreationSelectedQualitiesAuthority[] invalid =
        [
            witness with { QualityKarmaLimit = 999 },
            witness with { ProvenanceAuthorityDigest = Digest('f') },
            witness with { Options = [option with { KarmaCost = 0 }] },
            Seal(witness with { Options = [option with { KarmaCost = 0 }] }),
            Seal(witness with { Options = [] }),
            Seal(witness with { Options = [option, option] }),
            Seal(witness with { Options = [option, other] }),
            Seal(witness with { ProposedOptionIds = ["chosen", "chosen"] }),
            Seal(witness with { OriginalOptionIds = ["absent"] }),
            Seal(witness with { Schema = CharacterCreationQualitiesSchemas.AuthorityV1 }),
            Seal(witness with { ProvenanceAuthorityDigest = "not-a-digest" }),
            Seal(witness with { CostPolicy = new(-1, false, false) }),
            witness with { Options = null! },
            witness with { GrantedQualities = [null!] },
            witness with { OriginalOptionIds = null! }
        ];
        foreach (var changed in invalid)
        {
            Assert.IsFalse(CharacterCreationQualitiesRules.IsValidSelectedAuthority(changed));
            Assert.IsFalse(CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(changed, ["chosen"], 25, 0, out var result));
            Assert.IsNull(result);
        }
    }

    [TestMethod]
    public void Selected_math_matches_caps_excess_policy_grants_duplicate_and_budget_blockers()
    {
        var positive = Option("positive", 30);
        var negative = Option("negative", -30);
        var grantedChoice = Option("already-granted", 2);
        var grant = Grant(grantedChoice, 2);
        var disabled = Seal(Option("disabled", 4) with
            { IsSelectable = false, EligibilityIsExact = false, DisableReasonKey = "quality-requires-exact-choice" });
        foreach (var policy in new CharacterCreationQualityCostPolicy?[] { null, new(2, true, true) })
        {
            var authority = Seal(Authority(positive, negative, grantedChoice, disabled) with
            {
                GrantedQualities = [grant], CostPolicy = policy,
                MayExceedPositiveQualityLimit = true, MayExceedNegativeQualityLimit = true
            });
            var binding = Binding(authority);
            var witness = Capture(authority, ["already-granted"], ["positive", "negative", "disabled"]);
            AssertEquivalent(binding, authority, witness, [], 0);
            AssertEquivalent(binding, authority, witness, ["positive", "negative"], 3);
            AssertEquivalent(binding, authority, witness, ["positive", "positive", "unknown", "disabled"], 0);
            var duplicate = AssertEquivalent(binding, authority, witness, ["already-granted"], 0);
            CollectionAssert.Contains(duplicate.Blockers.ToArray(), CharacterCreationQualitiesBlockers.DuplicateSelection);
            var disabledResult = AssertEquivalent(binding, authority, witness, ["disabled"], 0);
            CollectionAssert.Contains(disabledResult.Blockers.ToArray(), "quality-requires-exact-choice");
            AssertEquivalent(binding, authority, witness, ["positive"], 99);
        }
        var limited = Authority(positive, negative);
        var limitedWitness = Capture(limited, [], ["positive", "negative"]);
        var capped = AssertEquivalent(Binding(limited), limited, limitedWitness, ["positive", "negative"], 0);
        CollectionAssert.Contains(capped.Blockers.ToArray(), CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
        CollectionAssert.Contains(capped.Blockers.ToArray(), CharacterCreationQualitiesBlockers.NegativeLimitExceeded);
    }

    [TestMethod]
    public void Different_levels_share_source_but_are_not_collapsed_or_made_stackable()
    {
        var levelOne = Option("rating-one", 2);
        var levelTwo = Seal(levelOne with { OptionId = "rating-two", Rating = 2, KarmaCost = 4 });
        var authority = Authority(levelOne, levelTwo);
        var witness = Capture(authority, [levelOne.OptionId], [levelTwo.OptionId]);
        Assert.AreEqual(2, witness.Options.Count);
        Assert.AreEqual(witness.Options[0].SourceId, witness.Options[1].SourceId);
        AssertEquivalent(Binding(authority), authority, witness, [levelOne.OptionId], 0);
        AssertEquivalent(Binding(authority), authority, witness, [levelTwo.OptionId], 0);
        var both = AssertEquivalent(Binding(authority), authority, witness, [levelOne.OptionId, levelTwo.OptionId], 0);
        CollectionAssert.Contains(both.Blockers.ToArray(), CharacterCreationQualitiesBlockers.DuplicateSelection);
    }

    [TestMethod]
    public void Capture_and_reprice_detach_all_caller_owned_collections()
    {
        string[] anchors = ["qualities.xml#chosen"];
        var option = Seal(Option("chosen", 5) with { SourceAnchorIds = anchors });
        string[] grantAnchors = ["metatypes.xml#grant"];
        var grant = Grant(Option("racial", 0), 0) with { SourceAnchorIds = grantAnchors };
        grant = grant with { GrantDigest = CharacterCreationQualitiesRules.ComputeGrantDigest(grant) };
        CharacterCreationQualityCatalogOption[] options = [option];
        CharacterCreationGrantedQuality[] grants = [grant];
        string[] authorityAnchors = ["settings.xml#policy"];
        var authority = Seal(Authority(options) with { GrantedQualities = grants, SourceAnchorIds = authorityAnchors });
        string[] original = ["chosen"];
        string[] proposed = ["chosen"];
        var witness = Capture(authority, original, proposed);
        string captured = JsonSerializer.Serialize(witness);
        original[0] = proposed[0] = "changed";
        anchors[0] = grantAnchors[0] = authorityAnchors[0] = "changed";
        options[0] = option with { KarmaCost = 999 };
        grants[0] = grant with { KarmaCost = 999 };
        Assert.AreEqual(captured, JsonSerializer.Serialize(witness));
        Assert.IsTrue(CharacterCreationQualitiesRules.IsValidSelectedAuthority(witness));

        // JSON rehydration supplies mutable collections. The evaluation owns its
        // selected/granted nested arrays even when that caller later edits them.
        string[] rehydratedAnchors = ["qualities.xml#chosen"];
        var mutableOption = witness.Options[0] with { SourceAnchorIds = rehydratedAnchors };
        CharacterCreationQualityCatalogOption[] rehydratedOptions = [mutableOption];
        string[] rehydratedGrantAnchors = ["metatypes.xml#grant"];
        var mutableGrant = witness.GrantedQualities[0] with { SourceAnchorIds = rehydratedGrantAnchors };
        var mutable = witness with { Options = rehydratedOptions, GrantedQualities = new[] { mutableGrant } };
        Assert.IsTrue(CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(mutable, ["chosen"], 25, 0, out var result));
        string priced = JsonSerializer.Serialize(result);
        rehydratedAnchors[0] = rehydratedGrantAnchors[0] = "changed";
        rehydratedOptions[0] = option with { KarmaCost = 999 };
        Assert.AreEqual(priced, JsonSerializer.Serialize(result));
        Assert.IsFalse(CharacterCreationQualitiesRules.IsValidSelectedAuthority(mutable));
    }

    private static CharacterCreationSelectedQualitiesAuthority Capture(CharacterCreationQualitiesAuthority authority,
        IReadOnlyList<string> original, IReadOnlyList<string> proposed)
    {
        Assert.IsTrue(CharacterCreationQualitiesRules.TryCaptureSelectedAuthority(authority, original, proposed, out var witness));
        Assert.IsNotNull(witness);
        Assert.IsTrue(CharacterCreationQualitiesRules.IsValidSelectedAuthority(witness));
        return witness;
    }

    private static CharacterCreationQualitiesCompletionEvaluation AssertEquivalent(CharacterCreationQualitiesBinding binding,
        CharacterCreationQualitiesAuthority authority, CharacterCreationSelectedQualitiesAuthority witness,
        IReadOnlyList<string> selected, int attributeKarma)
    {
        var expected = CharacterCreationQualitiesRules.EvaluateCompletion(new(binding, authority, selected), attributeKarma);
        Assert.IsTrue(CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(witness, selected,
            binding.CreationKarmaTotal, attributeKarma, out var actual));
        Assert.IsNotNull(actual);
        Assert.AreEqual(JsonSerializer.Serialize(expected with { AuthorityDigest = witness.WitnessDigest }),
            JsonSerializer.Serialize(actual));
        return actual;
    }

    private static CharacterCreationQualitiesService Qualities(ReadyContext context)
        => new(context.Store, context.Resolver,
            new CharacterCreationPrerequisiteService(context.Store, context.Queries, context.Resolver),
            new CharacterCreationAttributesService(context.Store, context.Resolver));

    private static void PrepareElf(ReadyContext context)
    {
        var service = new CharacterCreationPrerequisiteService(context.Store, context.Queries, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
        var heritage = state.Authority.Options.Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && option.Rank == "A").HeritageOptions.First(option => option.IsEnabled && option.MetatypeName == "Elf"
                && option.MetavariantSourceId is null);
        var talent = state.Authority.Options.Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && option.Rank == "E").TalentOptions.First(option => option.IsEnabled
                && option.Value.Equals("Mundane", StringComparison.OrdinalIgnoreCase));
        var preview = service.Preview(new(state.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Value!;
        var confirmed = service.Confirm(new(preview.Binding, ranks, preview.PreviewDigest, true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));
    }

    // Pure arithmetic fixtures below do not create, save or reseal any workspace.
    private static CharacterCreationQualityCatalogOption Option(string id, int cost)
    {
        Guid source = Guid.NewGuid();
        var type = cost < 0 ? CharacterCreationQualityType.Negative : CharacterCreationQualityType.Positive;
        string xml = $"<quality><id>{source:D}</id><name>{id}</name><karma>{cost}</karma><category>{type}</category><bonus /><source>SR5</source><page>1</page></quality>";
        return Seal(new CharacterCreationQualityCatalogOption(id, source, id, id, type, 1, cost, 1, false, true, true,
            false, true, true, null, null, null, [$"qualities.xml#{id}"], xml,
            CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml), string.Empty));
    }

    private static CharacterCreationGrantedQuality Grant(CharacterCreationQualityCatalogOption option, int cost)
    {
        var grant = new CharacterCreationGrantedQuality("grant-" + option.OptionId, option.SourceId,
            option.SelectionKey, option.Name, cost < 0 ? CharacterCreationQualityType.Negative : CharacterCreationQualityType.Positive,
            1, cost, false, true, true, "Heritage", ["metatypes.xml#grant"], string.Empty);
        return grant with { GrantDigest = CharacterCreationQualitiesRules.ComputeGrantDigest(grant) };
    }

    private static CharacterCreationQualitiesAuthority Authority(params CharacterCreationQualityCatalogOption[] options)
        => Seal(new CharacterCreationQualitiesAuthority(CharacterCreationQualitiesSchemas.AuthorityV1, "sr5", "settings-profile", 25,
            false, false, 5, options, [], ["qualities.xml", "settings.xml#profile"], [], true,
            Digest('1'), Digest('2'), Digest('3'), Digest('4'), string.Empty));

    private static CharacterCreationQualitiesBinding Binding(CharacterCreationQualitiesAuthority authority)
        => new(new CharacterWorkspaceId("selected-quality-arithmetic"), 7, 7, Digest('5'), new string('6', 64),
            2, Digest('7'), 3, Digest('8'), "sr5", CharacterCreationBuildMethods.Priority, false,
            25, 0, authority.AuthorityDigest, authority.RuntimeDigest);

    private static CharacterCreationQualityCatalogOption Seal(CharacterCreationQualityCatalogOption option)
        => option with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(option) };
    private static CharacterCreationQualitiesAuthority Seal(CharacterCreationQualitiesAuthority authority)
        => authority with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority) };
    private static CharacterCreationSelectedQualitiesAuthority Seal(CharacterCreationSelectedQualitiesAuthority witness)
        => witness with { WitnessDigest = CharacterCreationQualitiesRules.ComputeSelectedAuthorityDigest(witness) };
    private static string Digest(char value) => "sha256:" + new string(value, 64);
}
