using System.Globalization;
using System.Xml.Linq;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationCompletionApplyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_skill_karma_applies_once_cold_replays_and_persists_payment_provenance(bool specialization)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        var source = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var requests = specialization
            ? new[] { new CharacterCreationSkillKarmaPurchase(source.SourceSkillId, source.Kind, source.SourceNodeDigest,
                2, source.Specializations[0].OptionId) }
            : CharacterCreationCompletionPlannerTests.Purchases22(inputs);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = requests, TotalKarmaInvestment = specialization ? 0 : 3 };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var before = store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        var service = Service(context, store, owners);
        var review = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var expectedCost = specialization ? inputs.Skills.KarmaPurchasePolicy!.KarmaSpecialization : 22;
        Assert.AreEqual((decimal)expectedCost, review.Value!.Projection.OrderedDeltas
            .Where(delta => delta.Kind == CharacterCreationFinalizationDeltaKinds.Skill).Sum(delta => delta.KarmaCost));
        AssertUnchanged(context, files);
        var command = Command(review.Value, "completion-skill-karma-once");
        var applied = service.Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(1, probe.Replacements);
        var cold = new FileWorkspaceStore(context.Directory);
        var opened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(opened.Success, opened.Error);
        var after = opened.Value!;
        AssertCommitted(before, after, applied.Value!, review.Value.Projection);
        var xml = XDocument.Parse(after.Document.Content);
        var skill = xml.Root!.Element("newskills")!.Element("skills")!.Elements("skill")
            .Single(row => row.Element("suid")!.Value == source.SourceSkillId);
        Assert.AreEqual("2", skill.Element("base")!.Value);
        Assert.AreEqual(specialization ? "0" : "2", skill.Element("karma")!.Value);
        Assert.AreEqual(specialization ? "True" : null, skill.Element("buywithkarma")?.Value);
        if (specialization) Assert.AreEqual(source.Specializations[0].Name, skill.Element("specs")!.Element("spec")!.Element("name")!.Value);
        var archive = after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.CompletionAuthority!.SelectedReplay;
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out _, out var witness));
        Assert.IsNotNull(witness!.Skills.KarmaPurchasePolicy);
        Assert.AreEqual(inputs.Skills.KarmaPurchasePolicy!.AuthorityDigest, witness.Skills.KarmaPurchasePolicy!.AuthorityDigest);
        var committed = Snapshot(context);
        var replay = Service(context, cold, owners).Apply(owners.Capture(), command);
        Assert.AreEqual(applied.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        var changed = command with { Proposal = command.Proposal with { Selections = choices with
            { SkillKarmaPurchases = [requests[0] with { Rating = requests[0].Rating + 1 }] } } };
        Assert.IsNull(Service(context, cold, owners).Apply(owners.Capture(), changed).Value);
        Assert.IsNull(Service(context, cold, owners).Apply(owners.Capture(), command with { IdempotencyKey = "new-replay" }).Value);
        Assert.AreEqual(after.ContentRevision, cold.Get(context.WorkspaceId).Value!.ContentRevision);
        AssertUnchanged(context, committed);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public void Completion_skill_karma_archive_rejects_rehashed_policy_or_payment_changes_after_semantic_replay(bool changePolicy, bool fundedCompensation)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: fundedCompensation ? settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True") : null);
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = CharacterCreationCompletionPlannerTests.Purchases22(inputs), TotalKarmaInvestment = 3 };
        if (fundedCompensation) choices = choices with { SkillKarmaPurchases = FundedCompensationPurchases(inputs) };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var service = Service(context, context.Store, owners);
        var reviewed = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var applied = service.Apply(owners.Capture(), Command(reviewed.Value!, "completion-skill-karma-archive"));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        var opened = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId);
        Assert.IsTrue(opened.Success, opened.Error);
        var after = opened.Value!;
        var files = Snapshot(context);
        var archive = after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        var entry = after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single();
        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(
            after.Id, after.ContentRevision, archive, [entry]));
        var authority = archive.CompletionAuthority!;
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(authority.SelectedReplay, out var bootstrap, out var witness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap!, witness!, authority.Proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual((int?)(fundedCompensation ? 10 : 22), replay.Value!.Balances.SkillsKarmaCost);

        var proposal = authority.Proposal;
        var changed = witness!;
        if (changePolicy)
        {
            var policy = fundedCompensation
                ? changed.Skills.KarmaPurchasePolicy! with { KarmaImproveSkillGroup = 4 }
                : changed.Skills.KarmaPurchasePolicy! with { KarmaImproveActiveSkill = 1 };
            policy = policy with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy) };
            var skills = changed.Skills with { KarmaPurchasePolicy = policy, SnapshotDigest = string.Empty };
            skills = skills with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(skills) };
            changed = changed with { Skills = skills };
        }
        else
        {
            string target = fundedCompensation ? choices.SkillKarmaPurchases![0].SourceSkillId
                : inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols").SourceSkillId;
            proposal = proposal with { Selections = proposal.Selections with
            {
                SkillKarmaPurchases = proposal.Selections.SkillKarmaPurchases!.Select(row =>
                    row.SourceSkillId == target ? row with { Rating = 3 } : row).ToArray()
            } };
        }
        changed = changed with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(proposal) };
        changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, changed, out var encoded));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(encoded, out var decodedBootstrap, out var decoded));
        var historical = CharacterCreationSelectedCompletionEvaluator.Replay(original, decodedBootstrap!, decoded!, proposal);
        string diagnostic = $"Changed {(changePolicy ? "purchase policy" : "paid rating")}; "
            + $"historical outcome: {historical.Outcome}; blockers: {string.Join(",", historical.Blockers)}";
        // These alternative policies/choices are legal quotes. Passing transport/digest
        // validation must still reach repricing and the committed-result comparison.
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, historical.Outcome, diagnostic);
        Assert.AreEqual((int?)(fundedCompensation ? changePolicy ? 8 : 16 : changePolicy ? 13 : 14),
            historical.Value!.Balances.SkillsKarmaCost, diagnostic);
        var changedAuthority = authority with { Proposal = proposal, SelectedReplay = encoded };
        changedAuthority = changedAuthority with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(changedAuthority) };
        var receipt = entry.Receipt with { CompletionAuthorityDigest = changedAuthority.AuthorityDigest };
        receipt = receipt with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt) };
        var ledger = new[] { entry with { Receipt = receipt } };
        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidLedger(after.Id, after.ContentRevision, ledger), diagnostic);
        var changedArchive = archive with { CompletionAuthority = changedAuthority };
        Assert.IsFalse(CharacterCreationCompletionTransaction.IsValidArchive(after.Id, changedArchive, receipt), diagnostic);
        Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(
            after.Id, after.ContentRevision, changedArchive, ledger), diagnostic);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_skill_karma_owner_revision_and_atomic_failure_leave_no_partial_durable_state(bool fundedCompensation)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: fundedCompensation ? settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True") : null);
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = CharacterCreationCompletionPlannerTests.Purchases22(inputs) };
        if (fundedCompensation) choices = choices with { SkillKarmaPurchases = FundedCompensationPurchases(inputs) };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var stamp = owners.Capture();
        var probe = new AtomicWriteProbe { ThrowAt = FileWorkspaceStoreFaultStage.AfterTempFileFlushed };
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var reviewed = service.Review(stamp, context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var command = Command(reviewed.Value!, "completion-skill-karma-failure");
        var files = Snapshot(context);
        var stale = command with { Binding = command.Binding with { ContentRevision = command.Binding.ContentRevision - 1 } };
        Assert.IsNull(service.Apply(stamp, stale).Value);
        AssertUnchanged(context, files);
        var overspent = command with { Proposal = command.Proposal with
            { Selections = choices with { TotalKarmaInvestment = 4 } } };
        Assert.IsNull(service.Apply(stamp, overspent).Value);
        AssertUnchanged(context, files);
        owners.Set(new OwnerScope("different-skill-owner"));
        Assert.IsNull(service.Apply(stamp, command).Value);
        Assert.IsNull(service.Apply(owners.Capture(), command).Value);
        AssertUnchanged(context, files);
        owners.Set(OwnerScope.LocalSingleUser);
        probe.Armed = true;
        var faulted = service.Apply(owners.Capture(), command);
        Assert.IsNull(faulted.Value);
        Assert.AreEqual(1, probe.Throws);
        Assert.AreEqual(0, probe.Replacements);
        AssertUnchanged(context, files);
        var retry = service.Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, retry.Outcome, string.Join(",", retry.Blockers));
        Assert.AreEqual(1, probe.Replacements);
        Assert.IsTrue(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public void Completion_skill_karma_reprices_current_sources_before_atomic_replacement(bool duringFlush, bool fundedCompensation, bool restore)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            if (fundedCompensation) settings.SetElementValue("compensateskillgroupkarmadifference", "True");
        });
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        Assert.AreEqual(2, inputs.Skills.KarmaPurchasePolicy!.KarmaImproveActiveSkill);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = CharacterCreationCompletionPlannerTests.Purchases22(inputs) };
        if (fundedCompensation) choices = choices with { SkillKarmaPurchases = FundedCompensationPurchases(inputs) };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var reviewed = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var command = Command(reviewed.Value!, "completion-skill-karma-reprice");
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(CharacterCreationBuildMethods.Priority, out var profile));
        string path = Path.Combine(context.Directory, "source", "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(profile));
        var files = Snapshot(context);
        void Reprice()
        {
            byte[] original = File.ReadAllBytes(path);
            var xml = XDocument.Load(path);
            xml.Root!.Element("settings")!.Elements("setting").Single(row => row.Element("id")!.Value == profile)
                .Element("karmacost")!.SetElementValue("karmaimproveactiveskill", "3");
            xml.Save(path);
            if (restore) File.WriteAllBytes(path, original);
            files[Path.GetRelativePath(context.Directory, path)] = File.ReadAllBytes(path);
        }
        if (duringFlush) probe.AfterTempFileFlushed = Reprice;
        else Reprice();
        var denied = service.Apply(owners.Capture(), command);
        Assert.IsNull(denied.Value);
        Assert.AreEqual(0, probe.Replacements);
        var currentSource = context.Resolver.TryCreateContext(store.Get(context.WorkspaceId).Value!.Document.Content);
        Assert.IsNotNull(currentSource);
        Assert.IsTrue(currentSource.TryResolveCreationPrioritySkillKarmaPolicy(out var currentPolicy));
        Assert.AreEqual(restore ? 2 : 3, currentPolicy!.KarmaImproveActiveSkill);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void Completion_nonpayer_order_reseal_cannot_replace_committed_identity_on_cold_read(
        bool resealResultDigest, bool afterCareer)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True"));
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        Assert.IsNull(inputs.EffectivePolicy, "Exercise the supported path without consumer-policy comparison.");
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = FundedCompensationPurchases(inputs) };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var original = store.Get(context.WorkspaceId).Value!;
        var service = Service(context, store, owners);
        var review = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        const string key = "nonpayer-order-identity";
        var applied = service.Apply(owners.Capture(), Command(review.Value!, key));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        if (afterCareer) ApplyCompletionCareerReputation(context, store);
        var after = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.IsNotNull(after);
        Assert.AreEqual(applied.Value!.ContentRevision + (afterCareer ? 1 : 0), after.ContentRevision);
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidCurrentDocument(after.Document, after.ContentRevision));
        string genuineCareerReceipts = JsonSerializer.Serialize(after.Document.AuxiliaryState.CharacterCareerReputationReceipts);
        if (afterCareer)
        {
            Assert.AreNotEqual(review.Value!.Projection.CharacterXml, after.Document.Content);
            Assert.AreEqual(XDocument.Parse(review.Value.Projection.CharacterXml).Root!.Element("newskills")!.ToString(),
                XDocument.Parse(after.Document.Content).Root!.Element("newskills")!.ToString());
            Assert.IsTrue(CharacterCareerReputationTransaction.IsValidHistory(after));
        }
        var archive = after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        var entry = after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single();
        var authority = archive.CompletionAuthority!;
        Assert.AreEqual(CharacterCreationCompletionAuthority.SchemaV4, authority.Schema);
        Assert.AreEqual(CharacterCreationCompletionFinalizationSchemas.ProjectionV3, review.Value!.Projection.Schema);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(authority.SelectedReplay, out var bootstrap, out var witness));
        var order = witness.Skills.KarmaCompensationSourceOrder!.ToArray();
        var enabled = choices.SkillKarmaPurchases!.Select(row => row.SourceSkillId).ToHashSet(StringComparer.Ordinal);
        var positions = order.Select((id, index) => (id, index)).Where(row => enabled.Contains(row.id)).ToArray();
        Assert.IsTrue(positions.Length >= 3);
        (order[positions[1].index], order[positions[2].index]) = (order[positions[2].index], order[positions[1].index]);
        Assert.AreEqual(witness.Skills.KarmaCompensationSourceOrder[positions[0].index], order[positions[0].index]);
        var skills = witness.Skills with { KarmaCompensationSourceOrder = order, SnapshotDigest = string.Empty };
        skills = skills with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(skills) };
        var changed = witness with { Skills = skills };
        changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, changed, out var encoded));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(encoded, out var coldBootstrap, out var coldWitness));
        // Equal pricing and successful semantic replay are necessary controls:
        // rejecting a malformed transport would not exercise the review finding.
        var oldReplay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, authority.Proposal);
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, authority.Proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(oldReplay.Value!.Balances), JsonSerializer.Serialize(replay.Value!.Balances));
        Assert.AreEqual(JsonSerializer.Serialize(oldReplay.Value.Skills.Skills), JsonSerializer.Serialize(replay.Value.Skills.Skills));
        Assert.AreEqual(JsonSerializer.Serialize(oldReplay.Value.Skills.SkillGroups), JsonSerializer.Serialize(replay.Value.Skills.SkillGroups));
        var regenerated = CharacterCreationCompletionTransaction.ProjectSelected(original, coldBootstrap, coldWitness,
            authority.Proposal, authority.StartingCashSource, authority.StartingCash);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, regenerated.Outcome, string.Join(",", regenerated.Blockers));
        var projection = regenerated.Value!;
        Assert.AreEqual(JsonSerializer.Serialize(review.Value.Projection.OrderedDeltas), JsonSerializer.Serialize(projection.OrderedDeltas));
        Assert.IsFalse(NativeIdentityElements(XDocument.Parse(after.Document.Content).Root!.Element("newskills")!)
            .Select(row => Guid.Parse(row.Value)).Intersect(
                NativeIdentityElements(XDocument.Parse(projection.CharacterXml).Root!.Element("newskills")!)
                    .Select(row => Guid.Parse(row.Value))).Any(),
            "The order-bound projection must change every native skill/group seed, including price-neutral rows.");
        Assert.AreNotEqual(after.Document.Content, projection.CharacterXml);
        Assert.AreNotEqual(entry.Receipt.RawCharacterXmlDigest, projection.ExpectedResultRawCharacterXmlDigest);
        var changedAuthority = authority with { SelectedReplay = encoded };
        changedAuthority = changedAuthority with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(changedAuthority) };
        string commandDigest = CharacterCreationCompletionTransaction.CommandDigest(
            new(projection.Binding, authority.Proposal, projection.ReviewDigest, authority.StartingCash,
                projection.ProjectionDigest, key, true));
        var receipt = entry.Receipt with
        {
            CompletionAuthorityDigest = changedAuthority.AuthorityDigest,
            AuthorityDigest = projection.Binding.AuthorityDigest, PreviewDigest = projection.ReviewDigest,
            PlanDigest = projection.ProjectionDigest, CommandDigest = commandDigest,
            ReceiptId = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = CharacterCreationFinalizationSchemas.ReceiptV1, Id = after.Id,
                idempotencyDigest = entry.IdempotencyKeyDigest, commandDigest
            }),
            RawCharacterXmlDigest = resealResultDigest ? projection.ExpectedResultRawCharacterXmlDigest : entry.Receipt.RawCharacterXmlDigest
        };
        receipt = receipt with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt) };
        var ledger = new[] { entry with { CommandDigest = commandDigest, Receipt = receipt } };
        var changedArchive = archive with { CompletionAuthority = changedAuthority };
        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidLedger(after.Id, after.ContentRevision, ledger));
        Assert.AreEqual(resealResultDigest, CharacterCreationCompletionTransaction.IsValidArchive(after.Id, changedArchive, receipt));
        var auxiliary = after.Document.AuxiliaryState with
        { CharacterCreationFinalizationArchive = changedArchive, CharacterCreationFinalizationReceipts = ledger };
        var forged = after.Document with { State = after.Document.State with { AuxiliaryState = auxiliary } };
        Assert.AreEqual(!afterCareer && !resealResultDigest,
            CharacterCreationCompletionTransaction.IsValidCurrentDocument(forged, after.ContentRevision));
        Assert.AreEqual(after.Document.Content, forged.Content);
        Assert.AreEqual(genuineCareerReceipts, JsonSerializer.Serialize(forged.AuxiliaryState.CharacterCareerReputationReceipts));
        if (afterCareer)
            Assert.IsTrue(CharacterCareerReputationTransaction.IsValidHistory(after with { Document = forged }),
                "The negative must pass Career history validation and fail the independent completion identity check.");
        // The actual persisted native XML stays fixed. Reseal every auxiliary
        // layer, including the claimed result when requested, and use a new store.
        byte[] acceptedBytes = File.ReadAllBytes(probe.TargetPath!);
        try
        {
            var record = System.Text.Json.Nodes.JsonNode.Parse(acceptedBytes)!.AsObject();
            record["AuxiliaryState"] = JsonSerializer.SerializeToNode(auxiliary);
            File.WriteAllText(probe.TargetPath!, record.ToJsonString());
            var persisted = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(probe.TargetPath!))!;
            Assert.AreEqual(System.Text.Json.Nodes.JsonNode.Parse(acceptedBytes)!["Envelope"]!.ToJsonString(),
                persisted["Envelope"]!.ToJsonString());
            Assert.AreEqual(JsonSerializer.SerializeToNode(after.Document.AuxiliaryState.CharacterCareerReputationReceipts)?.ToJsonString(),
                persisted["AuxiliaryState"]!["CharacterCareerReputationReceipts"]?.ToJsonString());
            Assert.IsFalse(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Success);
        }
        finally { File.WriteAllBytes(probe.TargetPath!, acceptedBytes); }
        var restored = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId);
        Assert.IsTrue(restored.Success, restored.Error);
        Assert.AreEqual(after.Document.Content, restored.Value!.Document.Content);
        Assert.AreEqual(genuineCareerReceipts, JsonSerializer.Serialize(restored.Value.Document.AuxiliaryState.CharacterCareerReputationReceipts));
        Assert.AreEqual(applied.Value!.ReceiptDigest, Service(context, new FileWorkspaceStore(context.Directory), owners)
            .Apply(owners.Capture(), Command(review.Value, key)).Value!.ReceiptDigest);
    }


    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_funded_result_recovers_after_native_rename_failure_once(bool compensate)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", compensate.ToString()));
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = FundedCompensationPurchases(inputs) };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe { ThrowAt = FileWorkspaceStoreFaultStage.AfterTargetReplaced };
        var store = new FileWorkspaceStore(context.Directory, probe);
        var before = store.Get(context.WorkspaceId).Value!;
        var service = Service(context, store, owners);
        var review = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!, "funded-rename-recovery");
        probe.Armed = true;
        var applied = service.Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(1, probe.Throws);
        Assert.AreEqual(1, probe.Replacements);
        Assert.AreEqual(0, owners.ActiveLeases);
        var cold = new FileWorkspaceStore(context.Directory);
        var opened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(opened.Success, opened.Error);
        Assert.AreEqual(before.ContentRevision + 1, opened.Value!.ContentRevision);
        Assert.AreEqual(review.Value!.Projection.CharacterXml, opened.Value.Document.Content);
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidCurrentDocument(opened.Value.Document, opened.Value.ContentRevision));
        var repeated = Service(context, cold, owners).Apply(owners.Capture(), command);
        Assert.AreEqual(applied.Value!.ReceiptDigest, repeated.Value!.ReceiptDigest);
        Assert.AreEqual(JsonSerializer.Serialize(opened.Value), JsonSerializer.Serialize(cold.Get(context.WorkspaceId).Value));
        foreach (var replayCheck in context.ReplayChecks) replayCheck(new FileWorkspaceStore(context.Directory));
    }



    [TestMethod]
    [DataRow(true, "skill-rating")]
    [DataRow(true, "group-rating")]
    [DataRow(true, "specialization")]
    [DataRow(false, "skill-rating")]
    [DataRow(false, "group-rating")]
    [DataRow(false, "specialization")]
    public void Completion_history_accepts_native_career_rating_and_specialization_saves(bool compensate, string change)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", compensate.ToString()));
        var store = new FileWorkspaceStore(context.Directory);
        var inputs = CompleteIdentityHistory(context, store);
        ApplyCompletionCareerReputation(context, store);
        var rewards = new WorkspaceCharacterAfterRunRewardService(store);
        var reward = rewards.Preview(new(context.WorkspaceId, Guid.NewGuid(), Guid.NewGuid(),
            50, 0, new DateTime(2078, 9, 6, 18, 0, 0), "Fund Career skill progression"));
        Assert.IsNotNull(reward.Preview, reward.Error);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied,
            rewards.Commit(reward.Preview.Command with { ExplicitlyConfirmed = true }).Outcome);
        var before = store.Get(context.WorkspaceId).Value!;
        var xml = XDocument.Parse(before.Document.Content);
        var root = xml.Root!;
        var native = root.Element("newskills")!;
        string identitiesBefore = NativeIdentityList(native);
        var source = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var skill = native.Element("skills")!.Elements("skill").Single(row => row.Element("suid")!.Value == source.SourceSkillId);
        int availableKarma = int.Parse(root.Element("karma")!.Value, CultureInfo.InvariantCulture);
        Guid expenseId = Guid.NewGuid();
        int cost;
        string undoType, undoId, reason;
        if (change == "specialization")
        {
            var sources = context.Resolver.TryCreateContext(before.Document.Content)!;
            Assert.IsTrue(sources.TryResolveCareerSkillSpecializationSettings(out var settings, out var ruleState));
            Assert.IsTrue(sources.TryResolveCareerSkillSpecializationSource(source.SourceSkillId,
                CharacterCareerSkillKind.Active, out var specSource));
            var option = specSource.Options.First();
            var input = new CharacterCareerSkillSpecializationInput(
                new(Guid.Parse(skill.Element("guid")!.Value), Guid.Parse(source.SourceSkillId), CharacterCareerSkillKind.Active),
                true, true, false, true, true, false, specSource.Name, specSource.SkillCategory, specSource.Name, "",
                int.Parse(skill.Element("base")!.Value) + int.Parse(skill.Element("karma")!.Value),
                skill.Element("specs")?.Elements("spec").Count() ?? 0, availableKarma, 0, false, false,
                settings, [], specSource.Options, new(option.Name, option.Kind, option.OptionIdentity),
                before.Document.Content, specSource.RawSourceState, ruleState);
            Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(input, out var quote));
            Assert.IsTrue(quote.CanAdd, quote.Blocker.ToString());
            Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryPlanAdd(quote, quote.CharacterRevision,
                quote.SourceRevision, quote.RuleDigest, quote.LogicalRevision, true, Guid.NewGuid(), expenseId,
                new DateTime(2078, 9, 7, 18, 0, 0), out var plan));
            var specs = skill.Element("specs");
            if (specs is null) { specs = new XElement("specs"); skill.Add(specs); }
            specs.Add(new XElement("spec", new XElement("guid", plan.SpecializationId),
                new XElement("name", plan.SpecializationName), new XElement("free", plan.SavedFree),
                new XElement("expertise", plan.SavedExpertise)));
            cost = -plan.ExpenseAmount; undoType = plan.KarmaUndoType; undoId = plan.UndoObjectId; reason = plan.ExpenseReason;
            Assert.AreEqual(availableKarma - cost, plan.SavedCharacterKarma);
        }
        else
        {
            // Native Skill.Upgrade / SkillGroup.Upgrade retain the parent identity,
            // increment Karma and append an expense. Persist their saved XML shape
            // through the existing workspace CAS/checkpoint contract.
            var row = change == "group-rating"
                ? native.Element("groups")!.Elements("group").Single(group => group.Element("name")!.Value == "Athletics")
                : skill;
            int oldKarma = int.Parse(row.Element("karma")!.Value, CultureInfo.InvariantCulture);
            int nextRating = int.Parse(row.Element("base")!.Value, CultureInfo.InvariantCulture) + oldKarma + 1;
            cost = nextRating * (change == "group-rating"
                ? inputs.Skills.KarmaPurchasePolicy!.KarmaImproveSkillGroup
                : inputs.Skills.KarmaPurchasePolicy!.KarmaImproveActiveSkill);
            Assert.IsTrue(change != "group-rating" || row.Element("isbroken") is not { } broken || !bool.Parse(broken.Value));
            row.SetElementValue("karma", oldKarma + 1);
            undoType = change == "group-rating" ? "ImproveSkillGroup" : "ImproveSkill";
            undoId = row.Element(change == "group-rating" ? "id" : "guid")!.Value;
            reason = $"Career {change} to {nextRating}";
        }
        Assert.IsTrue(availableKarma >= cost);
        root.SetElementValue("karma", availableKarma - cost);
        root.Element("expenses")!.Add(new XElement("expense", new XElement("guid", expenseId),
            new XElement("date", "2078-09-07T18:00:00"), new XElement("amount", -cost),
            new XElement("reason", reason), new XElement("type", "Karma"), new XElement("refund", false),
            new XElement("forcecareervisible", false), new XElement("undo",
                new XElement("karmatype", undoType), new XElement("nuyentype", "AddCyberware"),
                new XElement("objectid", undoId), new XElement("qty", 0), new XElement("extra", ""))));
        Assert.AreEqual(identitiesBefore, NativeIdentityList(native));
        var replacement = before.Document with { State = before.Document.State with { Payload = xml.ToString(SaveOptions.DisableFormatting) } };
        var saved = store.ReplaceWorkspaceDocumentAndCheckpoint(before.Id, before.ContentRevision, replacement);
        Assert.IsTrue(saved.Success, saved.Error);
        var opened = new FileWorkspaceStore(context.Directory).Get(before.Id);
        Assert.IsTrue(opened.Success, opened.Error);
        Assert.AreEqual(before.ContentRevision + 1, opened.Value!.ContentRevision);
        Assert.AreEqual(replacement.Content, opened.Value.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, opened.Value.Document.AuxiliaryStateDigest);
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidCurrentDocument(opened.Value.Document, opened.Value.ContentRevision));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available,
            new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(context.Directory), context.Resolver).Read(before.Id).Outcome);
    }

    [TestMethod]
    [DataRow("remove-knowledge")]
    [DataRow("replace-skill")]
    [DataRow("retain-skill-only")]
    [DataRow("retain-group-only")]
    public void Completion_order_identity_survives_partial_native_removal_or_replacement(string change)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True"));
        var store = new FileWorkspaceStore(context.Directory);
        CompleteIdentityHistory(context, store);
        ApplyCompletionCareerReputation(context, store);
        var before = store.Get(context.WorkspaceId).Value!;
        var xml = XDocument.Parse(before.Document.Content);
        var native = xml.Root!.Element("newskills")!;
        var firstSkill = native.Element("skills")!.Elements("skill").First().Element("guid")!;
        var firstGroup = native.Element("groups")!.Elements("group").First().Element("id")!;
        if (change == "remove-knowledge")
            native.Element("knoskills")!.Elements("skill").First().Remove();
        else if (change == "replace-skill")
            firstSkill.Value = Guid.NewGuid().ToString("D");
        else
        {
            // Whole-document replacement is the existing API for native saves,
            // including imports into an editor and removal/re-add with new IDs.
            // Exercise each surviving kind independently, without a fake delete API.
            var retained = change == "retain-skill-only" ? firstSkill : firstGroup;
            foreach (var identity in NativeIdentityElements(native).Where(element => element != retained))
                identity.Value = Guid.NewGuid().ToString("D");
            retained.Value = Guid.Parse(retained.Value).ToString("D").ToUpperInvariant();
        }
        var replacement = before.Document with { State = before.Document.State with { Payload = xml.ToString(SaveOptions.DisableFormatting) } };
        var saved = store.ReplaceWorkspaceDocumentAndCheckpoint(before.Id, before.ContentRevision, replacement);
        Assert.IsTrue(saved.Success, saved.Error);
        var cold = new FileWorkspaceStore(context.Directory).Get(before.Id);
        Assert.IsTrue(cold.Success, cold.Error);
        Assert.AreEqual(replacement.Content, cold.Value!.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, cold.Value.Document.AuxiliaryStateDigest);
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidCurrentDocument(cold.Value.Document, cold.Value.ContentRevision));
    }

    [TestMethod]
    [DataRow("replace-all")]
    [DataRow("remove-all")]
    [DataRow("specialization-decoy")]
    [DataRow("duplicate-container")]
    [DataRow("duplicate-identity")]
    public void Completion_order_identity_rejects_missing_or_ambiguous_anchors_without_writing(string change)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True"));
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        CompleteIdentityHistory(context, store);
        ApplyCompletionCareerReputation(context, store);
        var before = store.Get(context.WorkspaceId).Value!;
        var xml = XDocument.Parse(before.Document.Content);
        var native = xml.Root!.Element("newskills")!;
        string originalSkillId = native.Element("skills")!.Elements("skill").First().Element("guid")!.Value;
        if (change == "duplicate-container") xml.Root.Add(new XElement(native));
        else if (change == "duplicate-identity")
            native.Element("skills")!.Add(new XElement(native.Element("skills")!.Elements("skill").First()));
        else
        {
            foreach (var identity in NativeIdentityElements(native)) identity.Value = Guid.NewGuid().ToString("D");
            if (change == "remove-all")
                foreach (string container in new[] { "skills", "knoskills", "groups" }) native.Element(container)!.RemoveNodes();
            if (change == "specialization-decoy")
                native.Element("skills")!.Elements("skill").First().Add(new XElement("specs",
                    new XElement("spec", new XElement("guid", originalSkillId), new XElement("name", "Decoy"))));
        }
        var replacement = before.Document with { State = before.Document.State with { Payload = xml.ToString(SaveOptions.DisableFormatting) } };
        var files = Snapshot(context);
        Assert.IsFalse(CharacterCreationCompletionTransaction.IsValidCurrentDocument(replacement, before.ContentRevision + 1));
        Assert.IsFalse(store.ReplaceWorkspaceDocumentAndCheckpoint(before.Id, before.ContentRevision, replacement).Success);
        AssertUnchanged(context, files);
        // Also exercise bypassing write admission: a new file-store lifetime must
        // reject the same invalid native payload independently of the failed CAS.
        byte[] accepted = File.ReadAllBytes(probe.TargetPath!);
        try
        {
            var record = System.Text.Json.Nodes.JsonNode.Parse(accepted)!;
            record["Envelope"]!["Payload"] = replacement.Content;
            record["ContentRevision"] = before.ContentRevision + 1;
            record["SavedRevision"] = before.ContentRevision + 1;
            File.WriteAllText(probe.TargetPath!, record.ToJsonString());
            Assert.IsFalse(new FileWorkspaceStore(context.Directory).Get(before.Id).Success);
        }
        finally { File.WriteAllBytes(probe.TargetPath!, accepted); }
        AssertUnchanged(context, files);
        Assert.IsTrue(new FileWorkspaceStore(context.Directory).Get(before.Id).Success);
        if (change == "replace-all")
        {
            // The existing raw-import contract starts a distinct workspace and
            // does not claim the old completion history for replacement identities.
            var codec = new Sr5WorkspaceCodec(context.Queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(context.Resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var workspaces = new WorkspaceService(store, new RulesetWorkspaceCodecResolver([codec]),
                new WorkspaceImportRulesetDetector());
            var imported = workspaces.Import(new WorkspaceImportDocument(replacement.Content, "sr5", WorkspaceDocumentFormat.NativeXml));
            Assert.AreNotEqual(before.Id, imported.Id);
            var importedRead = new FileWorkspaceStore(context.Directory).Get(imported.Id);
            Assert.IsTrue(importedRead.Success, importedRead.Error);
            Assert.IsNull(importedRead.Value!.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
            Assert.IsNull(importedRead.Value.Document.AuxiliaryState.CharacterCreationFinalizationReceipts);
            Assert.AreEqual(replacement.Content, importedRead.Value.Document.Content);
        }
    }

    [TestMethod]
    public void Completion_exact_revision_rejects_native_identity_corruption()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True"));
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        CompleteIdentityHistory(context, store);
        var before = store.Get(context.WorkspaceId).Value!;
        var xml = XDocument.Parse(before.Document.Content);
        NativeIdentityElements(xml.Root!.Element("newskills")!).First().Value = Guid.NewGuid().ToString("D");
        var replacement = before.Document with { State = before.Document.State with { Payload = xml.ToString(SaveOptions.DisableFormatting) } };
        Assert.IsFalse(CharacterCreationCompletionTransaction.IsValidCurrentDocument(replacement, before.ContentRevision));
        byte[] accepted = File.ReadAllBytes(probe.TargetPath!);
        try
        {
            var record = System.Text.Json.Nodes.JsonNode.Parse(accepted)!;
            record["Envelope"]!["Payload"] = replacement.Content;
            File.WriteAllText(probe.TargetPath!, record.ToJsonString());
            Assert.IsFalse(new FileWorkspaceStore(context.Directory).Get(before.Id).Success);
        }
        finally { File.WriteAllBytes(probe.TargetPath!, accepted); }
        Assert.IsTrue(new FileWorkspaceStore(context.Directory).Get(before.Id).Success);
    }

    private static CharacterCreationCompletionReviewInputs CompleteIdentityHistory(ReadyContext context, FileWorkspaceStore store)
    {
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with { SkillKarmaPurchases = [] };
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = Service(context, store, owners);
        var review = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var applied = service.Apply(owners.Capture(), Command(review.Value!, "career-native-identity"));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(inputs.Skills.KarmaPurchasePolicy!.CompensateSkillGroupKarmaDifference
            ? CharacterCreationCompletionAuthority.SchemaV4 : CharacterCreationCompletionAuthority.SchemaV3,
            store.Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.CompletionAuthority!.Schema);
        return inputs;
    }

    private static IEnumerable<XElement> NativeIdentityElements(XElement native)
        => native.Element("skills")!.Elements("skill").Select(row => row.Element("guid")!)
            .Concat(native.Element("knoskills")!.Elements("skill").Select(row => row.Element("guid")!))
            .Concat(native.Element("groups")!.Elements("group").Select(row => row.Element("id")!));

    private static string NativeIdentityList(XElement native)
        => string.Join("|", NativeIdentityElements(native).Select(element => element.Value));

    private static void ApplyCompletionCareerReputation(ReadyContext context, FileWorkspaceStore store)
    {
        var before = store.Get(context.WorkspaceId).Value!;
        var career = new WorkspaceCharacterCareerReputationService(store, context.Resolver);
        var preview = career.Preview(new(context.WorkspaceId, Guid.NewGuid(),
            CharacterCareerReputationOperation.AdjustManualAwards, new(1, null, null), "Completion identity regression"));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, preview.Outcome, preview.Error);
        var committed = career.Commit(preview.Preview!.Command with { ExplicitlyConfirmed = true });
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, committed.Outcome, committed.Error);
        var opened = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId);
        Assert.IsTrue(opened.Success, opened.Error);
        Assert.AreEqual(before.ContentRevision + 1, opened.Value!.ContentRevision);
        Assert.AreEqual(opened.Value.ContentRevision, opened.Value.SavedRevision);
        Assert.AreEqual(committed.Receipt!.ReceiptDigest,
            opened.Value.Document.AuxiliaryState.CharacterCareerReputationReceipts!.Single().ReceiptDigest);
    }

    private static CharacterCreationSkillKarmaPurchase[] FundedCompensationPurchases(CharacterCreationCompletionReviewInputs inputs)
    {
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        return CharacterCreationCompletionPlannerTests.EnabledGroupMembers(inputs, group)
            .Select(skill => new CharacterCreationSkillKarmaPurchase(skill.SourceSkillId, skill.Kind, skill.SourceNodeDigest, 2)).ToArray();
    }
}
