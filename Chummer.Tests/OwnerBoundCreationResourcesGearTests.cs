using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class OwnerBoundCreationResourcesGearTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Owner_only_resources_and_gear_save_reopen_and_reject_foreign_or_old_owners(string method)
    {
        using var fixture = ReadyContext.CreateUnprepared(method);
        string directory = Path.Combine(fixture.Directory, "resources-gear-owner-only");
        var store = new FileWorkspaceStore(directory);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var ownerA = new OwnerScope("resources-gear-owner-a");
        var ownerB = new OwnerScope("resources-gear-owner-b");
        owners.Set(ownerA);
        OwnerContextStamp original = owners.Capture();
        var codec = new Sr5WorkspaceCodec(fixture.Queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(fixture.Resolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(
            new CharacterCreationBootstrapService(store, new RulesetWorkspaceCodecResolver([codec]),
                fixture.Queries, fixture.Resolver), owners);
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(method, out var settings));
        var created = bootstrap.Create(original, new(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
            "Resource owner test", "Resources", method, settings));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome, string.Join(",", created.Blockers));
        var id = created.Value!.WorkspaceId;
        var source = store.Get(ownerA, id).Value!.Document;
        Assert.IsTrue(store.CreateWorkspaceDocument(ownerB, id, new WorkspaceDocument(source.Content, source.RulesetId)).Success);
        var prerequisites = new OwnerBoundCharacterCreationPrerequisiteService(
            store, owners, fixture.Queries, fixture.Resolver);
        var state = prerequisites.Load(original, new(id)).Value!;
        var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
        var heritage = state.Authority.Options.Single(x => x.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && x.Rank == "A").HeritageOptions.First(x => x.IsEnabled && x.MetatypeName == "Human"
                && x.MetavariantSourceId is null);
        var talent = state.Authority.Options.Single(x => x.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && x.Rank == "E").TalentOptions.First(x => x.IsEnabled
                && x.Value.Equals("Mundane", StringComparison.OrdinalIgnoreCase));
        var p = prerequisites.Preview(original, new(state.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            prerequisites.Confirm(original, new(p.Binding, ranks, p.PreviewDigest, true)
                { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Outcome);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, fixture.Resolver);
        var a = attributes.Preview(original, new(attributes.Load(original, new(id)).Value!.Binding, [])).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            attributes.Confirm(original, new(a.Binding, [], a.PreviewDigest, true)).Outcome);
        var before = store.Get(ownerA, id).Value!;
        var other = store.Get(ownerB, id).Value!;
        Assert.IsFalse(store.Get(id).Success, "No legacy copy may mask the native defect.");
        Assert.IsNull(new CharacterCreationResourcesService(store, fixture.Resolver).Load(new(id)).Value);
        Assert.IsNull(new CharacterCreationGearService(store, fixture.Resolver).Load(new(id)).Value);

        var resources = new OwnerBoundCharacterCreationResourcesService(store, owners, fixture.Resolver);
        var loaded = resources.Load(original, new(id));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        Assert.IsTrue(loaded.Value.CanEdit, string.Join(",", loaded.Blockers));
        var option = loaded.Value.Options.Single(x => x.KarmaInvestment == 1 && x.IsEnabled);
        var preview = resources.Preview(original, new(loaded.Value.Binding, option.OptionId)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var command = new CharacterCreationResourcesConfirmRequest(preview.Binding, option.OptionId,
            preview.PreviewDigest, "owner-resources", true);
        foreach (var denied in new[] { default(OwnerContextStamp), original with { Owner = ownerB },
                     original with { AuthorityInstanceId = "foreign" } })
        {
            Assert.IsNull(resources.Load(denied, new(id)).Value);
            Assert.IsNull(resources.Preview(denied, new(preview.Binding, option.OptionId)).Value);
            Assert.IsNull(resources.Confirm(denied, command).Value);
            Assert.IsNull(resources.LookupReceipt(denied, new(id, command.IdempotencyKey)).Value);
        }
        Assert.AreNotEqual(CharacterCreationResourcesOutcomes.Applied,
            resources.Confirm(original, command with { ExplicitlyConfirmed = false }).Outcome);
        Assert.AreNotEqual(CharacterCreationResourcesOutcomes.Applied,
            resources.Confirm(original, command with { PreviewDigest = new string('f', 64) }).Outcome);
        owners.Set(ownerB);
        owners.Set(ownerA);
        Assert.IsNull(resources.Confirm(original, command).Value);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(ownerA, id).Value!.Document.AuxiliaryStateDigest);
        var fresh = owners.Capture();
        var saved = resources.Confirm(fresh, command);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, saved.Outcome, string.Join(",", saved.Blockers));
        var cold = new FileWorkspaceStore(directory);
        var reopened = new OwnerBoundCharacterCreationResourcesService(cold, owners, fixture.Resolver);
        var restored = reopened.Load(fresh, new(id)).Value!;
        Assert.AreEqual(1, restored.PendingDraft!.KarmaInvestment);
        Assert.AreEqual(loaded.Value.Budget.PriorityNuyen + 2000m, restored.Budget.TotalStartingNuyen);
        Assert.AreEqual(saved.Value!.ReceiptDigest,
            reopened.LookupReceipt(fresh, new(id, command.IdempotencyKey)).Value!.ReceiptDigest);
        Assert.AreEqual(saved.Value.ReceiptDigest, reopened.Confirm(fresh, command).Value!.ReceiptDigest);
        Assert.AreEqual(before.ContentRevision + 1, cold.Get(ownerA, id).Value!.ContentRevision);

        var gear = new OwnerBoundCharacterCreationGearService(cold, owners, fixture.Resolver);
        var gearState = gear.Load(fresh, new(id)).Value!;
        Assert.IsTrue(gearState.CanEdit, string.Join(",", gearState.Blockers));
        CharacterCreationGearPreview? gearPreview = null;
        CharacterCreationGearSelection[] basket = [];
        foreach (var candidate in gearState.Authority.Options.Where(x => x.IsSelectable))
        {
            CharacterCreationGearSelection[] trial = [new(candidate.OptionId, 1)];
            var proposal = gear.Preview(fresh, new(gearState.Binding, trial));
            if (proposal.Value is { CanConfirm: true } valid && valid.BudgetAfter.BasketCost > 0)
            {
                basket = trial;
                gearPreview = valid;
                break;
            }
        }
        Assert.IsNotNull(gearPreview, "The real source catalog must allow a nonempty affordable purchase.");
        var gearCommand = new CharacterCreationGearConfirmRequest(gearState.Binding, basket,
            gearPreview.PreviewDigest, "owner-gear", true);
        foreach (var denied in new[] { default(OwnerContextStamp), original,
                     fresh with { Owner = ownerB }, fresh with { AuthorityInstanceId = "foreign" } })
        {
            Assert.IsNull(gear.Load(denied, new(id)).Value);
            Assert.IsNull(gear.Preview(denied, new(gearState.Binding, basket)).Value);
            Assert.IsNull(gear.Confirm(denied, gearCommand).Value);
            Assert.IsNull(gear.LookupReceipt(denied, new(id, gearCommand.IdempotencyKey)).Value);
        }
        Assert.AreNotEqual(CharacterCreationGearOutcomes.Applied,
            gear.Confirm(fresh, gearCommand with { ExplicitlyConfirmed = false }).Outcome);
        Assert.AreNotEqual(CharacterCreationGearOutcomes.Applied,
            gear.Confirm(fresh, gearCommand with { PreviewDigest = new string('f', 64) }).Outcome);
        var beforeGear = cold.Get(ownerA, id).Value!;
        owners.Set(ownerB);
        owners.Set(ownerA);
        Assert.IsNull(gear.Confirm(fresh, gearCommand).Value);
        Assert.AreEqual(beforeGear.Document.AuxiliaryStateDigest, cold.Get(ownerA, id).Value!.Document.AuxiliaryStateDigest);
        var current = owners.Capture();
        var gearSaved = gear.Confirm(current, gearCommand);
        Assert.AreEqual(CharacterCreationGearOutcomes.Applied, gearSaved.Outcome, string.Join(",", gearSaved.Blockers));
        var finalStore = new FileWorkspaceStore(directory);
        var gearReopened = new OwnerBoundCharacterCreationGearService(finalStore, owners, fixture.Resolver);
        var finalState = gearReopened.Load(current, new(id)).Value!;
        Assert.AreEqual(gearPreview.BudgetAfter.BasketCost, finalState.Budget.BasketCost);
        Assert.AreEqual(restored.Budget.TotalStartingNuyen - finalState.Budget.BasketCost, finalState.Budget.RemainingNuyen);
        Assert.AreEqual(gearSaved.Value!.ReceiptDigest,
            gearReopened.LookupReceipt(current, new(id, gearCommand.IdempotencyKey)).Value!.ReceiptDigest);
        Assert.AreEqual(gearSaved.Value.ReceiptDigest, gearReopened.Confirm(current, gearCommand).Value!.ReceiptDigest);
        var after = finalStore.Get(ownerA, id).Value!;
        Assert.AreEqual(before.ContentRevision + 2, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(1, after.Document.AuxiliaryState.CharacterCreationResourcesReceipts!.Count);
        Assert.AreEqual(1, after.Document.AuxiliaryState.CharacterCreationGearReceipts!.Count);
        Assert.AreEqual(other.Document.AuxiliaryStateDigest, finalStore.Get(ownerB, id).Value!.Document.AuxiliaryStateDigest);
        Assert.AreEqual(other.ContentRevision, finalStore.Get(ownerB, id).Value!.ContentRevision);
        Assert.IsFalse(finalStore.Get(id).Success);
        Assert.AreEqual(0, owners.ActiveLeases);
    }
}
