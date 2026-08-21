namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact saved inputs behind CharacterCareer.cmdEdgeSpent/cmdEdgeGained.
/// </summary>
public sealed record CharacterCareerEdgeUseState(
    int EdgeUsed,
    int TotalEdge,
    bool CanSpend,
    bool CanRegain)
{
    public int AvailableEdge => Math.Max(TotalEdge - EdgeUsed, 0);
}

public enum CharacterCareerEdgeUseAction
{
    Spend,
    Regain
}

public static class CharacterCareerEdgeUseRules
{
    public static bool TryProject(
        bool created,
        int edgeUsed,
        int totalEdge,
        out CharacterCareerEdgeUseState? state)
    {
        state = null;
        if (!created || edgeUsed < 0 || totalEdge < 0)
        {
            return false;
        }

        state = new CharacterCareerEdgeUseState(
            edgeUsed,
            totalEdge,
            CanSpend: edgeUsed < totalEdge,
            CanRegain: edgeUsed > 0);
        return true;
    }

    public static bool CanApply(
        CharacterCareerEdgeUseState state,
        CharacterCareerEdgeUseAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        return action switch
        {
            CharacterCareerEdgeUseAction.Spend => state.CanSpend,
            CharacterCareerEdgeUseAction.Regain => state.CanRegain,
            _ => false
        };
    }

    public static int Apply(
        CharacterCareerEdgeUseState state,
        CharacterCareerEdgeUseAction action)
    {
        if (!CanApply(state, action))
        {
            throw new InvalidOperationException(
                action == CharacterCareerEdgeUseAction.Spend
                    ? "No remaining Edge can be spent."
                    : "No spent Edge can be regained.");
        }

        return action == CharacterCareerEdgeUseAction.Spend
            ? checked(state.EdgeUsed + 1)
            : checked(state.EdgeUsed - 1);
    }
}
