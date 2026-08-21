using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterTraditionSpiritCategoryRulesTests
{
    private static readonly Guid TraditionId = Guid.Parse("91111111-9111-9111-9111-911111111111");

    private static readonly string[] Catalog =
    [
        "Spirit of Fire",
        "Spirit of Air",
        "Spirit of Water"
    ];

    [TestMethod]
    public void CustomMagicalTradition_projects_five_filtered_fields_and_blank()
    {
        Assert.IsTrue(CharacterTraditionSpiritCategoryRules.TryCreateSemantics(
            TraditionId,
            CharacterTraditionNameRules.CustomMagicalTraditionSourceId,
            "MAG",
            magicEnabled: true,
            resonanceEnabled: false,
            Fields(combat: "Spirit of Fire", detection: string.Empty),
            Catalog,
            ["Spirit of Fire", "Spirit of Air"],
            out CharacterTraditionSpiritCategorySemantics semantics));

        CollectionAssert.AreEqual(
            new[] { string.Empty, "Spirit of Fire", "Spirit of Air" },
            semantics.AllowedSpiritNames.ToArray());
        Assert.AreEqual(5, semantics.Fields.Count);
        Assert.AreEqual(5, semantics.Fields.Select(field => field.Revision).Distinct().Count());
        Assert.IsTrue(CharacterTraditionSpiritCategoryRules.TryValidateRequestedValue(
            semantics,
            CharacterTraditionSpiritCategory.Detection,
            semantics.Fields.Single(field => field.Category == CharacterTraditionSpiritCategory.Detection).Revision,
            string.Empty,
            out string blank));
        Assert.AreEqual(string.Empty, blank);
    }

    [TestMethod]
    public void NonCustom_resonance_missing_source_and_excluded_current_value_fail_closed()
    {
        CharacterTraditionSpiritCategoryValue[] fields = Fields();
        Assert.IsFalse(CharacterTraditionSpiritCategoryRules.TryCreateSemantics(
            TraditionId, Guid.NewGuid(), "MAG", true, false, fields, Catalog, [], out _));
        Assert.IsFalse(CharacterTraditionSpiritCategoryRules.TryCreateSemantics(
            TraditionId,
            CharacterTraditionNameRules.CustomMagicalTraditionSourceId,
            "RES",
            false,
            true,
            fields,
            Catalog,
            [],
            out _));
        Assert.IsFalse(CharacterTraditionSpiritCategoryRules.TryCreateSemantics(
            TraditionId,
            CharacterTraditionNameRules.CustomMagicalTraditionSourceId,
            "MAG",
            true,
            false,
            fields,
            [],
            [],
            out _));
        Assert.IsFalse(CharacterTraditionSpiritCategoryRules.TryCreateSemantics(
            TraditionId,
            CharacterTraditionNameRules.CustomMagicalTraditionSourceId,
            "MAG",
            true,
            false,
            Fields(combat: "Spirit of Water"),
            Catalog,
            ["Spirit of Fire"],
            out _));
    }

    [TestMethod]
    public void Revisions_are_field_local_and_catalog_drift_invalidates_all_five()
    {
        Assert.IsTrue(Project(Fields(), Catalog, out CharacterTraditionSpiritCategorySemantics original));
        Assert.IsTrue(Project(
            Fields(combat: "Spirit of Fire"),
            Catalog,
            out CharacterTraditionSpiritCategorySemantics combatChanged));

        foreach (CharacterTraditionSpiritCategory category in CharacterTraditionSpiritCategoryRules.Categories)
        {
            string before = Revision(original, category);
            string after = Revision(combatChanged, category);
            if (category == CharacterTraditionSpiritCategory.Combat)
            {
                Assert.AreNotEqual(before, after);
            }
            else
            {
                Assert.AreEqual(before, after);
            }
        }

        Assert.IsTrue(Project(
            Fields(),
            [.. Catalog, "Guardian Spirit"],
            out CharacterTraditionSpiritCategorySemantics catalogChanged));
        foreach (CharacterTraditionSpiritCategory category in CharacterTraditionSpiritCategoryRules.Categories)
        {
            Assert.AreNotEqual(Revision(original, category), Revision(catalogChanged, category));
        }

        Assert.IsTrue(Project(
            Fields(combat: "Spirit of Fire"),
            Catalog,
            ["Spirit of Fire", "Spirit of Air"],
            out CharacterTraditionSpiritCategorySemantics filtered));
        Assert.IsTrue(Project(
            Fields(combat: "Spirit of Fire"),
            [.. Catalog, "Guardian Spirit"],
            ["Spirit of Fire", "Spirit of Air"],
            out CharacterTraditionSpiritCategorySemantics filteredCatalogChanged));
        CollectionAssert.AreEqual(
            filtered.AllowedSpiritNames.ToArray(),
            filteredCatalogChanged.AllowedSpiritNames.ToArray());
        foreach (CharacterTraditionSpiritCategory category in CharacterTraditionSpiritCategoryRules.Categories)
        {
            Assert.AreNotEqual(
                Revision(filtered, category),
                Revision(filteredCatalogChanged, category));
        }
    }

    private static bool Project(
        IReadOnlyList<CharacterTraditionSpiritCategoryValue> fields,
        IReadOnlyList<string> catalog,
        out CharacterTraditionSpiritCategorySemantics semantics)
        => Project(fields, catalog, [], out semantics);

    private static bool Project(
        IReadOnlyList<CharacterTraditionSpiritCategoryValue> fields,
        IReadOnlyList<string> catalog,
        IReadOnlyList<string> limits,
        out CharacterTraditionSpiritCategorySemantics semantics)
        => CharacterTraditionSpiritCategoryRules.TryCreateSemantics(
            TraditionId,
            CharacterTraditionNameRules.CustomMagicalTraditionSourceId,
            "MAG",
            magicEnabled: true,
            resonanceEnabled: false,
            fields,
            catalog,
            limits,
            out semantics);

    private static string Revision(
        CharacterTraditionSpiritCategorySemantics semantics,
        CharacterTraditionSpiritCategory category)
        => semantics.Fields.Single(field => field.Category == category).Revision;

    private static CharacterTraditionSpiritCategoryValue[] Fields(
        string combat = "",
        string detection = "",
        string health = "",
        string illusion = "",
        string manipulation = "")
        =>
        [
            new(CharacterTraditionSpiritCategory.Combat, combat),
            new(CharacterTraditionSpiritCategory.Detection, detection),
            new(CharacterTraditionSpiritCategory.Health, health),
            new(CharacterTraditionSpiritCategory.Illusion, illusion),
            new(CharacterTraditionSpiritCategory.Manipulation, manipulation)
        ];
}
