using System.Xml.Linq;

namespace Chummer.Application.Characters;

// Additive access to the existing pure arithmetic. The Creation entry point and
// its created=false/inventory admission requirements remain exactly unchanged.
internal static partial class CharacterCreationFocusKarma1220
{
    internal static bool IsOrdinaryPriceField(string name, string field)
        => !name.Contains("Individualized", StringComparison.Ordinal)
        && types.Any(x => x.Name == Normalize(name, false).Name && x.Field == field);

    internal static bool TryPriceOwnedOrdinary(XElement profile, XElement character, XElement gear,
        out Component? component, out int karma)
    {
        component = null; karma = 0;
        try
        {
            string name = Scalar(gear, "name");
            if (name.Contains("Individualized", StringComparison.Ordinal)
                || name.StartsWith("Stacked Focus", StringComparison.Ordinal)
                || !TryReadPrices(profile, out var prices)) return false;
            component = PriceGear(gear, false, prices.ToDictionary(x => x.Field, StringComparer.Ordinal), ReadModifiers(character));
            karma = StandardRound(component.Unrounded);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException
            or ArgumentException or OverflowException or FormatException) { return false; }
    }
}
