using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    // Observation of the existing compiler, not selection, lifecycle or mutation authority.
    internal sealed record CurrentSkillEffect(string Type, string Target, decimal Value,
        int Minimum, int Maximum, string Condition, string Container, int Position, string? Unique);

    internal static bool TryCompileCurrentSkillEffects(XElement source, bool includeFirstLevel,
        out IReadOnlyList<CurrentSkillEffect> effects)
    {
        effects = [];
        if (source.Name.Namespace != XNamespace.None || source.HasAttributes
            || source.Elements("bonus").Count() > 1 || source.Elements("firstlevelbonus").Count() > 1)
            return false;
        var result = new List<CurrentSkillEffect>();
        foreach (string name in new[] { "bonus", "firstlevelbonus" })
        {
            // Compile even inactive first-level syntax: unsupported selected source effects
            // must not disappear merely because this is a later saved quality occurrence.
            if (!TryReadEffectContainer(source.Element(name), out var compiled,
                    CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)) return false;
            if (name == "firstlevelbonus" && !includeFirstLevel) continue;
            int position = 0;
            foreach (var effect in compiled)
                result.Add(new(effect.ImprovementType, effect.ImprovedName, effect.Value,
                    effect.Minimum, effect.Maximum, effect.Condition, name, position++, effect.Unique886));
        }
        effects = Array.AsReadOnly(result.ToArray());
        return true;
    }
}
