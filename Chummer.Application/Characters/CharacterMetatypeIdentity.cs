using System.Xml.Linq;

namespace Chummer.Application.Characters;

/// <summary>Reads saved aliases only; the caller must still resolve the selected source and its authority.</summary>
public static class CharacterMetatypeIdentity
{
    public static bool TryRead(XElement character, out string name, out Guid? id,
        out string variantName, out Guid? variantId)
    {
        name = variantName = string.Empty;
        id = variantId = null;
        return character.Name == "character" && !character.HasAttributes
            && Scalar(character, "metatype", out name) && name.Length > 0
            && Aliases(character, "metatypeguid", "metatypeid", out id) && id != Guid.Empty
            && Scalar(character, "metavariant", out variantName)
            && Aliases(character, "metavariantguid", "metavariantid", out variantId);
    }

    private static bool Aliases(XElement parent, string portable, string legacy, out Guid? id)
    {
        id = null;
        if (!Scalar(parent, portable, out var first) || !Scalar(parent, legacy, out var second)) return false;
        Guid? a = null, b = null;
        if (first.Length > 0)
        {
            if (!Guid.TryParseExact(first, "D", out var parsed)) return false;
            a = parsed;
        }
        if (second.Length > 0)
        {
            if (!Guid.TryParseExact(second, "D", out var parsed)) return false;
            b = parsed;
        }
        if (a.HasValue && b.HasValue && a != b) return false;
        id = a ?? b;
        return true;
    }

    private static bool Scalar(XElement parent, string name, out string value)
    {
        var rows = parent.Elements().Where(row => row.Name.LocalName == name).Take(2).ToArray();
        value = rows.FirstOrDefault()?.Value ?? string.Empty;
        return rows.Length == 0 || rows is [var row] && row.Name == name
            && !row.HasAttributes && !row.HasElements && value == value.Trim();
    }
}
