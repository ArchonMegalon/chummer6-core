using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterGearWirelessState(
    CharacterGearEquipmentIdentity Identity,
    string DisplayPath,
    CharacterGearEquipmentPhase Phase,
    bool WirelessOn,
    bool CanChangeWireless,
    CharacterGearEquipmentEconomics Economics,
    string Revision);

/// <summary>
/// Authority for CharacterCareer.chkGearWireless. Chummer5 exposes the
/// checkbox only in Career and writes IHasWirelessBonus.WirelessOn directly.
/// CharacterCreate has no matching Gear Wireless control, so creation state is
/// projected read-only rather than promoted into a wider parity claim.
/// </summary>
public static class CharacterGearWirelessRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterGearEquipmentIdentity? identity,
        bool created,
        string? displayPath,
        bool wirelessOn,
        out CharacterGearWirelessState state)
    {
        state = Unavailable();
        if (!CharacterGearEquipmentRules.IsValidIdentity(identity) || displayPath is null)
        {
            return false;
        }

        CharacterGearEquipmentPhase phase = created
            ? CharacterGearEquipmentPhase.Career
            : CharacterGearEquipmentPhase.Creation;
        var economics = new CharacterGearEquipmentEconomics(0m, 0);
        state = new CharacterGearWirelessState(
            identity!,
            displayPath,
            phase,
            wirelessOn,
            CanChangeWireless: created,
            economics,
            CalculateRevision(identity!, phase, wirelessOn, created));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterGearWirelessState? current,
        string? expectedRevision,
        bool wirelessOn)
        => current is
            {
                Phase: CharacterGearEquipmentPhase.Career,
                CanChangeWireless: true,
                Economics: { NuyenDelta: 0m, KarmaDelta: 0 }
            }
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.WirelessOn != wirelessOn;

    private static string CalculateRevision(
        CharacterGearEquipmentIdentity identity,
        CharacterGearEquipmentPhase phase,
        bool wirelessOn,
        bool canChangeWireless)
    {
        var payload = new StringBuilder();
        foreach (Guid id in identity.GearPath)
        {
            payload.Append(id.ToString("D")).Append('\0');
        }
        payload.Append((int)phase).Append('\0')
            .Append(wirelessOn ? '1' : '0').Append('\0')
            .Append(canChangeWireless ? '1' : '0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterGearWirelessState Unavailable()
        => new(
            new CharacterGearEquipmentIdentity(Array.Empty<Guid>()),
            string.Empty,
            CharacterGearEquipmentPhase.Creation,
            false,
            false,
            new CharacterGearEquipmentEconomics(0m, 0),
            string.Empty);
}
