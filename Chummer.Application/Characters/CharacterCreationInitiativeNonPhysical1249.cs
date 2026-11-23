using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;

namespace Chummer.Application.Characters;

internal static class CharacterCreationInitiativeNonPhysical1249
{
    // Re-execute the retained talent compiler, not a type-name exemption. Only
    // exact emitted access markers and learned grants are nonphysical here.
    internal static IReadOnlyList<XElement> Execute(XElement root, CharacterCreationPrerequisiteDraft draft,
        AwakenedGraphInputs inputs, string seed)
    {
        var sources = inputs switch
        {
            FullAwakenedGraphInputs full => full.Inputs.SelectedTalent.GrantedQualitySources ?? [],
            SelectedAwakenedGraphInputs selected => selected.Authority.Talent.GrantedQualitySources ?? [],
            _ => null
        };
        return Execute(root, draft, sources ?? [], seed);
    }

    internal static IReadOnlyList<XElement> Execute(XElement root, CharacterCreationPrerequisiteDraft draft,
        IReadOnlyList<CharacterCreationTalentQualitySource> sources, string seed)
    {
        if (draft.TalentSelection is not { } talent
            || !sources.Select(source => source.Reference).SequenceEqual(talent.GrantedQualities, StringComparer.Ordinal))
            throw new InvalidDataException("initiative-talent-source-unresolved");
        var candidates = new List<XElement>();
        var flags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (!CharacterCreationTalentQualitySourceRules.IsValidSource(source)) throw new InvalidDataException();
            string id = CharacterCreationFinalizationProjector.StableGuid($"heritage-quality:{source.SourceId}:{source.ForcedSelection}:{source.SourceNodeDigest}:{seed}").ToString("D");
            var lowered = new List<XElement>();
            var gears = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(
                XElement.Parse(source.CanonicalSourceXml).Element("bonus"), source.ForcedSelection,
                talent.GrantPlan?.SkillGroups.Select(group => group.CanonicalName).ToArray() ?? [],
                source, id, flags, lowered, gears);
            if (!CharacterCreationLegacySourceProjector.TryBuildHeritageQualityInstance(source, id, extra, out var saved)
                || root.Element("qualities")?.Elements("quality").Count(row => XNode.DeepEquals(row, saved)) != 1)
                throw new InvalidDataException();
            foreach (var effect in lowered)
                if (effect.Element("improvementttype")?.Value is "Attribute" or "SpecialTab" or "SpecialSkills")
                    candidates.Add(effect);
        }
        foreach (string flag in flags)
            if (root.Elements(flag).ToArray() is not [var savedFlag]
                || !bool.TryParse(savedFlag.Value, out bool enabled) || !enabled) throw new InvalidDataException();
        foreach (var grant in talent.GrantPlan?.ActiveSkills ?? [])
        {
            if (grant.ImprovementKind != CharacterCreationTalentGrantImprovementKinds.SkillBase) throw new InvalidDataException();
            candidates.Add(CharacterCreationAwakenedLegacyProjector.Improvement("SkillBase", grant.CanonicalName, "", "Heritage", grant.BaseRating));
        }
        foreach (var grant in talent.GrantPlan?.SkillGroups ?? [])
        {
            if (grant.BaseRating < 0) throw new InvalidDataException();
            if (grant.BaseRating == 0) continue;
            if (grant.ImprovementKind != CharacterCreationTalentGrantImprovementKinds.SkillGroupBase) throw new InvalidDataException();
            candidates.Add(CharacterCreationAwakenedLegacyProjector.Improvement("SkillGroupBase", grant.CanonicalName, "", "Heritage", grant.BaseRating));
        }
        foreach (var effect in candidates)
            if (root.Element("improvements")?.Elements("improvement").Count(row => XNode.DeepEquals(row, effect)) != 1)
                throw new InvalidDataException();
        return candidates;
    }
}
