namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact input boundary of the single-line Chummer5 group-name textbox.
/// </summary>
public static class CharacterGroupNameRules
{
    public const int MaximumLength = 32_767;

    public static bool TryValidate(string? value, out string validated)
    {
        validated = string.Empty;
        if (value is null
            || value.Length > MaximumLength
            || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            return false;
        }

        validated = value;
        return true;
    }
}
