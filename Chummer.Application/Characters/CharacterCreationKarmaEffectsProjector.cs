using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Pending racial, talent and purchased-quality effects; no document mutation.</summary>
internal static class CharacterCreationKarmaEffectsProjector
{
    internal static bool TryProject(CharacterCreationKarmaMetatypeQuote foundation,
        IReadOnlyList<CharacterCreationTalentQualitySource> racialSources,
        CharacterCreationTalentQualitySource? talentSource, out XElement improvements)
    {
        improvements = new("improvements");
        if (!CharacterCreationKarmaGrantsLegacyProjector.TryProject(foundation, racialSources, talentSource,
                out var grants, out _)) return false;
        improvements = new(grants.Single(item => item.Name == "improvements"));
        foreach (var option in foundation.Qualities!.Selections)
        {
            var selection = new CharacterCreationQualitySelection(option.OptionId, option.SourceId, option.SelectionKey,
                option.Name, option.Type, option.Rating, option.KarmaCost, option.IsMetagenic, option.CountsAgainstQualityLimit,
                option.CountsAgainstKarma, false, null, null, option.SourceAnchorIds, option.SourceNodeXml,
                option.SourceNodeDigest, option.OptionDigest);
            if (!CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, foundation.QuoteDigest,
                    out _, out var effects)) return false;
            improvements.Add(effects);
        }
        return true;
    }
}
