using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// The shared, pre-carryover Priority/Sum-to-Ten balance. Callers must first
/// validate the source-bound quality result, total resource investment and
/// Mystic Adept allocation. This arithmetic grants no source or write authority.
/// </summary>
internal static class CharacterCreationPriorityKarmaBudget
{
    internal static bool TryEvaluate(
        int remainingAfterQualities,
        int resourcesInvestment,
        CharacterCreationMysticAdeptPowerPointAllocation? evaluatedPowerPoints,
        out int remaining)
    {
        remaining = 0;
        int powerPointCost = evaluatedPowerPoints?.KarmaCost ?? 0;
        if (remainingAfterQualities < 0 || resourcesInvestment < 0 || powerPointCost < 0)
            return false;
        try
        {
            int balance = checked(remainingAfterQualities - resourcesInvestment - powerPointCost);
            if (balance < 0)
                return false;
            remaining = balance;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
