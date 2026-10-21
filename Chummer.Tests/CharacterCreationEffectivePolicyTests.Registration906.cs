using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
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
using Owners906 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;
using Context906 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Registration906_denied_host_entry_keeps_valid_reserved_creation_unwritten()
    {
        WithReservation850(f =>
        {
            var stages = new Stages906();
            var store = new FileWorkspaceStore(f.Directory, stages);
            var sentinel = new CharacterWorkspaceId(Guid.NewGuid().ToString("N"));
            Assert.IsTrue(store.CreateWorkspaceDocument(f.Owner.Owner, sentinel, f.PlainDocument).Success);
            stages.Reset();
            string before = Reservation850Disk(f.Directory);
            var authority = new Admission906(f.Owners, f.Owner, f.Request.Reservation) { Admitted = false };
            Assert.IsTrue(CharacterCreationReservation.IsValid(f.Request.Reservation));
            Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(f.Request.Creation), f.Request.Reservation.RequestDigest);
            Assert.IsNull(f.Service(store).CreateReserved(f.Owner, f.Request, authority).Value);
            Assert.AreEqual(1, authority.Calls, "Entry host check must run under the valid owner lease.");
            Assert.AreEqual(0, stages.Seen.Count, "Entry refusal must precede preparation and record writes.");
            Assert.AreEqual(before, Reservation850Disk(f.Directory));
            Assert.IsFalse(store.Get(f.Owner.Owner, f.Request.Reservation.WorkspaceId).Success);
            Assert.AreEqual(0, f.Owners.ActiveLeases);
            // The identical owner/request/source graph is otherwise creatable.
            authority.Admitted = true;
            Assert.IsNotNull(f.Service(store).CreateReserved(f.Owner, f.Request, authority).Value);
            Assert.AreEqual(6, authority.Calls); // denied entry + five admitted creation checks
            Assert.IsTrue(store.Get(f.Owner.Owner, sentinel).Success);
        });
    }

    [TestMethod]
    public void Registration906_host_revocation_after_flush_fences_rename_and_same_preparation_recovers()
    {
        WithReservation850(f =>
        {
            var sentinel = new CharacterWorkspaceId(Guid.NewGuid().ToString("N"));
            var plain = new FileWorkspaceStore(f.Directory);
            Assert.IsTrue(plain.CreateWorkspaceDocument(f.Owner.Owner, sentinel, f.PlainDocument).Success);
            var sentinelFiles = IdentityFiles809(f.Directory);
            var authority = new Admission906(f.Owners, f.Owner, f.Request.Reservation);
            var stages = new Stages906();
            string? target = null;
            int finalChecks = 0;
            stages.Action = (stage, path, temp) =>
            {
                if (stage == FileWorkspaceStoreFaultStage.AfterCreationReservationPrepared)
                {
                    target = path;
                    Assert.AreEqual(4, authority.Calls, "Entry, store entry, under-lease, and prepare checks must pass.");
                    Assert.IsFalse(File.Exists(path));
                }
                if (stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed) return;
                Assert.AreEqual(target, path);
                Assert.IsTrue(File.Exists(temp));
                Assert.IsFalse(File.Exists(path));
                Assert.AreEqual(4, authority.Calls);
                authority.Admitted = false;
                authority.OnCheck = () => { finalChecks++; Assert.AreEqual(1, f.Owners.ActiveLeases); };
            };
            var store = new FileWorkspaceStore(f.Directory, stages);
            Assert.IsNull(f.Service(store).CreateReserved(f.Owner, f.Request, authority).Value);
            Assert.AreEqual(5, authority.Calls);
            Assert.AreEqual(1, finalChecks, "The callback after the real temp flush must reject the rename.");
            CollectionAssert.AreEqual(new[] { FileWorkspaceStoreFaultStage.AfterCreationReservationPrepared,
                FileWorkspaceStoreFaultStage.AfterTempFileFlushed }, stages.Seen.ToArray());
            Assert.IsNotNull(target);
            Assert.IsFalse(File.Exists(target));
            Assert.IsFalse(new FileWorkspaceStore(f.Directory).Get(f.Owner.Owner, f.Request.Reservation.WorkspaceId).Success);
            AssertIdentityFiles809(f.Directory, sentinelFiles);
            string marker = target + ".creation-reservation";
            var prepared = JsonNode.Parse(File.ReadAllText(marker))!;
            Assert.AreEqual("prepared", prepared["State"]!.GetValue<string>());
            Assert.AreEqual(f.Request.Reservation.ReservationDigest, prepared["ReservationDigest"]!.GetValue<string>());
            Assert.AreEqual(0, Directory.GetFiles(f.Directory, "*.tmp.*", SearchOption.AllDirectories).Length);
            string deniedState = Reservation850Disk(f.Directory);
            authority.OnCheck = null;
            stages.Reset(); stages.Action = null;
            // A restart with unchanged reservation but still-denied admission cannot consume preparation.
            Assert.IsNull(f.Service(new FileWorkspaceStore(f.Directory, stages)).CreateReserved(f.Owner, f.Request, authority).Value);
            Assert.AreEqual(6, authority.Calls);
            Assert.AreEqual(0, stages.Seen.Count);
            Assert.AreEqual(deniedState, Reservation850Disk(f.Directory));
            authority.Admitted = true;
            Assert.IsNotNull(f.Service(new FileWorkspaceStore(f.Directory, stages)).CreateReserved(f.Owner, f.Request, authority).Value);
            Assert.AreEqual(10, authority.Calls, "Prepared retry has no new marker-preparation authority call.");
            CollectionAssert.AreEqual(new[] { FileWorkspaceStoreFaultStage.AfterCreationReservationPrepared,
                FileWorkspaceStoreFaultStage.AfterTempFileFlushed, FileWorkspaceStoreFaultStage.AfterTargetReplaced,
                FileWorkspaceStoreFaultStage.AfterCreationRecordCommitted,
                FileWorkspaceStoreFaultStage.AfterCreationReservationCommitted }, stages.Seen.ToArray());
            var committed = JsonNode.Parse(File.ReadAllText(marker))!;
            Assert.AreEqual("committed", committed["State"]!.GetValue<string>());
            committed["State"] = "prepared";
            Assert.IsTrue(JsonNode.DeepEquals(prepared, committed), "Recovery must consume the exact prepared candidate.");
            var cold = new FileWorkspaceStore(f.Directory);
            var observed = Observe809(new(cold, f.Owners), f.Owner, f.Request.Reservation.WorkspaceId);
            Assert.AreEqual(f.Request.Reservation, observed.CreationReservation);
            foreach (var file in sentinelFiles) CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key));
            string stable = Reservation850Disk(f.Directory);
            Assert.IsNotNull(f.Service(cold).CreateReserved(f.Owner, f.Request, authority).Value);
            Assert.AreEqual(stable, Reservation850Disk(f.Directory));
            Assert.AreEqual(0, f.Owners.ActiveLeases);
        });
    }

    [TestMethod]
    public void Registration906_reserved_source_draft_rebase_replays_cold_without_live_permission()
    {
        WithDraft906(f =>
        {
            var before = f.Store.Get(f.Owner.Owner, f.Id).Value!;
            var oldFiles = IdentityFiles809(f.Directory);
            string recordPath = Directory.GetFiles(f.Directory, f.Id.Value + ".json", SearchOption.AllDirectories).Single();
            var oldManifest = JsonNode.Parse(File.ReadAllText(recordPath))!["RetainedRuleSources"]!;
            var state = Success425(f.Attributes.Load(f.Owner, new(f.Id)));
            var rows = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
                row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
            var preview = Success425(f.Attributes.PreviewRebase(f.Owner, new(state.Binding, rows)));
            AssertRows774(rows, preview.Review.Review.Request.Selections.AttributeAllocations);
            AssertPreserved774(f.First.Command.Review.Review.Request.Selections, preview.Review.Review.Request.Selections);
            Assert.IsTrue(preview.Review.Review.Skills!.Attributes.CreationKarmaBudget.Used
                > f.First.Command.Review.Review.Skills!.Attributes.CreationKarmaBudget.Used);
            AssertIdentityFiles809(f.Directory, oldFiles);
            int guards = 0;
            CharacterCreationAttributesReplacementExecution.ReservationComparisonObserved = (read, captured) =>
            {
                guards++;
                Assert.AreEqual(f.Reservation, captured);
                Assert.AreEqual(captured, read.LocalHistory!.CreationReservation);
                Assert.AreEqual(captured.IncarnationId, read.LocalHistory.IncarnationId);
                Assert.AreEqual(1, f.Owners.ActiveLeases);
            };
            CharacterCreationCompletionDraftReceipt receipt;
            const string key = "registration906-positive-rebase";
            try { receipt = Success425(f.Attributes.ConfirmRebase(f.Owner, preview, key, true)); }
            finally { CharacterCreationAttributesReplacementExecution.ReservationComparisonObserved = null; }
            Assert.AreEqual(2, guards, "Initial TryBuild and the atomic writer's final TryBuild must both reach the fence.");
            var after = f.Store.Get(f.Owner.Owner, f.Id).Value!;
            Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
            Assert.AreEqual(after.ContentRevision, after.SavedRevision);
            Assert.AreEqual(before.Document.Content, after.Document.Content);
            Assert.AreEqual(before.LocalHistory, after.LocalHistory);
            var entries = after.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
            Assert.AreEqual(2, entries.Count);
            CollectionAssert.AreEqual(JsonSerializer.SerializeToUtf8Bytes(before.Document.AuxiliaryState.CharacterCreationCompletionDrafts![0]),
                JsonSerializer.SerializeToUtf8Bytes(entries[0]));
            Assert.AreEqual(receipt, entries[1].Receipt);
            AssertRows774(rows, entries[1].Baseline.Review.Review.Request.Selections.AttributeAllocations);
            Assert.IsNull(after.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
            foreach (var file in oldFiles.Where(pair => pair.Key.EndsWith(".bin", StringComparison.Ordinal)))
                CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key));
            var manifest = JsonNode.Parse(File.ReadAllText(recordPath))!["RetainedRuleSources"]!;
            foreach (var reference in oldManifest["References"]!.AsArray())
                Assert.IsTrue(manifest["References"]!.AsArray().Any(row => JsonNode.DeepEquals(reference, row)));
            foreach (var campaign in oldManifest["CommittedDraftCampaigns"]!.AsArray())
                Assert.IsTrue(manifest["CommittedDraftCampaigns"]!.AsArray().Any(row => JsonNode.DeepEquals(campaign, row)));
            // Remove live inputs, not the store's retained source artifacts.
            Directory.Delete(Path.Combine(f.Context.Directory, "source"), recursive: true);
            f.Campaigns.AfterObservation841 = () => Assert.Fail("Cold receipt replay must not request live campaign permission.");
            var stable = IdentityFiles809(f.Directory);
            string stableDisk = Reservation850Disk(f.Directory);
            var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(f.Directory, f.Context.Queries);
            ObserveDraftReplays841(count =>
            {
                var read = cold.Get(f.Owner.Owner, f.Id);
                Assert.IsTrue(read.Success, read.Error);
                Assert.IsTrue(count() > 0, "The cold store must replay real retained Draft history.");
                CollectionAssert.AreEqual(JsonSerializer.SerializeToUtf8Bytes(entries),
                    JsonSerializer.SerializeToUtf8Bytes(read.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts));
                Assert.AreEqual(f.Reservation, read.Value.LocalHistory!.CreationReservation);
                return read;
            });
            var service = new OwnerBoundCharacterCreationAttributesService(cold, f.Owners, f.Context.Resolver,
                new(f.Context.Queries, f.Campaigns));
            Assert.AreEqual(receipt, Success425(service.ConfirmRebase(f.Owner, preview, key, true)));
            Assert.AreEqual(receipt, Success425(DraftService550(f.Context, cold, f.Owners).LookupDraft(f.Owner, preview.ReceiptLookup(key))));
            Assert.IsNull(service.ConfirmRebase(f.Owner, preview, key + "-fresh", true).Value);
            // A real recorded witness and valid current owner lease still cannot make replay live.
            var entry = entries[1];
            Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(entry.Baseline.SelectedReplay, out _, out var witness));
            Assert.IsTrue(f.Owners.TryAcquire(f.Owner, out var lease));
            using (lease)
            {
                Assert.IsTrue(CharacterCreationAttributesReplacementExecution.TryEnterReplay(before, entry.Baseline.Review, witness!, out var replay));
                using (replay)
                    Assert.IsFalse(CharacterCreationAttributesReplacementExecution.AuthorizesCommit(f.Owner.Owner, before, preview.Command(key + "-fresh", true)));
            }
            AssertIdentityFiles809(f.Directory, stable);
            Assert.AreEqual(stableDisk, Reservation850Disk(f.Directory));
            Assert.AreEqual(0, f.Owners.ActiveLeases);
        });
    }

    [TestMethod]
    [DataRow("remove")]
    [DataRow("reseal")]
    public void Registration906_reserved_rebase_commit_read_identity_drift_reaches_reservation_guard(string change)
    {
        WithDraft906(f =>
        {
            var original = f.Store.Get(f.Owner.Owner, f.Id).Value!;
            var state = Success425(f.Attributes.Load(f.Owner, new(f.Id)));
            var rows = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
                row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
            var preview = Success425(f.Attributes.PreviewRebase(f.Owner, new(state.Binding, rows)));
            const string key = "registration906-refused-rebase";
            var command = preview.Command(key, true);
            Assert.IsTrue(CharacterCreationCompletionDraftTransaction.IsConfirmed(command));
            var injector = new Stages906();
            var store = FileWorkspaceStore.CreateWithRetainedRuleSources(f.Directory, f.Context.Queries, injector);
            var service = new OwnerBoundCharacterCreationAttributesService(store, f.Owners, f.Context.Resolver,
                new(f.Context.Queries, f.Campaigns));
            byte[]? originalBytes = null;
            DateTime originalTime = default;
            string? recordPath = null;
            string? afterInjection = null;
            int commitReads = 0, guards = 0;
            injector.Action = (stage, path, _) =>
            {
                if (stage != FileWorkspaceStoreFaultStage.BeforeCompletionDraftRead) return;
                Assert.AreEqual(0, commitReads++);
                Assert.AreEqual(1, f.Owners.ActiveLeases);
                recordPath = path; originalBytes = File.ReadAllBytes(path); originalTime = File.GetLastWriteTimeUtc(path);
                var record = JsonNode.Parse(originalBytes)!;
                var history = record["LocalHistory"]!.Deserialize<WorkspaceLocalHistory>()!;
                Assert.AreEqual(f.Reservation, history.CreationReservation);
                CharacterCreationReservation? changed = null;
                if (change == "reseal")
                {
                    changed = f.Reservation with { InstallationId = "registration906-other-install", ReservationDigest = "" };
                    changed = changed with { ReservationDigest = CharacterCreationFinalizationDigest.Compute(changed) };
                    Assert.IsTrue(CharacterCreationReservation.IsValid(changed));
                }
                history = history with { CreationReservation = changed };
                Assert.IsTrue(history.IsValid(original.ContentRevision), "Do not substitute malformed local history for the reservation guard.");
                record["LocalHistory"] = JsonSerializer.SerializeToNode(history);
                File.WriteAllText(path, record.ToJsonString());
                File.SetLastWriteTimeUtc(path, originalTime);
                // Only this explicit injected field changes; documents, receipts, manifests, owner and sources stay exact.
                var baseline = JsonNode.Parse(originalBytes)!;
                baseline["LocalHistory"] = record["LocalHistory"]!.DeepClone();
                Assert.IsTrue(JsonNode.DeepEquals(baseline, record));
                afterInjection = DurableDisk906(f.Directory);
            };
            CharacterCreationAttributesReplacementExecution.ReservationComparisonObserved = (read, captured) =>
            {
                guards++;
                Assert.AreEqual(1, commitReads, "Observe the real store read, not a preflight synthetic envelope.");
                Assert.AreEqual(f.Reservation, captured);
                Assert.AreNotEqual(captured, read.LocalHistory!.CreationReservation);
                Assert.AreEqual(captured.IncarnationId, read.LocalHistory.IncarnationId);
                Assert.AreEqual(original.ContentRevision, read.ContentRevision);
                Assert.AreEqual(original.SavedRevision, read.SavedRevision);
                CollectionAssert.AreEqual(JsonSerializer.SerializeToUtf8Bytes(original.Document),
                    JsonSerializer.SerializeToUtf8Bytes(read.Document));
                Assert.AreEqual(f.Owner, f.Owners.Capture());
                Assert.AreEqual(1, f.Owners.ActiveLeases);
            };
            try
            {
                var refused = service.ConfirmRebase(f.Owner, preview, key, true);
                Assert.IsNull(refused.Value);
                CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.SnapshotConflict }, refused.Blockers.ToArray());
                Assert.AreEqual(1, commitReads);
                Assert.AreEqual(1, guards, "Earlier history/source/campaign/request/owner refusal must not satisfy this test.");
                CollectionAssert.AreEqual(new[] { FileWorkspaceStoreFaultStage.BeforeCompletionDraftRead }, injector.Seen.ToArray());
                Assert.IsNotNull(afterInjection);
                Assert.AreEqual(afterInjection, DurableDisk906(f.Directory), "Refusal must not write records, receipts, private history, source artifacts or markers.");
            }
            finally
            {
                CharacterCreationAttributesReplacementExecution.ReservationComparisonObserved = null;
                injector.Action = null;
                if (recordPath is not null && originalBytes is not null)
                { File.WriteAllBytes(recordPath, originalBytes); File.SetLastWriteTimeUtc(recordPath, originalTime); }
            }
            // Restore only the fixture-injected drift. The exact request and current graph must now succeed.
            Assert.IsNotNull(Success425(f.Attributes.ConfirmRebase(f.Owner, preview, key, true)));
            Assert.AreEqual(0, f.Owners.ActiveLeases);
        });
    }

    private sealed class Admission906(Owners906 owners, OwnerContextStamp owner, CharacterCreationReservation reservation)
        : ICharacterCreationReservationAuthority
    {
        internal bool Admitted = true;
        internal int Calls;
        internal Action? OnCheck;
        public bool IsCurrent(OwnerContextStamp candidate, CharacterCreationReservation binding)
        {
            Calls++;
            Assert.AreEqual(owner, candidate);
            Assert.AreEqual(reservation, binding);
            Assert.AreEqual(owner, owners.Capture());
            Assert.AreEqual(1, owners.ActiveLeases);
            OnCheck?.Invoke();
            return Admitted;
        }
    }

    private sealed class Stages906 : IFileWorkspaceStoreFaultInjector
    {
        internal readonly List<FileWorkspaceStoreFaultStage> Seen = [];
        internal Action<FileWorkspaceStoreFaultStage, string, string>? Action;
        internal void Reset() => Seen.Clear();
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        { Seen.Add(stage); Action?.Invoke(stage, targetPath, tempPath); }
    }

    // The operation owns its .lock file with FileShare.None. Include every durable
    // record/source/marker/temp byte and timestamp, without reopening that coordination handle.
    private static string DurableDisk906(string directory) => JsonSerializer.Serialize(
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".lock", StringComparison.Ordinal)).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new { Path = Path.GetRelativePath(directory, path),
                Bytes = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
                Modified = File.GetLastWriteTimeUtc(path) }).ToArray());

    private sealed record Draft906(Context906 Context, string Directory, FileWorkspaceStore Store, Owners906 Owners,
        OwnerContextStamp Owner, CharacterWorkspaceId Id, CharacterCreationReservation Reservation,
        PreparedDraft550 First, IdentityFactory809 Campaigns, OwnerBoundCharacterCreationAttributesService Attributes);

    private static void WithDraft906(Action<Draft906> action)
    {
        // PricingContext supplies real source files and services only. No document or prepared
        // graph is copied from its separate workspace into this reserved runner.
        using var context = PricingContext747(savedMagic: false);
        string directory = Path.Combine(context.Directory, "registration906-reserved");
        var owners = new Owners906();
        owners.Set(new OwnerScope("registration906-owner"));
        var owner = owners.Capture();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(directory, context.Queries);
        var codec = new Sr5WorkspaceCodec(context.Queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(context.Resolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(
            new(store, new RulesetWorkspaceCodecResolver([codec]), context.Queries, context.Resolver), owners);
        var creation = new CharacterCreationBootstrapRequest(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, "sr5", "Reserved draft runner", "Reserved",
            CharacterCreationBuildMethods.Priority, CharacterCreationBootstrapProfiles.PrioritySettingsProfileId);
        var reservation = new CharacterCreationReservation(CharacterCreationReservation.SchemaV1, Guid.NewGuid().ToString("N"),
            owner.Owner.NormalizedValue, "registration906-install", new(Guid.NewGuid().ToString("N")), Guid.NewGuid().ToString("N"),
            CharacterCreationFinalizationDigest.Compute(creation), Hash518("registration906-admission"), "");
        reservation = reservation with { ReservationDigest = CharacterCreationFinalizationDigest.Compute(reservation) };
        var created = bootstrap.CreateReserved(owner, new(CharacterCreationReservedBootstrapRequest.SchemaV1, creation, reservation),
            new Admission906(owners, owner, reservation));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome, string.Join(",", created.Blockers));
        Assert.AreEqual(reservation.WorkspaceId, created.Value!.WorkspaceId);
        var id = reservation.WorkspaceId;
        // Same genuine prerequisite choices as CreateLinkedPrerequisite, applied to this reserved id.
        var prerequisites = new OwnerBoundCharacterCreationPrerequisiteService(store, owners, context.Queries, context.Resolver);
        var state = Success425(prerequisites.Load(owner, new(id)));
        var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
        var heritage = state.Authority.Options.Single(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && row.Rank == "A").HeritageOptions.First(row => row.IsEnabled && row.MetatypeName == "Human" && row.MetavariantSourceId is null);
        var talent = state.Authority.Options.Single(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && row.Rank == "E").TalentOptions.First(row => row.IsEnabled && row.Value.Equals("Mundane", StringComparison.OrdinalIgnoreCase));
        var prerequisitePreview = Success425(prerequisites.Preview(owner, new(state.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }));
        Success425(prerequisites.Confirm(owner, new(prerequisitePreview.Binding, ranks, prerequisitePreview.PreviewDigest, true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }));
        var first = PrepareDraft550(context, store, owners, workspaceId: id);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owner, first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var observed = Observe809(new(store, owners), owner, id);
        Assert.AreEqual(reservation, observed.CreationReservation);
        Assert.IsNotNull(observed.CompletionDraftIncludesLoadout);
        var campaigns = new IdentityFactory809(store, owners, observed);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver, new(context.Queries, campaigns));
        action(new(context, directory, store, owners, owner, id, reservation, first, campaigns, attributes));
    }
}
