namespace Chummer.Contracts.Characters;

public enum CharacterGearAttackSwapPhase
{
    Creation,
    Career
}

public enum CharacterGearAttackSwapTarget
{
    Sleaze,
    DataProcessing,
    Firewall
}

public sealed record CharacterGearAttackSwapIdentity(IReadOnlyList<Guid> GearPath);

public sealed record CharacterGearAttackSwapEconomics(decimal NuyenDelta, int KarmaDelta);

public sealed record CharacterGearAttackSwapState(
    CharacterGearAttackSwapIdentity Identity,
    string DisplayPath,
    CharacterGearAttackSwapPhase Phase,
    string Attack,
    string Sleaze,
    string DataProcessing,
    string Firewall,
    CharacterGearAttackSwapEconomics Economics,
    string Revision);

/// <summary>
/// Exact authority for CharacterCreate/CharacterCareer.cboGearAttack. The
/// legacy combo swaps the saved raw Attack string with one other base Matrix
/// attribute. Matrix bonuses are display-only and no transaction is created.
/// </summary>
public static class CharacterGearAttackSwapRules
{
    public const int RevisionHexLength = CharacterGearMatrixSwapRules.RevisionHexLength;

    public static bool TryCreateState(
        CharacterGearAttackSwapIdentity? identity,
        bool created,
        bool canSwapAttributes,
        string? displayPath,
        string? attack,
        string? sleaze,
        string? dataProcessing,
        string? firewall,
        out CharacterGearAttackSwapState state)
    {
        state = Unavailable();
        if (!CharacterGearMatrixSwapRules.TryCreateState(
                identity is null ? null : new CharacterGearMatrixSwapIdentity(identity.GearPath),
                created, canSwapAttributes, displayPath, attack, sleaze, dataProcessing, firewall,
                out CharacterGearMatrixSwapState shared)) return false;
        state = new(identity!, shared.DisplayPath,
            shared.Phase == CharacterGearMatrixSwapPhase.Career ? CharacterGearAttackSwapPhase.Career : CharacterGearAttackSwapPhase.Creation,
            shared.Attack, shared.Sleaze, shared.DataProcessing, shared.Firewall,
            new CharacterGearAttackSwapEconomics(shared.Economics.NuyenDelta, shared.Economics.KarmaDelta), shared.Revision);
        return true;
    }

    public static bool TryValidateMutation(
        CharacterGearAttackSwapState? current,
        string? expectedRevision,
        CharacterGearAttackSwapTarget target)
        => current is not null && CharacterGearMatrixSwapRules.TryValidateMutation(
            ToShared(current), expectedRevision, CharacterGearMatrixStat.Attack, ToShared(target));

    public static bool IsValidIdentity(CharacterGearAttackSwapIdentity? identity)
        => identity is not null && CharacterGearMatrixSwapRules.IsValidIdentity(
            new CharacterGearMatrixSwapIdentity(identity.GearPath));

    public static bool IdentityEquals(
        CharacterGearAttackSwapIdentity? left,
        CharacterGearAttackSwapIdentity? right)
        => left?.GearPath is not null
            && right?.GearPath is not null
            && left.GearPath.SequenceEqual(right.GearPath);

    public static string TargetElement(CharacterGearAttackSwapTarget target)
        => CharacterGearMatrixSwapRules.ElementName(ToShared(target));

    public static string ReadTarget(
        CharacterGearAttackSwapState state,
        CharacterGearAttackSwapTarget target)
        => CharacterGearMatrixSwapRules.Read(ToShared(state), ToShared(target));

    private static CharacterGearMatrixStat ToShared(CharacterGearAttackSwapTarget target) => target switch
    {
        CharacterGearAttackSwapTarget.Sleaze => CharacterGearMatrixStat.Sleaze,
        CharacterGearAttackSwapTarget.DataProcessing => CharacterGearMatrixStat.DataProcessing,
        CharacterGearAttackSwapTarget.Firewall => CharacterGearMatrixStat.Firewall,
        _ => (CharacterGearMatrixStat)(-1)
    };

    private static CharacterGearMatrixSwapState ToShared(CharacterGearAttackSwapState state) => new(
        new CharacterGearMatrixSwapIdentity(state.Identity.GearPath), state.DisplayPath,
        state.Phase == CharacterGearAttackSwapPhase.Career ? CharacterGearMatrixSwapPhase.Career : CharacterGearMatrixSwapPhase.Creation,
        state.Attack, state.Sleaze, state.DataProcessing, state.Firewall,
        new CharacterGearMatrixSwapEconomics(state.Economics.NuyenDelta, state.Economics.KarmaDelta), state.Revision);

    private static CharacterGearAttackSwapState Unavailable()
        => new(
            new CharacterGearAttackSwapIdentity(Array.Empty<Guid>()),
            string.Empty,
            CharacterGearAttackSwapPhase.Creation,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new CharacterGearAttackSwapEconomics(0m, 0),
            string.Empty);
}
