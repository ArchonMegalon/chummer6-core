using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterWeaponFireMode
{
    SingleShot,
    ShortBurst,
    LongBurst,
    FullBurst,
    SuppressiveFire
}

public sealed record CharacterWeaponFireIdentity(
    Guid WeaponId,
    int AmmoSlot,
    Guid AmmoGearId);

public sealed record CharacterWeaponFireAccessorySource(
    bool Equipped,
    string FireMode,
    string FireModeReplacement,
    int SingleShot,
    int ShortBurst,
    int LongBurst,
    int FullBurst,
    int SuppressiveFire);

public sealed record CharacterWeaponFireSource(
    string RangeType,
    string Ammo,
    string BaseModes,
    bool AllowSingleShot,
    bool AllowShortBurst,
    bool AllowLongBurst,
    bool AllowFullBurst,
    bool AllowSuppressiveFire,
    int SingleShot,
    int ShortBurst,
    int LongBurst,
    int FullBurst,
    int SuppressiveFire,
    IReadOnlyList<CharacterWeaponFireAccessorySource> Accessories);

public sealed record CharacterWeaponFireModeState(
    CharacterWeaponFireMode Mode,
    int Rounds);

public sealed record CharacterWeaponFireState(
    CharacterWeaponFireIdentity Identity,
    string DisplayName,
    int AmmoRemaining,
    decimal? AmmoGearQuantity,
    IReadOnlyList<CharacterWeaponFireModeState> Modes,
    CharacterWeaponFireMode? DefaultMode,
    string Revision);

public sealed record CharacterWeaponFirePlan(
    CharacterWeaponFireMode Mode,
    int RoundsConsumed,
    int NewAmmoRemaining,
    decimal? NewAmmoGearQuantity,
    bool DeleteAmmoGear,
    bool RequiresPartialConfirmation);

/// <summary>
/// Deterministic authority for the six Career weapon-fire controls in Chummer5.
/// The source contains only state persisted in a .chum5 weapon and its direct
/// accessories; callers must fail closed when unsaved source or bonus semantics
/// could change a firing mode.
/// </summary>
public static class CharacterWeaponFireRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterWeaponFireIdentity? identity,
        bool created,
        string? displayName,
        int ammoRemaining,
        decimal? ammoGearQuantity,
        CharacterWeaponFireSource? source,
        bool hasUnsupportedModeSemantics,
        out CharacterWeaponFireState state)
    {
        state = Unavailable();
        if (!created
            || !IsValidIdentity(identity)
            || string.IsNullOrWhiteSpace(displayName)
            || ammoRemaining < 0
            || source is null
            || hasUnsupportedModeSemantics
            || !TryValidateSource(source)
            || identity!.AmmoGearId != Guid.Empty
                && (ammoGearQuantity is null || ammoGearQuantity < ammoRemaining))
        {
            return false;
        }

        HashSet<string> effectiveModes = ParseModes(source.BaseModes);
        var addedModes = new HashSet<string>(StringComparer.Ordinal);
        int single = source.SingleShot;
        int shortBurst = source.ShortBurst;
        int longBurst = source.LongBurst;
        int fullBurst = source.FullBurst;
        int suppressive = source.SuppressiveFire;
        foreach (CharacterWeaponFireAccessorySource accessory in source.Accessories)
        {
            if (!accessory.Equipped)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(accessory.FireMode))
            {
                addedModes.UnionWith(ParseModes(accessory.FireMode));
            }
            if (!string.IsNullOrEmpty(accessory.FireModeReplacement))
            {
                effectiveModes = ParseModes(accessory.FireModeReplacement);
            }
            single = Math.Max(single, accessory.SingleShot);
            shortBurst = Math.Max(shortBurst, accessory.ShortBurst);
            longBurst = Math.Max(longBurst, accessory.LongBurst);
            fullBurst = Math.Max(fullBurst, accessory.FullBurst);
            suppressive = Math.Max(suppressive, accessory.SuppressiveFire);
        }
        effectiveModes.UnionWith(addedModes);

        var modes = new List<CharacterWeaponFireModeState>(5);
        bool ammoBearingMelee = string.Equals(source.RangeType, "Melee", StringComparison.Ordinal)
                                && !string.IsNullOrEmpty(source.Ammo)
                                && !string.Equals(source.Ammo, "0", StringComparison.Ordinal);
        if (ammoBearingMelee
            || source.AllowSingleShot && effectiveModes.Overlaps(["SS", "SA"]))
        {
            modes.Add(new(CharacterWeaponFireMode.SingleShot, single));
        }
        if (source.AllowShortBurst && effectiveModes.Overlaps(["BF", "SA", "FA"]))
        {
            modes.Add(new(CharacterWeaponFireMode.ShortBurst, shortBurst));
        }
        if (source.AllowLongBurst && effectiveModes.Overlaps(["BF", "FA"]))
        {
            modes.Add(new(CharacterWeaponFireMode.LongBurst, longBurst));
        }
        if (source.AllowFullBurst && effectiveModes.Contains("FA"))
        {
            modes.Add(new(CharacterWeaponFireMode.FullBurst, fullBurst));
        }
        if (source.AllowSuppressiveFire && effectiveModes.Contains("FA"))
        {
            modes.Add(new(CharacterWeaponFireMode.SuppressiveFire, suppressive));
        }
        if (modes.Count == 0)
        {
            return false;
        }

        CharacterWeaponFireMode? defaultMode = modes.Any(value => value.Mode == CharacterWeaponFireMode.SingleShot)
            ? CharacterWeaponFireMode.SingleShot
            : modes.Any(value => value.Mode == CharacterWeaponFireMode.ShortBurst)
                ? CharacterWeaponFireMode.ShortBurst
                : modes.Any(value => value.Mode == CharacterWeaponFireMode.LongBurst)
                    ? CharacterWeaponFireMode.LongBurst
                    : null;
        state = new CharacterWeaponFireState(
            identity,
            displayName,
            ammoRemaining,
            ammoGearQuantity,
            modes,
            defaultMode,
            CalculateRevision(identity, ammoRemaining, ammoGearQuantity, modes, defaultMode));
        return true;
    }

    public static bool TryCreatePlan(
        CharacterWeaponFireState? current,
        string? expectedRevision,
        CharacterWeaponFireMode mode,
        out CharacterWeaponFirePlan plan)
    {
        plan = new CharacterWeaponFirePlan(mode, 0, 0, null, false, false);
        if (current is null
            || expectedRevision is not { Length: RevisionHexLength }
            || !string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            || current.AmmoRemaining <= 0)
        {
            return false;
        }
        CharacterWeaponFireModeState[] selected = current.Modes
            .Where(value => value.Mode == mode)
            .Take(2)
            .ToArray();
        if (selected.Length != 1)
        {
            return false;
        }

        int requested = selected[0].Rounds;
        bool partial = current.AmmoRemaining < requested
                       && mode is CharacterWeaponFireMode.ShortBurst
                           or CharacterWeaponFireMode.LongBurst;
        if (current.AmmoRemaining < requested && !partial)
        {
            return false;
        }
        int consumed = partial ? current.AmmoRemaining : requested;
        int newAmmo = checked(current.AmmoRemaining - consumed);
        decimal? newQuantity = current.AmmoGearQuantity is decimal quantity
            ? quantity - consumed
            : null;
        bool deleteAmmoGear = current.Identity.AmmoGearId != Guid.Empty
                              && newQuantity is <= 0m;
        plan = new CharacterWeaponFirePlan(
            mode,
            consumed,
            newAmmo,
            newQuantity,
            deleteAmmoGear,
            partial);
        return true;
    }

    public static bool TryValidateMutation(
        CharacterWeaponFireState? current,
        string? expectedRevision,
        CharacterWeaponFireMode mode,
        bool confirmedPartial,
        out CharacterWeaponFirePlan plan)
        => TryCreatePlan(current, expectedRevision, mode, out plan)
           && (!plan.RequiresPartialConfirmation || confirmedPartial);

    public static bool IsValidIdentity(CharacterWeaponFireIdentity? identity)
        => identity is { WeaponId: var weaponId, AmmoSlot: > 0 }
           && weaponId != Guid.Empty;

    private static bool TryValidateSource(CharacterWeaponFireSource source)
    {
        if (string.IsNullOrWhiteSpace(source.RangeType)
            || source.SingleShot <= 0
            || source.ShortBurst <= 0
            || source.LongBurst <= 0
            || source.FullBurst <= 0
            || source.SuppressiveFire <= 0
            || source.Accessories is null)
        {
            return false;
        }
        try
        {
            _ = ParseModes(source.BaseModes);
            foreach (CharacterWeaponFireAccessorySource accessory in source.Accessories)
            {
                if (accessory.SingleShot < 0
                    || accessory.ShortBurst < 0
                    || accessory.LongBurst < 0
                    || accessory.FullBurst < 0
                    || accessory.SuppressiveFire < 0)
                {
                    return false;
                }
                if (!string.IsNullOrEmpty(accessory.FireMode))
                {
                    _ = ParseModes(accessory.FireMode);
                }
                if (!string.IsNullOrEmpty(accessory.FireModeReplacement))
                {
                    _ = ParseModes(accessory.FireModeReplacement);
                }
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        return true;
    }

    private static HashSet<string> ParseModes(string? value)
    {
        var modes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string mode in (value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (mode is not ("SS" or "SA" or "BF" or "FA" or "Special"))
            {
                throw new InvalidOperationException("Unknown Chummer5 weapon firing mode.");
            }
            modes.Add(mode);
        }
        return modes;
    }

    private static string CalculateRevision(
        CharacterWeaponFireIdentity identity,
        int ammoRemaining,
        decimal? ammoGearQuantity,
        IReadOnlyList<CharacterWeaponFireModeState> modes,
        CharacterWeaponFireMode? defaultMode)
    {
        string payload = string.Join("\0",
            "career-weapon-fire/v1",
            identity.WeaponId.ToString("D"),
            identity.AmmoSlot.ToString(CultureInfo.InvariantCulture),
            identity.AmmoGearId.ToString("D"),
            ammoRemaining.ToString(CultureInfo.InvariantCulture),
            ammoGearQuantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            defaultMode?.ToString() ?? string.Empty,
            string.Join("|", modes.Select(value =>
                $"{value.Mode}:{value.Rounds.ToString(CultureInfo.InvariantCulture)}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static CharacterWeaponFireState Unavailable()
        => new(
            new CharacterWeaponFireIdentity(Guid.Empty, 0, Guid.Empty),
            string.Empty,
            0,
            null,
            Array.Empty<CharacterWeaponFireModeState>(),
            null,
            string.Empty);
}
