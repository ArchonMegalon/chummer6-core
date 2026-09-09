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
public sealed class WorkspaceContinuationHistoryReviewTests
{
    private static readonly OwnerScope LinkedOwner = new("continuation-history-review-owner");
    private static readonly CharacterWorkspaceId LinkedId = new("continuation-history-review-runner");

    [TestMethod]
    public void Real_independent_career_commits_cannot_both_claim_one_workspace_revision()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        Finalize(context);
        WorkspaceContinuationSnapshot finalized = Read(context.Store, context.WorkspaceId);
        Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser, finalized));
        string branchDirectory = Path.Combine(Path.GetTempPath(), $"chummer-continuation-history-branch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(branchDirectory);
        try
        {
            // Both histories below are genuinely executed by production services
            // from the same real finalized workspace. No receipt is synthesized
            // or rehashed to manufacture a revision collision.
            CopyStore(context.Directory, branchDirectory);
            var branchStore = new FileWorkspaceStore(branchDirectory);
            ApplyReward(branchStore, context.WorkspaceId, "Independent branch reward");
            WorkspaceContinuationSnapshot branch = Read(new FileWorkspaceStore(branchDirectory), context.WorkspaceId);
            Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser, branch));
            var branchReward = branch.Workspace.Document.AuxiliaryState.CharacterAfterRunRewardReceipts!;
            Assert.HasCount(1, branchReward);

            var reputation = new WorkspaceCharacterCareerReputationService(context.Store, context.Resolver);
            var preview = reputation.Preview(new(context.WorkspaceId, Guid.NewGuid(),
                CharacterCareerReputationOperation.AdjustManualAwards, new(1, null, null), "Main branch reputation"));
            Assert.AreEqual(CharacterCareerReputationOutcome.Available, preview.Outcome, preview.Error);
            var applied = reputation.Commit(preview.Preview!.Command with { ExplicitlyConfirmed = true });
            Assert.AreEqual(CharacterCareerReputationOutcome.Applied, applied.Outcome, applied.Error);
            ApplyReward(context.Store, context.WorkspaceId, "Later legitimate reward");
            WorkspaceContinuationSnapshot sequential = Read(new FileWorkspaceStore(context.Directory), context.WorkspaceId);
            Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser, sequential));

            var auxiliary = sequential.Workspace.Document.AuxiliaryState;
            var reputationHistory = auxiliary.CharacterCareerReputationReceipts!;
            Assert.HasCount(1, reputationHistory);
            Assert.HasCount(1, auxiliary.CharacterAfterRunRewardReceipts!);
            Assert.AreEqual(reputationHistory[0].CommittedWorkspaceRevision + 1,
                auxiliary.CharacterAfterRunRewardReceipts![0].CommittedWorkspaceRevision);
            Assert.AreEqual(JsonSerializer.Serialize(finalized.Workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive),
                JsonSerializer.Serialize(auxiliary.CharacterCreationFinalizationArchive));

            WorkspaceContinuationSnapshot collision = WithAuxiliary(sequential, auxiliary with
            {
                CharacterAfterRunRewardReceipts = branchReward
            });
            Assert.AreEqual(reputationHistory[0].CommittedWorkspaceRevision,
                branchReward[0].CommittedWorkspaceRevision);
            Assert.IsTrue(collision.Workspace.ContentRevision > branchReward[0].CommittedWorkspaceRevision,
                "This is an intrinsic historical collision, not merely a mismatched current payload hash.");
            Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(collision.Workspace.Id,
                collision.Workspace.ContentRevision, collision.Workspace.Document.AuxiliaryState),
                "Both unmodified real receipt ledgers pass the preexisting per-lane shape checks.");
            Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser, collision),
                "Two independent Career transactions cannot both be the commit that advanced one workspace revision.");
            Assert.AreEqual(JsonSerializer.Serialize(sequential), JsonSerializer.Serialize(
                Read(new FileWorkspaceStore(context.Directory), context.WorkspaceId)),
                "The forged review candidate must never be persisted.");
        }
        finally
        {
            Directory.Delete(branchDirectory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("GM-visible note")]
    [DataRow("")]
    [DataRow("  GM-visible\r\nnote\rnext\nline\t  ")]
    public void Real_latest_GM_value_commitment_must_match_payload_without_requiring_a_checkpoint(string note)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-continuation-history-gm-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FileWorkspaceStore(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(LinkedOwner, LinkedId, Document("Original note")).Success);
            CharacterFileService files = new();
            Sr5WorkspaceCodec codec = new(new XmlCharacterFileQueries(files),
                new XmlCharacterSectionQueries(new CharacterSectionService()), new XmlCharacterMetadataCommands(files));
            var service = new DelegatedGmCharacterEditService(store, new RulesetWorkspaceCodecResolver([codec]),
                new GmAuthorizer(), new GmClock());
            var applied = service.Execute(new("history-review-campaign", "gm@example.com", LinkedOwner, LinkedId, 1,
                "history-review-gm-key", "Correct campaign-visible note",
                [new(DelegatedGmCharacterPatchOperationKind.Replace,
                    DelegatedGmCharacterEditContract.ProfileNotesPath, note)]));
            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, applied.Outcome, applied.Error);
            WorkspaceContinuationSnapshot candidate = Read(new FileWorkspaceStore(directory), LinkedId, LinkedOwner);
            Assert.HasCount(1, candidate.DelegatedGmCharacterEdits);
            Assert.AreEqual(candidate.Workspace.ContentRevision, candidate.DelegatedGmCharacterEdits[0].NewRevision);
            Assert.AreEqual(0L, candidate.Workspace.SavedRevision,
                "A delegated GM edit genuinely leaves the owner's checkpoint unchanged.");
            Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(LinkedOwner, candidate));

            foreach (string malformedXml in new[]
                     {
                         "<character",
                         "<!DOCTYPE character [<!ENTITY v 'untrusted'>]><character><notes>&v;</notes></character>",
                         "<character><notes>First</notes><notes>Second</notes></character>"
                     })
            {
                var malformed = candidate with { Workspace = candidate.Workspace with
                {
                    Document = candidate.Workspace.Document with { State = candidate.Workspace.Document.State with
                    {
                        Payload = malformedXml
                    } }
                } };
                Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(LinkedOwner, malformed));
            }

            XDocument xml = XDocument.Parse(candidate.Workspace.Document.Content);
            Assert.AreEqual(note, xml.Root!.Element("notes")!.Value);
            xml.Root.SetElementValue("notes", "Contradictory candidate note");
            WorkspaceContinuationSnapshot mismatch = candidate with
            {
                Workspace = candidate.Workspace with
                {
                    Document = candidate.Workspace.Document with
                    {
                        State = candidate.Workspace.Document.State with
                        {
                            Payload = xml.ToString(SaveOptions.DisableFormatting)
                        }
                    }
                }
            };
            Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(mismatch.Workspace.Id,
                mismatch.Workspace.ContentRevision, mismatch.Workspace.Document.AuxiliaryState));
            var gmLedger = mismatch.DelegatedGmCharacterEdits.Select(receipt =>
                new DelegatedGmCharacterEditLedgerEntry(receipt.IdempotencyKeySha256, receipt.CommandSha256, receipt)).ToArray();
            Assert.IsTrue(DelegatedGmCharacterEditLedgerValidator.IsValidLedger(LinkedOwner, LinkedId,
                mismatch.Workspace.ContentRevision, gmLedger), "The untouched real GM ledger passes its original checks.");
            Assert.IsFalse(WorkspaceContinuationHistoryIntegrity.TryValidate(LinkedOwner, mismatch),
                "The latest GM receipt's value digest/length must identify the current field value.");

            // An actual later owner write changes the revision and may replace
            // the same field. That does not invalidate the historical GM event.
            Assert.IsTrue(store.ReplaceWorkspaceDocument(LinkedOwner, LinkedId,
                candidate.Workspace.ContentRevision, Document("Later owner note")).Success);
            WorkspaceContinuationSnapshot later = Read(new FileWorkspaceStore(directory), LinkedId, LinkedOwner);
            Assert.IsTrue(later.Workspace.ContentRevision > candidate.DelegatedGmCharacterEdits[0].NewRevision);
            Assert.AreEqual(0L, later.Workspace.SavedRevision);
            Assert.AreEqual(JsonSerializer.Serialize(candidate.DelegatedGmCharacterEdits),
                JsonSerializer.Serialize(later.DelegatedGmCharacterEdits));
            Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(LinkedOwner, later));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Finalize(ReadyContext context)
    {
        var loaded = context.Finalizer.Load(new(context.WorkspaceId));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        var review = context.Finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        var applied = context.Finalizer.Confirm(new(loaded.Value.Binding, review.Value.PreviewDigest,
            review.Value.Plan!.PlanDigest, "history-review-finalization", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome, string.Join(",", applied.Blockers));
    }

    private static void ApplyReward(FileWorkspaceStore store, CharacterWorkspaceId id, string description)
    {
        var rewards = new WorkspaceCharacterAfterRunRewardService(store);
        var preview = rewards.Preview(new(id, Guid.NewGuid(), Guid.NewGuid(),
            8, 12500, new DateTime(2078, 9, 7, 18, 0, 0), description));
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, preview.Outcome, preview.Error);
        var applied = rewards.Commit(preview.Preview!.Command with { ExplicitlyConfirmed = true });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, applied.Outcome, applied.Error);
    }

    private static void CopyStore(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private static WorkspaceContinuationSnapshot Read(FileWorkspaceStore store, CharacterWorkspaceId id, OwnerScope? owner = null)
    {
        var result = owner is { } scope ? store.ReadContinuation(scope, id) : store.ReadContinuation(id);
        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(result.Value);
        return result.Value;
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
            "history-review-delegation", "campaign-owner@example.com", request.CharacterOwner.NormalizedValue,
            "history-review-authority", 7, GmClock.Now.AddMinutes(-5), GmClock.Now.AddHours(1),
            [DelegatedGmCharacterEditContract.ProfileNotesPath]);
    }

    private sealed class GmClock : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
