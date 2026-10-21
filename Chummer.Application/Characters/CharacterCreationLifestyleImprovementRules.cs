using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Shared creation-time admission for saved and pending lifestyle effects.</summary>
public static class CharacterCreationLifestyleImprovementRules
{
    /// <summary>
    /// The caller admits each selection against the enabled live source, including
    /// book, prerequisites and purchase origin. This adds no source capability.
    /// </summary>
    public static bool TryProjectPurchasedQualityCosts(IReadOnlyList<CharacterCreationQualitySelection> selections,
        out CharacterCreationLifestylePurchasedQualityCosts? costs)
    {
        costs = null;
        if (selections is null || selections.Count > 4096 || selections.Any(selection => selection is null
                || selection.SourceAnchorIds is null || selection.Rating is < 1 or > 100)
            || selections.Select(selection => selection.OptionId).Distinct(StringComparer.Ordinal).Count() != selections.Count
            || selections.Select(selection => selection.SourceId).Distinct().Count() != selections.Count
            || !CharacterCreationLegacySourceProjector.TryProjectCompletionQualityEffects(selections, out var effects)) return false;
        var occurrences = new List<CharacterCreationLifestyleQualityCostOccurrence>();
        foreach (var effect in effects)
        {
            if (effect.ImprovementType is not ("LifestyleCost" or "BasicLifestyleCost")) continue;
            if (effect.Condition.Length != 0 || effect.ImprovedName.Length != 0 || effect.Minimum != 0
                || effect.Maximum != 0 || effect.Value < -100m) return false;
            occurrences.Add(new(effect.OptionId, effect.SourceId, effect.SourceNodeDigest, effect.Level,
                effect.Container, effect.Position, effect.Value, effect.SourceAnchorIds.ToArray())
            {
                EffectType = effect.ImprovementType == "BasicLifestyleCost"
                    ? CharacterCreationLifestyleCostEffectType.BasicLifestyleCost
                    : CharacterCreationLifestyleCostEffectType.LifestyleCost
            });
        }
        if (occurrences.Count == 0) return true;
        var owners = occurrences.Select(effect => effect.OptionId).ToHashSet(StringComparer.Ordinal);
        var candidate = new CharacterCreationLifestylePurchasedQualityCosts(
            selections.Where(selection => owners.Contains(selection.OptionId)).ToArray(), occurrences.ToArray());
        if (!CharacterCreationLifestylesRules.IsValidPurchasedQualityCostShape(candidate)) return false;
        costs = candidate;
        return true;
    }

    public static bool IsValidPurchasedQualityCosts(CharacterCreationLifestylePurchasedQualityCosts? costs)
        => costs is null || CharacterCreationLifestylesRules.IsValidPurchasedQualityCostShape(costs)
            && TryProjectPurchasedQualityCosts(costs.Sources, out var expected)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(costs, expected);

    /// <summary>
    /// Resolve saved costs using live-source-admitted purchased quality instances.
    /// Level is the native quality occurrence (firstlevelbonus applies once).
    /// Missing, duplicate, orphaned or altered saved graphs fail closed.
    /// </summary>
    public static bool TryResolveSourceBackedCosts(IEnumerable<XElement> improvements,
        IReadOnlyList<CharacterCreationLifestyleQualityInstance> instances, int initialTrustFundLevel,
        out int trustFundLevel, out decimal metatypeCostPercent,
        out CharacterCreationLifestylePurchasedQualityCosts? costs, out string[] blockers)
    {
        trustFundLevel = initialTrustFundLevel;
        metatypeCostPercent = 0m;
        costs = null;
        blockers = [CharacterCreationLifestylesBlockers.UnsupportedSemantics];
        if (improvements is null || instances is null || instances.Count > 4096
            || instances.Any(instance => instance is null || instance.Quality is null || instance.Selection is null)) return false;
        var saved = improvements.ToArray();
        if (saved.Any(item => item is null)) return false;
        var qualityCosts = saved.Where(item => Read(item, "improvementsource") == "Quality"
            && Read(item, "improvementttype") is "LifestyleCost" or "BasicLifestyleCost").ToArray();
        var selections = new List<CharacterCreationQualitySelection>();
        var expected = new List<XElement>();
        var ids = new HashSet<Guid>();
        foreach (var group in instances.GroupBy(instance => instance.Selection.OptionId, StringComparer.Ordinal))
        {
            var selection = group.First().Selection;
            if (selection.Rating is < 1 or > 100 || group.Count() != selection.Rating
                || !group.Select(instance => instance.Level).Order().SequenceEqual(Enumerable.Range(1, selection.Rating))
                || group.Any(instance => !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(instance.Selection, selection))
                || !CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, selection.SourceNodeDigest,
                    out var nativeQualities, out var nativeImprovements)) return false;
            selections.Add(selection);
            foreach (var instance in group.OrderBy(instance => instance.Level))
            {
                if (!Guid.TryParse(Read(instance.Quality, "guid"), out var id) || id == Guid.Empty || !ids.Add(id)) return false;
                var nativeQuality = nativeQualities[instance.Level - 1];
                if (!MatchesNative(instance.Quality, nativeQuality, ["guid", "notes", "notesColor"])) return false;
                string nativeId = Read(nativeQuality, "guid");
                foreach (var native in nativeImprovements.Where(item => Read(item, "sourcename") == nativeId
                    && Read(item, "improvementttype") is "LifestyleCost" or "BasicLifestyleCost"))
                {
                    var effect = new XElement(native);
                    effect.SetElementValue("sourcename", id.ToString("D", CultureInfo.InvariantCulture));
                    expected.Add(effect);
                }
            }
        }
        if (!TryProjectPurchasedQualityCosts(selections, out var projected) || expected.Count != qualityCosts.Length) return false;
        // Match by owner and full native semantics, consuming each occurrence once.
        // Identical effects are safe only with the source-proven exact multiplicity.
        var remaining = qualityCosts.ToList();
        foreach (var native in expected)
        {
            int index = remaining.FindIndex(item => MatchesNative(item, native, ["notes", "notesColor"]));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        if (remaining.Count != 0 || !TryResolveMetatypeCosts(saved.Except(qualityCosts), initialTrustFundLevel,
            out trustFundLevel, out metatypeCostPercent, out blockers)) return false;
        costs = projected;
        return true;
    }

    private static bool MatchesNative(XElement saved, XElement expected, IReadOnlyList<string> ignored)
    {
        if (saved.Name != expected.Name || saved.HasAttributes
            || saved.Elements().Any(item => item.Name.Namespace != XNamespace.None || item.HasAttributes)
            || saved.Elements().GroupBy(item => item.Name).Any(group => group.Count() != 1)
            || saved.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) return false;
        var actual = saved.Elements().Where(item => !ignored.Contains(item.Name.LocalName)).OrderBy(item => item.Name.LocalName, StringComparer.Ordinal);
        var native = expected.Elements().Where(item => !ignored.Contains(item.Name.LocalName)).OrderBy(item => item.Name.LocalName, StringComparer.Ordinal);
        return actual.SequenceEqual(native, XNode.EqualityComparer);
    }

    public static bool TryResolve(IEnumerable<XElement> improvements, int initialTrustFundLevel,
        out int trustFundLevel, out string[] blockers)
    {
        bool valid = TryResolveMetatypeCosts(improvements, initialTrustFundLevel,
            out trustFundLevel, out decimal metatypeCostPercent, out blockers);
        if (metatypeCostPercent == 0m) return valid;
        // Older callers cannot consume the extra result. Do not silently drop it.
        blockers = blockers.Append(CharacterCreationLifestylesBlockers.UnsupportedSemantics)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return false;
    }

    public static bool TryResolveMetatypeCosts(IEnumerable<XElement> improvements, int initialTrustFundLevel,
        out int trustFundLevel, out decimal metatypeCostPercent, out string[] blockers)
    {
        trustFundLevel = initialTrustFundLevel;
        metatypeCostPercent = 0m;
        var findings = new List<string>();
        if (initialTrustFundLevel is < 0 or > 4)
            findings.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        foreach (var improvement in improvements)
        {
            // Inspect every type node before Read selects the first. Otherwise
            // duplicate/namespaced fields could hide a cost behind another type.
            if (improvement.Elements().Any(item => item.Name.LocalName == "improvementttype"
                    && item.Value.Trim() is "LifestyleCost" or "BasicLifestyleCost")
                && (improvement.Name != "improvement" || improvement.HasAttributes
                    || improvement.Elements().Any(item => item.Name.Namespace != XNamespace.None || item.HasAttributes || item.HasElements)
                    || improvement.Elements().GroupBy(item => item.Name).Any(group => group.Count() != 1)))
            {
                findings.Add(CharacterCreationLifestylesBlockers.UnsupportedSemantics);
                continue;
            }
            string enabled = Read(improvement, "enabled");
            string condition = Read(improvement, "condition");
            int flag = 1;
            if (!(enabled.Length == 0 || int.TryParse(enabled, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out flag) && flag >= 0)
                || condition is not ("" or "create" or "once" or "career"))
            {
                if (Read(improvement, "improvementttype") is "LifestyleCost" or "BasicLifestyleCost")
                    findings.Add(CharacterCreationLifestylesBlockers.UnsupportedSemantics);
                continue;
            }
            // Valid disabled and career-only effects do not apply during creation.
            // Validate shape and activation first so inactivity cannot hide malformed costs.
            if (flag == 0 || condition == "career") continue;
            switch (Read(improvement, "improvementttype"))
            {
                case "TrustFund":
                    if (!int.TryParse(Read(improvement, "val"), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int value) || value is < 1 or > 4 || trustFundLevel != 0)
                        findings.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
                    else trustFundLevel = value;
                    break;
                case "LifestyleCost":
                    // Only unconditional, global racial adjustments are handled
                    // here. Named/unique/once-only modifiers need their own
                    // precedence model, not a flattened percentage.
                    if (improvement.Name != "improvement" || improvement.HasAttributes
                        || improvement.Elements().Any(item => item.Name.Namespace != XNamespace.None || item.HasAttributes || item.HasElements)
                        || condition.Length != 0
                        || Read(improvement, "improvementsource") is not ("Metatype" or "Metavariant" or "Heritage")
                        || Read(improvement, "improvedname").Length != 0
                        || Read(improvement, "unique").Length != 0
                        || Read(improvement, "uniquename").Length != 0
                        || improvement.Elements().GroupBy(item => item.Name).Any(group => group.Count() != 1)
                        || !decimal.TryParse(Read(improvement, "val"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out decimal percentage) || percentage < 0m)
                        findings.Add(CharacterCreationLifestylesBlockers.UnsupportedSemantics);
                    else
                    {
                        try { metatypeCostPercent = checked(metatypeCostPercent + percentage); }
                        catch (OverflowException) { findings.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable); }
                    }
                    break;
                case "BasicLifestyleCost":
                    // Purchased effects must already have been consumed by the
                    // exact source/owner matcher. Other origins remain unsupported.
                    findings.Add(CharacterCreationLifestylesBlockers.UnsupportedSemantics);
                    break;
            }
        }
        blockers = findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return blockers.Length == 0;
    }

    private static string Read(XElement node, string name) => node.Element(name)?.Value.Trim() ?? string.Empty;
}

public sealed record CharacterCreationLifestyleQualityInstance(
    XElement Quality, int Level, CharacterCreationQualitySelection Selection);
