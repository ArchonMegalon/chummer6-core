using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public enum CharacterInitiativeMode { Physical = 0, Astral = 1, MatrixAr = 2, MatrixColdSim = 3, MatrixHotSim = 4 }

public sealed record CharacterInitiativePrerequisite(string Code, bool Satisfied, string Description, Guid? OwnerId = null);

/// <summary>Current source-derived value (including wounds), never a saved display total.</summary>
public sealed record CharacterInitiativeModeOption(CharacterInitiativeMode Mode, int? Value, int? Dice,
    string? UnavailableReason, IReadOnlyList<CharacterInitiativePrerequisite> Prerequisites)
{
    [JsonIgnore] public bool IsAvailable => Value.HasValue && Dice is >= 0 and <= 64
        && UnavailableReason is null && Prerequisites.All(p => p.Satisfied);
}

public sealed record CharacterInitiativeRollPreview(CharacterInitiativeMode Mode, int Base,
    IReadOnlyList<int> Dice, int Score, int? PreviousScore);

public static class CharacterInitiativeModes
{
    public static string Label(CharacterInitiativeMode mode) => mode switch
    {
        CharacterInitiativeMode.Physical => "Physical",
        CharacterInitiativeMode.Astral => "Astral projection / form",
        CharacterInitiativeMode.MatrixAr => "Matrix AR",
        CharacterInitiativeMode.MatrixColdSim => "Matrix cold-sim VR",
        CharacterInitiativeMode.MatrixHotSim => "Matrix hot-sim VR",
        _ => "Unavailable initiative mode"
    };

    // Only old origins lacking the new contract retain the old physical authority.
    // An explicit unknown/unavailable mode never falls back to physical.
    public static IReadOnlyList<CharacterInitiativeModeOption> From(CharacterSheetDerivedSection sheet)
        => sheet.InitiativeModes ?? (sheet.PhysicalInitiative is { } physical
            ? [new(CharacterInitiativeMode.Physical, physical.Value, physical.Dice, null, [])] : []);

    public static CharacterInitiativeModeOption? Find(CharacterSheetDerivedSection sheet, CharacterInitiativeMode mode)
        => From(sheet).SingleOrDefault(m => m.Mode == mode && m.IsAvailable);

    public static bool TryParse(string? value, out CharacterInitiativeMode mode)
        => Enum.TryParse(value, false, out mode) && Enum.IsDefined(mode) && mode.ToString() == value;

    public static CharacterInitiativeRollPreview Preview(CharacterInitiativeModeOption option,
        IReadOnlyList<int> dice, int? previousScore)
    {
        if (!option.IsAvailable || dice.Count != option.Dice || dice.Any(d => d is < 1 or > 6))
            throw new ArgumentException("initiative-dice-mismatch");
        return new(option.Mode, option.Value!.Value, dice.ToArray(),
            Math.Max(0, checked(option.Value.Value + dice.Sum())), previousScore);
    }

    public static int WithWounds(CharacterInitiativeMode mode, int uninjured, int wound)
        => mode is CharacterInitiativeMode.Physical or CharacterInitiativeMode.MatrixAr
            ? Math.Max(0, checked(uninjured + wound)) : checked(uninjured + wound);
}
