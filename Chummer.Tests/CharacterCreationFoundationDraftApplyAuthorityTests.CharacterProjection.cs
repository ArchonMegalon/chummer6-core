using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow("mundane")]
    [DataRow(MagicianTalentId)]
    [DataRow(AdeptTalentId)]
    [DataRow(MysticTalentId)]
    [DataRow(TechnomancerTalentId)]
    public void Life_module_character_projection_composes_a_complete_runner_without_a_partial_write(string talentId)
    {
        string directory = CreateTempDirectory();
        try
        {
            var f = LifeCharacterProjectionFixture(directory, talentId,
                talentId is MagicianTalentId or TechnomancerTalentId ? "<attributes/><tradition/><stream/>" : null);
            var p = f.Parts;
            Assert.IsTrue(CharacterCreationLifeModuleCharacterProjector.TryProject(f.Xml, p, out var candidate));
            Assert.IsNotNull(candidate);
            var root = XElement.Parse(candidate.CharacterXml);
            Assert.IsTrue(new CharacterFileService().ValidateXml(candidate.CharacterXml).IsValid);
            Assert.AreEqual("True", root.Element("created")!.Value);
            Assert.AreEqual("LifeModule", root.Element("buildmethod")!.Value);
            Assert.AreEqual("Elf", root.Element("metatype")!.Value);
            Assert.AreEqual(p.Racial.Metatype.OptionId, root.Element("metatypeid")!.Value);
            Assert.AreEqual(13, root.Element("attributes")!.Elements("attribute").Count());
            foreach (var value in p.Attributes.Attributes)
            {
                var saved = root.Element("attributes")!.Elements("attribute").Single(row => row.Element("name")!.Value == value.AttributeId);
                Assert.AreEqual(value.Current, (int)saved.Element("totalvalue")!);
                Assert.AreEqual(value.KarmaLevels, (int)saved.Element("karma")!);
                Assert.AreEqual(0, (int)saved.Element("base")!, "Module levels are retained Improvements, not paid base.");
            }
            var essence = root.Element("attributes")!.Elements("attribute").Single(row => row.Element("name")!.Value == "ESS");
            Assert.AreEqual(p.Racial.Metatype.Attributes.Single(row => row.AttributeId == "ESS").Maximum, (int)essence.Element("totalvalue")!);
            Assert.AreEqual(p.Effects.ModuleOwners.Count,
                root.Element("qualities")!.Elements("quality").Count(row => row.Element("qualitysource")!.Value == "LifeModule"));
            Assert.AreEqual(p.Effects.ImprovementXml.Count + p.Racial.ImprovementXml.Count + p.Talent.ImprovementXml.Count,
                root.Element("improvements")!.Elements("improvement").Count());
            Assert.AreEqual(p.Gear.Lines.Count + p.Racial.GearXml.Count + p.Talent.GearXml.Count, root.Element("gears")!.Elements("gear").Count());
            Assert.AreEqual(p.Contacts.Selection.Single().Identity.Name, root.Element("contacts")!.Element("contact")!.Element("name")!.Value);
            Assert.AreEqual(p.Lifestyles.Selection.Single().Name, root.Element("lifestyles")!.Element("lifestyle")!.Element("name")!.Value);
            Assert.AreEqual(p.Finances.KarmaCarried, (int)root.Element("karma")!);
            Assert.AreEqual(p.Finances.CareerNuyen, (decimal)root.Element("nuyen")!);
            Assert.AreEqual(p.Resources.NuyenFromKarma, (decimal)root.Element("startingnuyen")!);
            Assert.AreEqual(p.Magic.Selections.Spells.Count, root.Element("spells")!.Elements("spell").Count());
            Assert.AreEqual(p.Magic.Selections.AdeptPowers.Count, root.Element("powers")!.Elements("power").Count());
            Assert.AreEqual(p.Magic.Selections.ComplexForms.Count, root.Element("complexforms")!.Elements("complexform").Count());
            Assert.AreEqual(p.Magic.Selections.Tradition is not null || p.Magic.Selections.Stream is not null ? 1 : 0,
                root.Elements("tradition").Count());
            Assert.IsFalse(root.Elements("stream").Any());
            Assert.AreEqual(p.Resources.TotalKarma - p.Finances.KarmaBeforeCarryover, candidate.Deltas.Sum(row => row.KarmaCost));
            Assert.AreEqual(p.Lifestyles.Budget.Used, candidate.Deltas.Sum(row => row.NuyenCost));
            Assert.AreEqual(candidate.RawCharacterXmlDigest, f.Preview.FinalizationPlan!.ExpectedResultRawCharacterXmlDigest);
            Assert.AreEqual(candidate.ComponentsDigest, f.Preview.FinalizationPlan.Binding.AuthorityDigest);
            Assert.AreEqual(JsonSerializer.Serialize(candidate.Deltas), JsonSerializer.Serialize(f.Preview.FinalizationPlan.OrderedDeltas));
            Assert.IsFalse(f.Preview.CanApply, "Projection is not atomic finalization authority.");
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(f.Request).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(f.Preview.FinalizationPlan), JsonSerializer.Serialize(reopened.FinalizationPlan));
            CollectionAssert.AreEqual(f.Before, File.ReadAllBytes(WorkspacePath(directory, f.Request.Binding.WorkspaceId)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_character_projection_rejects_rehashed_component_changes()
    {
        string directory = CreateTempDirectory();
        try
        {
            var f = LifeCharacterProjectionFixture(directory, MagicianTalentId);
            var p = f.Parts;
            var alteredMoney = p.Finances with { CareerNuyen = p.Finances.CareerNuyen + 1 };
            alteredMoney = alteredMoney with { QuoteDigest = HashContacts(alteredMoney with { QuoteDigest = string.Empty }) };
            var alteredMagic = p.Magic with { KarmaAfterMagic = p.Magic.KarmaAfterMagic + 1 };
            alteredMagic = alteredMagic with { QuoteDigest = HashContacts(alteredMagic with { QuoteDigest = string.Empty }) };
            var alteredGear = p.Gear with { Lines = p.Gear.Lines.Select(row => row with { Quantity = row.Quantity + 1 }).ToArray() };
            alteredGear = alteredGear with { QuoteDigest = HashContacts(alteredGear with { QuoteDigest = string.Empty }) };
            var alteredBasis = p.Gear with { Basis = p.Gear.Basis with { MaximumAvailability = p.Gear.Basis.MaximumAvailability + 1 } };
            alteredBasis = alteredBasis with { QuoteDigest = HashContacts(alteredBasis with { QuoteDigest = string.Empty }) };
            var alteredContact = p.Contacts with { KarmaUsed = p.Contacts.KarmaUsed + 1 };
            alteredContact = alteredContact with { QuoteDigest = HashContacts(alteredContact with { QuoteDigest = string.Empty }) };
            var alteredSkills = p.Skills with { Skills = p.Skills.Skills.Select(row => row with { Rating = row.Rating + 1 }).ToArray() };
            alteredSkills = alteredSkills with { QuoteDigest = HashContacts(alteredSkills with { QuoteDigest = string.Empty }) };
            foreach (var changed in new[] { p with { Finances = alteredMoney }, p with { Magic = alteredMagic },
                         p with { Gear = alteredGear }, p with { Gear = alteredBasis }, p with { Contacts = alteredContact }, p with { Skills = alteredSkills } })
            {
                Assert.IsFalse(CharacterCreationLifeModuleCharacterProjector.TryProject(f.Xml, changed, out var candidate));
                Assert.IsNull(candidate);
            }
            Assert.IsFalse(CharacterCreationLifeModuleCharacterProjector.TryProject(f.Xml + " ", p, out _));
            CollectionAssert.AreEqual(f.Before, File.ReadAllBytes(WorkspacePath(directory, f.Request.Binding.WorkspaceId)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_character_projection_does_not_overwrite_unrecognized_saved_attribute_state()
    {
        string directory = CreateTempDirectory();
        try
        {
            var f = LifeCharacterProjectionFixture(directory, "mundane",
                "<attributes><attribute><name>Unknown imported modifier</name><base>0</base><karma>0</karma></attribute></attributes>",
                expectProjection: false);
            Assert.IsNull(f.Preview.FinalizationPlan);
            CollectionAssert.Contains(f.Preview.FinalizationBlocked.ToArray(), CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
            Assert.IsFalse(CharacterCreationLifeModuleCharacterProjector.TryProject(f.Xml, f.Parts, out var candidate));
            Assert.IsNull(candidate);
            CollectionAssert.AreEqual(f.Before, File.ReadAllBytes(WorkspacePath(directory, f.Request.Binding.WorkspaceId)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static LifeCharacterProjectionBinding LifeCharacterProjectionFixture(string directory, string talentId,
        string? pendingXmlTail = null, bool expectProjection = true)
    {
        var fixture = SeedFullGraph(directory, pendingXmlTail: pendingXmlTail);
        var (effects, baseline) = BuildFullGraph(fixture.Store, fixture.Id);
        var service = CreateService(fixture.Store);
        var prompt = baseline.ModuleSequence!.QualityLevels.Single().InstancePrompt!;
        var request = QualityInstanceRequest(service, fixture.Id, new Dictionary<string, string> { [prompt.PromptId] = "Renraku" })
            with { TalentSelection = new(talentId), AttributePurchases = talentId == "mundane" ? []
                : [new(talentId == TechnomancerTalentId ? "RES" : "MAG", 3)] };
        var opened = service.PreviewFinalization(request).Value!;
        var native = opened.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        request = request with { SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []),
            KarmaResourceInvestment = 2m, GearSelection = [], LifestyleSelection = [], ContactSelection = [LifeContact(1, 1)],
            MagicSelection = new(null, null, [], [], []), StartingNuyenDiceTotal = 6 };
        var options = service.PreviewFinalization(request).Value!;
        var item = options.GearAuthority!.Options.First(row => row.IsSelectable && row.PackageCost is > 0 and < 1000);
        var low = LifeLifestyle(options.LifestylesAuthority!, "Low");
        var magic = options.MagicCatalog!;
        request = request with { GearSelection = [new(item.OptionId, item.PackageQuantity)], LifestyleSelection = [low],
            StartingLifestyleId = low.LifestyleId,
            MagicSelection = new(talentId is MagicianTalentId or MysticTalentId ? LifeMagicOption(magic, "tradition").Identity : null,
                talentId == TechnomancerTalentId ? LifeMagicOption(magic, "stream").Identity : null,
                talentId is AdeptTalentId or MysticTalentId ? [new(LifeMagicOption(magic, "adept-power", row => row.PointCost <= 1m).Identity, 1)] : [],
                talentId is MagicianTalentId or MysticTalentId ? [LifeMagicOption(magic, "spell").Identity] : [],
                talentId == TechnomancerTalentId ? [LifeMagicOption(magic, "complex-form").Identity] : [])
                { MysticAdeptPowerPoints = talentId == MysticTalentId ? 1 : 0 } };
        var preview = service.PreviewFinalization(request).Value!;
        Assert.IsNotNull(preview.FinalizationBudget, string.Join(", ", preview.FinalizationBlocked));
        if (expectProjection) Assert.IsNotNull(preview.FinalizationPlan, string.Join(", ", preview.FinalizationBlocked));
        var state = Load(service, fixture.Id);
        var workspace = fixture.Store.Get(fixture.Id).Value!;
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(workspace.Document.Content)!;
        var racial = CharacterCreationLifeModuleMetatypeWritePlanner.Build(workspace, state.PendingDraft!, effects.Plan!,
            state.MetatypeOptions.Single(row => row.Label == state.PendingDraft!.RequestedMetatype), context).Plan!;
        var talent = CharacterCreationLifeModuleTalentWritePlanner.Build(workspace.Document.Content, effects.Plan!, racial,
            request.TalentSelection, context).Plan!;
        var parts = new CharacterCreationLifeModuleCharacterParts(effects.Plan!, racial, talent, preview.AttributeQuote!,
            preview.SkillsCatalog!, preview.SkillsQuote!, preview.ResourcesQuote!, preview.GearAuthority!, preview.GearQuote!,
            preview.LifestylesAuthority!, preview.LifestylesQuote!, preview.ContactsQuote!, preview.MagicCatalog!, preview.MagicQuote!, preview.FinalizationBudget!);
        return new(workspace.Document.Content, parts, request, preview, fixture.Before);
    }

    private sealed record LifeCharacterProjectionBinding(string Xml, CharacterCreationLifeModuleCharacterParts Parts,
        CharacterCreationFoundationFinalizationPreviewRequest Request, CharacterCreationFoundationFinalizationPreview Preview, byte[] Before);
}
