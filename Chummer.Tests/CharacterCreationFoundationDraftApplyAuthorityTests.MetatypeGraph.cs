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
    public void Metatype_graph_adds_source_owned_racial_quality_without_inventing_gear_or_spending()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (effects, preview) = BuildFullGraph(fixture.Store, fixture.Id);
            Assert.IsNotNull(effects.Plan);
            var workspace = fixture.Store.Get(fixture.Id).Value!;
            var state = Load(CreateService(fixture.Store), fixture.Id);
            var draft = state.PendingDraft!;
            var expected = state.MetatypeOptions.Single(item => item.Label == draft.RequestedMetatype);
            var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(workspace.Document.Content)!;
            var result = CharacterCreationLifeModuleMetatypeWritePlanner.Build(workspace, draft, effects.Plan, expected, context);
            Assert.IsNotNull(result.Plan, string.Join(", ", result.Blockers));
            var plan = result.Plan;
            Assert.IsNotNull(preview.MetatypeWriteSummary, string.Join(", ", preview.FinalizationBlocked));
            Assert.AreEqual(plan.Summary, preview.MetatypeWriteSummary);
            Assert.AreEqual(ElfId, plan.Metatype.OptionId);
            Assert.AreEqual(40, plan.Summary.MetatypeKarmaCost);
            Assert.AreEqual(effects.Plan.PlanDigest, plan.EffectPlanDigest);
            Assert.HasCount(1, plan.QualityXml);
            var quality = XElement.Parse(plan.QualityXml.Single());
            Assert.AreEqual("Low-Light Vision", quality.Element("name")!.Value);
            Assert.AreEqual("Metatype", quality.Element("qualitysource")!.Value);
            Assert.AreEqual("False", quality.Element("contributetolimit")!.Value);
            Assert.IsEmpty(plan.GearXml, "The canonical racial quality has no gear grant; do not invent one.");
            Assert.IsEmpty(plan.ImprovementXml, "Its canonical bonus is empty.");
            Assert.IsEmpty(plan.Flags, "Racial vision does not choose Mundane, Magician or another talent.");
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(plan with { PlanDigest = string.Empty }), plan.PlanDigest);
            var reopenedStore = new FileWorkspaceStore(directory);
            var reopenedEffects = BuildFullGraph(reopenedStore, fixture.Id);
            Assert.AreEqual(JsonSerializer.Serialize(preview.MetatypeWriteSummary), JsonSerializer.Serialize(reopenedEffects.Preview.MetatypeWriteSummary));
            Assert.IsFalse(preview.CanApply);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("selected-metatype")]
    [DataRow("cost")]
    [DataRow("effects")]
    [DataRow("draft")]
    public void Metatype_graph_rejects_changed_inputs_without_partial_quality_or_gear(string tamper)
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (effects, _) = BuildFullGraph(fixture.Store, fixture.Id);
            var workspace = fixture.Store.Get(fixture.Id).Value!;
            var state = Load(CreateService(fixture.Store), fixture.Id);
            var draft = state.PendingDraft!;
            var expected = state.MetatypeOptions.Single(item => item.Label == draft.RequestedMetatype);
            var plan = effects.Plan!;
            if (tamper == "selected-metatype") expected = state.MetatypeOptions.Single(item => item.Label == "Human");
            if (tamper == "cost") expected = expected with { Costs = [] };
            if (tamper == "effects") plan = plan with { QualityXml = [] };
            if (tamper == "draft") draft = draft with { RequestedMetatype = "Human" };
            var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(workspace.Document.Content)!;
            var result = CharacterCreationLifeModuleMetatypeWritePlanner.Build(workspace, draft, plan, expected, context);
            Assert.IsNull(result.Plan);
            Assert.IsNotEmpty(result.Blockers);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(HumanId, 0)]
    [DataRow(ElfId, 1)]
    [DataRow("8ed6892f-88e6-42d0-a704-b805778ec13e", 1)]
    public void Metatype_graph_sources_resolve_exact_grants_without_choosing_a_karma_talent(string metatypeId, int count)
    {
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(CharacterXml("Human"))!;
        Assert.IsTrue(context.TryResolveCreationLifeModuleMetatypeSources(metatypeId, out var sources));
        Assert.HasCount(count, sources);
        Assert.IsTrue(sources.All(CharacterCreationTalentQualitySourceRules.IsValidSource));
        Assert.IsFalse(context.TryResolveCreationKarmaGrantSources(metatypeId, CharacterCreationKarmaTalentCatalog.MundaneOptionId, out _, out _));
        Assert.IsFalse(context.TryResolveCreationLifeModuleMetatypeSources("unknown-metatype", out _));
    }

    [TestMethod]
    public void Metatype_graph_blocks_an_existing_racial_quality_instead_of_duplicating_or_replacing_it()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory, existingQualityXml: "<quality>"
                + "<sourceid>8ec5c9bb-aeb9-42f2-a436-a60f764adfe4</sourceid><name>Low-Light Vision</name>"
                + "<guid>00000000-0000-0000-0000-000000000001</guid><qualitysource>Selected</qualitysource></quality>");
            var (effects, preview) = BuildFullGraph(fixture.Store, fixture.Id);
            Assert.IsNotNull(effects.Plan, "The complete module plan does not own racial-quality reconciliation.");
            Assert.IsNull(preview.MetatypeWriteSummary);
            CollectionAssert.Contains(preview.FinalizationBlocked.ToArray(), CharacterCreationFoundationBlockers.PendingDraftConflict);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("polarity")]
    [DataRow("unknown-bonus")]
    [DataRow("catalog-drift")]
    public void Metatype_graph_requires_complete_stable_sources_even_when_source_nodes_are_rehashed(string fault)
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (effects, _) = BuildFullGraph(fixture.Store, fixture.Id);
            var workspace = fixture.Store.Get(fixture.Id).Value!;
            var state = Load(CreateService(fixture.Store), fixture.Id);
            var draft = state.PendingDraft!;
            var expected = state.MetatypeOptions.Single(item => item.Label == draft.RequestedMetatype);
            var real = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(workspace.Document.Content)!;
            var result = CharacterCreationLifeModuleMetatypeWritePlanner.Build(workspace, draft, effects.Plan!, expected,
                new MetatypeGraphFaultContext(real, fault));
            Assert.IsNull(result.Plan);
            Assert.IsNotEmpty(result.Blockers);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class MetatypeGraphFaultContext(ICharacterSourceDataContext inner, string fault) : ICharacterSourceDataContext
    {
        private int _catalogReads;
        public bool TryResolveCyberwareGradeDeviceRating(string sourceId, string grade, out int rating)
            => inner.TryResolveCyberwareGradeDeviceRating(sourceId, grade, out rating);
        public bool TryResolveVehicleModBonuses(string sourceId, string grade, out CharacterVehicleModSourceBonuses bonuses)
            => inner.TryResolveVehicleModBonuses(sourceId, grade, out bonuses);
        public bool TryResolveCreationMetatypeCatalog(out CharacterCreationMetatypeCatalogAuthority authority)
        {
            bool found = inner.TryResolveCreationMetatypeCatalog(out authority);
            if (fault == "catalog-drift" && ++_catalogReads > 1)
                authority = authority with { SourceContext = authority.SourceContext with { AuthorityDigest = "sha256:" + new string('0', 64) } };
            return found;
        }
        public bool TryResolveCreationFoundationEffectSources(out CharacterCreationFoundationEffectSources? sources)
            => inner.TryResolveCreationFoundationEffectSources(out sources);
        public bool TryResolveCreationLifeModuleMetatypeSources(string id, out IReadOnlyList<CharacterCreationTalentQualitySource> sources)
        {
            bool found = inner.TryResolveCreationLifeModuleMetatypeSources(id, out sources);
            if (fault == "missing") sources = [];
            else if (fault is "polarity" or "unknown-bonus") sources = sources.Select(source =>
            {
                var xml = XElement.Parse(source.CanonicalSourceXml);
                if (fault == "polarity") xml.Element("category")!.Value = "Negative";
                else xml.Element("bonus")!.Add(new XElement("uncompiled-effect", "1"));
                string canonical = xml.ToString(SaveOptions.DisableFormatting);
                return source with { CanonicalSourceXml = canonical,
                    CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(canonical),
                    SourceNodeDigest = CharacterCreationTalentQualitySourceRules.ComputeSourceNodeDigest(source.EffectiveSourceDigest, source.SourceId, canonical) };
            }).ToArray();
            return found;
        }
    }
}
