using System.Xml;
using System.Xml.Linq;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

/// <summary>
/// Resolves the winning quality's selecttext for review. This neither reconciles
/// existing runner qualities nor grants permission to apply an incomplete graph.
/// A lower tier's nationality must not become a higher tier's corporation.
/// </summary>
internal static class CharacterCreationFoundationQualityInstanceResolver
{
    public static CharacterCreationLifeModuleQualityLevelResolution[] Resolve(
        IReadOnlyList<CharacterCreationLifeModuleQualityLevelResolution> levels,
        IReadOnlyList<CharacterCreationLifeModuleOccurrenceCompilation> occurrences,
        CharacterCreationFoundationQualitySourceAuthority? qualities,
        IReadOnlyDictionary<string, string>? requestedValues, ICollection<string> blockers)
    {
        requestedValues ??= new Dictionary<string, string>(StringComparer.Ordinal);
        bool valuesValid = LifeModuleDecisionInputIntegrity.TryNormalize(requestedValues, out var values)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(requestedValues, values);
        if (!valuesValid)
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);

        var results = new List<CharacterCreationLifeModuleQualityLevelResolution>();
        var knownPrompts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in levels)
        {
            var result = level with { InstancePrompt = null, InstanceValue = null,
                InstancePush = null, InstancePushOccurrenceId = null };
            if (qualities is null || !qualities.TryGetDefinition(level.Target, out var definition, out string nodeDigest)
                || definition is null || !TryInspectSelection(definition, out bool needsText))
            {
                blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                results.Add(result);
                continue;
            }
            if (!needsText) { results.Add(result); continue; }

            var pushes = SourcePushCandidates(level, occurrences);
            string[] literals = pushes.Select(push => push.Instruction.Literal).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            string promptDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
            {
                Semantics = "life-module-winning-quality-selecttext/v1", level.Group, level.Level,
                level.Target, level.Contributions, QualityNodeDigest = nodeDigest,
                Pushes = pushes.Select(push => new { push.OccurrenceId, push.Instruction }).ToArray()
            });
            string promptId = "quality-instance:" + promptDigest[7..];
            var prompt = new LifeModuleFollowUpPromptDto(promptId, level.Target.CanonicalName,
                literals.Length == 0 ? "text" : "single-select", true,
                literals.Select((literal, index) => new LifeModuleFollowUpOptionDto(
                    "source-push-" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), literal,
                    true, null, new Dictionary<string, string>(), literal)).ToArray(),
                level.SourceAnchorIds.ToArray(), "quality-group:" + level.Group, "quality/bonus/selecttext");
            knownPrompts.Add(promptId);
            string? value = literals.Length == 1 ? literals[0] : null;
            if (valuesValid && values.TryGetValue(promptId, out string? requested))
                value = IsLiteral(requested) && (literals.Length == 0 || literals.Contains(requested, StringComparer.Ordinal))
                    ? requested : null;
            if (value is null)
                blockers.Add(CharacterCreationFoundationBlockers.FinalizationPromptRequired);
            var sourcePush = pushes.FirstOrDefault(push => push.Instruction.Literal == value);
            results.Add(result with { InstancePrompt = prompt, InstanceValue = value,
                InstancePush = sourcePush?.Instruction, InstancePushOccurrenceId = sourcePush?.OccurrenceId });
        }
        if (requestedValues.Keys.Any(key => !knownPrompts.Contains(key)))
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
        return results.ToArray();
    }

    private static SourcePush[] SourcePushCandidates(CharacterCreationLifeModuleQualityLevelResolution level,
        IReadOnlyList<CharacterCreationLifeModuleOccurrenceCompilation> occurrences)
    {
        var results = new List<SourcePush>();
        foreach (var contribution in level.Contributions.Where(item => item.Level == level.Level))
        {
            var occurrence = occurrences.SingleOrDefault(item => item.OccurrenceId == contribution.OccurrenceId);
            var effects = occurrence?.Compilation.Effects.Where(effect => effect.EffectKind == "qualitylevel").ToArray();
            // No guessed association across groups, lower tiers, later pushes,
            // multiple unconsumed pushes, or a value already consumed by another quality.
            if (occurrence is null || effects is not { Length: 1 }
                || effects[0].EffectId != contribution.EffectId
                || effects[0].InstructionDigest != contribution.InstructionDigest)
                continue;
            var consumed = occurrence.Compilation.SelectionBindings.Select(binding => binding.PushEffectId)
                .ToHashSet(StringComparer.Ordinal);
            var candidates = occurrence.Compilation.SelectionPushes.Where(push => push.Order < effects[0].Order
                && !consumed.Contains(push.EffectId) && IsLiteral(push.Literal)).ToArray();
            if (candidates.Length == 1)
                results.Add(new(occurrence.OccurrenceId, candidates[0]));
        }
        return results.ToArray();
    }

    private static bool TryInspectSelection(XElement definition, out bool needsText)
    {
        needsText = false;
        XElement[] selections = definition.Element("bonus")?.Elements("selecttext").ToArray() ?? [];
        if (selections.Length > 1) return false;
        var inspected = new XElement(definition);
        if (selections.Length == 1 && selections[0].HasAttributes)
        {
            XElement selection = selections[0];
            // Source allowedit permits a literal answer. These are metadata, not
            // permission to evaluate XPath or read an arbitrary file. Constrained
            // non-editable catalogs require a separate typed resolver.
            if (selection.Attributes().Count() != 3
                || selection.Attribute("xml") is not XAttribute xml || string.IsNullOrWhiteSpace(xml.Value)
                || selection.Attribute("xpath") is not XAttribute xpath || string.IsNullOrWhiteSpace(xpath.Value)
                || selection.Attribute("allowedit") is not XAttribute allow
                || !bool.TryParse(allow.Value, out bool editable) || !editable)
                return false;
            inspected.Element("bonus")!.Element("selecttext")!.RemoveAttributes();
        }
        // Keep the entire original definition digest-bound. Only normalize the
        // known selection metadata for the existing structural bonus validator;
        // required/forbidden conditions still require full-graph reconciliation.
        return CharacterCreationFoundationEffectCompiler.TryInspectDependentQuality(inspected,
            out needsText, out _, out bool bonusSupported) && bonusSupported;
    }

    private static bool IsLiteral(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value != value.Trim()
            || value.Any(char.IsControl) || value.Contains('$') || value.Contains('[') || value.Contains(']'))
            return false;
        try { XmlConvert.VerifyXmlChars(value); return true; }
        catch (XmlException) { return false; }
    }

    private sealed record SourcePush(string OccurrenceId, CharacterCreationFoundationSelectionPushInstruction Instruction);
}
