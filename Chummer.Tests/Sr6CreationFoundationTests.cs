using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow(Sr6CharacterCreationBuildMethods.Priority)]
    [DataRow(Sr6CharacterCreationBuildMethods.SumToTen)]
    public void Confirm_saves_choices_without_granting_effects_and_cold_reopen_replays_once(string method)
    {
        using var fixture = new Fixture(method);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request();
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision, "Preview must not write.");
        var result = fixture.Service.Confirm(fixture.Stamp, request);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        Assert.IsFalse(result.Value.Replayed);
        var coldStore = new FileWorkspaceStore(fixture.Directory);
        var reopened = coldStore.Get(fixture.Id).Value!;
        Assert.AreEqual(2L, reopened.ContentRevision);
        Assert.AreEqual(2L, reopened.SavedRevision);
        Assert.AreEqual(before.Document.Content, reopened.Document.Content, "Pending choices do not apply character values.");
        var coldService = new Sr6CreationFoundationService(coldStore, fixture.Owner);
        var state = coldService.Load(fixture.Stamp, fixture.Id).Value;
        Assert.IsNotNull(state);
        Assert.AreEqual(request.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.IsTrue(coldService.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, coldStore.Get(fixture.Id).Value!.ContentRevision);

        var changed = fixture.Request(Selection(method) with { MetatypeId = "troll" });
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, changed).Value);
        Assert.AreEqual(3L, coldStore.Get(fixture.Id).Value!.ContentRevision);
        Assert.AreEqual("troll", coldService.Load(fixture.Stamp, fixture.Id).Value!.Selection!.Selection.MetatypeId);
        Assert.IsTrue(coldService.Confirm(fixture.Stamp, request).Value!.Replayed);
    }

    [TestMethod]
    public void Priority_and_sum_to_ten_project_their_own_budgets()
    {
        using var priority = new Fixture();
        var preview = priority.Preview(Selection());
        Assert.AreEqual(new Sr6CreationPriorityBudget(24, 24, 150000, 4, "E"), preview.Budget);
        Assert.AreEqual(0, preview.BaseMagic);
        Assert.AreEqual(0, preview.BaseResonance);
        CollectionAssert.AreEqual(new[] { Sr6CreationFoundationRules.CoreSourceAnchor }, preview.SourceAnchorIds.ToArray());
        using var sum = new Fixture(Sr6CharacterCreationBuildMethods.SumToTen);
        var repeated = sum.Preview(Selection(Sr6CharacterCreationBuildMethods.SumToTen));
        Assert.AreEqual(new Sr6CreationPriorityBudget(16, 24, 275000, 4, "E"), repeated.Budget);
        CollectionAssert.Contains(repeated.SourceAnchorIds.ToArray(), "sr6_schattenkompendium_2022:p28");
        Assert.IsNull(priority.Service.Preview(priority.Stamp, priority.Binding, repeated.Selection).Value);
    }

    [TestMethod]
    [DataRow("human", "A", false)]
    [DataRow("human", "B", false)]
    [DataRow("human", "C", true)]
    [DataRow("elf", "A", false)]
    [DataRow("elf", "B", true)]
    [DataRow("dwarf", "A", true)]
    [DataRow("ork", "A", true)]
    [DataRow("troll", "A", true)]
    public void Metatype_selection_respects_sr6_priority_row(string metatype, string rank, bool allowed)
    {
        using var fixture = new Fixture();
        var selection = Ranked(CharacterCreationPriorityCategoryIds.Heritage, rank) with { MetatypeId = metatype };
        var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding, selection);
        Assert.AreEqual(allowed, result.Value is not null);
        if (!allowed) CollectionAssert.Contains(result.Blockers.ToArray(), Sr6CreationFoundationBlockers.MetatypeUnavailable);
    }

    [TestMethod]
    [DataRow("mundane", "E", 0, 0)]
    [DataRow("magician", "A", 4, 0)]
    [DataRow("magician", "D", 1, 0)]
    [DataRow("aspected-magician", "A", 5, 0)]
    [DataRow("aspected-magician", "D", 2, 0)]
    [DataRow("adept", "B", 3, 0)]
    [DataRow("mystic-adept", "C", 2, 0)]
    [DataRow("technomancer", "A", 0, 4)]
    [DataRow("technomancer", "D", 0, 1)]
    public void Talent_projects_priority_base_only(string talent, string rank, int magic, int resonance)
    {
        using var fixture = new Fixture();
        var preview = fixture.Preview(Ranked(CharacterCreationPriorityCategoryIds.Talent, rank) with { TalentId = talent });
        Assert.AreEqual(magic, preview.BaseMagic);
        Assert.AreEqual(resonance, preview.BaseResonance);
    }

    [TestMethod]
    [DataRow("mundane", "A")]
    [DataRow("magician", "E")]
    [DataRow("technomancer", "E")]
    public void Talent_rank_mismatch_fails_without_write(string talent, string rank)
    {
        using var fixture = new Fixture();
        var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            Ranked(CharacterCreationPriorityCategoryIds.Talent, rank) with { TalentId = talent });
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), Sr6CreationFoundationBlockers.TalentUnavailable);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow(Sr6CharacterCreationBuildMethods.PointBuy)]
    [DataRow(Sr6CharacterCreationBuildMethods.Karma)]
    [DataRow(Sr6CharacterCreationBuildMethods.LifePath)]
    public void Other_sr6_methods_do_not_enter_priority_foundation(string method)
    {
        using var fixture = new Fixture(method);
        var result = fixture.Service.Load(fixture.Stamp, fixture.Id);
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), Sr6CreationFoundationBlockers.PendingDraftRequired);
    }

    [TestMethod]
    public void Invalid_or_stale_confirmation_never_changes_workspace()
    {
        using var fixture = new Fixture();
        var request = fixture.Request();
        foreach (var invalid in new[]
        {
            request with { ExplicitlyConfirmed = false }, request with { OperationId = Guid.Empty },
            request with { PreviewDigest = "sha256:" + new string('0', 64) },
            request with { Binding = request.Binding with { ContentRevision = 2 } },
            request with { Binding = request.Binding with { AuthorityDigest = "sha256:" + new string('0', 64) } },
            request with { Selection = request.Selection with { MetatypeId = "Human" } },
            request with { Selection = request.Selection with { Assignments = [] } }
        }) Assert.IsNull(fixture.Service.Confirm(fixture.Stamp, invalid).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        Assert.IsNull(fixture.Service.Confirm(fixture.Stamp, request with { OperationId = Guid.NewGuid() }).Value);
        var conflict = fixture.Request(Selection() with { MetatypeId = "elf" }) with { OperationId = request.OperationId };
        Assert.IsNull(fixture.Service.Confirm(fixture.Stamp, conflict).Value);
        Assert.AreEqual(2L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public async Task Concurrent_confirmations_admit_exactly_one_operation()
    {
        using var fixture = new Fixture();
        var first = fixture.Request();
        var second = first with { OperationId = Guid.NewGuid() };
        var results = await Task.WhenAll(Task.Run(() => fixture.Service.Confirm(fixture.Stamp, first)),
            Task.Run(() => fixture.Service.Confirm(fixture.Stamp, second)));
        Assert.AreEqual(1, results.Count(result => result.Value is not null));
        Assert.AreEqual(2L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        Assert.HasCount(1, fixture.Store.Get(fixture.Id).Value!.Document.AuxiliaryState.Sr6CreationFoundationDecisions!);
    }

    [TestMethod]
    public void Generic_writer_cannot_append_or_remove_foundation_decisions()
    {
        using var fixture = new Fixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request();
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(before, request, out var candidate, out _));
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(fixture.Id,
            before.ContentRevision, before.Document.AuxiliaryStateDigest, candidate).Success);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var current = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(fixture.Id,
            current.ContentRevision, current.Document.AuxiliaryStateDigest, before.Document).Success);
        Assert.AreEqual(2L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Rehashing_a_fabricated_budget_does_not_make_it_rule_authority()
    {
        using var fixture = new Fixture();
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, fixture.Request(), out var candidate, out var decision));
        var preview = decision.Preview with { Budget = decision.Preview.Budget with { AttributePoints = 99 } };
        preview = preview with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(preview) };
        var forged = decision with { Preview = preview, Command = decision.Command with { PreviewDigest = preview.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        var forgedDocument = candidate with { State = candidate.State with { AuxiliaryState = state } };
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { Document = forgedDocument, ContentRevision = 2, SavedRevision = 2 }).Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Write_failure_recovers_exact_durable_commit_without_replaying_mutation(bool afterReplace)
    {
        using var fixture = new Fixture();
        var request = fixture.Request();
        fixture.Fault.Stage = afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed;
        var result = fixture.Service.Confirm(fixture.Stamp, request);
        Assert.AreEqual(afterReplace, result.Value is not null);
        Assert.AreEqual(1, fixture.Fault.Calls, "Recovery reads; it does not repeat the write.");
        fixture.Fault.Stage = null;
        var reopened = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        Assert.AreEqual(afterReplace, reopened.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Foreign_or_expired_owner_stamps_cannot_read_preview_or_confirm()
    {
        using var owner = new RequestOwnerContextAccessor(new OwnerScope("sr6-foundation-a"));
        using var other = new RequestOwnerContextAccessor(new OwnerScope("sr6-foundation-b"));
        using var fixture = new Fixture(owner: owner);
        var stamp = owner.Capture();
        var request = fixture.Request();
        var foreignService = new Sr6CreationFoundationService(fixture.Store, other);
        Assert.IsNull(foreignService.Load(other.Capture(), fixture.Id).Value);
        Assert.IsNull(foreignService.Preview(other.Capture(), request.Binding, request.Selection).Value);
        Assert.IsNull(foreignService.Confirm(other.Capture(), request).Value);
        Assert.IsNull(fixture.Service.Confirm(other.Capture(), request).Value);
        Assert.IsNotNull(fixture.Service.Confirm(stamp, request).Value);
        owner.Dispose();
        Assert.IsNull(fixture.Service.Load(stamp, fixture.Id).Value);
        Assert.IsNull(fixture.Service.Confirm(stamp, request).Value);
        Assert.AreEqual(2L, new FileWorkspaceStore(fixture.Directory).Get(stamp.Owner, fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Imported_decision_is_history_not_local_operation_replay()
    {
        using var fixture = new Fixture();
        var request = fixture.Request();
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var imported = saved with { LocalHistory = new WorkspaceLocalHistory(Guid.NewGuid().ToString("N"),
            saved.ContentRevision, new string('1', 64)) };
        Assert.IsNotNull(Sr6CreationFoundationRules.Load(imported).Value);
        var lookup = Sr6CreationFoundationRules.Lookup(imported, request);
        Assert.IsNotNull(lookup);
        Assert.IsNull(lookup.Value);
        CollectionAssert.Contains(lookup.Blockers.ToArray(), Sr6CreationFoundationBlockers.OperationConflict);
    }

    [TestMethod]
    public void Missing_atomic_capability_does_not_fall_back_to_generic_store_writes()
    {
        using var fixture = new Fixture();
        var store = new InMemoryWorkspaceStore();
        var service = new Sr6CreationFoundationService(store, fixture.Owner);
        var result = service.Confirm(fixture.Stamp, fixture.Request());
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), Sr6CreationFoundationBlockers.PersistenceUnavailable);
        Assert.HasCount(0, store.List());
    }

    private static Sr6CreationFoundationSelection Selection(string method = Sr6CharacterCreationBuildMethods.Priority)
        => new("human", "mundane", [
            new(CharacterCreationPriorityCategoryIds.Heritage, "D"), new(CharacterCreationPriorityCategoryIds.Talent, "E"),
            new(CharacterCreationPriorityCategoryIds.Attributes, method == Sr6CharacterCreationBuildMethods.SumToTen ? "B" : "A"),
            new(CharacterCreationPriorityCategoryIds.Skills, "B"),
            new(CharacterCreationPriorityCategoryIds.Resources, method == Sr6CharacterCreationBuildMethods.SumToTen ? "B" : "C")]);

    private static Sr6CreationFoundationSelection Ranked(string category, string rank)
    {
        var available = new Queue<string>(new[] { "A", "B", "C", "D", "E" }.Where(value => value != rank));
        var choices = CharacterCreationPriorityCategoryIds.Ordered.Select(id => new Sr6CreationPriorityChoice(id,
            id == category ? rank : available.Dequeue())).ToArray();
        string talentRank = choices.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent).Rank;
        return new("troll", talentRank == "E" ? "mundane" : "magician", choices);
    }

    private sealed class Fault : IFileWorkspaceStoreFaultInjector
    {
        public FileWorkspaceStoreFaultStage? Stage { get; set; }
        public int Calls { get; private set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == Stage) { Calls++; throw new IOException("Injected SR6 foundation write failure."); }
        }
    }

    private sealed class RejectSr5SourceResolver : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => throw new AssertFailedException("SR6 must not consult SR5 sources.");
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "chummer-sr6-foundation-" + Guid.NewGuid().ToString("N"));
        public FileWorkspaceStore Store { get; }
        public Fault Fault { get; } = new();
        public IOwnerContextLeaseAccessor Owner { get; }
        public OwnerContextStamp Stamp => Owner.Capture();
        public CharacterWorkspaceId Id { get; }
        public Sr6CreationFoundationService Service { get; }
        public Sr6CreationFoundationBinding Binding => Service.Load(Stamp, Id).Value!.Binding;
        private readonly string _method;

        public Fixture(string method = Sr6CharacterCreationBuildMethods.Priority, IOwnerContextLeaseAccessor? owner = null)
        {
            _method = method;
            Owner = owner ?? new LocalOwnerContextAccessor();
            Store = new FileWorkspaceStore(Directory, Fault);
            var queries = new XmlCharacterFileQueries(new CharacterFileService());
            var codec = new Sr6WorkspaceCodec(queries, new XmlCharacterSectionQueries(new CharacterSectionService()),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var bootstrap = new CharacterCreationBootstrapService(Store, new RulesetWorkspaceCodecResolver([codec]), queries,
                new RejectSr5SourceResolver(), rulesetBootstrapProviders: [new Sr6CharacterCreationBootstrapProvider()]);
            Assert.IsTrue(Sr6CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(method, out var profile));
            var created = new OwnerBoundCharacterCreationBootstrapService(bootstrap, Owner).Create(Stamp,
                new(CharacterCreationBootstrapSchemas.RequestV1, CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                    RulesetDefaults.Sr6, "SR6 foundation runner", "Foundation", method, profile));
            Assert.IsNotNull(created.Value, string.Join(",", created.Blockers));
            Id = created.Value.WorkspaceId;
            Service = new(Store, Owner);
        }

        public Sr6CreationFoundationPreview Preview(Sr6CreationFoundationSelection selection)
        {
            var result = Service.Preview(Stamp, Binding, selection);
            Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
            return result.Value;
        }

        public Sr6CreationFoundationConfirmRequest Request(Sr6CreationFoundationSelection? selection = null)
        {
            var preview = Preview(selection ?? Selection(_method));
            return new(preview.Binding, preview.Selection, preview.PreviewDigest, Guid.NewGuid(), true);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
