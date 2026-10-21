using System.Security.Cryptography;
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
using Ready1025 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    private const string Packet1025 = "/docker/chummercomplete-active-worktrees/completion-ooda-20261017.9w0jGkLw/core-draft-07.bAZkTMhA/.artifacts/lifestyle-upgrade1025.odHPGJ90";
    private const string OldCompiler1025 = "/docker/chummercomplete-active-worktrees/completion-ooda-20261017.9w0jGkLw/core-draft-07.bAZkTMhA/.artifacts/lifestyle-source1000.e4u0UH/overlay/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs";
    private const string OldCompilerHash1025 = "700d9735d2fae996e38c5b2685a103f6937552a75ec30eba84b6a0676f790f21";
    private const string OldInfrastructure1025 = "b80a9011db295eb8f107cb15faa76ebf9da673422fe29a14cd06208a0d3abdd9";
    private const string OldApplication1025 = "728cc69d08afb4a28f9e975d134fd8702183dbb2b6bd4116f855ae5790a69f23";
    private const string OldContracts1025 = "be3ff38659ce48cf7a0c8d58dc697c6a97b97ca3469e855787f997f9a874a9b9";
    private static string Fixture1025 => Path.Combine(Packet1025, "fixtures", "pre1008");

    public sealed record UpgradeManifest1025(string CompilerSha256, string InfrastructureSha256,
        string ApplicationSha256, string ContractsSha256, CharacterWorkspaceId WorkspaceId,
        IReadOnlyDictionary<string, string> Files);

#if LIFESTYLE_UPGRADE1025_OLD_WRITER
    // Compile this entrypoint into the existing test harness against the retained
    // compiled1022 assemblies. Hash checks prohibit a same-reader substitute.
    [TestMethod]
    public void Upgrade1025_export_pre1008_fixture()
    {
        Assert.AreEqual(OldCompilerHash1025, HashFile1025(OldCompiler1025));
        Assert.AreEqual(OldInfrastructure1025, HashFile1025(typeof(FileSystemCharacterSourceDataResolver).Assembly.Location));
        Assert.AreEqual(OldApplication1025, HashFile1025(typeof(CharacterCreationLifestylesService).Assembly.Location));
        Assert.AreEqual(OldContracts1025, HashFile1025(typeof(CharacterCreationLifestylesAuthority).Assembly.Location));
        Assert.IsFalse(Directory.Exists(Fixture1025), "Immutable export already exists; never overwrite it.");
        using var context = Ready1025.Create(includeGearReview: true, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "lifestyles.xml");
            var document = XDocument.Load(path);
            document.Root!.Element("cities")!.Add(new XElement("city", new XElement("name", "Harbor 1025"),
                new XElement("district", new XElement("name", "North 1025"),
                    new XElement("borough", new XElement("name", "Quay 1025"), new XElement("secRating", "C")))));
            document.Save(path);
        });
        var service = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
        Assert.IsFalse(JsonSerializer.Serialize(state.Authority).Contains("\"LocationCatalog\"", StringComparison.Ordinal));
        var low = state.Authority.LifestyleOptions.Single(row => row.Name == "Low");
        var home = new CharacterCreationLifestyleConfiguration(Guid.Parse("cbcc1025-0000-4000-8000-000000000001"),
            low.OptionId, "Old confirmed home", CharacterCreationLifestyleStyleIds.Standard, low.DefaultIncrementId,
            1, 100m, 0, false, false, 0, 0, 0, 0, "Harbor 1025", "North 1025", "Quay 1025", []);
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create, home.LifestyleId, home);
        var preview = service.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var request = new CharacterCreationLifestyleConfirmRequest(state.Binding, mutation, preview.PreviewDigest,
            "upgrade1025-original", true);
        var applied = service.Confirm(request);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, applied.Outcome, string.Join(",", applied.Blockers));
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        CopyTree1025(context.Directory, Path.Combine(Fixture1025, "workspace"));
        WriteJson1025("request.json", request);
        WriteJson1025("receipt.json", applied.Value!);
        WriteJson1025("old-authority.json", state.Authority);
        WriteJson1025("old-draft.json", saved.Document.AuxiliaryState.CharacterCreationLifestylesDraft!);
        WriteJson1025("old-state.json", service.Load(new(context.WorkspaceId)).Value!);
        // Prove the copied files reopen under the actual old reader before sealing.
        // This also detects any path-bound source identity that would invalidate a copy.
        var copiedStore = new FileWorkspaceStore(Path.Combine(Fixture1025, "workspace"));
        var copied = new CharacterCreationLifestylesService(copiedStore, Resolver1025(Path.Combine(Fixture1025, "workspace")));
        var reopened = copied.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(reopened.CanEdit, string.Join(",", reopened.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(copiedStore.Get(context.WorkspaceId).Value));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Replayed, copied.Confirm(request).Outcome);
        var files = Directory.GetFiles(Fixture1025, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(Fixture1025, path), HashFile1025);
        WriteJson1025("manifest.json", new UpgradeManifest1025(OldCompilerHash1025, OldInfrastructure1025,
            OldApplication1025, OldContracts1025, context.WorkspaceId, files));
        File.WriteAllText(Path.Combine(Fixture1025, "manifest.sha256"), HashFile1025(Path.Combine(Fixture1025, "manifest.json")));
    }
#else
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Upgrade1025_old_writer_new_reader_requires_review_then_edits_and_completion_work(bool changeChoices)
    {
        using var run = Import1025();
        var before = run.Read();
        string unchanged = JsonSerializer.Serialize(before);
        var oldRequest = ReadFixture1025<CharacterCreationLifestyleConfirmRequest>("request.json");
        var oldDraft = ReadFixture1025<CharacterCreationLifestylesDraft>("old-draft.json");
        var oldAuthority = ReadFixture1025<CharacterCreationLifestylesAuthority>("old-authority.json");
        var oldState = ReadFixture1025<CharacterCreationLifestylesState>("old-state.json");
        var state = run.Service.Load(new(run.Id)).Value!;
        Assert.IsFalse(state.CanEdit);
        Assert.IsTrue(state.Blockers.SequenceEqual([CharacterCreationLifestylesBlockers.AuthorityReReviewRequired]));
        Assert.AreEqual(JsonSerializer.Serialize(oldDraft), JsonSerializer.Serialize(state.AuthorityReview!.HistoricalDraft));
        Assert.AreEqual(JsonSerializer.Serialize(oldState.Budget), JsonSerializer.Serialize(state.Budget));
        Assert.AreEqual(JsonSerializer.Serialize(oldDraft.Lifestyles), JsonSerializer.Serialize(state.Lifestyles.Select(row => row.Configuration)));
        Assert.AreEqual(oldAuthority.AuthorityDigest, state.Authority.AuthorityDigest);
        Assert.IsNotNull(state.AuthorityReview.CurrentAuthority);
        Assert.AreNotEqual(state.Authority.AuthorityDigest, state.AuthorityReview.CurrentAuthority.AuthorityDigest);
        AssertOldRetry1025(run, oldRequest);
        var ordinaryMutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Edit,
            oldDraft.Lifestyles[0].LifestyleId, oldDraft.Lifestyles[0] with { Name = "Must wait" });
        Assert.IsFalse(run.Service.Preview(new(state.Binding, ordinaryMutation)).Value!.CanConfirm);
        Assert.IsNull(run.Service.LoadCompletionInputs(new(run.Id)).Value);
        var choices = oldDraft.Lifestyles.Select(row => changeChoices ? row with { Increments = row.Increments + 1 } : row).ToArray();
        var mutation = ReviewMutation1025(state, choices);
        var preview = run.Service.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(oldState.Budget), JsonSerializer.Serialize(preview.BudgetBefore));
        Assert.AreEqual(preview.BudgetBefore.Total, preview.BudgetAfter.Total);
        Assert.AreNotEqual(oldDraft.SourceAuthorityDigest, preview.WritePlan.PendingDraft!.SourceAuthorityDigest);
        Assert.IsNotNull(preview.WritePlan.AuthorityTransition!.CurrentAuthority.LocationCatalog);
        Assert.AreEqual(JsonSerializer.Serialize(choices), JsonSerializer.Serialize(preview.WritePlan.PendingDraft.Lifestyles));
        var request = new CharacterCreationLifestyleConfirmRequest(state.Binding, mutation, preview.PreviewDigest,
            "upgrade1025-review", false);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Blocked, run.Service.Confirm(request).Outcome);
        Assert.AreEqual(unchanged, JsonSerializer.Serialize(run.Read()));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var confirmed = changeChoices
            ? new OwnerBoundCharacterCreationLifestylesService(run.Store, owners, run.Resolver, run.Queries)
                .Confirm(owners.Capture(), request with { ExplicitlyConfirmed = true })
            : run.Service.Confirm(request with { ExplicitlyConfirmed = true });
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        Assert.AreNotEqual(state.AuthorityReview.HistoricalReceiptDigest, confirmed.Value!.ReceiptDigest);
        Assert.AreEqual(File.ReadAllText(Path.Combine(Fixture1025, "receipt.json")),
            JsonSerializer.Serialize(run.Read().Document.AuxiliaryState.CharacterCreationLifestyleReceipts![0].Receipt));
        var admitted = run.Read();
        Assert.AreEqual(before.Document.Content, admitted.Document.Content);
        Assert.AreEqual(before.ContentRevision + 1, admitted.ContentRevision);
        Assert.AreEqual(JsonSerializer.Serialize(before.Document.AuxiliaryState.CharacterCreationResourcesDraft),
            JsonSerializer.Serialize(admitted.Document.AuxiliaryState.CharacterCreationResourcesDraft));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Replayed, run.Service.Confirm(request with { ExplicitlyConfirmed = true }).Outcome);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Conflict, run.Service.Confirm(request with
        {
            ExplicitlyConfirmed = true,
            Mutation = mutation with { AuthorityReview = mutation.AuthorityReview! with { Lifestyles = [] } }
        }).Outcome);
        Assert.AreEqual(JsonSerializer.Serialize(admitted), JsonSerializer.Serialize(run.Read()));
        AssertOldRetry1025(run, oldRequest);
        var editable = run.Service.Load(new(run.Id)).Value!;
        Assert.IsTrue(editable.CanEdit, string.Join(",", editable.Blockers));
        Assert.IsNull(editable.AuthorityReview);
        Assert.IsNotNull(editable.Lifestyles[0].Location);
        Assert.IsNotNull(run.Service.LoadCompletionInputs(new(run.Id)).Value);
        Assert.IsNotNull(new CharacterCreationResourcesService(run.Store, run.Resolver, run.Queries).LoadCompletionInputs(new(run.Id)).Value);
        var selection = editable.Lifestyles[0].Configuration;
        var edit = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Edit, selection.LifestyleId,
            selection with { Name = "After explicit review" });
        var editPreview = run.Service.Preview(new(editable.Binding, edit)).Value!;
        Assert.IsTrue(editPreview.CanConfirm, string.Join(",", editPreview.Blockers));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, run.Service.Confirm(new(editable.Binding, edit,
            editPreview.PreviewDigest, "upgrade1025-later-edit", true)).Outcome);
        AssertOldRetry1025(run, oldRequest);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Replayed, run.Service.Confirm(request with { ExplicitlyConfirmed = true }).Outcome);
        Assert.IsNotNull(run.Service.LoadCompletionInputs(new(run.Id)).Value);
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("profile")]
    [DataRow("selected-cost")]
    [DataRow("selected-bonus")]
    public void Upgrade1025_source_and_profile_drift_refuse_without_writes(string change)
    {
        using var run = Import1025();
        var state = run.Service.Load(new(run.Id)).Value!;
        var mutation = ReviewMutation1025(state, state.AuthorityReview!.HistoricalDraft.Lifestyles);
        var preview = run.Service.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm);
        string before = JsonSerializer.Serialize(run.Read());
        string path = Path.Combine(run.Directory, "source", "data", "lifestyles.xml");
        if (change == "profile")
        {
            path = Path.Combine(run.Directory, "source", "data",
                CharacterCreationBootstrapProfiles.SettingsSourceFile(state.Authority.SettingsProfileId));
            File.AppendAllText(path, "\n<!-- changed profile input -->\n");
        }
        else if (change == "source") File.AppendAllText(path, "\n<!-- changed source input -->\n");
        else
        {
            var xml = XDocument.Load(path);
            var low = xml.Root!.Element("lifestyles")!.Elements("lifestyle").Single(row => row.Element("name")!.Value == "Low");
            if (change == "selected-cost") low.SetElementValue("cost", "2001");
            else low.Add(new XElement("bonus", new XElement("lifestylecost", "-1")));
            xml.Save(path);
        }
        Assert.AreNotEqual(CharacterCreationLifestyleOutcomes.Applied, run.Service.Confirm(new(state.Binding, mutation,
            preview.PreviewDigest, "upgrade1025-drift", true)).Outcome);
        var fresh = new CharacterCreationLifestylesService(run.Store, Resolver1025(run.Directory));
        Assert.IsNull(fresh.Load(new(run.Id)).Value!.AuthorityReview);
        Assert.AreEqual(before, JsonSerializer.Serialize(run.Read()));
    }

    [TestMethod]
    [DataRow("revision")]
    [DataRow("workspace")]
    [DataRow("receipt")]
    [DataRow("selection")]
    [DataRow("budget")]
    public void Upgrade1025_changed_review_binding_or_intent_refuses_without_writes(string change)
    {
        using var run = Import1025();
        var state = run.Service.Load(new(run.Id)).Value!;
        var mutation = ReviewMutation1025(state, state.AuthorityReview!.HistoricalDraft.Lifestyles);
        var preview = run.Service.Preview(new(state.Binding, mutation)).Value!;
        var binding = state.Binding;
        if (change == "revision") binding = binding with { ContentRevision = binding.ContentRevision - 1 };
        if (change == "workspace") binding = binding with { WorkspaceId = new("upgrade1025-foreign") };
        if (change == "receipt") mutation = mutation with { AuthorityReview = mutation.AuthorityReview! with { HistoricalReceiptDigest = "sha256:" + new string('0', 64) } };
        if (change == "selection") mutation = mutation with { AuthorityReview = mutation.AuthorityReview! with
            { Lifestyles = [mutation.AuthorityReview.Lifestyles[0] with { Name = "Unreviewed" }] } };
        if (change == "budget") mutation = mutation with { AuthorityReview = mutation.AuthorityReview! with
            { Lifestyles = [mutation.AuthorityReview.Lifestyles[0] with { Increments = 10000 }] } };
        string before = JsonSerializer.Serialize(run.Read());
        Assert.AreNotEqual(CharacterCreationLifestyleOutcomes.Applied, run.Service.Confirm(new(binding, mutation,
            preview.PreviewDigest, "upgrade1025-invalid", true)).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(run.Read()));
    }

    [TestMethod]
    public void Upgrade1025_owner_partition_and_stale_owner_refuse_without_writes()
    {
        using var run = Import1025();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var local = owners.Capture();
        var service = new OwnerBoundCharacterCreationLifestylesService(run.Store, owners, run.Resolver, run.Queries);
        var state = service.Load(local, new(run.Id)).Value!;
        var mutation = ReviewMutation1025(state, state.AuthorityReview!.HistoricalDraft.Lifestyles);
        var preview = service.Preview(local, new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm);
        var request = new CharacterCreationLifestyleConfirmRequest(state.Binding, mutation, preview.PreviewDigest, "upgrade1025-owner", true);
        string before = JsonSerializer.Serialize(run.Read());
        owners.Set(new OwnerScope("upgrade1025-other-owner"));
        Assert.AreNotEqual(CharacterCreationLifestyleOutcomes.Applied, service.Confirm(local, request).Outcome);
        Assert.AreNotEqual(CharacterCreationLifestyleOutcomes.Applied, service.Confirm(owners.Capture(), request).Outcome);
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.AreEqual(before, JsonSerializer.Serialize(run.Read()));
    }

    [TestMethod]
    [DataRow("untyped-copy")]
    [DataRow("authority-metadata")]
    [DataRow("authority-cost")]
    [DataRow("historical-receipt")]
    [DataRow("balance")]
    public void Upgrade1025_copied_and_resealed_transition_lookalikes_refuse(string change)
    {
        using var run = Import1025();
        var state = run.Service.Load(new(run.Id)).Value!;
        var mutation = ReviewMutation1025(state, state.AuthorityReview!.HistoricalDraft.Lifestyles);
        var preview = run.Service.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm);
        preview = JsonSerializer.Deserialize<CharacterCreationLifestylePreview>(JsonSerializer.Serialize(preview))!;
        if (change.StartsWith("authority-", StringComparison.Ordinal))
        {
            var authority = preview.WritePlan.AuthorityTransition!.CurrentAuthority;
            if (change == "authority-metadata") authority = authority with { SourceAnchorIds = [.. authority.SourceAnchorIds, "copied-authority"] };
            else
            {
                string selected = preview.WritePlan.PendingDraft!.Lifestyles[0].BaseLifestyleOptionId;
                var options = authority.LifestyleOptions.Select(option =>
                {
                    if (option.OptionId != selected) return option;
                    var changed = option with { BaseCost = option.BaseCost + 1m };
                    return changed with { OptionDigest = CharacterCreationLifestylesRules.ComputeOptionDigest(changed) };
                }).ToArray();
                authority = authority with { LifestyleOptions = options };
            }
            authority = authority with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(authority) };
            Assert.IsTrue(CharacterCreationLifestylesRules.IsValidAuthority(authority));
            var draft = preview.WritePlan.PendingDraft! with { SourceAuthorityDigest = authority.AuthorityDigest,
                ProjectionAuthority = CharacterCreationKarmaLifestylesRules.ProjectionAuthority(authority, preview.WritePlan.PendingDraft.Lifestyles) };
            draft = draft with { DraftDigest = CharacterCreationLifestylesDraftRules.Digest(draft) };
            decimal used = 0;
            foreach (var choice in draft.Lifestyles)
            {
                Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(choice, authority, out var row, out _));
                used += row.Economics.TotalCost;
            }
            preview = preview with
            {
                WritePlan = preview.WritePlan with { PendingDraft = draft,
                    AuthorityTransition = preview.WritePlan.AuthorityTransition with { CurrentAuthority = authority } },
                BudgetAfter = preview.BudgetAfter with { Used = used, Remaining = preview.BudgetAfter.Total - used }
            };
        }
        if (change == "historical-receipt") preview = preview with { WritePlan = preview.WritePlan with
            { AuthorityTransition = preview.WritePlan.AuthorityTransition! with { HistoricalReceiptDigest = "sha256:" + new string('0', 64) } } };
        preview = preview with { WritePlan = preview.WritePlan with { PlanDigest = CharacterCreationLifestylesRules.ComputePlanDigest(preview.WritePlan) } };
        if (change == "balance") preview = preview with { BudgetAfter = preview.BudgetAfter with { Remaining = preview.BudgetAfter.Remaining + 1m } };
        preview = preview with { PreviewDigest = CharacterCreationLifestylesRules.ComputePreviewDigest(preview) };
        var observed = run.Read();
        var replacement = ReviewReplacement1025(observed, preview, mutation);
        if (change is "untyped-copy" or "authority-metadata" or "authority-cost")
        {
            var receipts = replacement.AuxiliaryState.CharacterCreationLifestyleReceipts!;
            Assert.IsTrue(CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(run.Id, observed.ContentRevision + 1, receipts));
            Assert.IsTrue(CharacterCreationLifestyleReceiptLedgerIntegrity.HasValidContentTransition(receipts[^1], observed.Document, replacement));
            Assert.AreEqual(change == "untyped-copy", run.Service.AdmitsAuthorityReviewCommit(observed, preview, replacement));
        }
        string before = JsonSerializer.Serialize(run.Read());
        var result = change == "untyped-copy"
            ? ((IWorkspaceAuxiliaryStateAtomicCommitCapability)run.Store).ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                run.Id, state.Binding.ContentRevision, state.Binding.AuxiliaryStateDigest, replacement)
            : ((ICharacterCreationLifestyleAuthorityReviewCommitCapability)run.Store).CommitLifestyleAuthorityReview(
                OwnerScope.LocalSingleUser, replacement, preview, run.Resolver);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(before, JsonSerializer.Serialize(run.Read()));
    }

    [TestMethod]
    public void Upgrade1025_source_drift_after_flush_refuses_before_replacement()
    {
        using var run = Import1025();
        var state = run.Service.Load(new(run.Id)).Value!;
        var mutation = ReviewMutation1025(state, state.AuthorityReview!.HistoricalDraft.Lifestyles);
        var preview = run.Service.Preview(new(state.Binding, mutation)).Value!;
        var fault = new UpgradeSourceDrift1025(Path.Combine(run.Directory, "source", "data", "lifestyles.xml"));
        var store = new FileWorkspaceStore(run.Directory, fault);
        var service = new CharacterCreationLifestylesService(store, run.Resolver);
        string before = JsonSerializer.Serialize(run.Read());
        Assert.AreNotEqual(CharacterCreationLifestyleOutcomes.Applied, service.Confirm(new(state.Binding, mutation,
            preview.PreviewDigest, "upgrade1025-final-source-fence", true)).Outcome);
        Assert.AreEqual(1, fault.Calls);
        Assert.AreEqual(before, JsonSerializer.Serialize(run.Read()));
    }

    private sealed class UpgradeSourceDrift1025(string sourcePath) : IFileWorkspaceStoreFaultInjector
    {
        public int Calls { get; private set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed) return;
            Calls++;
            File.AppendAllText(sourcePath, "\n<!-- changed after flush -->\n");
        }
    }

    private static WorkspaceDocument ReviewReplacement1025(WorkspaceStoredDocument workspace,
        CharacterCreationLifestylePreview preview, CharacterCreationLifestyleMutation mutation)
    {
        var request = new CharacterCreationLifestyleConfirmRequest(preview.Binding, mutation, preview.PreviewDigest, "upgrade1025-forgery", true);
        string key = CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest("chummer.sr5.creation-lifestyles.idempotency.v1\0" + request.IdempotencyKey);
        string command = CharacterCreationLifestylesRules.ComputeCommandDigest(request);
        long next = workspace.ContentRevision + 1;
        var receipt = new CharacterCreationLifestyleReceipt(CharacterCreationLifestylesSchemas.ReceiptV1,
            "creation-lifestyle-" + command["sha256:".Length..][..24], CharacterCreationWizardStepIds.ContactsLifestyles,
            workspace.Id, mutation.MutationKind, mutation.LifestyleId, key, command, workspace.ContentRevision, next,
            workspace.ContentRevision, next, workspace.SavedRevision, next, preview.WritePlan.ContentDigestBefore,
            preview.WritePlan.ContentDigestAfter, preview.Binding.SourceDigest, preview.Binding.RulesDigest,
            preview.Binding.RuntimeDigest, preview.BudgetBefore.Used, preview.BudgetAfter.Used, preview.BudgetAfter.Remaining,
            preview.WritePlan, string.Empty);
        receipt = receipt with { ReceiptDigest = CharacterCreationLifestylesRules.ComputeReceiptDigest(receipt) };
        return workspace.Document with { State = workspace.Document.State with { AuxiliaryState = workspace.Document.AuxiliaryState with
        {
            CharacterCreationLifestylesDraft = preview.WritePlan.PendingDraft,
            CharacterCreationLifestyleReceipts = [.. workspace.Document.AuxiliaryState.CharacterCreationLifestyleReceipts!, new(key, command, receipt)]
        } } };
    }

    [TestMethod]
    [DataRow("full-digest")]
    [DataRow("projection")]
    public void Upgrade1025_resealed_historical_authority_is_not_an_old_compiler_capability(string change)
    {
        using var run = Import1025();
        var saved = run.Read();
        var draft = saved.Document.AuxiliaryState.CharacterCreationLifestylesDraft!;
        if (change == "full-digest") draft = draft with { SourceAuthorityDigest = "sha256:" + new string('0', 64) };
        else
        {
            var authority = draft.ProjectionAuthority with { SourceAnchorIds = [.. draft.ProjectionAuthority.SourceAnchorIds, "copied-old-authority"] };
            authority = authority with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(authority) };
            draft = draft with { ProjectionAuthority = authority };
        }
        draft = draft with { DraftDigest = CharacterCreationLifestylesDraftRules.Digest(draft) };
        var entry = saved.Document.AuxiliaryState.CharacterCreationLifestyleReceipts!.Single();
        var plan = entry.Receipt.WritePlan with { PendingDraft = draft };
        plan = plan with { PlanDigest = CharacterCreationLifestylesRules.ComputePlanDigest(plan) };
        var receipt = entry.Receipt with { WritePlan = plan };
        receipt = receipt with { ReceiptDigest = CharacterCreationLifestylesRules.ComputeReceiptDigest(receipt) };
        var copied = saved with { Document = saved.Document with { State = saved.Document.State with
        { AuxiliaryState = saved.Document.AuxiliaryState with
        { CharacterCreationLifestylesDraft = draft, CharacterCreationLifestyleReceipts = [entry with { Receipt = receipt }] } } } };
        // A syntactically valid, rehashed snapshot is still not historical source authority.
        Assert.IsTrue(CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(run.Id, saved.ContentRevision,
            copied.Document.AuxiliaryState.CharacterCreationLifestyleReceipts));
        var reader = new CharacterCreationLifestylesService(new WorkspaceContinuationReadView(copied), run.Resolver);
        var result = reader.Load(new(run.Id)).Value!;
        Assert.IsNull(result.AuthorityReview);
        Assert.IsTrue(result.Blockers.Contains(CharacterCreationLifestylesBlockers.AuthorityUnavailable));
        Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(run.Read()));
    }

    [TestMethod]
    public void Upgrade1025_fixed_compilers_have_separate_caches_on_the_same_source_context()
    {
        using var run = Import1025();
        var context = run.Resolver.TryCreateContext(run.Read().Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationLifestylesAuthority(out var current));
        Assert.IsTrue(context.TryResolveCreationLifestylesPre1008Authority(out var old));
        Assert.IsNotNull(current.LocationCatalog);
        Assert.IsNull(old.LocationCatalog);
        Assert.IsNull(old.Categories);
        Assert.IsTrue(old.QualityOptions.All(option => option.SelectionSemantics is null));
        Assert.AreNotEqual(current.AuthorityDigest, old.AuthorityDigest);
        Assert.AreEqual(current.SourceDigest, old.SourceDigest);
        Assert.AreEqual(current.ProfileDigest, old.ProfileDigest);
        Assert.IsTrue(context.TryResolveCreationLifestylesAuthority(out var currentAgain));
        Assert.IsTrue(context.TryResolveCreationLifestylesPre1008Authority(out var oldAgain));
        Assert.AreEqual(JsonSerializer.Serialize(current), JsonSerializer.Serialize(currentAgain));
        Assert.AreEqual(JsonSerializer.Serialize(old), JsonSerializer.Serialize(oldAgain));
    }

    private static CharacterCreationLifestyleMutation ReviewMutation1025(CharacterCreationLifestylesState state,
        IReadOnlyList<CharacterCreationLifestyleConfiguration> choices)
        => new(CharacterCreationLifestyleMutationKinds.ReReviewAuthority, Guid.Empty, null)
        { AuthorityReview = new(state.AuthorityReview!.HistoricalReceiptDigest, choices) };

    private static void AssertOldRetry1025(UpgradeRun1025 run, CharacterCreationLifestyleConfirmRequest request)
    {
        string before = JsonSerializer.Serialize(run.Read());
        var replay = run.Service.Confirm(request);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Replayed, replay.Outcome);
        string old = File.ReadAllText(Path.Combine(Fixture1025, "receipt.json"));
        Assert.AreEqual(old, JsonSerializer.Serialize(replay.Value));
        Assert.AreEqual(old, JsonSerializer.Serialize(run.Service.LookupReceipt(new(run.Id, request.IdempotencyKey)).Value));
        Assert.AreEqual(before, JsonSerializer.Serialize(run.Read()));
    }

    private static UpgradeRun1025 Import1025()
    {
        string manifestPath = Path.Combine(Fixture1025, "manifest.json");
        Assert.IsTrue(File.Exists(manifestPath), "Run the bounded pre1008 exporter against the retained old assemblies first.");
        Assert.AreEqual(File.ReadAllText(Path.Combine(Fixture1025, "manifest.sha256")), HashFile1025(manifestPath));
        var manifest = ReadFixture1025<UpgradeManifest1025>("manifest.json");
        Assert.AreEqual(OldCompilerHash1025, manifest.CompilerSha256);
        Assert.AreEqual(OldInfrastructure1025, manifest.InfrastructureSha256);
        Assert.AreEqual(OldApplication1025, manifest.ApplicationSha256);
        Assert.AreEqual(OldContracts1025, manifest.ContractsSha256);
        Assert.AreNotEqual(OldInfrastructure1025, HashFile1025(typeof(FileSystemCharacterSourceDataResolver).Assembly.Location));
        foreach (var file in manifest.Files)
        {
            Assert.IsFalse(Path.IsPathRooted(file.Key) || file.Key.Split(Path.DirectorySeparatorChar).Contains(".."));
            Assert.AreEqual(file.Value, HashFile1025(Path.Combine(Fixture1025, file.Key)), file.Key);
        }
        string directory = Path.Combine(Packet1025, "runs", Guid.NewGuid().ToString("N"));
        CopyTree1025(Path.Combine(Fixture1025, "workspace"), directory);
        return new(directory, manifest.WorkspaceId);
    }

    private sealed class UpgradeRun1025 : IDisposable
    {
        public UpgradeRun1025(string directory, CharacterWorkspaceId id)
        {
            Directory = directory; Id = id; Store = new(directory); Resolver = Resolver1025(directory);
            Queries = new XmlCharacterFileQueries(new CharacterFileService());
            Service = new(Store, Resolver, Queries);
        }
        public string Directory { get; }
        public CharacterWorkspaceId Id { get; }
        public FileWorkspaceStore Store { get; }
        public ICharacterSourceDataResolver Resolver { get; }
        public ICharacterFileQueries Queries { get; }
        public CharacterCreationLifestylesService Service { get; }
        public WorkspaceStoredDocument Read() => Store.Get(Id).Value!;
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
#endif

    private static FileSystemCharacterSourceDataResolver Resolver1025(string workspaceRoot)
    {
        string root = Path.Combine(workspaceRoot, "source");
        return new(new FileSystemContentOverlayCatalogService(root, root, null));
    }
    private static T ReadFixture1025<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Fixture1025, name)))!;
    private static void WriteJson1025<T>(string name, T value) => File.WriteAllText(Path.Combine(Fixture1025, name), JsonSerializer.Serialize(value));
    private static string HashFile1025(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void CopyTree1025(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: false);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(path));
        }
    }
}
