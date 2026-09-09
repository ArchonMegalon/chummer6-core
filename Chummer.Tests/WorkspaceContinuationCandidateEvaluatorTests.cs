using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationCandidateEvaluatorTests
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly OwnerScope ForeignOwner = new("another-continuation-owner");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Real_complete_mundane_and_Mystic_Adept_drafts_pass_readonly_checks_without_restore_authority(bool awakened)
    {
        using ReadyContext context = ReadyContext.Create(true, includeNonEmptyPurchases: !awakened,
            talentValue: awakened ? "Mystic Adept" : null, mysticPowerPoints: awakened ? 2 : 0);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        WorkspaceContinuationExport exported = Export(context, owner);
        byte[] bytes = Encode(exported);
        byte[] originalBytes = bytes.ToArray();
        var durable = CaptureFiles(context.Directory);
        ObservedResolver sources = new(context.Resolver, () => AssertLease(owner));
        ObservedCatalog catalog = new(Catalog(), () => Assert.AreEqual(1, owner.ActiveLeases));

        var result = new WorkspaceContinuationCandidateEvaluator(owner, sources, context.Queries, catalog)
            .Evaluate(owner.Capture(), bytes, MaximumBytes);

        Passed(result);
        Assert.AreEqual(exported.SnapshotDigest, result.Candidate!.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(exported), JsonSerializer.Serialize(result.Candidate));
        Assert.AreEqual(2, sources.Reads, "Only capture and independent recapture may consult the live resolver.");
        Assert.AreEqual(2, catalog.StageReads);
        Assert.IsTrue(result.DomainChecks.SelectMany(check => check.ReadinessBlockers).Any(),
            "Read-only evaluation must expose unavailable persistence, not claim an atomic writer.");
        foreach (string domain in new[] { "prerequisite", "attributes", "skills", "qualities", "resources", "gear" })
            Assert.IsTrue(result.DomainChecks.Any(check => check.Domain == domain), domain);
        if (awakened)
        {
            Assert.IsTrue(result.DomainChecks.Any(check => check.Domain == "magic-resonance"));
            Assert.IsNotNull(result.Candidate.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft);
        }
        Assert.AreEqual(0, owner.ActiveLeases);
        CollectionAssert.AreEqual(originalBytes, bytes);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Real_bootstrap_only_wizard_does_not_require_absent_future_choices()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var auxiliary = exported.Snapshot.Workspace.Document.AuxiliaryState;
        Assert.IsNotNull(auxiliary.CharacterCreationBootstrapBinding);
        Assert.IsNull(auxiliary.CharacterCreationPrerequisiteDraft);
        Assert.IsNull(auxiliary.CharacterCreationAttributesDraft);
        Assert.IsNull(auxiliary.CharacterCreationSkillsDraft);
        Assert.IsNull(auxiliary.CharacterCreationResourcesDraft);
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), Encode(exported), MaximumBytes);

        Passed(result);
        CollectionAssert.AreEqual(new[] { "bootstrap" }, result.DomainChecks.Select(check => check.Domain).ToArray());
        Assert.IsNull(result.Candidate!.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Bootstrap_XML_marker_without_its_binding_is_never_an_empty_valid_continuation(bool created)
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var workspace = exported.Snapshot.Workspace;
        var xml = System.Xml.Linq.XDocument.Parse(workspace.Document.Content);
        xml.Root!.SetElementValue("created", created ? "True" : "False");
        if (created)
            xml.Root.SetElementValue("metatype", "Human");
        var document = workspace.Document with { State = workspace.Document.State with
        {
            Payload = xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting),
            AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
        } };
        Assert.IsTrue(CharacterCreationBootstrapAuthority.HasBootstrapState(document));
        var forged = WithDigest(exported.Snapshot with
        {
            Workspace = workspace with { Document = document }
        });
        byte[] bytes = Encode(forged);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, MaximumBytes, out _));
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), bytes, MaximumBytes);

        Assert.IsFalse(result.CurrentDraftChecksPassed, Describe(result));
        var bootstrap = result.DomainChecks.Single(check => check.Domain == "bootstrap");
        Assert.IsFalse(bootstrap.Evaluated);
        Assert.IsNotEmpty(bootstrap.Blockers);
        if (created)
            CollectionAssert.Contains(result.BoundaryBlockers.ToArray(), "active-creation-drafts-on-career-character");
        NoAuthority(result);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Continuation_readers_do_not_hide_invalid_attribute_math_behind_missing_writer_readiness()
    {
        using ReadyContext context = ReadyContext.Create(true, talentValue: "Mystic Adept", mysticPowerPoints: 2);
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        var view = new WorkspaceContinuationReadView(OwnerScope.LocalSingleUser, saved);
        var skills = new CharacterCreationSkillsService(view, context.Resolver).LoadForContinuation(new(saved.Id));
        var magic = new CharacterCreationMagicResonanceService(view, context.Resolver).LoadForContinuation(new(saved.Id));
        Assert.IsNotNull(skills.Value!.PendingDraft);
        Assert.IsNotNull(magic.Value!.PendingDraft);
        Assert.IsTrue(skills.Blockers.All(item => item == CharacterCreationSkillsBlockers.PersistenceAuthorityRequired));
        Assert.IsTrue(magic.Blockers.All(item => item == CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired));

        var original = saved.Document.AuxiliaryState.CharacterCreationAttributesDraft!;
        var invalid = original with { Attributes = original.Attributes.Select(item =>
            item.AttributeId == "INT" ? item with { Current = item.Current + 1 } : item).ToArray() };
        invalid = invalid with { DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(invalid) };
        var document = saved.Document with { State = saved.Document.State with
        {
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCreationAttributesDraft = invalid }
        } };
        var malformedView = new WorkspaceContinuationReadView(OwnerScope.LocalSingleUser, saved with { Document = document });
        var attributes = new CharacterCreationAttributesService(malformedView, context.Resolver).Load(new(saved.Id));
        CollectionAssert.Contains(attributes.Blockers.ToArray(), CharacterCreationAttributesBlockers.DraftInvalid);
        skills = new CharacterCreationSkillsService(malformedView, context.Resolver).LoadForContinuation(new(saved.Id));
        magic = new CharacterCreationMagicResonanceService(malformedView, context.Resolver).LoadForContinuation(new(saved.Id));
        CollectionAssert.Contains(skills.Blockers.ToArray(), CharacterCreationSkillsBlockers.AttributesDraftRequired);
        CollectionAssert.Contains(magic.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.AttributesDraftRequired);
        Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(context.Store.Get(saved.Id).Value));
    }

    [TestMethod]
    public void Stale_ABA_foreign_and_value_only_owner_authorities_are_rejected_before_source_reads()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        ObservedResolver sources = new(context.Resolver, () => Assert.Fail("An unadmitted owner must not resolve sources."));
        ObservedCatalog catalog = new(Catalog(), () => Assert.Fail("An unadmitted owner must not read catalog sources."));
        var evaluator = new WorkspaceContinuationCandidateEvaluator(owner, sources, context.Queries, catalog);
        OwnerContextStamp original = owner.Capture();
        owner.Transition(ForeignOwner);
        Denied(evaluator.Evaluate(original, bytes, MaximumBytes), "owner-authority-unavailable");
        owner.Transition(OwnerScope.LocalSingleUser);
        Denied(evaluator.Evaluate(original, bytes, MaximumBytes), "owner-authority-unavailable");
        foreach (OwnerContextStamp bad in new[]
                 {
                     default, owner.Capture() with { Owner = ForeignOwner },
                     owner.Capture() with { AuthorityInstanceId = "different-issuer" },
                     owner.Capture() with { TransitionRevision = -1 }
                 })
            Denied(evaluator.Evaluate(bad, bytes, MaximumBytes), "owner-authority-unavailable");
        var valueOnly = new WorkspaceContinuationCandidateEvaluator(
            new ValueOnlyOwner(OwnerScope.LocalSingleUser), sources, context.Queries, catalog);
        Denied(valueOnly.Evaluate(owner.Capture(), bytes, MaximumBytes), "owner-authority-unavailable");
        Assert.AreEqual(0, sources.Reads);
        Assert.AreEqual(0, catalog.Reads);
        Assert.AreEqual(0, owner.ActiveLeases);
    }

    [TestMethod]
    public void Codec_tampering_over_budget_and_foreign_payload_owner_fail_before_source_capture()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        byte[] bytes = Encode(exported);
        var durable = CaptureFiles(context.Directory);
        ObservedResolver sources = new(context.Resolver, () => Assert.Fail("Wire or owner failure must precede source capture."));
        ObservedCatalog catalog = new(Catalog(), () => Assert.Fail("Wire or owner failure must precede catalog capture."));
        var evaluator = new WorkspaceContinuationCandidateEvaluator(owner, sources, context.Queries, catalog);
        JsonObject tampered = JsonNode.Parse(bytes)!.AsObject();
        tampered["Snapshot"]!["Workspace"]!["Document"]!["State"]!["Payload"] = "<character><name>Forged</name></character>";
        Denied(evaluator.Evaluate(owner.Capture(), Encoding.UTF8.GetBytes(tampered.ToJsonString()), MaximumBytes),
            "continuation-wire-invalid");
        Denied(evaluator.Evaluate(owner.Capture(), bytes, bytes.Length - 1), "continuation-wire-invalid");
        Denied(evaluator.Evaluate(owner.Capture(), Encoding.UTF8.GetBytes("{"), MaximumBytes), "continuation-wire-invalid");

        // Recomputing a content digest is possible for any caller. It is not an
        // owner lease or authorization to evaluate another owner's workspace.
        var foreign = WithDigest(exported.Snapshot with { OwnerId = ForeignOwner.NormalizedValue });
        var mismatch = evaluator.Evaluate(owner.Capture(), Encode(foreign), MaximumBytes);
        Denied(mismatch, "continuation-owner-mismatch");
        Assert.IsNull(mismatch.Candidate, "A foreign candidate must not be returned through this owner-bound seam.");
        owner.Transition(new OwnerScope(OwnerScope.LocalSingleUser.Value));
        var forgedLocal = evaluator.Evaluate(owner.Capture(), bytes, MaximumBytes);
        Assert.IsFalse(forgedLocal.CurrentDraftChecksPassed);
        Assert.IsNotEmpty(forgedLocal.BoundaryBlockers);
        NoAuthority(forgedLocal);
        Assert.AreEqual(0, sources.Reads);
        Assert.AreEqual(0, catalog.Reads);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Loss_of_the_actual_source_resolver_during_independent_recapture_invalidates_the_review()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        ObservedResolver sources = new(context.Resolver, () => AssertLease(owner)) { MissingAfterFirstRead = true };

        var result = new WorkspaceContinuationCandidateEvaluator(owner, sources, context.Queries, Catalog())
            .Evaluate(owner.Capture(), bytes, MaximumBytes);

        Denied(result, "continuation-sources-changed");
        Assert.IsNotNull(result.SourceDigest, "The first genuine capture must have succeeded.");
        Assert.IsTrue(result.DomainChecks.All(check => check.Evaluated && check.Blockers.Count == 0), Describe(result));
        Assert.AreEqual(2, sources.Reads);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Actual_catalog_stage_metadata_drift_changes_the_full_capture_even_with_the_same_claimed_catalog_digest()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        byte[] bytes = Encode(Export(context, owner));
        var durable = CaptureFiles(context.Directory);
        ObservedCatalog catalog = new(Catalog(), () => Assert.AreEqual(1, owner.ActiveLeases))
        {
            ChangeStageOnRecapture = true
        };
        string claimedDigest = catalog.Inner.GetAuthority().RawXmlDigest;

        var result = new WorkspaceContinuationCandidateEvaluator(owner, context.Resolver, context.Queries, catalog)
            .Evaluate(owner.Capture(), bytes, MaximumBytes);

        Denied(result, "continuation-sources-changed");
        Assert.IsNotNull(result.SourceDigest);
        Assert.AreEqual(claimedDigest, catalog.Inner.GetAuthority().RawXmlDigest);
        Assert.AreEqual(2, catalog.StageReads);
        Assert.AreEqual(0, owner.ActiveLeases);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Rehashed_Resources_history_still_requires_genuine_source_bound_recomputation()
    {
        using ReadyContext context = ReadyContext.Create(false, includeNonEmptyPurchases: true);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var workspace = exported.Snapshot.Workspace;
        var auxiliary = workspace.Document.AuxiliaryState;
        var original = auxiliary.CharacterCreationResourcesDraft!;
        const decimal unearned = 1000m;
        var draft = original with
        {
            Budget = original.Budget with
            {
                PriorityNuyen = original.Budget.PriorityNuyen + unearned,
                TotalStartingNuyen = original.Budget.TotalStartingNuyen + unearned,
                RemainingNuyen = original.Budget.RemainingNuyen + unearned,
                CarryoverExcess = Math.Max(0m, original.Budget.RemainingNuyen + unearned - original.Budget.CarryoverLimit)
            },
            FinalizationContribution = original.FinalizationContribution with
            {
                StartingNuyen = original.FinalizationContribution.StartingNuyen + unearned
            }
        };
        draft = draft with { FinalizationContribution = draft.FinalizationContribution with
        {
            ContributionDigest = CharacterCreationResourcesRules.ComputeContributionDigest(draft.FinalizationContribution)
        } };
        draft = draft with { DraftDigest = CharacterCreationResourcesRules.ComputeDraftDigest(draft) };
        var entry = auxiliary.CharacterCreationResourcesReceipts!.Single();
        var receipt = entry.Receipt with
        {
            DraftDigest = draft.DraftDigest, TotalStartingNuyen = draft.Budget.TotalStartingNuyen,
            RemainingNuyen = draft.Budget.RemainingNuyen
        };
        receipt = receipt with { ReceiptDigest = CharacterCreationResourcesRules.ComputeReceiptDigest(receipt) };
        var forgedAuxiliary = auxiliary with
        {
            CharacterCreationResourcesDraft = draft,
            CharacterCreationResourcesReceipts = [entry with { Receipt = receipt }]
        };
        Assert.IsTrue(CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
            workspace.Id, workspace.ContentRevision, draft, forgedAuxiliary.CharacterCreationResourcesReceipts));
        var forged = WithDigest(exported.Snapshot with { Workspace = workspace with
        {
            Document = workspace.Document with { State = workspace.Document.State with { AuxiliaryState = forgedAuxiliary } }
        } });
        byte[] bytes = Encode(forged);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, MaximumBytes, out _));
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), bytes, MaximumBytes);

        Assert.IsTrue(result.HistoryConsistent, Describe(result));
        Assert.IsFalse(result.CurrentDraftChecksPassed);
        var resources = result.DomainChecks.Single(check => check.Domain == "resources");
        CollectionAssert.Contains(resources.Blockers.ToArray(), CharacterCreationResourcesBlockers.ReceiptLedgerCorrupt);
        NoAuthority(result);
        AssertFilesUnchanged(context.Directory, durable);
    }

    [TestMethod]
    public void Real_finalized_Career_archive_survives_dirty_candidate_review_without_normalizing_checkpoint_or_granting_mutation()
    {
        using ReadyContext context = ReadyContext.Create(true, includeNonEmptyPurchases: true);
        var loaded = context.Finalizer.Load(new(context.WorkspaceId));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        var review = context.Finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        var finalized = context.Finalizer.Confirm(new(loaded.Value.Binding, review.Value.PreviewDigest,
            review.Value.Plan!.PlanDigest, "continuation-candidate-finalize", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, finalized.Outcome, string.Join(",", finalized.Blockers));
        var clean = context.Store.Get(context.WorkspaceId).Value!;
        var edited = clean.Document with { State = clean.Document.State with
        {
            Payload = clean.Document.Content.Replace("Finalization Runner", "Career Runner", StringComparison.Ordinal)
        } };
        Assert.AreNotEqual(clean.Document.Content, edited.Content);
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocument(context.WorkspaceId, clean.ContentRevision, edited).Success);
        var dirty = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(dirty.SavedRevision < dirty.ContentRevision);
        Assert.IsFalse(CharacterCareerReputationProjector.TryRead(dirty, context.Resolver, out _, out string error));
        Assert.AreEqual("reputation_workspace_not_clean", error);
        TestOwner owner = new(OwnerScope.LocalSingleUser);
        var exported = Export(context, owner);
        var durable = CaptureFiles(context.Directory);

        var result = Evaluator(context, owner).Evaluate(owner.Capture(), Encode(exported), MaximumBytes);

        Passed(result);
        CollectionAssert.AreEqual(new[] { "career-reputation" }, result.DomainChecks.Select(check => check.Domain).ToArray());
        Assert.AreEqual(dirty.ContentRevision, result.Candidate!.Snapshot.Workspace.ContentRevision);
        Assert.AreEqual(dirty.SavedRevision, result.Candidate.Snapshot.Workspace.SavedRevision);
        Assert.AreEqual(JsonSerializer.Serialize(exported), JsonSerializer.Serialize(result.Candidate));
        Assert.IsNotNull(result.Candidate.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        Assert.HasCount(1, result.Candidate.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
        Assert.IsFalse(CharacterCareerReputationProjector.TryRead(dirty, context.Resolver, out _, out error));
        Assert.AreEqual("reputation_workspace_not_clean", error);
        AssertFilesUnchanged(context.Directory, durable);
    }

    private static WorkspaceContinuationCandidateEvaluator Evaluator(ReadyContext context, IOwnerContextAccessor owner)
        => new(owner, context.Resolver, context.Queries, Catalog());

    private static XmlLifeModulesCatalogService Catalog() => new(Path.Combine(FindCoreRoot(), "Chummer", "data", "lifemodules.xml"));

    private static string FindCoreRoot()
    {
        for (DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate canonical Chummer source data.");
    }

    private static WorkspaceContinuationExport Export(ReadyContext context, TestOwner owner)
    {
        var result = new WorkspaceContinuationExportService(context.Store, owner).Export(owner.Capture(), context.WorkspaceId);
        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static WorkspaceContinuationExport WithDigest(WorkspaceContinuationSnapshot snapshot)
        => new(snapshot, WorkspaceContinuationSnapshotDigest.Compute(snapshot));

    private static byte[] Encode(WorkspaceContinuationExport exported) => WorkspaceContinuationCodec.Encode(exported, MaximumBytes);

    private static void Passed(WorkspaceContinuationCandidateEvaluation result)
    {
        Assert.IsTrue(result.CurrentDraftChecksPassed, Describe(result));
        Assert.IsTrue(result.HistoryConsistent);
        Assert.IsNotNull(result.Candidate);
        Assert.IsNotNull(result.SourceDigest);
        Assert.IsEmpty(result.BoundaryBlockers);
        Assert.IsTrue(result.DomainChecks.All(check => check.Evaluated && check.Blockers.Count == 0), Describe(result));
        NoAuthority(result);
    }

    private static void Denied(WorkspaceContinuationCandidateEvaluation result, string blocker)
    {
        Assert.IsFalse(result.CurrentDraftChecksPassed);
        CollectionAssert.Contains(result.BoundaryBlockers.ToArray(), blocker, Describe(result));
        NoAuthority(result);
    }

    private static void NoAuthority(WorkspaceContinuationCandidateEvaluation result)
    {
        Assert.IsFalse(result.RestoreAuthorized);
        Assert.IsFalse(result.HistoricalProvenanceVerified);
    }

    private static string Describe(WorkspaceContinuationCandidateEvaluation result) => JsonSerializer.Serialize(result);

    private static Dictionary<string, (byte[] Bytes, DateTime Timestamp)> CaptureFiles(string directory)
        => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)), StringComparer.Ordinal);

    private static void AssertFilesUnchanged(string directory, Dictionary<string, (byte[] Bytes, DateTime Timestamp)> before)
    {
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        foreach (var (path, snapshot) in before)
        {
            CollectionAssert.AreEqual(snapshot.Bytes, File.ReadAllBytes(path), path);
            Assert.AreEqual(snapshot.Timestamp, File.GetLastWriteTimeUtc(path), path);
        }
    }

    private static void AssertLease(TestOwner owner)
    {
        Assert.AreEqual(1, owner.ActiveLeases);
        Assert.IsFalse(owner.CanTransitionFromAnotherThread(), "The actual owner lease must span both live-source observations.");
    }

    private sealed class ObservedResolver(ICharacterSourceDataResolver inner, Action beforeRead) : ICharacterSourceDataResolver
    {
        public int Reads { get; private set; }
        public bool MissingAfterFirstRead { get; init; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            Reads++;
            beforeRead();
            return MissingAfterFirstRead && Reads > 1 ? null : inner.TryCreateContext(characterXml);
        }
    }

    // Every value starts at the actual XML-backed catalog. The adversarial
    // change below alters only recaptured stage metadata, not rule success.
    private sealed class ObservedCatalog(ILifeModulesCatalogService inner, Action beforeRead) : ILifeModulesCatalogService
    {
        public ILifeModulesCatalogService Inner => inner;
        public int Reads { get; private set; }
        public int StageReads { get; private set; }
        public bool ChangeStageOnRecapture { get; init; }
        private void Read() { Reads++; beforeRead(); }
        public LifeModuleCatalogAuthorityDto GetAuthority() { Read(); return inner.GetAuthority(); }
        public IReadOnlyList<LifeModuleStageDto> GetStages()
        {
            Read();
            StageReads++;
            var stages = inner.GetStages().ToArray();
            if (ChangeStageOnRecapture && StageReads > 1)
            {
                Assert.IsTrue(stages.Length > 1);
                stages[^1] = stages[^1] with { Order = stages[^1].Order + 100 };
            }
            return stages;
        }
        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
        { Read(); return inner.GetModules(stage); }
        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(string? stage = null, IReadOnlyCollection<string>? enabledSources = null)
        { Read(); return inner.GetOptionProjections(stage, enabledSources); }
    }

    private sealed class TestOwner(OwnerScope initial) : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _issuer = Guid.NewGuid().ToString("N");
        private OwnerScope _owner = initial;
        private long _revision;
        public int ActiveLeases { get; private set; }
        public OwnerScope Current { get { lock (_gate) return _owner; } }
        public OwnerContextStamp Capture() { lock (_gate) return new(_owner, _issuer, _revision); }
        public void Transition(OwnerScope owner)
        {
            lock (_gate)
            {
                Assert.AreEqual(0, ActiveLeases);
                _owner = owner;
                _revision++;
            }
        }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            if (!expected.IsValid || expected != new OwnerContextStamp(_owner, _issuer, _revision))
            {
                Monitor.Exit(_gate);
                lease = null;
                return false;
            }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }
        public bool CanTransitionFromAnotherThread()
        {
            var task = Task.Run(() =>
            {
                if (!Monitor.TryEnter(_gate)) return false;
                Monitor.Exit(_gate);
                return true;
            });
            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(10)));
            return task.Result;
        }
        private sealed class Lease(TestOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
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

    private sealed class ValueOnlyOwner(OwnerScope owner) : IOwnerContextAccessor
    {
        public OwnerScope Current => owner;
    }
}
