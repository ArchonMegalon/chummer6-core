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
public sealed class OwnerBoundCreationQualitiesTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Owner_only_quality_draft_saves_reopens_and_rejects_foreign_or_old_owners(string method)
    {
        using var fixture = ReadyContext.CreateUnprepared(method);
        string directory = Path.Combine(fixture.Directory, "quality-owner-only");
        var store = new FileWorkspaceStore(directory);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var ownerA = new OwnerScope("qualities-owner-a");
        var ownerB = new OwnerScope("qualities-owner-b");
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
            "Quality owner test", "Quality", method, settings));
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
        Assert.IsFalse(store.Get(id).Success, "A legacy copy must not mask this regression.");
        var old = new CharacterCreationQualitiesService(store, fixture.Resolver,
            new CharacterCreationPrerequisiteService(store, fixture.Queries, fixture.Resolver),
            new CharacterCreationAttributesService(store, fixture.Resolver));
        Assert.IsNull(old.Load(new(id)).Value);
        var service = new OwnerBoundCharacterCreationQualitiesService(store, owners, fixture.Queries, fixture.Resolver);
        var loaded = service.Load(original, new(id));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        var preview = service.Preview(original, new(loaded.Value.Binding, [])).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var command = new CharacterCreationQualitiesConfirmRequest(preview.Binding, [], preview.PreviewDigest,
            "owner-qualities", Guid.NewGuid(), true);
        foreach (var denied in new[] { default(OwnerContextStamp), original with { Owner = ownerB },
                     original with { AuthorityInstanceId = "foreign" } })
        {
            Assert.IsNull(service.Load(denied, new(id)).Value);
            Assert.IsNull(service.Preview(denied, new(preview.Binding, [])).Value);
            Assert.IsNull(service.Confirm(denied, command).Value);
        }
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            service.Confirm(original, command with { ExplicitlyConfirmed = false }).Outcome);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            service.Confirm(original, command with { PreviewDigest = new string('f', 64) }).Outcome);
        owners.Set(ownerB);
        owners.Set(ownerA);
        Assert.IsNull(service.Confirm(original, command).Value);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(ownerA, id).Value!.Document.AuxiliaryStateDigest);
        var fresh = owners.Capture();
        var saved = service.Confirm(fresh, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
        var cold = new FileWorkspaceStore(directory);
        var reopened = new OwnerBoundCharacterCreationQualitiesService(cold, owners, fixture.Queries, fixture.Resolver);
        Assert.IsNotNull(reopened.Load(fresh, new(id)).Value!.PendingDraft);
        Assert.AreEqual(saved.Value!.ReceiptDigest, reopened.Confirm(fresh, command).Value!.ReceiptDigest);
        var after = cold.Get(ownerA, id).Value!;
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest,
            after.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest);
        Assert.AreEqual(1, after.Document.AuxiliaryState.CharacterCreationQualitiesReceipts!.Count);
        Assert.AreEqual(other.Document.AuxiliaryStateDigest, cold.Get(ownerB, id).Value!.Document.AuxiliaryStateDigest);
        Assert.AreEqual(other.ContentRevision, cold.Get(ownerB, id).Value!.ContentRevision);
        Assert.IsFalse(cold.Get(id).Success);
        Assert.AreEqual(0, owners.ActiveLeases);
    }
}
