using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private static bool TryCompiledLoadoutConsumer(string scope, XElement graph,
        IReadOnlyList<CharacterCreationCompiledSourceEffect> compiled, string funded, out string consumer)
    {
        consumer = string.Empty;
        if (scope is not ("weapons" or "armor")
            || !CharacterCreationFinalizationDigest.IsCanonical(funded)) return false;
        string container = scope == "weapons" ? "weapons" : "armors";
        string kind = scope == "weapons" ? CharacterCreationLoadoutKinds.Weapon : CharacterCreationLoadoutKinds.Armor;
        var effects = compiled.Where(row => row.Scope == scope).ToArray();
        var replayed = new List<string>();
        var expected = new List<XElement>();
        foreach (var effect in effects)
        {
            if (effect.LoadoutLine?.Kind != kind || effect.SourceCategoryXml is null
                || !TryReplayCorrectedSource(effect, out var replay)) return false;
            var emitted = XElement.Parse(effect.EffectXml);
            if (emitted.Name != container || emitted.HasAttributes
                || emitted.Elements().Any(node => node.Name != kind)) return false;
            expected.AddRange(emitted.Elements().Select(node => new XElement(node)));
            replayed.Add(replay);
        }
        // A purchase trace must cover the entire actual owner set. Retained or
        // extra owners cannot silently become 'not selected' in the source ledger.
        // Such cases need their own privately source-proven retained execution.
        var actual = graph.Elements(container).ToArray();
        if (actual.Length > 1 || actual.Length == 0 && expected.Count != 0
            || actual.Length == 1 && !XNode.DeepEquals(actual[0], new XElement(container, expected))) return false;
        consumer = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-complete-loadout-source-compilation.v1", Scope = scope,
            Funding = funded, Graph = graph.ToString(SaveOptions.DisableFormatting), Executions = replayed
        });
        return true;
    }
}
