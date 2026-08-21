using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterTraditionSpiritCategory
{
    Combat,
    Detection,
    Health,
    Illusion,
    Manipulation
}

public sealed record CharacterTraditionSpiritCategoryValue(
    CharacterTraditionSpiritCategory Category,
    string SpiritName);

public sealed record CharacterTraditionSpiritCategoryFieldState(
    CharacterTraditionSpiritCategory Category,
    string ElementName,
    string SpiritName,
    string Revision);

public sealed record CharacterTraditionSpiritCategorySemantics(
    Guid TraditionId,
    Guid SourceId,
    IReadOnlyList<string> AllowedSpiritNames,
    IReadOnlyList<CharacterTraditionSpiritCategoryFieldState> Fields);

/// <summary>
/// Exact authority for the five Chummer5 magical-tradition spirit-category
/// selectors. Only the Custom MAG tradition is editable. The source catalog is
/// filtered by enabled LimitSpiritCategory improvements, and blank is the
/// canonical first choice.
/// </summary>
public static class CharacterTraditionSpiritCategoryRules
{
    public const int RevisionHexLength = 64;

    private static readonly CharacterTraditionSpiritCategory[] OrderedCategories =
    [
        CharacterTraditionSpiritCategory.Combat,
        CharacterTraditionSpiritCategory.Detection,
        CharacterTraditionSpiritCategory.Health,
        CharacterTraditionSpiritCategory.Illusion,
        CharacterTraditionSpiritCategory.Manipulation
    ];

    public static IReadOnlyList<CharacterTraditionSpiritCategory> Categories => OrderedCategories;

    public static string ElementName(CharacterTraditionSpiritCategory category)
        => category switch
        {
            CharacterTraditionSpiritCategory.Combat => "spiritcombat",
            CharacterTraditionSpiritCategory.Detection => "spiritdetection",
            CharacterTraditionSpiritCategory.Health => "spirithealth",
            CharacterTraditionSpiritCategory.Illusion => "spiritillusion",
            CharacterTraditionSpiritCategory.Manipulation => "spiritmanipulation",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };

    public static bool TryCreateSemantics(
        Guid traditionId,
        Guid sourceId,
        string? traditionType,
        bool magicEnabled,
        bool resonanceEnabled,
        IReadOnlyList<CharacterTraditionSpiritCategoryValue>? fields,
        IReadOnlyList<string>? sourceCatalogNames,
        IReadOnlyList<string>? limitCategories,
        out CharacterTraditionSpiritCategorySemantics semantics)
    {
        semantics = Unavailable();
        if (traditionId == Guid.Empty
            || sourceId != CharacterTraditionNameRules.CustomMagicalTraditionSourceId
            || !string.Equals(traditionType, "MAG", StringComparison.Ordinal)
            || !magicEnabled
            || resonanceEnabled
            || fields is null
            || sourceCatalogNames is null
            || limitCategories is null
            || !TryNormalizeCatalog(sourceCatalogNames, out string[] catalog)
            || !TryNormalizeLimits(limitCategories, out HashSet<string> limits)
            || !TryNormalizeFields(fields, out Dictionary<CharacterTraditionSpiritCategory, string> values))
        {
            return false;
        }

        var allowed = new List<string>(catalog.Length + 1) { string.Empty };
        foreach (string name in catalog)
        {
            if (limits.Count == 0 || limits.Contains(name))
            {
                allowed.Add(name);
            }
        }

        var fieldStates = new List<CharacterTraditionSpiritCategoryFieldState>(OrderedCategories.Length);
        foreach (CharacterTraditionSpiritCategory category in OrderedCategories)
        {
            string value = values[category];
            if (!allowed.Contains(value, StringComparer.Ordinal))
            {
                return false;
            }
            fieldStates.Add(new CharacterTraditionSpiritCategoryFieldState(
                category,
                ElementName(category),
                value,
                CalculateFieldRevision(
                    traditionId,
                    sourceId,
                    category,
                    value,
                    catalog,
                    limits)));
        }

        semantics = new CharacterTraditionSpiritCategorySemantics(
            traditionId,
            sourceId,
            allowed.ToArray(),
            fieldStates.ToArray());
        return true;
    }

    public static bool TryValidateRequestedValue(
        CharacterTraditionSpiritCategorySemantics semantics,
        CharacterTraditionSpiritCategory category,
        string? expectedFieldRevision,
        string? requestedValue,
        out string validated)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        validated = string.Empty;
        if (requestedValue is null
            || !IsValidChoice(requestedValue, allowBlank: true)
            || expectedFieldRevision is null
            || expectedFieldRevision.Length != RevisionHexLength
            || semantics.Fields is null
            || semantics.AllowedSpiritNames is null)
        {
            return false;
        }

        CharacterTraditionSpiritCategoryFieldState? field = semantics.Fields.SingleOrDefault(
            candidate => candidate.Category == category);
        if (field is null
            || !string.Equals(field.Revision, expectedFieldRevision, StringComparison.Ordinal)
            || !semantics.AllowedSpiritNames.Contains(requestedValue, StringComparer.Ordinal))
        {
            return false;
        }

        validated = requestedValue;
        return true;
    }

    private static bool TryNormalizeCatalog(IReadOnlyList<string> source, out string[] catalog)
    {
        var values = new List<string>(source.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? value in source)
        {
            if (!IsValidChoice(value, allowBlank: false) || !seen.Add(value!))
            {
                catalog = [];
                return false;
            }
            values.Add(value!);
        }
        catalog = values.ToArray();
        return catalog.Length != 0;
    }

    private static bool TryNormalizeLimits(
        IReadOnlyList<string> source,
        out HashSet<string> limits)
    {
        limits = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? value in source)
        {
            if (!IsValidChoice(value, allowBlank: false))
            {
                return false;
            }
            limits.Add(value!);
        }
        return true;
    }

    private static bool TryNormalizeFields(
        IReadOnlyList<CharacterTraditionSpiritCategoryValue> source,
        out Dictionary<CharacterTraditionSpiritCategory, string> values)
    {
        values = new Dictionary<CharacterTraditionSpiritCategory, string>();
        foreach (CharacterTraditionSpiritCategoryValue? field in source)
        {
            if (field is null
                || !OrderedCategories.Contains(field.Category)
                || !IsValidChoice(field.SpiritName, allowBlank: true)
                || !values.TryAdd(field.Category, field.SpiritName))
            {
                return false;
            }
        }
        return values.Count == OrderedCategories.Length;
    }

    private static bool IsValidChoice(string? value, bool allowBlank)
        => value is not null
            && (allowBlank || value.Length != 0)
            && (value.Length == 0 || !string.IsNullOrWhiteSpace(value))
            && value.Length <= CharacterSpiritNameChoiceRules.MaximumNameLength
            && value.IndexOfAny(['\r', '\n', '\0']) < 0;

    private static string CalculateFieldRevision(
        Guid traditionId,
        Guid sourceId,
        CharacterTraditionSpiritCategory category,
        string value,
        IReadOnlyList<string> sourceCatalog,
        IReadOnlySet<string> limitCategories)
    {
        var payload = new StringBuilder();
        payload.Append(traditionId.ToString("D"))
            .Append('\0')
            .Append(sourceId.ToString("D"))
            .Append('\0')
            .Append(category)
            .Append('\0')
            .Append(value);
        payload.Append("\0catalog");
        foreach (string choice in sourceCatalog)
        {
            payload.Append('\0').Append(choice);
        }
        payload.Append("\0limits");
        foreach (string limit in limitCategories.OrderBy(limit => limit, StringComparer.Ordinal))
        {
            payload.Append('\0').Append(limit);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterTraditionSpiritCategorySemantics Unavailable()
        => new(
            Guid.Empty,
            Guid.Empty,
            Array.Empty<string>(),
            Array.Empty<CharacterTraditionSpiritCategoryFieldState>());
}
