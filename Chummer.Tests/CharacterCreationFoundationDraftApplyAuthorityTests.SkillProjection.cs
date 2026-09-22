using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 0)]
    [DataRow(0, 1)]
    [DataRow(1, 1)]
    public void Life_module_skills_legacy_projection_keeps_free_levels_out_of_paid_fields(int personal, int groupPurchase)
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 2m), ("SkillGroupLevel", "Firearms", 1m));
        var source = fixture.Catalog.ActiveSkills.Single(row => row.Name == "Pistols");
        var group = fixture.Catalog.SkillGroups.Single(row => row.Name == "Firearms");
        var native = fixture.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var quote = fixture.Quote(new([new(source.SourceSkillId, source.Kind, personal),
            new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], [new(group.GroupId, groupPurchase)])).Quote!;
        Assert.IsNotNull(quote);
        Assert.IsEmpty(quote.Blockers, string.Join(", ", quote.Blockers));
        string before = JsonSerializer.Serialize(fixture);
        Assert.IsTrue(ProjectLifeSkills(fixture, quote, out var graph, out var deltas));
        Assert.IsNotNull(graph);
        var saved = graph.Element("skills")!.Elements("skill").Single(row => row.Element("name")!.Value == "Pistols");
        var savedGroup = graph.Element("groups")!.Elements("group").Single(row => row.Element("name")!.Value == "Firearms");
        Assert.AreEqual(personal, (int)saved.Element("karma")!);
        Assert.AreEqual(groupPurchase, (int)savedGroup.Element("karma")!);
        Assert.AreEqual(0, (int)saved.Element("base")!);
        Assert.AreEqual(0, (int)savedGroup.Element("base")!);
        // Recombine once as the legacy loader does: paid personal + paid group
        // + retained individual/group Improvements, never effective rating as base.
        Assert.AreEqual(quote.Skills.Single(row => row.Name == "Pistols").Rating,
            (int)saved.Element("karma")! + (int)savedGroup.Element("karma")! + 2 + 1);
        Assert.AreEqual(source.SourceSkillId, saved.Element("suid")!.Value);
        Assert.AreEqual(quote.Groups.Single(row => row.Name == "Firearms").IsBroken,
            bool.Parse(savedGroup.Element("isbroken")!.Value));
        Assert.AreEqual("True", graph.Element("knoskills")!.Elements("skill").Single().Element("isnativelanguage")!.Value);
        Assert.IsFalse(graph.Descendants("improvement").Any());
        Assert.AreEqual(quote.KarmaUsed, deltas.Sum(row => row.KarmaCost));
        Assert.IsTrue(deltas.All(row => row.SourceAnchorIds.Count > 0 && row.DeltaId.StartsWith("life-module-", StringComparison.Ordinal)));
        Assert.AreEqual(deltas.Length, deltas.Select(row => row.DeltaId).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture), "Projection cannot consume or mutate the pending grants.");
        var copy = JsonSerializer.Deserialize<CharacterCreationLifeModuleSkillsQuote>(JsonSerializer.Serialize(quote))!;
        Assert.IsTrue(ProjectLifeSkills(fixture, copy, out var reopened, out var reopenedDeltas));
        Assert.IsTrue(XNode.DeepEquals(XElement.Parse(graph.ToString(SaveOptions.DisableFormatting)), reopened));
        Assert.AreEqual(JsonSerializer.Serialize(deltas), JsonSerializer.Serialize(reopenedDeltas));
    }

    [TestMethod]
    [DataRow("knowledge-point", "False")]
    [DataRow("karma", "True")]
    public void Life_module_skills_legacy_projection_retains_knowledge_and_specialization_payment(string payment, string expected)
    {
        var fixture = SkillMathFixture();
        var native = fixture.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var source = fixture.Catalog.KnowledgeSkills.First(row => !row.CanBeNativeLanguage && row.Specializations.Count > 0);
        var specialization = source.Specializations[0];
        var quote = fixture.Quote(new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true),
            new(source.SourceSkillId, source.Kind, 1, KnowledgePointLevels: 2,
                SpecializationOptionId: specialization.OptionId, SpecializationPayment: payment)], [])).Quote!;
        Assert.IsNotNull(quote);
        Assert.IsEmpty(quote.Blockers, string.Join(", ", quote.Blockers));
        Assert.IsTrue(ProjectLifeSkills(fixture, quote, out var graph, out var deltas));
        var saved = graph!.Element("knoskills")!.Elements("skill").Single(row => row.Element("name")!.Value == source.Name);
        Assert.AreEqual("1", saved.Element("karma")!.Value);
        Assert.AreEqual("2", saved.Element("base")!.Value);
        Assert.AreEqual(expected, saved.Element("buywithkarma")!.Value);
        Assert.AreEqual("False", saved.Element("isnativelanguage")!.Value);
        var spec = saved.Element("specs")!.Elements("spec").Single();
        Assert.AreEqual(specialization.Name, spec.Element("name")!.Value);
        Assert.AreEqual("False", spec.Element("free")!.Value);
        Assert.AreEqual("False", spec.Element("expertise")!.Value);
        Assert.AreEqual(quote.KarmaUsed, deltas.Sum(row => row.KarmaCost));
        Assert.IsTrue(deltas.Any(row => row.SourceAnchorIds.Contains(specialization.SourceAnchorId)));
    }

    [TestMethod]
    [DataRow("rating")]
    [DataRow("cost")]
    [DataRow("source")]
    [DataRow("allocation")]
    [DataRow("group")]
    [DataRow("method")]
    [DataRow("missing-language")]
    public void Life_module_skills_legacy_projection_rejects_rehashed_semantic_changes(string fault)
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 2m), ("SkillGroupLevel", "Firearms", 1m));
        var native = fixture.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var quote = fixture.Quote(new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], [])).Quote!;
        Assert.IsNotNull(quote);
        Assert.IsEmpty(quote.Blockers);
        var skill = quote.Skills.Single(row => row.Name == "Pistols");
        var changed = fault switch
        {
            "rating" => quote with { Skills = quote.Skills.Select(row => row == skill ? row with { Rating = row.Rating + 1 } : row).ToArray() },
            "cost" => quote with { KarmaUsed = quote.KarmaUsed + 1m },
            "source" => quote with { Skills = quote.Skills.Select(row => row == skill ? row with { SourceNodeDigest = "sha256:" + new string('0', 64) } : row).ToArray() },
            "allocation" => quote with { Skills = quote.Skills.Select(row => row == skill ? row with { Allocation = row.Allocation with { KarmaLevels = 1 } } : row).ToArray() },
            "group" => quote with { Groups = quote.Groups.Select(row => row with { ModuleLevels = row.ModuleLevels + 1 }).ToArray() },
            "method" => quote with { Policy = quote.Policy with { Schema = CharacterCreationKarmaSkillsPolicy.SchemaV1 } },
            "missing-language" => quote with { Selection = new([], []) },
            _ => throw new InvalidOperationException()
        };
        if (fault == "method") changed = changed with { Policy = changed.Policy with
            { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(changed.Policy) } };
        changed = changed with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(changed with { QuoteDigest = string.Empty }) };
        Assert.IsFalse(ProjectLifeSkills(fixture, changed, out var graph, out var deltas));
        Assert.IsNull(graph);
        Assert.IsEmpty(deltas);
    }

    private static bool ProjectLifeSkills(SkillMathBinding fixture, CharacterCreationLifeModuleSkillsQuote quote,
        out XElement? graph, out CharacterCreationFinalizationDelta[] deltas)
        => CharacterCreationLifeModuleSkillsLegacyProjector.TryProject(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, fixture.Catalog, quote, out graph, out deltas);
}
