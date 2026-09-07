using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

/// <summary>Source instance limits are not rating limits. The effective rating cap
/// is min(source maxlevel(s), current MAG), matching Power.TotalMaximumLevels.</summary>
public static class CharacterCreationAdeptPowerSourceRules
{
    public static bool TryReadMaximumLevels(XElement source, out int maximumLevels, out int savedMaximum)
    {
        maximumLevels = 1;
        savedMaximum = 0;
        if (!Scalar(source, "levels", "False", out string text) || !bool.TryParse(text, out bool levels)
            || source.Elements("maxlevel").Any() && source.Elements("maxlevels").Any()
            || !Scalar(source, source.Element("maxlevel") is null ? "maxlevels" : "maxlevel", "0", out string maximum)
            || !int.TryParse(maximum, NumberStyles.None, CultureInfo.InvariantCulture, out savedMaximum)) return false;
        maximumLevels = levels ? (savedMaximum > 0 ? savedMaximum : int.MaxValue) : 1;
        return true;
    }

    public static int EffectiveMaximumLevels(CharacterCreationMagicResonanceCatalogOption option, int magic) =>
        Math.Max(0, Math.Min(option.MaximumLevels, magic));

    /// <summary>Only prompt-free, effect-free powers in this projection. Way
    /// eligibility is retained verbatim as metadata and no discount is applied.</summary>
    public static bool IsUndiscountedPayloadSupported(XElement source)
    {
        if (!TryReadMaximumLevels(source, out _, out _)
            || !Scalar(source, "points", "", out string pointsText)
            || !decimal.TryParse(pointsText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal points)
            || points <= 0 || !Scalar(source, "adeptway", "0", out string wayText)
            || !decimal.TryParse(wayText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal way)
            || way < 0 || way > points
            || source.Elements("bonus").Count() > 1
            || source.Element("bonus") is { } bonus && (bonus.HasAttributes || bonus.HasElements || !string.IsNullOrWhiteSpace(bonus.Value))
            || source.Elements("adeptwayrequires").Count() > 1) return false;
        XElement? requirements = source.Element("adeptwayrequires");
        if (requirements is null) return true;
        if (requirements.HasAttributes || HasText(requirements)) return false;
        foreach (var rule in requirements.Elements())
        {
            if (rule.HasAttributes) return false;
            if (rule.Name == "magicianswayforbids")
            {
                if (rule.HasElements || !string.IsNullOrWhiteSpace(rule.Value)) return false;
                continue;
            }
            if (rule.Name != "required" || HasText(rule) || rule.Elements().Count() != 1
                || rule.Element("oneof") is not { } oneOf || oneOf.HasAttributes || HasText(oneOf)
                || !oneOf.HasElements || oneOf.Elements().Any(item => item.Name != "quality"
                    || item.HasAttributes || item.HasElements || string.IsNullOrWhiteSpace(item.Value))) return false;
        }
        return true;
    }

    private static bool Scalar(XElement source, string name, string fallback, out string value)
    {
        XElement[] nodes = source.Elements(name).ToArray();
        value = nodes.FirstOrDefault()?.Value ?? fallback;
        return nodes.Length <= 1 && nodes.All(item => !item.HasAttributes && !item.HasElements);
    }
    private static bool HasText(XElement node) => node.Nodes().OfType<XText>().Any(item => !string.IsNullOrWhiteSpace(item.Value));
}
