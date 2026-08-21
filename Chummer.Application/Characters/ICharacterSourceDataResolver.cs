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

    /// <summary>
    /// Resolves the exact effective source entry, allowed grades, and Essence
    /// rounding settings needed by the bounded Cyberware Career commerce lane.
    /// False means custom/source/profile semantics were not proven and callers
    /// must refuse the mutation.
    /// </summary>
    bool TryResolveCyberwareCommerceSource(
        string sourceId,
        string name,
        string improvementSource,
        out CharacterCyberwareCommerceSource source)
    {
        source = CharacterCyberwareCommerceSource.Unavailable;
        return false;
    }

    bool TryResolveVehicleModBonuses(
        string sourceId,
        string name,
        out CharacterVehicleModSourceBonuses bonuses);
}

public sealed record CharacterCyberwareCommerceGradeSource(
    string Id,
    string Name,
    decimal CostMultiplier,
    decimal EssenceMultiplier,
    string Source,
    bool SpecialSemantics);

public sealed record CharacterCyberwareCommerceSource(
    string SourceId,
    string Name,
    string Source,
    string MinimumRatingExpression,
    string MaximumRatingExpression,
    string CostExpression,
    string EssenceExpression,
    string CapacityExpression,
    string ForcedGrade,
    IReadOnlyList<string> BannedGrades,
    IReadOnlyList<CharacterCyberwareCommerceGradeSource> Grades,
    int EssenceDecimals,
    bool DoNotRoundEssenceInternally,
    string EssenceModifierPostExpression,
    bool SourceEntryUsesGeneratedOrImprovementSemantics)
{
    public static CharacterCyberwareCommerceSource Unavailable { get; } = new(
        SourceId: string.Empty,
        Name: string.Empty,
        Source: string.Empty,
        MinimumRatingExpression: string.Empty,
        MaximumRatingExpression: string.Empty,
        CostExpression: string.Empty,
        EssenceExpression: string.Empty,
        CapacityExpression: string.Empty,
        ForcedGrade: string.Empty,
        BannedGrades: Array.Empty<string>(),
        Grades: Array.Empty<CharacterCyberwareCommerceGradeSource>(),
        EssenceDecimals: 0,
        DoNotRoundEssenceInternally: false,
        EssenceModifierPostExpression: string.Empty,
        SourceEntryUsesGeneratedOrImprovementSemantics: true);
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
