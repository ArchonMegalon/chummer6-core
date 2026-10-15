using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationCompletionApplyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_attribute_karma_apply_rejects_source_repricing_before_atomic_commit(bool changeBeforeRename)
    {
        using var context = ReadyContext.Create(false, amendSettings: _ => { }, playerChosenAttributeIds: []);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var inputs = Inputs(context, store, owners);
        var planned = CharacterCreationCompletionPlanner.Plan(inputs,
            CharacterCreationCompletionPlannerTests.KarmaRequest(inputs, "BOD", 3, 25));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, planned.Outcome, string.Join(",", planned.Blockers));
        var service = Service(context, store, owners);
        var choices = planned.Value!.Selections;
        var reviewed = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var command = Command(reviewed.Value!, "completion-attribute-karma-source-drift");
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            CharacterCreationBuildMethods.Priority, out string profileId));
        string path = Path.Combine(context.Directory, "source", "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(profileId));
        var files = Snapshot(context);
        int sourceChanges = 0;
        void ChangeSource()
        {
            var source = XDocument.Load(path);
            source.Root!.Element("settings")!.Elements("setting").Single(row => row.Element("id")!.Value == profileId)
                .SetElementValue("karmaattribute", "6");
            source.Save(path);
            files[Path.GetRelativePath(context.Directory, path)] = File.ReadAllBytes(path);
            sourceChanges++;
        }
        if (changeBeforeRename) probe.AfterTempFileFlushed = ChangeSource;
        else ChangeSource();
        var denied = service.Apply(owners.Capture(), command);
        Assert.IsNull(denied.Value);
        Assert.AreEqual(1, sourceChanges);
        Assert.AreEqual(0, probe.Replacements);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Completion_attribute_karma_plan_applies_atomically_and_cold_replays_exactly_without_new_player_intent()
    {
        using var context = ReadyContext.Create(false, playerChosenAttributeIds: []);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var inputs = Inputs(context, store, owners);
        var before = store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        var planned = CharacterCreationCompletionPlanner.Plan(inputs,
            CharacterCreationCompletionPlannerTests.KarmaRequest(inputs, "BOD", 3, 25));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, planned.Outcome, string.Join(",", planned.Blockers));
        var plan = planned.Value!;
        Assert.AreEqual((int?)0, plan.Review.Balances.RemainingCreationKarma);
        var service = Service(context, store, owners);
        var reviewed = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, plan.Selections), CashChoice(context, inputs, plan.Selections));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        Assert.AreEqual(plan.Review.ReviewDigest, reviewed.Value!.Projection.ReviewDigest);
        AssertUnchanged(context, files);
        var command = Command(reviewed.Value, "completion-attribute-karma-cold-replay");
        Assert.Throws<OperationCanceledException>(() => service.Apply(owners.Capture(), command, new CancellationToken(true)));
        AssertUnchanged(context, files);
        var applied = service.Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(1, probe.Replacements);
        var cold = new FileWorkspaceStore(context.Directory);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success, reopened.Error);
        var after = reopened.Value!;
        AssertCommitted(before, after, applied.Value!, reviewed.Value.Projection);
        Assert.AreEqual(0, after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!
            .State.CharacterCreationAttributesDraft!.PlayerChosenAttributeIds!.Count);
        var committedFiles = Snapshot(context);
        var replay = Service(context, cold, owners).Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        Assert.AreEqual(after.ContentRevision, cold.Get(context.WorkspaceId).Value!.ContentRevision);
        AssertUnchanged(context, committedFiles);
    }
}
