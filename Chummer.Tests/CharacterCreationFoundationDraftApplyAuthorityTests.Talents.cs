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
    private const string MagicianTalentId = "0e741331-d776-4be8-abc5-4101228abdef";
    private const string AdeptTalentId = "55247bdc-c313-4614-ae15-5012308096ff";
    private const string MysticTalentId = "9d53e1e4-3f31-40cb-bfbe-4b94f5ba757e";
    private const string TechnomancerTalentId = "c4b35412-bd91-45b4-b428-29da7edd5ff4";
    private const string AspectedTalentId = "4adeb2d4-e42e-4b7a-9a5d-3df325ae59a5";

    [TestMethod]
    public void Life_module_talents_have_their_own_profile_catalog_and_typed_unlock_choices()
    {
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(CharacterXml("Human"))!;
        Assert.IsTrue(context.TryResolveCreationLifeModuleTalents(out var catalog));
        Assert.IsNotNull(catalog);
        Assert.AreEqual(CharacterCreationLifeModuleTalentCatalog.SchemaV1, catalog.Schema);
        Assert.AreEqual(CanonicalLifeModuleSettingsId, catalog.SettingsProfileId);
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(catalog with { AuthorityDigest = string.Empty }), catalog.AuthorityDigest);
        Assert.IsFalse(context.TryResolveCreationKarmaTalents(out _), "Do not relabel a Karma catalog as Life Modules.");
        Assert.IsTrue(context.TryResolveCreationLifeModuleTalentSource("mundane", out var mundane));
        Assert.IsNull(mundane);
        Assert.IsFalse(context.TryResolveCreationLifeModuleTalentSource("not-a-talent", out _));
        CollectionAssert.AreEqual(new[] { "Sorcery", "Conjuring", "Enchanting" }, catalog.SkillUnlockChoices[AspectedTalentId].ToArray());
        foreach (var id in new[] { MagicianTalentId, AdeptTalentId, MysticTalentId, TechnomancerTalentId, AspectedTalentId })
        {
            var option = catalog.Options.Single(item => item.OptionId == id);
            Assert.IsTrue(option.IsEnabled, string.Join(", ", option.Blockers));
            Assert.IsTrue(context.TryResolveCreationLifeModuleTalentSource(id, out var source), id);
            Assert.IsNotNull(source);
            Assert.AreEqual(option.SourceNodeXml, source.CanonicalSourceXml);
            Assert.AreEqual(catalog.SourceInputsDigest, source.EffectiveSourceDigest);
            Assert.AreEqual(int.Parse(XElement.Parse(source.CanonicalSourceXml).Element("karma")!.Value)
                * catalog.KarmaQuality, option.KarmaCost);
        }
    }

    [TestMethod]
    [DataRow("mundane", null, null, 0)]
    [DataRow(MagicianTalentId, null, "MAG", 0)]
    [DataRow(AdeptTalentId, null, "MAG", 0)]
    [DataRow(MysticTalentId, null, "MAG", 0)]
    [DataRow(TechnomancerTalentId, null, "RES", 1)]
    [DataRow(AspectedTalentId, "Sorcery", "MAG", 0)]
    public void Life_module_talents_plan_exact_grants_after_modules_without_partial_writes(
        string id, string? unlock, string? attribute, int gearCount)
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (effects, baseline) = BuildFullGraph(fixture.Store, fixture.Id);
            var workspace = fixture.Store.Get(fixture.Id).Value!;
            var service = CreateService(fixture.Store);
            var state = Load(service, fixture.Id);
            var draft = state.PendingDraft!;
            var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(workspace.Document.Content)!;
            var racial = CharacterCreationLifeModuleMetatypeWritePlanner.Build(workspace, draft, effects.Plan!,
                state.MetatypeOptions.Single(item => item.Label == draft.RequestedMetatype), context).Plan!;
            Assert.IsNotNull(baseline.TalentCatalog);
            Assert.IsNull(baseline.TalentWriteSummary, "Missing choice must never become Mundane.");
            CollectionAssert.Contains(baseline.FinalizationBlocked.ToArray(), CharacterCreationLifeModuleTalentCatalog.SelectionRequired);
            var choice = new CharacterCreationLifeModuleTalentSelection(id, unlock);
            var result = CharacterCreationLifeModuleTalentWritePlanner.Build(workspace.Document.Content, effects.Plan!, racial, choice, context);
            Assert.IsNotNull(result.Plan, string.Join(", ", result.Blockers));
            var plan = result.Plan;
            Assert.AreEqual(attribute, plan.Talent.EnabledAttribute);
            Assert.AreEqual(gearCount, plan.GearXml.Count);
            Assert.AreEqual(id == "mundane" ? 0 : 1, plan.QualityXml.Count);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(plan with { PlanDigest = string.Empty }), plan.PlanDigest);
            if (id != "mundane")
            {
                var quality = XElement.Parse(plan.QualityXml.Single());
                Assert.AreEqual("Selected", quality.Element("qualitysource")!.Value);
                Assert.AreEqual("False", quality.Element("contributetolimit")!.Value);
                Assert.AreEqual(id, quality.Element("sourceid")!.Value);
                var enable = plan.ImprovementXml.Select(xml => XElement.Parse(xml)).Single(item => item.Element("unique")?.Value == "enableattribute");
                Assert.AreEqual("0", enable.Element("val")!.Value, "A talent does not grant a Priority/free attribute rating.");
                Assert.AreEqual(attribute, enable.Element("improvedname")!.Value);
            }
            if (gearCount == 1)
            {
                Assert.AreEqual("Living Persona", XElement.Parse(plan.GearXml.Single()).Element("name")!.Value);
                Assert.HasCount(1, plan.Source!.GrantedGearSources!);
            }
            var prompt = baseline.ModuleSequence!.QualityLevels.Single().InstancePrompt!;
            var request = QualityInstanceRequest(service, fixture.Id,
                new Dictionary<string, string> { [prompt.PromptId] = "Renraku" }) with { TalentSelection = choice };
            var preview = service.PreviewFinalization(request).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(plan.Summary), JsonSerializer.Serialize(preview.TalentWriteSummary));
            Assert.AreNotEqual(baseline.PreviewDigest, preview.PreviewDigest);
            Assert.IsFalse(preview.CanApply);
            Assert.IsNotNull(preview.AttributeQuote);
            Assert.AreEqual(plan.PlanDigest, preview.AttributeQuote.TalentPlanDigest);
            Assert.IsNotNull(preview.SkillsQuote, string.Join(", ", preview.FinalizationBlocked));
            Assert.IsNotNull(preview.SkillsCatalog);
            Assert.AreEqual(plan.PlanDigest, preview.SkillsQuote.TalentPlanDigest);
            var unlocked = preview.SkillsQuote.AllowedActiveSkillSourceIds.ToHashSet(StringComparer.Ordinal);
            foreach (var skill in preview.SkillsCatalog.ActiveSkills.Where(item => item.Category is "Magical Active" or "Resonance Active"))
            {
                bool permitted = id switch
                {
                    MagicianTalentId or MysticTalentId => skill.Category == "Magical Active",
                    AdeptTalentId => skill.Category == "Magical Active" && string.IsNullOrEmpty(skill.SkillGroup),
                    TechnomancerTalentId => skill.Category == "Resonance Active",
                    AspectedTalentId => skill.Category == "Magical Active" && (string.IsNullOrEmpty(skill.SkillGroup) || skill.SkillGroup == unlock),
                    _ => false
                };
                Assert.AreEqual(permitted, unlocked.Contains(skill.SourceSkillId), skill.Name);
            }
            Assert.HasCount(attribute is null ? 9 : 10, preview.AttributeQuote.Attributes);
            if (attribute is not null)
            {
                var before = preview.AttributeQuote.Attributes.Single(item => item.AttributeId == attribute);
                Assert.AreEqual(racial.Metatype.Attributes.Single(item => item.AttributeId == attribute).Minimum, before.Current);
                Assert.AreEqual(0, before.KarmaCost);
                var purchased = service.PreviewFinalization(request with { AttributePurchases = [new(attribute, 1)] }).Value!;
                Assert.IsNotNull(purchased.AttributeQuote);
                var value = purchased.AttributeQuote.Attributes.Single(item => item.AttributeId == attribute);
                Assert.AreEqual(before.Current + 1, value.Current);
                int costBase = purchased.AttributeQuote.Policy.AlternateMetatypeAttributeKarma ? 1 : before.Current;
                Assert.AreEqual((costBase + 1) * purchased.AttributeQuote.Policy.KarmaAttribute, value.KarmaCost);
                Assert.AreNotEqual(preview.PreviewDigest, purchased.PreviewDigest);
            }
            string disabled = attribute == "MAG" ? "RES" : "MAG";
            var invalid = service.PreviewFinalization(request with { AttributePurchases = [new(disabled, 1)] }).Value!;
            Assert.IsNull(invalid.AttributeQuote);
            CollectionAssert.Contains(invalid.FinalizationBlocked.ToArray(), CharacterCreationAttributesBlockers.AllocationInvalid);
            var omitted = service.ConfirmFinalization(new(request.Binding, request.DraftRevision, request.DraftDigest,
                preview.PreviewDigest, true) { QualityInstanceValues = request.QualityInstanceValues });
            CollectionAssert.Contains(omitted.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            var exact = service.ConfirmFinalization(new(request.Binding, request.DraftRevision, request.DraftDigest,
                preview.PreviewDigest, true) { QualityInstanceValues = request.QualityInstanceValues, TalentSelection = choice });
            Assert.IsFalse(exact.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch));
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(request).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(preview.TalentWriteSummary), JsonSerializer.Serialize(reopened.TalentWriteSummary));
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)), "Runner and Origin bytes stay untouched.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("missing-unlock")]
    [DataRow("wrong-unlock")]
    [DataRow("mundane-unlock")]
    [DataRow("catalog-drift")]
    [DataRow("profile-mix")]
    [DataRow("price-tamper")]
    [DataRow("source-drift")]
    [DataRow("source-mismatch")]
    [DataRow("quality-conflict")]
    [DataRow("effect-digest")]
    public void Life_module_talents_reject_changed_sources_choices_and_conflicting_grants(string fault)
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (effectsResult, _) = BuildFullGraph(fixture.Store, fixture.Id);
            var workspace = fixture.Store.Get(fixture.Id).Value!;
            var state = Load(CreateService(fixture.Store), fixture.Id);
            var real = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(workspace.Document.Content)!;
            var effects = effectsResult.Plan!;
            var racial = CharacterCreationLifeModuleMetatypeWritePlanner.Build(workspace, state.PendingDraft!, effects,
                state.MetatypeOptions.Single(item => item.Label == state.PendingDraft!.RequestedMetatype), real).Plan!;
            var selection = fault switch
            {
                "unknown" => new CharacterCreationLifeModuleTalentSelection("unknown"),
                "missing-unlock" => new(AspectedTalentId),
                "wrong-unlock" => new(AspectedTalentId, "Technomancer"),
                "mundane-unlock" => new("mundane", "Sorcery"),
                _ => new(MagicianTalentId)
            };
            if (fault == "effect-digest") effects = effects with { ImprovementXml = [] };
            if (fault == "quality-conflict")
            {
                effects = effects with { QualityXml = effects.QualityXml.Append(
                    $"<quality><name>Adept</name><sourceid>{AdeptTalentId}</sourceid><qualitysource>Selected</qualitysource></quality>").ToArray() };
                effects = effects with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(effects with { PlanDigest = string.Empty }) };
                racial = racial with { EffectPlanDigest = effects.PlanDigest };
                racial = racial with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(racial with { PlanDigest = string.Empty }) };
            }
            var result = CharacterCreationLifeModuleTalentWritePlanner.Build(workspace.Document.Content, effects, racial,
                selection, new TalentFaultContext(real, fault));
            Assert.IsNull(result.Plan, fault);
            string expectedBlocker = fault switch
            {
                "unknown" or "wrong-unlock" or "mundane-unlock" => CharacterCreationLifeModuleTalentCatalog.SelectionInvalid,
                "missing-unlock" => CharacterCreationLifeModuleTalentCatalog.SkillUnlockRequired,
                "catalog-drift" or "source-drift" => CharacterCreationFoundationBlockers.SourceDigestConflict,
                "quality-conflict" => CharacterCreationLifeModuleTalentCatalog.QualityConflict,
                _ => CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict
            };
            CollectionAssert.Contains(result.Blockers.ToArray(), expectedBlocker, fault);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class TalentFaultContext(ICharacterSourceDataContext inner, string fault) : ICharacterSourceDataContext
    {
        private int _catalogReads;
        private int _sourceReads;
        public bool TryResolveCyberwareGradeDeviceRating(string sourceId, string grade, out int rating)
            => inner.TryResolveCyberwareGradeDeviceRating(sourceId, grade, out rating);
        public bool TryResolveVehicleModBonuses(string sourceId, string grade, out CharacterVehicleModSourceBonuses bonuses)
            => inner.TryResolveVehicleModBonuses(sourceId, grade, out bonuses);
        public bool TryResolveCreationFoundationEffectSources(out CharacterCreationFoundationEffectSources? sources)
            => inner.TryResolveCreationFoundationEffectSources(out sources);
        public bool TryResolveCreationLifeModuleTalents(out CharacterCreationLifeModuleTalentCatalog? catalog)
        {
            bool found = inner.TryResolveCreationLifeModuleTalents(out catalog);
            if (catalog is not null && fault == "price-tamper")
            {
                catalog = catalog with { Options = catalog.Options.Select(option => option.OptionId == MagicianTalentId
                    ? option with { KarmaCost = 0 } : option).ToArray() };
                catalog = catalog with { AuthorityDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(catalog with { AuthorityDigest = string.Empty }) };
            }
            if (catalog is not null && (fault == "profile-mix" || fault == "catalog-drift" && ++_catalogReads > 1))
            {
                catalog = catalog with { SettingsProfileId = "wrong-profile" };
                catalog = catalog with { AuthorityDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(catalog with { AuthorityDigest = string.Empty }) };
            }
            return found;
        }
        public bool TryResolveCreationLifeModuleTalentSource(string id, out CharacterCreationTalentQualitySource? source)
        {
            bool found = inner.TryResolveCreationLifeModuleTalentSource(id, out source);
            if (source is not null && (fault == "source-mismatch" || fault == "source-drift" && ++_sourceReads > 1))
                source = source with { CanonicalSourceXml = source.CanonicalSourceXml.Replace("<karma>30</karma>", "<karma>0</karma>", StringComparison.Ordinal) };
            return found;
        }
    }
}
