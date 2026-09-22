using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow("mundane")]
    [DataRow(MagicianTalentId)]
    public void Life_module_atomic_finalization_enters_career_and_recovers_once_after_cold_reopen(string talentId)
    {
        string directory = CreateTempDirectory();
        try
        {
            var f = LifeCharacterProjectionFixture(directory, talentId, withOrigin: talentId == "mundane");
            var store = new FileWorkspaceStore(directory);
            var previous = store.Get(f.Request.Binding.WorkspaceId).Value!;
            var originalBook = LifeFinalizationBook(store).Start(previous.Id.Value).Value;
            if (talentId == "mundane") Assert.IsNotNull(originalBook);
            var request = LifeFinalizationCommand(f);
            var committed = CreateService(store).ConfirmFinalization(request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, committed.Outcome, string.Join(", ", committed.Blockers));
            Assert.IsTrue(committed.Value!.CharacterCreated);
            Assert.IsTrue(committed.Value.CharacterEffectsApplied);
            Assert.IsTrue(committed.Value.RequiresFreshCareerReopen);
            Assert.AreEqual(previous.ContentRevision + 1, committed.Value.ContentRevision);
            var reopened = new FileWorkspaceStore(directory);
            var current = reopened.Get(previous.Id).Value!;
            Assert.IsNotNull(current);
            Assert.AreEqual(current.ContentRevision, current.SavedRevision);
            Assert.AreEqual("True", XElement.Parse(current.Document.Content).Element("created")!.Value);
            Assert.AreEqual(f.Preview.FinalizationPlan!.ExpectedResultRawCharacterXmlDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(current.Document.Content));
            Assert.IsNull(current.Document.AuxiliaryState.CharacterCreationFoundationDraft);
            var archive = current.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
            Assert.AreEqual(previous.Document.AuxiliaryStateDigest, WorkspaceDocumentAuxiliaryStateDigest.Compute(archive.State));
            Assert.AreEqual(JsonSerializer.Serialize(previous.Document.AuxiliaryState), JsonSerializer.Serialize(archive.State));
            Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(current.Id, current.ContentRevision, current.Document.AuxiliaryState));
            Assert.IsTrue(new CharacterFileService().ValidateXml(current.Document.Content).IsValid);
            if (originalBook is not null)
            {
                var book = LifeFinalizationBook(reopened).Start(current.Id.Value);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, book.Outcome, string.Join(", ", book.Blockers));
                Assert.AreEqual(JsonSerializer.Serialize(originalBook), JsonSerializer.Serialize(book.Value));
                Assert.IsTrue(book.Value!.Projection.CurrentTurn.IsTerminal);
                Assert.HasCount(7, book.Value.Projection.VisibleChapters);
            }
            byte[] bytes = File.ReadAllBytes(WorkspacePath(directory, current.Id));
            var replay = CreateService(reopened).ConfirmFinalization(request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(", ", replay.Blockers));
            Assert.AreEqual(committed.Value, replay.Value);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                CreateService(reopened).ConfirmFinalization(request with { StartingNuyenDiceTotal = request.StartingNuyenDiceTotal + 1 }).Outcome);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(WorkspacePath(directory, current.Id)));
            Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(current.Id, current.ContentRevision,
                current.Document.AuxiliaryState with { CharacterCreationFinalizationArchive = null }));
            var altered = archive.LifeModuleAuthority! with { Parts = archive.LifeModuleAuthority.Parts with
                { Finances = archive.LifeModuleAuthority.Parts.Finances with { CareerNuyen = 99999 } } };
            Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(current.Id, current.ContentRevision,
                current.Document.AuxiliaryState with { CharacterCreationFinalizationArchive = archive with { LifeModuleAuthority = altered } }));
            var reputation = new WorkspaceCharacterCareerReputationService(reopened,
                new FileSystemCharacterSourceDataResolver(CreateOverlays()));
            Assert.AreEqual(CharacterCareerReputationOutcome.Available, reputation.Read(current.Id).Outcome);
            var rewards = new WorkspaceCharacterAfterRunRewardService(reopened);
            var reward = rewards.Preview(new(current.Id, Guid.NewGuid(), Guid.NewGuid(), 8, 12500,
                new DateTime(2078, 9, 22, 18, 0, 0), "First Life Modules run"));
            Assert.IsNotNull(reward.Preview, reward.Error);
            Assert.AreEqual(f.Parts.Finances.KarmaCarried + 8, reward.Preview.KarmaAfter);
            Assert.AreEqual(f.Parts.Finances.CareerNuyen + 12500, reward.Preview.NuyenAfter);
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied,
                rewards.Commit(reward.Preview.Command with { ExplicitlyConfirmed = true }).Outcome);
            var afterRun = new FileWorkspaceStore(directory).Get(current.Id).Value!;
            Assert.AreEqual(current.ContentRevision + 1, afterRun.ContentRevision);
            Assert.AreEqual(JsonSerializer.Serialize(archive), JsonSerializer.Serialize(afterRun.Document.AuxiliaryState.CharacterCreationFinalizationArchive));
            if (originalBook is not null)
                Assert.AreEqual(JsonSerializer.Serialize(originalBook), JsonSerializer.Serialize(LifeFinalizationBook(reopened).Start(current.Id.Value).Value));
            Assert.AreEqual(committed.Value, CreateService(reopened).ConfirmFinalization(request).Value);
            Assert.AreEqual(afterRun.ContentRevision, reopened.Get(current.Id).Value!.ContentRevision);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Life_module_atomic_finalization_preserves_before_replace_and_observes_after_replace(bool afterReplace)
    {
        string directory = CreateTempDirectory();
        try
        {
            var f = LifeCharacterProjectionFixture(directory, "mundane");
            var stage = afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed;
            var store = new FileWorkspaceStore(directory, new ThrowingFaultInjector(stage));
            var result = CreateService(store).ConfirmFinalization(LifeFinalizationCommand(f));
            Assert.AreEqual(afterReplace, result.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(", ", result.Blockers));
            if (!afterReplace) CollectionAssert.AreEqual(f.Before, File.ReadAllBytes(WorkspacePath(directory, f.Request.Binding.WorkspaceId)));
            var observed = new FileWorkspaceStore(directory).Get(f.Request.Binding.WorkspaceId).Value!;
            Assert.AreEqual(f.Request.Binding.ContentRevision + (afterReplace ? 1 : 0), observed.ContentRevision);
            Assert.AreEqual(afterReplace, bool.Parse(XElement.Parse(observed.Document.Content).Element("created")!.Value));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_atomic_finalization_rejects_unconfirmed_foreign_owner_and_source_change_at_rename()
    {
        string directory = CreateTempDirectory();
        try
        {
            var f = LifeCharacterProjectionFixture(directory, "mundane");
            var request = LifeFinalizationCommand(f);
            var resolver = new LifeFinalizationSwitchResolver(new FileSystemCharacterSourceDataResolver(CreateOverlays()));
            var store = new FileWorkspaceStore(directory, new LifeFinalizationSourceFault(resolver));
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                CreateService(store).ConfirmFinalization(request with { ExplicitlyConfirmed = false }).Outcome);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                store.CommitLifeModuleFinalization(new OwnerScope("different-owner"), request, resolver, CreateCatalog(),
                    new XmlCharacterFileQueries(new CharacterFileService())).Outcome);
            var result = store.CommitLifeModuleFinalization(request, resolver, CreateCatalog(), new XmlCharacterFileQueries(new CharacterFileService()));
            Assert.IsTrue(resolver.Disabled, "Reach the flush boundary before simulating source removal.");
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            CollectionAssert.AreEqual(f.Before, File.ReadAllBytes(WorkspacePath(directory, f.Request.Binding.WorkspaceId)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static CharacterCreationFoundationFinalizationConfirmRequest LifeFinalizationCommand(LifeCharacterProjectionBinding f)
    {
        var r = f.Request;
        return new(r.Binding, r.DraftRevision, r.DraftDigest, f.Preview.PreviewDigest, true)
        {
            QualityInstanceValues = r.QualityInstanceValues, AttributePurchases = r.AttributePurchases,
            TalentSelection = r.TalentSelection, SkillSelection = r.SkillSelection, KarmaResourceInvestment = r.KarmaResourceInvestment,
            GearSelection = r.GearSelection, LifestyleSelection = r.LifestyleSelection, StartingLifestyleId = r.StartingLifestyleId,
            ContactSelection = r.ContactSelection, StartingNuyenDiceTotal = r.StartingNuyenDiceTotal, MagicSelection = r.MagicSelection
        };
    }

    private static LifeModuleOriginDossierInteractionService LifeFinalizationBook(FileWorkspaceStore store)
        => new(new LifeModuleOriginDossierService(new CharacterCreationFoundationLifeModuleDecisionAuthority(
            store, CreateService(store), new XmlCharacterFileQueries(new CharacterFileService()), () => "de-DE")));

    private static (FileWorkspaceStore Store, CharacterWorkspaceId Id, byte[] Before) SeedFullOriginGraph(string directory)
    {
        var id = new CharacterWorkspaceId("full-origin-effect-graph");
        var store = new FileWorkspaceStore(directory);
        Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(CharacterXml("Elf"), RulesetDefaults.Sr5)).Success);
        var interaction = LifeFinalizationBook(store);
        var checkpoint = interaction.Start(id.Value).Value!;
        Assert.IsFalse(checkpoint.Projection.CurrentTurn.LegalChoices.Any(row => row.ChoiceId ==
            CharacterCreationFoundationLifeModuleDecisionAuthority.Digest(new
            {
                MetatypeOptionId = ElfId, ModuleId = TirModuleId,
                VersionId = "db539cd7-d694-4f9c-9e4c-e24a3bcd0128"
            })), "An Elf must not be offered the Ork/Troll/Dwarf nationality variant.");
        string[] modules = [TirModuleId, FormativeArcologyId, TeenCorporateId, SkipEducationId, BountyHunterId, BountyHunterId];
        var catalog = CreateCatalog().GetOptionProjections().ToArray();
        for (int index = 0; index <= modules.Length; index++)
        {
            var choices = checkpoint.Projection.CurrentTurn.LegalChoices;
            var chosen = index == modules.Length ? choices.SingleOrDefault(row => row.ChoiceId == "finish-life-module-selection")
                : index == 0 ? choices.SingleOrDefault(row => row.ChoiceId == CharacterCreationFoundationLifeModuleDecisionAuthority.Digest(
                    new { MetatypeOptionId = ElfId, ModuleId = TirModuleId, VersionId = TirHumanElfVersionId }))
                : choices.SingleOrDefault(row => row.Label == catalog.Single(module => module.ModuleId == modules[index]).Name);
            Assert.IsNotNull(chosen, $"Missing stage {index} choice; offered: " +
                string.Join("; ", choices.Select(row => row.ChoiceId + " " + row.Label)));
            var answers = chosen.FollowUps?.ToDictionary(prompt => prompt.PromptId,
                prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Renraku");
            var preparation = answers is null ? interaction.Prepare(checkpoint, chosen.ChoiceId)
                : interaction.Prepare(checkpoint, chosen.ChoiceId, answers);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, preparation.Outcome, string.Join(", ", preparation.Blockers));
            var prepared = preparation.Value!;
            var accepted = interaction.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "archive-module-" + index, true);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, accepted.Outcome, string.Join(", ", accepted.Blockers));
            checkpoint = accepted.Value!.Checkpoint;
        }
        Assert.IsTrue(checkpoint.Projection.CurrentTurn.IsTerminal);
        return (store, id, File.ReadAllBytes(WorkspacePath(directory, id)));
    }

    private sealed class LifeFinalizationSwitchResolver(ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public bool Disabled { get; set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml) => Disabled ? null : inner.TryCreateContext(xml);
    }
    private sealed class LifeFinalizationSourceFault(LifeFinalizationSwitchResolver resolver) : IFileWorkspaceStoreFaultInjector
    {
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        { if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) resolver.Disabled = true; }
    }
}
