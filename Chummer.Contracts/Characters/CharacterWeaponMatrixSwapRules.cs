using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterWeaponMatrixSwapPhase { Creation, Career }

public enum CharacterWeaponMatrixStat { Attack, Sleaze, DataProcessing, Firewall }

public sealed record CharacterWeaponMatrixSwapIdentity(Guid WeaponId);
public sealed record CharacterWeaponMatrixSwapEconomics(decimal NuyenDelta, int KarmaDelta);
public sealed record CharacterWeaponMatrixSwapProvenance(
    string AttributeArray,
    bool CanSwapAttributes,
    string LegacySurface);
public sealed record CharacterWeaponMatrixSwapState(
    CharacterWeaponMatrixSwapIdentity Identity,
    string DisplayName,
    CharacterWeaponMatrixSwapPhase Phase,
    string Attack,
    string Sleaze,
    string DataProcessing,
    string Firewall,
    CharacterWeaponMatrixSwapProvenance Provenance,
    CharacterWeaponMatrixSwapEconomics Economics,
    string Revision);

/// <summary>
/// Exact saved-data authority for the four Matrix attribute combo handlers on a
/// direct top-level Weapon selected in CharacterCareer.treWeapons. Chummer5 does
/// not expose these controls on CharacterCreate. Weapon descendants, accessories,
/// child Gear and Vehicle-owned Weapons remain outside this authority and fail closed.
/// </summary>
public static class CharacterWeaponMatrixSwapRules
{
    public const int RevisionHexLength = CharacterMatrixPermutationAuthority.RevisionHexLength;
    public const string LegacySurface = "CharacterCareer.treWeapons";

    public static bool TryCreateState(
        CharacterWeaponMatrixSwapIdentity? identity,
        bool created,
        string? displayName,
        string? attack,
        string? sleaze,
        string? dataProcessing,
        string? firewall,
        string? attributeArray,
        bool canSwapAttributes,
        out CharacterWeaponMatrixSwapState state)
    {
        state = Unavailable();
        if (!created
            || !IsValidIdentity(identity)
            || string.IsNullOrWhiteSpace(displayName)
            || !CharacterMatrixPermutationAuthority.HasExactRawState(
                attack, sleaze, dataProcessing, firewall, attributeArray, canSwapAttributes))
        {
            return false;
        }

        var provenance = new CharacterWeaponMatrixSwapProvenance(
            attributeArray!,
            CanSwapAttributes: true,
            LegacySurface);
        state = new CharacterWeaponMatrixSwapState(
            identity!,
            displayName,
            CharacterWeaponMatrixSwapPhase.Career,
            attack!,
            sleaze!,
            dataProcessing!,
            firewall!,
            provenance,
            new CharacterWeaponMatrixSwapEconomics(0m, 0),
            CalculateRevision(
                identity!, attack!, sleaze!, dataProcessing!, firewall!, provenance));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterWeaponMatrixSwapState? current,
        string? expectedRevision,
        CharacterWeaponMatrixStat changedAttribute,
        CharacterWeaponMatrixStat targetAttribute)
        => current is not null
            && current.Phase == CharacterWeaponMatrixSwapPhase.Career
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 }
            && current.Provenance.CanSwapAttributes
            && string.Equals(current.Provenance.LegacySurface, LegacySurface, StringComparison.Ordinal)
            && IsDefined(changedAttribute)
            && IsDefined(targetAttribute)
            && changedAttribute != targetAttribute
            && CharacterMatrixPermutationAuthority.TryValidatePermutation(
                current.Revision,
                expectedRevision,
                Read(current, changedAttribute),
                Read(current, targetAttribute),
                current.Provenance.AttributeArray,
                current.Provenance.CanSwapAttributes);

    public static bool RequiresMatrixInitiativeNotification(
        CharacterWeaponMatrixStat changedAttribute,
        CharacterWeaponMatrixStat targetAttribute)
        => IsDefined(changedAttribute)
            && IsDefined(targetAttribute)
            && (changedAttribute == CharacterWeaponMatrixStat.DataProcessing
                || targetAttribute == CharacterWeaponMatrixStat.DataProcessing);

    public static bool IsValidIdentity(CharacterWeaponMatrixSwapIdentity? identity)
        => identity is { WeaponId: var id } && id != Guid.Empty;

    public static string ElementName(CharacterWeaponMatrixStat attribute) => attribute switch
    {
        CharacterWeaponMatrixStat.Attack => "attack",
        CharacterWeaponMatrixStat.Sleaze => "sleaze",
        CharacterWeaponMatrixStat.DataProcessing => "dataprocessing",
        CharacterWeaponMatrixStat.Firewall => "firewall",
        _ => throw new ArgumentOutOfRangeException(nameof(attribute))
    };

    public static string Read(
        CharacterWeaponMatrixSwapState state,
        CharacterWeaponMatrixStat attribute) => attribute switch
        {
            CharacterWeaponMatrixStat.Attack => state.Attack,
            CharacterWeaponMatrixStat.Sleaze => state.Sleaze,
            CharacterWeaponMatrixStat.DataProcessing => state.DataProcessing,
            CharacterWeaponMatrixStat.Firewall => state.Firewall,
            _ => throw new ArgumentOutOfRangeException(nameof(attribute))
        };

    private static bool IsDefined(CharacterWeaponMatrixStat value)
        => value is CharacterWeaponMatrixStat.Attack
            or CharacterWeaponMatrixStat.Sleaze
            or CharacterWeaponMatrixStat.DataProcessing
            or CharacterWeaponMatrixStat.Firewall;

    private static string CalculateRevision(
        CharacterWeaponMatrixSwapIdentity identity,
        string attack,
        string sleaze,
        string dataProcessing,
        string firewall,
        CharacterWeaponMatrixSwapProvenance provenance)
    {
        string payload = string.Join(
            "\0",
            "weapon-matrix-swap/v1",
            provenance.LegacySurface,
            identity.WeaponId.ToString("D"),
            CharacterWeaponMatrixSwapPhase.Career,
            attack,
            sleaze,
            dataProcessing,
            firewall,
            provenance.AttributeArray,
            provenance.CanSwapAttributes ? "1" : "0");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static CharacterWeaponMatrixSwapState Unavailable() => new(
        new CharacterWeaponMatrixSwapIdentity(Guid.Empty),
        string.Empty,
        CharacterWeaponMatrixSwapPhase.Creation,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        new CharacterWeaponMatrixSwapProvenance(string.Empty, false, string.Empty),
        new CharacterWeaponMatrixSwapEconomics(0m, 0),
        string.Empty);
}
