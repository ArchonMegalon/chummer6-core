using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Owners834 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow("restart")]
    [DataRow("lost-commit-ack")]
    [DataRow("deleted")]
    [DataRow("collision")]
    [DataRow("owner-aba")]
    [DataRow("foreign-owner")]
    [DataRow("changed-request")]
    public void Registration834_atomic_bootstrap_replay_and_negative_boundaries(string scenario)
    {
        using var context = PricingContext747(savedMagic: false);
        string directory = Path.Combine(context.Directory, "registration834");
        var owners = new Owners834();
        owners.Set(new OwnerScope("registration834-owner"));
        var stamp = owners.Capture();
        var creation = new CharacterCreationBootstrapRequest(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, "sr5", "Reserved runner", "Reserved",
            CharacterCreationBuildMethods.Priority, CharacterCreationBootstrapProfiles.PrioritySettingsProfileId);
        // Explicit test host admission; public checksum validation is not issuance.
        var reservation = new CharacterCreationReservation(CharacterCreationReservation.SchemaV1,
            Guid.NewGuid().ToString("N"), stamp.Owner.NormalizedValue, "registration834-install",
            new(Guid.NewGuid().ToString("N")), Guid.NewGuid().ToString("N"),
            CharacterCreationFinalizationDigest.Compute(creation), CharacterCreationFinalizationDigest.ComputeUtf8("server-transition"), "");
        reservation = reservation with { ReservationDigest = CharacterCreationFinalizationDigest.Compute(reservation) };
        var request = new CharacterCreationReservedBootstrapRequest(CharacterCreationReservedBootstrapRequest.SchemaV1, creation, reservation);
        var admission = new Reservation834Admission(stamp, reservation, owners);
        var store = scenario == "lost-commit-ack" ? new FileWorkspaceStore(directory, new Reservation834LostAck()) : new FileWorkspaceStore(directory);
        OwnerBoundCharacterCreationBootstrapService Service(FileWorkspaceStore selected)
        {
            var codec = new Sr5WorkspaceCodec(context.Queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(context.Resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            return new(new(selected, new RulesetWorkspaceCodecResolver([codec]), context.Queries, context.Resolver), owners);
        }
        if (scenario == "collision")
        {
            var source = context.Store.Get(context.WorkspaceId).Value!.Document;
            var plain = new Chummer.Contracts.Workspaces.WorkspaceDocument(source.Content, source.RulesetId, source.Format);
            Assert.IsTrue(store.CreateWorkspaceDocument(stamp.Owner, reservation.WorkspaceId, plain).Success);
            Assert.IsNull(Service(store).CreateReserved(stamp, request, admission).Value);
            Assert.IsNull(new WorkspaceLocalIdentityObservationService(store, owners).Observe(stamp, reservation.WorkspaceId).Value?.CreationReservation);
            return;
        }
        if (scenario is "owner-aba" or "foreign-owner")
        {
            owners.Set(new OwnerScope("different-owner"));
            if (scenario == "owner-aba") owners.Set(stamp.Owner);
            Assert.IsNull(Service(store).CreateReserved(stamp, request, admission).Value);
            Assert.AreEqual(0, store.List(stamp.Owner).Count);
            return;
        }
        if (scenario == "changed-request")
        {
            Assert.IsNull(Service(store).CreateReserved(stamp, request with { Creation = creation with { Name = "Changed" } }, admission).Value);
            return;
        }
        var created = Service(store).CreateReserved(stamp, request, admission);
        if (scenario != "lost-commit-ack") Assert.IsNotNull(created.Value);
        var cold = new FileWorkspaceStore(directory);
        var identity = new WorkspaceLocalIdentityObservationService(cold, owners).Observe(stamp, reservation.WorkspaceId);
        Assert.IsTrue(identity.Success);
        Assert.AreEqual(reservation, identity.Value!.CreationReservation);
        Assert.IsNull(identity.Value.UnimportedLocalHistoryDigest);
        if (scenario == "deleted")
        {
            Assert.IsTrue(cold.Delete(stamp.Owner, reservation.WorkspaceId, identity.Value.ContentRevision).Success);
            Assert.IsNull(Service(cold).CreateReserved(stamp, request, admission).Value);
            Assert.IsFalse(cold.Get(stamp.Owner, reservation.WorkspaceId).Success);
            return;
        }
        var replay = Service(cold).CreateReserved(stamp, request, admission);
        Assert.IsNotNull(replay.Value);
        Assert.AreEqual(reservation.WorkspaceId, replay.Value.WorkspaceId);
        Assert.AreEqual(1, cold.List(stamp.Owner).Count);
        Assert.AreEqual(identity.Value, new WorkspaceLocalIdentityObservationService(cold, owners).Observe(stamp, reservation.WorkspaceId).Value);
    }

    [TestMethod]
    public void Registration834_historical_json_and_import_restore_adoption_history_do_not_gain_registration()
    {
        var history = new WorkspaceLocalHistory(Guid.NewGuid().ToString("N"), 0, null);
        Assert.IsFalse(JsonSerializer.Serialize(history).Contains("CreationReservation", StringComparison.Ordinal));
        var imported = new WorkspaceLocalHistory(Guid.NewGuid().ToString("N"), 1, new string('a', 64));
        Assert.IsNull(imported.CreationReservation);
        // Explicitly prevent any retained reservation from surviving adoption or
        // imported history, even if a tampered local JSON record carries it.
        var creation = new CharacterCreationBootstrapRequest(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, "sr5", "Original", "", CharacterCreationBuildMethods.Priority,
            CharacterCreationBootstrapProfiles.PrioritySettingsProfileId);
        var r = new CharacterCreationReservation(CharacterCreationReservation.SchemaV1, Guid.NewGuid().ToString("N"),
            "owner", "installation", new(Guid.NewGuid().ToString("N")), history.IncarnationId,
            CharacterCreationFinalizationDigest.Compute(creation), CharacterCreationFinalizationDigest.ComputeUtf8("authority"), "");
        r = r with { ReservationDigest = CharacterCreationFinalizationDigest.Compute(r) };
        Assert.IsTrue((history with { CreationReservation = r }).IsValid(1));
        Assert.IsFalse((history with { CreationReservation = r, ImportedThroughRevision = 1, ImportedSnapshotDigest = new string('a', 64) }).IsValid(1));
        var adoption = new WorkspaceLocalAdoptionReceipt(Guid.NewGuid(), "owner", r.WorkspaceId, r.IncarnationId,
            new string('b', 64), new string('c', 64), 1, 1, DateTimeOffset.UtcNow);
        Assert.IsFalse((history with { CreationReservation = r, LocalAdoption = adoption }).IsValid(1));
    }

    private sealed class Reservation834Admission(OwnerContextStamp owner, CharacterCreationReservation reservation, Owners834 owners)
        : ICharacterCreationReservationAuthority
    {
        public bool IsCurrent(OwnerContextStamp candidate, CharacterCreationReservation binding)
            => candidate == owner && binding == reservation && owners.Capture() == owner;
    }
    private sealed class Reservation834LostAck : IFileWorkspaceStoreFaultInjector
    {
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        { if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) throw new IOException("Test lost acknowledgement"); }
    }
    [TestMethod]
    [DataRow("AfterCreationReservationPrepared")]
    [DataRow("AfterTempFileFlushed")]
    [DataRow("AfterTargetReplaced")]
    [DataRow("AfterCreationRecordCommitted")]
    [DataRow("AfterCreationReservationCommitted")]
    public void Reservation850_real_store_cold_recovers_each_prepare_commit_boundary(string stage)
    {
        WithReservation850(f =>
        {
            var interrupted = new FileWorkspaceStore(f.Directory, new Reservation850Fault(stage));
            var first = f.Create(interrupted);
            bool committed = stage is "AfterTargetReplaced" or "AfterCreationRecordCommitted" or "AfterCreationReservationCommitted";
            var cold = new FileWorkspaceStore(f.Directory);
            Assert.AreEqual(committed, cold.Get(f.Owner.Owner, f.Request.Reservation.WorkspaceId).Success);
            // AfterTargetReplaced is deliberately swallowed by the existing writer.
            // Discard its successful receipt to model successful-but-response-lost.
            if (stage == "AfterTargetReplaced") Assert.IsNotNull(first.Value);
            else Assert.IsNull(first.Value);
            var replay = f.Create(cold);
            Assert.IsNotNull(replay.Value);
            Assert.AreEqual(f.Request.Reservation.WorkspaceId, replay.Value.WorkspaceId);
            var observed = new WorkspaceLocalIdentityObservationService(cold, f.Owners).Observe(f.Owner, replay.Value.WorkspaceId);
            Assert.IsTrue(observed.Success);
            Assert.AreEqual(f.Request.Reservation, observed.Value!.CreationReservation);
            Assert.AreEqual(1, cold.List(f.Owner.Owner).Count);
            string before = Reservation850Disk(f.Directory);
            Assert.IsNotNull(f.Create(new FileWorkspaceStore(f.Directory)).Value);
            Assert.AreEqual(before, Reservation850Disk(f.Directory), "Completed replay rewrote durable state.");
        });
    }

    [TestMethod]
    [DataRow("AfterCreationRecordCommitted")]
    [DataRow("AfterCreationReservationCommitted")]
    public void Reservation850_delete_after_commit_cannot_resurrect_even_with_prepared_marker(string stage)
    {
        WithReservation850(f =>
        {
            Assert.IsNull(f.Create(new FileWorkspaceStore(f.Directory, new Reservation850Fault(stage))).Value);
            var cold = new FileWorkspaceStore(f.Directory);
            var id = f.Request.Reservation.WorkspaceId;
            Assert.IsTrue(cold.Delete(f.Owner.Owner, id, 1).Success);
            string before = Reservation850Disk(f.Directory);
            Assert.IsNull(f.Create(new FileWorkspaceStore(f.Directory)).Value);
            Assert.AreEqual(before, Reservation850Disk(f.Directory));
            Assert.IsFalse(cold.Get(f.Owner.Owner, id).Success);
        });
    }

    [TestMethod]
    [DataRow("request")]
    [DataRow("incarnation")]
    [DataRow("installation")]
    [DataRow("owner")]
    [DataRow("owner-aba")]
    [DataRow("import")]
    [DataRow("import-delete")]
    [DataRow("adoption")]
    [DataRow("adoption-delete")]
    [DataRow("corrupt-marker")]
    [DataRow("partial-marker")]
    [DataRow("legacy-consumed")]
    [DataRow("record-digest")]
    public void Reservation850_prepared_refusals_preserve_all_durable_bytes(string scenario)
    {
        WithReservation850(f =>
        {
            Assert.IsNull(f.Create(new FileWorkspaceStore(f.Directory,
                new Reservation850Fault("AfterCreationReservationPrepared"))).Value);
            var cold = new FileWorkspaceStore(f.Directory);
            var id = f.Request.Reservation.WorkspaceId;
            var candidate = f.Request;
            if (scenario == "request") candidate = candidate with { Creation = candidate.Creation with { Name = "different" } };
            if (scenario is "incarnation" or "installation")
            {
                var r = scenario == "incarnation" ? candidate.Reservation with { IncarnationId = Guid.NewGuid().ToString("N"), ReservationDigest = "" }
                    : candidate.Reservation with { InstallationId = "different-install", ReservationDigest = "" };
                candidate = candidate with { Reservation = r with { ReservationDigest = CharacterCreationFinalizationDigest.Compute(r) } };
            }
            if (scenario is "owner" or "owner-aba")
            {
                f.Owners.Set(new OwnerScope("other-owner"));
                if (scenario == "owner-aba") f.Owners.Set(f.Owner.Owner);
            }
            if (scenario is "import" or "import-delete")
            {
                Assert.IsTrue(cold.CreateWorkspaceDocument(f.Owner.Owner, id, f.PlainDocument).Success);
                if (scenario == "import-delete") Assert.IsTrue(cold.Delete(f.Owner.Owner, id, 1).Success);
            }
            if (scenario is "adoption" or "adoption-delete")
            {
                Assert.IsTrue(cold.CreateWorkspaceDocument(id, f.PlainDocument).Success);
                var adoption = new WorkspaceLocalAdoptionService(cold, f.Owners);
                var review = adoption.Review(f.Owner, id);
                Assert.IsNotNull(review);
                Assert.AreEqual(WorkspaceLocalAdoptionOutcome.Applied,
                    adoption.Confirm(f.Owner, review, true).Outcome);
                if (scenario == "adoption-delete") Assert.IsTrue(cold.Delete(f.Owner.Owner, id, 1).Success);
            }
            string marker = Directory.GetFiles(f.Directory, "*.creation-reservation", SearchOption.AllDirectories).Single();
            if (scenario == "corrupt-marker") File.WriteAllText(marker, "{}");
            if (scenario == "partial-marker") File.WriteAllText(marker, "{\"Schema\":");
            if (scenario == "legacy-consumed") File.WriteAllText(marker, candidate.Reservation.ReservationDigest);
            if (scenario == "record-digest")
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(marker))!;
                json["RecordDigest"] = CharacterCreationFinalizationDigest.ComputeUtf8("different record");
                File.WriteAllText(marker, json.ToJsonString());
            }
            string before = Reservation850Disk(f.Directory);
            var denied = f.Service(cold).CreateReserved(f.Owner, candidate,
                new Reservation834Admission(f.Owner, candidate.Reservation, f.Owners));
            Assert.IsNull(denied.Value);
            Assert.AreEqual(before, Reservation850Disk(f.Directory), "Refusal changed file bytes/names/timestamps.");
        });
    }

    [TestMethod]
    public void Reservation850_partial_prepare_temporary_is_not_a_consumed_operation()
    {
        WithReservation850(f =>
        {
            // Force the ordinary writer's pre-rename failure, then represent a process
            // exit during the first marker temporary write (no durable marker yet).
            Assert.IsNull(f.Create(new FileWorkspaceStore(f.Directory,
                new Reservation850Fault("AfterCreationReservationPrepared"))).Value);
            string marker = Directory.GetFiles(f.Directory, "*.creation-reservation", SearchOption.AllDirectories).Single();
            File.Move(marker, marker + ".tmp." + Guid.NewGuid().ToString("N"));
            Assert.IsNotNull(f.Create(new FileWorkspaceStore(f.Directory)).Value);
        });
    }

    private static string Reservation850Disk(string directory) => JsonSerializer.Serialize(
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new { Path = Path.GetRelativePath(directory, p), Bytes = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))),
                Modified = File.GetLastWriteTimeUtc(p) }).ToArray());

    private sealed record Reservation850Fixture(string Directory, Owners834 Owners, OwnerContextStamp Owner,
        CharacterCreationReservedBootstrapRequest Request,
        Func<FileWorkspaceStore, OwnerBoundCharacterCreationBootstrapService> Service,
        Chummer.Contracts.Workspaces.WorkspaceDocument PlainDocument)
    {
        internal CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> Create(FileWorkspaceStore store) =>
            Service(store).CreateReserved(Owner, Request, new Reservation834Admission(Owner, Request.Reservation, Owners));
    }

    private static void WithReservation850(Action<Reservation850Fixture> action)
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners834();
        owners.Set(new OwnerScope("reservation850-owner"));
        var owner = owners.Capture();
        var creation = new CharacterCreationBootstrapRequest(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, "sr5", "Cold runner", "Cold alias",
            CharacterCreationBuildMethods.Priority, CharacterCreationBootstrapProfiles.PrioritySettingsProfileId);
        var r = new CharacterCreationReservation(CharacterCreationReservation.SchemaV1, Guid.NewGuid().ToString("N"),
            owner.Owner.NormalizedValue, "reservation850-install", new(Guid.NewGuid().ToString("N")), Guid.NewGuid().ToString("N"),
            CharacterCreationFinalizationDigest.Compute(creation), CharacterCreationFinalizationDigest.ComputeUtf8("admitted test authority"), "");
        r = r with { ReservationDigest = CharacterCreationFinalizationDigest.Compute(r) };
        OwnerBoundCharacterCreationBootstrapService Service(FileWorkspaceStore store)
        {
            var codec = new Sr5WorkspaceCodec(context.Queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(context.Resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            return new(new(store, new RulesetWorkspaceCodecResolver([codec]), context.Queries, context.Resolver), owners);
        }
        action(new(Path.Combine(context.Directory, "reservation850"), owners, owner,
            new(CharacterCreationReservedBootstrapRequest.SchemaV1, creation, r), Service,
            new("<character><name>Other runner</name><metatype>Human</metatype><created>False</created></character>", "sr5")));
    }

    private sealed class Reservation850Fault(string selected) : IFileWorkspaceStoreFaultInjector
    {
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        { if (stage.ToString() == selected) throw new IOException("Injected creation interruption"); }
    }

}
