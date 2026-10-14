using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    /// <summary>
    /// Proves only that the original racial/talent grants cannot change Contacts,
    /// Charisma or Lifestyle pricing under the existing grant compiler. Unknown
    /// effects fail closed. This is not whole-grant or finalization admission.
    /// </summary>
    internal static bool AreCompletionGrantEffectsIndependent(
        CharacterCreationPrerequisiteDraft prerequisite,
        IReadOnlyList<CharacterCreationTalentQualitySource> talentSources)
    {
        if (prerequisite?.HeritageSelection is not { } heritage
            || prerequisite.TalentSelection?.GrantedQualities is not { } references
            || talentSources is null || talentSources.Count > 32
            || talentSources.Any(source => !CharacterCreationTalentQualitySourceRules.IsValidSource(source))
            || !references.SequenceEqual(talentSources.Select(source => source.Reference), StringComparer.Ordinal))
            return false;
        var racialSources = heritage.RacialQualitySources;
        if (racialSources is null && (heritage.MetatypeName != "Human" || heritage.MetavariantSourceId is not null)
            || racialSources is { Count: > 32 }
            || racialSources?.Any(source => !CharacterCreationTalentQualitySourceRules.IsValidSource(source)) == true)
            return false;

        foreach (var source in racialSources ?? [])
        {
            // PriorityRacialQualities rejects every generated improvement/flag.
            // No saved graph or placeholder source ID is needed to prove this.
            var node = XElement.Parse(source.CanonicalSourceXml);
            if (!HasIndependentGrantContainers(node, racial: true)) return false;
        }
        foreach (var source in talentSources)
        {
            var node = XElement.Parse(source.CanonicalSourceXml);
            if (!HasIndependentGrantContainers(node, racial: false)) return false;
            foreach (var gear in source.GrantedGearSources ?? [])
            {
                var gearNode = XElement.Parse(gear.CanonicalSourceXml);
                // The existing granted Gear compiler accepts no bonus effects.
                if (gearNode.Elements("bonus").Concat(gearNode.Elements("wirelessbonus"))
                    .Any(bonus => bonus.HasAttributes || bonus.HasElements || !string.IsNullOrWhiteSpace(bonus.Value)))
                    return false;
            }
        }
        return true;
    }

    private static bool HasIndependentGrantContainers(XElement node, bool racial)
    {
        if (node.Elements("bonus").Count() > 1 || node.Elements("firstlevelbonus").Count() > 1
            || node.Elements("firstlevelbonus").Any(bonus => bonus.HasAttributes || bonus.HasElements
                || !string.IsNullOrWhiteSpace(bonus.Value))) return false;
        if (node.Element("bonus") is not { } container) return true;
        if (container.Attributes().Any(attribute => attribute.Name != "useselected" || !bool.TryParse(attribute.Value, out _))
            || container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) return false;
        foreach (var effect in container.Elements())
        {
            if (racial || effect.HasAttributes || effect.Name.Namespace != XNamespace.None) return false;
            switch (effect.Name.LocalName)
            {
                case "enableattribute":
                    if (effect.Elements().Count() != 1 || effect.Element("name") is not { } name
                        || name.HasAttributes || name.HasElements
                        || name.Value.Trim().ToUpperInvariant() is not ("MAG" or "RES" or "DEP")) return false;
                    break;
                case "enabletab":
                    if (effect.Elements().Any(child => child.Name != "name" || child.HasAttributes || child.HasElements
                        || child.Value.Trim().ToLowerInvariant() is not ("magician" or "adept" or "technomancer"))) return false;
                    break;
                case "specificskill":
                    if (effect.Elements().Any(child => child.Name.LocalName is not ("name" or "bonus" or "condition" or "applytorating")
                        || child.HasAttributes || child.HasElements)) return false;
                    break;
                case "addgear": // Exact source-to-gear references were checked by IsValidSource.
                case "unlockskills": // Only the SpecialSkills improvement is emitted by this compiler branch.
                case "blockspelldescriptor":
                case "limitspellcategory":
                case "limitspiritcategory":
                case "pathogencontactresist":
                case "pathogeningestionresist":
                case "pathogeninhalationresist":
                case "pathogeninjectionresist":
                case "toxincontactresist":
                case "toxiningestionresist":
                case "toxininhalationresist":
                case "toxininjectionresist":
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Compiles the same exact selected-quality source used by the saved graph,
    /// without constructing saved IDs, XML or a synthetic draft. Callers must
    /// first admit the whole selection against its original quality authority;
    /// this helper does not grant source, prerequisite or budget authority.
    /// </summary>
    internal static bool TryProjectCompletionQualityEffects(
        IReadOnlyList<CharacterCreationQualitySelection> sourceAdmittedSelections,
        out IReadOnlyList<SourceQualityEffect> effects)
    {
        effects = [];
        if (sourceAdmittedSelections is null)
            return false;

        var projected = new List<SourceQualityEffect>();
        foreach (CharacterCreationQualitySelection selection in sourceAdmittedSelections)
        {
            if (selection is null
                || !TryReadSelectedQualityDefinition(selection, selection.ResolvedArmCount,
                    out _, out QualityDefinition definition))
                return false;

            // Match ordinary graph order and multiplicity, including first-level
            // effects exactly once. Never silently discard unsupported effects.
            for (int level = 1; level <= selection.Rating; level++)
            {
                foreach (CompiledEffect effect in definition.BonusEffects)
                    projected.Add(new(effect.ImprovementType, effect.ImprovedName, effect.Value));
                if (level == 1)
                {
                    foreach (CompiledEffect effect in definition.FirstLevelEffects)
                        projected.Add(new(effect.ImprovementType, effect.ImprovedName, effect.Value));
                }
            }
        }

        effects = projected.ToArray();
        return true;
    }

    // BuildImprovement serializes these effects as unconditional, enabled,
    // non-custom Quality improvements with no unique-name override.
    internal sealed record SourceQualityEffect(
        string ImprovementType,
        string ImprovedName,
        decimal Value);

    private static bool TryReadSelectedQualityDefinition(
        CharacterCreationQualitySelection selection,
        int? armCount,
        out XElement source,
        out QualityDefinition definition)
    {
        source = null!;
        definition = null!;
        if (selection is null
            || selection.SourceNodeXml is not
                { Length: > 0 and <= CharacterCreationQualitiesRules.MaximumSourceNodeLength }
            || !CharacterCreationQualitiesRules.DigestsEqual(
                selection.SourceNodeDigest,
                CharacterCreationQualitiesRules.ComputeSourceNodeDigest(selection.SourceNodeXml)))
            return false;

        XElement? parsedSource = ParseBoundedSource(
            selection.SourceNodeXml,
            "quality",
            s_QualitySourceChildren);
        if (parsedSource is null
            || !TryReadQualityDefinition(parsedSource, out QualityDefinition parsedDefinition, armCount)
            || parsedDefinition.SourceId != selection.SourceId
            || !string.Equals(parsedDefinition.Name, selection.Name, StringComparison.Ordinal)
            || parsedDefinition.Type != selection.Type
            || parsedDefinition.Metagenic != selection.IsMetagenic
            || parsedDefinition.ContributeToLimit != selection.CountsAgainstQualityLimit
            || parsedDefinition.ContributeToBuildPoints != selection.CountsAgainstKarma
            || selection.IsFreeOrGranted
            || selection.FollowUpChoiceId is not null
            || selection.FollowUpChoiceLabel is not null
            || selection.Rating is < 1
            || selection.Rating > parsedDefinition.MaximumRating)
            return false;

        int expectedCost;
        try
        {
            expectedCost = checked(parsedDefinition.Karma * selection.Rating);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (expectedCost != selection.KarmaCost)
            return false;

        source = parsedSource;
        definition = parsedDefinition;
        return true;
    }
}
