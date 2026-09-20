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
        trustFundLevel = initialTrustFundLevel;
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
