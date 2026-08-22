namespace Chummer.Contracts.Characters;

/// <summary>
/// Source-exact state for Chummer5's shared SpiritControl Spirit/Sprite metatype selector.
/// The selected value is the persisted Spirit.name; CritterName remains a separate field.
/// </summary>
public sealed record CharacterSpiritNameChoiceState(
    Guid SpiritId,
    string EntityType,
    string CurrentName,
    IReadOnlyList<string> AllowedNames);

public static class CharacterSpiritNameChoiceRules
{
    public const int MaximumNameLength = 32_767;

    /// <summary>
    /// Mirrors SpiritControl.RebuildSpiritList: category limits filter the tradition/stream
    /// base list, while enabled AddSpirit/AddSprite improvements are appended afterwards.
    /// </summary>
    public static bool TryProject(
        Guid selectedSpiritId,
        IReadOnlyList<Guid> spiritIds,
        string? entityType,
        string? currentName,
        IReadOnlyList<string> baseNames,
        IReadOnlyList<string> limitCategories,
        bool magicEnabled,
        bool resonanceEnabled,
        IReadOnlyList<string> addedSpiritNames,
        IReadOnlyList<string> addedSpriteNames,
        out CharacterSpiritNameChoiceState? state)
    {
        ArgumentNullException.ThrowIfNull(spiritIds);
        ArgumentNullException.ThrowIfNull(baseNames);
        ArgumentNullException.ThrowIfNull(limitCategories);
        ArgumentNullException.ThrowIfNull(addedSpiritNames);
        ArgumentNullException.ThrowIfNull(addedSpriteNames);
        state = null;

        var identities = new HashSet<Guid>();
        if (selectedSpiritId == Guid.Empty
            || spiritIds.Count == 0
            || spiritIds.Any(id => id == Guid.Empty || !identities.Add(id))
            || !identities.Contains(selectedSpiritId)
            || entityType is not ("Spirit" or "Sprite")
            || !IsValidName(currentName))
        {
            return false;
        }

        if (!TryNormalizeNames(baseNames, allowEmpty: true, requireUnique: false, out string[] normalizedBase)
            || !TryNormalizeNames(limitCategories, allowEmpty: true, requireUnique: false, out string[] normalizedLimits)
            || !TryNormalizeNames(addedSpiritNames, allowEmpty: true, requireUnique: false, out string[] normalizedAddedSpirits)
            || !TryNormalizeNames(addedSpriteNames, allowEmpty: true, requireUnique: false, out string[] normalizedAddedSprites))
        {
            return false;
        }

        HashSet<string>? limits = normalizedLimits.Length == 0
            ? null
            : normalizedLimits.ToHashSet(StringComparer.Ordinal);
        var allowed = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in normalizedBase)
        {
            if ((limits is null || limits.Contains(name)) && seen.Add(name))
            {
                allowed.Add(name);
            }
        }
        if (magicEnabled)
        {
            AppendUnique(normalizedAddedSpirits, allowed, seen);
        }
        if (resonanceEnabled)
        {
            AppendUnique(normalizedAddedSprites, allowed, seen);
        }
        if (allowed.Count == 0)
        {
            return false;
        }

        state = new CharacterSpiritNameChoiceState(
            selectedSpiritId,
            entityType,
            currentName!,
            allowed.ToArray());
        return true;
    }

    public static bool IsValidState(CharacterSpiritNameChoiceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.SpiritId != Guid.Empty
            && state.EntityType is "Spirit" or "Sprite"
            && IsValidName(state.CurrentName)
            && state.AllowedNames is not null
            && TryNormalizeNames(state.AllowedNames, allowEmpty: false, requireUnique: true, out string[] names)
            && names.Length == state.AllowedNames.Count;
    }

    public static bool Matches(
        CharacterSpiritNameChoiceState expected,
        CharacterSpiritNameChoiceState current)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(current);
        return IsValidState(expected)
            && IsValidState(current)
            && expected.SpiritId == current.SpiritId
            && string.Equals(expected.EntityType, current.EntityType, StringComparison.Ordinal)
            && string.Equals(expected.CurrentName, current.CurrentName, StringComparison.Ordinal)
            && expected.AllowedNames.SequenceEqual(current.AllowedNames, StringComparer.Ordinal);
    }

    public static bool CanSet(CharacterSpiritNameChoiceState state, string? requestedName)
    {
        ArgumentNullException.ThrowIfNull(state);
        return IsValidState(state)
            && IsValidName(requestedName)
            && state.AllowedNames.Contains(requestedName!, StringComparer.Ordinal);
    }

    private static bool TryNormalizeNames(
        IReadOnlyList<string> values,
        bool allowEmpty,
        bool requireUnique,
        out string[] normalized)
    {
        var names = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? value in values)
        {
            if (!IsValidName(value))
            {
                normalized = [];
                return false;
            }
            if (seen.Add(value))
            {
                names.Add(value);
            }
            else if (requireUnique)
            {
                normalized = [];
                return false;
            }
        }
        normalized = names.ToArray();
        return allowEmpty || normalized.Length != 0;
    }

    private static bool IsValidName(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaximumNameLength
            && value.IndexOfAny(['\r', '\n', '\0']) < 0;

    private static void AppendUnique(
        IEnumerable<string> source,
        List<string> target,
        HashSet<string> seen)
    {
        foreach (string name in source)
        {
            if (seen.Add(name))
            {
                target.Add(name);
            }
        }
    }
}
