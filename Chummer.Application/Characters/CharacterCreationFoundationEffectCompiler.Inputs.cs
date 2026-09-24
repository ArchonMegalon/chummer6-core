using System.Xml;
using System.Xml.Linq;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationFoundationEffectCompiler
{
    private static bool HasValidAnswerMap(IReadOnlyList<LifeModuleFollowUpPromptDto> prompts,
        IReadOnlyDictionary<string, string> answers)
        => (prompts.Count == 0 || LifeModuleDecisionInputIntegrity.ValidForms(prompts))
           && LifeModuleDecisionInputIntegrity.TryNormalize(answers, out var normalized)
           && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(answers, normalized)
           && answers.Keys.All(key => prompts.Any(prompt => prompt.PromptId == key));

    private static ResolvedEffectInput ResolveEffectInput(LifeModuleEffectProjectionDto source,
        IReadOnlyList<LifeModuleFollowUpPromptDto> prompts,
        IReadOnlyDictionary<string, string> answers, bool sourceMatches)
    {
        var unresolved = new ResolvedEffectInput(source, prompts, null);
        if (!sourceMatches || prompts.Count == 0)
            return unresolved;

        try
        {
            XElement effect = XElement.Parse(source.RawXml, LoadOptions.None);
            // This is deliberately not a general XML substitution engine. These
            // descriptive fields do not change val, costs, budgets or skill IDs.
            // The downstream oracle compiler still validates the complete shape
            // and retains legacy-ignored knowledge metadata rather than inventing
            // rated knowledge/language skill grants.
            if (effect.Name != "knowledgeskilllevel" || effect.HasAttributes
                || prompts.Select(prompt => prompt.ValuePath).Distinct(StringComparer.Ordinal).Count() != prompts.Count)
                return unresolved;

            var bindings = new List<CharacterCreationFoundationEffectInputBinding>();
            foreach (var prompt in prompts)
            {
                if (prompt.EffectId != source.EffectId
                    || !prompt.SourceAnchorIds.SequenceEqual(source.SourceAnchorIds, StringComparer.Ordinal)
                    || !answers.TryGetValue(prompt.PromptId, out string? value)
                    || string.IsNullOrWhiteSpace(value) || value.Length > 1024
                    || value != value.Trim() || value.Any(char.IsControl)
                    || value.Contains('$', StringComparison.Ordinal) || ContainsPlaceholderToken(value))
                    return unresolved;
                XmlConvert.VerifyXmlChars(value);

                // Accept a unique direct child only: no XPath, indices, nested
                // selection actions, ambiguous siblings or numeric substitution.
                string[] path = prompt.ValuePath.Split('/');
                if (path.Length != 2 || path[0] != "knowledgeskilllevel"
                    || path[1] is not ("name" or "group" or "spec" or "options" or "option"))
                    return unresolved;
                XElement[] matches = effect.Elements().Where(child => child.Name == path[1]).ToArray();
                if (matches.Length != 1 || matches[0].HasAttributes)
                    return unresolved;
                XElement target = matches[0];
                if (path[1] is "options" or "option")
                {
                    XElement[] choices = target.Elements().ToArray();
                    if (prompt.InputKind != "single-select" || effect.Elements("name").Any()
                        || choices.Length == 0 || choices.Any(child => child.HasElements || child.HasAttributes
                            || child.Name.NamespaceName.Length != 0)
                        || target.Nodes().Any(node => node is XText text
                            ? !string.IsNullOrWhiteSpace(text.Value) : node is not XElement)
                        || !choices.Select(child => child.Value.Trim()).SequenceEqual(
                            prompt.Options.Select(option => option.SourceValue), StringComparer.Ordinal)
                        || prompt.Options.Count(option => option.IsEnabled && option.SourceValue == value) != 1)
                        return unresolved;
                    target.ReplaceWith(new XElement("name", value));
                }
                else
                {
                    string placeholder = target.Value.Trim();
                    if (prompt.InputKind != "text" || prompt.Options.Count != 0 || target.HasElements
                        || placeholder.Length < 3 || placeholder[0] != '[' || placeholder[^1] != ']'
                        || placeholder[1..^1].Contains('[', StringComparison.Ordinal)
                        || placeholder[1..^1].Contains(']', StringComparison.Ordinal))
                        return unresolved;
                    target.Value = value; // XML escaping, never parsing the answer as markup.
                }
                bindings.Add(new(prompt.PromptId, prompt.ValuePath, value,
                    CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(prompt)));
            }

            // Do not discard malformed/unknown source children while projecting.
            // The downstream typed compiler must inspect the resolved raw XML.
            var parameters = effect.Elements().Where(child => !child.HasElements)
                .GroupBy(child => child.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => string.Join("|", group.Select(child => child.Value.Trim())),
                    StringComparer.Ordinal);
            var resolved = source with
            {
                TargetId = effect.Element("name")?.Value.Trim() ?? source.TargetId,
                Parameters = parameters,
                RawXml = effect.ToString(SaveOptions.DisableFormatting)
            };
            // AfterValue, BudgetId and BudgetDelta remain exactly source-owned.
            return new(resolved, [], new(
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(source),
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(resolved),
                bindings.ToArray()));
        }
        catch (XmlException)
        {
            return unresolved;
        }
    }

    private sealed record ResolvedEffectInput(LifeModuleEffectProjectionDto Effect,
        IReadOnlyList<LifeModuleFollowUpPromptDto> PendingPrompts,
        CharacterCreationFoundationEffectInputResolution? Resolution);
}
