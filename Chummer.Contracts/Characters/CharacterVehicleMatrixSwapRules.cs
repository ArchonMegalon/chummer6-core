using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterVehicleMatrixSwapPhase { Creation, Career }

public enum CharacterVehicleMatrixStat { Attack, Sleaze, DataProcessing, Firewall }

public sealed record CharacterVehicleMatrixSwapIdentity(Guid VehicleId);
public sealed record CharacterVehicleMatrixSwapEconomics(decimal NuyenDelta, int KarmaDelta);
public sealed record CharacterVehicleMatrixSwapProvenance(string AttributeArray, bool CanSwapAttributes);
public sealed record CharacterVehicleMatrixSwapState(
    CharacterVehicleMatrixSwapIdentity Identity,
    string DisplayName,
    CharacterVehicleMatrixSwapPhase Phase,
    string Attack,
    string Sleaze,
    string DataProcessing,
    string Firewall,
    CharacterVehicleMatrixSwapProvenance Provenance,
    CharacterVehicleMatrixSwapEconomics Economics,
    string Revision);

/// <summary>
/// Exact saved-data authority for the Vehicle root selected by the four legacy
/// Attack/Sleaze/Data Processing/Firewall combo handlers in creation or career.
/// Descendant Vehicle-tree IHasMatrixAttributes targets remain outside this bounded
/// authority and fail closed.
/// </summary>
public static class CharacterVehicleMatrixSwapRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterVehicleMatrixSwapIdentity? identity,
        bool created,
        string? displayName,
        string? attack,
        string? sleaze,
        string? dataProcessing,
        string? firewall,
        string? attributeArray,
        bool canSwapAttributes,
        out CharacterVehicleMatrixSwapState state)
    {
        state = Unavailable();
        if (!IsValidIdentity(identity)
            || string.IsNullOrEmpty(displayName)
            || string.IsNullOrEmpty(attack)
            || string.IsNullOrEmpty(sleaze)
            || string.IsNullOrEmpty(dataProcessing)
            || string.IsNullOrEmpty(firewall)
            || string.IsNullOrEmpty(attributeArray)
            || !canSwapAttributes)
        {
            return false;
        }

        CharacterVehicleMatrixSwapPhase phase = created
            ? CharacterVehicleMatrixSwapPhase.Career
            : CharacterVehicleMatrixSwapPhase.Creation;
        var provenance = new CharacterVehicleMatrixSwapProvenance(attributeArray, true);
        var economics = new CharacterVehicleMatrixSwapEconomics(0m, 0);
        state = new CharacterVehicleMatrixSwapState(
            identity!, displayName, phase, attack, sleaze, dataProcessing, firewall,
            provenance, economics,
            CalculateRevision(identity!, phase, attack, sleaze, dataProcessing, firewall, provenance));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterVehicleMatrixSwapState? current,
        string? expectedRevision,
        CharacterVehicleMatrixStat changedAttribute,
        CharacterVehicleMatrixStat targetAttribute)
        => current is not null
            && current.Provenance.CanSwapAttributes
            && !string.IsNullOrEmpty(current.Provenance.AttributeArray)
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 }
            && IsDefined(changedAttribute)
            && IsDefined(targetAttribute)
            && changedAttribute != targetAttribute
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && !string.Equals(Read(current, changedAttribute), Read(current, targetAttribute), StringComparison.Ordinal);

    public static bool RequiresMatrixInitiativeNotification(
        CharacterVehicleMatrixStat changedAttribute,
        CharacterVehicleMatrixStat targetAttribute)
        => IsDefined(changedAttribute) && IsDefined(targetAttribute)
            && (changedAttribute == CharacterVehicleMatrixStat.DataProcessing
                || targetAttribute == CharacterVehicleMatrixStat.DataProcessing);

    public static bool IsValidIdentity(CharacterVehicleMatrixSwapIdentity? identity)
        => identity is { VehicleId: var id } && id != Guid.Empty;

    public static string ElementName(CharacterVehicleMatrixStat attribute) => attribute switch
    {
        CharacterVehicleMatrixStat.Attack => "attack",
        CharacterVehicleMatrixStat.Sleaze => "sleaze",
        CharacterVehicleMatrixStat.DataProcessing => "dataprocessing",
        CharacterVehicleMatrixStat.Firewall => "firewall",
        _ => throw new ArgumentOutOfRangeException(nameof(attribute))
    };

    public static string Read(CharacterVehicleMatrixSwapState state, CharacterVehicleMatrixStat attribute)
        => attribute switch
        {
            CharacterVehicleMatrixStat.Attack => state.Attack,
            CharacterVehicleMatrixStat.Sleaze => state.Sleaze,
            CharacterVehicleMatrixStat.DataProcessing => state.DataProcessing,
            CharacterVehicleMatrixStat.Firewall => state.Firewall,
            _ => throw new ArgumentOutOfRangeException(nameof(attribute))
        };

    private static bool IsDefined(CharacterVehicleMatrixStat value)
        => value is CharacterVehicleMatrixStat.Attack or CharacterVehicleMatrixStat.Sleaze
            or CharacterVehicleMatrixStat.DataProcessing or CharacterVehicleMatrixStat.Firewall;

    private static string CalculateRevision(
        CharacterVehicleMatrixSwapIdentity identity,
        CharacterVehicleMatrixSwapPhase phase,
        string attack,
        string sleaze,
        string dataProcessing,
        string firewall,
        CharacterVehicleMatrixSwapProvenance provenance)
    {
        string payload = string.Join("\0", identity.VehicleId.ToString("D"), phase.ToString(),
            attack, sleaze, dataProcessing, firewall, provenance.AttributeArray,
            provenance.CanSwapAttributes ? "1" : "0");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static CharacterVehicleMatrixSwapState Unavailable() => new(
        new CharacterVehicleMatrixSwapIdentity(Guid.Empty), string.Empty,
        CharacterVehicleMatrixSwapPhase.Creation, string.Empty, string.Empty,
        string.Empty, string.Empty, new CharacterVehicleMatrixSwapProvenance(string.Empty, false),
        new CharacterVehicleMatrixSwapEconomics(0m, 0), string.Empty);
}
