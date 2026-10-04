using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Source projection for the paired Apprentice prompts. This does not
/// enable a talent, spend Karma, select a default, or mutate a character.</summary>
public static class CharacterCreationTalentRestrictionAuthority
{
    public static bool TryProjectCatalog(CharacterCreationTalentQualitySource quality,
        XElement spellCategories, IReadOnlyList<XElement> spirits,
        string spellsSourceInputsDigest, string spiritsSourceInputsDigest,
        out CharacterCreationTalentRestrictionCatalog? catalog)
    {
        catalog = null;
        if (!HasPairedPrompts(quality)
            || !CharacterCreationSkillsDigest.IsCanonical(spellsSourceInputsDigest)
            || !CharacterCreationSkillsDigest.IsCanonical(spiritsSourceInputsDigest)
            || spellCategories is null || spellCategories.Name != "categories" || spellCategories.HasAttributes
            || !OnlyChildren(spellCategories) || spirits is not { Count: > 0 and <= 512 }) return false;
        XElement[] categories = spellCategories.Elements().ToArray();
        if (categories.Length is 0 or > 256) return false;
        var spells = new List<CharacterCreationTalentSpellCategory>();
        foreach (var category in categories)
        {
            // These are metadata on real Chummer5 spell categories, not nested choices.
            if (category.Name != "category" || category.HasElements || !Identity(category.Value)
                || category.Attributes().Any(attribute => attribute.Name.Namespace != XNamespace.None
                    || attribute.Name.LocalName is not ("translate" or "useskill" or "alchemicalskill" or "barehandedadeptskill")
                    || !Identity(attribute.Value))) return false;
            spells.Add(new(category.Value, category.Attribute("translate")?.Value ?? category.Value,
                NodeDigest(category)));
        }
        var projectedSpirits = new List<CharacterCreationTalentSpiritCategory>();
        foreach (var spirit in spirits)
        {
            if (spirit is null || spirit.Name != "spirit" || spirit.HasAttributes || !OnlyChildren(spirit)
                || spirit.ToString(SaveOptions.DisableFormatting).Length > 128 * 1024
                || !Scalar(spirit, "id", out var id) || !CanonicalId(id)
                || !Scalar(spirit, "name", out var name)
                || !Scalar(spirit, "source", out _) || !Scalar(spirit, "page", out _)
                || spirit.Elements("translate").Count() > 1) return false;
            string label = name;
            if (spirit.Element("translate") is not null && !Scalar(spirit, "translate", out label)) return false;
            projectedSpirits.Add(new(id, name, label, NodeDigest(spirit)));
        }
        if (spells.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() != spells.Count
            || projectedSpirits.Select(item => item.SourceId).Distinct(StringComparer.Ordinal).Count() != projectedSpirits.Count
            || projectedSpirits.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != projectedSpirits.Count)
            return false;
        var result = new CharacterCreationTalentRestrictionCatalog(quality.SourceId, quality.SourceNodeDigest,
            quality.EffectiveSourceDigest, spellsSourceInputsDigest, spiritsSourceInputsDigest,
            spells.ToArray(), projectedSpirits.ToArray(), string.Empty);
        catalog = result with { AuthorityDigest = ComputeDigest(result) };
        return true;
    }

    public static string ComputeDigest(CharacterCreationTalentRestrictionCatalog catalog)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(catalog with { AuthorityDigest = string.Empty });

    internal static bool TryChoose(CharacterCreationTalentQualitySource quality,
        CharacterCreationTalentRestrictionCatalog catalog, CharacterCreationTalentRestrictionSelection? selection,
        out CharacterCreationTalentRestrictionPlan? plan)
    {
        plan = null;
        if (!HasPairedPrompts(quality) || catalog is null || selection is null
            || catalog.QualitySourceId != quality.SourceId || catalog.QualitySourceNodeDigest != quality.SourceNodeDigest
            || catalog.QualitySourceInputsDigest != quality.EffectiveSourceDigest
            || !ValidCatalog(catalog)
            || catalog.SpellCategories.SingleOrDefault(item => item.Value == selection.SpellCategory) is not { } spell
            || catalog.SpiritCategories.SingleOrDefault(item => item.SourceId == selection.SpiritSourceId) is not { } spirit)
            return false;
        // Freeze the selected immutable records, not the caller's mutable catalog lists.
        plan = new(quality.SourceId, quality.SourceNodeDigest, catalog.AuthorityDigest, selection, spell, spirit);
        return true;
    }

    internal static bool MatchesSource(XElement row, CharacterCreationTalentRestrictionCatalog catalog)
        => ValidCatalog(catalog) && HasPairedPrompts(row)
            && row.Element("id")?.Value == catalog.QualitySourceId
            && catalog.QualitySourceNodeDigest == CharacterCreationTalentQualitySourceRules.ComputeSourceNodeDigest(
                catalog.QualitySourceInputsDigest, catalog.QualitySourceId, row.ToString(SaveOptions.DisableFormatting));

    private static bool ValidCatalog(CharacterCreationTalentRestrictionCatalog catalog)
        => catalog is not null && CanonicalId(catalog.QualitySourceId)
            && CharacterCreationSkillsDigest.IsCanonical(catalog.QualitySourceNodeDigest)
            && CharacterCreationSkillsDigest.IsCanonical(catalog.QualitySourceInputsDigest)
            && CharacterCreationSkillsDigest.IsCanonical(catalog.SpellsSourceInputsDigest)
            && CharacterCreationSkillsDigest.IsCanonical(catalog.SpiritsSourceInputsDigest)
            && catalog.SpellCategories is { Count: > 0 and <= 256 }
            && catalog.SpiritCategories is { Count: > 0 and <= 512 }
            && catalog.SpellCategories.All(item => item is not null && Identity(item.Value) && Identity(item.Label)
                && CharacterCreationSkillsDigest.IsCanonical(item.SourceNodeDigest))
            && catalog.SpiritCategories.All(item => item is not null && CanonicalId(item.SourceId)
                && Identity(item.Name) && Identity(item.Label) && CharacterCreationSkillsDigest.IsCanonical(item.SourceNodeDigest))
            && catalog.SpellCategories.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() == catalog.SpellCategories.Count
            && catalog.SpiritCategories.Select(item => item.SourceId).Distinct(StringComparer.Ordinal).Count() == catalog.SpiritCategories.Count
            && catalog.SpiritCategories.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() == catalog.SpiritCategories.Count
            && catalog.AuthorityDigest == ComputeDigest(catalog);

    internal static bool HasPairedPrompts(CharacterCreationTalentQualitySource? quality)
    {
        if (!CharacterCreationTalentQualitySourceRules.IsValidSource(quality)) return false;
        try
        {
            using var text = new StringReader(quality!.CanonicalSourceXml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 });
            return HasPairedPrompts(XElement.Load(reader));
        }
        catch (XmlException) { return false; }
    }

    private static bool HasPairedPrompts(XElement root)
        => root.Name == "quality" && root.Elements("bonus").ToArray() is [var bonus]
            && EmptyPrompt(bonus, "limitspellcategory") && EmptyPrompt(bonus, "limitspiritcategory");

    private static bool EmptyPrompt(XElement bonus, string name)
        => bonus.Elements(name).ToArray() is [var prompt] && !prompt.HasAttributes
            && !prompt.HasElements && string.IsNullOrWhiteSpace(prompt.Value);

    private static bool Scalar(XElement parent, string name, out string value)
    {
        value = string.Empty;
        if (parent.Elements(name).ToArray() is not [var node] || node.HasAttributes || node.HasElements || !Identity(node.Value))
            return false;
        value = node.Value;
        return true;
    }

    private static bool OnlyChildren(XElement node)
        => node.Nodes().All(item => item is XElement or XComment || item is XText text && string.IsNullOrWhiteSpace(text.Value));

    private static bool Identity(string? value)
        => value is { Length: > 0 and <= 256 } && value == value.Trim() && !value.Any(char.IsControl);

    private static bool CanonicalId(string? value)
        => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && id.ToString("D") == value;

    private static string NodeDigest(XElement node)
        => CharacterCreationQualitiesRules.ComputeSourceNodeDigest(node.ToString(SaveOptions.DisableFormatting));
}

internal sealed record CharacterCreationTalentRestrictionPlan(string QualitySourceId, string QualitySourceNodeDigest,
    string CatalogDigest, CharacterCreationTalentRestrictionSelection Selection,
    CharacterCreationTalentSpellCategory Spell, CharacterCreationTalentSpiritCategory Spirit);
