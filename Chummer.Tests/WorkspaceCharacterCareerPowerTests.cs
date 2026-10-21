using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed partial class WorkspaceCharacterCareerPowerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Enabled_Combat_Sense_add_and_raise_persist_points_effects_and_checkpoint(bool linked)
    {
        using var f = new Fixture(linked: linked);
        var command = Command("Combat Sense", 1);
        var plan = f.Review(command);
        Assert.AreEqual(0.5m, plan.Quote.IncrementalCost);
        Assert.AreEqual(6m, plan.Quote.TotalPoints);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        Assert.AreEqual(1m, Effect(f.Saved(), "Dodge"));
        Assert.AreEqual(2L, f.Saved().SavedRevision);
        var raised = f.Review(command with { TransactionId = Guid.NewGuid(), PaidRating = 2 });
        Assert.AreEqual(0.5m, raised.Quote.IncrementalCost);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(raised).Outcome);
        Assert.AreEqual(2m, Effect(f.Saved(), "Dodge"));
        Assert.AreEqual(2, f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions!.Count);
        Assert.AreEqual("100", XElement.Parse(f.Saved().Document.Content).Element("karma")!.Value);
        Assert.AreEqual(0, XElement.Parse(f.Saved().Document.Content).Element("expenses")!.Elements().Count());
    }

    [TestMethod]
    public void Mystic_adept_uses_purchased_pool_and_quarter_point_increase()
    {
        using var f = new Fixture(character: Runner(mystic: true, purchased: 2));
        var command = Command("Kinesics", 1);
        var first = f.Review(command);
        Assert.AreEqual(2m, first.Quote.TotalPoints);
        Assert.AreEqual(0.25m, first.Quote.IncrementalCost);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(first).Outcome);
        var second = f.Review(command with { TransactionId = Guid.NewGuid(), PaidRating = 2 });
        Assert.AreEqual(0.25m, second.Quote.IncrementalCost);
        Assert.AreEqual(1.5m, second.Quote.ProposedRemainingPoints);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(second).Outcome);
        Assert.AreEqual(2m, Effect(f.Saved(), "JudgeIntentionsDefense"));
    }

    [TestMethod]
    public void Separate_mystic_magic_uses_explicit_selected_profile_policy()
    {
        using var f = new Fixture(character: Runner(mystic: true, purchased: 1));
        // Explicit test profile variant; no production fallback or rules default.
        f.Sources.SeparateMagic = true;
        var plan = f.Review(Command("Kinesics", 1));
        Assert.AreEqual(3m, plan.Quote.TotalPoints);
    }

    [TestMethod]
    public void Insufficient_points_forbidden_source_unknown_semantics_and_bad_choice_do_not_mutate()
    {
        using var f = new Fixture(character: Runner(mystic: true, purchased: 0));
        string before = f.Saved().Document.Content;
        Assert.IsNull(f.Service.Review(f.Owners.Capture(), f.Id, Command("Kinesics", 1)));
        using var enabled = new Fixture();
        enabled.Sources.Forbidden = Source("powers", "power", "Combat Sense").Element("id")!.Value;
        Assert.IsNull(enabled.Service.Review(enabled.Owners.Capture(), enabled.Id, Command("Combat Sense", 1)));
        Assert.IsNull(enabled.Service.Review(enabled.Owners.Capture(), enabled.Id, Command("Improved Reflexes", 1)));
        var bad = Command("Kinesics", 1);
        Assert.IsNull(enabled.Service.Review(enabled.Owners.Capture(), enabled.Id,
            bad with { Identity = bad.Identity with { Extra = "unbound choice" } }));
        Assert.AreEqual(before, f.Saved().Document.Content);
    }

    [TestMethod]
    public void Actual_way_requirement_discounts_once_and_requires_source_identity()
    {
        var character = XElement.Parse(Runner());
        character.Element("qualities")!.Add(Quality("Adept"), Quality("The Warrior's Way"));
        using var f = new Fixture(character: character.ToString());
        var command = Command("Combat Sense", 1) with { ApplyWayDiscount = true };
        var first = f.Review(command);
        Assert.AreEqual(0.25m, first.Quote.IncrementalCost);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(first).Outcome);
        var second = f.Review(command with { TransactionId = Guid.NewGuid(), PaidRating = 2 });
        Assert.AreEqual(0.5m, second.Quote.IncrementalCost);
        Assert.AreEqual(0.75m, second.Quote.Proposed.UsedPoints);
        using var noWay = new Fixture();
        Assert.IsNull(noWay.Service.Review(noWay.Owners.Capture(), noWay.Id, command));
    }

    [TestMethod]
    public void Actual_Bear_mentor_grant_recomputes_effects_at_paid_plus_free_rating()
    {
        using var f = new Fixture(character: MentorRunner());
        var plan = f.Review(Command("Rapid Healing", 1));
        Assert.AreEqual(1, plan.Quote.Proposed.FreeLevels);
        Assert.AreEqual(2, plan.Quote.Proposed.TotalRating);
        Assert.AreEqual(0.5m, plan.Quote.Proposed.UsedPoints);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        Assert.AreEqual(2m, Effect(f.Saved(), "PhysicalCMRecovery"));
        Assert.AreEqual(1, f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions![0].Grants.Count);
    }

    [TestMethod]
    public void Explicit_custom_specificpower_quarter_grant_is_bound_to_exact_source_and_choice()
    {
        var character = XElement.Parse(MentorRunner());
        var grant = character.Element("improvements")!.Elements().Single();
        grant.Element("improvementttype")!.Value = "AdeptPowerFreePoints";
        grant.Element("rating")!.Value = "3";
        using var f = new Fixture(character: character.ToString());
        // An authored test overlay of Bear's existing specificpower schema. It
        // deliberately is not a claim that the stock Bear grants free PP.
        f.Sources.CustomQuarterGrant = true;
        var plan = f.Review(Command("Rapid Healing", 1));
        Assert.AreEqual(0.75m, plan.Quote.Proposed.FreePoints);
        Assert.AreEqual(0.25m, plan.Quote.Proposed.UsedPoints);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        Assert.AreEqual(2m, Effect(f.Saved(), "StunCMRecovery"));
    }

    [TestMethod]
    public void Forged_duplicate_and_mismatched_grant_origins_fail_closed()
    {
        var character = XElement.Parse(MentorRunner());
        var effects = character.Element("improvements")!;
        effects.Add(new XElement(effects.Elements().Single()));
        using var duplicate = new Fixture(character: character.ToString());
        Assert.IsNull(duplicate.Service.Review(duplicate.Owners.Capture(), duplicate.Id, Command("Rapid Healing", 1)));
        character = XElement.Parse(MentorRunner());
        character.Element("improvements")!.Element("improvement")!.Element("unique")!.Value = "other choice";
        using var mismatch = new Fixture(character: character.ToString());
        Assert.IsNull(mismatch.Service.Review(mismatch.Owners.Capture(), mismatch.Id, Command("Rapid Healing", 1)));
    }

    [TestMethod]
    public void Attribute_choice_effect_and_cached_attribute_recalculate_together()
    {
        using var f = new Fixture();
        var cmd = Command("Improved Physical Attribute", 1);
        cmd = cmd with { Identity = cmd.Identity with { Extra = "BOD" } };
        var plan = f.Review(cmd);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        var root = XElement.Parse(f.Saved().Document.Content);
        Assert.AreEqual("4", root.Element("attributes")!.Elements().Single(a => a.Element("name")!.Value == "BOD")
            .Element("totalvalue")!.Value);
        Assert.AreEqual("BOD", root.Element("powers")!.Element("power")!.Element("extra")!.Value);
        Assert.AreEqual("1", root.Element("improvements")!.Element("improvement")!.Element("aug")!.Value);
    }

    [TestMethod]
    public void Owner_A_B_A_and_foreign_owner_are_rejected_before_revalidation()
    {
        using var f = new Fixture(linked: true);
        var plan = f.Review(Command("Kinesics", 1));
        var stamp = f.Owners.Capture();
        f.Owners.Set(new("owner-B"));
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Service.Commit(f.Owners.Capture(), plan).Outcome);
        f.Owners.Set(f.Owner);
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Service.Commit(stamp, plan).Outcome);
        Assert.AreEqual(1L, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Revision_and_source_drift_reject_without_partial_effects()
    {
        using var f = new Fixture();
        var plan = f.Review(Command("Kinesics", 1));
        f.Sources.Generation++;
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(plan).Outcome);
        Assert.AreEqual(1L, f.Saved().ContentRevision);
        f.Sources.Generation--;
        var saved = f.Saved();
        var doc = new WorkspaceDocument(saved.Document.Content.Replace("keep", "changed"), "sr5");
        Assert.IsTrue(f.Store.ReplaceWorkspaceDocument(f.Id, saved.ContentRevision, doc).Success);
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(plan).Outcome);
    }

    [TestMethod]
    public void Source_drift_at_replace_boundary_leaves_no_receipt_or_rating()
    {
        var fault = new Fault();
        using var f = new Fixture(fault: fault);
        var plan = f.Review(Command("Kinesics", 1));
        fault.Action = stage => { if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) f.Sources.Generation++; };
        Assert.AreEqual(CharacterCareerPowerOutcome.Unavailable, f.Commit(plan).Outcome);
        Assert.AreEqual(1L, f.Saved().ContentRevision);
        Assert.IsNull(f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions);
    }

    [TestMethod]
    public void Cold_reopen_exact_retry_precedes_missing_authority_and_different_command_conflicts()
    {
        using var f = new Fixture();
        var plan = f.Review(Command("Kinesics", 1));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        f.Sources.Unavailable = true;
        var cold = new OwnerBoundCharacterCareerPowerService(new FileWorkspaceStore(f.Path), f.Owners, f.Authority);
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed, cold.Commit(f.Owners.Capture(), plan).Outcome);
        var different = plan with { Command = plan.Command with { ApplyWayDiscount = true } };
        Assert.AreEqual(CharacterCareerPowerOutcome.IdempotencyConflict, cold.Commit(f.Owners.Capture(), different).Outcome);
        Assert.AreEqual(2L, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Uncertain_result_replay_recovers_one_durable_operation()
    {
        var fault = new Fault();
        using var f = new Fixture(fault: fault);
        var plan = f.Review(Command("Kinesics", 1));
        fault.Action = stage => { if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) throw new IOException("lost acknowledgement"); };
        var result = f.Commit(plan);
        Assert.IsTrue(result.Outcome is CharacterCareerPowerOutcome.Applied or CharacterCareerPowerOutcome.Replayed);
        var cold = new OwnerBoundCharacterCareerPowerService(new FileWorkspaceStore(f.Path), f.Owners, f.Authority);
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed, cold.Lookup(f.Owners.Capture(), plan).Outcome);
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed, cold.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.AreEqual(1, f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions!.Count);
        Assert.AreEqual(0.25m, f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions![0].Plan.Quote.IncrementalCost);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Durable_readback_rejects_unwritten_success_and_recovers_lost_adapter_response(bool written)
    {
        using var f = new Fixture();
        var plan = f.Review(Command("Kinesics", 1));
        var adapter = new ObservingStore(f.Store) { SimulateUnwrittenSuccess = !written, LoseResponse = written };
        var service = new OwnerBoundCharacterCareerPowerService(adapter, f.Owners, f.Authority);
        var result = service.Commit(f.Owners.Capture(), plan);
        Assert.AreEqual(written ? CharacterCareerPowerOutcome.Replayed : CharacterCareerPowerOutcome.Unavailable,
            result.Outcome);
        Assert.AreEqual(written ? 2L : 1L, f.Saved().ContentRevision);
        adapter.LoseResponse = adapter.SimulateUnwrittenSuccess = false;
        Assert.AreEqual(written ? CharacterCareerPowerOutcome.Replayed : CharacterCareerPowerOutcome.Applied,
            service.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.AreEqual(1, f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions!.Count);
    }

    [TestMethod]
    public void Generic_writer_cannot_append_or_delete_power_history()
    {
        using var f = new Fixture();
        var plan = f.Review(Command("Kinesics", 1));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        var saved = f.Saved();
        var altered = saved.Document with { State = saved.Document.State with {
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerPowerTransactions = null } } };
        Assert.IsFalse(f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id, saved.ContentRevision,
            saved.Document.AuxiliaryStateDigest, altered).Success);
    }

    [TestMethod]
    public void Imported_power_history_reserves_key_without_becoming_local_replay_authority()
    {
        using var f = new Fixture();
        var plan = f.Review(Command("Kinesics", 1));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        WorkspaceImportedHistoryTestFixture.MarkImported(f.Path, f.Id);
        string before = f.Saved().Document.Content;
        Assert.AreEqual(CharacterCareerPowerOutcome.IdempotencyConflict, f.Commit(plan).Outcome);
        Assert.AreEqual(before, f.Saved().Document.Content);
    }

    [TestMethod]
    public void Auxiliary_digest_drift_and_forged_quote_are_not_admitted()
    {
        using var f = new Fixture();
        var plan = f.Review(Command("Kinesics", 1));
        var changedAuxiliary = plan with { ExpectedAuxiliaryDigest = CharacterCareerPowerTransaction.Hash("different auxiliary state") };
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(changedAuxiliary).Outcome);
        var q = plan.Quote;
        var forged = plan with { Quote = q with {
            TotalPoints = q.TotalPoints + 1, CurrentRemainingPoints = q.CurrentRemainingPoints + 1,
            ProposedRemainingPoints = q.ProposedRemainingPoints + 1 } };
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(forged).Outcome);
        Assert.AreEqual(1L, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Duplicate_add_same_choice_and_nonincrease_are_not_second_purchases()
    {
        using var f = new Fixture();
        var command = Command("Kinesics", 1);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(f.Review(command)).Outcome);
        Assert.IsNull(f.Service.Review(f.Owners.Capture(), f.Id, Command("Kinesics", 1)));
        Assert.IsNull(f.Service.Review(f.Owners.Capture(), f.Id, command with { TransactionId = Guid.NewGuid() }));
        Assert.AreEqual(2L, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Restricted_existing_power_is_preserved_and_blocks_a_budget_rewrite()
    {
        using var f = new Fixture();
        var plan = f.Review(Command("Kinesics", 1));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        string before = f.Saved().Document.Content;
        f.Sources.Forbidden = plan.Command.Identity.SourceId.ToString("D");
        Assert.IsNull(f.Service.Review(f.Owners.Capture(), f.Id, Command("Combat Sense", 1)));
        Assert.AreEqual(before, f.Saved().Document.Content);
    }

    private static decimal Effect(WorkspaceStoredDocument saved, string type) => decimal.Parse(
        XElement.Parse(saved.Document.Content).Element("improvements")!.Elements()
            .Single(e => e.Element("improvementttype")!.Value == type).Element("val")!.Value,
        System.Globalization.CultureInfo.InvariantCulture);
    private static CharacterCareerPowerCommand Command(string name, int rating) =>
        new(Guid.NewGuid(), new(Guid.NewGuid(), Guid.Parse(Source("powers", "power", name).Element("id")!.Value), ""), rating, false);
    private static XElement Source(string file, string row, string name) => XDocument.Load(
        System.IO.Path.Combine(CoreRoot(), "Chummer", "data", file + ".xml")).Root!.Element(file)!
        .Elements(row).Single(e => e.Element("name")!.Value == name);
    private static XElement Quality(string name) => new("quality",
        new XElement("sourceid", Source("qualities", "quality", name).Element("id")!.Value),
        new XElement("guid", Guid.NewGuid()), new XElement("name", name), new XElement("implemented", true));
    private static string Runner(bool mystic = false, int purchased = 0) =>
        "<character><created>True</created><karma>100</karma><adept>True</adept><magenabled>True</magenabled>"
        + "<initiategrade>0</initiategrade>"
        + $"<magician>{mystic}</magician><magsplitadept>{purchased}</magsplitadept>"
        + $"<settings>{CharacterCreationBootstrapProfiles.PrioritySettingsProfileId}</settings>"
        + "<metatype>Human</metatype><metatypeguid>a53d885d-a4a4-443d-b6a6-b0a55b0a96c7</metatypeguid>"
        + "<attributes>" + Attribute("MAG", 6, 6) + Attribute("MAGAdept", 3, 6) + Attribute("BOD", 3, 10)
        + "</attributes><powers/><qualities/><improvements/><expenses/><notes>keep</notes></character>";
    private static string Attribute(string name, int value, int aug) =>
        $"<attribute><name>{name}</name><metatypemin>1</metatypemin><metatypemax>6</metatypemax>"
        + $"<metatypeaugmax>{aug}</metatypeaugmax><base>{value - 1}</base><karma>0</karma><totalvalue>{value}</totalvalue></attribute>";
    private static string MentorRunner()
    {
        var root = XElement.Parse(Runner());
        var bear = Source("mentors", "mentor", "Bear");
        var id = Guid.NewGuid();
        root.Add(new XElement("mentorspirits", new XElement("mentorspirit",
            new XElement("guid", id), new XElement("sourceid", bear.Element("id")!.Value), new XElement("name", "Bear"),
            new XElement("choice1", bear.Element("choices")!.Elements("choice").Last().Element("name")!.Value))));
        root.Element("improvements")!.Add(new XElement("improvement", new XElement("sourcename", id),
            new XElement("improvementsource", "MentorSpirit"), new XElement("improvementttype", "AdeptPowerFreeLevels"),
            new XElement("improvedname", "Rapid Healing"), new XElement("unique", ""), new XElement("rating", 1),
            new XElement("val", 0), new XElement("min", 0), new XElement("max", 0), new XElement("aug", 0),
            new XElement("augmax", 0), new XElement("target", ""), new XElement("exclude", ""),
            new XElement("condition", ""), new XElement("custom", false), new XElement("addtorating", false),
            new XElement("enabled", true)));
        return root.ToString();
    }
    private static string CoreRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "Chummer", "data", "powers.xml"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Core source fixture root unavailable.");
    }

    private sealed class ObservingStore(FileWorkspaceStore inner) : IWorkspaceStore, ICharacterCareerPowerAtomicCommitCapability
    {
        public bool SimulateUnwrittenSuccess, LoseResponse;
        public int Reads, Commits;
        public string CareerPowerRuntimeFingerprint => inner.CareerPowerRuntimeFingerprint;
        public CharacterCareerPowerResult CommitCareerPower(OwnerScope owner, CharacterCareerPowerPlan plan,
            ICharacterCareerPowerAuthorityResolver authority, CancellationToken token = default)
        {
            Commits++;
            if (SimulateUnwrittenSuccess)
            {
                var saved = (owner.IsLocalSingleUser ? inner.Get(new(plan.WorkspaceId)) : inner.Get(owner, new(plan.WorkspaceId))).Value!;
                using var admission = authority.Open(owner, saved, plan.Command, CareerPowerRuntimeFingerprint);
                Assert.IsTrue(CharacterCareerPowerTransaction.TryBuild(owner, saved, plan, admission!, CareerPowerRuntimeFingerprint,
                    out _, out var entry));
                return new(CharacterCareerPowerOutcome.Applied, entry, plan.TargetWorkspaceRevision);
            }
            var result = inner.CommitCareerPower(owner, plan, authority, token);
            if (LoseResponse) throw new IOException("synthetic lost adapter response");
            return result;
        }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) { Reads++; return inner.Get(id); }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) { Reads++; return inner.Get(owner, id); }
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => inner.CreateWorkspaceDocument(document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => inner.CreateWorkspaceDocument(owner, document);
        public IReadOnlyList<WorkspaceStoreEntry> List() => inner.List();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => inner.List(owner);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => inner.ReplaceWorkspaceDocument(id, revision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document) => inner.ReplaceWorkspaceDocument(owner, id, revision, document);
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => inner.SaveCheckpoint(id, revision);
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision) => inner.SaveCheckpoint(owner, id, revision);
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => inner.Delete(id, revision);
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long revision) => inner.Delete(owner, id, revision);
    }

    private sealed class Fault : IFileWorkspaceStoreFaultInjector
    {
        public Action<FileWorkspaceStoreFaultStage>? Action;
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath) => Action?.Invoke(stage);
    }
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("chummer-career-power-").FullName;
        public CharacterWorkspaceId Id { get; } = new("career-power-test");
        public OwnerScope Owner { get; }
        public FileWorkspaceStore Store { get; }
        public CharacterCreationAttributesServiceTests.AllocationOwners Owners { get; } = new();
        public Sources Sources { get; } = new();
        public CampaignDenyingCareerPowerAuthorityResolver Authority { get; }
        public OwnerBoundCharacterCareerPowerService Service { get; }
        public Fixture(bool linked = false, string? character = null, Fault? fault = null)
        {
            Owner = linked ? new("owner-A") : OwnerScope.LocalSingleUser;
            Owners.Set(Owner);
            Store = fault is null ? new(Path) : new(Path, fault);
            Authority = new(Sources); Service = new(Store, Owners, Authority);
            var doc = new WorkspaceDocument(character ?? Runner(), "sr5");
            Assert.IsTrue((linked ? Store.CreateWorkspaceDocument(Owner, Id, doc) : Store.CreateWorkspaceDocument(Id, doc)).Success);
            Assert.IsTrue((linked ? Store.SaveCheckpoint(Owner, Id, 1) : Store.SaveCheckpoint(Id, 1)).Success);
        }
        public WorkspaceStoredDocument Saved()
        {
            var cold = new FileWorkspaceStore(Path);
            var read = Owner.IsLocalSingleUser ? cold.Get(Id) : cold.Get(Owner, Id);
            Assert.IsTrue(read.Success, read.Error); return read.Value!;
        }
        public CharacterCareerPowerPlan Review(CharacterCareerPowerCommand c)
        {
            var plan = Service.Review(Owners.Capture(), Id, c);
            Assert.IsNotNull(plan, "The exact source fixture should be admitted."); return plan;
        }
        public CharacterCareerPowerResult Commit(CharacterCareerPowerPlan p) => Service.Commit(Owners.Capture(), p);
        public void Dispose() => Directory.Delete(Path, true);
    }
    // Decorator keeps the real file-backed source path. Explicit knobs isolate
    // policy/source drift and a custom-grant schema; they are never app authority.
    private sealed class Sources : ICharacterSourceDataResolver
    {
        private readonly ICharacterSourceDataResolver inner = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(CoreRoot(), CoreRoot(), null));
        public int Generation;
        public bool Unavailable, SeparateMagic, CustomQuarterGrant;
        public string? Forbidden;
        public Action<XElement>? AmendPower;
        public Action? OnCatalog;
        public int CatalogReads;
        public IReadOnlySet<string>? CatalogNames;
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            var value = Unavailable ? null : inner.TryCreateContext(xml);
            return value is null ? null : new Context(this, value);
        }
        private sealed class Context(Sources source, ICharacterSourceDataContext inner) : ICharacterSourceDataContext
        {
            public bool TryResolveCyberwareGradeDeviceRating(
                string gradeName, string improvementSource, out int deviceRating) =>
                inner.TryResolveCyberwareGradeDeviceRating(gradeName, improvementSource, out deviceRating);
            public bool TryResolveVehicleModBonuses(
                string sourceId, string name, out CharacterVehicleModSourceBonuses bonuses) =>
                inner.TryResolveVehicleModBonuses(sourceId, name, out bonuses);
            public bool TryResolveCareerSettingsProfile(out string xml, out string state)
            {
                if (!inner.TryResolveCareerSettingsProfile(out xml, out state) || source.Unavailable) return false;
                var root = XElement.Parse(xml);
                if (source.SeparateMagic) root.SetElementValue("mysadeptsecondmagattribute", true);
                xml = root.ToString(); state += source.Generation + ":" + xml; return true;
            }
            public bool TryResolveCareerQualityMetatype(out string xml, out string state) =>
                inner.TryResolveCareerQualityMetatype(out xml, out state);
            public bool TryResolveCareerPowerCatalog(out IReadOnlyList<CharacterCareerPowerSource> rows)
            {
                source.CatalogReads++;
                source.OnCatalog?.Invoke();
                if (!inner.TryResolveCareerPowerCatalog(out rows) || source.Unavailable) return false;
                if (source.AmendPower is not null)
                    rows = rows.Select(row => {
                        var xml = XElement.Parse(row.RawSourceXml);
                        source.AmendPower(xml);
                        return row with { RawSourceXml = xml.ToString(), SourceInputsDigest = row.SourceInputsDigest + xml };
                    }).ToArray();
                rows = rows.Where(r => r.SourceId.ToString("D") != source.Forbidden
                    && (source.CatalogNames is null || source.CatalogNames.Contains(r.Name))).ToArray(); return true;
            }
            public bool TryResolveCareerPowerGrantSource(string kind, Guid id, out CharacterCareerPowerGrantSource? row)
            {
                if (!inner.TryResolveCareerPowerGrantSource(kind, id, out row) || row is null) return false;
                if (source.CustomQuarterGrant && kind == "MentorSpirit")
                {
                    var xml = XElement.Parse(row.RawSourceXml);
                    var node = xml.Descendants("specificpower").Single();
                    node.SetElementValue("pointsperlevel", "0.5"); node.SetElementValue("val", 3);
                    row = row with { RawSourceXml = xml.ToString(), AuthorityState = row.AuthorityState + xml };
                }
                return true;
            }
        }
    }
}
