using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Origin_availability_explains_real_budget_exclusions_without_admitting_or_writing_them(bool linked)
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("origin-availability");
            var store = new FileWorkspaceStore(directory);
            var owner = new AvailabilityOwner(linked ? new OwnerScope("availability-account-a") : OwnerScope.LocalSingleUser);
            var scope = owner.Current;
            var stamp = owner.Capture();
            var document = new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5);
            Assert.IsTrue((scope.IsLocalSingleUser ? store.CreateWorkspaceDocument(id, document)
                : store.CreateWorkspaceDocument(scope, id, document)).Success);
            Chummer.Application.Workspaces.WorkspaceStoredDocument Read()
                => (scope.IsLocalSingleUser ? store.Get(id) : store.Get(scope, id)).Value!;
            OwnerBoundLifeModuleOriginService Service(FileWorkspaceStore current) => new(current, owner,
                new XmlCharacterFileQueries(new CharacterFileService()),
                new FileSystemCharacterSourceDataResolver(CreateOverlays()), CreateCatalog());
            var service = Service(store);
            var start = service.Start(stamp, id.Value);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, start.Outcome, string.Join(",", start.Blockers));
            var checkpoint = start.Value!;
            LifeModuleDecisionAvailabilitySnapshot? snapshot = null;
            for (int decision = 0; decision < 14; decision++)
            {
                var turn = checkpoint.Projection.CurrentTurn;
                var read = Read();
                if (decision > 0)
                {
                    string before = JsonSerializer.Serialize(read);
                    var inspected = service.LoadAvailability(stamp, new(id.Value, read.ContentRevision,
                        read.SavedRevision, turn.TurnId, turn.DecisionDigest));
                    Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, inspected.Outcome, string.Join(",", inspected.Blockers));
                    Assert.AreEqual(before, JsonSerializer.Serialize(Read()));
                    snapshot = inspected.Value!;
                    if (snapshot.Options.Any(option => option.Availability == LifeModuleOptionAvailabilityStates.BudgetExcluded)) break;
                }
                var choice = turn.LegalChoices.Where(item => item.FollowUps is null or { Count: 0 })
                    .OrderByDescending(item => item.MechanicsPreview.KarmaCost).First();
                var prepared = service.Prepare(stamp, checkpoint, choice.ChoiceId);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, prepared.Outcome, string.Join(",", prepared.Blockers));
                var accepted = service.Confirm(stamp, prepared.Value!, prepared.Value!.PendingPreview!.PreviewDigest,
                    $"availability-stage-{decision}", true);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, accepted.Outcome, string.Join(",", accepted.Blockers));
                checkpoint = accepted.Value!.Checkpoint;
            }
            Assert.IsNotNull(snapshot);
            var excluded = snapshot.Options.FirstOrDefault(option => option.Availability == LifeModuleOptionAvailabilityStates.BudgetExcluded);
            Assert.IsNotNull(excluded, "The real catalog/budget must demonstrate a blocked option, not a synthetic caption.");
            var current = checkpoint.Projection.CurrentTurn;
            Assert.IsFalse(current.LegalChoices.Any(choice => choice.ChoiceId == excluded.ChoiceId));
            Assert.IsTrue(snapshot.Options.Where(option => option.Availability == LifeModuleOptionAvailabilityStates.Available)
                .All(option => current.LegalChoices.Any(choice => choice.ChoiceId == option.ChoiceId)));
            Assert.IsTrue(excluded.SourceAnchorIds.Count > 0);
            Assert.AreEqual(current.SourceDigest, snapshot.SourceDigest);
            string stable = JsonSerializer.Serialize(Read());
            string checkpointBytes = JsonSerializer.Serialize(checkpoint);
            Assert.AreNotEqual(LifeModuleOriginDossierOutcomes.Success,
                service.Prepare(stamp, checkpoint, excluded.ChoiceId).Outcome,
                "An explanation is never an additional selectable module.");
            var reopened = Service(new FileWorkspaceStore(directory));
            Assert.AreEqual(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(
                reopened.LoadAvailability(stamp, snapshot.Binding).Value));
            Assert.AreEqual(checkpointBytes, JsonSerializer.Serialize(reopened.Restore(stamp, checkpoint).Value));
            foreach (var stale in new[] {
                snapshot.Binding with { WorkspaceRevision = snapshot.Binding.WorkspaceRevision + 1 },
                snapshot.Binding with { SavedRevision = snapshot.Binding.SavedRevision + 1 },
                snapshot.Binding with { TurnId = "old-turn" },
                snapshot.Binding with { DecisionDigest = new string('0', 64) },
                snapshot.Binding with { WorkspaceId = "another-workspace" } })
                Assert.IsNull(reopened.LoadAvailability(stamp, stale).Value);
            owner.Transition(new OwnerScope("availability-account-b"));
            Assert.IsNull(reopened.LoadAvailability(stamp, snapshot.Binding).Value);
            Assert.IsNull(reopened.LoadAvailability(owner.Capture(), snapshot.Binding).Value);
            owner.Transition(scope);
            Assert.IsNull(reopened.LoadAvailability(stamp, snapshot.Binding).Value, "A→B→A does not restore an old admission.");
            Assert.AreEqual(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(
                reopened.LoadAvailability(owner.Capture(), snapshot.Binding).Value));
            Assert.AreEqual(stable, JsonSerializer.Serialize(Read()));
            Assert.AreEqual(0, owner.ActiveLeases);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class AvailabilityOwner(OwnerScope initial) : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _issuer = Guid.NewGuid().ToString("N");
        private OwnerScope _scope = initial;
        private long _revision;
        public int ActiveLeases { get; private set; }
        public OwnerScope Current { get { lock (_gate) return _scope; } }
        public OwnerContextStamp Capture() { lock (_gate) return new(_scope, _issuer, _revision); }
        public void Transition(OwnerScope next)
        { lock (_gate) { Assert.AreEqual(0, ActiveLeases); _scope = next; _revision++; } }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            if (expected != new OwnerContextStamp(_scope, _issuer, _revision))
            { lease = null; Monitor.Exit(_gate); return false; }
            Assert.AreEqual(0, ActiveLeases);
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }
        private sealed class Lease(AvailabilityOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true; owner.ActiveLeases--; Monitor.Exit(owner._gate);
            }
        }
    }
}
