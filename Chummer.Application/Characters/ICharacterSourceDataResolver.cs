namespace Chummer.Application.Characters;

/// <summary>
/// Resolves source-only values using the exact content profile selected by a saved character.
/// A null context or a false result means the caller must fail closed rather than guess.
/// </summary>
public interface ICharacterSourceDataResolver
{
    ICharacterSourceDataContext? TryCreateContext(string characterXml);
}

public interface ICharacterSourceDataContext
{
    /// <summary>
    /// Resolves whether the exact source profile saved by the runner enables a sourcebook.
    /// False means the profile could not prove the answer and callers must fail closed.
    /// </summary>
    bool TryIsBookEnabled(string sourceCode, out bool enabled)
    {
        enabled = false;
        return false;
    }

    bool TryResolveMaxNuyenDecimals(out int decimalPlaces)
    {
        decimalPlaces = 0;
        return false;
    }

    bool TryResolveCyberwareGradeDeviceRating(
        string gradeName,
        string improvementSource,
        out int deviceRating);

    bool TryResolveVehicleModBonuses(
        string sourceId,
        string name,
        out CharacterVehicleModSourceBonuses bonuses);
}

public sealed record CharacterVehicleModSourceBonuses(
    string BodyExpression,
    string DeviceRatingExpression,
    string MatrixConditionExpression,
    string WirelessBodyExpression,
    string WirelessDeviceRatingExpression,
    string WirelessMatrixConditionExpression)
{
    public static CharacterVehicleModSourceBonuses Empty { get; } = new(
        BodyExpression: string.Empty,
        DeviceRatingExpression: string.Empty,
        MatrixConditionExpression: string.Empty,
        WirelessBodyExpression: string.Empty,
        WirelessDeviceRatingExpression: string.Empty,
        WirelessMatrixConditionExpression: string.Empty);
}
