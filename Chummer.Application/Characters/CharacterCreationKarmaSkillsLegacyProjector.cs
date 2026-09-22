using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Pure Karma skills contribution for whole-runner finalization. Intrinsic
/// validation is not fresh source admission or permission to persist.</summary>
public static class CharacterCreationKarmaSkillsLegacyProjector
{
    public static bool TryProject(CharacterCreationMetatypeOptionProjection metatype,
        CharacterCreationKarmaTalentOption talent, CharacterCreationKarmaAttributesQuote attributes,
        CharacterCreationKarmaSkillsQuote quote, out XElement? skillsGraph,
        out CharacterCreationFinalizationDelta[] deltas)
    {
        skillsGraph = null;
        deltas = [];
        try
        {
            if (metatype is null || talent is null || attributes is null || quote is not { Blockers.Count: 0 }
                || !CharacterCreationKarmaSkillsRules.IsValid(quote, metatype, talent, attributes, quote.Selection))
                return false;
            return CharacterCreationPurchasedSkillsLegacyProjector.TryProject(quote.Basis.Catalog,
                quote.Skills.Select(row => (row.Allocation, row.Rating, row.KarmaCost)).ToArray(),
                quote.Groups.Select(row => (row.Allocation, row.IsBroken, row.KarmaCost)).ToArray(),
                quote.KarmaUsed, quote.QuoteDigest, "karma", out skillsGraph, out deltas);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
                                      or KeyNotFoundException or OverflowException or XmlException)
        { return false; }
    }
}
