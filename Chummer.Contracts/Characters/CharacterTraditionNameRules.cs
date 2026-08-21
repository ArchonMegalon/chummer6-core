namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact identity and input boundary of the Chummer5 custom-tradition name textbox.
/// </summary>
public static class CharacterTraditionNameRules
{
    public static Guid CustomMagicalTraditionSourceId { get; }
        = new("616ba093-306c-45fc-8f41-0b98c8cccb46");

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
