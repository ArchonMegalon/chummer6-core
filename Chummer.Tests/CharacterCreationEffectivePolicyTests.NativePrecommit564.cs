using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AllocationOwners564 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;

namespace Chummer.Tests;

// AUTHORED, UNRUN. Real Core file transaction tests. The Android harness separately
// verifies the concrete signed synchronous exchange; these are not Android/device tests.
public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Native564_fresh_denial_after_real_temp_flush_blocks_atomic_replace()
    {
        using var context = ReputationContext414(preserved: false);
        CampaignAuthority518? live = null;
        int readsAtFlush = -1;
        var fault = new CampaignFault518(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, () =>
        {
            readsAtFlush = live!.Reads;
            live.State = CharacterCreationCampaignState.Denied;
            live.Failure = CharacterCreationCampaignFailure.Denied;
        });
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        var owners = new AllocationOwners564();
        var prepared = PrepareDraft550(context, store, owners);
        live = prepared.Authority;
        var authority = new Native564FreshAuthority(live);
        var apply = CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Apply);
        byte[] original = File.ReadAllBytes(Record418(context));
        var result = DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), prepared.Command, apply);
        Assert.IsTrue(fault.Called);
        Assert.IsTrue(live.Reads > readsAtFlush, "Core did not Observe again after temp flush.");
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Native564_exact_committed_retry_and_lookup_need_no_new_observation()
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var owners = new AllocationOwners564();
        var prepared = PrepareDraft550(context, store, owners);
        var authority = new Native564FreshAuthority(prepared.Authority);
        var apply = CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Apply);
        var service = DraftService550(context, store, owners);
        var receipt = Success425(service.ApplyDraftWithCampaign(owners.Capture(), prepared.Command, apply));
        authority.ForbidObservation = true;
        int reads = authority.Reads;
        // Exactly the same command/key resolves through the private committed lane.
        // Do NOT Capture another live context before performing this lookup/retry.
        var cold = DraftService550(context,
            FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries), owners);
        Assert.AreEqual(receipt, Success425(cold.ApplyDraft(owners.Capture(), prepared.Command)));
        Assert.AreEqual(receipt, Success425(cold.LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(prepared.Command))));
        Assert.AreEqual(reads, authority.Reads);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private sealed class Native564FreshAuthority(ICharacterCreationCampaignAuthority live) : ICharacterCreationCampaignAuthority
    {
        internal int Reads;
        internal bool ForbidObservation;
        public CharacterCreationCampaignObservation Observe(CharacterCreationCampaignPhase phase, CancellationToken ct)
        {
            Reads++;
            Assert.IsFalse(ForbidObservation, "Committed recovery requested new permission.");
            return live.Observe(phase, ct); // no remembered successful observation
        }
    }
}
