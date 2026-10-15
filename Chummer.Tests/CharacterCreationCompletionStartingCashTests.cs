using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationCompletionStartingCashTests
{
    [TestMethod]
    public void Completion_starting_cash_resolves_proposed_unsaved_Low_without_saving_or_rolling()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var inputs = Inputs(context, owners);
        var choices = UnsavedChoices(inputs);
        var proposal = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices);
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationSkillsDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationGearDraft);
        Assert.AreEqual(0, inputs.Lifestyles.Lifestyles.Count);
        var source = context.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationDefaultStartingNuyen(out var originalCash));
        var low = inputs.Lifestyles.Authority.LifestyleOptions.Single(row =>
            row.OptionId == choices.Lifestyles.Single().BaseLifestyleOptionId);
        Assert.AreEqual("Low", low.Name);
        Assert.IsTrue(source.TryResolveCreationPriorityStartingNuyen(low.SourceId, out var expectedCash));
        Assert.IsNotNull(originalCash);
        Assert.IsNotNull(expectedCash);
        Assert.AreEqual("Street", originalCash!.Name);
        Assert.AreNotEqual(originalCash.AuthorityDigest, expectedCash!.AuthorityDigest);
        var files = Snapshot(context);

        var result = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expectedCash, result.Value));
        Assert.AreEqual(expectedCash.AuthorityDigest,
            service.LoadStartingCash(owners.Capture(), context.WorkspaceId, proposal).Value!.AuthorityDigest);

        // Only the test supplies this explicit legal roll, after reading its real
        // source terms. A successful source read is not a review or save receipt.
        var reviewed = service.Review(owners.Capture(), context.WorkspaceId, proposal,
            new(result.Value!.AuthorityDigest, result.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        Assert.AreEqual(result.Value.AuthorityDigest, reviewed.Value!.Projection.StartingCash.Choice.SourceAuthorityDigest);
        AssertUnchanged(context, before, files, owners);
    }

    [TestMethod]
    public void Completion_starting_cash_without_a_proposed_lifestyle_uses_real_Street_terms()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var inputs = Inputs(context, owners);
        var choices = UnsavedChoices(inputs) with { Lifestyles = [], StartingLifestyleId = null };
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var source = context.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationDefaultStartingNuyen(out var expected));
        Assert.IsNotNull(expected);
        var files = Snapshot(context);
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);

        var result = service.LoadStartingCash(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.AreEqual("Street", result.Value!.Name);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, result.Value));
        IOwnerBoundCharacterCreationCompletionService unsupported = new CompletionWithoutCashRead();
        var unavailable = unsupported.LoadStartingCash(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, unavailable.Outcome);
        Assert.IsNull(unavailable.Value);
        CollectionAssert.Contains(unavailable.Blockers.ToArray(), CharacterCreationFinalizationBlockers.StartingCashUnavailable);
        AssertUnchanged(context, before, files, owners);
    }

    [TestMethod]
    public void Completion_starting_cash_rejects_stale_snapshot_invalid_selection_and_unaffordable_whole_proposal()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var inputs = Inputs(context, owners);
        var choices = UnsavedChoices(inputs);
        var proposal = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices);
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        string wrongDigest = inputs.SnapshotDigest[..^1]
            + (inputs.SnapshotDigest.EndsWith("a", StringComparison.Ordinal) ? "b" : "a");
        var unknownLifestyle = Guid.Parse("848bf945-b563-424e-a13c-9043549505de");
        CharacterCreationCompletionReviewRequest[] invalid =
        [
            proposal with { InputsSnapshotDigest = wrongDigest },
            proposal with { Selections = choices with { StartingLifestyleId = unknownLifestyle } },
            proposal with { Selections = choices with { Lifestyles = [] } },
            proposal with { Selections = choices with
                { Lifestyles = [choices.Lifestyles.Single() with { BaseLifestyleOptionId = "missing-lifestyle-option" }] } },
            proposal with { Selections = choices with { TotalKarmaInvestment = 1000 } }
        ];
        foreach (var rejectedProposal in invalid)
        {
            var result = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, rejectedProposal);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            Assert.IsNull(result.Value, "An invalid whole proposal must not disclose usable starting-cash authority.");
            Assert.IsTrue(result.Blockers.Count > 0);
            AssertUnchanged(context, before, files, owners);
        }

        // Staleness must also be detected after a real supported save, not just
        // when the caller supplies an incorrect digest string.
        MagicResonanceCompletionTests.SaveAttributes(context, [new("MAG", 1, 0), new("CHA", 1, 0)]);
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision + 1, saved.ContentRevision);
        Assert.IsNotNull(saved.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        var savedFiles = Snapshot(context);
        var stale = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        AssertUnchanged(context, saved, savedFiles, owners);
    }

    [TestMethod]
    public void Completion_starting_cash_rejects_owner_ABA_and_allows_a_fresh_exact_owner_read()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var inputs = Inputs(context, owners);
        var proposal = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, UnsavedChoices(inputs));
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        var originalOwner = owners.Capture();
        owners.Set(new OwnerScope("completion-cash-other-owner"));
        owners.Set(originalOwner.Owner);
        Assert.AreNotEqual(originalOwner, owners.Capture());

        var stale = service.LoadStartingCash(originalOwner, context.WorkspaceId, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        AssertUnchanged(context, before, files, owners);
        var fresh = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, fresh.Outcome, string.Join(",", fresh.Blockers));
        Assert.AreEqual("Low", fresh.Value!.Name);
        AssertUnchanged(context, before, files, owners);
    }

    private static CharacterCreationCompletionReviewInputs Inputs(ReadyContext context,
        CharacterCreationAttributesServiceTests.AllocationOwners owners)
    {
        var result = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputs(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        return result.Value!;
    }

    private static CharacterCreationCompletionSelections UnsavedChoices(CharacterCreationCompletionReviewInputs inputs)
    {
        // Use the same actual unsaved full proposal already exercised by
        // CharacterCreationCompletionApplyTests, without saving intermediate stages.
        var native = inputs.Skills.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var guts = inputs.Lifestyles.Qualities.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("0d0cb0fc-c79d-418e-9216-8a197369e52d") && row.Rating == 1);
        var supplies = inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("76aca45d-0d4f-45a3-a532-c47ac0b26b89") && row.IsSelectable);
        var home = CharacterCreationCompletionReviewServiceTests.LowLifestyle(inputs.Lifestyles.Authority);
        return new([new("MAG", 1, 0)],
            [.. inputs.Skills.Allocations,
                new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                new(pistols.SourceSkillId, CharacterCreationSkillKinds.Active, 2, null, false)],
            inputs.Skills.GroupAllocations,
            MagicResonanceCompletionTests.CompleteSelections(inputs.Magic, inputs.Magic.SelectedTalent.Magic + 1, 1),
            [guts.OptionId], 3, [new(supplies.OptionId, 1)],
            [new(Guid.Parse("0cfb6e5f-9019-40cb-b0f1-420d02c107c3"),
                new CharacterCreationContactIdentity("Street Medic", "Professional", "Seattle", "", "",
                    "Human", "Other", "Adult", "Professional", "Nuyen", "", "", ""), 1, 1)], [home], home.LifestyleId)
        { ChangeableAttributeIds = ["MAG"] };
    }

    private static Dictionary<string, byte[]> Snapshot(ReadyContext context)
        => Directory.EnumerateFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertUnchanged(ReadyContext context, WorkspaceStoredDocument before,
        Dictionary<string, byte[]> files, CharacterCreationAttributesServiceTests.AllocationOwners owners)
    {
        var after = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            before.Document.AuxiliaryState, after.Document.AuxiliaryState));
        var currentFiles = Snapshot(context);
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), currentFiles.Keys.ToArray());
        foreach (var entry in files) CollectionAssert.AreEqual(entry.Value, currentFiles[entry.Key], entry.Key);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private sealed class CompletionWithoutCashRead : IOwnerBoundCharacterCreationCompletionService
    {
        public CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
            OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
            CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
            CancellationToken cancellationToken = default)
            => throw new AssertFailedException("The default cash read must not invent a review or dice roll.");

        public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
            OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
            CancellationToken cancellationToken = default)
            => throw new AssertFailedException("The default cash read must not dispatch a mutation.");
    }
}
