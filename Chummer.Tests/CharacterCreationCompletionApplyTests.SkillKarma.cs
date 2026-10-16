using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
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
    [DataRow(true)]
    [DataRow(false)]
    public void Completion_skill_karma_archive_rejects_rehashed_policy_or_payment_changes_after_semantic_replay(bool changePolicy)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = CharacterCreationCompletionPlannerTests.Purchases22(inputs), TotalKarmaInvestment = 3 };
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
        Assert.AreEqual((int?)22, replay.Value!.Balances.SkillsKarmaCost);

        var proposal = authority.Proposal;
        var changed = witness!;
        if (changePolicy)
        {
            var policy = changed.Skills.KarmaPurchasePolicy! with { KarmaImproveActiveSkill = 1 };
            policy = policy with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy) };
            var skills = changed.Skills with { KarmaPurchasePolicy = policy, SnapshotDigest = string.Empty };
            skills = skills with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(skills) };
            changed = changed with { Skills = skills };
        }
        else
        {
            var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
            proposal = proposal with { Selections = proposal.Selections with
            {
                SkillKarmaPurchases = proposal.Selections.SkillKarmaPurchases!.Select(row =>
                    row.SourceSkillId == pistols.SourceSkillId ? row with { Rating = 3 } : row).ToArray()
            } };
        }
        changed = changed with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(proposal) };
        changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, changed, out var encoded));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(encoded, out var decodedBootstrap, out var decoded));
        var historical = CharacterCreationSelectedCompletionEvaluator.Replay(original, decodedBootstrap!, decoded!, proposal);
        string diagnostic = $"Changed {(changePolicy ? "purchase policy" : "paid rating")}; "
            + $"historical outcome: {historical.Outcome}; blockers: {string.Join(",", historical.Blockers)}";
        // Both cheaper alternatives are legal quotes. Passing transport/digest
        // validation must still reach repricing and the committed-result comparison.
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, historical.Outcome, diagnostic);
        Assert.AreEqual((int?)(changePolicy ? 13 : 14), historical.Value!.Balances.SkillsKarmaCost, diagnostic);
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
    public void Completion_skill_karma_owner_revision_and_atomic_failure_leave_no_partial_durable_state()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = CharacterCreationCompletionPlannerTests.Purchases22(inputs) };
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
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_skill_karma_reprices_current_sources_before_atomic_replacement(bool duringFlush)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: _ => { });
        var inputs = CharacterCreationCompletionPlannerTests.SkillKarmaInputs(context);
        Assert.AreEqual(2, inputs.Skills.KarmaPurchasePolicy!.KarmaImproveActiveSkill);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { SkillKarmaPurchases = CharacterCreationCompletionPlannerTests.Purchases22(inputs) };
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
            var xml = XDocument.Load(path);
            xml.Root!.Element("settings")!.Elements("setting").Single(row => row.Element("id")!.Value == profile)
                .Element("karmacost")!.SetElementValue("karmaimproveactiveskill", "3");
            xml.Save(path);
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
        Assert.AreEqual(3, currentPolicy!.KarmaImproveActiveSkill);
        AssertUnchanged(context, files);
    }
}
