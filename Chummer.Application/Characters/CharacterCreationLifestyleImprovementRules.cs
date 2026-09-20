using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Shared creation-time admission for saved and pending lifestyle effects.</summary>
public static class CharacterCreationLifestyleImprovementRules
{
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
            string enabled = Read(improvement, "enabled");
            string condition = Read(improvement, "condition");
            if (!(enabled.Length == 0 || int.TryParse(enabled, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int flag) && flag > 0)
                || condition is not ("" or "create" or "once")) continue;
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
                    // Existing source precedence is not projected by this lane.
                    // A pending quality must not silently escape the saved-XML guard.
                    findings.Add(CharacterCreationLifestylesBlockers.UnsupportedSemantics);
                    break;
            }
        }
        blockers = findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return blockers.Length == 0;
    }

    private static string Read(XElement node, string name) => node.Element(name)?.Value.Trim() ?? string.Empty;
}
