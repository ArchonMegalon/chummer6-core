using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Owners809 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // Real FileWorkspaceStore/source history; campaign policy is an explicit Core
    // test authority. Android's real owner/signer adapter is covered separately.
    [TestMethod]
    public void Identity809_retained_draft_rebase_and_cold_observation_preserve_private_export_restriction_local()
        => AssertRetainedDraftIdentity809(false);

    [TestMethod]
    public void Identity809_retained_draft_rebase_and_cold_observation_preserve_private_export_restriction_linked()
        => AssertRetainedDraftIdentity809(true);

    private static void AssertRetainedDraftIdentity809(bool linked)
    {
        IdentityPhase809(linked, "fixture-start");
        using var context = PricingContext747(savedMagic: false);
        string directory = linked ? Path.Combine(context.Directory, "identity809-linked") : context.Directory;
        var owners = new Owners809();
        var id = context.WorkspaceId;
        if (linked)
        {
            var setup = CharacterCreationCompletionApplyTests.CreateLinkedPrerequisite(context, directory);
            owners = setup.Owners; id = setup.Id;
        }
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(directory, context.Queries);
        IdentityPhase809(linked, "draft-prepare");
        var first = PrepareDraft550(context, store, owners, workspaceId: linked ? id : null);
        IdentityPhase809(linked, "draft-apply");
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var observer = new WorkspaceLocalIdentityObservationService(store, owners);
        var beforeFiles = IdentityFiles809(directory);
        IdentityPhase809(linked, "observe-before");
        var before = Observe809(observer, owners.Capture(), id);
        Assert.IsNotNull(before.CompletionDraftIncludesLoadout);
        Assert.AreEqual(!linked, before.UnimportedLocalHistoryDigest is not null);
        Assert.IsFalse(new WorkspaceContinuationExportService(store, owners).Export(owners.Capture(), id).Success);
        // Independently compare the canonical digest once. Production observation
        // never creates this carrier, and the later-edit Export798 test is unchanged.
        IdentityPhase809(linked, "private-digest-comparison");
        var closure = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), id,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes);
        Assert.AreEqual(closure.Binding.ContinuationDigest, before.ContinuationDigest);
        AssertIdentityFiles809(directory, beforeFiles);
        var old = linked ? store.Get(owners.Capture().Owner, id).Value! : store.Get(id).Value!;
        byte[] firstEntry = JsonSerializer.SerializeToUtf8Bytes(old.Document.AuxiliaryState.CharacterCreationCompletionDrafts![0]);
        var factory = new IdentityFactory809(store, owners, before);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver,
            new(context.Queries, factory));
        IdentityPhase809(linked, "attribute-load");
        var state = Success425(attributes.Load(owners.Capture(), new(id)));
        var allocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        IdentityPhase809(linked, "attribute-preview");
        var preview = Success425(attributes.PreviewRebase(owners.Capture(), new(state.Binding, allocations)));
        AssertIdentityFiles809(directory, beforeFiles);
        IdentityPhase809(linked, "attribute-confirm");
        var receipt = Success425(attributes.ConfirmRebase(owners.Capture(), preview, "identity809-rebase", true));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(directory, context.Queries);
        IdentityPhase809(linked, "cold-observe");
        var after = Observe809(new(cold, owners), owners.Capture(), id);
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreNotEqual(before.ContinuationDigest, after.ContinuationDigest);
        Assert.AreEqual(before.UnimportedLocalHistoryDigest, after.UnimportedLocalHistoryDigest);
        var saved = linked ? cold.Get(owners.Capture().Owner, id).Value! : cold.Get(id).Value!;
        CollectionAssert.AreEqual(firstEntry,
            JsonSerializer.SerializeToUtf8Bytes(saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts![0]));
        Assert.AreEqual(receipt, saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1].Receipt);
        Assert.IsFalse(new WorkspaceContinuationExportService(cold, owners).Export(owners.Capture(), id).Success);
        var committed = IdentityFiles809(directory);
        // Old preparation cannot authorize a second revision, even if Core is loaded afresh.
        IdentityPhase809(linked, "second-preparation");
        var current = Success425(attributes.Load(owners.Capture(), new(id)));
        var nextAllocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels)).ToArray();
        Assert.IsNull(attributes.PreviewRebase(owners.Capture(), new(current.Binding, nextAllocations)).Value);
        AssertIdentityFiles809(directory, committed);
        factory.Expected = after;
        ObserveDraftReplays841(count =>
        {
            int? admittedReplays = null;
            factory.AfterObservation841 = () => admittedReplays = count();
            try
            {
                var fresh = Success425(attributes.PreviewRebase(owners.Capture(), new(current.Binding, nextAllocations)));
                Assert.IsTrue(admittedReplays is > 0, "The real retained history must have been replayed before review.");
                Assert.AreEqual(admittedReplays!.Value, count(),
                    "The nested Finish reviewer must reuse this operation's exact verified prefixes.");
                return fresh;
            }
            finally { factory.AfterObservation841 = null; }
        });
        AssertIdentityFiles809(directory, committed);
        if (linked)
        {
            owners.Set(new OwnerScope("  " + owners.Capture().Owner.NormalizedValue.ToUpperInvariant() + "  "));
            Assert.AreEqual(after, Observe809(new(cold, owners), owners.Capture(), id));
        }
        Assert.AreEqual(0, owners.ActiveLeases);
        IdentityPhase809(linked, "complete");
    }

    private static void IdentityPhase809(bool linked, string phase)
    {
        string? path = Environment.GetEnvironmentVariable("CHUMMER_IDENTITY809_PHASE_PATH");
        if (!string.IsNullOrWhiteSpace(path))
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {(linked ? "linked" : "local")} {phase}{Environment.NewLine}");
    }

    [TestMethod]
    [DataRow("missing-source")]
    [DataRow("corrupt-source")]
    [DataRow("missing-campaign")]
    [DataRow("ambiguous-record")]
    [DataRow("legacy-record")]
    [DataRow("unconfigured-reader")]
    public void Identity809_invalid_retained_history_is_unavailable_without_rewriting(string damage)
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners809();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        if (damage == "missing-source") File.Delete(Artifact418(context));
        if (damage == "corrupt-source") File.WriteAllBytes(Artifact418(context), [0, 1, 2, 3]);
        if (damage is "missing-campaign" or "legacy-record")
        {
            var record = JsonNode.Parse(File.ReadAllText(Record418(context)))!.AsObject();
            if (damage == "missing-campaign") record["RetainedRuleSources"]!["CommittedDraftCampaigns"] = new JsonArray();
            else record["RecordSchemaVersion"] = 1;
            File.WriteAllText(Record418(context), record.ToJsonString());
        }
        if (damage == "ambiguous-record")
        {
            string text = File.ReadAllText(Record418(context));
            File.WriteAllText(Record418(context), text.Insert(text.IndexOf('{') + 1, "\"RecordSchemaVersion\":5,"));
        }
        var before = IdentityFiles809(context.Directory);
        var cold = damage == "unconfigured-reader" ? new FileWorkspaceStore(context.Directory)
            : FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Assert.IsFalse(new WorkspaceLocalIdentityObservationService(cold, owners).Observe(owners.Capture(), context.WorkspaceId).Success);
        AssertIdentityFiles809(context.Directory, before);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Identity809_current_admission_drift_cannot_reuse_observed_history_source() =>
        AssertCurrentAdmissionDrift809("source");

    [TestMethod]
    public void Identity809_current_admission_drift_cannot_reuse_observed_history_policy() =>
        AssertCurrentAdmissionDrift809("policy");

    [TestMethod]
    public void Identity809_current_admission_drift_cannot_reuse_observed_history_owner_aba() =>
        AssertCurrentAdmissionDrift809("owner-aba");

    [TestMethod]
    public void Identity809_current_admission_drift_cannot_reuse_observed_history_cancel() =>
        AssertCurrentAdmissionDrift809("cancel");

    private static void AssertCurrentAdmissionDrift809(string drift)
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners809();
        var stamp = owners.Capture();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(stamp, first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var observer = new WorkspaceLocalIdentityObservationService(store, owners);
        var identity = Observe809(observer, stamp, context.WorkspaceId);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver,
            new(context.Queries, new IdentityFactory809(store, owners, identity)));
        var state = Success425(attributes.Load(stamp, new(context.WorkspaceId)));
        var allocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        var preview = Success425(attributes.PreviewRebase(stamp, new(state.Binding, allocations)));
        using var cancel = new CancellationTokenSource();
        if (drift is "source" or "policy")
        {
            string path = Path.Combine(context.Directory, "source", "data", drift == "policy"
                ? CharacterCreationBootstrapProfiles.SettingsSourceFile(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
                : "qualities.xml");
            if (drift == "source") File.AppendAllText(path, "\n<!-- identity809 current source drift -->\n");
            else
            {
                var xml = System.Xml.Linq.XDocument.Load(path);
                xml.Root!.Element("settings")!.Elements("setting")
                    .Single(row => row.Element("id")!.Value == CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
                    .Element("karmacost")!.SetElementValue("karmaattribute", 6);
                xml.Save(path);
            }
            // Retained historical identity is still valid, but grants no current source/policy permission.
            Assert.AreEqual(identity, Observe809(observer, stamp, context.WorkspaceId));
        }
        if (drift == "owner-aba")
        {
            owners.Set(new OwnerScope("identity809-foreign"));
            Assert.IsFalse(observer.Observe(owners.Capture(), context.WorkspaceId).Success);
            owners.Set(stamp.Owner);
            Assert.IsFalse(observer.Observe(stamp, context.WorkspaceId).Success);
        }
        if (drift == "cancel") cancel.Cancel();
        var before = IdentityFiles809(context.Directory);
        if (drift == "cancel")
        {
            Assert.ThrowsExactly<OperationCanceledException>(() => observer.Observe(stamp, context.WorkspaceId, cancel.Token));
            Assert.ThrowsExactly<OperationCanceledException>(() =>
                attributes.ConfirmRebase(stamp, preview, "identity809-cancel", true, cancel.Token));
        }
        else Assert.IsNull(attributes.ConfirmRebase(stamp, preview, "identity809-drift", true).Value);
        AssertIdentityFiles809(context.Directory, before);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Identity841_public_draft_review_replays_history_in_each_fresh_operation(bool sourcebookSelection)
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners809();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners, sourcebookSelection: sourcebookSelection);
        var service = DraftService550(context, store, owners);
        Success425(service.ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var next = PrepareDraft550(context, store, owners, sourcebookSelection: sourcebookSelection);
        int entries = next.Original.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Count;
        var before = IdentityFiles809(context.Directory);
        // A caller-owned ambient memo must not authorize either public review.
        using (CharacterCreationCompletionDraftTransaction.EnterValidationOperation())
        {
            Assert.IsTrue(store.Get(context.WorkspaceId).Success);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var review = ObserveDraftReplays841(count =>
                {
                    var result = Success425(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId,
                        next.Command.Review.Review.Request,
                        CharacterCreationCampaignExecutionContext.Capture(next.Authority, CharacterCreationCampaignPhase.Review)));
                    Assert.AreEqual(entries, count(), "Each public call must independently replay its retained entries.");
                    return result;
                });
                Assert.AreEqual(next.Command.Review.DraftReviewDigest, review.DraftReviewDigest);
            }
            // A child operation must restore, rather than clear or replace, its parent.
            ObserveDraftReplays841(count =>
            {
                var read = store.Get(context.WorkspaceId);
                Assert.IsTrue(read.Success);
                Assert.AreEqual(0, count(), "Public review must restore the caller's separate memo.");
                return read;
            });
        }
        AssertIdentityFiles809(context.Directory, before);
        Assert.IsFalse(new WorkspaceContinuationExportService(store, owners).Export(owners.Capture(), context.WorkspaceId).Success);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Identity841_nested_review_rechecks_authority_after_history_admission_missing_source() =>
        AssertNestedReviewAuthority841("missing-source");

    [TestMethod]
    public void Identity841_nested_review_rechecks_authority_after_history_admission_corrupt_source() =>
        AssertNestedReviewAuthority841("corrupt-source");

    [TestMethod]
    public void Identity841_nested_review_rechecks_authority_after_history_admission_missing_campaign() =>
        AssertNestedReviewAuthority841("missing-campaign");

    [TestMethod]
    public void Identity841_nested_review_rechecks_authority_after_history_admission_corrupt_history() =>
        AssertNestedReviewAuthority841("corrupt-history");

    [TestMethod]
    public void Identity841_nested_review_rechecks_authority_after_history_admission_live_source() =>
        AssertNestedReviewAuthority841("live-source");

    [TestMethod]
    public void Identity841_nested_review_rechecks_authority_after_history_admission_live_policy() =>
        AssertNestedReviewAuthority841("live-policy");

    private static void AssertNestedReviewAuthority841(string damage)
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners809();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var identity = Observe809(new(store, owners), owners.Capture(), context.WorkspaceId);
        var factory = new IdentityFactory809(store, owners, identity);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver,
            new(context.Queries, factory));
        var state = Success425(attributes.Load(owners.Capture(), new(context.WorkspaceId)));
        var allocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        Dictionary<string, byte[]>? changed = null;
        factory.AfterObservation841 = () =>
        {
            Assert.AreEqual(1, owners.ActiveLeases);
            if (damage == "missing-source") File.Delete(Artifact418(context));
            else if (damage == "corrupt-source")
            {
                byte[] bytes = File.ReadAllBytes(Artifact418(context));
                bytes[^1] ^= 1;
                File.WriteAllBytes(Artifact418(context), bytes);
            }
            else if (damage is "missing-campaign" or "corrupt-history")
            {
                var record = JsonNode.Parse(File.ReadAllText(Record418(context)))!;
                if (damage == "missing-campaign") record["RetainedRuleSources"]!["CommittedDraftCampaigns"] = new JsonArray();
                else record["AuxiliaryState"]!["CharacterCreationCompletionDrafts"]![0]!["Baseline"]!["RawCharacterXml"] = "invalid-history";
                File.WriteAllText(Record418(context), record.ToJsonString());
            }
            else
            {
                string path = Path.Combine(context.Directory, "source", "data", damage == "live-policy"
                    ? CharacterCreationBootstrapProfiles.SettingsSourceFile(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
                    : "qualities.xml");
                if (damage == "live-source") File.AppendAllText(path, "\n<!-- identity841 live source drift -->\n");
                else
                {
                    var xml = System.Xml.Linq.XDocument.Load(path);
                    xml.Root!.Element("settings")!.Elements("setting")
                        .Single(row => row.Element("id")!.Value == CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
                        .Element("karmacost")!.SetElementValue("karmaattribute", 6);
                    xml.Save(path);
                }
            }
            changed = IdentityFiles809(context.Directory);
        };
        var result = attributes.PreviewRebase(owners.Capture(), new(state.Binding, allocations));
        Assert.IsNotNull(changed, "Mutation must occur after successful history admission.");
        Assert.IsNull(result.Value);
        Assert.IsTrue(result.Blockers.Count > 0);
        AssertIdentityFiles809(context.Directory, changed!);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static T ObserveDraftReplays841<T>(Func<Func<int>, T> action)
    {
        int thread = Environment.CurrentManagedThreadId;
        int replays = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name ==
                CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (Environment.CurrentManagedThreadId != thread || activity.OperationName != "DraftHistoryReplay") return;
                replays++;
                Assert.AreEqual(0, activity.TagObjects.Count(), "Historical replay spans must remain content-free.");
            }
        };
        ActivitySource.AddActivityListener(listener);
        return action(() => replays);
    }

    private static WorkspaceLocalIdentityObservation Observe809(WorkspaceLocalIdentityObservationService service,
        OwnerContextStamp stamp, CharacterWorkspaceId id)
    {
        var result = service.Observe(stamp, id);
        Assert.IsTrue(result.Success, result.Error);
        return result.Value!;
    }

    private static Dictionary<string, byte[]> IdentityFiles809(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".bin", StringComparison.Ordinal))
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertIdentityFiles809(string directory, Dictionary<string, byte[]> expected)
    {
        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), IdentityFiles809(directory).Keys.ToArray());
        foreach (var pair in expected) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key));
    }

    private sealed class IdentityFactory809(FileWorkspaceStore store, Owners809 owners,
        WorkspaceLocalIdentityObservation expected) : ICharacterCreationAttributesRebaseCampaignFactory
    {
        internal WorkspaceLocalIdentityObservation Expected = expected;
        internal Action? AfterObservation841;
        public ICharacterCreationCampaignAuthority Observe(CharacterCreationAttributesRebaseObservation observation,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual(1, owners.ActiveLeases);
            Assert.AreEqual(owners.Capture(), observation.Owner);
            var read = store.ReadLocalIdentity(observation.Owner.Owner, observation.WorkspaceId, cancellationToken);
            if (!read.Success || read.Value != Expected || Expected.ContentRevision != observation.ContentRevision
                || Expected.SavedRevision != observation.SavedRevision || Expected.RawCharacterXmlDigest != observation.RawCharacterXmlDigest
                || Expected.AuxiliaryStateDigest != observation.AuxiliaryStateDigest)
                throw new InvalidOperationException(CharacterCreationCampaignExecutionContext.Blocker);
            var identity = new WorkspaceContinuationCompletionIdentity(Expected.OwnerId, Expected.WorkspaceId,
                Expected.ContentRevision, Expected.SavedRevision, Expected.ContinuationDigest, observation.SourceDigest,
                Expected.RawCharacterXmlDigest, Expected.AuxiliaryStateDigest, observation.InputsDigest, observation.PolicyDigest, string.Empty);
            identity = identity with { ObservationDigest = CharacterCreationFinalizationDigest.Compute(identity) };
            // Test hook runs after real history admission and before nested review.
            AfterObservation841?.Invoke();
            // Deliberate test policy port, not a copied historical Finish binding.
            return new CampaignAuthority518(new(CharacterCreationCampaignBinding.SchemaV1, identity,
                Hash518("identity809-current-test-policy")), CharacterCreationCampaignState.AdmittedUnchangedPolicy);
        }
    }
}
