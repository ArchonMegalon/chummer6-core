namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact saved state behind Chummer5's CharacterCreate/CharacterCareer chkJoinGroup controls.
/// </summary>
public sealed record CharacterGroupMembershipState(
    bool GroupMember,
    bool Created,
    bool MagicEnabled,
    bool ResonanceEnabled,
    int AvailableKarma,
    bool KarmaCostsExact,
    int JoinKarmaCost,
    int LeaveKarmaCost,
    int TransitionKarmaCost,
    bool RequiresConfirmation,
    bool CanChange);

public static class CharacterGroupMembershipRules
{
    public static bool TryProject(
        bool groupMember,
        bool created,
        bool magicEnabled,
        bool resonanceEnabled,
        int availableKarma,
        int? joinKarmaCost,
        int? leaveKarmaCost,
        out CharacterGroupMembershipState? state)
    {
        state = null;
        if (joinKarmaCost is < 0 || leaveKarmaCost is < 0)
        {
            return false;
        }

        bool costsExact = !created
            || !magicEnabled
            || (joinKarmaCost.HasValue && leaveKarmaCost.HasValue);
        int transitionCost = groupMember
            ? leaveKarmaCost.GetValueOrDefault()
            : joinKarmaCost.GetValueOrDefault();
        bool requiresConfirmation = created && magicEnabled;
        bool canChange = costsExact
            && (!requiresConfirmation || transitionCost <= availableKarma);

        state = new CharacterGroupMembershipState(
            groupMember,
            created,
            magicEnabled,
            resonanceEnabled,
            availableKarma,
            costsExact,
            joinKarmaCost.GetValueOrDefault(),
            leaveKarmaCost.GetValueOrDefault(),
            transitionCost,
            requiresConfirmation,
            canChange);
        return true;
    }

    public static bool CanSet(CharacterGroupMembershipState state, bool requestedMembership)
    {
        ArgumentNullException.ThrowIfNull(state);
        return requestedMembership == state.GroupMember || state.CanChange;
    }
}
