using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Policy_reader_binds_a_detached_bounded_context_and_review_never_claims_complete_coverage()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = Load(context);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.InputsV3, inputs.Schema);
        var policy = inputs.EffectivePolicy!;
        Assert.AreEqual(inputs.Skills.Authority.RawProfileInputsDigest, policy.ProfileInputsDigest);
        Assert.AreEqual(inputs.Skills.Authority.SettingsProfileId, policy.SettingsProfileId);
        Assert.AreEqual(CharacterCreationEffectivePolicyContext.ComputeDigest(policy), policy.PolicyDigest);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.ReviewV3, review.Value!.Schema);
        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, complete.Outcome);
        CollectionAssert.Contains(complete.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(inputs.SnapshotDigest, Load(context).SnapshotDigest);
        ((IList<CharacterCreationPolicySettingCoverage>)policy.Settings)[0] = policy.Settings[0] with { IntegerValue = 99 };
        Assert.AreEqual((int?)inputs.Skills.Authority.MaxActiveSkillRatingCreate, Load(context).EffectivePolicy!.Settings[0].IntegerValue);
    }

    [TestMethod]
    public void Required_policy_rejects_missing_and_forged_coverage_even_when_all_public_digests_are_recomputed()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var old = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var missing = CharacterCreationCompletionReviewService.Review(old, Request(old, CharacterCreationPolicyRequirement.SkillLimits));
        CollectionAssert.Contains(missing.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        var inputs = Load(context);
        var policy = inputs.EffectivePolicy!;
        var forgedCap = policy.Settings.ToArray();
        forgedCap[0] = forgedCap[0] with { IntegerValue = 5 };
        var forgedImported = policy.Settings.ToArray();
        Assert.IsTrue(forgedImported.Length > 3);
        forgedImported[3] = forgedImported[3] with { Disposition = CharacterCreationPolicyDisposition.Effective };
        foreach (var forged in new[]
                 {
                     policy with { Settings = forgedCap },
                     policy with { Settings = forgedImported },
                     policy with { Settings = policy.Settings.Skip(1).ToArray() },
                     policy with { Domains = policy.Domains.Skip(1).ToArray() },
                     policy with { Domains = policy.Domains.Select(row => row with { Disposition = CharacterCreationPolicyDisposition.Effective }).ToArray() },
                     policy with { Sources = [] },
                     policy with { ProfileInputsDigest = CharacterCreationFinalizationDigest.Compute("another profile") },
                     policy with { BuildMethod = CharacterCreationBuildMethods.Karma }
                 })
        {
            var resealed = WithPolicy(inputs, forged);
            var denied = CharacterCreationCompletionReviewService.Review(resealed, Request(resealed, CharacterCreationPolicyRequirement.SkillLimits));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
            CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);
        }
        var absent = inputs with { EffectivePolicy = null };
        absent = absent with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(absent) };
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            CharacterCreationCompletionReviewService.Review(absent, Request(absent, CharacterCreationPolicyRequirement.SkillLimits)).Outcome);
    }

    [TestMethod]
    public void Legacy_null_omitted_inputs_and_requests_keep_their_exact_digest_shape()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        Assert.IsNull(inputs.Loadout);
        Assert.IsNull(inputs.EffectivePolicy);
        Assert.IsFalse(JsonSerializer.SerializeToElement(inputs).TryGetProperty("EffectivePolicy", out _));
        var historical = new { inputs.Schema, inputs.Skills, inputs.Magic, inputs.Lifestyles, inputs.Contacts, SnapshotDigest = string.Empty };
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(historical), inputs.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(inputs), JsonSerializer.Serialize(inputs with { EffectivePolicy = null }));
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var historicalRequest = new { request.InputsSnapshotDigest, request.Selections };
        Assert.AreEqual(JsonSerializer.Serialize(historicalRequest), JsonSerializer.Serialize(request));
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(historicalRequest), CharacterCreationFinalizationDigest.Compute(request));
        var policyInputs = Load(context);
        Assert.AreNotEqual(inputs.SnapshotDigest, policyInputs.SnapshotDigest);
        var changed = policyInputs.EffectivePolicy! with
        {
            Sources = policyInputs.EffectivePolicy.Sources.Select((row, index) => index == 3
                ? row with { ContentDigest = CharacterCreationFinalizationDigest.Compute("changed rules bytes") } : row).ToArray()
        };
        Assert.AreNotEqual(policyInputs.SnapshotDigest, WithPolicy(policyInputs, changed).SnapshotDigest);
    }

    [TestMethod]
    public void Selected_capture_and_legacy_replay_refuse_unsupported_or_unbound_policy_requirements()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(source, original.Document.Content, out var bootstrap));
        var inputs = Load(context);
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs,
            Request(inputs, CharacterCreationPolicyRequirement.Complete));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, captured.Outcome);
        CollectionAssert.Contains(captured.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        // Even an optional context requires an explicit binding, never silent reduction.
        captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs,
            Request(inputs, CharacterCreationPolicyRequirement.None) with { EffectivePolicyDigest = null });
        CollectionAssert.Contains(captured.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);

        var old = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var oldRequest = Request(old, CharacterCreationPolicyRequirement.None);
        var legacy = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, old, oldRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, legacy.Outcome, string.Join(",", legacy.Blockers));
        var forged = oldRequest with { PolicyRequirement = CharacterCreationPolicyRequirement.SkillLimits };
        var witness = legacy.Value! with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(forged) };
        witness = witness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(witness) };
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, forged);
        CollectionAssert.Contains(replay.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.ReplayUnavailable);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, legacy.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var decodedBootstrap, out var decodedWitness));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(decodedBootstrap, decodedWitness, out var repeated));
        Assert.AreEqual(JsonSerializer.Serialize(archive), JsonSerializer.Serialize(repeated));

        Assert.IsTrue(CharacterCreationBootstrapSourceSnapshot.TryCapture(source, original.Document.Content, out var activation));
        Assert.IsFalse(activation.CreateFrozenContext().TryResolveCreationEffectivePolicy(out var unavailable));
        Assert.IsNull(unavailable, "Older resolvers must use the default-unavailable capability.");
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void V3_capture_retains_exact_optional_or_required_policy_and_matches_full_review(bool loadout, bool required)
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var inputs = Load(context, loadout);
        var request = Request(inputs, required ? CharacterCreationPolicyRequirement.SkillLimits : CharacterCreationPolicyRequirement.None);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        Assert.AreEqual(CharacterCreationSelectedCompletionWitness.SchemaV3, witness.Schema);
        Assert.AreEqual((CharacterCreationPolicyRequirement?)request.PolicyRequirement, witness.PolicyRequirement);
        Assert.AreEqual(request.EffectivePolicyDigest, witness.EffectivePolicy!.PolicyDigest);
        Assert.AreEqual(JsonSerializer.Serialize(inputs.EffectivePolicy), JsonSerializer.Serialize(witness.EffectivePolicy));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var decodedBootstrap, out var decoded));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(decodedBootstrap, decoded, out var repeated));
        Assert.AreEqual(JsonSerializer.Serialize(archive), JsonSerializer.Serialize(repeated));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, decodedBootstrap, decoded, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(review.Value!.Balances), JsonSerializer.Serialize(replay.Value!.Balances));
        Assert.AreEqual(JsonSerializer.Serialize(review.Value.Loadout), JsonSerializer.Serialize(replay.Value.Loadout));
    }

    [TestMethod]
    public void Policy_witness_rejects_missing_forged_altered_and_downgraded_bindings_after_resealing()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var inputs = Load(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        var policy = witness.EffectivePolicy!;
        var forgedSettings = policy.Settings.ToArray();
        forgedSettings[0] = forgedSettings[0] with { IntegerValue = 5 };
        var altered = Seal(policy with { BooksInputsDigest = CharacterCreationFinalizationDigest.Compute("changed books") });
        foreach (var changed in new[]
        {
            witness with { EffectivePolicy = null },
            witness with { PolicyRequirement = null },
            witness with { PolicyRequirement = CharacterCreationPolicyRequirement.None },
            witness with { Schema = CharacterCreationSelectedCompletionWitness.SchemaV1 },
            witness with { Schema = CharacterCreationSelectedCompletionWitness.SchemaV2 },
            witness with { Schema = "unsupported" },
            witness with { EffectivePolicy = altered },
            witness with { EffectivePolicy = policy with { PolicyDigest = CharacterCreationFinalizationDigest.Compute("forged") } }
        })
        {
            var resealed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, resealed, request).Outcome);
            if (!changed.HasVersionedShape())
                Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, resealed, out _));
        }
        foreach (var forgedPolicy in new[]
        {
            policy with { Settings = forgedSettings },
            policy with { Sources = [] },
            policy with { Schema = "chummer.creation-effective-policy.v2" },
            policy with { EnabledSourcebooks = [] },
            policy with { ProfileInputsDigest = CharacterCreationFinalizationDigest.Compute("wrong profile") }
        })
        {
            var forged = Seal(forgedPolicy);
            var changedRequest = request with { EffectivePolicyDigest = forged.PolicyDigest };
            var changed = witness with { EffectivePolicy = forged, ProposalDigest = CharacterCreationFinalizationDigest.Compute(changedRequest) };
            changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, changedRequest).Outcome);
        }
        foreach (var changedRequest in new[]
        {
            request with { EffectivePolicyDigest = null },
            request with { EffectivePolicyDigest = CharacterCreationFinalizationDigest.Compute("wrong binding") },
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.None },
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete },
            request with { PolicyRequirement = (CharacterCreationPolicyRequirement)99 }
        })
        {
            var changed = witness with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(changedRequest) };
            changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, changedRequest).Outcome);
        }
        var completeRequest = request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete };
        var completeWitness = witness with { PolicyRequirement = completeRequest.PolicyRequirement,
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(completeRequest) };
        completeWitness = completeWitness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(completeWitness) };
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, completeWitness, completeRequest).Outcome);
    }

    [TestMethod]
    public void Policy_collections_cannot_mutate_an_admitted_capture_or_an_encoded_archive()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var inputs = Load(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var admitted = CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.Evaluate(inputs, request, default);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, admitted.Outcome);
        ((IList<CharacterCreationPolicySettingCoverage>)inputs.EffectivePolicy!.Settings)[0] = inputs.EffectivePolicy.Settings[0] with { IntegerValue = 99 };
        var captured = admitted.Value!.Capture(original, bootstrap, default);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        string digest = witness.WitnessDigest;
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        ((IList<CharacterCreationPolicySource>)witness.EffectivePolicy!.Sources)[0] = witness.EffectivePolicy.Sources[0] with { ContentDigest = null };
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, request).Outcome);
        var recaptured = admitted.Value.Capture(original, bootstrap, default);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, recaptured.Outcome);
        Assert.AreEqual(digest, recaptured.Value!.WitnessDigest);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        Assert.AreEqual(digest, coldWitness.WitnessDigest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, request).Outcome);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Legacy_witness_V1_and_V2_keep_the_exact_pre_policy_json_and_digest_shape(bool loadout)
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = loadout ? reader.LoadCompletionInputsWithLoadout(owners.Capture(), new(context.WorkspaceId))
            : reader.LoadCompletionInputs(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
        var request = Request(loaded.Value!, CharacterCreationPolicyRequirement.None);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, loaded.Value!, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        Assert.AreEqual(loadout ? CharacterCreationSelectedCompletionWitness.SchemaV2 : CharacterCreationSelectedCompletionWitness.SchemaV1, witness.Schema);
        var historical = JsonSerializer.SerializeToNode(new
        {
            witness.Schema, witness.ProvenanceInputsSnapshotDigest, witness.ProvenanceReviewDigest,
            witness.Skills, witness.Qualities, witness.Magic, witness.Gear, witness.Resources, witness.Lifestyles,
            witness.BootstrapBinding, witness.ContactsPolicy, witness.CarryoverPolicy, witness.ContactsSourceProvenanceSeed,
            witness.ProposedQualityProjectionProvenanceSeed, witness.ProposalDigest, WitnessDigest = string.Empty
        })!.AsObject();
        if (loadout) historical.Add("Loadout", JsonSerializer.SerializeToNode(witness.Loadout));
        Assert.AreEqual(JsonSerializer.Serialize(historical), JsonSerializer.Serialize(witness with { WitnessDigest = string.Empty }));
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(historical), witness.WitnessDigest);
        Assert.IsFalse(JsonSerializer.SerializeToElement(request).TryGetProperty("EffectivePolicyDigest", out _));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var decodedBootstrap, out var decoded));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(decodedBootstrap, decoded, out var repeated));
        Assert.AreEqual(JsonSerializer.Serialize(archive), JsonSerializer.Serialize(repeated));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Policy_apply_cold_reopen_retains_historical_evidence_after_live_source_change(bool required)
    {
        using var context = ReadyContext.Create(true, amendSettings: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var inputs = Load(context);
        var request = Request(inputs, required ? CharacterCreationPolicyRequirement.SkillLimits : CharacterCreationPolicyRequirement.None);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var review = service.Review(owners.Capture(), context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!);
        var applied = service.Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        File.AppendAllText(Path.Combine(context.Directory, "source", "data", "weapons.xml"), "\n<!-- policy source changed -->");
        var cold = new FileWorkspaceStore(context.Directory);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success, reopened.Error);
        var archive = reopened.Value!.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId, archive, applied.Value!));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive.CompletionAuthority!.SelectedReplay, out _, out var witness));
        Assert.AreEqual(request.EffectivePolicyDigest, witness.EffectivePolicy!.PolicyDigest);
        Assert.AreEqual((CharacterCreationPolicyRequirement?)request.PolicyRequirement, witness.PolicyRequirement);
        var repeated = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries)
            .Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, repeated.Outcome, string.Join(",", repeated.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, repeated.Value!.ReceiptDigest, "A committed receipt is historical idempotency, not a new Apply authorization.");
        Assert.AreEqual(reopened.Value.ContentRevision, cold.Get(context.WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Current_policy_apply_refuses_same_profile_or_policy_only_source_drift(bool profile, bool duringCommit)
    {
        using var context = ReadyContext.Create(true, amendSettings: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var inputs = Load(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request).Value!;
        var review = service.Review(owners.Capture(), context.WorkspaceId, request, new(cash.AuthorityDigest, cash.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        string before = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value);
        string path = Path.Combine(context.Directory, "source", "data", profile
            ? CharacterCreationBootstrapProfiles.SettingsSourceFile(inputs.EffectivePolicy!.SettingsProfileId) : "weapons.xml");
        int changes = 0;
        void Change() { File.AppendAllText(path, "\n<!-- changed bytes under the same profile -->"); changes++; }
        if (duringCommit) probe.AfterFlush = Change; else Change();
        Assert.IsNull(service.Apply(owners.Capture(), Command(review.Value!)).Value);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(before, JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Current_policy_apply_rejects_owner_ABA_and_source_ABA_inside_a_live_observation()
    {
        using var context = ReadyContext.Create(true, amendSettings: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var inputs = Load(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request).Value!;
        var review = service.Review(stamp, context.WorkspaceId, request, new(cash.AuthorityDigest, cash.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        owners.Set(new OwnerScope("policy-other-owner"));
        Assert.IsNull(service.Apply(stamp, command).Value);
        owners.Set(OwnerScope.LocalSingleUser);
        Assert.IsNull(service.Apply(stamp, command).Value);

        string root = Path.Combine(context.Directory, "source");
        string path = Path.Combine(root, "data", "weapons.xml");
        byte[] bytes = File.ReadAllBytes(path);
        DateTime time = File.GetLastWriteTimeUtc(path);
        bool changed = false;
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null), file =>
        {
            if (changed || file != path) return;
            changed = true;
            File.AppendAllText(path, "\n<!-- intermediate B -->");
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, time);
        });
        var denied = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, resolver, context.Queries)
            .Apply(owners.Capture(), command);
        Assert.IsTrue(changed, "Exercise the real resolver's policy-source observation.");
        Assert.IsNull(denied.Value);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static CharacterCreationCompletionApplyRequest Command(CharacterCreationCompletionApplyReview review)
        => new(review.Projection.Binding, review.Proposal, review.Projection.ReviewDigest,
            review.Projection.StartingCash.Choice, review.Projection.ProjectionDigest, "effective-policy-completion", true);

    private static CharacterCreationEffectivePolicyContext Seal(CharacterCreationEffectivePolicyContext policy)
        => policy with { PolicyDigest = CharacterCreationEffectivePolicyContext.ComputeDigest(policy) };

    private sealed class PolicyWriteProbe : IFileWorkspaceStoreFaultInjector
    {
        public Action? AfterFlush { get; set; }
        public Action? AfterReplace { get; set; }
        public int Replacements { get; private set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
            {
                var action = AfterFlush;
                AfterFlush = null;
                action?.Invoke();
            }
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced)
            {
                Replacements++;
                var action = AfterReplace;
                AfterReplace = null;
                action?.Invoke();
            }
        }
    }


    [TestMethod]
    public void Consumer_policy_review_uses_saved_quality_excess_and_contact_expression_without_repricing_sources()
    {
        var costs = new List<int>();
        var allowances = new List<int>();
        int? originalQualityCost = null;
        foreach (bool doubled in new[] { false, true })
        {
            using var context = ReadyContext.Create(includeGearReview: false, amendSettings: settings =>
            {
                settings.SetElementValue("qualitykarmalimit", "1");
                settings.SetElementValue("exceedpositivequalities", "True");
                settings.SetElementValue("exceedpositivequalitiescostdoubled", doubled ? "True" : "False");
                settings.SetElementValue("contactpointsexpression", doubled ? "{CHAUnaug} * 5" : "{CHAUnaug} * 3");
            });
            string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
            var inputs = LoadConsumers(context);
            var policy = inputs.EffectivePolicy!.Consumers!;
            var guts = inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == "Guts" && row.IsSelectable);
            Assert.IsTrue(guts.KarmaCost > 1);
            originalQualityCost ??= guts.KarmaCost;
            Assert.AreEqual(originalQualityCost.Value, guts.KarmaCost, "Catalog Karma stays source-owned.");
            Assert.AreEqual(1, policy.Qualities.CostPolicy.KarmaMultiplier);
            Assert.AreEqual(doubled, policy.Qualities.CostPolicy.DoublePositiveExcess);
            Assert.IsTrue(policy.Qualities.MayExceedPositiveQualityLimit);
            Assert.AreEqual(inputs.Contacts.Policy.ContactPointsExpression, policy.Contacts.ContactPointsExpression);

            var contact = new CharacterCreationKarmaContactSelection(Guid.NewGuid(),
                new CharacterCreationContactIdentity("Policy Medic", "Professional", "Seattle", "", "",
                    "Human", "Other", "Adult", "Professional", "Nuyen", "", "", ""), 2, 2);
            var request = Request(inputs, CharacterCreationPolicyRequirement.None);
            request = request with
            {
                Selections = request.Selections with { QualityOptionIds = [guts.OptionId], Contacts = [contact] }
            };
            var result = CharacterCreationCompletionReviewService.Review(inputs, request);
            Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
            var review = result.Value!;
            Assert.IsNotNull(review.UpstreamBudget);
            Assert.IsNotNull(review.Contacts);
            int expectedCost = guts.KarmaCost + (doubled ? guts.KarmaCost - 1 : 0);
            Assert.AreEqual((int?)(inputs.Skills.PrerequisiteDraft.CreationKarmaTotal - expectedCost),
                review.Balances.KarmaAfterAttributesAndQualities);
            Assert.AreEqual(doubled ? 5 : 3, review.Contacts!.ContactBudget.Total);
            Assert.AreEqual(4, review.Contacts.ContactBudget.Used);
            if (doubled)
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            else
                CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationContactsBlockers.BudgetExceeded);
            costs.Add(expectedCost);
            allowances.Add(review.Contacts.ContactBudget.Total);
            Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        }
        Assert.IsTrue(costs[1] > costs[0]);
        CollectionAssert.AreEqual(new[] { 3, 5 }, allowances.ToArray());
    }

    [TestMethod]
    public void Consumer_policy_rejects_forged_values_and_coverage_after_public_rehash_and_keeps_complete_closed()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = LoadConsumers(context);
        var policy = inputs.EffectivePolicy!;
        var consumers = policy.Consumers!;
        var baseline = CharacterCreationCompletionReviewService.Review(inputs, Request(inputs, CharacterCreationPolicyRequirement.None));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, baseline.Outcome, string.Join(",", baseline.Blockers));
        var forgedContacts = consumers.Contacts with { ContactPointsExpression = "{CHAUnaug} * 99" };
        forgedContacts = forgedContacts with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(forgedContacts) };
        foreach (var forged in new[]
        {
            consumers with { Contacts = forgedContacts },
            consumers with { Qualities = consumers.Qualities with { QualityKarmaLimit = consumers.Qualities.QualityKarmaLimit + 1 } },
            consumers with { Qualities = consumers.Qualities with
                { CostPolicy = consumers.Qualities.CostPolicy with { DoublePositiveExcess = !consumers.Qualities.CostPolicy.DoublePositiveExcess } } },
            consumers with { OriginalInputsDigest = CharacterCreationFinalizationDigest.Compute("another original observation") },
            consumers with { Coverage = consumers.Coverage.Skip(1).ToArray() },
            consumers with { Coverage = consumers.Coverage.Select(row => row with
                { Disposition = CharacterCreationConsumerPolicyDisposition.Admitted,
                    Reason = CharacterCreationConsumerPolicyReason.MatchedConsumerInputs }).ToArray() }
        })
        {
            var rehashed = WithPolicy(inputs, policy with { Consumers = forged });
            var denied = CharacterCreationCompletionReviewService.Review(rehashed, Request(rehashed, CharacterCreationPolicyRequirement.None));
            Assert.IsNull(denied.Value);
            CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);
        }
        var forgedErrata = WithPolicy(inputs, policy with
        {
            Domains = policy.Domains.Select(row => row.Domain == CharacterCreationPolicyDomain.Errata
                ? row with { Disposition = CharacterCreationPolicyDisposition.Effective,
                    Reason = CharacterCreationPolicyReason.ResolvedAuthority } : row).ToArray()
        });
        CollectionAssert.Contains(CharacterCreationCompletionReviewService.Review(forgedErrata,
            Request(forgedErrata, CharacterCreationPolicyRequirement.None)).Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);
        foreach (var operation in new[] { CharacterCreationConsumerPolicyOperation.Amendments,
            CharacterCreationConsumerPolicyOperation.Errata, CharacterCreationConsumerPolicyOperation.CharacterMechanics,
            CharacterCreationConsumerPolicyOperation.Campaign })
            Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Missing,
                consumers.Coverage.Single(row => row.Operation == operation).Disposition);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Admitted, consumers.Coverage.Single(
            row => row.Operation == CharacterCreationConsumerPolicyOperation.ContactGroupKarma).Disposition);
        var complete = CharacterCreationCompletionReviewService.Review(inputs, Request(inputs, CharacterCreationPolicyRequirement.Complete));
        CollectionAssert.Contains(complete.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);

        var paidGroup = inputs.Contacts.Contacts.Single() with { IsGroup = true, Loyalty = 1 };
        var groupRequest = Request(inputs, CharacterCreationPolicyRequirement.None);
        var unpriced = CharacterCreationCompletionReviewService.Review(inputs,
            groupRequest with { Selections = groupRequest.Selections with { Contacts = [paidGroup] } });
        Assert.IsNotNull(unpriced.Value!.Contacts!.GroupKarma);
        Assert.IsFalse(unpriced.Blockers.Contains(CharacterCreationContactsCompletionSchemas.KarmaContributionUnavailable));
    }

    [TestMethod]
    public void Consumer_policy_is_detached_versioned_and_requires_selected_input_evidence()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var legacy = Load(context);
        var v1 = legacy.EffectivePolicy!;
        Assert.AreEqual(CharacterCreationEffectivePolicyContext.SchemaV1, v1.Schema);
        Assert.IsNull(v1.Consumers);
        Assert.IsFalse(JsonSerializer.SerializeToElement(v1).TryGetProperty("Consumers", out _));
        var historical = new
        {
            v1.Schema, v1.RulesetId, v1.BuildMethod, v1.SettingsProfileId, v1.ProfileInputsDigest,
            v1.EnabledSourcebooks, v1.BooksInputsDigest, v1.Sources, v1.Domains, v1.Settings,
            PolicyDigest = string.Empty
        };
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(historical), v1.PolicyDigest);

        var inputs = LoadConsumers(context);
        Assert.AreEqual(CharacterCreationEffectivePolicyContext.SchemaV2, inputs.EffectivePolicy!.Schema);
        Assert.AreNotEqual(legacy.SnapshotDigest, inputs.SnapshotDigest);
        Assert.AreEqual(legacy.SnapshotDigest, Load(context).SnapshotDigest);
        // Preserve the same owner-bound read-only capability normalization.
        var originalInputs = legacy with
        {
            Schema = legacy.Loadout is null ? CharacterCreationCompletionReviewSchemas.InputsV1
                : CharacterCreationCompletionReviewSchemas.InputsV2,
            EffectivePolicy = null, AttributePolicy = inputs.AttributePolicy,
            LoadoutLegalityPolicy = inputs.LoadoutLegalityPolicy, SnapshotDigest = string.Empty
        };
        Assert.AreEqual(CharacterCreationCompletionReviewRules.ComputeInputsDigest(originalInputs),
            inputs.EffectivePolicy.Consumers!.OriginalInputsDigest);
        var downgrade = WithPolicy(inputs, inputs.EffectivePolicy with { Schema = CharacterCreationEffectivePolicyContext.SchemaV1 });
        CollectionAssert.Contains(CharacterCreationCompletionReviewService.Review(downgrade,
            Request(downgrade, CharacterCreationPolicyRequirement.None)).Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);

        var original = context.Store.Get(context.WorkspaceId).Value!;
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(source, original.Document.Content, out var bootstrap));
        var selected = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs,
            Request(inputs, CharacterCreationPolicyRequirement.None));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, selected.Outcome, string.Join(",", selected.Blockers));
        Assert.AreEqual(CharacterCreationSelectedCompletionWitness.SchemaV4, selected.Value!.Schema);
        Assert.IsNotNull(selected.Value.SelectedPolicyInputs);

        var consumers = inputs.EffectivePolicy.Consumers;
        string[] anchors = inputs.Contacts.Policy.SourceAnchorIds.ToArray();
        var originalCoverage = consumers.Coverage[0];
        ((IList<string>)consumers.Contacts.SourceAnchorIds)[0] = "caller-mutated";
        ((IList<CharacterCreationConsumerPolicyCoverage>)consumers.Coverage)[0] = consumers.Coverage[0] with
        {
            Disposition = originalCoverage.Disposition == CharacterCreationConsumerPolicyDisposition.Admitted
                ? CharacterCreationConsumerPolicyDisposition.Missing : CharacterCreationConsumerPolicyDisposition.Admitted
        };
        Assert.AreNotEqual(originalCoverage, consumers.Coverage[0]);
        CollectionAssert.AreEqual(anchors, inputs.Contacts.Policy.SourceAnchorIds.ToArray());
        var fresh = LoadConsumers(context);
        CollectionAssert.AreEqual(anchors, fresh.EffectivePolicy!.Consumers!.Contacts.SourceAnchorIds.ToArray());
        Assert.AreEqual(originalCoverage, fresh.EffectivePolicy.Consumers.Coverage[0]);
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Domain_policy_uses_real_saved_attribute_costs_and_maximum_count_rules()
    {
        var costs = new List<int>();
        foreach (int rate in new[] { 4, 6 })
        {
            int cap = rate == 4 ? 1 : 2;
            using var context = ReadyContext.Create(false, amendSettings: settings =>
            {
                settings.Element("karmacost")!.SetElementValue("karmaattribute", rate);
                settings.SetElementValue("maxnumbermaxattributescreate", cap);
            });
            string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
            var inputs = LoadConsumers(context);
            var domain = inputs.EffectivePolicy!.Consumers!.Domains!.Attributes;
            Assert.AreEqual(rate, domain.Spending!.KarmaAttribute);
            Assert.AreEqual(cap, domain.EffectiveMaximumAttributesAtCap);
            var request = Request(inputs, CharacterCreationPolicyRequirement.None);
            var paid = CharacterCreationCompletionReviewService.Review(inputs, request with
            {
                Selections = request.Selections with
                {
                    AttributeAllocations = [new("BOD", 0, 1)], ChangeableAttributeIds = ["BOD"]
                }
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, paid.Outcome, string.Join(",", paid.Blockers));
            int cost = 2 * rate;
            Assert.AreEqual((int?)(inputs.Skills.PrerequisiteDraft.CreationKarmaTotal - cost),
                paid.Value!.Balances.KarmaAfterAttributesAndQualities);
            costs.Add(cost);
            var capped = CharacterCreationCompletionReviewService.Review(inputs, request with
            {
                Selections = request.Selections with
                {
                    AttributeAllocations = [new("BOD", 5, 0), new("AGI", 5, 0)], ChangeableAttributeIds = ["BOD", "AGI"]
                }
            });
            Assert.AreEqual(cap == 1, capped.Blockers.Contains(CharacterCreationAttributesBlockers.MaximumAttributeCountExceeded));
            if (cap == 2) Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capped.Outcome, string.Join(",", capped.Blockers));
            Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        }
        CollectionAssert.AreEqual(new[] { 8, 12 }, costs.ToArray());
    }

    [TestMethod]
    public void Domain_policy_prices_actual_skill_purchases_and_retains_opt_in_absence()
    {
        var costs = new List<int>();
        foreach (int rate in new[] { 3, 5 })
        {
            using var context = ReadyContext.Create(false, amendSettings: settings =>
                settings.Element("karmacost")!.SetElementValue("karmanewactiveskill", rate));
            var absent = LoadConsumers(context);
            Assert.IsNull(absent.EffectivePolicy!.Consumers!.Domains!.Skills.Purchases);
            Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Missing, absent.EffectivePolicy.Consumers.Coverage.Single(
                row => row.Operation == CharacterCreationConsumerPolicyOperation.SkillPurchases).Disposition);
            var inputs = LoadConsumers(context, includeSkillPurchases: true);
            var policy = inputs.EffectivePolicy!.Consumers!.Domains!.Skills;
            Assert.AreEqual(rate, policy.Purchases!.KarmaNewActiveSkill);
            Assert.AreEqual(inputs.Skills.Authority.UsePointsOnBrokenGroups, policy.UsePointsOnBrokenGroups);
            Assert.AreEqual(inputs.Skills.Authority.KnowledgePointsExpression, policy.KnowledgePointsExpression);
            var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
            var request = Request(inputs, CharacterCreationPolicyRequirement.None);
            var result = CharacterCreationCompletionReviewService.Review(inputs, request with
            {
                Selections = request.Selections with
                {
                    SkillKarmaPurchases = [new(pistols.SourceSkillId, CharacterCreationSkillKinds.Active, pistols.SourceNodeDigest, 1)]
                }
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.AreEqual((int?)rate, result.Value!.Balances.SkillsKarmaCost);
            costs.Add(result.Value.Balances.SkillsKarmaCost!.Value);
        }
        CollectionAssert.AreEqual(new[] { 3, 5 }, costs.ToArray());
    }

    [TestMethod]
    public void Domain_policy_keeps_mystic_grants_separate_from_saved_exchange_and_prices_the_exchange()
    {
        var costs = new List<int>();
        var slots = new List<int>();
        foreach (bool exchange in new[] { false, true })
        {
            using var context = ReadyContext.Create(false, talentValue: "Mystic Adept", mysticPowerPoints: 1,
                amendSettings: settings =>
                {
                    settings.SetElementValue("priorityspellsasadeptpowers", exchange ? "True" : "False");
                    settings.Element("karmacost")!.SetElementValue("karmamysadpp", 7);
                });
            var inputs = LoadConsumers(context);
            var policy = inputs.EffectivePolicy!.Consumers!.Domains!.Magic;
            Assert.AreEqual(inputs.Magic.SelectedTalent.Magic, policy.GrantedMagic);
            Assert.AreEqual(inputs.Magic.SelectedTalent.AdeptPowerPointBudget, policy.SourceAdeptPowerPointBudget);
            Assert.AreEqual(1, inputs.Magic.Selections.MysticAdeptPowerPoints);
            Assert.AreEqual(exchange, policy.MysticAdeptExchange!.PrioritySpellsAsPowerPoints);
            var result = CharacterCreationCompletionReviewService.Review(inputs, Request(inputs, CharacterCreationPolicyRequirement.None));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var purchase = result.Value!.UpstreamBudget!.Magic.MysticAdeptPowerPoints!;
            Assert.AreEqual(exchange ? 0 : 7, purchase.KarmaCost);
            Assert.AreEqual(exchange ? 1 : 0, purchase.ExchangedSpellSlots);
            Assert.AreEqual(policy.SourceSpellBudget - purchase.ExchangedSpellSlots, purchase.SpellBudget);
            costs.Add(purchase.KarmaCost); slots.Add(purchase.ExchangedSpellSlots);
        }
        CollectionAssert.AreEqual(new[] { 7, 0 }, costs.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, slots.ToArray());
    }

    [TestMethod]
    public void Domain_policy_preserves_decimal_resource_rates_and_saved_investment_limits()
    {
        var totals = new List<decimal>();
        foreach (decimal rate in new[] { 2000.5m, 2400.25m })
        {
            int maximum = rate == 2000.5m ? 1 : 3;
            using var context = ReadyContext.Create(false, amendSettings: settings =>
            {
                settings.SetElementValue("chargenkarmatonuyenexpression", "{Karma} * "
                    + rate.ToString(System.Globalization.CultureInfo.InvariantCulture) + " + {PriorityNuyen}");
                settings.SetElementValue("nuyenmaxbp", maximum);
                settings.SetElementValue("nuyencarryover", "1234.5");
            });
            var inputs = LoadConsumers(context);
            var policy = inputs.EffectivePolicy!.Consumers!.Domains!.Resources;
            Assert.AreEqual(rate, policy.KarmaToNuyenRate);
            Assert.AreEqual(1234.5m, policy.NuyenCarryover);
            var request = Request(inputs, CharacterCreationPolicyRequirement.None);
            var result = CharacterCreationCompletionReviewService.Review(inputs, request with
                { Selections = request.Selections with { TotalKarmaInvestment = 1 } });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.AreEqual((decimal?)(policy.PriorityOption.BasePriorityNuyen + rate), result.Value!.Balances.CreationNuyen);
            totals.Add(result.Value.Balances.CreationNuyen!.Value);
            var limited = CharacterCreationCompletionReviewService.Review(inputs, request with
                { Selections = request.Selections with { TotalKarmaInvestment = 2 } });
            Assert.AreEqual(maximum == 3, limited.Outcome == CharacterCreationFoundationOutcomes.Success);
        }
        Assert.AreEqual(399.75m, totals[1] - totals[0]);
    }

    [TestMethod]
    public void Domain_policy_availability_changes_actual_gear_and_loadout_admission()
    {
        Guid? gearId = null;
        Guid? weaponId = null;
        foreach (int maximum in new[] { 12, 0 })
        {
            using var context = ReadyContext.Create(false, amendSettings: settings =>
            {
                settings.SetElementValue("availability", maximum);
                settings.SetElementValue("multiplyrestrictedcost", "False");
                settings.SetElementValue("multiplyforbiddencost", "False");
            });
            var inputs = LoadConsumers(context, includeLoadout: true);
            var policies = inputs.EffectivePolicy!.Consumers!.Domains!;
            Assert.AreEqual(maximum, policies.Gear.MaximumAvailability);
            Assert.AreEqual(maximum, policies.Loadout!.MaximumAvailability);
            var gearCatalog = inputs.Lifestyles.Gear.State.Authority.Options;
            var weaponCatalog = inputs.Loadout!.Authority.Options;
            gearId ??= gearCatalog.First(row => row.IsSelectable && row.Availability > 0 && row.PackageCost < 1000m).SourceId;
            weaponId ??= weaponCatalog.First(row => row.IsSelectable && row.Kind == CharacterCreationLoadoutKinds.Weapon
                && row.Availability > 0 && row.UnitCost < 1000m).SourceId;
            var gear = gearCatalog.Single(row => row.SourceId == gearId);
            var weapon = weaponCatalog.Single(row => row.SourceId == weaponId && row.Kind == CharacterCreationLoadoutKinds.Weapon);
            var request = Request(inputs, CharacterCreationPolicyRequirement.None);
            var result = CharacterCreationCompletionReviewService.Review(inputs, request with
            {
                Selections = request.Selections with
                {
                    Gear = [new(gear.OptionId, 1)],
                    Loadout = new([new(weapon.OptionId)], [])
                }
            });
            Assert.AreEqual(maximum > 0, gear.IsSelectable);
            Assert.AreEqual(maximum > 0, weapon.IsSelectable);
            Assert.AreEqual(maximum > 0, result.Outcome == CharacterCreationFoundationOutcomes.Success);
            // The loadout evaluator runs first; exercise Gear independently as well.
            var gearOnly = CharacterCreationCompletionReviewService.Review(inputs, request with
                { Selections = request.Selections with { Gear = [new(gear.OptionId, 1)] } });
            Assert.AreEqual(maximum > 0, gearOnly.Outcome == CharacterCreationFoundationOutcomes.Success);
            if (maximum > 0)
            {
                Assert.AreEqual((decimal?)gear.PackageCost, result.Value!.Balances.GearCost);
                Assert.AreEqual((decimal?)weapon.UnitCost, result.Value.Balances.LoadoutCost);
            }
        }
    }

    [TestMethod]
    public void Domain_policy_observes_career_switches_without_changing_creation_prices()
    {
        Guid? sourceId = null;
        foreach (bool restricted in new[] { false, true })
        {
            using var context = ReadyContext.Create(false, amendSettings: settings =>
            {
                settings.SetElementValue("multiplyrestrictedcost", restricted ? "True" : "False");
                settings.SetElementValue("multiplyforbiddencost", "False");
                settings.SetElementValue("restrictedcostmultiplier", 2);
            });
            var inputs = LoadConsumers(context, includeLoadout: true);
            var policy = inputs.EffectivePolicy!.Consumers!.Domains!.Loadout!.LoadoutLegality!;
            Assert.AreEqual(restricted, policy.MultiplyRestrictedCost);
            sourceId ??= inputs.Loadout!.Authority.Options.First(row => row.IsSelectable
                && row.Kind == CharacterCreationLoadoutKinds.Weapon && row.Legality == CharacterCreationGearLegality.Restricted).SourceId;
            var option = inputs.Loadout!.Authority.Options.Single(row => row.SourceId == sourceId
                && row.Kind == CharacterCreationLoadoutKinds.Weapon);
            var result = CharacterCreationLoadoutCompletionService.Preview(inputs.Loadout, new([new(option.OptionId)], []));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.IsTrue(option.PricingIsExact);
            Assert.AreEqual(decimal.Parse(XElement.Parse(option.SourceNodeXml).Element("cost")!.Value,
                System.Globalization.CultureInfo.InvariantCulture), result.Value!.PurchaseCost);
            var coverage = inputs.EffectivePolicy.Consumers.Coverage.Single(
                row => row.Operation == CharacterCreationConsumerPolicyOperation.LoadoutLegalityMultiplierPricing);
            Assert.AreEqual(restricted ? CharacterCreationConsumerPolicyDisposition.NotApplicable
                : CharacterCreationConsumerPolicyDisposition.Unsupported, coverage.Disposition);
            if (restricted)
            {
                Assert.AreEqual(CharacterCreationConsumerPolicyReason.CareerPurchaseOnly, coverage.Reason);
                Assert.IsTrue(policy.HasCreationLifecycle(inputs.Skills.Binding.RawCharacterXmlDigest));
            }
            else
            {
                Assert.IsNull(policy.Lifecycle);
                var oldShape = new { policy.SettingsProfileId, policy.RawProfileInputsDigest,
                    policy.MultiplyRestrictedCost, policy.MultiplyForbiddenCost, policy.PolicyDigest };
                Assert.AreEqual(JsonSerializer.Serialize(oldShape), JsonSerializer.Serialize(policy));
            }
            Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
                Request(inputs, CharacterCreationPolicyRequirement.Complete)).Value);
            Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Unsupported,
                inputs.EffectivePolicy.Consumers.Coverage.Single(row => row.Operation ==
                    CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation).Disposition);
        }
    }

    [TestMethod]
    public void Domain_policy_free_grid_switch_changes_real_lifestyle_builtin_admission()
    {
        var counts = new List<int>();
        foreach (bool free in new[] { false, true })
        {
            using var context = ReadyContext.Create(false, amendSettings: settings =>
            {
                settings.SetElementValue("allowfreegrids", free ? "True" : "False");
                // HT itself enables free grids, independently of the setting.
                foreach (var book in settings.Element("books")!.Elements("book").Where(row => row.Value == "HT").ToArray()) book.Remove();
            });
            var inputs = LoadConsumers(context);
            Assert.AreEqual(free, inputs.EffectivePolicy!.Consumers!.Domains!.Lifestyles.FreeGridsEnabled);
            var authority = inputs.Lifestyles.Authority;
            var home = CharacterCreationCompletionReviewServiceTests.LowLifestyle(authority);
            var option = authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId);
            home = CharacterCreationLifestylesRules.AddBuiltInQualities(home, option);
            counts.Add(home.Qualities.Count(row => row.IsBuiltIn));
            var request = Request(inputs, CharacterCreationPolicyRequirement.None);
            var result = CharacterCreationCompletionReviewService.Review(inputs, request with
                { Selections = request.Selections with { Lifestyles = [home], StartingLifestyleId = home.LifestyleId } });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.AreEqual(counts[^1], result.Value!.Lifestyles!.Lifestyles.Single().Configuration.Qualities.Count(row => row.IsBuiltIn));
            if (free)
            {
                Assert.IsTrue(home.Qualities.Any(row => row.IsBuiltIn));
                // A paid selection cannot impersonate a source-provided built-in.
                var invented = home with { Qualities = [home.Qualities[0] with { InstanceId = Guid.NewGuid(), IsBuiltIn = false }] };
                var denied = CharacterCreationCompletionReviewService.Review(inputs, request with
                    { Selections = request.Selections with { Lifestyles = [invented], StartingLifestyleId = home.LifestyleId } });
                Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, denied.Outcome);
            }
        }
        Assert.AreEqual(0, counts[0]); Assert.IsTrue(counts[1] > 0);
    }

    [TestMethod]
    public void Domain_policy_rejects_rehashed_claims_for_every_domain_and_detaches_originals()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        string workspaceBefore = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = LoadConsumers(context, includeLoadout: true, includeSkillPurchases: true);
        var policy = inputs.EffectivePolicy!;
        var consumers = policy.Consumers!;
        var domains = consumers.Domains!;
        Assert.AreEqual(CharacterCreationCompletionConsumerPolicy.SchemaV3, consumers.Schema);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            CharacterCreationCompletionReviewService.Review(inputs, Request(inputs, CharacterCreationPolicyRequirement.None)).Outcome);
        var attribute = domains.Attributes.Spending! with { KarmaAttribute = domains.Attributes.Spending!.KarmaAttribute + 1 };
        attribute = attribute with { AuthorityDigest = CharacterCreationAttributePolicyAuthority.ComputeDigest(attribute) };
        var purchase = domains.Skills.Purchases! with { KarmaNewActiveSkill = domains.Skills.Purchases!.KarmaNewActiveSkill + 1 };
        purchase = purchase with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(purchase) };
        var legality = domains.Loadout!.LoadoutLegality! with { MultiplyRestrictedCost = !domains.Loadout.LoadoutLegality!.MultiplyRestrictedCost };
        legality = legality with { PolicyDigest = CharacterCreationLoadoutLegalityPolicy.ComputeDigest(legality) };
        foreach (var forged in new[]
        {
            domains with { Attributes = domains.Attributes with { Spending = attribute } },
            domains with { Attributes = domains.Attributes with { EffectiveMaximumAttributesAtCap = 99 } },
            domains with { Skills = domains.Skills with { Purchases = purchase } },
            domains with { Skills = domains.Skills with { UsePointsOnBrokenGroups = !domains.Skills.UsePointsOnBrokenGroups } },
            domains with { Magic = domains.Magic with { GrantedMagic = domains.Magic.GrantedMagic + 1 } },
            domains with { Magic = domains.Magic with { SourceAdeptPowerPointBudget = domains.Magic.SourceAdeptPowerPointBudget + 0.25m } },
            domains with { Resources = domains.Resources with { KarmaToNuyenRate = domains.Resources.KarmaToNuyenRate + 0.01m } },
            domains with { Gear = domains.Gear with { MaximumAvailability = 99 } },
            domains with { Gear = domains.Gear with { CatalogDigest = CharacterCreationFinalizationDigest.Compute("forged catalog") } },
            domains with { Lifestyles = domains.Lifestyles with { FreeGridsEnabled = !domains.Lifestyles.FreeGridsEnabled } },
            domains with { Lifestyles = domains.Lifestyles with { MetatypeCostPercent = domains.Lifestyles.MetatypeCostPercent + 0.01m } },
            domains with { Loadout = domains.Loadout with { MaximumQuantityPerLine = domains.Loadout.MaximumQuantityPerLine + 1 } },
            domains with { Loadout = domains.Loadout with { LoadoutLegality = legality } },
            domains with { Loadout = null }
        })
        {
            var changed = WithPolicy(inputs, policy with { Consumers = consumers with { Domains = forged } });
            var denied = CharacterCreationCompletionReviewService.Review(changed, Request(changed, CharacterCreationPolicyRequirement.None));
            Assert.IsNull(denied.Value);
            CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);
        }
        string originalAttributes = JsonSerializer.Serialize(inputs.Skills.Attributes.Attributes);
        // Even a replacement supplemental source claim must agree with the original
        // loadout authority's rules digest, not merely with its own recomputed hash.
        var replacedSource = inputs with { LoadoutLegalityPolicy = legality };
        var replacedOriginal = replacedSource with { EffectivePolicy = null,
            Schema = CharacterCreationCompletionReviewSchemas.InputsV2, SnapshotDigest = string.Empty };
        replacedSource = WithPolicy(replacedSource, policy with { Consumers = consumers with
        {
            OriginalInputsDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(replacedOriginal),
            Domains = domains with { Loadout = domains.Loadout with { LoadoutLegality = legality } }
        } });
        CollectionAssert.Contains(CharacterCreationCompletionReviewService.Review(replacedSource,
            Request(replacedSource, CharacterCreationPolicyRequirement.None)).Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);
        string originalAnchors = JsonSerializer.Serialize(inputs.Skills.KarmaPurchasePolicy!.SourceAnchorIds);
        ((IList<CharacterCreationAttributeProjection>)domains.Attributes.OriginalAttributes)[0] = domains.Attributes.OriginalAttributes[0] with { Current = 99 };
        ((IList<string>)domains.Skills.Purchases!.SourceAnchorIds)[0] = "caller-mutated";
        Assert.AreEqual(originalAttributes, JsonSerializer.Serialize(inputs.Skills.Attributes.Attributes));
        Assert.AreEqual(originalAnchors, JsonSerializer.Serialize(inputs.Skills.KarmaPurchasePolicy.SourceAnchorIds));
        var fresh = LoadConsumers(context, includeLoadout: true, includeSkillPurchases: true);
        Assert.AreEqual(originalAttributes, JsonSerializer.Serialize(fresh.EffectivePolicy!.Consumers!.Domains!.Attributes.OriginalAttributes));
        Assert.AreEqual(workspaceBefore, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    [DataRow(nameof(ICharacterSourceDataContext.TryResolveCreationAttributePolicy), true, true)]
    [DataRow(nameof(ICharacterSourceDataContext.TryResolveCreationAttributePolicy), false, true)]
    [DataRow(nameof(ICharacterSourceDataContext.TryResolveCreationLoadoutLegalityPolicy), true, true)]
    [DataRow(nameof(ICharacterSourceDataContext.TryResolveCreationLoadoutLegalityPolicy), false, true)]
    [DataRow(nameof(ICharacterSourceDataContext.TryResolveCreationAttributePolicy), true, false)]
    [DataRow(nameof(ICharacterSourceDataContext.TryResolveCreationAttributePolicy), false, false)]
    [DataRow(nameof(ICharacterSourceDataContext.TryResolveCreationLoadoutLegalityPolicy), true, false)]
    [DataRow(nameof(ICharacterSourceDataContext.TryResolveCreationLoadoutLegalityPolicy), false, false)]
    public void Domain_policy_supplemental_reads_refuse_real_source_drift(string method, bool profile, bool scoped)
    {
        using var context = ReadyContext.Create(false, amendSettings: _ => { });
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        string path = Path.Combine(context.Directory, "source", "data", profile
            ? CharacterCreationBootstrapProfiles.SettingsSourceFile(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
            : "weapons.xml");
        string sourceBefore = File.ReadAllText(path);
        int changes = 0;
        var resolver = new SupplementalPolicyResolver(context.Resolver)
        {
            BeforeSupplementalRead = name =>
            {
                if (name != method) return;
                File.AppendAllText(path, "\n<!-- supplemental policy source drift -->");
                changes++;
            }
        };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        ICharacterSourceDataResolver observed = scoped ? resolver : new UnscopedSupplementalPolicyResolver(resolver);
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, observed, context.Queries);
        var result = reader.LoadCompletionInputsWithConsumerPolicy(owners.Capture(), new(context.WorkspaceId), true);

        Assert.AreEqual(1, changes);
        Assert.AreEqual(scoped ? 1 : 2, resolver.PolicyReads,
            "A poisoned scope refuses recapture; an unscoped resolver returns a changed inventory.");
        Assert.AreNotEqual(sourceBefore, File.ReadAllText(path));
        CollectionAssert.Contains(resolver.FailedSupplementalReads, method,
            "The real source context must detect the mutation; the probe never invents a failure.");
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        CollectionAssert.AreEqual(new[] { scoped ? CharacterCreationEffectivePolicyContext.Unavailable
            : CharacterCreationEffectivePolicyContext.Invalid }, result.Blockers.ToArray());
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.AreEqual(scoped ? 1 : 0, resolver.DisposedScopes);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Domain_policy_unchanged_legacy_supplemental_absence_remains_missing(bool includeLoadout, bool scoped)
    {
        using var context = ReadyContext.Create(false);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var resolver = new SupplementalPolicyResolver(context.Resolver) { OmitSupplementalPolicies = true };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        ICharacterSourceDataResolver observed = scoped ? resolver : new UnscopedSupplementalPolicyResolver(resolver);
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, observed, context.Queries);
        var result = reader.LoadCompletionInputsWithConsumerPolicy(owners.Capture(), new(context.WorkspaceId), includeLoadout);

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var inputs = result.Value!;
        Assert.IsNull(inputs.AttributePolicy);
        Assert.IsNull(inputs.LoadoutLegalityPolicy);
        Assert.AreEqual(2, resolver.PolicyReads, "Absence is accepted only after recapturing the inventory.");
        Assert.AreEqual(includeLoadout ? 2 : 1, resolver.SupplementalReads.Count);
        foreach (var operation in new[] { CharacterCreationConsumerPolicyOperation.AttributeSpending,
                     CharacterCreationConsumerPolicyOperation.LoadoutLegalityExclusions })
        {
            var row = inputs.EffectivePolicy!.Consumers!.Coverage.Single(item => item.Operation == operation);
            Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Missing, row.Disposition);
            Assert.AreEqual(CharacterCreationConsumerPolicyReason.OriginalInputUnavailable, row.Reason);
        }
        var review = CharacterCreationCompletionReviewService.Review(inputs, Request(inputs, CharacterCreationPolicyRequirement.None));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        CollectionAssert.AreEqual(new[] { CharacterCreationEffectivePolicyContext.CoverageUnavailable },
            CharacterCreationCompletionReviewService.Review(inputs, Request(inputs, CharacterCreationPolicyRequirement.Complete)).Blockers.ToArray());
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.AreEqual(scoped ? 1 : 0, resolver.DisposedScopes);
    }

    [TestMethod]
    [DataRow("Metatype", 20, 2400)]
    [DataRow("Metavariant", 10, 2200)]
    [DataRow("Heritage", 5, 2100)]
    public void Source_resolver_prices_supported_racial_lifestyle_surcharge(string origin, int surcharge, int cost)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var root = XElement.Parse(context.Store.Get(context.WorkspaceId).Value!.Document.Content);
        // This detached saved-effect observation exercises the real resolver.
        // Binding it as a pending bootstrap would correctly fail attribute admission:
        // any persisted improvement requires the separate legacy import path.
        root.Element("improvements")!.Add(new XElement("improvement",
            new XElement("improvementttype", "LifestyleCost"),
            new XElement("val", surcharge),
            new XElement("enabled", 1), new XElement("improvementsource", origin)));
        var source = context.Resolver.TryCreateContext(root.ToString(SaveOptions.DisableFormatting));
        Assert.IsNotNull(source);
        Assert.IsTrue(source.TryResolveCreationLifestylesAuthority(out var authority));
        Assert.IsTrue(CharacterCreationLifestylesRules.IsValidAuthority(authority),
            string.Join(",", authority.Blockers));
        Assert.AreEqual((decimal)surcharge, authority.MetatypeCostPercent);
        var home = CharacterCreationCompletionReviewServiceTests.LowLifestyle(authority);
        Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(home, authority, out var lifestyle, out var blockers),
            string.Join(",", blockers));
        Assert.AreEqual((decimal)cost, lifestyle.Economics.CostPerIncrement);
        Assert.AreEqual((decimal)cost, lifestyle.Economics.TotalCost);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Domain_policy_reports_racial_lifestyle_support_without_general_quality_effects()
    {
        // Coverage describes the supported consumer operation. A saved racial
        // effect cannot be smuggled through the pending attribute draft to prove it.
        using var context = ReadyContext.Create(false);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = LoadConsumers(context);
        var consumers = inputs.EffectivePolicy!.Consumers!;
        var racial = consumers.Coverage.Single(row => row.Operation == CharacterCreationConsumerPolicyOperation.LifestyleMetatypeCost);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Admitted, racial.Disposition);
        Assert.AreEqual(CharacterCreationConsumerPolicyReason.MatchedConsumerInputs, racial.Reason);
        var general = consumers.Coverage.Single(row => row.Operation == CharacterCreationConsumerPolicyOperation.LifestyleCostEffects);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Unsupported, general.Disposition);
        Assert.AreEqual(CharacterCreationConsumerPolicyReason.ConsumerOperationUnavailable, general.Reason);

        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        CollectionAssert.AreEqual(new[] { CharacterCreationEffectivePolicyContext.CoverageUnavailable },
            CharacterCreationCompletionReviewService.Review(inputs, request with
                { PolicyRequirement = CharacterCreationPolicyRequirement.Complete }).Blockers.ToArray());
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Domain_policy_rejects_orphan_supplemental_policies(bool loadoutPolicy)
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumers(context, includeLoadout: true);
        Assert.IsNotNull(inputs.AttributePolicy);
        Assert.IsNotNull(inputs.LoadoutLegalityPolicy);
        var orphan = inputs with
        {
            Schema = CharacterCreationCompletionReviewSchemas.InputsV2, EffectivePolicy = null,
            AttributePolicy = loadoutPolicy ? null : inputs.AttributePolicy,
            LoadoutLegalityPolicy = loadoutPolicy ? inputs.LoadoutLegalityPolicy : null
        };
        orphan = orphan with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(orphan) };
        var denied = CharacterCreationCompletionReviewService.Review(orphan, Request(orphan, CharacterCreationPolicyRequirement.None));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
        Assert.IsNull(denied.Value);
        CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.AuthorityUnavailable }, denied.Blockers.ToArray());
    }

    [TestMethod]
    public void Domain_policy_preserves_legacy_shapes_and_rejects_unknown_or_downgraded_claims()
    {
        using var context = ReadyContext.Create(false);
        foreach (bool loadout in new[] { false, true })
        {
            var legacy = Load(context, loadout);
            Assert.IsNull(legacy.AttributePolicy);
            Assert.IsNull(legacy.LoadoutLegalityPolicy);
            Assert.IsFalse(JsonSerializer.SerializeToElement(legacy).TryGetProperty("AttributePolicy", out _));
            Assert.IsFalse(JsonSerializer.SerializeToElement(legacy).TryGetProperty("LoadoutLegalityPolicy", out _));
            var inputs = LoadConsumers(context, includeLoadout: loadout);
            var consumers = inputs.EffectivePolicy!.Consumers!;
            foreach (var malformed in new[]
            {
                consumers with { Domains = null },
                consumers with { Schema = "unknown" },
                consumers with { Schema = CharacterCreationCompletionConsumerPolicy.SchemaV1 },
                consumers with { Coverage = consumers.Coverage.Select(row => row with
                    { Disposition = CharacterCreationConsumerPolicyDisposition.Admitted,
                        Reason = CharacterCreationConsumerPolicyReason.MatchedConsumerInputs }).ToArray() }
            })
            {
                var changed = WithPolicy(inputs, inputs.EffectivePolicy with { Consumers = malformed });
                var denied = CharacterCreationCompletionReviewService.Review(changed, Request(changed, CharacterCreationPolicyRequirement.None));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
                Assert.IsNull(denied.Value);
                CollectionAssert.AreEqual(new[] { CharacterCreationEffectivePolicyContext.Invalid }, denied.Blockers.ToArray());
            }

            // Reconstruct the old consumer-v1 projection using these same real inputs.
            // This is compatibility evidence, never invented ready/source authority.
            var original = inputs with { AttributePolicy = null, LoadoutLegalityPolicy = null, EffectivePolicy = null,
                Schema = loadout ? CharacterCreationCompletionReviewSchemas.InputsV2 : CharacterCreationCompletionReviewSchemas.InputsV1 };
            var oldCoverage = consumers.Coverage.Take(16).Select(row => row.Operation switch
            {
                CharacterCreationConsumerPolicyOperation.ContactGroupKarma => row with
                    { Disposition = CharacterCreationConsumerPolicyDisposition.Unsupported,
                        Reason = CharacterCreationConsumerPolicyReason.GroupContactKarmaUnavailable },
                CharacterCreationConsumerPolicyOperation.AttributeSpending or CharacterCreationConsumerPolicyOperation.SkillPurchases
                    or CharacterCreationConsumerPolicyOperation.MysticAdeptExchange or CharacterCreationConsumerPolicyOperation.ResourcesAndAvailability
                    or CharacterCreationConsumerPolicyOperation.Gear or CharacterCreationConsumerPolicyOperation.LifestylesAndFreeGrid
                    or CharacterCreationConsumerPolicyOperation.Loadout => row with
                    { Disposition = CharacterCreationConsumerPolicyDisposition.Missing,
                        Reason = CharacterCreationConsumerPolicyReason.PolicyProjectionUnavailable },
                _ => row
            }).ToArray();
            var oldConsumers = consumers with { Schema = CharacterCreationCompletionConsumerPolicy.SchemaV1, Domains = null,
                Coverage = oldCoverage, OriginalInputsDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(original) };
            Assert.IsFalse(JsonSerializer.SerializeToElement(oldConsumers).TryGetProperty("Domains", out _));
            var oldInputs = WithPolicy(inputs with { AttributePolicy = null, LoadoutLegalityPolicy = null },
                inputs.EffectivePolicy with { Consumers = oldConsumers });
            var review = CharacterCreationCompletionReviewService.Review(oldInputs, Request(oldInputs, CharacterCreationPolicyRequirement.None));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
            Assert.AreEqual(legacy.SnapshotDigest, Load(context, loadout).SnapshotDigest);
            CollectionAssert.Contains(CharacterCreationCompletionReviewService.Review(inputs,
                Request(inputs, CharacterCreationPolicyRequirement.Complete)).Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        }
    }

    private sealed class SupplementalPolicyResolver(ICharacterSourceDataResolver inner)
        : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        public Action<string>? BeforeSupplementalRead { get; init; }
        public Action<string>? AfterSuccessfulRead { get; init; }
        public bool OmitSupplementalPolicies { get; init; }
        public int PolicyReads { get; private set; }
        public int DisposedScopes { get; private set; }
        public List<string> SupplementalReads { get; } = [];
        public List<string> FailedSupplementalReads { get; } = [];

        public ICharacterSourceDataContext? TryCreateContext(string xml) => Wrap(inner.TryCreateContext(xml));

        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
            => new Scope(this, ((ICharacterSourceDataResolverOperationScopeFactory)inner).CreateOperationScope());

        private ICharacterSourceDataContext? Wrap(ICharacterSourceDataContext? context)
        {
            if (context is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, SupplementalPolicyProbe>();
            ((SupplementalPolicyProbe)(object)proxy).Call = (method, args) =>
            {
                bool supplementalMethod = method.Name is
                    nameof(ICharacterSourceDataContext.TryResolveCreationAttributePolicy)
                    or nameof(ICharacterSourceDataContext.TryResolveCreationLoadoutLegalityPolicy);
                bool supplemental = PolicyReads > 0 && supplementalMethod;
                if (supplemental)
                {
                    SupplementalReads.Add(method.Name);
                    BeforeSupplementalRead?.Invoke(method.Name);
                }
                if (OmitSupplementalPolicies && supplementalMethod)
                {
                    args![0] = null;
                    return false;
                }
                var result = method.Invoke(context, args);
                if (method.Name == nameof(ICharacterSourceDataContext.TryResolveCreationEffectivePolicy)) PolicyReads++;
                if (supplemental && result is false) FailedSupplementalReads.Add(method.Name);
                if (result is true) AfterSuccessfulRead?.Invoke(method.Name);
                return result;
            };
            return proxy;
        }

        private sealed class Scope(SupplementalPolicyResolver probe, ICharacterSourceDataResolverOperationScope innerScope)
            : ICharacterSourceDataResolverOperationScope
        {
            public ICharacterSourceDataContext? TryCreateContext(string xml) => probe.Wrap(innerScope.TryCreateContext(xml));
            public void Dispose() { innerScope.Dispose(); probe.DisposedScopes++; }
        }
    }

    private sealed class UnscopedSupplementalPolicyResolver(SupplementalPolicyResolver inner) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml) => inner.TryCreateContext(xml);
    }

    public class SupplementalPolicyProbe : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(3, false)]
    [DataRow(3, true)]
    public void Group_contact_review_prices_the_selected_quality_cap_and_profile_rate_once(
        int rate, bool doubleExcess)
    {
        using var context = ReadyContext.Create(false, amendSettings: settings =>
        {
            settings.Element("karmacost")!.SetElementValue("karmacontact", rate);
            settings.Element("karmacost")!.SetElementValue("karmaquality", 2);
            settings.SetElementValue("qualitykarmalimit", 10);
            settings.SetElementValue("exceedpositivequalities", true);
            settings.SetElementValue("exceedpositivequalitiescostdoubled", doubleExcess);
        });
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = LoadConsumers(context);
        var quality = inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == "Ambidextrous" && row.IsSelectable);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var group = new CharacterCreationKarmaContactSelection(Guid.NewGuid(),
            new CharacterCreationContactIdentity("Selected group", "", "", "", "", "", "", "", "", "", "", "", GroupName: string.Empty), 2, 1, IsGroup: true);
        request = request with { Selections = request.Selections with
        {
            QualityOptionIds = request.Selections.QualityOptionIds.Append(quality.OptionId).Distinct().ToArray(),
            Contacts = request.Selections.Contacts.Append(group).ToArray()
        } };
        var result = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        var review = result.Value!;
        var evaluated = review.Contacts!.GroupKarma!;
        Assert.IsNotNull(evaluated);
        int qualityCost = quality.KarmaCost * 2;
        int baseKarma = 3 * rate;
        int beforeCost = qualityCost + (doubleExcess ? Math.Max(0, qualityCost - 10) : 0);
        int afterCost = qualityCost + baseKarma + (doubleExcess ? Math.Max(0, qualityCost + baseKarma - 10) : 0);
        Assert.AreEqual(baseKarma, evaluated.BaseGroupKarma);
        Assert.AreEqual(beforeCost, evaluated.BeforeGroups.NetKarmaSpent);
        Assert.AreEqual(afterCost, evaluated.WithGroups.NetKarmaSpent);
        Assert.AreEqual(afterCost - beforeCost, evaluated.AdditionalKarmaCost);
        Assert.AreEqual((int?)(afterCost - beforeCost), review.Balances.ContactsKarmaCost);
        Assert.AreEqual(review.Balances.KarmaAfterAttributesAndQualities
            - review.Balances.SkillsKarmaCost - review.Balances.MysticAdeptPowerPointKarmaCost
            - review.Balances.ResourcesKarmaInvestment - review.Balances.ContactsKarmaCost,
            review.Balances.RemainingCreationKarma);
        Assert.AreEqual(0, review.Contacts.ContactBudget.Used);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        CollectionAssert.Contains(complete.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(int.MaxValue)]
    public void Group_contact_review_keeps_limit_and_overflow_failures_distinct_from_known_prices(int rate)
    {
        using var context = ReadyContext.Create(false, amendSettings: settings =>
        {
            settings.Element("karmacost")!.SetElementValue("karmacontact", rate);
            settings.SetElementValue("qualitykarmalimit", 10);
            settings.SetElementValue("exceedpositivequalities", false);
        });
        var inputs = LoadConsumers(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        request = request with { Selections = request.Selections with { Contacts =
        [new(Guid.NewGuid(), new("Group", "", "", "", "", "", "", "", "", "", "", "", GroupName: string.Empty), 2, 1, IsGroup: true)] } };
        var result = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNotNull(result.Value);
        if (rate == 4)
        {
            Assert.AreEqual(12, result.Value!.Contacts!.GroupKarma!.BaseGroupKarma);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
        }
        else
        {
            Assert.IsNull(result.Value!.Contacts!.GroupKarma);
            Assert.IsNull(result.Value.Contacts.AdditionalKarmaCost);
            Assert.IsNull(result.Value.Balances.RemainingCreationKarma);
        }
        Assert.AreEqual(rate == int.MaxValue,
            result.Blockers.Contains(CharacterCreationContactsCompletionSchemas.KarmaContributionUnavailable));
    }

    [TestMethod]
    public void Group_contact_replay_rechecks_overflow_and_cannot_reseal_consumer_coverage()
    {
        using var context = ReadyContext.Create(true, amendSettings: settings => settings.Element("karmacost")!.SetElementValue("karmacontact", int.MaxValue));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(original);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var inputs = LoadConsumers(context);
        var baseline = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, baseline);
        Assert.IsNotNull(captured.Value, string.Join(",", captured.Blockers));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, captured.Value!, baseline);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.IsNull(replay.Value!.Contacts.GroupKarma);
        Assert.IsFalse(JsonSerializer.Serialize(replay.Value.Contacts).Contains("GroupKarma", StringComparison.Ordinal));

        var proposal = baseline with { Selections = baseline.Selections with { Contacts = baseline.Selections.Contacts.Append(
            new CharacterCreationKarmaContactSelection(Guid.NewGuid(), new("Group", "", "", "", "", "", "", "", "", "", "", "", GroupName: string.Empty),
                2, 1, IsGroup: true)).ToArray() } };
        var rehashed = captured.Value! with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(proposal) };
        rehashed = rehashed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(rehashed) };
        var denied = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, rehashed, proposal);
        CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationContactsCompletionSchemas.KarmaContributionUnavailable);
        Assert.IsNull(denied.Value);

        var consumers = inputs.EffectivePolicy!.Consumers!;
        var forged = WithPolicy(inputs, inputs.EffectivePolicy with { Consumers = consumers with
        {
            Coverage = consumers.Coverage.Select(row => row.Operation == CharacterCreationConsumerPolicyOperation.ContactGroupKarma
                ? row with { Disposition = CharacterCreationConsumerPolicyDisposition.Unsupported,
                    Reason = CharacterCreationConsumerPolicyReason.GroupContactKarmaUnavailable } : row).ToArray()
        } });
        var forgedReview = CharacterCreationCompletionReviewService.Review(forged,
            Request(forged, CharacterCreationPolicyRequirement.SkillLimits) with { Selections = proposal.Selections });
        CollectionAssert.Contains(forgedReview.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Group_contact_pricing_preserves_saved_quality_choices_and_does_not_accept_source_ABA()
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true, qualityName: "Guts",
            amendSettings: settings => settings.Element("karmacost")!.SetElementValue("karmacontact", 3));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(original);
        var inputs = LoadConsumers(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        Assert.IsTrue(request.Selections.QualityOptionIds.Count > 0);
        var removal = CharacterCreationCompletionReviewService.Review(inputs, request with
        { Selections = request.Selections with { QualityOptionIds = [] } });
        CollectionAssert.Contains(removal.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);

        using var scope = ((ICharacterSourceDataResolverOperationScopeFactory)context.Resolver).CreateOperationScope();
        var source = scope.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationContactsPolicy(out var admitted));
        Assert.AreEqual(3, admitted!.GroupContactKarmaMultiplier);
        string path = Path.Combine(context.Directory, "source", "data",
            CharacterCreationBootstrapProfiles.SettingsSourceFile(inputs.EffectivePolicy!.SettingsProfileId));
        byte[] bytes = File.ReadAllBytes(path);
        DateTime time = File.GetLastWriteTimeUtc(path);
        try
        {
            File.AppendAllText(path, "\n<!-- contact pricing source drift -->");
            Assert.IsFalse(source.TryResolveCreationContactsPolicy(out _));
        }
        finally
        {
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, time);
        }
        Assert.IsFalse(source.TryResolveCreationContactsPolicy(out _), "An observed source failure must survive byte restoration.");
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }


    [TestMethod]
    public void Creation_legality_lifecycle_requires_actual_source_and_rejects_rehashed_career_claims()
    {
        using var context = ReadyContext.Create(false, amendSettings: settings =>
        {
            settings.SetElementValue("multiplyrestrictedcost", true);
            settings.SetElementValue("multiplyforbiddencost", true);
            settings.SetElementValue("restrictedcostmultiplier", 2);
            settings.SetElementValue("forbiddencostmultiplier", 3);
        });
        var inputs = LoadConsumers(context, includeLoadout: true);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var xml = XElement.Parse(original.Document.Content);
        xml.SetElementValue("created", true);
        var source = context.Resolver.TryCreateContext(xml.ToString(SaveOptions.DisableFormatting))!;
        Assert.IsNotNull(source);
        Assert.IsFalse(source.TryResolveCreationLoadoutLegalityPolicy(out _),
            "A Creation claim must not be minted from actual career XML.");
        var policy = inputs.LoadoutLegalityPolicy! with
            { Lifecycle = inputs.LoadoutLegalityPolicy!.Lifecycle! with { Created = true } };
        policy = policy with { PolicyDigest = CharacterCreationLoadoutLegalityPolicy.ComputeDigest(policy) };
        var changed = inputs with { LoadoutLegalityPolicy = policy };
        changed = WithPolicy(changed, CharacterCreationCompletionConsumerPolicyProjection.Attach(inputs.EffectivePolicy!, changed));
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(changed,
            Request(changed, CharacterCreationPolicyRequirement.None)).Value);

        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.IsNotNull(capture.Value, string.Join(",", capture.Blockers));
        var witness = capture.Value!;
        var consumers = witness.EffectivePolicy!.Consumers!;
        var effective = Seal(witness.EffectivePolicy with { Consumers = consumers with
        { Domains = consumers.Domains! with { Loadout = consumers.Domains.Loadout! with { LoadoutLegality = policy } } } });
        var selected = witness.SelectedPolicyInputs! with { Policy = witness.SelectedPolicyInputs!.Policy with
        { Loadout = witness.SelectedPolicyInputs.Policy.Loadout! with { LoadoutLegality = policy } } };
        selected = selected with { InputsDigest = CharacterCreationSelectedPolicyInputs.ComputeDigest(selected) };
        var forgedRequest = request with { EffectivePolicyDigest = effective.PolicyDigest };
        var forged = witness with { LoadoutLegalityPolicy = policy, EffectivePolicy = effective,
            SelectedPolicyInputs = selected, ProposalDigest = CharacterCreationFinalizationDigest.Compute(forgedRequest) };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, forgedRequest);
        Assert.IsNull(replay.Value);
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    private static CharacterCreationCompletionReviewInputs LoadConsumers(ReadyContext context,
        bool includeLoadout = false, bool includeSkillPurchases = false)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader =
            new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var result = reader.LoadCompletionInputsWithConsumerPolicy(owners.Capture(),
            new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = includeSkillPurchases }, includeLoadout);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value!.EffectivePolicy!.Consumers);
        Assert.AreEqual(result.Value.SnapshotDigest, CharacterCreationCompletionReviewRules.ComputeInputsDigest(result.Value));
        return result.Value;
    }

    private static CharacterCreationCompletionReviewInputs Load(ReadyContext context, bool includeLoadout = false)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var result = reader.LoadCompletionInputsWithEffectivePolicy(owners.Capture(), new(context.WorkspaceId), includeLoadout);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value!.EffectivePolicy);
        return result.Value;
    }

    private static CharacterCreationCompletionReviewRequest Request(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationPolicyRequirement requirement) => new(inputs.SnapshotDigest,
        CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        {
            Loadout = inputs.Loadout?.OriginalSelections,
            SkillKarmaPurchases = inputs.Skills.KarmaPurchasePolicy is null ? null : []
        })
        { PolicyRequirement = requirement, EffectivePolicyDigest = inputs.EffectivePolicy?.PolicyDigest,
            PolicyConsumptionSchema = inputs.EffectivePolicy?.SettingObservations is null ? null : CharacterCreationPolicyConsumption.SchemaV1 };

    private static CharacterCreationCompletionReviewInputs WithPolicy(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationEffectivePolicyContext policy)
    {
        policy = policy with { PolicyDigest = CharacterCreationEffectivePolicyContext.ComputeDigest(policy) };
        var changed = inputs with { EffectivePolicy = policy };
        return changed with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(changed) };
    }
}
