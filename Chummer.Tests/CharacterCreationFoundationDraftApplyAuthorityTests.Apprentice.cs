using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    private const string ApprenticeTalentId = "c1d4d7ec-9ebb-4e85-ae72-8155b0f27478";
    private const string ApprenticeAirId = "380a4860-e5b7-4d07-9b8f-24951c1d656a";
    private static CharacterCreationLifeModuleTalentSelection ApprenticeChoice()
        => new(ApprenticeTalentId) { Restrictions = new("Combat", ApprenticeAirId) };

    [TestMethod]
    public void Life_module_Apprentice_catalog_binds_categories_and_keeps_old_choice_serialization()
    {
        var resolver = new FileSystemCharacterSourceDataResolver(CreateOverlays());
        var legacy = resolver.TryCreateContext(CharacterXml("Human"))!;
        Assert.IsTrue(legacy.TryResolveCreationLifeModuleTalents(out var legacyCatalog));
        var legacyTalent = legacyCatalog!.Options.Single(row => row.OptionId == ApprenticeTalentId);
        Assert.IsFalse(legacyTalent.IsEnabled, "An old profile must not silently enable its disabled source books.");
        CollectionAssert.Contains(legacyTalent.Blockers.ToArray(), CharacterCreationKarmaTalentCatalog.SourceDisabled);
        var context = resolver.TryCreateContext(CharacterXml("Human").Replace(CanonicalLifeModuleSettingsId,
            CharacterCreationBootstrapProfiles.LifeModulesSettingsProfileId, StringComparison.Ordinal))!;
        Assert.IsTrue(context.TryResolveCreationLifeModuleTalents(out var catalog));
        var talent = catalog!.Options.Single(row => row.OptionId == ApprenticeTalentId);
        Assert.IsTrue(talent.IsEnabled, string.Join(", ", talent.Blockers));
        Assert.IsNotNull(talent.Restrictions);
        Assert.AreEqual(catalog.SourceInputsDigest, talent.Restrictions.QualitySourceInputsDigest);
        Assert.IsEmpty(catalog.SkillUnlockChoices[ApprenticeTalentId]);
        Assert.IsTrue(context.TryResolveCreationLifeModuleTalentSource(ApprenticeTalentId, out var source));
        Assert.AreEqual(source!.SourceNodeDigest, talent.Restrictions.QualitySourceNodeDigest);
        Assert.IsTrue(talent.Restrictions.SpellCategories.Any(row => row.Value == "Combat"));
        Assert.AreEqual("Spirit of Air", talent.Restrictions.SpiritCategories.Single(row => row.SourceId == ApprenticeAirId).Name);
        CollectionAssert.Contains(catalog.SourceAnchorIds.ToArray(), "spells.xml");
        CollectionAssert.Contains(catalog.SourceAnchorIds.ToArray(), "traditions.xml");
        Assert.AreEqual(JsonSerializer.Serialize(new { OptionId = "mundane", SkillUnlock = (string?)null }),
            JsonSerializer.Serialize(new CharacterCreationLifeModuleTalentSelection("mundane")));
        Assert.AreEqual(ApprenticeChoice(), JsonSerializer.Deserialize<CharacterCreationLifeModuleTalentSelection>(JsonSerializer.Serialize(ApprenticeChoice())));
    }

    [TestMethod]
    public void Life_module_Apprentice_requires_both_answers_and_never_defaults_or_writes_a_partial_grant()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory, settingsProfileId: CharacterCreationBootstrapProfiles.LifeModulesSettingsProfileId);
            var (effects, _) = BuildFullGraph(fixture.Store, fixture.Id);
            var state = Load(CreateService(fixture.Store), fixture.Id);
            var workspace = fixture.Store.Get(fixture.Id).Value!;
            var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(workspace.Document.Content)!;
            var racial = CharacterCreationLifeModuleMetatypeWritePlanner.Build(workspace, state.PendingDraft!, effects.Plan!,
                state.MetatypeOptions.Single(item => item.Label == state.PendingDraft!.RequestedMetatype), context).Plan!;
            var invalid = new[]
            {
                new CharacterCreationLifeModuleTalentSelection(ApprenticeTalentId),
                ApprenticeChoice() with { Restrictions = new("", ApprenticeAirId) },
                ApprenticeChoice() with { Restrictions = new("Combat", "Spirit of Air") },
                ApprenticeChoice() with { Restrictions = new("unrecognized", ApprenticeAirId) },
                ApprenticeChoice() with { SkillUnlock = "Sorcery" },
                ApprenticeChoice() with { OptionId = "mundane" },
                ApprenticeChoice() with { OptionId = MagicianTalentId }
            };
            foreach (var selection in invalid)
            {
                var result = CharacterCreationLifeModuleTalentWritePlanner.Build(workspace.Document.Content, effects.Plan!, racial, selection, context);
                Assert.IsNull(result.Plan, JsonSerializer.Serialize(selection));
                CollectionAssert.Contains(result.Blockers.ToArray(), selection.Restrictions is null
                    ? CharacterCreationLifeModuleTalentCatalog.RestrictionsRequired : CharacterCreationLifeModuleTalentCatalog.SelectionInvalid);
            }
            var valid = CharacterCreationLifeModuleTalentWritePlanner.Build(workspace.Document.Content, effects.Plan!, racial, ApprenticeChoice(), context);
            Assert.IsNotNull(valid.Plan, string.Join(", ", valid.Blockers));
            Assert.AreEqual(15, valid.Plan.Talent.KarmaCost);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_Apprentice_retains_restrictions_both_skill_groups_and_exact_cost_through_Career()
    {
        string directory = CreateTempDirectory();
        try
        {
            var f = LifeCharacterProjectionFixture(directory, ApprenticeTalentId);
            Assert.IsTrue(f.Preview.CanApply, string.Join(", ", f.Preview.FinalizationBlocked));
            Assert.AreEqual(15, f.Preview.TalentWriteSummary!.KarmaCost);
            Assert.AreEqual(5, f.Preview.MagicQuote!.Cost.TotalKarma);
            var allowed = f.Preview.SkillsQuote!.AllowedActiveSkillSourceIds.ToHashSet(StringComparer.Ordinal);
            foreach (var skill in f.Preview.SkillsCatalog!.ActiveSkills.Where(row => row.Category is "Magical Active" or "Resonance Active"))
                Assert.AreEqual(skill.Category == "Magical Active" && (string.IsNullOrEmpty(skill.SkillGroup)
                    || skill.SkillGroup is "Sorcery" or "Conjuring"), allowed.Contains(skill.SourceSkillId), skill.Name);
            var service = CreateService(new FileWorkspaceStore(directory));
            var wrongSpell = LifeMagicOption(f.Preview.MagicCatalog!, "spell", row => row.Category == "Health").Identity;
            var wrong = service.PreviewFinalization(f.Request with { MagicSelection = f.Request.MagicSelection! with { Spells = [wrongSpell] } }).Value!;
            Assert.IsFalse(wrong.CanApply);
            CollectionAssert.Contains(wrong.FinalizationBlocked.ToArray(), CharacterCreationMagicResonanceBlockers.SpellSelectionNotAllowed);
            var committed = service.ConfirmFinalization(LifeFinalizationCommand(f));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, committed.Outcome, string.Join(", ", committed.Blockers));
            var saved = new FileWorkspaceStore(directory).Get(f.Request.Binding.WorkspaceId).Value!;
            var root = XElement.Parse(saved.Document.Content);
            Assert.AreEqual("True", root.Element("created")!.Value);
            Assert.AreEqual("Spirit of Air", root.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("sourceid")?.Value == ApprenticeTalentId).Element("extra")!.Value);
            var improvements = root.Element("improvements")!.Elements("improvement").ToArray();
            Assert.AreEqual("Combat", improvements.Single(row => row.Element("improvementttype")?.Value == "LimitSpellCategory").Element("improvedname")!.Value);
            Assert.AreEqual("Spirit of Air", improvements.Single(row => row.Element("improvementttype")?.Value == "LimitSpiritCategory").Element("improvedname")!.Value);
            CollectionAssert.AreEquivalent(new[] { "Sorcery", "Conjuring" }, improvements
                .Where(row => row.Element("improvementttype")?.Value == "SpecialSkills").Select(row => row.Element("improvedname")!.Value).ToArray());
            Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(saved.Id, saved.ContentRevision, saved.Document.AuxiliaryState));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
