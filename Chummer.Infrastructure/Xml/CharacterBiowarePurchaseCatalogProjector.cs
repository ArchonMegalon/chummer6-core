using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

/// <summary>
/// Deterministic projection of the already-composed effective Bioware source.
/// It keeps unsupported rows visible as exclusions and never simplifies a
/// dynamic formula, requirement, child, generated asset, or improvement.
/// </summary>
internal static partial class CharacterBiowarePurchaseCatalogProjector
{
    private static readonly HashSet<string> s_AdmittedSourceFields = new(StringComparer.Ordinal)
    {
        "id", "name", "translate", "category", "ess", "capacity", "avail", "cost",
        "source", "page", "forcegrade", "bannedgrades", "isgeneware"
    };

    private static readonly HashSet<string> s_AdmittedGradeFields = new(StringComparer.Ordinal)
    {
        "id", "name", "translate", "ess", "cost", "devicerating", "avail",
        "source", "page", "altpage"
    };

    public static bool TryProject(
        CharacterBiowarePurchaseSourceBinding binding,
        CharacterBiowarePurchaseSettings settings,
        IReadOnlyList<XElement> sourceRows,
        IReadOnlyList<XElement> gradeRows,
        IReadOnlyList<XElement> categoryRows,
        Func<string, bool> isEnabledSource,
        out CharacterBiowarePurchaseCatalogAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(gradeRows);
        ArgumentNullException.ThrowIfNull(categoryRows);
        ArgumentNullException.ThrowIfNull(isEnabledSource);
        authority = CharacterBiowarePurchaseCatalogAuthority.Unavailable;
        if (string.IsNullOrWhiteSpace(binding.SettingsProfileId)
            || string.IsNullOrWhiteSpace(binding.ProfileDigest)
            || !string.Equals(
                binding.RawBiowareXmlDigest,
                $"sha256:{CharacterBiowarePurchaseLegacyAuthority.BiowareXmlSha256}",
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(binding.EffectiveBiowareInputsDigest)
            || string.IsNullOrWhiteSpace(binding.SelectedBiowareCustomDataInputsDigest)
            || string.IsNullOrWhiteSpace(binding.EffectiveSettingsInputsDigest)
            || settings.EssenceDecimals is < 0 or > 28
            || settings.RestrictedCostMultiplier < 0m
            || settings.ForbiddenCostMultiplier < 0m)
        {
            return false;
        }

        var categories = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement row in categoryRows)
        {
            string name = row.Value;
            XAttribute[] attributes = row.Attributes().ToArray();
            if (row.Name.NamespaceName.Length != 0
                || row.Name.LocalName != "category"
                || row.HasElements
                || !string.Equals(name, name.Trim(), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(name)
                || name.IndexOfAny(['\0', '\r', '\n']) >= 0
                || attributes.Length != 1
                || attributes[0].Name.NamespaceName.Length != 0
                || attributes[0].Name.LocalName != "blackmarket"
                || attributes[0].Value is not ("Bioware" or "Geneware")
                || !categories.TryAdd(name, attributes[0].Value))
            {
                return false;
            }
        }

        var gradesById = new Dictionary<Guid, CharacterBiowarePurchaseGrade>();
        foreach (XElement row in gradeRows)
        {
            if (row.Name.NamespaceName.Length != 0
                || row.Name.LocalName != "grade"
                || row.HasAttributes
                || row.Elements().Any(element => element.Name.NamespaceName.Length != 0
                                                  || !s_AdmittedGradeFields.Contains(element.Name.LocalName))
                || !TryReadRequiredScalar(row, "id", out string idText)
                || !Guid.TryParseExact(idText, "D", out Guid id)
                || id == Guid.Empty
                || !TryReadRequiredScalar(row, "name", out string name)
                || !TryReadRequiredNonNegativeDecimal(row, "cost", out decimal costMultiplier)
                || !TryReadRequiredNonNegativeDecimal(row, "ess", out decimal essenceMultiplier)
                || !TryReadRequiredInteger(row, "avail", out int availabilityModifier)
                || !TryReadRequiredScalar(row, "source", out string sourceBook)
                || !TryReadRequiredScalar(row, "page", out string page))
            {
                return false;
            }
            // Parenthesized grades carry character-quality/improvement gates
            // (for example Burnout's Way) that this no-improvements lane cannot
            // replay. They are deliberately unavailable, not a catalog error.
            if (name.Contains('(') || !isEnabledSource(sourceBook))
                continue;
            if (!gradesById.TryAdd(id, new CharacterBiowarePurchaseGrade(
                    new CharacterBiowareGradeId(id),
                    name,
                    costMultiplier,
                    essenceMultiplier,
                    availabilityModifier,
                    sourceBook,
                    page)))
            {
                return false;
            }
        }
        if (gradesById.Count == 0)
            return false;

        var rowsById = new Dictionary<Guid, XElement>();
        foreach (XElement row in sourceRows)
        {
            if (!TryReadRequiredScalar(row, "id", out string idText)
                || !Guid.TryParseExact(idText, "D", out Guid id)
                || id == Guid.Empty
                || !rowsById.TryAdd(id, row))
            {
                return false;
            }
        }

        var entries = new List<CharacterBiowarePurchaseCatalogEntry>();
        var exclusions = new List<CharacterBiowarePurchaseCatalogExclusion>();
        foreach ((Guid id, XElement row) in rowsById.OrderBy(pair => pair.Key))
        {
            string name = ReadOptionalScalar(row, "name");
            var sourceId = new CharacterBiowareSourceId(id);
            string? reason = SourceExclusionReason(row, name, isEnabledSource, categories, out ProjectedSource projected);
            if (reason is not null)
            {
                exclusions.Add(new CharacterBiowarePurchaseCatalogExclusion(sourceId, name, reason));
                continue;
            }

            string[] bannedGrades = projected.BannedGrades
                .Concat(settings.BannedGrades)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            CharacterBiowarePurchaseGrade[] grades = gradesById.Values
                .Where(grade => !bannedGrades.Contains(grade.Name, StringComparer.Ordinal))
                .Where(grade => grade.Name != "None"
                                || string.Equals(projected.ForcedGrade, "None", StringComparison.Ordinal))
                .Where(grade => projected.ForcedGrade.Length == 0
                                || string.Equals(projected.ForcedGrade, grade.Name, StringComparison.Ordinal))
                .OrderBy(grade => grade.Id.Value)
                .ToArray();
            if (grades.Length == 0)
            {
                exclusions.Add(new CharacterBiowarePurchaseCatalogExclusion(
                    sourceId,
                    name,
                    "No side-effect-free enabled grade remains."));
                continue;
            }

            string expectedMarket = projected.IsGeneware ? "Geneware" : "Bioware";
            entries.Add(new CharacterBiowarePurchaseCatalogEntry(
                sourceId,
                name,
                projected.Category,
                projected.EssenceExpression,
                projected.CapacityExpression,
                projected.BaseAvailability,
                projected.Legality,
                projected.AvailabilityExpression,
                projected.CostExpression,
                projected.SourceBook,
                projected.Page,
                string.Equals(categories[projected.Category], expectedMarket, StringComparison.Ordinal),
                projected.IsGeneware,
                projected.ForcedGrade,
                projected.BannedGrades,
                grades));
        }

        var unsigned = new CharacterBiowarePurchaseCatalogAuthority(
            binding,
            settings,
            entries.OrderBy(entry => entry.SourceId.Value).ToArray(),
            exclusions.OrderBy(entry => entry.SourceId.Value).ToArray(),
            string.Empty);
        authority = unsigned with
        {
            AuthorityDigest = CharacterBiowarePurchaseRules.ComputeCatalogAuthorityDigest(unsigned)
        };
        return CharacterBiowarePurchaseRules.IsCanonicalDigest(authority.AuthorityDigest);
    }

    private static string? SourceExclusionReason(
        XElement row,
        string name,
        Func<string, bool> isEnabledSource,
        IReadOnlyDictionary<string, string> categories,
        out ProjectedSource projected)
    {
        projected = ProjectedSource.Unavailable;
        if (row.Name.NamespaceName.Length != 0
            || row.Name.LocalName != "bioware"
            || row.HasAttributes
            || row.Elements().Any(element => element.Name.NamespaceName.Length != 0
                                              || !s_AdmittedSourceFields.Contains(element.Name.LocalName)))
            return "The source row contains rating, improvement, requirement, child, prompt, generated-asset, or otherwise unsupported fields.";
        string[] required = ["id", "name", "category", "ess", "capacity", "avail", "cost", "source", "page"];
        if (required.Any(field => !TryReadRequiredScalar(row, field, out _)))
            return "The source row is missing a unique required scalar.";
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal)
            || name.IndexOfAny(['\r', '\n', '\0']) >= 0)
            return "The source name is not a stable scalar.";
        if (!TryReadOptionalScalar(row, "forcegrade", out string forcedGrade)
            || !TryReadLegacyPresenceBoolean(row, "isgeneware", out bool isGeneware)
            || !TryReadBannedGrades(row, out string[] bannedGrades))
            return "The source grade or Geneware constraints are ambiguous.";
        if (!TryReadRequiredNonNegativeDecimal(row, "cost", out _)
            || !TryReadRequiredNonNegativeDecimal(row, "ess", out _)
            || !TryReadRequiredNonNegativeDecimal(row, "capacity", out _))
            return "Dynamic or negative cost, Essence, or capacity expressions are outside the fixed-value slice.";
        string availabilityExpression = ReadOptionalScalar(row, "avail");
        Match availability = FixedAvailability().Match(availabilityExpression);
        if (!availability.Success
            || !int.TryParse(
                availability.Groups["value"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int baseAvailability))
            return "Dynamic availability expressions are outside the fixed-value slice.";
        CharacterBiowareLegality legality = availability.Groups["legality"].Value switch
        {
            "R" => CharacterBiowareLegality.Restricted,
            "F" => CharacterBiowareLegality.Forbidden,
            _ => CharacterBiowareLegality.Legal
        };
        string category = ReadOptionalScalar(row, "category");
        string sourceBook = ReadOptionalScalar(row, "source");
        if (!categories.ContainsKey(category))
            return "The exact effective category identity is absent.";
        if (!isEnabledSource(sourceBook))
            return "The source book is disabled by the saved profile.";

        projected = new ProjectedSource(
            category,
            ReadOptionalScalar(row, "ess"),
            ReadOptionalScalar(row, "capacity"),
            baseAvailability,
            legality,
            availabilityExpression,
            ReadOptionalScalar(row, "cost"),
            sourceBook,
            ReadOptionalScalar(row, "page"),
            isGeneware,
            forcedGrade,
            bannedGrades);
        return null;
    }

    private sealed record ProjectedSource(
        string Category,
        string EssenceExpression,
        string CapacityExpression,
        int BaseAvailability,
        CharacterBiowareLegality Legality,
        string AvailabilityExpression,
        string CostExpression,
        string SourceBook,
        string Page,
        bool IsGeneware,
        string ForcedGrade,
        IReadOnlyList<string> BannedGrades)
    {
        public static ProjectedSource Unavailable { get; } = new(
            string.Empty, string.Empty, string.Empty, 0, CharacterBiowareLegality.Legal,
            string.Empty, string.Empty, string.Empty, string.Empty, false, string.Empty, []);
    }

    private static bool TryReadBannedGrades(XElement row, out string[] grades)
    {
        grades = [];
        XElement[] containers = row.Elements("bannedgrades").Take(2).ToArray();
        if (containers.Length > 1
            || containers.Any(container => container.HasAttributes
                || container.Elements().Any(node => node.Name.NamespaceName.Length != 0
                                                  || node.Name.LocalName != "grade"
                                                  || node.HasAttributes
                                                  || node.HasElements
                                                  || !string.Equals(node.Value, node.Value.Trim(), StringComparison.Ordinal)
                                                  || string.IsNullOrWhiteSpace(node.Value))))
            return false;
        grades = containers.SingleOrDefault()?.Elements("grade")
            .Select(node => node.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray() ?? [];
        return true;
    }

    private static bool TryReadRequiredNonNegativeDecimal(XElement parent, string name, out decimal value)
    {
        value = 0m;
        return TryReadRequiredScalar(parent, name, out string text)
               && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
               && value >= 0m;
    }

    private static bool TryReadRequiredInteger(XElement parent, string name, out int value)
    {
        value = 0;
        return TryReadRequiredScalar(parent, name, out string text)
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadRequiredScalar(XElement parent, string name, out string value)
        => TryReadScalar(parent, name, out value) && !string.IsNullOrWhiteSpace(value);

    private static bool TryReadOptionalScalar(XElement parent, string name, out string value)
    {
        value = string.Empty;
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length == 0)
            return true;
        if (matches.Length != 1 || matches[0].HasAttributes || matches[0].HasElements)
            return false;
        value = matches[0].Value;
        return string.Equals(value, value.Trim(), StringComparison.Ordinal)
               && value.IndexOfAny(['\0', '\r', '\n']) < 0;
    }

    private static bool TryReadLegacyPresenceBoolean(XElement parent, string name, out bool value)
    {
        value = false;
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length == 0)
            return true;
        if (matches.Length != 1
            || matches[0].HasAttributes
            || matches[0].HasElements
            || !string.Equals(matches[0].Value, matches[0].Value.Trim(), StringComparison.Ordinal)
            || matches[0].Value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            return false;
        // Cyberware.CreateAsync treats the presence of isgeneware as true
        // unless its exact text is the legacy Boolean.FalseString.
        value = !string.Equals(matches[0].Value, bool.FalseString, StringComparison.Ordinal);
        return true;
    }

    private static bool TryReadScalar(XElement parent, string name, out string value)
    {
        value = string.Empty;
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].HasAttributes || matches[0].HasElements)
            return false;
        value = matches[0].Value;
        return string.Equals(value, value.Trim(), StringComparison.Ordinal)
               && value.IndexOfAny(['\0', '\r', '\n']) < 0;
    }

    private static string ReadOptionalScalar(XElement parent, string name)
        => TryReadScalar(parent, name, out string value) ? value : string.Empty;

    [GeneratedRegex("^(?<value>[0-9]+)(?<legality>[RF]?)$", RegexOptions.CultureInvariant)]
    private static partial Regex FixedAvailability();
}
