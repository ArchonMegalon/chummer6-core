using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Pure racial/talent contribution to Karma finalization. It compiles complete
/// admitted source payloads, not Priority grants, and never persists or marks a
/// character created. Fresh source admission and whole-build legality remain
/// the finalizer's responsibility. Returned containers must be composed with
/// purchases, not used to overwrite them.
/// </summary>
public static class CharacterCreationKarmaGrantsLegacyProjector
{
    public static bool TryProject(CharacterCreationKarmaMetatypeQuote quote,
        IReadOnlyList<CharacterCreationTalentQualitySource> racialSources,
        CharacterCreationTalentQualitySource? talentSource,
        out XElement[] elements, out CharacterCreationFinalizationDelta[] deltas)
    {
        elements = [];
        deltas = [];
        try
        {
            if (quote is not { Schema: CharacterCreationKarmaMetatypeSchemas.QuoteV1, CanSelect: true, Blockers.Count: 0,
                    Metatype: { IsEnabled: true, Blockers.Count: 0, GrantedQualities.Count: <= 32 },
                    Talent: not null, Attributes: not null, Skills: not null,
                    Qualities.Selections.Count: <= CharacterCreationKarmaQualitiesRules.MaximumSelections }
                || racialSources is null
                || quote.Metatype.GrantedQualities.Any(item => item is null)
                || quote.Qualities.Selections.Any(item => item is null)
                || quote.QuoteDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    quote with { QuoteDigest = string.Empty })
                || !CharacterCreationKarmaAttributesRules.IsValid(quote.Attributes, quote.Metatype, quote.Talent, quote.Attributes.Allocations)
                || !CharacterCreationKarmaSkillsRules.IsValid(quote.Skills, quote.Metatype, quote.Talent,
                    quote.Attributes, quote.Skills.Selection)
                || !CharacterCreationKarmaQualitiesRules.IsValid(quote.Qualities, quote.Metatype, quote.Talent,
                    quote.Qualities.Selections.Select(item => item.OptionId).ToArray())
                || racialSources.Count != quote.Metatype.GrantedQualities.Count
                || racialSources.Any(source => !CharacterCreationTalentQualitySourceRules.IsValidSource(source)))
                return false;

            bool mundane = quote.Talent.OptionId == CharacterCreationKarmaTalentCatalog.MundaneOptionId;
            if (mundane)
            {
                if (talentSource is not null || quote.Talent.KarmaCost != 0 || quote.Talent.EnabledAttribute is not null
                    || quote.Talent.SourceNodeXml != string.Empty || quote.Talent.SourceNodeDigest !=
                        CharacterCreationQualitiesRules.ComputeSourceNodeDigest(string.Empty)) return false;
            }
            else
            {
                if (!CharacterCreationTalentQualitySourceRules.IsValidSource(talentSource)
                    || talentSource!.SourceId != quote.Talent.OptionId || talentSource.ForcedSelection != string.Empty
                    || quote.Talent.SourceNodeDigest != CharacterCreationQualitiesRules.ComputeSourceNodeDigest(quote.Talent.SourceNodeXml))
                    return false;
                var talentRow = XElement.Parse(quote.Talent.SourceNodeXml, LoadOptions.None);
                if (talentRow.ToString(SaveOptions.DisableFormatting) != talentSource.CanonicalSourceXml
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quote.Talent,
                        CharacterCreationKarmaTalentAuthority.Project(XElement.Parse(quote.Talent.SourceNodeXml, LoadOptions.PreserveWhitespace),
                            quote.Qualities.Policy.Costs.KarmaMultiplier, bookEnabled: true)))
                    return false;
            }

            var qualities = new List<XElement>();
            var improvements = new List<XElement>();
            var gears = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            var flags = new HashSet<string>(StringComparer.Ordinal);
            var changes = new List<CharacterCreationFinalizationDelta>();
            var allSources = racialSources.Concat(talentSource is null ? [] : new[] { talentSource }).ToArray();
            if (allSources.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Count() != allSources.Length)
                return false;
            for (int index = 0; index < allSources.Length; index++)
            {
                var source = allSources[index];
                bool racial = index < racialSources.Count;
                var row = XElement.Parse(source.CanonicalSourceXml);
                if (racial && (source.Name != quote.Metatype.GrantedQualities[index].Name
                    || source.Reference != source.Name || source.ForcedSelection != string.Empty
                    || !string.Equals(row.Element("category")?.Value, quote.Metatype.GrantedQualities[index].Polarity,
                        StringComparison.OrdinalIgnoreCase))) return false;
                string id = CharacterCreationFinalizationProjector.StableGuid(
                    $"karma-grant:{source.SourceId}:{source.SourceNodeDigest}:{quote.QuoteDigest}").ToString("D");
                string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(row.Element("bonus"), string.Empty,
                    quote.Skills.Selection.TalentUnlock is { } unlock ? new[] { unlock } : [],
                    source, id, flags, improvements, gears);
                if (!CharacterCreationLegacySourceProjector.TryBuildGrantedQualityInstance(source, id, extra,
                    racial ? "Metatype" : "Selected", out var saved)) return false;
                qualities.Add(saved);
                changes.Add(new(changes.Count, $"karma-grant:{source.SourceId}", CharacterCreationFinalizationDeltaKinds.Quality,
                    source.SourceId, null, source.Name, racial ? 0 : quote.Talent.KarmaCost, 0,
                    source.SourceAnchorIds.Concat(racial ? quote.Metatype.GrantedQualities[index].SourceAnchorIds
                        : quote.Talent.SourceAnchorIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
            }
            // Check restrictions against both automatic grants and purchases.
            var purchased = new XElement("character", new XElement("qualities", quote.Qualities.Selections.Select(item =>
                new XElement("quality", new XElement("name", item.Name), new XElement("sourceid", item.SourceId)))));
            foreach (var source in allSources)
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(XElement.Parse(source.CanonicalSourceXml), purchased, qualities, flags);
            foreach (var (attribute, flag) in new[] { ("MAG", "magenabled"), ("RES", "resenabled"), ("DEP", "depenabled") })
                if (quote.Attributes.Attributes.Single(item => item.AttributeId == attribute).IsEnabled != flags.Contains(flag))
                    return false;

            var gearContainer = new XElement("gears");
            foreach (var (saved, source) in gears)
            {
                // Active-commlink selection belongs to whole-build composition,
                // where these free instances are combined with purchased gear.
                gearContainer.Add(saved);
                changes.Add(new(changes.Count, "karma-grant-gear:" + saved.Element("guid")!.Value,
                    CharacterCreationFinalizationDeltaKinds.Gear, source.SourceId, null, source.Name, 0, 0, source.SourceAnchorIds));
            }
            var flagNodes = new[] { "magenabled", "resenabled", "depenabled", "adept", "magician", "technomancer", "ai" }
                .Select(flag => new XElement(flag, flags.Contains(flag) ? "True" : "False")).ToArray();
            foreach (string flag in flags.Order(StringComparer.Ordinal))
                changes.Add(new(changes.Count, "karma-talent-flag:" + flag, CharacterCreationFinalizationDeltaKinds.MagicResonance,
                    flag, "False", "True", 0, 0, quote.Talent.SourceAnchorIds));
            XElement[] result = [new("qualities", qualities), new("improvements", improvements), gearContainer, .. flagNodes];
            foreach (var element in result) _ = element.ToString(SaveOptions.DisableFormatting);
            if (changes.Sum(change => change.KarmaCost) != quote.Talent.KarmaCost) return false;
            elements = result;
            deltas = changes.ToArray();
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException
            or XmlException or OverflowException or FormatException)
        {
            return false;
        }
    }
}
