using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Compiles source-owned racial qualities for Priority/Sum-to-Ten. No partial
/// write, purchased quality, attribute modifier or unresolved prompt is allowed.
/// Source admission and the complete character transaction remain separate.
/// </summary>
public static class CharacterCreationPriorityRacialQualities
{
    public static bool TryProject(IReadOnlyList<CharacterCreationTalentQualitySource>? sources,
        string seed, out XElement[] qualities)
    {
        qualities = [];
        if (sources is not { Count: > 0 and <= 32 }
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(seed)
            || sources.Any(source => !CharacterCreationTalentQualitySourceRules.IsValidSource(source))
            || sources.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Count() != sources.Count)
            return false;
        try
        {
            var result = new List<XElement>();
            var flags = new HashSet<string>(StringComparer.Ordinal);
            var improvements = new List<XElement>();
            var gear = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            foreach (var source in sources)
            {
                if (source.Reference != source.Name || source.ForcedSelection != string.Empty
                    || source.GrantedGearSources is { Count: > 0 }) return false;
                var definition = XElement.Parse(source.CanonicalSourceXml);
                string id = CharacterCreationFinalizationProjector.StableGuid(
                    $"priority-racial:{seed}:{source.SourceId}:{source.SourceNodeDigest}").ToString("D");
                string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(definition.Element("bonus"),
                    string.Empty, [], source, id, flags, improvements, gear);
                if (extra.Length != 0 || flags.Count != 0 || improvements.Count != 0 || gear.Count != 0
                    || !CharacterCreationLegacySourceProjector.TryBuildGrantedQualityInstance(
                        source, id, extra, "Metatype", out var saved)) return false;
                result.Add(saved);
            }
            foreach (var source in sources)
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(
                    XElement.Parse(source.CanonicalSourceXml), new XElement("character", new XElement("qualities")), result, flags);
            qualities = result.ToArray();
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidDataException
            or InvalidOperationException or OverflowException or FormatException) { return false; }
    }

    internal static bool TryApply(XElement root, CharacterCreationPriorityHeritageSelection heritage,
        string seed, ICollection<CharacterCreationFinalizationDelta> deltas, ref int order)
    {
        if (heritage.RacialQualitySources is null)
            return heritage.MetatypeName == "Human" && heritage.MetavariantSourceId is null;
        if (!TryProject(heritage.RacialQualitySources, seed, out var qualities)) return false;
        var container = root.Element("qualities");
        if (container is null || qualities.Any(quality => container.Elements("quality").Any(existing =>
                existing.Element("sourceid")?.Value == quality.Element("sourceid")?.Value))) return false;
        try
        {
            foreach (var source in heritage.RacialQualitySources)
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(XElement.Parse(source.CanonicalSourceXml),
                    root, qualities, new HashSet<string>(StringComparer.Ordinal));
            container.Add(qualities);
            foreach (var source in heritage.RacialQualitySources)
                CharacterCreationFinalizationProjector.AddDelta(deltas, ref order, "racial-quality:" + source.SourceId,
                    CharacterCreationFinalizationDeltaKinds.Quality, source.SourceId, null, source.Name,
                    0, 0, heritage.SourceAnchorIds.Concat(source.SourceAnchorIds).Distinct(StringComparer.Ordinal).ToArray(), source.Name);
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidDataException
            or InvalidOperationException or OverflowException or FormatException) { return false; }
    }
}
