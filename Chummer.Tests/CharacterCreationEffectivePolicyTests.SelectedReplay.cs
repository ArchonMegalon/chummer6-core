using System.Diagnostics;
using System.Reflection;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(CharacterCreationCompletionConsumerPolicy.SchemaV2)]
    [DataRow(CharacterCreationCompletionConsumerPolicy.SchemaV3)]
    public void Compensation_coverage_preserves_legacy_projection_and_rejects_resealed_old_claims(string schema)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True"));
        var current = LoadConsumers(context, includeSkillPurchases: true);
        Assert.AreEqual(CharacterCreationCompletionConsumerPolicy.SchemaV4, current.EffectivePolicy!.Consumers!.Schema);
        // Build the historical observation before review/capture. Never rewrite a
        // successful evaluation or authority to manufacture compatibility.
        var legacyCoverage = CharacterCreationCompletionDomainPolicyProjection.DescribeCoverage(current,
            CharacterCreationCompletionConsumerPolicyProjection.DescribeCoverage(
                schema == CharacterCreationCompletionConsumerPolicy.SchemaV3));
        var inputs = WithPolicy(current, current.EffectivePolicy with
        {
            Consumers = current.EffectivePolicy.Consumers with { Schema = schema, Coverage = legacyCoverage }
        });
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        Assert.AreEqual(CharacterCreationSelectedCompletionWitness.SchemaV4, witness.Schema);
        Assert.AreEqual(CharacterCreationSelectedPolicyInputs.SchemaV1, witness.SelectedPolicyInputs!.Schema);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Unsupported,
            witness.SelectedPolicyInputs.Policy.Coverage.Single(row =>
                row.Operation == CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation).Disposition);
        string retained = JsonSerializer.Serialize(witness);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(retained, JsonSerializer.Serialize(coldWitness));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(coldBootstrap, coldWitness, out var repeated));
        Assert.AreEqual(JsonSerializer.Serialize(archive), JsonSerializer.Serialize(repeated));

        var coverage = legacyCoverage.Select(row => row.Operation == CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation
            ? row with { Disposition = CharacterCreationConsumerPolicyDisposition.Admitted,
                Reason = CharacterCreationConsumerPolicyReason.MatchedConsumerInputs } : row).ToArray();
        var policy = Seal(witness.EffectivePolicy! with { Consumers = witness.EffectivePolicy!.Consumers! with { Coverage = coverage } });
        var forgedRequest = request with { EffectivePolicyDigest = policy.PolicyDigest };
        var selected = witness.SelectedPolicyInputs with { Policy = witness.SelectedPolicyInputs.Policy with { Coverage = coverage } };
        selected = selected with { InputsDigest = CharacterCreationSelectedPolicyInputs.ComputeDigest(selected) };
        var changed = witness with { EffectivePolicy = policy, SelectedPolicyInputs = selected,
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(forgedRequest) };
        changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
        var refused = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, forgedRequest);
        CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);
        Assert.IsNull(refused.Value);

        var currentRequest = Request(current, CharacterCreationPolicyRequirement.None);
        var currentCapture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, current, currentRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, currentCapture.Outcome, string.Join(",", currentCapture.Blockers));
        foreach (string oldSchema in new[] { CharacterCreationSelectedCompletionWitness.SchemaV2,
            CharacterCreationSelectedCompletionWitness.SchemaV3, CharacterCreationSelectedCompletionWitness.SchemaV4 })
        {
            var relabeled = currentCapture.Value! with { Schema = oldSchema };
            relabeled = relabeled with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(relabeled) };
            Assert.IsFalse(relabeled.HasVersionedShape());
            Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, relabeled, out _));
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, relabeled, currentRequest).Value);
        }
    }

    [TestMethod]
    [DataRow(0, true, 1, "Acrobatic Defender")]
    [DataRow(2, false, 1, "Acrobatic Defender")]
    [DataRow(2, true, 1, "Acrobatic Defender")]
    [DataRow(1, true, 2, "Acrobatic Defender")]
    [DataRow(2, true, 1, "Common Sense")]
    public void V4_paid_groups_finish_with_one_marginal_expense_and_durable_replay(
        int rate, bool doubleExcess, int groups, string qualityName)
    {
        using var context = CreateGroupContext(rate, doubleExcess, qualityName);
        var inputs = LoadConsumers(context, includeLoadout: true, includeSkillPurchases: true);
        var request = AddGroups(Request(inputs, CharacterCreationPolicyRequirement.SkillLimits), groups);
        Assert.IsTrue(request.Selections.QualityOptionIds.Count > 0);
        Assert.IsTrue(request.Selections.SkillAllocations.Any(row => row.Kind == CharacterCreationSkillKinds.Active && row.Rating > 0));
        Assert.IsTrue(request.Selections.GroupAllocations.Any(row => row.Rating > 0));
        Assert.IsTrue(request.Selections.Gear.Count > 0);
        Assert.IsTrue(request.Selections.Lifestyles.Count > 0);
        Assert.IsTrue(request.Selections.MagicSelections.Spells.Count > 0);
        Assert.IsTrue(request.Selections.MagicSelections.AdeptPowers.Count > 0);
        Assert.AreEqual(groups + 1, request.Selections.Contacts.Count);
        Assert.AreEqual(rate, inputs.Contacts.Policy.GroupContactKarmaMultiplier);
        var qualityAuthority = inputs.Lifestyles.Qualities.Authority;
        var quality = qualityAuthority.Options.Single(row => request.Selections.QualityOptionIds.Contains(row.OptionId));
        Assert.AreEqual(qualityName, quality.Name);
        Assert.IsTrue(quality.IsSelectable && quality.EligibilityIsExact, quality.DisableReasonKey);
        Assert.IsTrue(quality.CountsAgainstQualityLimit && quality.CountsAgainstKarma && !quality.IsFreeOrGranted);
        CollectionAssert.Contains(inputs.EffectivePolicy!.EnabledSourcebooks.ToArray(),
            XElement.Parse(quality.SourceNodeXml).Element("source")!.Value);
        Assert.AreEqual(2, qualityAuthority.CostPolicy!.KarmaMultiplier);
        Assert.AreEqual(10, qualityAuthority.QualityKarmaLimit);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var price = review.Value!.Contacts!.GroupKarma!;
        int before = quality.KarmaCost * qualityAuthority.CostPolicy.KarmaMultiplier;
        int limit = qualityAuthority.QualityKarmaLimit;
        int group = groups * 3 * rate;
        int expected = group + (doubleExcess ? Math.Max(0, before + group - limit) - Math.Max(0, before - limit) : 0);
        Assert.AreEqual(group, price.BaseGroupKarma);
        Assert.AreEqual(expected, price.AdditionalKarmaCost);
        Assert.AreEqual((int?)expected, review.Value.Balances.ContactsKarmaCost);
        Assert.AreEqual(2, review.Value.Contacts.ContactBudget.Used, "Saved individual contact still uses the free pool.");
        var removal = CharacterCreationCompletionReviewService.Review(inputs, request with
        { Selections = request.Selections with { Contacts = request.Selections.Contacts.Where(row => row.IsGroup).ToArray() } });
        CollectionAssert.Contains(removal.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
        var qualityRemoval = CharacterCreationCompletionReviewService.Review(inputs, request with
        { Selections = request.Selections with { QualityOptionIds = [] } });
        CollectionAssert.Contains(qualityRemoval.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        CollectionAssert.Contains(complete.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        AssertV4Commit(context, inputs, request, review.Value);
    }

    [TestMethod]
    [DataRow("source-before")]
    [DataRow("source-at-commit")]
    [DataRow("source-aba")]
    [DataRow("owner-aba")]
    public void V4_paid_group_apply_retains_current_source_and_owner_guards(string change)
    {
        using var context = CreateGroupContext(2, true);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var inputs = LoadConsumers(context);
        var request = AddGroups(Request(inputs, CharacterCreationPolicyRequirement.SkillLimits), 1);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.IsNotNull(cash.Value, string.Join(",", cash.Blockers));
        var reviewed = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        string before = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value);
        string sourceRoot = Path.Combine(context.Directory, "source");
        string path = Path.Combine(sourceRoot, "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(inputs.EffectivePolicy!.SettingsProfileId));
        byte[] bytes = File.ReadAllBytes(path);
        DateTime time = File.GetLastWriteTimeUtc(path);
        void Drift() => File.AppendAllText(path, "\n<!-- group contact policy changed -->");
        bool changed = false;
        if (change == "source-before") Drift();
        if (change == "source-at-commit") probe.AfterFlush = Drift;
        if (change == "owner-aba")
        {
            // Review released its lease. Returning to the same owner must not
            // make the captured transition stamp current again.
            Assert.AreEqual(0, owners.ActiveLeases);
            owners.Set(new OwnerScope("contact-other-owner"));
            owners.Set(stamp.Owner);
            var current = owners.Capture();
            Assert.AreEqual(stamp.Owner, current.Owner);
            Assert.AreEqual(stamp.TransitionRevision + 2, current.TransitionRevision);
            Assert.AreNotEqual(stamp, current);
        }
        if (change == "source-aba")
        {
            var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(sourceRoot, sourceRoot, null), file =>
            {
                if (changed || file != path) return;
                changed = true;
                Drift();
                File.WriteAllBytes(path, bytes);
                File.SetLastWriteTimeUtc(path, time);
            });
            service = new OwnerBoundCharacterCreationCompletionService(store, owners, resolver, context.Queries);
        }
        var denied = service.Apply(stamp, Command(reviewed.Value!));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
        Assert.IsNull(denied.Value);
        if (change == "owner-aba")
            CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.SnapshotConflict }, denied.Blockers.ToArray());
        if (change == "source-aba")
        {
            Assert.IsTrue(changed);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(4, 10, false)]
    [DataRow(9, 100, true)]
    [DataRow(int.MaxValue, 100, true)]
    public void Paid_group_illegal_caps_shared_wallet_and_overflow_never_gain_save_authority(int rate, int limit, bool excess)
    {
        using var context = ReadyContext.Create(true, amendSettings: settings =>
        {
            settings.Element("karmacost")!.SetElementValue("karmacontact", rate);
            settings.SetElementValue("qualitykarmalimit", limit);
            settings.SetElementValue("exceedpositivequalities", excess);
        });
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = LoadConsumers(context);
        var request = AddGroups(Request(inputs, CharacterCreationPolicyRequirement.SkillLimits), 1);
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, reviewed.Outcome);
        Assert.IsNotNull(reviewed.Value);
        if (rate == int.MaxValue)
        {
            Assert.IsNull(reviewed.Value!.Balances.ContactsKarmaCost);
            Assert.IsNull(reviewed.Value.Balances.RemainingCreationKarma);
        }
        else
        {
            Assert.AreEqual((int?)(3 * rate), reviewed.Value!.Balances.ContactsKarmaCost);
            Assert.IsNotNull(reviewed.Value.Balances.RemainingCreationKarma);
            if (!excess) CollectionAssert.Contains(reviewed.Blockers.ToArray(), CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
            else Assert.IsTrue(reviewed.Value.Balances.KarmaOverspend > 0);
        }
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        Assert.IsNull(service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request).Value);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void V4_paid_group_resealed_witness_rate_and_projected_charge_are_not_save_authority()
    {
        using var context = CreateGroupContext(2, true);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var inputs = LoadConsumers(context);
        var request = AddGroups(Request(inputs, CharacterCreationPolicyRequirement.SkillLimits), 1);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.IsNotNull(capture.Value, string.Join(",", capture.Blockers));
        var policy = capture.Value!.ContactsPolicy with { GroupContactKarmaMultiplier = 0 };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(policy) };
        var forged = capture.Value with { ContactsPolicy = policy };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, request).Value);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request).Value!;
        var review = service.Review(owners.Capture(), context.WorkspaceId, request, new(cash.AuthorityDigest, cash.Dice * 3));
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        var fake = review.Value!.Projection with
        {
            KarmaBeforeCarryover = review.Value.Projection.KarmaBeforeCarryover + 1,
            OrderedDeltas = review.Value.Projection.OrderedDeltas.Select(row => row.DeltaId == "contacts:karma"
                ? row with { KarmaCost = 0, AfterValue = "0" } : row).ToArray()
        };
        fake = fake with { ProjectionDigest = CharacterCreationCompletionFinalizationRules.ComputeProjectionDigest(fake) };
        Assert.IsNull(service.Apply(owners.Capture(), Command(review.Value) with { ProjectionDigest = fake.ProjectionDigest }).Value);
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void V4_historical_no_group_consumer_policy_and_contact_bytes_replay_without_migration()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = LoadConsumers(context);
        var policy = inputs.EffectivePolicy!;
        var legacy = WithPolicy(inputs, policy with { Consumers = policy.Consumers! with
        {
            Schema = CharacterCreationCompletionConsumerPolicy.SchemaV2,
            Coverage = policy.Consumers!.Coverage.Select(row => row.Operation == CharacterCreationConsumerPolicyOperation.ContactGroupKarma
                ? row with { Disposition = CharacterCreationConsumerPolicyDisposition.Unsupported,
                    Reason = CharacterCreationConsumerPolicyReason.GroupContactKarmaUnavailable } : row).ToArray()
        } });
        var request = Request(legacy, CharacterCreationPolicyRequirement.SkillLimits);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var review = CharacterCreationCompletionReviewService.Review(legacy, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var contact = review.Value!.Contacts!;
        Assert.IsNull(contact.GroupKarma);
        Assert.AreEqual(JsonSerializer.Serialize(new { contact.Schema, contact.Binding, contact.ContactsSnapshotDigest,
            contact.Upstream, contact.Contacts, contact.ContactBudget, contact.HighPlacesBudget, contact.AdditionalKarmaCost,
            contact.Blockers, contact.ProposalDigest }), JsonSerializer.Serialize(contact));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, legacy, request);
        Assert.IsNotNull(captured.Value, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var encoded));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(encoded, out var reopenedBootstrap, out var reopened));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, reopenedBootstrap, reopened, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(review.Value.Balances), JsonSerializer.Serialize(replay.Value!.Balances));
        Assert.IsNull(replay.Value.Contacts.GroupKarma);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(reopenedBootstrap, reopened, out var repeated));
        Assert.AreEqual(JsonSerializer.Serialize(encoded), JsonSerializer.Serialize(repeated));
    }

    private static CharacterCreationCompletionReviewRequest AddGroups(CharacterCreationCompletionReviewRequest request, int count)
        => request with { Selections = request.Selections with
        {
            Contacts = request.Selections.Contacts.Concat(Enumerable.Range(0, count).Select(index =>
                new CharacterCreationKarmaContactSelection(Guid.NewGuid(),
                    new CharacterCreationContactIdentity($"Chosen group {index}", "", "Seattle", "", "", "", "", "", "", "", "", "", GroupName: string.Empty),
                    2, 1, IsGroup: true))).ToArray()
        } };

    private static ReadyContext CreateGroupContext(int rate, bool doubleExcess, string qualityName = "Acrobatic Defender")
    {
        // Canonical fixed-cost qualities keep this Mystic Adept fixture inside
        // the supported catalog. Ambidextrous needs an arm-count proof that is
        // currently bounded to mundane prerequisites; do not bypass admission.
        var context = ReadyContext.Create(true, includeNonEmptyPurchases: true, talentValue: "Mystic Adept",
            mysticPowerPoints: 1, qualityName: qualityName, includeSkillPurchase: true, legacyQualityCatalog: false,
            amendSettings: settings =>
            {
                settings.Element("karmacost")!.SetElementValue("karmacontact", rate);
                settings.Element("karmacost")!.SetElementValue("karmaquality", 2);
                settings.SetElementValue("qualitykarmalimit", 10);
                settings.SetElementValue("exceedpositivequalities", true);
                settings.SetElementValue("exceedpositivequalitiescostdoubled", doubleExcess);
            });
        try
        {
            var contacts = new CharacterCreationContactsService(context.Store, context.Resolver, context.Queries);
            var state = contacts.Load(new(context.WorkspaceId)).Value!;
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            var edit = new CharacterCreationContactEdit(Guid.NewGuid(), state.NewContactTemplate!.Identity with
                { Name = "Saved medic" }, 1, 1) { ChangeKind = CharacterCreationContactChangeKind.Add };
            var preview = contacts.Preview(new(state.Binding, edit)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied,
                contacts.Confirm(new(state.Binding, edit, preview.PreviewDigest, "group-test-saved-contact", true)).Outcome);
            var lifestyles = new CharacterCreationLifestylesService(context.Store, context.Resolver, context.Queries);
            var lifestyle = lifestyles.Load(new(context.WorkspaceId)).Value!;
            var home = CharacterCreationCompletionReviewServiceTests.LowLifestyle(lifestyle.Authority);
            var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create, home.LifestyleId, home);
            var priced = lifestyles.Preview(new(lifestyle.Binding, mutation)).Value!;
            Assert.IsTrue(priced.CanConfirm, string.Join(",", priced.Blockers));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied,
                lifestyles.Confirm(new(lifestyle.Binding, mutation, priced.PreviewDigest, "group-test-saved-home", true)).Outcome);
            return context;
        }
        catch { context.Dispose(); throw; }
    }

    [TestMethod]
    public void V4_selected_settings_feed_real_consumers_and_replay_without_claiming_complete_policy()
    {
        using var context = CreateSelectedSettingsContext();
        var inputs = LoadConsumers(context, includeSkillPurchases: true);
        AssertSelectedSettingsMetatypeAuthority(context, inputs);
        Assert.AreEqual(7, inputs.AttributePolicy!.KarmaAttribute);
        Assert.AreEqual(inputs.EffectivePolicy!.ProfileInputsDigest, inputs.AttributePolicy.RawProfileInputsDigest);
        CollectionAssert.AreEqual(new[] { "SR5" }, inputs.EffectivePolicy.EnabledSourcebooks.ToArray());
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        request = request with { Selections = request.Selections with
        {
            AttributeAllocations = [new("BOD", 0, 1)], ChangeableAttributeIds = ["BOD"],
            SkillKarmaPurchases = [new(pistols.SourceSkillId, CharacterCreationSkillKinds.Active, pistols.SourceNodeDigest, 1)]
        }};
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        // Human BOD starts at 1; one Karma level reaches 2 and costs 2 * 7 = 14.
        // Keep the real draft fixture and evaluator/commit/archive/replay path intact.
        var body = review.Value!.Skills!.Attributes.Attributes.Single(row => row.AttributeId == "BOD");
        Assert.AreEqual(1, body.Minimum);
        Assert.AreEqual(2, body.Current);
        Assert.AreEqual(1, body.KarmaLevels);
        Assert.AreEqual(14, body.KarmaCost);
        Assert.AreEqual((int?)3, review.Value.Balances.SkillsKarmaCost);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete }).Value);
        AssertV4Commit(context, inputs, request, review.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void V4_selected_settings_source_changes_are_rejected_before_and_during_atomic_apply(bool duringCommit)
    {
        using var context = CreateSelectedSettingsContext();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var inputs = LoadConsumers(context, includeSkillPurchases: true);
        AssertSelectedSettingsMetatypeAuthority(context, inputs);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request).Value!;
        var review = service.Review(owners.Capture(), context.WorkspaceId, request, new(cash.AuthorityDigest, cash.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        string before = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value);
        string path = Path.Combine(context.Directory, "source", "customdata", "SelectedPolicy", "amend_"
            + CharacterCreationBootstrapProfiles.SettingsSourceFile(inputs.EffectivePolicy!.SettingsProfileId));
        int changes = 0;
        void Change() { File.AppendAllText(path, "\n<!-- selected policy bytes changed -->"); changes++; }
        if (duringCommit) probe.AfterFlush = Change; else Change();
        Assert.IsNull(service.Apply(owners.Capture(), Command(review.Value!)).Value);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(before, JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static void AssertSelectedSettingsMetatypeAuthority(
        ReadyContext context, CharacterCreationCompletionReviewInputs inputs)
    {
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        var character = XDocument.Parse(saved.Document.Content).Root!;
        CollectionAssert.AreEqual(new[] { "SelectedPolicy" }, character.Element("customdatadirectorynames")!
            .Elements("directoryname").Select(row => row.Value).ToArray());
        var source = context.Resolver.TryCreateContext(saved.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationSourceProfile(out var profile));
        Assert.AreEqual(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId, profile.SettingsProfileId);
        Assert.AreEqual(inputs.EffectivePolicy!.ProfileInputsDigest, profile.RawProfileInputsDigest);
        Assert.IsNotNull(profile.BootstrapSourceAnchors);
        Assert.AreEqual(profile.SettingsProfileId, profile.BootstrapSourceAnchors.SettingsProfileId);
        Assert.AreEqual(profile.RawProfileInputsDigest, profile.BootstrapSourceAnchors.RawProfileInputsDigest);
        Assert.IsTrue(CharacterCreationBootstrapProfiles.HasExactCanonicalSourceAnchors(
            "sr5", profile.BuildMethod, profile.SettingsProfileId, profile.BootstrapSourceAnchors.SourceAnchorIds));
        CollectionAssert.Contains(profile.BootstrapSourceAnchors.SourceAnchorIds.ToArray(), "customdata:SelectedPolicy");
        Assert.IsTrue(source.TryResolveCreationMetatypeCatalog(out var metatypes));
        Assert.IsTrue(metatypes.IsAuthoritative, string.Join(",", metatypes.Blockers));
        Assert.AreEqual(profile.SettingsProfileId, metatypes.SourceContext.SettingsProfileId);
        Assert.AreEqual(profile.RawProfileInputsDigest, metatypes.SourceContext.RawProfileInputsDigest);
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            metatypes.SourceContext.SelectedCustomDataInputsDigest));
        Assert.AreEqual(metatypes.SourceContext.SelectedCustomDataInputsDigest,
            profile.BootstrapSourceAnchors.SelectedCustomDataInputsDigest);
        CollectionAssert.Contains(metatypes.SourceContext.SourceAnchorIds.ToArray(), "customdata:SelectedPolicy");
        Assert.IsTrue(File.Exists(Path.Combine(context.Directory, "source", "customdata", "SelectedPolicy",
            "amend_" + CharacterCreationBootstrapProfiles.SettingsSourceFile(profile.SettingsProfileId))));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void V4_selected_settings_missing_directory_is_rejected_before_and_during_atomic_apply(bool duringCommit)
    {
        using var context = CreateSelectedSettingsContext();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var inputs = LoadConsumers(context, includeSkillPurchases: true);
        AssertSelectedSettingsMetatypeAuthority(context, inputs);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request).Value!;
        var review = service.Review(owners.Capture(), context.WorkspaceId, request, new(cash.AuthorityDigest, cash.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        string before = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value);
        string directory = Path.Combine(context.Directory, "source", "customdata", "SelectedPolicy");
        void RemoveSelectedIdentity() => Directory.Move(directory, directory + "-unselected");
        if (duringCommit) probe.AfterFlush = RemoveSelectedIdentity; else RemoveSelectedIdentity();
        Assert.IsNull(service.Apply(owners.Capture(), Command(review.Value!)).Value);
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(before, JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static ReadyContext CreateSelectedSettingsContext() => ReadyContext.Create(false,
        beforeDrafts: character => character.Add(new XElement("customdatadirectorynames", new XElement("directoryname", "SelectedPolicy"))),
        amendSettings: settings => settings.SetElementValue("customdatadirectorynames", string.Empty),
        prepareSource: root =>
        {
            string id = CharacterCreationBootstrapProfiles.PrioritySettingsProfileId;
            string file = CharacterCreationBootstrapProfiles.SettingsSourceFile(id);
            string path = Path.Combine(root, "data", file);
            var document = XDocument.Load(path);
            var profile = document.Descendants("setting").Single(row => row.Element("id")?.Value == id);
            profile.Element("customdatadirectorynames")!.Add(new XElement("customdatadirectoryname",
                new XElement("directoryname", "SelectedPolicy"), new XElement("order", 0), new XElement("enabled", true)));
            document.Save(path);
            string custom = Path.Combine(root, "customdata", "SelectedPolicy");
            Directory.CreateDirectory(custom);
            File.WriteAllText(Path.Combine(custom, "amend_" + file), $"""
                <chummer><settings><setting><id>{id}</id>
                <books amendoperation="REPLACE"><book>SR5</book></books>
                <maxskillratingcreate>7</maxskillratingcreate>
                <karmacost><karmaattribute>7</karmaattribute><karmanewactiveskill>3</karmanewactiveskill></karmacost>
                </setting></settings></chummer>
                """);
        });

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void V4_real_house_rules_changed_attributes_and_optional_purchases_apply_once_and_cold_replay(bool skills, bool loadout)
    {
        using var context = ReadyContext.Create(false, amendSettings: settings =>
        {
            settings.SetElementValue("qualitykarmalimit", "1");
            settings.SetElementValue("exceedpositivequalities", "True");
            settings.SetElementValue("exceedpositivequalitiescostdoubled", "True");
            settings.SetElementValue("contactpointsexpression", "{CHAUnaug} * 5");
            settings.Element("karmacost")!.SetElementValue("karmanewactiveskill", "3");
        });
        var inputs = LoadConsumers(context, loadout, skills);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var guts = inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == "Guts" && row.IsSelectable);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var contact = new CharacterCreationKarmaContactSelection(Guid.NewGuid(),
            new CharacterCreationContactIdentity("Policy Medic", "Professional", "Seattle", "", "", "Human",
                "Other", "Adult", "Professional", "Nuyen", "", "", ""), 2, 2);
        request = request with { Selections = request.Selections with
        {
            AttributeAllocations = [new("BOD", 1, 0), new("CHA", 1, 0)], ChangeableAttributeIds = ["BOD", "CHA"],
            QualityOptionIds = [guts.OptionId], Contacts = [contact],
            SkillKarmaPurchases = skills ? [new(pistols.SourceSkillId, CharacterCreationSkillKinds.Active, pistols.SourceNodeDigest, 1)] : null,
            Loadout = loadout ? new([new(inputs.Loadout!.Authority.Options.Where(row => row.IsSelectable
                && row.Kind == CharacterCreationLoadoutKinds.Weapon).OrderBy(row => row.UnitCost).First().OptionId)], []) : null
        } };
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        Assert.AreEqual((int?)(inputs.Skills.PrerequisiteDraft.CreationKarmaTotal - guts.KarmaCost * 2 + 1),
            reviewed.Value!.Balances.KarmaAfterAttributesAndQualities);
        Assert.AreEqual(10, reviewed.Value.Contacts!.ContactBudget.Total);
        Assert.AreEqual((int?)(skills ? 3 : 0), reviewed.Value.Balances.SkillsKarmaCost);
        Assert.AreNotEqual(JsonSerializer.Serialize(inputs.Skills.Attributes.Attributes),
            JsonSerializer.Serialize(reviewed.Value.Skills!.Attributes.Attributes));
        AssertV4Commit(context, inputs, request, reviewed.Value);
    }

    [TestMethod]
    public void V4_real_saved_choices_are_preserved_through_atomic_apply_and_historical_reopen()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = LoadConsumers(context, includeLoadout: true, includeSkillPurchases: true);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        Assert.IsTrue(request.Selections.MagicSelections.Spells.Count > 0);
        Assert.IsTrue(request.Selections.MagicSelections.AdeptPowers.Count > 0);
        Assert.IsTrue(request.Selections.Gear.Count > 0);
        Assert.IsTrue(request.Selections.QualityOptionIds.Count > 0);
        Assert.IsTrue(request.Selections.Lifestyles.Count > 0);
        Assert.IsTrue(request.Selections.Contacts.Count > 0);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var dropped = request with { Selections = request.Selections with { Gear = [] } };
        CollectionAssert.Contains(CharacterCreationCompletionReviewService.Review(inputs, dropped).Blockers.ToArray(),
            CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
        AssertV4Commit(context, inputs, request, review.Value!);
    }

    [TestMethod]
    public void V4_resealed_typed_policy_projection_downgrades_and_full_provenance_do_not_grant_live_authority()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = LoadConsumers(context, includeLoadout: true, includeSkillPurchases: true);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        var reserveRequest = request with { Selections = request.Selections with { KarmaToReserve = 1 } };
        var reserve = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, reserveRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reserve.Outcome, string.Join(",", reserve.Blockers));
        Assert.AreEqual(witness.SelectedPolicyInputs!.InputsDigest, reserve.Value!.SelectedPolicyInputs!.InputsDigest,
            "Proposed outcomes cannot change the bounded original policy-input identity.");
        Assert.AreEqual(JsonSerializer.Serialize(inputs.Skills.Attributes.Attributes),
            JsonSerializer.Serialize(witness.SelectedPolicyInputs.Policy.Attributes.OriginalAttributes));
        var consumers = witness.EffectivePolicy!.Consumers!;
        var contacts = consumers.Contacts with { ContactPointsExpression = "{CHAUnaug} * 99" };
        contacts = contacts with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(contacts) };
        var forged = Seal(witness.EffectivePolicy with { Consumers = consumers with { Contacts = contacts } });
        var forgedRequest = request with { EffectivePolicyDigest = forged.PolicyDigest };
        var selected = witness.SelectedPolicyInputs with { Policy = witness.SelectedPolicyInputs.Policy with { Contacts = contacts } };
        selected = selected with { InputsDigest = CharacterCreationSelectedPolicyInputs.ComputeDigest(selected) };
        var changed = witness with { EffectivePolicy = forged, SelectedPolicyInputs = selected,
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(forgedRequest) };
        changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, forgedRequest).Outcome);

        // Reseal every public binding so this reaches the independent reconstruction from witness.Skills.
        var attributes = consumers.Domains!.Attributes with { OriginalAttributes = [] };
        Assert.IsTrue(witness.Skills.Attributes.Attributes.Count > 0);
        forged = Seal(witness.EffectivePolicy with { Consumers = consumers with
            { Domains = consumers.Domains! with { Attributes = attributes } } });
        forgedRequest = request with { EffectivePolicyDigest = forged.PolicyDigest };
        selected = witness.SelectedPolicyInputs with { Policy = witness.SelectedPolicyInputs.Policy with { Attributes = attributes } };
        selected = selected with { InputsDigest = CharacterCreationSelectedPolicyInputs.ComputeDigest(selected) };
        changed = witness with { EffectivePolicy = forged, SelectedPolicyInputs = selected,
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(forgedRequest) };
        changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
        Assert.IsTrue(changed.HasVersionedShape());
        var refusedAttributes = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, forgedRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, refusedAttributes.Outcome);
        Assert.IsNull(refusedAttributes.Value);
        CollectionAssert.AreEqual(new[] { CharacterCreationEffectivePolicyContext.Invalid }, refusedAttributes.Blockers.ToArray(),
            "IsValidSelected must reject CharacterCreationSelectedPolicyProjection.IsValid's reconstructed OriginalAttributes; "
            + "an envelope/original-state failure would instead report HistoryMismatch.");
        foreach (var candidate in new[]
        {
            witness with { SelectedPolicyInputs = null },
            witness with { AttributePolicy = null },
            witness with { LoadoutLegalityPolicy = null },
            witness with { Schema = CharacterCreationSelectedCompletionWitness.SchemaV3 },
            witness with { Schema = CharacterCreationSelectedCompletionWitness.SchemaV2, EffectivePolicy = null, PolicyRequirement = null },
            witness with { SelectedPolicyInputs = witness.SelectedPolicyInputs with { OriginalObservationDigest = new string('0', 64) } }
        })
        {
            var resealed = candidate with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(candidate) };
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, resealed, request).Outcome);
            if (!resealed.HasVersionedShape()) Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, resealed, out _));
        }
        var complete = request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete };
        changed = witness with { PolicyRequirement = complete.PolicyRequirement,
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(complete) };
        changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, complete).Outcome);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var opaque = WithPolicy(inputs, inputs.EffectivePolicy! with { Consumers = consumers with
            { OriginalInputsDigest = CharacterCreationFinalizationDigest.Compute("copied full observation") } });
        Assert.IsNull(service.LoadStartingCash(owners.Capture(), context.WorkspaceId,
            Request(opaque, CharacterCreationPolicyRequirement.SkillLimits)).Value);
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void V4_archive_preserves_size_and_canonical_json_limits()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = LoadConsumers(context);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs,
            Request(inputs, CharacterCreationPolicyRequirement.None));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value, out var archive));
        foreach (var bad in new[]
        {
            archive with { UncompressedBytes = CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes + 1 },
            archive with { Data = archive.Data + "=" },
            archive with { Data = archive.Data + " " },
            archive with { UncompressedBytes = archive.UncompressedBytes - 1 }
        }) Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryDecode(bad, out _, out _));
        var huge = captured.Value! with { SelectedPolicyInputs = captured.Value!.SelectedPolicyInputs! with
            { OriginalObservationDigest = new string('a', CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes + 1) } };
        Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, huge, out _));
        string canonical = JsonSerializer.Serialize(new { Bootstrap = bootstrap, Witness = captured.Value });
        foreach (string json in new[] { " " + canonical, canonical.Insert(1, "\"Unexpected\":true,"),
            canonical.Insert(1, "\"Bootstrap\":" + JsonSerializer.Serialize(bootstrap) + ",") })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using var stream = new MemoryStream();
            using (var encoder = new BrotliStream(stream, CompressionLevel.SmallestSize, leaveOpen: true)) encoder.Write(bytes);
            var bad = archive with { UncompressedBytes = bytes.Length,
                UncompressedDigest = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                Data = Convert.ToBase64String(stream.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_') };
            Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryDecode(bad, out _, out _));
        }
    }

    [TestMethod]
    public void V4_review_diagnostics_recognize_consumer_V3_with_bounded_tags()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumers(context);
        Assert.AreEqual(CharacterCreationCompletionConsumerPolicy.SchemaV3, inputs.EffectivePolicy!.Consumers!.Schema);
        using var parent = new Activity("consumer-schema-test").Start();
        var spans = new List<KeyValuePair<string, object?>[]>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "Review" && activity.ParentId == parent.Id)
                    spans.Add(activity.TagObjects.ToArray());
            }
        };
        ActivitySource.AddActivityListener(listener);
        var reviewed = CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.Evaluate(inputs,
            Request(inputs, CharacterCreationPolicyRequirement.None), CancellationToken.None);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        Assert.IsNotNull(reviewed.Value);
        Assert.AreEqual(1, spans.Count);
        var tags = spans.Single().ToDictionary(row => row.Key, row => row.Value);
        CollectionAssert.AreEqual(new[] { "completion.blocker.count", "completion.consumer.schema",
            "completion.outcome", "completion.policy.schema" }, tags.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.AreEqual(CharacterCreationEffectivePolicyContext.SchemaV2, tags["completion.policy.schema"]);
        Assert.AreEqual(CharacterCreationCompletionConsumerPolicy.SchemaV3, tags["completion.consumer.schema"]);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, tags["completion.outcome"]);
        Assert.AreEqual(0, tags["completion.blocker.count"]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void V4_review_diagnostics_never_echo_untrusted_policy_schema_text(bool consumerSchema)
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumers(context);
        const string privateText = "PRIVATE-CHARACTER-CONTENT-MUST-NOT-ENTER-DIAGNOSTICS";
        var policy = inputs.EffectivePolicy!;
        var changed = WithPolicy(inputs, consumerSchema
            ? policy with { Consumers = policy.Consumers! with { Schema = privateText } }
            : policy with { Schema = privateText });
        using var parent = new Activity("policy-schema-test").Start();
        var spans = new List<KeyValuePair<string, object?>[]>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "Review" && activity.ParentId == parent.Id)
                    spans.Add(activity.TagObjects.ToArray());
            }
        };
        ActivitySource.AddActivityListener(listener);
        var refused = CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.Evaluate(changed,
            Request(changed, CharacterCreationPolicyRequirement.None), CancellationToken.None);
        Assert.IsNull(refused.Value);
        Assert.AreEqual(1, spans.Count);
        var tags = spans.Single().ToDictionary(row => row.Key, row => row.Value);
        Assert.AreEqual("unknown", tags[consumerSchema ? "completion.consumer.schema" : "completion.policy.schema"]);
        Assert.IsFalse(JsonSerializer.Serialize(tags).Contains(privateText, StringComparison.Ordinal));
    }

    [TestMethod]
    public void V4_real_missing_supplemental_policies_capture_cash_apply_and_cold_replay()
    {
        using var context = ReadyContext.Create(false);
        var resolver = new SupplementalPolicyResolver(context.Resolver) { OmitSupplementalPolicies = true };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithConsumerPolicy(owners.Capture(), new(context.WorkspaceId), true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        Assert.IsNotNull(inputs.Loadout);
        Assert.IsNull(inputs.AttributePolicy);
        Assert.IsNull(inputs.LoadoutLegalityPolicy);
        Assert.AreEqual(2, resolver.PolicyReads, "The missing-source resolver must recapture the real unchanged inventory.");
        Assert.AreEqual(2, resolver.SupplementalReads.Count);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.AreEqual(CharacterCreationSelectedCompletionWitness.SchemaV4, captured.Value!.Schema);
        Assert.IsNull(captured.Value.AttributePolicy);
        Assert.IsNull(captured.Value.LoadoutLegalityPolicy);
        foreach (var operation in new[] { CharacterCreationConsumerPolicyOperation.AttributeSpending,
                     CharacterCreationConsumerPolicyOperation.LoadoutLegalityExclusions })
        {
            var row = captured.Value.SelectedPolicyInputs!.Policy.Coverage.Single(item => item.Operation == operation);
            Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Missing, row.Disposition);
            Assert.AreEqual(CharacterCreationConsumerPolicyReason.OriginalInputUnavailable, row.Reason);
        }
        AssertV4Commit(context, inputs, request, review.Value!, resolver);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Creation_base_prices_keep_manual_history_shared_wallet_and_native_cost_through_apply(bool formerCatalog)
    {
        using var context = ReadyContext.Create(false, includeNonEmptyPurchases: true, amendSettings: settings =>
        {
            settings.SetElementValue("multiplyrestrictedcost", true);
            settings.SetElementValue("multiplyforbiddencost", true);
            settings.SetElementValue("restrictedcostmultiplier", 2);
            settings.SetElementValue("forbiddencostmultiplier", 3);
        });
        // Confirm a real manual armor draft under either current or the frozen
        // pre-correction catalog algorithm. Its correct base-cost history stays intact.
        ICharacterSourceDataResolver purchaseResolver = formerCatalog
            ? new FormerCreationExclusionResolver(context.Resolver) : context.Resolver;
        var manual = new CharacterCreationLoadoutService(context.Store, purchaseResolver);
        var prepared = manual.Prepare(context.WorkspaceId);
        Assert.IsNotNull(prepared.Value, string.Join(",", prepared.Blockers));
        var state = prepared.Value!;
        var armor = state.Inputs.Authority.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Armor
            && row.IsSelectable && row.UnitCost > 0m).OrderBy(row => row.UnitCost).First();
        var quote = manual.Preview(new(state.Inputs.Binding, state.Inputs.SnapshotDigest, new([], [new(armor.OptionId)])));
        Assert.IsNotNull(quote.Value, string.Join(",", quote.Blockers));
        var receipt = manual.Confirm(new(quote.Value!.Request, quote.Value.PreviewDigest, "base-price-history", true));
        Assert.IsNotNull(receipt.Value, string.Join(",", receipt.Blockers));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var savedDraftBytes = JsonSerializer.Serialize(original.Document.AuxiliaryState.CharacterCreationLoadoutDraft);

        var inputs = LoadConsumers(context, includeLoadout: true);
        Assert.AreEqual(savedDraftBytes, JsonSerializer.Serialize(inputs.Loadout!.PendingDraft));
        var baseline = Request(inputs, CharacterCreationPolicyRequirement.None);
        var baselineReview = CharacterCreationCompletionReviewService.Review(inputs, baseline);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, baselineReview.Outcome, string.Join(",", baselineReview.Blockers));
        Assert.IsNotNull(baselineReview.Value, string.Join(",", baselineReview.Blockers));
        var weapon = inputs.Loadout.Authority.Options.Single(row => row.SourceId ==
            Guid.Parse("676660a2-3320-4b4a-acaf-f645f2ba6cd7") && row.Kind == CharacterCreationLoadoutKinds.Weapon);
        Assert.IsTrue(weapon.IsSelectable);
        Assert.AreEqual(320m, weapon.UnitCost);
        var ammo = inputs.Lifestyles.Gear.State.Authority.Options.Single(row => row.SourceId ==
            Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae"));
        Assert.IsTrue(ammo.IsSelectable && ammo.PricingIsExact && ammo.AvailabilityIsExact, string.Join(",", ammo.Blockers));
        Assert.IsTrue(ammo.Availability <= inputs.Lifestyles.Gear.State.Authority.MaximumAvailability);
        CollectionAssert.Contains(inputs.EffectivePolicy!.EnabledSourcebooks.ToArray(), ammo.SourceBook);
        Assert.AreEqual(20m, ammo.PackageCost);
        Assert.AreEqual(10, ammo.PackageQuantity);
        Assert.AreEqual("20", XElement.Parse(ammo.SourceNodeXml).Element("cost")!.Value);
        var request = baseline with { Selections = baseline.Selections with
        {
            Gear = [.. baseline.Selections.Gear, new(ammo.OptionId, 37)],
            Loadout = new([new(weapon.OptionId)], baseline.Selections.Loadout!.Armor)
        } };
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        Assert.AreEqual(baselineReview.Value!.Balances.GearCost + 74m, review.Value!.Balances.GearCost);
        Assert.AreEqual((decimal?)(armor.UnitCost + 320m), review.Value.Balances.LoadoutCost);
        Assert.AreEqual(baselineReview.Value.Balances.RemainingNuyen - 394m, review.Value.Balances.RemainingNuyen);
        // Complete both catalog-history positive controls before the detached negative reviews.
        AssertV4Commit(context, inputs, request, review.Value);
        var after = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.AreEqual(savedDraftBytes, JsonSerializer.Serialize(
            after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State.CharacterCreationLoadoutDraft));
        var xml = XElement.Parse(after.Document.Content);
        var nativeAmmo = xml.Element("gears")!.Elements("gear").Single(row =>
            row.Element("sourceid")?.Value == ammo.SourceId.ToString("D"));
        Assert.AreEqual("20", nativeAmmo.Element("cost")!.Value);
        Assert.AreEqual("10", nativeAmmo.Element("costfor")!.Value);
        Assert.AreEqual("37", nativeAmmo.Element("qty")!.Value);
        Assert.AreEqual("320", xml.Element("weapons")!.Elements("weapon").Single(row =>
            row.Element("sourceid")?.Value == weapon.SourceId.ToString("D")).Element("cost")!.Value);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewService.Review(inputs, request with
        { Selections = request.Selections with { Loadout = new([new(weapon.OptionId)], []) } }).Outcome,
            "A corrected quote cannot remove a saved manual armor selection.");
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete }).Value);

        // Keep Gear + loadout affordable so admission reaches the full shared-wallet
        // balance. With no lifestyle, exhausting that pair stops earlier with
        // InsufficientNuyen and no Lifestyles projection (hence no NuyenOverspend).
        var home = CharacterCreationCompletionReviewServiceTests.LowLifestyle(inputs.Lifestyles.Authority);
        var homeOption = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId);
        Assert.AreEqual(2000m, homeOption.BaseCost);
        home = CharacterCreationLifestylesRules.AddBuiltInQualities(home, homeOption);
        var funded = baseline with { Selections = baseline.Selections with
        {
            Loadout = new([new(weapon.OptionId)], baseline.Selections.Loadout!.Armor),
            Lifestyles = [.. baseline.Selections.Lifestyles, home],
            StartingLifestyleId = baseline.Selections.StartingLifestyleId ?? home.LifestyleId
        } };
        var bare = CharacterCreationCompletionReviewService.Review(inputs, funded);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, bare.Outcome, string.Join(",", bare.Blockers));
        Assert.IsNotNull(bare.Value, string.Join(",", bare.Blockers));
        Assert.AreEqual(baselineReview.Value.Lifestyles!.Budget.Used + 2000m, bare.Value!.Lifestyles!.Budget.Used);
        decimal unitCost = ammo.PackageCost / ammo.PackageQuantity;
        int cartridges = checked((int)decimal.Floor(bare.Value.Balances.RemainingNuyen!.Value / unitCost) + 1);
        Assert.IsTrue(cartridges > 1 && cartridges <= inputs.Lifestyles.Gear.State.Authority.MaximumQuantityPerLine);
        var over = funded with { Selections = funded.Selections with
        { Gear = [.. baseline.Selections.Gear, new(ammo.OptionId, cartridges)] } };
        Assert.IsTrue(CharacterCreationGearRules.TryProjectBasket(over.Selections.Gear,
            inputs.Lifestyles.Gear.State.Authority, bare.Value.Lifestyles.Gear.Resources.Budget.TotalStartingNuyen,
            out _, out var gearBudget, out var gearBlockers), string.Join(",", gearBlockers));
        decimal loadoutCost = armor.UnitCost + weapon.UnitCost;
        decimal lifestyleCost = bare.Value.Lifestyles.Budget.Used;
        Assert.IsTrue(gearBudget.RemainingNuyen >= loadoutCost, "Gear and retained loadout must pass the earlier funds fence.");
        Assert.IsTrue(gearBudget.BasketCost + lifestyleCost <= gearBudget.TotalStartingNuyen,
            "The cart must fit without loadout; the shortage must depend on the shared wallet.");
        decimal expectedOverspend = gearBudget.BasketCost + lifestyleCost + loadoutCost - gearBudget.TotalStartingNuyen;
        Assert.IsTrue(expectedOverspend > 0m && expectedOverspend <= unitCost);
        var unaffordable = CharacterCreationCompletionReviewService.Review(inputs, over);
        string findings = string.Join(",", unaffordable.Blockers);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, unaffordable.Outcome, findings);
        Assert.IsNotNull(unaffordable.Value, findings);
        Assert.IsNotNull(unaffordable.Value!.Lifestyles, findings);
        Assert.IsTrue(unaffordable.Value.Lifestyles!.Budget.IsExact, findings);
        CollectionAssert.AreEquivalent(new[]
        {
            CharacterCreationLifestylesBlockers.InsufficientFunds,
            CharacterCreationResourcesBlockers.InsufficientNuyen
        }, unaffordable.Blockers.ToArray(), findings);
        Assert.AreEqual((decimal?)gearBudget.BasketCost, unaffordable.Value.Balances.GearCost, findings);
        Assert.AreEqual((decimal?)loadoutCost, unaffordable.Value.Balances.LoadoutCost, findings);
        Assert.AreEqual((decimal?)expectedOverspend, unaffordable.Value.Balances.NuyenOverspend, findings);
        Assert.AreEqual((decimal?)0m, unaffordable.Value.Balances.RemainingNuyen, findings);

        var affordable = CharacterCreationCompletionReviewService.Review(inputs, over with
        { Selections = over.Selections with { Gear = [.. baseline.Selections.Gear, new(ammo.OptionId, cartridges - 1)] } });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, affordable.Outcome, string.Join(",", affordable.Blockers));
        Assert.AreEqual((decimal?)0m, affordable.Value!.Balances.NuyenOverspend);
        Assert.AreEqual((decimal?)(unitCost - expectedOverspend), affordable.Value.Balances.RemainingNuyen);
    }

    // Frozen reproduction of the former catalog branch, not a new price policy.
    private sealed class FormerCreationExclusionResolver(ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            var source = inner.TryCreateContext(characterXml);
            if (source is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, FormerCreationExclusionContext>();
            ((FormerCreationExclusionContext)(object)proxy).Inner = source;
            return proxy;
        }
    }

    public class FormerCreationExclusionContext : DispatchProxy
    {
        public ICharacterSourceDataContext Inner { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Inner, args);
            if (method.Name == nameof(ICharacterSourceDataContext.TryResolveCreationLoadoutAuthority)
                && result is true && args![0] is CharacterCreationLoadoutAuthority authority)
            {
                var options = authority.Options.Select(option =>
                {
                    if (option.Legality is not (CharacterCreationGearLegality.Restricted or CharacterCreationGearLegality.Forbidden))
                        return option;
                    var excluded = option with { IsSelectable = false, PricingIsExact = false,
                        Blockers = option.Blockers.Append(CharacterCreationLoadoutBlockers.UnsupportedSemantics)
                            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
                    return excluded with { OptionDigest = CharacterCreationLoadoutRules.ComputeOptionDigest(excluded) };
                }).ToArray();
                authority = authority with { Options = options };
                args[0] = authority with { AuthorityDigest = CharacterCreationLoadoutRules.ComputeAuthorityDigest(authority) };
            }
            return result;
        }
    }

    private static void AssertV4Commit(ReadyContext context, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationCompletionReview review,
        ICharacterSourceDataResolver? resolver = null, string expectedWitnessSchema = CharacterCreationSelectedCompletionWitness.SchemaV4)
    {
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        resolver ??= context.Resolver;
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, resolver, context.Queries);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var final = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, final.Outcome, string.Join(",", final.Blockers));
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value));
        var command = Command(final.Value!);
        if (review.Contacts!.GroupKarma is { } groupKarma)
        {
            var ledger = final.Value!.Projection.OrderedDeltas;
            Assert.AreEqual(1, ledger.Count(row => row.DeltaId == "contacts:karma"));
            Assert.AreEqual((decimal)groupKarma.AdditionalKarmaCost,
                ledger.Single(row => row.DeltaId == "contacts:karma").KarmaCost);
            Assert.IsTrue(ledger.Where(row => row.DeltaId.StartsWith("contact:", StringComparison.Ordinal))
                .All(row => row.KarmaCost == 0));
            Assert.AreEqual((decimal)(review.Balances.CreationKarmaTotal - final.Value.Projection.KarmaBeforeCarryover),
                ledger.Sum(row => row.KarmaCost));
            Assert.AreEqual(review.Balances.RemainingCreationKarma, (int?)final.Value.Projection.KarmaBeforeCarryover);
            Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
                context.Resolver.TryCreateContext(before.Document.Content)!, before.Document.Content, out var bootstrapBefore));
            var forgedContacts = review.Contacts with
            { GroupKarma = groupKarma with { AdditionalKarmaCost = groupKarma.AdditionalKarmaCost + 1 } };
            forgedContacts = forgedContacts with
            { ProposalDigest = CharacterCreationContactsCompletionRules.ComputeProjectionDigest(forgedContacts) };
            var forgedReview = review with { Contacts = forgedContacts };
            forgedReview = forgedReview with { ReviewDigest = CharacterCreationCompletionReviewRules.ComputeReviewDigest(forgedReview) };
            Assert.IsNull(CharacterCreationCompletionFinalizationProjector.Project(before, bootstrapBefore,
                inputs, forgedReview, cash.Value!, final.Value.Projection.StartingCash.Choice).Value);
        }
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(1, probe.Replacements);
        var cold = new FileWorkspaceStore(context.Directory, probe);
        var opened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(opened.Success, opened.Error);
        var after = opened.Value!;
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(final.Value!.Projection.CharacterXml, after.Document.Content);
        var xml = XDocument.Parse(after.Document.Content).Root!;
        Assert.AreEqual(final.Value.Projection.KarmaRemaining, int.Parse(xml.Element("karma")!.Value, CultureInfo.InvariantCulture));
        Assert.AreEqual(final.Value.Projection.NuyenRemaining, decimal.Parse(xml.Element("nuyen")!.Value, CultureInfo.InvariantCulture));
        if (review.Contacts.GroupKarma is not null)
        {
            Assert.AreEqual(request.Selections.Contacts.Count, xml.Element("contacts")!.Elements("contact").Count());
            foreach (var contact in request.Selections.Contacts)
            {
                var saved = xml.Element("contacts")!.Elements("contact")
                    .Single(row => row.Element("guid")!.Value == contact.ContactId.ToString("D"));
                Assert.AreEqual(contact.Identity.Name, saved.Element("name")!.Value);
                Assert.AreEqual(contact.IsGroup, bool.Parse(saved.Element("group")!.Value));
            }
        }
        var archive = after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        Assert.AreEqual(JsonSerializer.Serialize(before.Document.AuxiliaryState), JsonSerializer.Serialize(archive.State));
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId, archive, applied.Value!));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive.CompletionAuthority!.SelectedReplay,
            out var bootstrap, out var witness));
        Assert.AreEqual(expectedWitnessSchema, witness.Schema);
        Assert.AreEqual(JsonSerializer.Serialize(review.PolicyConsumption), JsonSerializer.Serialize(witness.EffectivePolicy?.SelectedConsumption));
        if (review.Contacts.GroupKarma is not null)
        {
            var forgedPolicy = witness.ContactsPolicy with { GroupContactKarmaMultiplier = witness.ContactsPolicy.GroupContactKarmaMultiplier + 1 };
            forgedPolicy = forgedPolicy with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(forgedPolicy) };
            var forgedWitness = witness with { ContactsPolicy = forgedPolicy };
            forgedWitness = forgedWitness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forgedWitness) };
            Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, forgedWitness, out var forgedPayload));
            var forgedAuthority = archive.CompletionAuthority with { SelectedReplay = forgedPayload };
            forgedAuthority = forgedAuthority with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(forgedAuthority) };
            var forgedReceipt = applied.Value! with { CompletionAuthorityDigest = forgedAuthority.AuthorityDigest };
            forgedReceipt = forgedReceipt with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(forgedReceipt) };
            Assert.IsFalse(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId,
                archive with { CompletionAuthority = forgedAuthority }, forgedReceipt));
        }
        Assert.AreEqual(JsonSerializer.Serialize(inputs.AttributePolicy), JsonSerializer.Serialize(witness.AttributePolicy));
        Assert.AreEqual(JsonSerializer.Serialize(inputs.LoadoutLegalityPolicy), JsonSerializer.Serialize(witness.LoadoutLegalityPolicy));
        Assert.AreEqual(JsonSerializer.Serialize(inputs.EffectivePolicy!.Consumers!.Coverage),
            JsonSerializer.Serialize(witness.SelectedPolicyInputs!.Policy.Coverage));
        Assert.AreEqual(inputs.EffectivePolicy!.Consumers!.OriginalInputsDigest, witness.EffectivePolicy!.Consumers!.OriginalInputsDigest);
        Assert.AreNotEqual(witness.EffectivePolicy.Consumers.OriginalInputsDigest, witness.SelectedPolicyInputs!.InputsDigest);
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(before, bootstrap, witness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(review.Balances), JsonSerializer.Serialize(replay.Value!.Balances));
        Assert.AreEqual(JsonSerializer.Serialize(review.Contacts.GroupKarma), JsonSerializer.Serialize(replay.Value.Contacts.GroupKarma));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var repeatedArchive));
        Assert.AreEqual(JsonSerializer.Serialize(archive.CompletionAuthority.SelectedReplay), JsonSerializer.Serialize(repeatedArchive));
        var repeated = new OwnerBoundCharacterCreationCompletionService(cold, owners, resolver, context.Queries).Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, repeated.Outcome, string.Join(",", repeated.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, repeated.Value!.ReceiptDigest);
        Assert.AreEqual(1, after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Count);
        Assert.AreEqual(1, probe.Replacements);
        Assert.AreEqual(JsonSerializer.Serialize(after), JsonSerializer.Serialize(cold.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void V4_current_policy_apply_refuses_same_profile_or_policy_only_source_drift(bool profile, bool duringCommit)
    {
        using var context = ReadyContext.Create(true, amendSettings: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var inputs = LoadConsumers(context, includeLoadout: true, includeSkillPurchases: true);
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
    [DataRow(false)]
    [DataRow(true)]
    public void V4_cash_and_admission_refuse_source_drift_after_initial_policy_admission_before_final_recapture(bool admission)
    {
        using var context = ReadyContext.Create(false, amendSettings: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var clean = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var inputs = LoadConsumers(context, includeLoadout: true, includeSkillPurchases: true);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var stamp = owners.Capture();
        var cash = clean.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var choice = new CharacterCreationStartingCashChoice(cash.Value!.AuthorityDigest, cash.Value.Dice * 3);
        var admitted = clean.Review(stamp, context.WorkspaceId, request, choice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, admitted.Outcome, string.Join(",", admitted.Blockers));
        string before = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value);
        string path = Path.Combine(context.Directory, "source", "data", "weapons.xml");
        string sourceBefore = File.ReadAllText(path);
        int cashReads = 0, changes = 0;
        SupplementalPolicyResolver? resolver = null;
        resolver = new SupplementalPolicyResolver(context.Resolver)
        {
            AfterSuccessfulRead = name =>
            {
                if (name is not (nameof(ICharacterSourceDataContext.TryResolveCreationPriorityStartingNuyen)
                    or nameof(ICharacterSourceDataContext.TryResolveCreationDefaultStartingNuyen))) return;
                cashReads++;
                // Cash checks its source twice; admission resolves it once before pure capture/replay.
                if (cashReads != (admission ? 1 : 2)) return;
                Assert.AreEqual(2, resolver!.PolicyReads,
                    "Initial policy admission and its inventory recheck must succeed before mutation.");
                File.AppendAllText(path, "\n<!-- after admission, before final recapture -->");
                changes++;
            }
        };
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, resolver, context.Queries);
        if (admission)
        {
            var refused = service.Review(stamp, context.WorkspaceId, request, choice);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, refused.Outcome);
            Assert.IsNull(refused.Value);
            CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.SnapshotConflict }, refused.Blockers.ToArray());
        }
        else
        {
            var refused = service.LoadStartingCash(stamp, context.WorkspaceId, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, refused.Outcome);
            Assert.IsNull(refused.Value);
            CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.SnapshotConflict }, refused.Blockers.ToArray());
        }
        Assert.AreEqual(admission ? 1 : 2, cashReads);
        Assert.AreEqual(1, changes);
        Assert.AreNotEqual(sourceBefore, File.ReadAllText(path));
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(before, JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value));
        Assert.AreEqual(1, resolver.DisposedScopes);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void V4_current_policy_apply_rejects_owner_ABA_and_source_ABA_inside_a_live_observation()
    {
        using var context = ReadyContext.Create(true, amendSettings: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var inputs = LoadConsumers(context, includeLoadout: true, includeSkillPurchases: true);
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

}
