using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationHistoryIntegrityTests
{
    private static readonly OwnerScope LinkedOwner = new("history-owner-a");
    private static readonly CharacterWorkspaceId LinkedId = new("history-gm-runner");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Actual_creation_finalization_and_career_history_remain_complete_and_consistent_without_mutation(bool awakened)
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true, includeNonEmptyPurchases: !awakened,
            talentValue: awakened ? "Mystic Adept" : null, mysticPowerPoints: awakened ? 2 : 0);
        WorkspaceContinuationSnapshot creation = Read(context.Store, context.WorkspaceId);
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, creation);
        var auxiliary = creation.Workspace.Document.AuxiliaryState;
        Assert.IsNotNull(auxiliary.CharacterCreationPrerequisiteDraft);
        if (!awakened) Assert.IsTrue(auxiliary.CharacterCreationGearDraft!.Budget.BasketCost > 0m);
        if (awakened) Assert.IsNotNull(auxiliary.CharacterCreationMagicResonanceDraft);
        foreach (var (corruption, reason) in IntrinsicDraftCorruptions(auxiliary))
            AssertRejectedAfterOuterReseal(WithAuxiliary(creation, corruption), reason);
        // An ordinary later owner edit can leave an active draft stale. That
        // requires current evaluation, not rewriting its historical base XML.
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, LaterOwnerRevision(creation));

        // Malformed nested values are in-process inputs here, not fixtures that
        // have been declared rule-valid merely because they can be serialized.
        var assignments = auxiliary.CharacterCreationPrerequisiteDraft.Assignments.ToArray();
        assignments[0] = null!;
        WorkspaceDocumentAuxiliaryState[] malformedCreation =
        [
            auxiliary with
            {
                CharacterCreationPrerequisiteDraft = auxiliary.CharacterCreationPrerequisiteDraft with
                {
                    Assignments = assignments
                }
            },
            auxiliary with
            {
                CharacterCreationPrerequisiteDraft = auxiliary.CharacterCreationPrerequisiteDraft with
                {
                    SourceAnchorIds = [null!]
                }
            },
            auxiliary with
            {
                CharacterCreationResourcesDraft = auxiliary.CharacterCreationResourcesDraft! with
                {
                    Budget = auxiliary.CharacterCreationResourcesDraft!.Budget with { Blockers = null! }
                }
            },
            auxiliary with
            {
                CharacterCreationGearDraft = auxiliary.CharacterCreationGearDraft! with { Lines = [null!] }
            }
        ];
        foreach (var malformed in malformedCreation)
            Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser,
                WithAuxiliary(creation, malformed)));

        var loaded = context.Finalizer.Load(new(context.WorkspaceId));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        var review = context.Finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        var finalized = context.Finalizer.Confirm(new(loaded.Value.Binding, review.Value.PreviewDigest,
            review.Value.Plan!.PlanDigest, "history-consistency-finalization", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, finalized.Outcome, string.Join(",", finalized.Blockers));
        WorkspaceContinuationSnapshot finalizedSnapshot = Read(new FileWorkspaceStore(context.Directory), context.WorkspaceId);
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, finalizedSnapshot);
        var finalAuxiliary = finalizedSnapshot.Workspace.Document.AuxiliaryState;
        Assert.IsNotNull(finalAuxiliary.CharacterCreationFinalizationArchive);
        Assert.AreEqual(JsonSerializer.Serialize(auxiliary),
            JsonSerializer.Serialize(finalAuxiliary.CharacterCreationFinalizationArchive.State));
        AssertLatestOutputAndCheckpoint(finalizedSnapshot);
        foreach (var (corruption, reason) in IntrinsicDraftCorruptions(auxiliary))
        {
            var resealed = ResealArchive(finalizedSnapshot, corruption);
            Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(resealed.Workspace.Id,
                resealed.Workspace.ContentRevision, resealed.Workspace.Document.AuxiliaryState),
                "The shared persisted-shape check must not itself catch this attack: " + reason);
            AssertRejectedAfterOuterReseal(resealed, "archived " + reason);
        }
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser,
            WithAuxiliary(finalizedSnapshot, finalAuxiliary with
            {
                CharacterCreationFinalizationArchive = new(null!)
            })));
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser,
            WithAuxiliary(finalizedSnapshot, finalAuxiliary with
            {
                CharacterCreationFinalizationArchive = new(auxiliary with
                {
                    CharacterCreationFinalizationArchive = new(auxiliary)
                })
            })));

        var reputation = new WorkspaceCharacterCareerReputationService(context.Store, context.Resolver);
        var preview = reputation.Preview(new(context.WorkspaceId, Guid.NewGuid(),
            CharacterCareerReputationOperation.AdjustManualAwards, new(1, null, null), "First local Career decision"));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, preview.Outcome, preview.Error);
        var applied = reputation.Commit(preview.Preview!.Command with { ExplicitlyConfirmed = true });
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, applied.Outcome, applied.Error);
        WorkspaceContinuationSnapshot career = Read(new FileWorkspaceStore(context.Directory), context.WorkspaceId);
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, career);
        var reputationReceipts = career.Workspace.Document.AuxiliaryState.CharacterCareerReputationReceipts!;
        Assert.HasCount(1, reputationReceipts);
        Assert.AreEqual(career.Workspace.ContentRevision, reputationReceipts[0].CommittedWorkspaceRevision);

        // Shape and receipt hashes still validate, but current XML bytes no
        // longer match the receipt committed at this exact workspace revision.
        var changedXml = career with
        {
            Workspace = career.Workspace with
            {
                Document = career.Workspace.Document with
                {
                    State = career.Workspace.Document.State with { Payload = career.Workspace.Document.Content + " " }
                }
            }
        };
        Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(changedXml.Workspace.Id,
            changedXml.Workspace.ContentRevision, changedXml.Workspace.Document.AuxiliaryState));
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser, changedXml));
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser, career with
        {
            Workspace = career.Workspace with { SavedRevision = reputationReceipts[0].CommittedWorkspaceRevision - 1 }
        }));
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser,
            WithAuxiliary(career, career.Workspace.Document.AuxiliaryState with
            {
                CharacterCareerReputationReceipts = [reputationReceipts[0] with { Quote = null! }]
            })));

        // A real later After Run commit legitimately changes XML beyond the
        // reputation receipt's revision while retaining its complete history.
        var rewards = new WorkspaceCharacterAfterRunRewardService(context.Store);
        var rewardPreview = rewards.Preview(new(context.WorkspaceId, Guid.NewGuid(), Guid.NewGuid(),
            8, 12500, new DateTime(2078, 9, 7, 18, 0, 0), "Later run"));
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, rewardPreview.Outcome, rewardPreview.Error);
        var reward = rewards.Commit(rewardPreview.Preview!.Command with { ExplicitlyConfirmed = true });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, reward.Outcome, reward.Error);
        WorkspaceContinuationSnapshot later = Read(new FileWorkspaceStore(context.Directory), context.WorkspaceId);
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, later);
        AssertLatestOutputAndCheckpoint(later);
        Assert.IsTrue(later.Workspace.ContentRevision > reputationReceipts[0].CommittedWorkspaceRevision);
        Assert.AreEqual(JsonSerializer.Serialize(reputationReceipts),
            JsonSerializer.Serialize(later.Workspace.Document.AuxiliaryState.CharacterCareerReputationReceipts));
        Assert.AreEqual(JsonSerializer.Serialize(auxiliary),
            JsonSerializer.Serialize(later.Workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State));
    }

    [TestMethod]
    public void Actual_contact_and_catalog_lifestyle_receipts_bind_current_XML_and_checkpoint_but_not_later_owner_XML()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        Guid contactId = Guid.NewGuid();
        var xml = XDocument.Parse(saved.Document.Content);
        xml.Root!.SetElementValue("contactpoints", "15");
        xml.Root.SetElementValue("nuyen", "10000");
        xml.Root.SetElementValue("startingnuyen", "10000");
        xml.Root.Element("contacts")?.Remove();
        xml.Root.Add(new XElement("contacts", new XElement("contact",
            new XElement("guid", contactId.ToString("D")), new XElement("name", "Fixer"),
            new XElement("connection", 3), new XElement("loyalty", 2), new XElement("type", "Contact"),
            new XElement("free", false), new XElement("group", false),
            new XElement("family", false), new XElement("blackmail", false))));
        var document = saved.Document with { State = saved.Document.State with { Payload = xml.ToString(SaveOptions.DisableFormatting) } };
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocument(context.WorkspaceId, saved.ContentRevision, document).Success);
        var contacts = new CharacterCreationContactsService(context.Store);
        var contactState = contacts.Load(new(context.WorkspaceId));
        Assert.IsNotNull(contactState.Value, string.Join(",", contactState.Blockers));
        var edit = new CharacterCreationContactEdit(contactId, Free: true);
        var preview = contacts.Preview(new(contactState.Value.Binding, edit));
        Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
        var confirmed = contacts.Confirm(new(contactState.Value.Binding, edit, preview.Value.PreviewDigest,
            "history-contact", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationContactOutcomes.Applied, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        AssertLatestOutputAndCheckpoint(Read(context.Store, context.WorkspaceId));

        // The catalog and prices come from the real filesystem resolver already
        // used by the bootstrap fixture, not a manufactured historical authority.
        var lifestyles = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var lifestyleState = lifestyles.Load(new(context.WorkspaceId));
        Assert.IsNotNull(lifestyleState.Value, string.Join(",", lifestyleState.Blockers));
        Assert.IsTrue(lifestyleState.Value.CanEdit, string.Join(",", lifestyleState.Blockers));
        Assert.AreEqual(10000m, lifestyleState.Value.Budget.Total);
        var option = lifestyleState.Value.Authority.LifestyleOptions.First(item => item.IsSelectable
            && item.BaseCost > 0m && item.BaseCost <= lifestyleState.Value.Budget.Total);
        Guid lifestyleId = Guid.NewGuid();
        var configuration = new CharacterCreationLifestyleConfiguration(lifestyleId, option.OptionId,
            "History apartment", CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId,
            1, 100m, 0, false, false, 0, 0, 0, 0, "Vienna", "", "", []);
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create,
            lifestyleId, configuration);
        var lifestylePreview = lifestyles.Preview(new(lifestyleState.Value.Binding, mutation));
        Assert.IsNotNull(lifestylePreview.Value, string.Join(",", lifestylePreview.Blockers));
        var lifestyle = lifestyles.Confirm(new(lifestyleState.Value.Binding, mutation,
            lifestylePreview.Value.PreviewDigest, "history-lifestyle", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, lifestyle.Outcome, string.Join(",", lifestyle.Blockers));
        AssertLatestOutputAndCheckpoint(Read(new FileWorkspaceStore(context.Directory), context.WorkspaceId));
    }

    [TestMethod]
    public void Actual_Foundation_pending_draft_rejects_stale_self_hash_and_rehashed_foreign_identity()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.LifeModules);
        DirectoryInfo? coreRoot = new(AppDomain.CurrentDomain.BaseDirectory);
        while (coreRoot is not null && !File.Exists(Path.Combine(coreRoot.FullName, "Chummer", "data", "lifemodules.xml")))
            coreRoot = coreRoot.Parent;
        Assert.IsNotNull(coreRoot);
        var catalog = new XmlLifeModulesCatalogService(Path.Combine(coreRoot.FullName, "Chummer", "data", "lifemodules.xml"));
        var service = new CharacterCreationFoundationService(context.Store, context.Queries, context.Resolver,
            catalog, new CharacterCreationFoundationDraftApplyAuthority(context.Store));
        var state = service.Load(new(context.WorkspaceId));
        Assert.IsNotNull(state.Value, string.Join(",", state.Blockers));
        var nationality = catalog.GetOptionProjections("Nationality", ["RF"])
            .Single(item => item.ModuleId == "83c132b5-fcf5-4a43-b9de-6c8ab206a586");
        var version = nationality.Versions.Single(item => item.VersionId == "604831d9-0fdc-4579-aa7e-bc5d99bcee5d");
        var followUps = nationality.FollowUps.Concat(version.FollowUps).ToDictionary(prompt => prompt.PromptId,
            prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Confirmed", StringComparer.Ordinal);
        var preview = service.Preview(new(state.Value.Binding, "Human", new(nationality.ModuleId, version.VersionId), followUps));
        Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
        var confirmed = service.Confirm(new(preview.Value.Binding, preview.Value.RequestedMetatype,
            preview.Value.Selection, preview.Value.PreviewDigest, ExplicitlyConfirmed: true, preview.Value.FollowUpValues));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        var candidate = Read(new FileWorkspaceStore(context.Directory), context.WorkspaceId);
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, candidate);
        var auxiliary = candidate.Workspace.Document.AuxiliaryState;
        var draft = auxiliary.CharacterCreationFoundationDraft!;
        Assert.IsNotEmpty(draft.ProjectedEffects);
        AssertRejectedAfterOuterReseal(WithAuxiliary(candidate, auxiliary with
        {
            CharacterCreationFoundationDraft = draft with { SourceAnchorIds = ["forged-foundation-anchor"] }
        }), "foundation content with stale self hash");
        var foreign = draft with { WorkspaceId = new("foreign-foundation") };
        foreign = foreign with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(foreign) };
        AssertRejectedAfterOuterReseal(WithAuxiliary(candidate, auxiliary with { CharacterCreationFoundationDraft = foreign }),
            "self-rehashed foundation with foreign workspace");
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, candidate);
    }

    [TestMethod]
    public void Genuine_later_Attributes_confirmation_keeps_stale_active_Skills_as_unverified_history()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: false);
        var original = Read(context.Store, context.WorkspaceId);
        var skills = original.Workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft!;
        var service = new CharacterCreationAttributesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId));
        Assert.IsNotNull(state.Value, string.Join(",", state.Blockers));
        CharacterCreationAttributeAllocation[] allocation = [new("BOD", 1, 0)];
        var preview = service.Preview(new(state.Value.Binding, allocation));
        Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
        var confirmed = service.Confirm(new(preview.Value.Binding, allocation, preview.Value.PreviewDigest, ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        var later = Read(new FileWorkspaceStore(context.Directory), context.WorkspaceId);
        Assert.IsTrue(later.Workspace.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftRevision
            > skills.AttributesDraftRevision);
        Assert.AreEqual(JsonSerializer.Serialize(skills),
            JsonSerializer.Serialize(later.Workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft));
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, later);
        // Consistent retained history is not a grant to continue this stale
        // Skills draft against the newer Attributes graph.
    }

    [TestMethod]
    public void Actual_scoped_GM_history_is_owner_bound_and_retained_after_a_later_owner_edit()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-history-gm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            FileWorkspaceStore store = new(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(LinkedOwner, LinkedId, Document("Original owner note")).Success);
            CharacterFileService files = new();
            Sr5WorkspaceCodec codec = new(new XmlCharacterFileQueries(files),
                new XmlCharacterSectionQueries(new CharacterSectionService()), new XmlCharacterMetadataCommands(files));
            var service = new DelegatedGmCharacterEditService(store, new RulesetWorkspaceCodecResolver([codec]),
                new GmAuthorizer(), new GmClock());
            var applied = service.Execute(new("history-campaign", "gm@example.com", LinkedOwner, LinkedId, 1,
                "history-gm-key", "Correct campaign-visible note",
                [new(DelegatedGmCharacterPatchOperationKind.Replace, DelegatedGmCharacterEditContract.ProfileNotesPath, "GM-visible note")]));
            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, applied.Outcome, applied.Error);
            Assert.IsTrue(store.ReplaceWorkspaceDocument(LinkedOwner, LinkedId, 2, Document("Later owner note")).Success);
            var read = new FileWorkspaceStore(directory).ReadContinuation(LinkedOwner, LinkedId);
            Assert.IsTrue(read.Success, read.Error);
            Assert.IsNotNull(read.Value);
            WorkspaceContinuationSnapshot candidate = read.Value;
            AssertConsistentAndUnchanged(LinkedOwner, candidate);
            Assert.HasCount(1, candidate.DelegatedGmCharacterEdits);
            Assert.AreEqual(JsonSerializer.Serialize(applied.Receipt), JsonSerializer.Serialize(candidate.DelegatedGmCharacterEdits[0]));
            Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(new("history-owner-b"), candidate));
            Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser,
                candidate with { OwnerId = OwnerScope.LocalSingleUser.NormalizedValue }));
            Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(LinkedOwner,
                candidate with { Workspace = candidate.Workspace with { Id = new("another-workspace") } }));

            var receipt = candidate.DelegatedGmCharacterEdits[0];
            IReadOnlyList<DelegatedGmCharacterEditAuditReceipt>[] malformedLedgers =
            [
                [null!],
                [receipt, receipt],
                [receipt with { Operations = [null!] }],
                [receipt with { Operations = default }],
                [receipt with { CharacterOwnerId = "history-owner-b" }],
                [receipt with { CommandSha256 = new string('f', 64) }],
                [receipt with { PreviousRevision = candidate.Workspace.ContentRevision, NewRevision = candidate.Workspace.ContentRevision + 1 }]
            ];
            foreach (var ledger in malformedLedgers)
                Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(LinkedOwner,
                    candidate with { DelegatedGmCharacterEdits = ledger }));
            AssertConsistentAndUnchanged(LinkedOwner, candidate);
            Assert.AreEqual(JsonSerializer.Serialize(candidate), JsonSerializer.Serialize(
                new FileWorkspaceStore(directory).ReadContinuation(LinkedOwner, LinkedId).Value));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("null-candidate")]
    [DataRow("null-owner")]
    [DataRow("wrong-owner-case")]
    [DataRow("owner-whitespace")]
    [DataRow("null-workspace")]
    [DataRow("null-document")]
    [DataRow("null-state")]
    [DataRow("null-auxiliary")]
    [DataRow("null-GM-ledger")]
    [DataRow("invalid-id")]
    [DataRow("null-id")]
    [DataRow("zero-revision")]
    [DataRow("negative-checkpoint")]
    [DataRow("future-checkpoint")]
    [DataRow("unknown-format")]
    [DataRow("zero-schema")]
    [DataRow("null-payload")]
    public void Malformed_candidate_shells_fail_closed_instead_of_becoming_empty_history(string corruption)
    {
        WorkspaceContinuationSnapshot? candidate = new(LinkedOwner.NormalizedValue,
            new(LinkedId, Document("Owner note"), GmClock.Now, 1, 0), []);
        var workspace = candidate.Workspace;
        candidate = corruption switch
        {
            "null-candidate" => null,
            "null-owner" => candidate with { OwnerId = null! },
            "wrong-owner-case" => candidate with { OwnerId = LinkedOwner.NormalizedValue.ToUpperInvariant() },
            "owner-whitespace" => candidate with { OwnerId = " " + LinkedOwner.NormalizedValue },
            "null-workspace" => candidate with { Workspace = null! },
            "null-document" => candidate with { Workspace = workspace with { Document = null! } },
            "null-state" => candidate with { Workspace = workspace with { Document = workspace.Document with { State = null! } } },
            "null-auxiliary" => WithAuxiliary(candidate, null!),
            "null-GM-ledger" => candidate with { DelegatedGmCharacterEdits = null! },
            "invalid-id" => candidate with { Workspace = workspace with { Id = new("../another-owner") } },
            "null-id" => candidate with { Workspace = workspace with { Id = new(null!) } },
            "zero-revision" => candidate with { Workspace = workspace with { ContentRevision = 0 } },
            "negative-checkpoint" => candidate with { Workspace = workspace with { SavedRevision = -1 } },
            "future-checkpoint" => candidate with { Workspace = workspace with { SavedRevision = 2 } },
            "unknown-format" => candidate with { Workspace = workspace with { Document = workspace.Document with { Format = (WorkspaceDocumentFormat)999 } } },
            "zero-schema" => candidate with { Workspace = workspace with { Document = workspace.Document with { State = workspace.Document.State with { SchemaVersion = 0 } } } },
            "null-payload" => candidate with { Workspace = workspace with { Document = workspace.Document with { State = workspace.Document.State with { Payload = null! } } } },
            _ => throw new AssertFailedException("Unknown corruption case.")
        };
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(LinkedOwner, candidate));
    }

    [TestMethod]
    public void Unknown_and_untrusted_local_owner_values_do_not_admit_even_empty_history()
    {
        var local = new WorkspaceContinuationSnapshot(OwnerScope.LocalSingleUser.NormalizedValue,
            new(LinkedId, Document("Local note"), GmClock.Now, 1, 0), []);
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, local);
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(default, local));
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(new OwnerScope("local-single-user"), local));
    }

    private static WorkspaceContinuationSnapshot Read(FileWorkspaceStore store, CharacterWorkspaceId id)
    {
        var result = store.ReadContinuation(id);
        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static IEnumerable<(WorkspaceDocumentAuxiliaryState State, string Reason)> IntrinsicDraftCorruptions(
        WorkspaceDocumentAuxiliaryState auxiliary)
    {
        yield return (auxiliary with
        {
            CharacterCreationPrerequisiteDraft = auxiliary.CharacterCreationPrerequisiteDraft! with
            {
                SourceAnchorIds = ["forged-prerequisite-anchor"]
            }
        }, "prerequisite content with stale self hash");
        yield return (auxiliary with
        {
            CharacterCreationAttributesDraft = auxiliary.CharacterCreationAttributesDraft! with
            {
                SourceAnchorIds = ["forged-attribute-anchor"]
            }
        }, "attributes content with stale self hash");
        yield return (auxiliary with
        {
            CharacterCreationSkillsDraft = auxiliary.CharacterCreationSkillsDraft! with
            {
                SourceAnchorIds = ["forged-skills-anchor"]
            }
        }, "skills content with stale self hash");

        foreach (var unsigned in new[]
        {
            auxiliary.CharacterCreationSkillsDraft! with { WorkspaceId = new("foreign-workspace") },
            auxiliary.CharacterCreationSkillsDraft! with { AttributesDraftRevision = 999 },
            auxiliary.CharacterCreationSkillsDraft! with { PrerequisiteDraftDigest = "sha256:" + new string('f', 64) },
            auxiliary.CharacterCreationSkillsDraft! with { BaseContentRevision = 0 },
            auxiliary.CharacterCreationSkillsDraft! with
            {
                ActivePointTotal = -1,
                ActivePointUsed = -1 - (auxiliary.CharacterCreationSkillsDraft.ActivePointTotal
                    - auxiliary.CharacterCreationSkillsDraft.ActivePointUsed)
            }
        })
        {
            var draft = unsigned with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(unsigned) };
            var receipts = auxiliary.CharacterCreationSkillsReceipts!.ToArray();
            var receipt = receipts[^1] with { DraftDigest = draft.DraftDigest };
            receipts[^1] = receipt with
            {
                ReceiptDigest = CharacterCreationSkillsDigest.Compute(receipt with { ReceiptDigest = string.Empty })
            };
            yield return (auxiliary with { CharacterCreationSkillsDraft = draft, CharacterCreationSkillsReceipts = receipts },
                "self-rehashed skills with invalid workspace, revision, dependency or intrinsic budget");
        }

        if (auxiliary.CharacterCreationMagicResonanceDraft is not { } magic) yield break;
        yield return (auxiliary with
        {
            CharacterCreationMagicResonanceDraft = magic with { SourceAnchorIds = ["forged-magic-anchor"] }
        }, "magic content with stale self hash");
        foreach (var unsigned in new[]
        {
            magic with { WorkspaceId = new("foreign-workspace") },
            magic with { AttributesDraftRevision = 999 },
            magic with { PrerequisiteAuthorityDigest = "sha256:" + new string('f', 64) },
            magic with { BaseContentRevision = 0 },
            magic with { AssignedMagic = -1 },
            magic with
            {
                SpellBudget = magic.SpellBudget with { Total = -1m, Used = -1m - magic.SpellBudget.Remaining }
            }
        })
        {
            var draft = unsigned with { DraftDigest = CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(unsigned) };
            var receipts = auxiliary.CharacterCreationMagicResonanceReceipts!.ToArray();
            var receipt = receipts[^1] with { DraftDigest = draft.DraftDigest };
            receipts[^1] = receipt with
            {
                ReceiptDigest = CharacterCreationMagicResonanceDigest.Compute(receipt with { ReceiptDigest = string.Empty })
            };
            yield return (auxiliary with
            {
                CharacterCreationMagicResonanceDraft = draft,
                CharacterCreationMagicResonanceReceipts = receipts
            }, "self-rehashed magic with invalid workspace, revision, dependency or intrinsic budget");
        }
    }

    private static WorkspaceContinuationSnapshot ResealArchive(WorkspaceContinuationSnapshot candidate,
        WorkspaceDocumentAuxiliaryState archive)
    {
        var auxiliary = candidate.Workspace.Document.AuxiliaryState;
        var entry = auxiliary.CharacterCreationFinalizationReceipts![0];
        var receipt = entry.Receipt with { PreviousAuxiliaryStateDigest = WorkspaceDocumentAuxiliaryStateDigest.Compute(archive) };
        receipt = receipt with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt) };
        return WithAuxiliary(candidate, auxiliary with
        {
            CharacterCreationFinalizationArchive = new(archive),
            CharacterCreationFinalizationReceipts = [entry with { Receipt = receipt }]
        });
    }

    private static void AssertRejectedAfterOuterReseal(WorkspaceContinuationSnapshot candidate, string reason)
    {
        const int limit = 16 * 1024 * 1024;
        var export = new WorkspaceContinuationExport(candidate, WorkspaceContinuationSnapshotDigest.Compute(candidate));
        byte[] wire = WorkspaceContinuationCodec.Encode(export, limit);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(wire, limit, out var decoded), reason);
        Assert.IsNotNull(decoded);
        Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser, decoded.Snapshot), reason);
    }

    private static WorkspaceContinuationSnapshot WithChangedXml(WorkspaceContinuationSnapshot candidate) => candidate with
    {
        Workspace = candidate.Workspace with
        {
            Document = candidate.Workspace.Document with
            {
                State = candidate.Workspace.Document.State with { Payload = candidate.Workspace.Document.Content + "\n" }
            }
        }
    };

    private static WorkspaceContinuationSnapshot LaterOwnerRevision(WorkspaceContinuationSnapshot candidate)
    {
        var changed = WithChangedXml(candidate);
        return changed with { Workspace = changed.Workspace with { ContentRevision = changed.Workspace.ContentRevision + 1 } };
    }

    private static void AssertLatestOutputAndCheckpoint(WorkspaceContinuationSnapshot candidate)
    {
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, candidate);
        Assert.AreEqual(candidate.Workspace.ContentRevision, candidate.Workspace.SavedRevision);
        AssertRejectedAfterOuterReseal(WithChangedXml(candidate), "latest output XML digest must bind actual candidate bytes");
        AssertRejectedAfterOuterReseal(candidate with
        {
            Workspace = candidate.Workspace with { SavedRevision = candidate.Workspace.SavedRevision - 1 }
        }, "latest durable receipt cannot lose its checkpoint");
        var later = LaterOwnerRevision(candidate);
        AssertConsistentAndUnchanged(OwnerScope.LocalSingleUser, later);
        AssertRejectedAfterOuterReseal(later with
        {
            Workspace = later.Workspace with { SavedRevision = candidate.Workspace.SavedRevision - 1 }
        }, "historical receipt checkpoint remains a floor after a later owner edit");
    }

    private static void AssertConsistentAndUnchanged(OwnerScope owner, WorkspaceContinuationSnapshot candidate)
    {
        string before = JsonSerializer.Serialize(candidate);
        string digest = WorkspaceContinuationSnapshotDigest.Compute(candidate);
        Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(owner, candidate));
        Assert.AreEqual(before, JsonSerializer.Serialize(candidate));
        Assert.AreEqual(digest, WorkspaceContinuationSnapshotDigest.Compute(candidate));
    }

    private static WorkspaceContinuationSnapshot WithAuxiliary(WorkspaceContinuationSnapshot candidate, WorkspaceDocumentAuxiliaryState auxiliary) =>
        candidate with { Workspace = candidate.Workspace with { Document = candidate.Workspace.Document with { State = candidate.Workspace.Document.State with { AuxiliaryState = auxiliary } } } };

    private static WorkspaceDocument Document(string notes) => new(
        $"<character><name>Runner One</name><alias>One</alias><notes>{notes}</notes><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>", "sr5");

    private sealed class GmAuthorizer : ICampaignGmCharacterEditAuthorizer
    {
        public CampaignGmCharacterEditAuthorization Authorize(CampaignGmCharacterEditAuthorizationRequest request) => new(
            true, request.CampaignId, request.ActorId, DelegatedGmCharacterEditContract.GameMasterRole,
            DelegatedGmCharacterEditContract.CharacterEditScope, request.CharacterOwner, request.CharacterId,
            "history-delegation", "campaign-owner@example.com", request.CharacterOwner.NormalizedValue,
            "history-authority-receipt", 7, GmClock.Now.AddMinutes(-5), GmClock.Now.AddHours(1),
            [DelegatedGmCharacterEditContract.ProfileNotesPath]);
    }

    private sealed class GmClock : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
