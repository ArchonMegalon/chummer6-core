namespace Chummer.Contracts.Characters;

public enum CharacterCyberwareMatrixSwapPhase { Creation, Career }

public enum CharacterCyberwareMatrixStat { Attack, Sleaze, DataProcessing, Firewall }

public sealed record CharacterCyberwareMatrixSwapIdentity(Guid CyberwareId);
public sealed record CharacterCyberwareMatrixSwapEconomics(decimal NuyenDelta, int KarmaDelta);
public sealed record CharacterCyberwareMatrixSwapProvenance(string AttributeArray, bool CanSwapAttributes);
public sealed record CharacterCyberwareMatrixSwapState(
    CharacterCyberwareMatrixSwapIdentity Identity,
    string DisplayName,
    CharacterCyberwareMatrixSwapPhase Phase,
    string Attack,
    string Sleaze,
    string DataProcessing,
    string Firewall,
    CharacterCyberwareMatrixSwapProvenance Provenance,
    CharacterCyberwareMatrixSwapEconomics Economics,
    string Revision);

/// <summary>
/// Exact saved-data authority for a top-level Cyberware node selected by the four
/// legacy Attack/Sleaze/Data Processing/Firewall combo handlers. Descendant
/// Cyberware and child Gear remain outside this bounded authority and fail closed.
/// </summary>
public static class CharacterCyberwareMatrixSwapRules
{
    public const int RevisionHexLength = CharacterMatrixPermutationAuthority.RevisionHexLength;

    public static bool TryCreateState(
        CharacterCyberwareMatrixSwapIdentity? identity,
        bool created,
        string? displayName,
        string? attack,
        string? sleaze,
        string? dataProcessing,
        string? firewall,
        string? attributeArray,
        bool canSwapAttributes,
        out CharacterCyberwareMatrixSwapState state)
    {
        state = Unavailable();
        if (!IsValidIdentity(identity)
            || string.IsNullOrEmpty(displayName)
            || !CharacterMatrixPermutationAuthority.HasExactRawState(
                attack, sleaze, dataProcessing, firewall, attributeArray, canSwapAttributes))
        {
            return false;
        }

        CharacterCyberwareMatrixSwapPhase phase = created
            ? CharacterCyberwareMatrixSwapPhase.Career
            : CharacterCyberwareMatrixSwapPhase.Creation;
        var provenance = new CharacterCyberwareMatrixSwapProvenance(attributeArray!, true);
        state = new CharacterCyberwareMatrixSwapState(
            identity!, displayName, phase, attack!, sleaze!, dataProcessing!, firewall!,
            provenance, new CharacterCyberwareMatrixSwapEconomics(0m, 0),
            CharacterMatrixPermutationAuthority.CalculateRevision(
                identity!.CyberwareId, phase.ToString(), attack!, sleaze!, dataProcessing!, firewall!,
                provenance.AttributeArray, provenance.CanSwapAttributes));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterCyberwareMatrixSwapState? current,
        string? expectedRevision,
        CharacterCyberwareMatrixStat changedAttribute,
        CharacterCyberwareMatrixStat targetAttribute)
        => current is not null
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 }
            && IsDefined(changedAttribute)
            && IsDefined(targetAttribute)
            && changedAttribute != targetAttribute
            && CharacterMatrixPermutationAuthority.TryValidatePermutation(
                current.Revision, expectedRevision, Read(current, changedAttribute), Read(current, targetAttribute),
                current.Provenance.AttributeArray, current.Provenance.CanSwapAttributes);

    public static bool RequiresMatrixInitiativeNotification(
        CharacterCyberwareMatrixStat changedAttribute,
        CharacterCyberwareMatrixStat targetAttribute)
        => IsDefined(changedAttribute) && IsDefined(targetAttribute)
            && (changedAttribute == CharacterCyberwareMatrixStat.DataProcessing
                || targetAttribute == CharacterCyberwareMatrixStat.DataProcessing);

    public static bool IsValidIdentity(CharacterCyberwareMatrixSwapIdentity? identity)
        => identity is { CyberwareId: var id } && id != Guid.Empty;

    public static string ElementName(CharacterCyberwareMatrixStat attribute) => attribute switch
    {
        CharacterCyberwareMatrixStat.Attack => "attack",
        CharacterCyberwareMatrixStat.Sleaze => "sleaze",
        CharacterCyberwareMatrixStat.DataProcessing => "dataprocessing",
        CharacterCyberwareMatrixStat.Firewall => "firewall",
        _ => throw new ArgumentOutOfRangeException(nameof(attribute))
    };

    public static string Read(
        CharacterCyberwareMatrixSwapState state,
        CharacterCyberwareMatrixStat attribute) => attribute switch
        {
            CharacterCyberwareMatrixStat.Attack => state.Attack,
            CharacterCyberwareMatrixStat.Sleaze => state.Sleaze,
            CharacterCyberwareMatrixStat.DataProcessing => state.DataProcessing,
            CharacterCyberwareMatrixStat.Firewall => state.Firewall,
            _ => throw new ArgumentOutOfRangeException(nameof(attribute))
        };

    private static bool IsDefined(CharacterCyberwareMatrixStat value)
        => value is CharacterCyberwareMatrixStat.Attack or CharacterCyberwareMatrixStat.Sleaze
            or CharacterCyberwareMatrixStat.DataProcessing or CharacterCyberwareMatrixStat.Firewall;

    private static CharacterCyberwareMatrixSwapState Unavailable() => new(
        new CharacterCyberwareMatrixSwapIdentity(Guid.Empty), string.Empty,
        CharacterCyberwareMatrixSwapPhase.Creation, string.Empty, string.Empty, string.Empty, string.Empty,
        new CharacterCyberwareMatrixSwapProvenance(string.Empty, false),
        new CharacterCyberwareMatrixSwapEconomics(0m, 0), string.Empty);
}
