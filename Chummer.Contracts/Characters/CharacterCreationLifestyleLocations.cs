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

public sealed record CharacterCreationLifestylePredicate(
    string Kind, string Value, string? Extra, IReadOnlyList<CharacterCreationLifestylePredicate> Children);
public sealed record CharacterCreationLifestyleTextChoice(string ChoiceId, string Text);
public sealed record CharacterCreationLifestyleTextPrompt(
    string SourceFile, string XPath, string SourceDigest, bool AllowCustom,
    IReadOnlyList<CharacterCreationLifestyleTextChoice> Choices);
public sealed record CharacterCreationLifestyleTextSelection(string Kind, string? ChoiceId);
public sealed record CharacterCreationLifestyleQualityMechanics(
    IReadOnlyList<CharacterCreationLifestylePredicate> Required,
    IReadOnlyList<CharacterCreationLifestylePredicate> Forbidden,
    CharacterCreationLifestyleTextPrompt? Prompt);
public sealed record CharacterCreationLifestyleCharacterFact(Guid SourceId, string Name, string Extra);
public sealed record CharacterCreationLifestyleMechanicsContext(
    string Capability, bool CharacterFactsExact,
    IReadOnlyList<CharacterCreationLifestyleCharacterFact> CharacterQualities);
public sealed record CharacterCreationLifestyleAspectProjection(
    int Area, int Comforts, int Security, int MaximumAreaIncrease,
    int MaximumComfortsIncrease, int MaximumSecurityIncrease);

/// <summary>V7 quote predicates. Data is compiled and rebound to retained sources by the application.</summary>
public static class CharacterCreationLifestyleMechanicsRules
{
    public const string CapabilityElement = "lifestylequalitysemantics";
    public const string Capability = "source-quality-mechanics-v1";
    // Native extension v1; deliberately not an upstream Chummer5a predicate.
    public const string StartingBasePointsPredicate = "chummer-starting-base-lp-v1";

    public static bool TryReadStartingBasePoints(string? value, out int minimum)
        => int.TryParse(value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out minimum)
            && minimum is >= 0 and <= 65_535
            && value == minimum.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public const string PrerequisiteNotMet = "creation-lifestyles-prerequisite-not-met";
    public const string ExclusionMatched = "creation-lifestyles-exclusion-matched";
    public const string TextSelectionInvalid = "creation-lifestyles-text-selection-invalid";

    public static bool IsValid(CharacterCreationLifestylesAuthority authority)
    {
        if (authority.QualityOptions is null || authority.QualityOptions.Any(option => option is null)) return false;
        if (authority.Mechanics is null) return authority.QualityOptions.All(option => option.Mechanics is null);
        var context = authority.Mechanics;
        return context.Capability == Capability && context.CharacterQualities is { Count: <= 4096 }
            && context.CharacterQualities.All(fact => fact is not null && Text(fact.Name) && fact.Name.Length != 0 && Text(fact.Extra))
            && authority.QualityOptions.All(option => option.Mechanics is null || Valid(option.Mechanics));
    }

    private static bool Text(string? text) => text is not null && text.Length <= 32767
        && text.All(c => !char.IsControl(c) || c is '\t' or '\r' or '\n');
    private static bool Valid(CharacterCreationLifestyleQualityMechanics mechanics)
    {
        int budget = 256;
        if (!Predicates(mechanics.Required, 0, ref budget) || !Predicates(mechanics.Forbidden, 0, ref budget)) return false;
        var prompt = mechanics.Prompt;
        return prompt is null || prompt.SourceFile == "skills.xml" && Text(prompt.XPath)
            && CharacterCreationLifestylesRules.IsCanonicalDigest(prompt.SourceDigest)
            && prompt.Choices is { Count: > 0 and <= 4096 }
            && prompt.Choices.All(choice => choice is not null && Text(choice.Text) && choice.Text.Length != 0
                && CharacterCreationLifestylesRules.IsCanonicalDigest(choice.ChoiceId))
            && prompt.Choices.Select(choice => choice.ChoiceId).Distinct(StringComparer.Ordinal).Count() == prompt.Choices.Count;
    }
    private static bool Predicates(IReadOnlyList<CharacterCreationLifestylePredicate>? rows, int depth, ref int budget)
    {
        if (rows is null || rows.Count > 256 || depth > 8) return false;
        foreach (var row in rows)
        {
            if (--budget < 0 || row is null || !Text(row.Value) || row.Extra is not null && !Text(row.Extra)) return false;
            if (row.Kind is "oneof" or "allof")
            {
                if (row.Value.Length != 0 || row.Extra is not null || row.Children is null || row.Children.Count == 0
                    || !Predicates(row.Children, depth + 1, ref budget)) return false;
            }
            else if (row.Kind == StartingBasePointsPredicate)
            {
                if (row.Extra is not null || row.Children is not { Count: 0 }
                    || !TryReadStartingBasePoints(row.Value, out _)) return false;
            }
            else if (row.Kind is not ("lifestyle" or "lifestylequality" or "characterquality")
                || row.Value.Length == 0 || row.Children is not { Count: 0 }) return false;
        }
        return true;
    }

    public static IReadOnlyList<string> ValidateSelection(CharacterCreationLifestyleConfiguration configuration,
        CharacterCreationLifestyleCatalogOption lifestyle, CharacterCreationLifestylesAuthority authority)
    {
        var findings = new List<string>();
        if (authority.Mechanics is not { } context) return findings;
        var catalog = authority.QualityOptions.ToDictionary(option => option.OptionId, StringComparer.Ordinal);
        var selected = configuration.Qualities.Where(selection => catalog.ContainsKey(selection.OptionId))
            .Select(selection => (Selection: selection, Option: catalog[selection.OptionId])).ToArray();
        foreach (var (selection, option) in selected)
        {
            if (selection.IsBuiltIn || option.Mechanics is not { } mechanics) continue;
            if (option.SelectionSemantics is { AllowMultiple: false }
                && selected.Count(other => other.Option.Name == option.Name || other.Option.SourceId == option.SourceId) > 1)
                findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation + ":" + option.OptionId);
            // Validate every selected row against the final collection, including reverse exclusions.
            bool Match(CharacterCreationLifestylePredicate predicate) => predicate.Kind switch
            {
                "oneof" => predicate.Children.Any(Match),
                "allof" => predicate.Children.All(Match),
                "lifestyle" => lifestyle.Name == predicate.Value,
                // Source base LP only: never remaining LP, quality credits, roommates or bonus rolls.
                StartingBasePointsPredicate => TryReadStartingBasePoints(predicate.Value, out int minimum)
                    && lifestyle.LifestylePoints >= minimum,
                "lifestylequality" => selected.Any(other => other.Selection.InstanceId != selection.InstanceId
                    && Identity(other.Option.SourceId, other.Option.Name, predicate.Value)
                    && (predicate.Extra is null || other.Selection.Extra == predicate.Extra)),
                "characterquality" => context.CharacterQualities.Any(fact => Identity(fact.SourceId, fact.Name, predicate.Value)
                    && (predicate.Extra is null || fact.Extra == predicate.Extra)),
                _ => false
            };
            bool UsesCharacter(CharacterCreationLifestylePredicate predicate) => predicate.Kind == "characterquality"
                || predicate.Children.Any(UsesCharacter);
            if (!context.CharacterFactsExact && mechanics.Required.Concat(mechanics.Forbidden).Any(UsesCharacter))
                findings.Add(CharacterCreationLifestylesBlockers.UnsupportedSemantics + ":" + option.OptionId + ":re-resolve selected character qualities");
            if (!mechanics.Required.All(Match)) findings.Add(PrerequisiteNotMet + ":" + option.OptionId + ":requires "
                + string.Join("; ", mechanics.Required.Where(predicate => !Match(predicate)).Select(Explain)));
            if (mechanics.Forbidden.Any(Match)) findings.Add(ExclusionMatched + ":" + option.OptionId + ":conflicts with "
                + string.Join("; ", mechanics.Forbidden.Where(Match).Select(Explain)));
            if (mechanics.Prompt is { } prompt)
            {
                bool valid = selection.TextSelection is { Kind: "catalogue", ChoiceId: not null } choice
                    && prompt.Choices.Any(item => item.ChoiceId == choice.ChoiceId && item.Text == selection.Extra)
                    || selection.TextSelection is { Kind: "custom", ChoiceId: null }
                        && prompt.AllowCustom && !string.IsNullOrWhiteSpace(selection.Extra);
                if (!valid) findings.Add(TextSelectionInvalid + ":" + option.OptionId);
            }
            else if (selection.TextSelection is not null) findings.Add(TextSelectionInvalid + ":" + option.OptionId);
        }
        return findings;
    }
    private static string Explain(CharacterCreationLifestylePredicate predicate) => predicate.Kind switch
    {
        StartingBasePointsPredicate => "starting base lifestyle points >= " + predicate.Value,
        "oneof" => "one of (" + string.Join(", ", predicate.Children.Select(Explain)) + ")",
        "allof" => "all of (" + string.Join(", ", predicate.Children.Select(Explain)) + ")",
        _ => predicate.Value + (predicate.Extra is null ? string.Empty : " (" + predicate.Extra + ")")
    };
    private static bool Identity(Guid id, string name, string value) => name == value
        || Guid.TryParse(value, out var parsed) && parsed == id;
}
