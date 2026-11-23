using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    private static CharacterSheetDerivedQuery.ImportedCareerSheet5699 ImportedMagicRead5699(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source)
    {
        var read = CompileCurrentCareerEffects1960(current, source, [])?.Imported?.Read;
        RequireTalents1949(read is { IsCurrent: true } && ReferenceEquals(read.Current, current),
            "current-imported-magic-owner-closure-unavailable");
        return read!;
    }

    private static CharacterCareerMagicEvaluation CurrentMagic5699(CharacterCurrentRuleEnvironmentContext current,
        XElement root, XElement settings, ICharacterSourceDataContext source,
        CharacterSheetDerivedQuery.ImportedCareerSheet5699? retained = null)
    {
        if (!UsesOwnedPriorityCareer9569(current))
            return CharacterCareerMagicEvaluator.Evaluate(root, settings, sourceContext: source);
        var read = retained ?? ImportedMagicRead5699(current, source);
        var magic = read.Owners!.SpecialResults5699.SingleOrDefault(value => value.Id == "MAG");
        var adept = read.Owners.SpecialResults5699.SingleOrDefault(value => value.Id == "MAGAdept");
        RequireTalents1949(magic is not null && read.HasPrioritySelection9569,
            "current-imported-magic-selection-unavailable");
        // Current grade comes from the same retained history proof as the maxima.
        return new(BoolTalents1949(root, "adept", false), BoolTalents1949(root, "magician", false),
            adept is not null, read.Owners.RetainedGrades5719 is { Resonance: false } history ? history.Grade : 0,
            magic!.Current, magic.Maximum, adept?.Current, adept?.Maximum,
            BoolTalents1949(root, "initiationdisabled", false), []) { EssenceSourceDigest = read.Sheet.AuthorityDigest };
    }

    private static CharacterCareerResonanceEvaluation CurrentResonance5699(CharacterCurrentRuleEnvironmentContext current,
        XElement root, ICharacterSourceDataContext source,
        CharacterSheetDerivedQuery.ImportedCareerSheet5699? retained = null)
    {
        if (!UsesOwnedPriorityCareer9569(current)) return CharacterCareerResonanceEvaluator.Evaluate(root, source);
        var read = retained ?? ImportedMagicRead5699(current, source);
        var resonance = read.Owners!.SpecialResults5699.SingleOrDefault(value => value.Id == "RES");
        RequireTalents1949(resonance is not null && (read.HasPrioritySelection9569 || read.Owners.Sprite5751 is not null)
            && BoolTalents1949(root, "technomancer", false), "current-imported-resonance-selection-unavailable");
        var costs = read.Owners.Effects.Where(row => TextTalents1949(row, "improvementttype")
            is "NewComplexFormKarmaCost" or "NewComplexFormKarmaCostMultiplier").Select(row =>
        {
            RequireTalents1949(ImprovementEnabledTalents1949(row)
                && TextTalents1949(row, "improvedname", "").Length == 0
                && TextTalents1949(row, "condition", "").Length == 0
                && TextTalents1949(row, "unique", "").Length == 0, "current-imported-form-cost-unsupported");
            return new CharacterCareerComplexFormLearningCostEffect(TextTalents1949(row, "improvementttype"),
                decimal.Parse(TextTalents1949(row, "val"), System.Globalization.CultureInfo.InvariantCulture));
        }).ToArray();
        return new(true, resonance!.Current, read.Owners.Channel("ComplexFormLimit"), costs,
            read.Sheet.AuthorityDigest!, []);
    }
}
