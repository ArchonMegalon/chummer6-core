using System.Text.Json;
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
using Owners839 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;
using ReadyPrivate839 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sources839_V3_continuation_binds_capture_and_preserves_legacy_defaults(bool books)
    {
        using var context = ReadyPrivate839.Create(includeGearReview: true, prepareSource: root => SelectBooks800(root, "core"));
        var owners = new Owners839();
        var stamp = owners.Capture();
        var exported = new WorkspaceContinuationExportService(context.Store, owners).Export(stamp, context.WorkspaceId);
        Assert.IsNotNull(exported.Value, exported.Error);
        byte[] wire = WorkspaceContinuationCodec.Encode(exported.Value!, WorkspacePrivateRuleSourceClosureBinding.MaximumContinuationBytes);
        byte[] record = File.ReadAllBytes(Record418(context));
        var reader = new WorkspaceContinuationCompletionReader(owners, context.Resolver, context.Queries, LifeModules654());
        var v1 = reader.LoadCompletionInputsWithRuleSourceExecution(stamp, wire, wire.Length, out var sources1);
        var v2 = reader.LoadCompletionInputsWithPurchaseSourceExecution(stamp, wire, wire.Length, out var sources2);
        Assert.IsTrue(v1.Success);
        Assert.IsTrue(v2.Success);
        byte[] legacy1 = sources1!.ExportPrivateInputs(), legacy2 = sources2!.ExportPrivateInputs();
        var current = PrepareSources832(context, books);
        var observed = reader.EvaluateCompletionInputs(stamp, wire, wire.Length,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, (fresh, sources) =>
            {
                AssertFresh839(fresh, sources, books);
                Assert.AreEqual(current.Inputs.SnapshotDigest, fresh.Inputs!.SnapshotDigest);
                CollectionAssert.AreEqual(current.Execution.ExportPrivateInputs(), sources!.ExportPrivateInputs());
                return fresh;
            }, sourcebookSelection: books);
        Assert.AreEqual(exported.Value!.SnapshotDigest, observed.Identity!.ContinuationDigest);
        Assert.IsFalse(observed.HasPersistenceAuthority);
        Assert.IsFalse(observed.CompletePolicyCoverage);
        var after1 = reader.LoadCompletionInputsWithRuleSourceExecution(stamp, wire, wire.Length, out sources1);
        var after2 = reader.LoadCompletionInputsWithPurchaseSourceExecution(stamp, wire, wire.Length, out sources2);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1, after1.Inputs!.RuleSourceExecutionSchema);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, after2.Inputs!.RuleSourceExecutionSchema);
        Assert.AreEqual(JsonSerializer.Serialize(v1), JsonSerializer.Serialize(after1));
        Assert.AreEqual(JsonSerializer.Serialize(v2), JsonSerializer.Serialize(after2));
        CollectionAssert.AreEqual(legacy1, sources1!.ExportPrivateInputs());
        CollectionAssert.AreEqual(legacy2, sources2!.ExportPrivateInputs());
        bool called = false;
        Assert.ThrowsExactly<ArgumentException>(() => reader.EvaluateCompletionInputs(stamp, wire, wire.Length,
            "chummer.creation-rule-source-execution.v839-unknown", (_, _) => { called = true; return true; }, sourcebookSelection: books));
        Assert.ThrowsExactly<ArgumentException>(() => reader.EvaluateCompletionInputs(stamp, wire, wire.Length,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1, (_, _) => { called = true; return true; }, sourcebookSelection: true));
        Assert.ThrowsExactly<ArgumentException>(() => reader.EvaluateCompletionInputs(stamp, wire, wire.Length,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, (_, _) => { called = true; return Task.FromResult(true); },
            sourcebookSelection: books));
        Assert.IsFalse(called);
        owners.Set(new OwnerScope("sources839-other-owner"));
        Assert.IsFalse(reader.EvaluateCompletionInputs(stamp, wire, wire.Length,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, (fresh, sources) =>
            { Assert.IsNull(sources); return fresh.Success; }, sourcebookSelection: books));
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void Sources839_V3_private_Draft_observation_transfer_preserves_capture_identity(bool books, bool linked)
        => AssertPrivateDraftObservation897(books, linked, CharacterCreationBuildMethods.Priority);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Composite897_SumToTen_private_Draft_observation_preserves_capture_identity(bool books)
        => AssertPrivateDraftObservation897(books, false, CharacterCreationBuildMethods.SumToTen);

    private static void AssertPrivateDraftObservation897(bool books, bool linked, string buildMethod)
    {
        using var context = ReadyPrivate839.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", buildMethod: buildMethod,
            amendSettings: profile => profile.Element("books")!.ReplaceWith(
                new XElement("books", new XElement("book", "SR5"))),
            sourcebooksOwner800: linked ? new OwnerScope("sources839-linked") : null);
        var owners = new Owners839();
        if (context.SourcebooksOwner800 is { } owner) owners.Set(owner);
        var stamp = owners.Capture();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft839(context, store, owners, books);
        var receipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(stamp, first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var saved = Read839(store, owners, context.WorkspaceId);
        string before = CharacterCreationFinalizationDigest.Compute(saved);
        var exported = store.ExportPrivateRuleSourceClosure(owners, stamp, context.WorkspaceId,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes);
        byte[] wire = exported.ExportPrivateBytes();
        var imported = WorkspacePrivateRuleSourceClosure.Import(wire, exported.Binding,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, context.Queries);
        Assert.AreEqual(1, imported.Binding.ArchiveCount);
        Assert.AreEqual(1, imported.Binding.CampaignCount);
        Assert.AreEqual(exported.Binding, imported.Binding);
        CollectionAssert.AreEqual(wire, imported.ExportPrivateBytes());
        CharacterCreationCompletionReviewInputs? detached = null;
        CharacterCreationRuleSourceExecutionContext? detachedSources = null;
        CharacterCreationCompletionReviewRequest? proposal = null;
        CampaignAuthority518? authority = null;
        (WorkspaceContinuationCompletionIdentity Identity, byte[] Capture) Observe(WorkspacePrivateRuleSourceClosure closure,
            CancellationToken cancellationToken = default, Action? inside = null)
            => closure.EvaluateCompletionInputs(owners, stamp, context.Resolver, context.Queries, LifeModules654(),
                CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, (fresh, sources, binding) =>
                {
                    Assert.AreEqual(1, owners.ActiveLeases);
                    Assert.AreEqual(exported.Binding, binding);
                    AssertFresh839(fresh, sources, books);
                    detached = fresh.Inputs!;
                    detachedSources = sources;
                    // A real composite retires the ordinary Skills state. Continuation
                    // must validate its effective whole-graph projection instead.
                    Assert.IsNotNull(detached.CompositeBaseline);
                    Assert.IsNull(detached.Skills.SavedSkillsState);
                    Assert.IsFalse(fresh.HasPersistenceAuthority);
                    Assert.IsFalse(fresh.CompletePolicyCoverage);
                    Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromFull(detached, out _));
                    var campaign = new CharacterCreationCampaignBinding(CharacterCreationCampaignBinding.SchemaV1,
                        fresh.Identity!, Hash518("sources839-private-current"));
                    authority = new(campaign, CharacterCreationCampaignState.AdmittedUnchangedPolicy);
                    proposal = SourceRequest832(detached, CharacterCreationPolicyRequirement.None) with
                    { Selections = detached.CompositeBaseline!.Selections, CampaignAuthority = campaign };
                    // Observation cannot evaluate arbitrary replacement choices or
                    // turn a V3 request into a source-free legacy review.
                    var changed = proposal with { Selections = proposal.Selections with { TotalKarmaInvestment = -1 } };
                    var refusedSelection = CharacterCreationCompletionReviewService.ObserveComposite(detached, changed,
                        ruleSources: sources);
                    Assert.IsNull(refusedSelection.Value);
                    CollectionAssert.Contains(refusedSelection.Blockers.ToArray(),
                        CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
                    var refusedSources = CharacterCreationCompletionReviewService.ObserveComposite(detached, proposal);
                    Assert.IsNull(refusedSources.Value);
                    CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.RequestInvalid },
                        refusedSources.Blockers.ToArray());
                    var refusedCampaign = CharacterCreationCompletionReviewService.Review(detached, proposal,
                        ruleSources: sources);
                    Assert.IsNull(refusedCampaign.Value);
                    CollectionAssert.Contains(refusedCampaign.Blockers.ToArray(), CharacterCreationCampaignExecutionContext.Blocker);
                    Success425(CharacterCreationCompletionReviewService.Review(detached, proposal, ruleSources: sources,
                        campaign: CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Preparation)));
                    inside?.Invoke();
                    return (fresh.Identity!, sources!.ExportPrivateInputs());
                }, cancellationToken, sourcebookSelection: books);
        var beforeTransfer = Observe(exported);
        var afterTransfer = Observe(imported);
        Assert.AreEqual(beforeTransfer.Identity, afterTransfer.Identity);
        CollectionAssert.AreEqual(beforeTransfer.Capture, afterTransfer.Capture);
        Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryFromFull(detached!, out _));
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(detached!, proposal!, ruleSources: detachedSources,
            campaign: CharacterCreationCampaignExecutionContext.Capture(authority!, CharacterCreationCampaignPhase.Preparation)).Value);
        bool called = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => imported.EvaluateCompletionInputs(owners, stamp,
            context.Resolver, context.Queries, LifeModules654(), "chummer.creation-rule-source-execution.v839-unknown",
            (_, _, _) => { called = true; return true; }, sourcebookSelection: books));
        Assert.ThrowsExactly<InvalidOperationException>(() => imported.EvaluateCompletionInputs(owners, stamp,
            context.Resolver, context.Queries, LifeModules654(), CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3,
            (_, _, _) => { called = true; return Task.FromResult(true); }, sourcebookSelection: books));
        Assert.IsFalse(called);
        Assert.ThrowsExactly<InvalidOperationException>(() => imported.EvaluateCompletionInputs<object>(owners, stamp,
            context.Resolver, context.Queries, LifeModules654(), CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3,
            (_, _, _) => Task.FromResult(true), sourcebookSelection: books));
        var foreign = new Owners839();
        foreign.Set(new OwnerScope("sources839-foreign"));
        Assert.ThrowsExactly<InvalidOperationException>(() => imported.EvaluateCompletionInputs(foreign, foreign.Capture(),
            context.Resolver, context.Queries, LifeModules654(), CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3,
            (_, _, _) => { called = true; return true; }, sourcebookSelection: books));
        Assert.IsFalse(called);
        using var cancel = new CancellationTokenSource();
        Assert.ThrowsExactly<OperationCanceledException>(() => Observe(imported, cancel.Token, () => cancel.Cancel()));
        // Mutate only the disposable fixture's source during the synchronous callback.
        string sourcePath = Path.Combine(context.Directory, "source", "data", "metatypes.xml");
        byte[] originalSource = File.ReadAllBytes(sourcePath);
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => Observe(imported, inside: () =>
            {
                var source = XDocument.Load(sourcePath);
                source.Root!.Element("metatypes")!.Elements("metatype")
                    .Single(row => row.Element("name")?.Value == "Human").SetElementValue("walk", "99/1/0");
                source.Save(sourcePath);
            }));
        }
        finally { File.WriteAllBytes(sourcePath, originalSource); }
        Assert.AreEqual(afterTransfer.Identity, Observe(imported).Identity);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(Read839(store, owners, context.WorkspaceId)));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var coldImport = WorkspacePrivateRuleSourceClosure.Import(wire, exported.Binding,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, context.Queries);
        CollectionAssert.AreEqual(wire, coldImport.ExportPrivateBytes());
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = Read839(cold, owners, context.WorkspaceId);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(reopened));
        Assert.AreEqual(receipt, reopened.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single().Receipt);
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.AreEqual(0, foreign.ActiveLeases);
        if (linked) Assert.IsFalse(cold.Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sources839_V3_attribute_rebase_keeps_schema_books_and_exact_cold_history(bool books)
    {
        using var context = ReadyPrivate839.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: root => SelectBooks800(root, "core"));
        var owners = new Owners839();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft839(context, store, owners, books);
        var firstReceipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var original = store.Get(context.WorkspaceId).Value!;
        byte[] oldEntry = JsonSerializer.SerializeToUtf8Bytes(original.Document.AuxiliaryState.CharacterCreationCompletionDrafts![0]);
        var retained = RetainedSources774(context);
        var factory = new CampaignFactory774(owners);
        var service = Rebase774(context, store, owners, factory);
        var state = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        var requested = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        byte[] before = File.ReadAllBytes(Record418(context));
        var preview = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, requested)));
        var repeated = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, requested.Reverse().ToArray())));
        Assert.AreEqual(preview.PreviewDigest, repeated.PreviewDigest);
        var proposal = preview.Review.Review.Request;
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, proposal.RuleSourceExecutionSchema);
        Assert.AreEqual(books, proposal.SourcebookSelection is not null);
        Assert.AreEqual(first.Command.Review.Review.Request.SourcebookSelection, proposal.SourcebookSelection);
        Assert.AreEqual(proposal.RuleSourceCaptureDigest, proposal.CampaignAuthority!.RetainedIdentity.SourceDigest);
        AssertRows774(requested, proposal.Selections.AttributeAllocations);
        AssertPreserved774(first.Command.Review.Review.Request.Selections, proposal.Selections);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        var receipt = Success425(service.ConfirmRebase(owners.Capture(), preview, "sources839-rebase", true));
        var saved = store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(original.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(original.Document.Content, saved.Document.Content);
        Assert.IsNull(saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        var entries = saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        Assert.AreEqual(2, entries.Count);
        CollectionAssert.AreEqual(oldEntry, JsonSerializer.SerializeToUtf8Bytes(entries[0]));
        byte[] newEntry = JsonSerializer.SerializeToUtf8Bytes(entries[1]);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(entries[1].Baseline.SelectedReplay, out _, out var witness));
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, witness!.RuleSourceExecutionSchema);
        Assert.AreEqual(proposal.SourcebookSelection, witness.SourcebookSelection);
        foreach (var source in retained) CollectionAssert.AreEqual(source.Value, File.ReadAllBytes(source.Key));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        factory.ForbidObservation = true;
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success, reopened.Error);
        var coldEntries = reopened.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        CollectionAssert.AreEqual(new[] { firstReceipt, receipt }, coldEntries.Select(row => row.Receipt).ToArray());
        CollectionAssert.AreEqual(oldEntry, JsonSerializer.SerializeToUtf8Bytes(coldEntries[0]));
        CollectionAssert.AreEqual(newEntry, JsonSerializer.SerializeToUtf8Bytes(coldEntries[1]));
        before = File.ReadAllBytes(Record418(context));
        Assert.AreEqual(receipt, Success425(Rebase774(context, cold, owners, factory)
            .ConfirmRebase(owners.Capture(), preview, "sources839-rebase", true)));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static void AssertFresh839(WorkspaceContinuationCompletionResult fresh,
        CharacterCreationRuleSourceExecutionContext? sources, bool books)
    {
        Assert.IsTrue(fresh.Success, string.Join(",", fresh.AuthorityBlockers));
        Assert.IsNotNull(sources);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, fresh.Inputs!.RuleSourceExecutionSchema);
        Assert.AreEqual(books, fresh.Inputs.SourcebookSelection is not null);
        Assert.AreEqual(fresh.Inputs.RuleSourceCaptureDigest, fresh.Identity!.SourceDigest);
        byte[] bytes = sources!.ExportPrivateInputs();
        Assert.AreEqual(RetainedCharacterRuleSourceInputs.Digest(bytes), fresh.Identity.SourceDigest);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(bytes, fresh.Identity.SourceDigest, out var retained));
        Assert.AreEqual(fresh.Inputs.SourcebookSelection, retained!.SourcebookSelection);
        CollectionAssert.AreEqual(bytes, retained.ExportPrivateInputs());
        Assert.AreEqual(fresh.Inputs.SnapshotDigest, fresh.Identity.InputsDigest);
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            fresh.Identity with { ObservationDigest = string.Empty }), fresh.Identity.ObservationDigest);
    }

    private static WorkspaceStoredDocument Read839(FileWorkspaceStore store, Owners839 owners, CharacterWorkspaceId id)
    {
        var result = owners.Capture().Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owners.Capture().Owner, id);
        Assert.IsTrue(result.Success, result.Error);
        return result.Value!;
    }

    private static PreparedDraft550 PrepareDraft839(ReadyPrivate839 context, FileWorkspaceStore store, Owners839 owners, bool books)
    {
        WorkspaceStoredDocument original;
        CampaignAuthority518 authority;
        CharacterCreationCompletionReviewRequest proposal;
        using (CharacterCreationCompletionDraftTransaction.BeginValidationOperation())
        {
            var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
            var prepared = Success425(reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(),
                new(context.WorkspaceId), includeLoadout: true, includeSourcebooks: books));
            original = Read839(store, owners, context.WorkspaceId);
            var inputs = prepared.Inputs;
            var binding = new CharacterCreationCampaignBinding(CharacterCreationCampaignBinding.SchemaV1,
                new(owners.Capture().Owner.Value, original.Id, original.ContentRevision, original.SavedRevision,
                    Hash518("sources839-continuation")[7..], inputs.RuleSourceCaptureDigest!, inputs.Skills.Binding.RawCharacterXmlDigest,
                    original.Document.AuxiliaryStateDigest, inputs.SnapshotDigest, inputs.EffectivePolicy!.PolicyDigest,
                    Hash518("sources839-observation")), Hash518("sources839-transition-" + original.ContentRevision));
            authority = new(binding, CharacterCreationCampaignState.AdmittedUnchangedPolicy);
            proposal = SourceRequest832(inputs, CharacterCreationPolicyRequirement.None) with
            { CampaignAuthority = binding };
            Success425(CharacterCreationCompletionReviewService.Review(inputs, proposal, ruleSources: prepared.Execution,
                campaign: CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Preparation)));
        }
        var reviewed = Success425(DraftService550(context, store, owners).ReviewDraftWithCampaign(owners.Capture(),
            context.WorkspaceId, proposal, CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Review)));
        return new(original, authority, new(CharacterCreationCompletionDraftApplyRequest.SchemaV1, reviewed,
            "sources839-draft-" + original.ContentRevision, true));
    }
}
