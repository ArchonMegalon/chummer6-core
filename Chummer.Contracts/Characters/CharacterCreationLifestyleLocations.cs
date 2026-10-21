namespace Chummer.Contracts.Characters;

// These are source data and projection values, never execution capabilities.
public sealed record CharacterCreationLifestyleLocationNode(
    Guid? SourceId,
    string Name,
    string SourcePath,
    string? SecurityRating,
    string SourceNodeDigest,
    IReadOnlyList<CharacterCreationLifestyleLocationNode> Children);

public sealed record CharacterCreationLifestyleLocationCatalog(
    IReadOnlyList<CharacterCreationLifestyleLocationNode> Cities);

public enum CharacterCreationLifestyleLocationKind
{
    Absent = 0,
    Custom = 1,
    Catalogue = 2,
    Placeholder = 3
}

public sealed record CharacterCreationLifestyleLocationComponent(
    string Text,
    CharacterCreationLifestyleLocationKind Kind,
    Guid? SourceId,
    string? SourceNodeDigest,
    string? SourcePath,
    string? SecurityRating);

public sealed record CharacterCreationLifestyleLocationSelection(
    CharacterCreationLifestyleLocationComponent City,
    CharacterCreationLifestyleLocationComponent District,
    CharacterCreationLifestyleLocationComponent Borough);

public sealed record CharacterCreationLifestyleCategory(string Name, string QualityType);

public sealed record CharacterCreationLifestyleQualitySelectionSemantics(bool RequiresText, bool AllowMultiple);

public static class CharacterCreationLifestyleLocationRules
{
    public static bool IsValidCatalog(CharacterCreationLifestyleLocationCatalog? catalog)
    {
        if (catalog is null) return true; // Historical authority: no catalogue claim.
        var ids = new HashSet<Guid>();
        int remaining = 65_536;
        return ValidRows(catalog.Cities, 0, "lifestyles.xml#/cities", ids, ref remaining);
    }

    private static bool ValidRows(IReadOnlyList<CharacterCreationLifestyleLocationNode>? rows,
        int depth, string parentPath, HashSet<Guid> ids, ref int remaining)
    {
        if (rows is null || rows.Count > remaining || depth > 3 || depth == 3 && rows.Count != 0) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var row in rows)
        {
            string kind = depth == 0 ? "city" : depth == 1 ? "district" : "borough";
            string path = parentPath + "/" + kind + "[" + (++index).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            if (row is null || --remaining < 0 || !ValidText(row.Name) || !names.Add(row.Name)
                || row.SourceId is { } id && (id == Guid.Empty || !ids.Add(id))
                || row.SourcePath != path
                || !CharacterCreationLifestylesRules.IsCanonicalDigest(row.SourceNodeDigest)
                || row.SecurityRating is not null && (depth != 2 || row.Name.Length == 0
                    || !ValidText(row.SecurityRating) || row.SecurityRating.Length == 0)
                || !ValidRows(row.Children, depth + 1, path, ids, ref remaining)) return false;
            // A blank source row is a placeholder, not an unnamed real place.
            if (row.Name.Length == 0 && (row.SourceId is not null || row.Children.Any(child => child.Name.Length != 0)))
                return false;
        }
        return true;
    }

    public static bool TrySelect(CharacterCreationLifestyleConfiguration configuration,
        CharacterCreationLifestyleLocationCatalog catalog, out CharacterCreationLifestyleLocationSelection selection)
    {
        selection = null!;
        if (catalog is null || configuration is null || !IsValidCatalog(catalog) || !ValidText(configuration.City)
            || !ValidText(configuration.District) || !ValidText(configuration.Borough)) return false;
        var city = Find(catalog.Cities, configuration.City);
        var district = Find(city?.Children, configuration.District);
        var borough = Find(district?.Children, configuration.Borough);
        selection = new(Component(configuration.City, city), Component(configuration.District, district),
            Component(configuration.Borough, borough));
        return true;
    }

    private static CharacterCreationLifestyleLocationNode? Find(
        IReadOnlyList<CharacterCreationLifestyleLocationNode>? rows, string text) =>
        rows?.SingleOrDefault(row => string.Equals(row.Name, text, StringComparison.Ordinal));

    private static CharacterCreationLifestyleLocationComponent Component(string text,
        CharacterCreationLifestyleLocationNode? node) => new(text,
        node is not null ? (text.Length == 0 ? CharacterCreationLifestyleLocationKind.Placeholder
            : CharacterCreationLifestyleLocationKind.Catalogue)
            : text.Length == 0 ? CharacterCreationLifestyleLocationKind.Absent : CharacterCreationLifestyleLocationKind.Custom,
        node?.SourceId, node?.SourceNodeDigest, node?.SourcePath, node?.SecurityRating);

    private static bool ValidText(string? text) => text is not null
        && text.Length <= CharacterCreationLifestylesRules.MaximumTextLength && text == text.Trim()
        && text.All(character => !char.IsControl(character) || character is '\r' or '\n' or '\t');

    public static string QualityType(string category) => category switch
    {
        "Positive" => CharacterCreationLifestyleQualityTypes.Positive,
        "Negative" => CharacterCreationLifestyleQualityTypes.Negative,
        "Contracts" => CharacterCreationLifestyleQualityTypes.Contracts,
        _ when category.StartsWith("Entertainment", StringComparison.Ordinal) => CharacterCreationLifestyleQualityTypes.Entertainment,
        _ => string.Empty
    };

    public static bool IsValidCategories(IReadOnlyList<CharacterCreationLifestyleCategory>? categories) =>
        categories is null || categories.Count <= 65_536
        && categories.All(category => category is not null && ValidText(category.Name)
            && category.Name.Length != 0 && category.QualityType is { Length: > 0 } && category.QualityType == QualityType(category.Name))
        && categories.Select(category => category.Name).Distinct(StringComparer.Ordinal).Count() == categories.Count;
}
