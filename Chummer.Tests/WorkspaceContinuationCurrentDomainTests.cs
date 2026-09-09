using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
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
public sealed class WorkspaceContinuationCurrentDomainTests
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly Guid ContactId = Guid.Parse("87796157-0366-4154-836a-034326e8e924");
    private static readonly Guid LifestyleId = Guid.Parse("11111111-2222-4333-8444-555555555555");

    [TestMethod]
    [DataRow("contacts")]
    [DataRow("lifestyles")]
    public void Actual_typed_current_domain_decision_and_receipt_pass_readonly_evaluation(string domain)
    {
        using DomainContext context = new(domain);
        WorkspaceContinuationExport exported = context.Export();
        byte[] bytes = WorkspaceContinuationCodec.Encode(exported, MaximumBytes);
        string before = JsonSerializer.Serialize(exported);
        var durable = CaptureFiles(context.Directory);
        CountingResolver sources = new(context.Resolver, context.Owner);

        var result = new WorkspaceContinuationCandidateEvaluator(
                context.Owner, sources, context.Queries, context.LifeModules)
            .Evaluate(context.Owner.Capture(), bytes, MaximumBytes);

        Assert.IsTrue(result.CurrentDraftChecksPassed, Describe(result));
        Assert.IsTrue(result.HistoryConsistent);
        Assert.IsEmpty(result.BoundaryBlockers);
        Assert.HasCount(1, result.DomainChecks);
        var check = result.DomainChecks.Single();
        Assert.AreEqual(domain, check.Domain);
        Assert.IsTrue(check.Evaluated);
        Assert.IsEmpty(check.Blockers);
        CollectionAssert.Contains(check.ReadinessBlockers.ToArray(), PersistenceBlocker(domain));
        Assert.AreEqual(2, sources.Reads, "Only the independent before/after captures may consult live sources.");
        Assert.AreEqual(0, context.Owner.ActiveLeases);
        Assert.IsNotNull(result.Candidate);
        Assert.AreEqual(before, JsonSerializer.Serialize(result.Candidate));
        Assert.AreEqual(before, JsonSerializer.Serialize(exported));
        CollectionAssert.AreEqual(bytes, WorkspaceContinuationCodec.Encode(result.Candidate, MaximumBytes));
        AssertHistoryPresent(domain, result.Candidate.Snapshot.Workspace.Document.AuxiliaryState);
        NoAuthority(result);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    [DataRow("contacts")]
    [DataRow("lifestyles")]
    public void Rehashed_current_choices_over_budget_are_rejected_without_discarding_genuine_receipt_history(string domain)
    {
        using DomainContext context = new(domain);
        var exported = context.Export();
        var workspace = exported.Snapshot.Workspace;
        XDocument xml = XDocument.Parse(workspace.Document.Content, LoadOptions.PreserveWhitespace);
        string blocker;
        if (domain == "contacts")
        {
            XElement contact = xml.Root!.Element("contacts")!.Elements("contact").Single();
            // Each rating is individually legal. Their 12-point sum exceeds
            // the imported character's recorded 10-point pool.
            contact.Element("connection")!.Value = "6";
            contact.Element("loyalty")!.Value = "6";
            blocker = CharacterCreationContactsBlockers.BudgetExceeded;
        }
        else
        {
            XElement lifestyle = xml.Root!.Element("lifestyles")!.Elements("lifestyle").Single();
            int increments = checked((int)decimal.Floor(10_000m / context.LifestyleCostPerIncrement) + 1);
            Assert.IsTrue(increments is > 1 and <= 10_000);
            lifestyle.Element("months")!.Value = increments.ToString(CultureInfo.InvariantCulture);
            blocker = CharacterCreationLifestylesBlockers.InsufficientFunds;
        }
        var changedWorkspace = workspace with
        {
            // A later candidate observation keeps the original receipt as
            // history; it does not rewrite that receipt to claim this decision.
            ContentRevision = workspace.ContentRevision + 1,
            Document = workspace.Document with
            {
                State = workspace.Document.State with { Payload = xml.ToString(SaveOptions.DisableFormatting) }
            }
        };
        var changedSnapshot = exported.Snapshot with { Workspace = changedWorkspace };
        var rehashed = new WorkspaceContinuationExport(changedSnapshot,
            WorkspaceContinuationSnapshotDigest.Compute(changedSnapshot));
        byte[] bytes = WorkspaceContinuationCodec.Encode(rehashed, MaximumBytes);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, MaximumBytes, out _));
        Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(context.Owner.Current, changedSnapshot),
            "The genuine earlier receipt history remains consistent; current choices require separate recomputation.");
        string before = JsonSerializer.Serialize(rehashed);
        var durable = CaptureFiles(context.Directory);

        var result = new WorkspaceContinuationCandidateEvaluator(
                context.Owner, context.Resolver, context.Queries, context.LifeModules)
            .Evaluate(context.Owner.Capture(), bytes, MaximumBytes);

        Assert.IsFalse(result.CurrentDraftChecksPassed, Describe(result));
        Assert.IsTrue(result.HistoryConsistent, Describe(result));
        Assert.IsEmpty(result.BoundaryBlockers, Describe(result));
        var check = result.DomainChecks.Single(item => item.Domain == domain);
        Assert.IsTrue(check.Evaluated);
        CollectionAssert.Contains(check.Blockers.ToArray(), blocker, Describe(result));
        CollectionAssert.Contains(check.ReadinessBlockers.ToArray(), PersistenceBlocker(domain));
        Assert.IsNotNull(result.Candidate);
        Assert.AreEqual(before, JsonSerializer.Serialize(result.Candidate));
        Assert.AreEqual(workspace.Document.AuxiliaryStateDigest,
            result.Candidate.Snapshot.Workspace.Document.AuxiliaryStateDigest);
        AssertHistoryPresent(domain, result.Candidate.Snapshot.Workspace.Document.AuxiliaryState);
        Assert.AreEqual(0, context.Owner.ActiveLeases);
        NoAuthority(result);
        AssertFilesUnchanged(context.Directory, durable);
    }

    private static string PersistenceBlocker(string domain) => domain == "contacts"
        ? CharacterCreationContactsBlockers.PersistenceAuthorityRequired
        : CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired;

    private static void AssertHistoryPresent(string domain, WorkspaceDocumentAuxiliaryState auxiliary)
    {
        if (domain == "contacts") Assert.HasCount(1, auxiliary.CharacterCreationContactReceipts!);
        else Assert.HasCount(1, auxiliary.CharacterCreationLifestyleReceipts!);
    }

    private static void NoAuthority(WorkspaceContinuationCandidateEvaluation result)
    {
        Assert.IsFalse(result.RestoreAuthorized);
        Assert.IsFalse(result.HistoricalProvenanceVerified);
    }

    private static string Describe(WorkspaceContinuationCandidateEvaluation result) => JsonSerializer.Serialize(result);

    private static Dictionary<string, (byte[] Bytes, DateTime Timestamp)> CaptureFiles(string directory)
        => System.IO.Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)), StringComparer.Ordinal);

    private static void AssertFilesUnchanged(string directory, Dictionary<string, (byte[] Bytes, DateTime Timestamp)> before)
    {
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), System.IO.Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        foreach (var (path, snapshot) in before)
        {
            CollectionAssert.AreEqual(snapshot.Bytes, File.ReadAllBytes(path), path);
            Assert.AreEqual(snapshot.Timestamp, File.GetLastWriteTimeUtc(path), path);
        }
    }

    // This is a conventional imported Creation character, not fabricated
    // bootstrap/draft authority. Contact points and starting nuyen are recorded
    // XML inputs in these existing domains. These tests prove current choices
    // against those inputs, not the provenance or independent legality of the
    // imported totals. All decision plans and receipts below are production.
    private sealed class DomainContext : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "chummer-continuation-current-domains-" + Guid.NewGuid().ToString("N"));
        public CharacterWorkspaceId Id { get; } = new("current-domain-runner");
        public LeaseOwner Owner { get; } = new();
        public FileWorkspaceStore Store { get; }
        public ICharacterSourceDataResolver Resolver { get; }
        public ICharacterFileQueries Queries { get; }
        public ILifeModulesCatalogService LifeModules { get; }
        public decimal LifestyleCostPerIncrement { get; private set; }

        public DomainContext(string domain)
        {
            System.IO.Directory.CreateDirectory(Directory);
            try
            {
                string coreRoot = FindCoreRoot();
                Resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
                LifeModules = new XmlLifeModulesCatalogService(Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml"));
                Queries = new XmlCharacterFileQueries(new CharacterFileService());
                Store = new FileWorkspaceStore(Directory);
                string contact = domain == "contacts" ? $"""
                    <contact>
                      <guid>{ContactId:D}</guid><name>Fixer</name><role>Broker</role><location>Vienna</location>
                      <connection>3</connection><loyalty>2</loyalty><group>False</group><free>False</free>
                      <family>False</family><blackmail>False</blackmail><type>Contact</type>
                      <chummercomplete><sentinel>preserved contact history</sentinel></chummercomplete>
                    </contact>
                    """ : string.Empty;
                string xml = $"""
                    <character>
                      <name>Continuation Runner</name><alias>Continuity</alias><metatype>Human</metatype>
                      <buildmethod>Priority</buildmethod><gameedition>SR5</gameedition>
                      <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion><created>False</created>
                      <settings>{CharacterCreationBootstrapProfiles.PrioritySettingsProfileId}</settings>
                      <karma>25</karma><nuyen>10000</nuyen><startingnuyen>10000</startingnuyen><nuyenbp>0</nuyenbp>
                      <contactpoints>10</contactpoints><improvements /><expenses /><qualities />
                      <contacts>{contact}</contacts><lifestyles />
                    </character>
                    """;
                Assert.IsTrue(Queries.Validate(new CharacterDocument(xml)).IsValid);
                Assert.IsTrue(Store.CreateWorkspaceDocument(Id, new WorkspaceDocument(xml, "sr5")).Success);
                if (domain == "contacts") ConfirmContact();
                else if (domain == "lifestyles") ConfirmLifestyle();
                else throw new ArgumentException("Unknown current domain.", nameof(domain));
            }
            catch
            {
                System.IO.Directory.Delete(Directory, recursive: true);
                throw;
            }
        }

        public WorkspaceContinuationExport Export()
        {
            var result = new WorkspaceContinuationExportService(new FileWorkspaceStore(Directory), Owner)
                .Export(Owner.Capture(), Id);
            Assert.IsTrue(result.Success, result.Error);
            Assert.IsNotNull(result.Value);
            AssertHistoryPresent(result.Value.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationContactReceipts is not null
                ? "contacts" : "lifestyles", result.Value.Snapshot.Workspace.Document.AuxiliaryState);
            return result.Value;
        }

        private void ConfirmContact()
        {
            var service = new CharacterCreationContactsService(Store);
            var loaded = service.Load(new(Id));
            Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
            Assert.IsTrue(loaded.Value.CanEdit, string.Join(",", loaded.Blockers));
            var edit = new CharacterCreationContactEdit(ContactId, Loyalty: 3);
            var preview = service.Preview(new(loaded.Value.Binding, edit));
            Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
            Assert.IsTrue(preview.Value.CanConfirm, string.Join(",", preview.Blockers));
            Assert.AreEqual(6, preview.Value.ContactBudgetAfter.Used);
            var confirmed = service.Confirm(new(loaded.Value.Binding, edit, preview.Value.PreviewDigest,
                "continuation-contact-decision", ExplicitlyConfirmed: true));
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied, confirmed.Outcome, string.Join(",", confirmed.Blockers));
            var lookup = new CharacterCreationContactsService(new FileWorkspaceStore(Directory))
                .LookupReceipt(new(Id, "continuation-contact-decision"));
            Assert.AreEqual(CharacterCreationContactOutcomes.Available, lookup.Outcome, string.Join(",", lookup.Blockers));
            Assert.AreEqual(confirmed.Value!.ReceiptDigest, lookup.Value!.ReceiptDigest);
        }

        private void ConfirmLifestyle()
        {
            var service = new CharacterCreationLifestylesService(Store, Resolver);
            var loaded = service.Load(new(Id));
            Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
            Assert.IsTrue(loaded.Value.CanEdit, string.Join(",", loaded.Blockers));
            var option = loaded.Value.Authority.LifestyleOptions.Single(item => item.Name == "Low");
            Assert.IsTrue(option.IsSelectable, string.Join(",", option.Blockers));
            var configuration = new CharacterCreationLifestyleConfiguration(LifestyleId, option.OptionId,
                "Vienna apartment", CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId,
                1, 100m, 0, false, false, 0, 0, 0, 0, "Vienna", "Innere Stadt", "First", []);
            var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create,
                LifestyleId, configuration);
            var preview = service.Preview(new(loaded.Value.Binding, mutation));
            Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
            Assert.IsTrue(preview.Value.CanConfirm, string.Join(",", preview.Blockers));
            Assert.IsNotNull(preview.Value.After);
            LifestyleCostPerIncrement = preview.Value.After.Economics.CostPerIncrement;
            Assert.IsTrue(LifestyleCostPerIncrement > 0m && LifestyleCostPerIncrement <= 10_000m);
            var confirmed = service.Confirm(new(loaded.Value.Binding, mutation, preview.Value.PreviewDigest,
                "continuation-lifestyle-decision", ExplicitlyConfirmed: true));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, confirmed.Outcome, string.Join(",", confirmed.Blockers));
            var lookup = new CharacterCreationLifestylesService(new FileWorkspaceStore(Directory), Resolver)
                .LookupReceipt(new(Id, "continuation-lifestyle-decision"));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, lookup.Outcome, string.Join(",", lookup.Blockers));
            Assert.AreEqual(confirmed.Value!.ReceiptDigest, lookup.Value!.ReceiptDigest);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

        private static string FindCoreRoot()
        {
            for (DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory); current is not null; current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml"))) return current.FullName;
            throw new DirectoryNotFoundException("Could not locate canonical Chummer source data.");
        }
    }

    private sealed class CountingResolver(ICharacterSourceDataResolver inner, LeaseOwner owner) : ICharacterSourceDataResolver
    {
        public int Reads { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            Reads++;
            Assert.AreEqual(1, owner.ActiveLeases);
            return inner.TryCreateContext(characterXml);
        }
    }

    private sealed class LeaseOwner : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _issuer = Guid.NewGuid().ToString("N");
        public OwnerScope Current => OwnerScope.LocalSingleUser;
        public int ActiveLeases { get; private set; }
        public OwnerContextStamp Capture() => new(Current, _issuer, 0);
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            if (expected != Capture())
            {
                Monitor.Exit(_gate);
                lease = null;
                return false;
            }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }
        private sealed class Lease(LeaseOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.ActiveLeases--;
                Monitor.Exit(owner._gate);
            }
        }
    }
}
