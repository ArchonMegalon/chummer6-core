using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Chummer.Infrastructure.Files;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready886 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D886 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Initiative918_operation_scope_preserves_explicit_admission_and_disposal()
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905,
            prepareSource: root => ChangeHuman886(root, human => human.SetElementValue("initiativedice", 2)));
        string root = Path.Combine(context.Directory, "source");
        string raw = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        using var historical = resolver.CreateOperationScope();
        var admitted = historical.ForCreationSourceExecution(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4);
        Assert.IsNotNull(admitted, "The ordinary operation wrapper must forward explicit V4 admission.");
        using var operation = ((ICharacterSourceDataResolverOperationScopeFactory)admitted!).CreateOperationScope();
        Assert.AreSame(operation, operation.ForCreationSourceExecution(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4));
        var source = operation.TryCreateContext(raw)!;
        Assert.IsTrue(source.TryResolveCreationMetatypeCatalog(out var catalogue));
        Assert.AreEqual(2, catalogue.Options.Single(o => o.Label == "Human").Initiative.DirectDice886);
        Assert.IsTrue(source.TryResolveCreationPrerequisiteAuthority(out var prerequisite));
        Assert.IsTrue(prerequisite.Options.SelectMany(o => o.HeritageOptions)
            .Any(o => o.MetatypeName == "Human" && o.IsEnabled && o.Blockers.Count == 0));
        foreach (string? version in HistoricalVersions905())
        {
            var old = operation.ForCreationSourceExecution(version)!.TryCreateContext(raw)!;
            Assert.IsTrue(old.TryResolveCreationMetatypeCatalog(out var oldCatalogue));
            Assert.IsFalse(oldCatalogue.Options.Any(o => o.Label == "Human" && o.IsEnabled && o.Blockers.Count == 0));
            Assert.AreSame(historical, historical.ForCreationSourceExecution(version));
        }
        Assert.IsNull(operation.ForCreationSourceExecution("chummer.creation-rule-source-execution.v918-unknown"));
        operation.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => operation.ForCreationSourceExecution(
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Initiative918_private_current_read_admits_v4_before_capture_with_one_owner_lease(bool books)
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: root =>
        {
            SelectBooks800(root, "core");
            ChangeHuman886(root, human => human.SetElementValue("initiativedice", 2));
        });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var stamp = owners.Capture();
        var exported = new WorkspaceContinuationExportService(context.Store, owners).Export(stamp, context.WorkspaceId);
        Assert.IsNotNull(exported.Value, exported.Error);
        byte[] wire = WorkspaceContinuationCodec.Encode(exported.Value!, WorkspacePrivateRuleSourceClosureBinding.MaximumContinuationBytes);
        byte[] before = File.ReadAllBytes(Record418(context));
        string root = Path.Combine(context.Directory, "source");
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        var reader = new WorkspaceContinuationCompletionReader(owners, resolver, context.Queries, LifeModules654());
        var observed = reader.EvaluateCompletionInputs(stamp, wire, wire.Length,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, (fresh, sources) =>
            {
                Assert.AreEqual(1, owners.ActiveLeases);
                Assert.IsTrue(fresh.Success, string.Join(",", fresh.AuthorityBlockers));
                Assert.IsNotNull(sources);
                Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, fresh.Inputs!.RuleSourceExecutionSchema);
                Assert.AreEqual(2, fresh.Inputs.PhysicalInitiative886!.BaseDice);
                Assert.AreEqual(books, fresh.Inputs.SourcebookSelection is not null);
                Assert.AreEqual(fresh.Inputs.RuleSourceCaptureDigest, fresh.Identity!.SourceDigest);
                byte[] archive = sources!.ExportPrivateInputs();
                Assert.AreEqual(fresh.Identity.SourceDigest, "sha256:" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(archive)).ToLowerInvariant());
                Assert.AreEqual(books, archive.AsSpan().StartsWith(System.Text.Encoding.ASCII.GetBytes("CHMR-SOURCEBOOKS-800\0")));
                Assert.IsFalse(fresh.HasPersistenceAuthority);
                return fresh;
            }, sourcebookSelection: books);
        Assert.AreEqual(exported.Value!.SnapshotDigest, observed.Identity!.ContinuationDigest);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Initiative925_committed_v4_draft_retry_reconciles_before_historical_default_refusal()
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905,
            prepareSource: root => ChangeHuman886(root, row => row.SetElementValue("initiativedice", 2)));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var stamp = owners.Capture();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var prepared = PrepareDraftInitiative886(context, store, owners);
        var command = prepared.Command;
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4,
            command.Review.Review.Request.RuleSourceExecutionSchema);
        var probe = new HistoricalOnlyRetryResolver925();
        ICharacterSourceDataResolver historical = probe; // Deliberately uses the interface default.
        Assert.IsNull(historical.ForCreationSourceExecution(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4));
        Assert.AreSame(historical, historical.ForCreationSourceExecution(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3));
        var unavailable = new OwnerBoundCharacterCreationCompletionService(store, owners, historical, context.Queries);
        var original = PersistedBytes925(context);
        var fresh = unavailable.ApplyDraftWithCampaign(stamp, command,
            CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply));
        Assert.IsNull(fresh.Value);
        CollectionAssert.Contains(fresh.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        Assert.IsNull(unavailable.ApplyDraftWithCampaign(stamp,
            DraftSchema925(command, "chummer.creation-rule-source-execution.v925-unsupported"),
            CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply)).Value);
        AssertPersistedBytes925(context, original);
        Assert.AreEqual(0, probe.LiveContextReads);
        Assert.AreEqual(0, owners.ActiveLeases);

        var receipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(stamp, command,
            CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply)));
        Assert.AreEqual(0, owners.ActiveLeases);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var persisted = PersistedBytes925(context);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(cold, owners, historical, context.Queries);
        int campaignReads = prepared.Authority.Reads;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var repeated = Success425(service.ApplyDraft(stamp, command));
            Assert.AreEqual(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(repeated));
            AssertPersistedBytes925(context, persisted);
            Assert.AreEqual(0, probe.LiveContextReads);
            Assert.AreEqual(0, owners.ActiveLeases);
        }
        Assert.AreEqual(campaignReads, prepared.Authority.Reads);
        Assert.IsNull(service.ApplyDraft(stamp, command with { IdempotencyKey = "initiative925-new-draft-key" }).Value);
        var changed = DraftSchema925(command, CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3);
        Assert.IsTrue(CharacterCreationCompletionDraftTransaction.IsConfirmed(changed));
        var conflict = service.ApplyDraft(stamp, changed);
        Assert.IsNull(conflict.Value);
        CollectionAssert.Contains(conflict.Blockers.ToArray(), CharacterCreationFinalizationBlockers.IdempotencyConflict);
        AssertPersistedBytes925(context, persisted);
        Assert.AreEqual(0, probe.LiveContextReads);
        Assert.AreEqual(0, owners.ActiveLeases);

        // An exact key is insufficient when the retained source is no longer authentic.
        string artifact = Artifact418(context);
        byte[] corrupted = File.ReadAllBytes(artifact);
        corrupted[^1] ^= 1;
        File.WriteAllBytes(artifact, corrupted);
        var damaged = PersistedBytes925(context);
        Assert.IsNull(service.ApplyDraft(stamp, command).Value);
        AssertPersistedBytes925(context, damaged);
        Assert.AreEqual(0, probe.LiveContextReads);
        Assert.AreEqual(0, owners.ActiveLeases);
        owners.Set(stamp.Owner); // Same owner value, different transition stamp.
        var staleOwner = service.ApplyDraft(stamp, command);
        Assert.IsNull(staleOwner.Value);
        CollectionAssert.Contains(staleOwner.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        AssertPersistedBytes925(context, damaged);
        Assert.AreEqual(0, probe.LiveContextReads);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    // Strict controller gate: setup must obtain genuine Complete authority before
    // any receipt exists. A partial fixture fails here; never reseal a None witness.
    [TestMethod]
    public void Initiative925_committed_v4_complete_retry_requires_genuine_complete_and_reconciles_before_refusal()
    {
        using var context = Ready886.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", wrapResolver: V4Sources905,
            prepareSource: root => ChangeHuman886(root, row => row.SetElementValue("initiativedice", 2)));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var stamp = owners.Capture();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        CharacterCreationCompletionApplyRequest command;
        using (CharacterCreationCompletionDraftTransaction.BeginValidationOperation())
        {
            var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
            var prepared = Success425(reader.LoadCompletionInputsWithInitiativeSourceExecution(stamp,
                new(context.WorkspaceId), includeLoadout: true));
            var proposal = SourceRequest696(prepared.Inputs) with
            { ProfileSettingsExecutionSchema = CharacterCreationCompletionReviewSchemas.ProfileSettingsExecutionV1 };
            Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, proposal.PolicyRequirement);
            Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, proposal.RuleSourceExecutionSchema);
            var reviewed = CharacterCreationCompletionReviewService.Review(prepared.Inputs, proposal, ruleSources: prepared.Execution);
            Assert.IsNotNull(reviewed.Value?.CompleteAssessment, string.Join(",", reviewed.Blockers));
            string diagnostic = "Complete setup refused; no committed-retry coverage: "
                + string.Join(",", reviewed.Blockers) + "; "
                + CompleteDiagnostic788(prepared.Inputs, reviewed.Value!.CompleteAssessment!);
            Assert.IsTrue(reviewed.Value.CompleteAssessment!.SatisfiesComplete, diagnostic);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, diagnostic);
            var cash = Success425(service.LoadStartingCash(stamp, context.WorkspaceId, proposal));
            var admitted = Success425(service.Review(stamp, context.WorkspaceId, proposal,
                new(cash.AuthorityDigest, cash.Dice * 3)));
            command = Command(admitted) with { IdempotencyKey = "initiative925-real-complete" };
        }
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsConfirmed(command));
        var probe = new HistoricalOnlyRetryResolver925();
        ICharacterSourceDataResolver historical = probe;
        Assert.IsNull(historical.ForCreationSourceExecution(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4));
        Assert.AreSame(historical, historical.ForCreationSourceExecution(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3));
        var unavailable = new OwnerBoundCharacterCreationCompletionService(store, owners, historical, context.Queries);
        var original = PersistedBytes925(context);
        var fresh = unavailable.Apply(stamp, command);
        Assert.IsNull(fresh.Value);
        CollectionAssert.Contains(fresh.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        Assert.IsNull(unavailable.Apply(stamp, command with { Proposal = command.Proposal with
            { RuleSourceExecutionSchema = "chummer.creation-rule-source-execution.v925-unsupported" } }).Value);
        AssertPersistedBytes925(context, original);
        Assert.AreEqual(0, probe.LiveContextReads);
        Assert.AreEqual(0, owners.ActiveLeases);

        var receipt = Success425(service.Apply(stamp, command));
        Assert.AreEqual(0, owners.ActiveLeases);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var persisted = PersistedBytes925(context);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = new OwnerBoundCharacterCreationCompletionService(cold, owners, historical, context.Queries);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var repeated = Success425(reopened.Apply(stamp, command));
            Assert.AreEqual(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(repeated));
            AssertPersistedBytes925(context, persisted);
            Assert.AreEqual(0, probe.LiveContextReads);
            Assert.AreEqual(0, owners.ActiveLeases);
        }
        var newKey = reopened.Apply(stamp, command with { IdempotencyKey = "initiative925-new-complete-key" });
        Assert.IsNull(newKey.Value);
        CollectionAssert.Contains(newKey.Blockers.ToArray(), CharacterCreationFinalizationBlockers.CharacterAlreadyCreated);
        var changed = command with { Proposal = command.Proposal with
            { RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 } };
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsConfirmed(changed));
        var conflict = reopened.Apply(stamp, changed);
        Assert.IsNull(conflict.Value);
        CollectionAssert.Contains(conflict.Blockers.ToArray(), CharacterCreationFinalizationBlockers.IdempotencyConflict);
        AssertPersistedBytes925(context, persisted);
        Assert.AreEqual(0, probe.LiveContextReads);
        Assert.AreEqual(0, owners.ActiveLeases);

        string artifact = Artifact418(context);
        byte[] corrupted = File.ReadAllBytes(artifact);
        corrupted[^1] ^= 1;
        File.WriteAllBytes(artifact, corrupted);
        var damaged = PersistedBytes925(context);
        Assert.IsNull(reopened.Apply(stamp, command).Value);
        AssertPersistedBytes925(context, damaged);
        Assert.AreEqual(0, probe.LiveContextReads);
        Assert.AreEqual(0, owners.ActiveLeases);
        owners.Set(stamp.Owner);
        var staleOwner = reopened.Apply(stamp, command);
        Assert.IsNull(staleOwner.Value);
        CollectionAssert.Contains(staleOwner.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        AssertPersistedBytes925(context, damaged);
        Assert.AreEqual(0, probe.LiveContextReads);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private sealed class HistoricalOnlyRetryResolver925 : ICharacterSourceDataResolver
    {
        public int LiveContextReads { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            LiveContextReads++;
            throw new InvalidOperationException("Historical-only retry must not read live source context.");
        }
    }

    private static CharacterCreationCompletionDraftApplyRequest DraftSchema925(
        CharacterCreationCompletionDraftApplyRequest command, string schema)
    {
        // Negative command mutation only; this is never used to obtain a receipt.
        var review = command.Review with { Review = command.Review.Review with
            { Request = command.Review.Review.Request with { RuleSourceExecutionSchema = schema } } };
        review = review with { DraftReviewDigest = CharacterCreationCompletionDraftReviewRules.ComputeDigest(review) };
        return command with { Review = review };
    }

    private static Dictionary<string, byte[]> PersistedBytes925(Ready886 context)
    {
        string root = Path.Combine(context.Directory, "workspaces");
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);
    }

    private static void AssertPersistedBytes925(Ready886 context, IReadOnlyDictionary<string, byte[]> expected)
    {
        var actual = PersistedBytes925(context);
        CollectionAssert.AreEqual(expected.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
            actual.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        foreach (var pair in expected) CollectionAssert.AreEqual(pair.Value, actual[pair.Key]);
    }

    private static string InitiativeUtf8Digest917(string value)
        => "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static CharacterCreationRuleSourcePreparation PrepareInitiative886(Ready886 context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithInitiativeSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.IsNotNull(loaded.Value);
        Assert.AreEqual(0, owners.ActiveLeases);
        return loaded.Value!;
    }

    private static (CharacterPhysicalInitiativeProjection Result, ObservedCapture839 Capture) PairInitiative886(
        Ready886 context, CharacterCreationRuleSourcePreparation prepared, CharacterCreationCompletionReviewRequest? proposal = null)
    {
        var request = proposal ?? SourceRequest696(prepared.Inputs);
        var full = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, full.Review.CompleteAssessment!.MechanicalSucceeded);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var witness = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.None }, ruleSources: prepared.Execution);
        Assert.IsNotNull(witness.Value, string.Join(",", witness.Blockers));
        var selected = AssessSelectedSources839(context, prepared, request, original, bootstrap, witness.Value!);
        var left = full.Capture.Invocations.Single();
        var right = selected.Capture.Invocations.Single();
        Assert.IsNotNull(left.PhysicalInitiative886);
        Assert.AreEqual(left.PhysicalInitiative886, right.PhysicalInitiative886);
        Assert.AreEqual(left.GraphDigest, right.GraphDigest);
        Assert.AreEqual(JsonSerializer.Serialize(full.Capture.Reports), JsonSerializer.Serialize(selected.Capture.Reports));
        Assert.IsFalse(left.PhysicalInitiative886!.Created);
        return (left.PhysicalInitiative886, full.Capture);
    }

    [TestMethod]
    [DataRow(1, -999, 5, 1)]
    [DataRow(3, -999, 5, 3)]
    [DataRow(3, 2, 5, 2)]
    [DataRow(1, 4, 2, 2)]
    [DataRow(3, 0, 5, 0)]
    public void Initiative886_full_selected_use_direct_base_fallback_and_ceiling(int minimum, int direct, int maximum, int expected)
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905,
            amendSettings: p => { p.SetElementValue("mininitiativedice", minimum); p.SetElementValue("maxinitiativedice", maximum); },
            prepareSource: root => { if (direct != -999) ChangeHuman886(root, row => row.SetElementValue("initiativedice", direct)); });
        var prepared = PrepareInitiative886(context);
        var observed = PairInitiative886(context, prepared);
        Assert.AreEqual(2, observed.Result.Value);
        Assert.AreEqual(expected, observed.Result.Dice);
        Assert.AreEqual(direct == -999 ? minimum : direct, observed.Result.BaseDice);
        var profile = observed.Capture.Reports.Single(r => r.Scope == "profile");
        foreach (string field in new[] { "mininitiativedice", "maxinitiativedice" })
            Assert.IsTrue(profile.Operations.Any(r => HasField832(r, field) && r.Disposition == D886.Compiled));
    }

    [TestMethod]
    [DataRow(12, 12, 20)]
    [DataRow(0, 1, 20)]
    [DataRow(0, 1, 1)]
    public void Initiative886_legacy_caps_preserve_mechanics_but_change_exact_source(int minimum, int maximum, int augmented)
    {
        using var baseline = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: _ => { });
        var before = PrepareInitiative886(baseline);
        var original = PairInitiative886(baseline, before);
        using var changed = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: root => ChangeHuman886(root, row =>
        { row.SetElementValue("inimin", minimum); row.SetElementValue("inimax", maximum); row.SetElementValue("iniaug", augmented); }));
        var after = PrepareInitiative886(changed);
        var observed = PairInitiative886(changed, after);
        Assert.AreEqual(original.Result.Value, observed.Result.Value);
        Assert.AreEqual(original.Result.Dice, observed.Result.Dice);
        Assert.AreNotEqual(before.Inputs.PhysicalInitiative886!.MetatypeSourceNodeDigest,
            after.Inputs.PhysicalInitiative886!.MetatypeSourceNodeDigest);
        Assert.AreNotEqual(before.Inputs.RuleSourceCaptureDigest, after.Inputs.RuleSourceCaptureDigest);
        var report = observed.Capture.Reports.Single(r => r.Scope == "metatypes");
        var classified = report.Operations.Where(o => o.Disposition == D886.LegacyNonOperative).ToArray();
        Assert.AreEqual(6, classified.Length, "Only the three scalar elements and their text occurrences qualify.");
        foreach (var row in classified)
        {
            Assert.IsNull(row.ExecutionDigest); Assert.IsNull(row.ConsumedValue718); Assert.IsNull(row.OperationResult718);
            Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, row.Classification886!.Schema);
            Assert.AreEqual(report.ObligationId, row.Classification886.ObligationId);
            Assert.IsTrue(row.ValidClassification886);
            var forged = row with { Classification886 = row.Classification886 with { Field = "initiativedice" } };
            Assert.IsFalse(forged.ValidClassification886);
            var deserialized = JsonSerializer.Deserialize<CharacterCreationSourceOperation>(JsonSerializer.Serialize(row))!;
            Assert.IsFalse(deserialized.ValidClassification886, "Resealed/serialized evidence has no invocation validation.");
        }
        var stale = CharacterCreationCompletionReviewService.Review(after.Inputs, SourceRequest696(before.Inputs), ruleSources: after.Execution);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, stale.Outcome);
    }

    [TestMethod]
    public void Initiative886_real_source_compilation_groups_dice_and_observes_paid_reaction()
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: root =>
            {
                // A selected, paid source mutation goes through the ordinary quality
                // authority/compiler. No improvement or invocation is fabricated.
                string path = Path.Combine(root, "data", "qualities.xml");
                var xml = XDocument.Load(path);
                xml.Root!.Element("qualities")!.Elements("quality").Single(q => q.Element("name")?.Value == "Ambidextrous")
                    .Element("bonus")!.Add(new XElement("initiative", "2.25"), new XElement("initiativedice", "0.10"),
                        new XElement("initiativedice", "0.25"), new XElement("initiativediceadd", "0.25"));
                xml.Save(path);
            });
        var prepared = PrepareInitiative886(context);
        var before = PairInitiative886(context, prepared);
        // trace878: Character.cs:29106,29157; DecimalExtensions.cs:42.
        // ceil(.25) + ceil(.25) = 2, unlike ceil(.25 + .25) = 1.
        Assert.AreEqual(5, before.Result.Value);
        Assert.AreEqual(3, before.Result.Dice);
        Assert.AreEqual(1, before.Result.DiceImprovement);
        Assert.AreEqual(1, before.Result.AdditiveDiceImprovement);
        var request = SourceRequest696(prepared.Inputs);
        var changed = request with { Selections = request.Selections with
        {
            AttributeAllocations = [new("REA", 0, 1)], ChangeableAttributeIds = ["REA"]
        } };
        var after = PairInitiative886(context, prepared, changed);
        Assert.AreEqual(6, after.Result.Value);
        Assert.AreEqual(before.Result.Dice, after.Result.Dice);
        Assert.AreEqual(before.Result.BaseDice, after.Result.BaseDice);
    }

    [TestMethod]
    public void Initiative886_cold_full_selected_finalization_and_resealed_authority_tamper()
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905,
            includeNonEmptyPurchases: true, qualityName: "Ambidextrous", prepareSource: root =>
            {
                ChangeHuman886(root, row => row.SetElementValue("initiativedice", 2));
                string path = Path.Combine(root, "data", "qualities.xml");
                var xml = XDocument.Load(path);
                xml.Root!.Element("qualities")!.Elements("quality").Single(q => q.Element("name")?.Value == "Ambidextrous")
                    .Element("bonus")!.Add(new XElement("initiative", "2.25"),
                        new XElement("initiativedice", "0.25"), new XElement("initiativediceadd", "0.25"));
                xml.Save(path);
            });
        var prepared = PrepareInitiative886(context);
        var request = SourceRequest696(prepared.Inputs) with { PolicyRequirement = CharacterCreationPolicyRequirement.None };
        var review = ReviewScopedSources839(prepared, request);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs, request,
            ruleSources: prepared.Execution);
        Assert.IsNotNull(captured.Value, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(prepared.Execution.ExportPrivateInputs(),
            prepared.Inputs.RuleSourceCaptureDigest!, out var retained));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request);
        Assert.IsNotNull(cash.Value, string.Join(",", cash.Blockers));
        var choice = new CharacterCreationStartingCashChoice(cash.Value!.AuthorityDigest, cash.Value.Dice * 3);
        var live = CharacterCreationCompletionFinalizationProjector.Project(original, bootstrap, prepared.Inputs, review,
            cash.Value, choice, ruleSources: prepared.Execution);
        Assert.IsNotNull(live.Value, string.Join(",", live.Blockers));
        byte[] tampered = prepared.Execution.ExportPrivateInputs();
        int capOffset = tampered.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("<inimin>2</inimin>"));
        Assert.IsTrue(capOffset >= 0);
        tampered[capOffset + "<inimin>".Length] = (byte)'3';
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(tampered,
            prepared.Inputs.RuleSourceCaptureDigest!, out _));
        string resealed = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(tampered));
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(tampered, resealed, out _));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var witness));
        var coldSources = new CharacterCreationRuleSourceExecutionContext(retained!, context.Queries);
        var full = CharacterCreationCompletionFinalizationProjector.Project(original, coldBootstrap!, prepared.Inputs, review,
            cash.Value, choice, ruleSources: coldSources);
        var selected = CharacterCreationCompletionTransaction.ProjectSelected(original, coldBootstrap!, witness!, request,
            cash.Value, choice, ruleSources: coldSources);
        Assert.IsNotNull(full.Value, string.Join(",", full.Blockers));
        Assert.IsNotNull(selected.Value, string.Join(",", selected.Blockers));
        Assert.AreEqual(live.Value!.CharacterXml, full.Value!.CharacterXml);
        Assert.AreEqual(full.Value.CharacterXml, selected.Value!.CharacterXml);
        Assert.AreEqual(full.Value.PhysicalInitiative886, selected.Value.PhysicalInitiative886);
        Assert.AreEqual(full.Value.ProjectionDigest, selected.Value.ProjectionDigest);
        foreach (var projection in new[] { live.Value!, full.Value!, selected.Value! })
        {
            Assert.AreEqual(5, projection.PhysicalInitiative886!.Value);
            Assert.AreEqual(4, projection.PhysicalInitiative886.Dice);
            Assert.AreEqual(2, projection.PhysicalInitiative886.BaseDice);
            Assert.AreEqual(1, projection.PhysicalInitiative886.DiceImprovement);
            Assert.AreEqual(1, projection.PhysicalInitiative886.AdditiveDiceImprovement);
            Assert.IsTrue(projection.PhysicalInitiative886.Created);
            var root = XDocument.Parse(projection.CharacterXml).Root!;
            Assert.AreEqual("2", root.Element("initiativedice")!.Value);
            // A second calculation from the saved graph cannot use improved dice as its base.
            Assert.AreEqual(projection.PhysicalInitiative886,
                CharacterCreationPhysicalInitiative886.Evaluate(root, prepared.Inputs.PhysicalInitiative886!));
        }
        var forged = witness! with { PhysicalInitiative886 = witness.PhysicalInitiative886! with { MaximumDice = 1 } };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap!, forged, request, ruleSources: coldSources).Outcome);
        var forgedDirect = witness! with { PhysicalInitiative886 = witness.PhysicalInitiative886! with
        { Metatype = witness.PhysicalInitiative886!.Metatype with { DirectDice886 = 4 } } };
        forgedDirect = forgedDirect with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forgedDirect) };
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap!, forgedDirect, request, ruleSources: coldSources).Outcome);
    }

    [TestMethod]
    public void Initiative886_historical_v1_v2_v3_wire_omission_and_repeatability_only()
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: _ => { });
        var first = new[] { PrepareSources696(context, legacy: true), PrepareSources696(context), PrepareSources832(context) };
        string[] before = first.Select(p => JsonSerializer.Serialize(p.Inputs)).ToArray();
        _ = PairInitiative886(context, PrepareInitiative886(context));
        var second = new[] { PrepareSources696(context, legacy: true), PrepareSources696(context), PrepareSources832(context) };
        CollectionAssert.AreEqual(before, second.Select(p => JsonSerializer.Serialize(p.Inputs)).ToArray());
        foreach (string wire in before) Assert.IsFalse(wire.Contains("PhysicalInitiative886", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Initiative886_lifecycle_wounds_unique_precedence_and_unsupported_shapes_are_explicit()
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: _ => { });
        var prepared = PrepareInitiative886(context);
        var authority = prepared.Inputs.PhysicalInitiative886!;
        var request = SourceRequest696(prepared.Inputs);
        var observed = ObserveSources839(context, prepared, request);
        var scoped = request with { PolicyRequirement = CharacterCreationPolicyRequirement.None };
        var review = ReviewScopedSources839(prepared, scoped);
        var complete = CharacterCreationCompleteAssessmentService.FromFullAuthority(prepared.Inputs, scoped);
        var effects = CharacterCreationSelectedEffectsConsumer.FromFull(prepared.Inputs, review, complete);
        Assert.IsNotNull(effects);
        var graph = effects!.CopyCompiledGraph();
        graph.Element("improvements")!.Add(Improvement886("Initiative", 2, "", "create"),
            Improvement886("Initiative", 7, "", "career"), Improvement886("InitiativeDice", 2, "precedence0"),
            Improvement886("InitiativeDice", 1, "precedence-1"), Improvement886("InitiativeDice", 10, "other"),
            Improvement886("InitiativeDice", 99, "precedence1", enabled: false));
        graph.SetElementValue("physicalcmfilled", 3);
        var creation = CharacterCreationPhysicalInitiative886.Evaluate(graph, authority);
        Assert.AreEqual(3, creation.Value); Assert.AreEqual(4, creation.Dice);
        graph.SetElementValue("created", "True");
        var career = CharacterCreationPhysicalInitiative886.Evaluate(graph, authority);
        Assert.AreEqual(8, career.Value); Assert.AreEqual(4, career.Dice);
        var improvements = graph.Element("improvements")!;
        improvements.RemoveNodes();
        graph.SetElementValue("created", "False");
        improvements.Add(Improvement886("InitiativeDice", .25m), Improvement886("InitiativeDiceAdd", .25m));
        var fractional = CharacterCreationPhysicalInitiative886.Evaluate(graph, authority);
        Assert.AreEqual(3, fractional.Dice); // 1 + ceil(.25) + ceil(.25)
        Assert.AreEqual(1, fractional.DiceImprovement);
        Assert.AreEqual(1, fractional.AdditiveDiceImprovement);
        improvements.RemoveNodes();
        improvements.Add(Improvement886("InitiativeDice", -1.25m), Improvement886("InitiativeDiceAdd", -.25m));
        var negative = CharacterCreationPhysicalInitiative886.Evaluate(graph, authority);
        Assert.AreEqual(-2, negative.Dice); // 1 + floor(-1.25) + floor(-.25); no lower clamp.
        improvements.RemoveNodes();
        improvements.Add(Improvement886("Initiative", .25m, condition: "create"),
            Improvement886("Initiative", 9m, condition: "career"),
            Improvement886("Initiative", 99m, enabled: false),
            Improvement886("Initiative", 99m, addToRating: true),
            Improvement886("InitiativeDice", .25m, "precedence1"),
            Improvement886("InitiativeDice", 1.25m, "precedence1"),
            Improvement886("InitiativeDice", .25m, "precedence-1"),
            Improvement886("InitiativeDice", 99m, "ordinary"),
            Improvement886("InitiativeDice", 99m, "precedence0", enabled: false),
            Improvement886("InitiativeDice", 99m, "precedence0", addToRating: true));
        var precedenceOne = CharacterCreationPhysicalInitiative886.Evaluate(graph, authority);
        Assert.AreEqual(2, precedenceOne.Value); // REA1 + INT1 - wound1 + ceil(.25)
        Assert.AreEqual(3, precedenceOne.Dice); // 1 + ceil(.25 + 1.25 + .25)
        graph.SetElementValue("created", "True");
        Assert.AreEqual(10, CharacterCreationPhysicalInitiative886.Evaluate(graph, authority).Value);
        graph.Element("improvements")!.Add(Improvement886("DisableQuality", 0));
        Assert.ThrowsExactly<InvalidDataException>(() => CharacterCreationPhysicalInitiative886.Evaluate(graph, authority));
        var row = observed.Capture.Reports.Single(r => r.Scope == "metatypes").Operations.First(r => r.Classification886 is not null);
        foreach (var forged in new[] { row with { Path = row.Path + "/@x" }, row with { Order = row.Order + 1 },
            row with { SourceDigest = new string('a', 64) }, row with { ExecutionDigest = new string('b', 64) } })
            Assert.IsFalse(forged.ValidClassification886);
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    [DataRow("attributed")]
    [DataRow("malformed")]
    [DataRow("direct-dice-malformed")]
    public void Initiative886_invalid_selected_source_is_rejected_by_fresh_bootstrap(string mutation)
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: _ => { });
        string sourceRoot = Path.Combine(context.Directory, "source");
        ChangeHuman886(sourceRoot, row =>
        {
            switch (mutation)
            {
                case "unknown": row.Add(new XElement("initiative886unknown", 1)); break;
                case "duplicate": row.Add(new XElement("inimin", 2)); break;
                case "attributed": row.Element("inimin")!.SetAttributeValue("x", 1); break;
                case "malformed": row.SetElementValue("inimax", "oops"); break;
                default: row.SetElementValue("initiativedice", "1 + Rating"); break;
            }
        });
        var resolver = V4Sources905(new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(sourceRoot, sourceRoot, null)));
        var codec = new Sr5WorkspaceCodec(context.Queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(resolver)), new XmlCharacterMetadataCommands(new CharacterFileService()));
        var bootstrap = new CharacterCreationBootstrapService(context.Store, new RulesetWorkspaceCodecResolver([codec]), context.Queries, resolver);
        var result = bootstrap.Create(new CharacterCreationBootstrapRequest(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5, "Initiative886", "Invalid source",
            CharacterCreationBuildMethods.Priority, CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Invalid, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public void Initiative886_private_draft_paid_rebase_retains_v4_and_reopens_without_live_sources()
    {
        using var context = Ready886.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: root =>
            ChangeHuman886(root, row => row.SetElementValue("initiativedice", 2)));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraftInitiative886(context, store, owners);
        var receipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var saved = Read839(store, owners, context.WorkspaceId);
        byte[] originalEntry = JsonSerializer.SerializeToUtf8Bytes(saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts![0]);
        var closure = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes);
        var imported = WorkspacePrivateRuleSourceClosure.Import(closure.ExportPrivateBytes(), closure.Binding,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, context.Queries);
        int observedBase = imported.EvaluateCompletionInputs(owners, owners.Capture(), context.Resolver, context.Queries,
            LifeModules654(), CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, (fresh, sources, binding) =>
            {
                Assert.IsTrue(fresh.Success, string.Join(",", fresh.AuthorityBlockers));
                Assert.AreEqual(closure.Binding, binding);
                Assert.IsNotNull(sources);
                Assert.AreEqual(fresh.Inputs!.RuleSourceCaptureDigest, fresh.Identity!.SourceDigest);
                Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, fresh.Inputs.RuleSourceExecutionSchema);
                return fresh.Inputs.PhysicalInitiative886!.BaseDice;
            });
        Assert.AreEqual(2, observedBase);
        var factory = new CampaignFactory774(owners);
        var rebase = Rebase774(context, store, owners, factory);
        var state = Success425(rebase.Load(owners.Capture(), new(context.WorkspaceId)));
        var requested = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "REA" ? 1 : 0))).ToArray();
        var preview = Success425(rebase.PreviewRebase(owners.Capture(), new(state.Binding, requested)));
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, preview.Review.Review.Request.RuleSourceExecutionSchema);
        var changed = Success425(rebase.ConfirmRebase(owners.Capture(), preview, "initiative886-rebase", true));
        var after = store.Get(context.WorkspaceId).Value!;
        var entries = after.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        Assert.AreEqual(2, entries.Count);
        CollectionAssert.AreEqual(originalEntry, JsonSerializer.SerializeToUtf8Bytes(entries[0]));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(entries[1].Baseline.SelectedReplay, out _, out var witness));
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, witness!.RuleSourceExecutionSchema);
        Assert.AreEqual(2, witness.PhysicalInitiative886!.BaseDice);
        // Draft/rebase commits source XML unchanged; its selected graph is materialized on replay.
        Assert.AreEqual(saved.Document.Content, after.Document.Content);
        Assert.IsNull(XDocument.Parse(after.Document.Content).Root!.Element("initiativedice"));
        Assert.IsTrue(entries[1].Baseline.Review.Review.Skills!.Attributes.CreationKarmaBudget.Used
            > entries[0].Baseline.Review.Review.Skills!.Attributes.CreationKarmaBudget.Used);
        byte[] beforeMaterialization = File.ReadAllBytes(Record418(context));
        var paid = MaterializeRetainedInitiative926(context, store, owners, after);
        CollectionAssert.AreEqual(beforeMaterialization, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(2, paid.Reaction);
        Assert.AreEqual(1, paid.Intuition);
        Assert.AreEqual(3, paid.Value); // paid REA rebase: 2 + 1, independently of source caps.
        Assert.AreEqual(2, paid.Dice);
        byte[] newEntry = JsonSerializer.SerializeToUtf8Bytes(entries[1]);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        factory.ForbidObservation = true;
        byte[] beforeCold = File.ReadAllBytes(Record418(context));
        var retainedFiles = IdentityFiles809(context.Directory);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = ObserveDraftReplays841(count =>
        {
            var read = cold.Get(context.WorkspaceId);
            Assert.IsTrue(read.Success, read.Error);
            Assert.IsTrue(count() > 0, "Cold Get must replay the persisted Draft history.");
            return read;
        });
        Assert.AreEqual(after.Document.Content, reopened.Value!.Document.Content);
        // Decode and recover again from the cold store; do not reuse the warm witness or graph.
        Assert.AreEqual(paid, MaterializeRetainedInitiative926(context, cold, owners, reopened.Value));
        CollectionAssert.AreEqual(beforeCold, File.ReadAllBytes(Record418(context)));
        CollectionAssert.AreEqual(originalEntry, JsonSerializer.SerializeToUtf8Bytes(
            reopened.Value.Document.AuxiliaryState.CharacterCreationCompletionDrafts![0]));
        CollectionAssert.AreEqual(new[] { receipt, changed }, reopened.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!
            .Select(entry => entry.Receipt).ToArray());
        CollectionAssert.AreEqual(newEntry, JsonSerializer.SerializeToUtf8Bytes(reopened.Value.Document.AuxiliaryState.CharacterCreationCompletionDrafts![1]));
        byte[] beforeRetry = File.ReadAllBytes(Record418(context));
        Assert.AreEqual(changed, Success425(Rebase774(context, cold, owners, factory)
            .ConfirmRebase(owners.Capture(), preview, "initiative886-rebase", true)));
        CollectionAssert.AreEqual(beforeRetry, File.ReadAllBytes(Record418(context)));
        AssertIdentityFiles809(context.Directory, retainedFiles);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static CharacterPhysicalInitiativeProjection MaterializeRetainedInitiative926(
        Ready886 context, FileWorkspaceStore store,
        CharacterCreationAttributesServiceTests.AllocationOwners owners, WorkspaceStoredDocument stored)
    {
        // Read only the real store's private manifest. The production recovery port validates
        // its references against owner/workspace/incarnation and the retained artifact bytes.
        using var record = JsonDocument.Parse(File.ReadAllBytes(Record418(context)));
        var manifest = record.RootElement.GetProperty("RetainedRuleSources");
        var references = manifest.GetProperty("References")
            .Deserialize<CharacterCreationRetainedRuleSourceReference[]>()!;
        var campaigns = manifest.GetProperty("CommittedDraftCampaigns")
            .Deserialize<CharacterCreationCommittedCampaign[]>()!;
        return store.WithRetainedRuleSources(owners.Capture().Owner, stored.Id, context.Queries, references, recovery =>
        {
            foreach (var committed in campaigns) recovery.RetainCommittedDraftCampaign(committed);
            using var history = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
            Assert.IsTrue(CharacterCreationCompletionDraftTransaction.IsValidLedger(
                stored.Id, stored.ContentRevision, stored.Document.AuxiliaryState, recovery));
            var entries = stored.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
            var entry = entries[^1];
            var baseline = entry.Baseline;
            Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(
                baseline.SelectedReplay, out var bootstrap, out var witness));
            var request = baseline.Review.Review.Request;
            Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, request.RuleSourceExecutionSchema);
            Assert.AreEqual(request.RuleSourceExecutionSchema, witness!.RuleSourceExecutionSchema);
            Assert.AreEqual(2, witness.PhysicalInitiative886!.BaseDice);
            // Reconstruct the exact archived origin and prefix, as IsValidLedger does.
            // No XML fields, selections, digests or Complete evidence are manufactured.
            var priorState = baseline.OriginalState with
            { CharacterCreationCompletionDrafts = entries.Count == 1 ? null : entries.Take(entries.Count - 1).ToArray() };
            Assert.AreEqual(entry.Receipt.PreviousAuxiliaryStateDigest, WorkspaceDocumentAuxiliaryStateDigest.Compute(priorState));
            var document = new WorkspaceDocument(baseline.RawCharacterXml, RulesetDefaults.Sr5);
            document = document with { State = document.State with { AuxiliaryState = priorState } };
            var original = new WorkspaceStoredDocument(stored.Id, document, entry.Receipt.PreviousContentRevision,
                entry.Receipt.PreviousSavedRevision, DateTimeOffset.UnixEpoch);
            Assert.IsTrue(recovery.TryRecoverRuleSources(request.RuleSourceCaptureDigest!, out var sources));
            Assert.IsNotNull(sources);
            Assert.IsNotNull(request.CampaignAuthority);
            Assert.IsTrue(recovery.TryRecoverCampaignAuthority(request.CampaignAuthority!, entry.Receipt.ReceiptDigest, out var campaign));
            Assert.IsTrue(campaign is { IsCommittedReplay: true, Phase: CharacterCreationCampaignPhase.Replay });
            Assert.IsTrue(CharacterCreationAttributesReplacementExecution.TryEnterReplay(
                original, baseline.Review, witness, out var replacement));
            using (replacement)
            {
                var selected = Success425(CharacterCreationSelectedCompletionEvaluator.Replay(
                    original, bootstrap!, witness, request, ruleSources: sources, campaign: campaign));
                Assert.IsTrue(CharacterCreationSelectedCompletionEvaluator.MatchesFullReview(selected, baseline.Review.Review));
                Assert.IsTrue(CharacterCreationSelectedCompletionEvaluator.TryReadOriginal(
                    original, bootstrap!, witness, out var originalSelections, out var originalEffects));
                var authority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(
                    witness, request, originalSelections!, originalEffects!);
                Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromSelected(
                    original, bootstrap!, witness, request, out var originalCharacter));
                var effects = CharacterCreationSelectedEffectsConsumer.FromSelected(witness, request, selected, authority,
                    original.Document.AuxiliaryState.CharacterCreationLoadoutDraft?.Instances, originalCharacter: originalCharacter);
                Assert.IsNotNull(effects);
                var graph = effects!.CopyCompiledGraph();
                Assert.AreEqual("2", graph.Element("initiativedice")!.Value);
                var result = CharacterCreationPhysicalInitiative886.Evaluate(graph, witness.PhysicalInitiative886);
                Assert.AreEqual(2, result.BaseDice);
                Assert.IsFalse(result.Created);
                Assert.IsTrue(recovery.ValidateRetainedRuleSources());
                return result;
            }
        });
    }

    private static PreparedDraft550 PrepareDraftInitiative886(Ready886 context, FileWorkspaceStore store,
        CharacterCreationAttributesServiceTests.AllocationOwners owners)
    {
        WorkspaceStoredDocument original;
        CampaignAuthority518 authority;
        CharacterCreationCompletionReviewRequest proposal;
        using (CharacterCreationCompletionDraftTransaction.BeginValidationOperation())
        {
            var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
            var prepared = Success425(reader.LoadCompletionInputsWithInitiativeSourceExecution(owners.Capture(),
                new(context.WorkspaceId), includeLoadout: true));
            original = Read839(store, owners, context.WorkspaceId);
            var inputs = prepared.Inputs;
            var binding = new CharacterCreationCampaignBinding(CharacterCreationCampaignBinding.SchemaV1,
                new(owners.Capture().Owner.Value, original.Id, original.ContentRevision, original.SavedRevision,
                    Hash518("initiative886-continuation")[7..], inputs.RuleSourceCaptureDigest!, inputs.Skills.Binding.RawCharacterXmlDigest,
                    original.Document.AuxiliaryStateDigest, inputs.SnapshotDigest, inputs.EffectivePolicy!.PolicyDigest,
                    Hash518("initiative886-observation")), Hash518("initiative886-transition-" + original.ContentRevision));
            authority = new(binding, CharacterCreationCampaignState.AdmittedUnchangedPolicy);
            proposal = SourceRequest696(inputs) with { PolicyRequirement = CharacterCreationPolicyRequirement.None, CampaignAuthority = binding };
            Success425(CharacterCreationCompletionReviewService.Review(inputs, proposal, ruleSources: prepared.Execution,
                campaign: CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Preparation)));
        }
        var reviewed = Success425(DraftService550(context, store, owners).ReviewDraftWithCampaign(owners.Capture(),
            context.WorkspaceId, proposal, CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Review)));
        return new(original, authority, new(CharacterCreationCompletionDraftApplyRequest.SchemaV1, reviewed, "initiative886-draft", true));
    }

    private static ICharacterSourceDataResolver V4Sources905(ICharacterSourceDataResolver resolver)
        => resolver.ForCreationSourceExecution(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            ?? throw new InvalidOperationException("Missing explicit V4 source admission.");

    private static XElement Improvement886(string type, decimal value, string unique = "", string condition = "", bool enabled = true, bool addToRating = false)
        => new("improvement", new XElement("improvementttype", type), new XElement("val", value),
            new XElement("unique", unique), new XElement("condition", condition), new XElement("enabled", enabled ? 1 : 0),
            new XElement("addtorating", addToRating ? 1 : 0));

    private static void ChangeHuman886(string root, Action<XElement> change)
    {
        string path = Path.Combine(root, "data", "metatypes.xml");
        var document = XDocument.Load(path);
        change(document.Root!.Element("metatypes")!.Elements("metatype").Single(row => row.Element("name")?.Value == "Human"));
        document.Save(path);
    }
    private const string HistoricalSource905 = """<quality><id>68cfe94a-fa7e-4129-a9b9-b5d73e3ced99</id><name>Ambidextrous</name><karma>4</karma><category>Positive</category><limit>{arm} - 1</limit><bonus><ambidextrous /></bonus><source>SR5</source><page>71</page></quality>""";
    private const string HistoricalSourceDigest905 = "sha256:377dad37ac1493bc6b4f4c1d2370e4bb4795f8d559ff31cd4a265a7e7da35be2";
    private const string HistoricalSeed905 = "sha256:7777777777777777777777777777777777777777777777777777777777777777";

    private static CharacterCreationQualitySelection HistoricalSelection905(string xml = HistoricalSource905)
        => new("quality:68cfe94a-fa7e-4129-a9b9-b5d73e3ced99:rating:1",
            Guid.Parse("68cfe94a-fa7e-4129-a9b9-b5d73e3ced99"), "68cfe94a-fa7e-4129-a9b9-b5d73e3ced99",
            "Ambidextrous", CharacterCreationQualityType.Positive, 1, 4, false, true, true, false, null, null,
            ["qualities.xml#quality:68cfe94a-fa7e-4129-a9b9-b5d73e3ced99"], xml,
            CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml), "sha256:507b342d1704a948b8e4e6388a83312837e88969fc290278607c18f0752634cb")
        { ResolvedArmCount = 2 };

    private static string?[] HistoricalVersions905() => [null,
        CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
        CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
        CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3];

    [TestMethod]
    public void Initiative905_historical_success_matches_independent_before_graph_and_wire_oracle()
    {
        // Static expected bytes were independently lowered from saved BEFORE code.
        // They are not generated by, or compared to another run of, the new evaluator.
        const string expected = """<character><qualities><quality><sourceid>68cfe94a-fa7e-4129-a9b9-b5d73e3ced99</sourceid><guid>e63476fc-6f3e-5ed6-a4de-128331b0b10d</guid><name>Ambidextrous</name><extra></extra><bp>4</bp><implemented>True</implemented><contributetobp>True</contributetobp><contributetolimit>True</contributetolimit><stagedpurchase>False</stagedpurchase><doublecareer>True</doublecareer><canbuywithspellpoints>False</canbuywithspellpoints><metagenic>False</metagenic><print>True</print><qualitytype>Positive</qualitytype><qualitysource>Selected</qualitysource><mutant>False</mutant><source>SR5</source><page>71</page><sourcename></sourcename><bonus><ambidextrous /></bonus><firstlevelbonus /><naturalweapons /><notes></notes><notesColor>#000000</notesColor></quality></qualities><improvements><improvement><target></target><improvedname></improvedname><sourcename>e63476fc-6f3e-5ed6-a4de-128331b0b10d</sourcename><min>0</min><max>0</max><aug>0</aug><augmax>0</augmax><val>0</val><rating>1</rating><exclude></exclude><condition></condition><improvementttype>Ambidextrous</improvementttype><improvementsource>Quality</improvementsource><custom>False</custom><customname></customname><customid></customid><customgroup></customgroup><addtorating>0</addtorating><enabled>1</enabled><order>0</order><notes></notes><notesColor>#000000</notesColor></improvement></improvements></character>""";
        var selection = HistoricalSelection905();
        Assert.AreEqual(HistoricalSourceDigest905, selection.SourceNodeDigest);
        Assert.AreEqual("sha256:1ff88a374a0cb61c05c27018dd0acdb31c43239fdfcfd39f03f6919d719f49b2", InitiativeUtf8Digest917(JsonSerializer.Serialize(selection)));
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, HistoricalSeed905,
            out var oldQualities, out var oldImprovements));
        Assert.AreEqual(expected, new XElement("character", new XElement("qualities", oldQualities),
            new XElement("improvements", oldImprovements)).ToString(SaveOptions.DisableFormatting));
        foreach (string? version in HistoricalVersions905())
        {
            Assert.IsTrue(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(HistoricalSource905, 2, version));
            Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, HistoricalSeed905,
                out var qualities, out var improvements, 2, version));
            string graph = new XElement("character", new XElement("qualities", qualities),
                new XElement("improvements", improvements)).ToString(SaveOptions.DisableFormatting);
            Assert.AreEqual(expected, graph);
            Assert.AreEqual("sha256:e7ee079ad1c18a1790874aae362323b0546dc6642ea2db8429b5c0a1742e094a", InitiativeUtf8Digest917(graph));
        }
    }

    [TestMethod]
    public void Initiative905_historical_new_quality_instructions_remain_rejected()
    {
        foreach (string instruction in new[] { "initiative", "initiativedice", "initiativepass", "initiativediceadd", "initiativepassadd" })
        {
            var node = XElement.Parse(HistoricalSource905);
            node.Element("bonus")!.Add(new XElement(instruction, ".25"));
            string xml = node.ToString(SaveOptions.DisableFormatting);
            Assert.IsFalse(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(xml, 2));
            var selection = HistoricalSelection905(xml);
            Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, HistoricalSeed905, out _, out _));
            foreach (string? version in HistoricalVersions905())
            {
                Assert.IsFalse(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(xml, 2, version));
                Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, HistoricalSeed905, out _, out _, 2, version));
            }
            const string v4 = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4;
            Assert.IsTrue(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(xml, 2, v4));
            var admitted = selection with { SourceExecutionSchema = v4 };
            Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(admitted, HistoricalSeed905, out _, out _));
            Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(admitted, HistoricalSeed905, out _, out _, 2, v4));
        }
    }

    [TestMethod]
    public void Initiative905_catalogue_and_prerequisite_admission_are_versioned_before_drafts()
    {
        using var context = Ready886.Create(includeGearReview: true, prepareSource: sourceRoot =>
        {
            string qualitiesPath = Path.Combine(sourceRoot, "data", "qualities.xml");
            var qualitiesXml = XDocument.Load(qualitiesPath);
            qualitiesXml.Root!.Element("qualities")!.Elements("quality").Single(q => q.Element("name")?.Value == "Ambidextrous")
                .Element("bonus")!.Add(new XElement("initiative", "0.25"));
            qualitiesXml.Save(qualitiesPath);
        });
        var baseline = PrepareSources832(context);
        string root = Path.Combine(context.Directory, "source");
        string raw = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        ICharacterSourceDataResolver resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        foreach (string? version in HistoricalVersions905().Append(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4))
        {
            var source = resolver.ForCreationSourceExecution(version)!.TryCreateContext(raw)!;
            Assert.IsTrue(source.TryResolveCreationQualitiesAuthority(baseline.Inputs.Skills.PrerequisiteDraft, out var qualities));
            var option = qualities.Options.First(q => q.Name == "Ambidextrous");
            bool v4 = version == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4;
            Assert.AreEqual(v4, option.IsSelectable);
            Assert.AreEqual(v4 ? version : null, option.SourceExecutionSchema);
            Assert.AreEqual(option.OptionDigest, CharacterCreationQualitiesRules.ComputeOptionDigest(option));
        }
        ChangeHuman886(root, human => human.SetElementValue("initiativedice", 2));
        // Fresh views cannot reuse a projection from the previous source grammar.
        resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        foreach (string? version in HistoricalVersions905().Append(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4))
        {
            var source = resolver.ForCreationSourceExecution(version)!.TryCreateContext(raw)!;
            Assert.IsTrue(source.TryResolveCreationMetatypeCatalog(out var catalogue));
            Assert.IsTrue(source.TryResolveCreationPrerequisiteAuthority(out var prerequisite));
            bool v4 = version == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4;
            Assert.AreEqual(v4, catalogue.Options.Any(o => o.Label == "Human" && o.IsEnabled && o.Blockers.Count == 0));
            Assert.AreEqual(v4, prerequisite.Options.SelectMany(o => o.HeritageOptions)
                .Any(o => o.MetatypeName == "Human" && o.IsEnabled && o.Blockers.Count == 0));
            if (v4) Assert.AreEqual(2, catalogue.Options.Single(o => o.Label == "Human").Initiative.DirectDice886);
        }
        // Downshifting a V4 resolver explicitly returns historical admission, too.
        var downgraded = V4Sources905(resolver).ForCreationSourceExecution(null)!.TryCreateContext(raw)!;
        Assert.IsTrue(downgraded.TryResolveCreationMetatypeCatalog(out var historical));
        Assert.IsFalse(historical.Options.Any(o => o.Label == "Human" && o.IsEnabled && o.Blockers.Count == 0));
    }

}
