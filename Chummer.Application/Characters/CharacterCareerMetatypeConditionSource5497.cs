using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

// Source-shape discovery only. It cannot issue a catalog, execution or authority.
public interface ICurrentCareerConditionSourceCapabilities5497
{
    bool TryRequiresCareerCondition5497(out bool required);
}

/// <summary>Bounded integer subset of the sheet's conditionmonitor/overflow effect.</summary>
public static class CharacterCareerMetatypeConditionSource5497
{
    public static bool TryReadOverflow(XElement condition, out int overflow)
    {
        overflow = 0;
        return condition.Name == "conditionmonitor" && !condition.HasAttributes
            && condition.Nodes().All(n => n is XElement || n is XText t
                && t.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(t.Value))
            && condition.Elements().ToArray() is [var field] && field.Name == "overflow"
            && !field.HasAttributes && !field.HasElements
            && field.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText)
            && int.TryParse(text.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out overflow)
            && overflow is >= -100 and <= 100;
    }

    internal static XElement Compile(string metatypeId, int overflow)
    {
        if (!Guid.TryParseExact(metatypeId, "D", out var id) || id == Guid.Empty
            || overflow is < -100 or > 100) throw new InvalidDataException();
        var row = CharacterCreationAwakenedLegacyProjector.Improvement(
            "CMOverflow", string.Empty, metatypeId, "Metatype", overflow);
        // The sheet owner compiler and Character.Save include the empty unique
        // field. Keep it in this source-compiled row; matching stays exact.
        row.Add(new XElement("unique", string.Empty));
        return row;
    }
}
