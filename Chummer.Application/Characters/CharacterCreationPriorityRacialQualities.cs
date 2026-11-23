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
    // Read-only projection shared by current observation and Priority admission.
    // Resistance improvements are returned to the caller; only TryApply writes
    // them into the saved character.
    public static bool TryProjectCurrent(IReadOnlyList<CharacterCreationTalentQualitySource>? sources,
        string seed, out XElement[] qualities, out XElement[] improvements)
        => TryProjectCurrent(sources, seed, false, out qualities, out improvements);

    // Native Karma/Life Modules already price and materialize this literal cost
    // closure. Keep it separate from Priority's resistance-only observation.
    // CompileBonus remains the strict source grammar: no prompts, conditions,
    // bounds, unknown effects, generated owners or Career-only costs are admitted.
    public static bool TryProjectCurrentNativeSkillCosts(
        IReadOnlyList<CharacterCreationTalentQualitySource>? sources, string seed,
        out XElement[] qualities, out XElement[] improvements, bool allowLifeModuleRacialDice = false)
        => TryProjectCurrent(sources, seed, true, out qualities, out improvements, allowLifeModuleRacialDice);

    private static bool TryProjectCurrent(IReadOnlyList<CharacterCreationTalentQualitySource>? sources,
        string seed, bool nativeSkillCosts, out XElement[] qualities, out XElement[] improvements, bool allowLifeModuleRacialDice = false)
    {
        qualities = [];
        improvements = [];
        if (sources is not { Count: > 0 and <= 32 }
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(seed)
            || sources.Any(source => !CharacterCreationTalentQualitySourceRules.IsValidSource(source))
            || sources.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Count() != sources.Count)
            return false;
        try
        {
            var result = new List<XElement>();
            var effects = new List<XElement>();
            var validatedDice = new HashSet<XElement>();
            var flags = new HashSet<string>(StringComparer.Ordinal);
            var gear = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            string[] resistanceTypes = ["PathogenContactResist", "PathogenIngestionResist", "PathogenInhalationResist",
                "PathogenInjectionResist", "ToxinContactResist", "ToxinIngestionResist", "ToxinInhalationResist", "ToxinInjectionResist"];
            foreach (var source in sources)
            {
                if (source.Reference != source.Name || source.ForcedSelection != string.Empty
                    || source.GrantedGearSources is { Count: > 0 }) return false;
                var definition = XElement.Parse(source.CanonicalSourceXml);
                if (nativeSkillCosts && (definition.Elements("bonus").Count() > 1
                    || definition.Elements("firstlevelbonus").Any(node => node.HasElements))) return false;
                string id = CharacterCreationFinalizationProjector.StableGuid(
                    $"priority-racial:{seed}:{source.SourceId}:{source.SourceNodeDigest}").ToString("D");
                string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(definition.Element("bonus"),
                    string.Empty, [], source, id, flags, effects, gear,
                    retainUnresolvedDefensePenalty: allowLifeModuleRacialDice);
                if (definition.Element("bonus")?.Elements("defensetest").Any() == true)
                {
                    // Defense stays in the exact saved quality source; the portable
                    // owner graph consumes it. Only the two real legacy skill rows
                    // may be persisted, with the complete source/owner check below.
                    if (!allowLifeModuleRacialDice || !TryValidateRacialDiceOutputs8582(definition, id,
                        effects.Where(effect => effect.Element("sourcename")?.Value == id).ToArray(), out var dice)) return false;
                    foreach (var effect in dice) validatedDice.Add(effect);
                }
                if (extra.Length != 0 || flags.Count != 0 || gear.Count != 0
                    || effects.Any(effect => !resistanceTypes.Contains(effect.Element("improvementttype")?.Value, StringComparer.Ordinal)
                        && !(nativeSkillCosts && effect.Element("addtorating")?.Value == "0"
                            && (CharacterCreationSkillKarmaModifierRules.IsSupportedType(effect.Element("improvementttype")?.Value ?? string.Empty)
                                || effect.Element("improvementttype")?.Value is "Notoriety" or "BlockSkillCategoryDefault"
                                || validatedDice.Contains(effect))))
                    || !CharacterCreationLegacySourceProjector.TryBuildGrantedQualityInstance(
                        source, id, extra, "Metatype", out var saved)) return false;
                result.Add(saved);
            }
            foreach (var source in sources)
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(XElement.Parse(source.CanonicalSourceXml),
                    new XElement("character", new XElement("qualities")), result, flags);
            qualities = result.ToArray();
            improvements = effects.ToArray();
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidDataException
            or InvalidOperationException or OverflowException or FormatException) { return false; }
    }

    private enum RacialDiceKind8582 { CombatCategory, Gunnery }
    private sealed record RacialDice8582(RacialDiceKind8582 Kind, int Value);

    private static bool TryValidateRacialDiceOutputs8582(XElement definition, string owner,
        XElement[] output, out XElement[] validated)
    {
        validated = [];
        var bonus = definition.Element("bonus");
        var exact = new XElement("bonus",
            new XElement("skillcategory", new XElement("name", "Combat Active"), new XElement("bonus", -1)),
            new XElement("specificskill", new XElement("name", "Gunnery"), new XElement("bonus", -1)),
            new XElement("defensetest", -1));
        if (definition.Elements("bonus").Count() != 1 || bonus is null || output.Length != 2) return false;
        // Stock Cyclopean Eye includes an explanatory comment inside specificskill.
        // Ignore comments only for this grammar comparison; retain the original
        // source and its exact digest/bonus binding in the caller and compiler.
        var comparableBonus = new XElement(bonus);
        comparableBonus.DescendantNodes().OfType<XComment>().Remove();
        if (!XNode.DeepEquals(comparableBonus, exact)) return false;
        RacialDice8582[] typed = [new(RacialDiceKind8582.CombatCategory, -1),
            new(RacialDiceKind8582.Gunnery, -1)];
        var expected = typed.Select(effect => effect.Kind switch
        {
            RacialDiceKind8582.CombatCategory => CharacterCreationAwakenedLegacyProjector.Improvement("SkillCategory", "Combat Active", owner, "Quality", effect.Value),
            RacialDiceKind8582.Gunnery => CharacterCreationAwakenedLegacyProjector.Improvement("Skill", "Gunnery", owner, "Quality", effect.Value),
            _ => throw new InvalidOperationException()
        }).ToArray();
        // CompileBonus assigns the parsed empty condition on both dice rows.
        // Match that XML content shape without weakening the exact row check.
        foreach (var row in expected) row.Element("condition")!.Value = string.Empty;
        // Also proves owner, enabled flag, addtorating=0, bounds, condition and all
        // remaining output fields; missing/extra/duplicate rows cannot pass.
        if (expected.Any(row => output.Count(actual => XNode.DeepEquals(row, actual)) != 1)) return false;
        validated = output;
        return true;
    }

    public static bool TryProject(IReadOnlyList<CharacterCreationTalentQualitySource>? sources,
        string seed, out XElement[] qualities)
        => TryProjectCurrent(sources, seed, out qualities, out _);

    internal static bool TryApply(XElement root, CharacterCreationPriorityHeritageSelection heritage,
        string seed, ICollection<CharacterCreationFinalizationDelta> deltas, ref int order)
    {
        if (heritage.RacialQualitySources is null)
            return heritage.MetatypeName == "Human" && heritage.MetavariantSourceId is null;
        if (!TryProjectCurrent(heritage.RacialQualitySources, seed, out var qualities, out var improvements)) return false;
        var container = root.Element("qualities");
        if (container is null || qualities.Any(quality => container.Elements("quality").Any(existing =>
                existing.Element("sourceid")?.Value == quality.Element("sourceid")?.Value))) return false;
        try
        {
            var improvementContainers = root.Elements("improvements").ToArray();
            if (improvements.Length != 0 && improvementContainers.Length > 1) return false;
            foreach (var source in heritage.RacialQualitySources)
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(XElement.Parse(source.CanonicalSourceXml),
                    root, qualities, new HashSet<string>(StringComparer.Ordinal));
            container.Add(qualities);
            if (improvements.Length != 0)
            {
                if (improvementContainers.Length == 1) improvementContainers[0].Add(improvements);
                else root.Add(new XElement("improvements", improvements));
            }
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
