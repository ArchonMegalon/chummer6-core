using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

internal static class CharacterCreationPriorityRacialAuthority
{
    internal static CharacterCreationPrerequisiteAuthority Bind(CharacterCreationPrerequisiteAuthority original,
        XDocument metatypes, XElement[] qualities, string qualityDigest, IReadOnlyList<string> books)
    {
        if (!original.IsAuthoritative || original.Blockers.Count != 0) return original;
        var sources = new Dictionary<string, CharacterCreationTalentQualitySource[]>(StringComparer.Ordinal);
        var declarations = new Dictionary<string, CharacterCreationMetatypeOptionProjection>(StringComparer.Ordinal);
        foreach (string name in new[] { "Elf", "Ork" })
        {
            var matches = metatypes.Root?.Element("metatypes")?.Elements("metatype")
                .Where(row => row.Element("name")?.Value == name).Take(2).ToArray() ?? [];
            if (matches.Length != 1 || !CharacterCreationMetatypeCatalogProjector.TryProjectPriorityRacialOption(
                    matches[0], books, out var metatype)) continue;
            var blockers = new List<string>();
            var grants = CharacterCreationMagicResonanceAuthorityProjector.ResolveQualityReferences(
                metatype.GrantedQualities.Select(item => (Reference: item.Name, Selection: string.Empty)).ToArray(),
                qualities, [], qualityDigest, string.Empty, books, blockers);
            if (blockers.Count != 0 || grants.Length != metatype.GrantedQualities.Count
                || grants.Where((source, index) => !string.Equals(
                    XElement.Parse(source.CanonicalSourceXml).Element("category")?.Value,
                    metatype.GrantedQualities[index].Polarity, StringComparison.OrdinalIgnoreCase)).Any()
                || !CharacterCreationPriorityRacialQualities.TryProject(grants, original.AuthorityDigest, out _)) continue;
            sources.Add(name, grants);
            declarations.Add(name, metatype);
        }
        if (sources.Count == 0) return original;
        var authority = original with
        {
            Options = original.Options.Select(priority => priority with
            {
                HeritageOptions = priority.HeritageOptions.Select(option =>
                {
                    if (option.MetavariantSourceId is not null || option.MetavariantName is not null
                        || option.Kind != CharacterCreationPriorityChildKinds.Metatype
                        || option.KarmaCost < 0 || !sources.TryGetValue(option.MetatypeName, out var grants)
                        || option.Blockers.Count != 1
                        || option.Blockers[0] != CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported)
                        return option;
                    var metatype = declarations[option.MetatypeName];
                    if (option.MetatypeSourceId != metatype.OptionId || option.HalvesNormalAttributePoints
                        || !option.Attributes.SequenceEqual(metatype.Attributes) || option.Movement != metatype.Movement)
                        return option;
                    return option with { IsEnabled = true, Blockers = [], RacialQualitySources = grants };
                }).ToArray()
            }).ToArray(),
            AuthorityDigest = string.Empty
        };
        return authority with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority) };
    }
}
