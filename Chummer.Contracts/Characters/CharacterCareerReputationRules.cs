namespace Chummer.Contracts.Characters;

public sealed record CharacterCareerReputationSettings(bool UseCalculatedPublicAwareness);

/// <summary>
/// Core-resolved SR5 Career inputs. Improvement values are already combined and
/// rounded by the active rules environment; CareerKarma is earned career Karma,
/// not the available spending balance. No provider or UI may substitute guessed
/// modifier/settings values. Source/workspace binding belongs to the caller's
/// canonical projection and persistence service, not this pure calculation.
/// </summary>
public sealed record CharacterCareerReputationInputs(
    string RulesetId,
    bool IsCareer,
    int CareerKarma,
    int StreetCred,
    int Notoriety,
    int PublicAwareness,
    int BurntStreetCred,
    int StreetCredDivisorAdjustment,
    int StreetCredImprovement,
    int NotorietyImprovement,
    int PublicAwarenessImprovement,
    bool UseCalculatedPublicAwareness,
    bool Erased);

/// <summary>
/// Manual awards stay distinct from calculated totals. Notoriety and Public
/// Awareness totals can be negative in the pinned Chummer5 implementation.
/// </summary>
public sealed record CharacterCareerReputationProjection(
    CharacterCareerReputationInputs Inputs,
    int CalculatedStreetCred,
    int TotalStreetCred,
    int CalculatedNotoriety,
    int TotalNotoriety,
    int CalculatedPublicAwareness,
    int TotalPublicAwareness,
    bool CanBurnStreetCred);

/// <summary>
/// Null leaves a manual award untouched. A supplied value is the signed change,
/// not a replacement total. The resulting selected manual entry must satisfy
/// the pinned legacy input control bounds. Unselected imported values are never
/// silently clamped, normalized or rewritten.
/// </summary>
public sealed record CharacterCareerReputationAdjustment(
    int? StreetCredDelta = null,
    int? NotorietyDelta = null,
    int? PublicAwarenessDelta = null);

public enum CharacterCareerReputationOperation
{
    AdjustManualAwards,
    BurnStreetCred
}

/// <summary>
/// A read-only local what-if, NOT a reserved/saved command, GM approval or run
/// settlement. A persistence owner must independently reload and bind all inputs,
/// require explicit review, and save with revision/CAS and durable replay evidence.
/// </summary>
public sealed record CharacterCareerReputationQuote(
    CharacterCareerReputationOperation Operation,
    CharacterCareerReputationProjection Before,
    CharacterCareerReputationProjection After);

/// <summary>
/// Canonical arithmetic from Character.cs Calculated/Total reputation properties
/// at the pinned legacy revision. Manual bounds come from CharacterCareer's
/// three numeric controls, not a cap on calculated reputation. Burning changes
/// only burntstreetcred by two; it must not also decrement manual notoriety.
/// This class does not evaluate improvements, read XML, or authorize mutations.
/// </summary>
public static class CharacterCareerReputationRules
{
    public const string PinnedChummer5Revision = "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3";
    public const int MinimumManualEntry = 0;
    public const int MaximumManualEntry = 100;
    public const int StreetCredBurnCost = 2;

    public static bool TryProject(
        CharacterCareerReputationInputs inputs,
        out CharacterCareerReputationProjection? projection)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        projection = null;
        if (!string.Equals(inputs.RulesetId, "sr5", StringComparison.Ordinal)
            || !inputs.IsCareer || inputs.BurntStreetCred < 0)
            return false;

        try
        {
            int divisor = checked(10 + inputs.StreetCredDivisorAdjustment);
            if (divisor == 0) return false;
            // Preserve C# integer division from the oracle, including signed
            // inputs: it truncates toward zero, not mathematical negative floor.
            int streetCred = checked(inputs.CareerKarma / divisor - inputs.BurntStreetCred);
            int totalStreetCred = Math.Max(checked(streetCred + inputs.StreetCred + inputs.StreetCredImprovement), 0);
            int notoriety = checked(inputs.NotorietyImprovement - inputs.BurntStreetCred / StreetCredBurnCost);
            int totalNotoriety = checked(notoriety + inputs.Notoriety);
            int awareness = inputs.PublicAwarenessImprovement;
            if (inputs.UseCalculatedPublicAwareness)
                awareness = checked(awareness + checked(totalStreetCred + totalNotoriety) / 3);
            int totalAwareness = checked(awareness + inputs.PublicAwareness);
            if (inputs.Erased && totalAwareness >= 1) totalAwareness = 1;

            projection = new(inputs, streetCred, totalStreetCred, notoriety, totalNotoriety,
                awareness, totalAwareness, totalStreetCred >= StreetCredBurnCost);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    public static bool TryQuoteAdjustment(
        CharacterCareerReputationInputs inputs,
        CharacterCareerReputationAdjustment adjustment,
        out CharacterCareerReputationQuote? quote)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(adjustment);
        quote = null;
        if (!TryProject(inputs, out CharacterCareerReputationProjection? before)) return false;
        try
        {
            if (!TryAdjust(inputs.StreetCred, adjustment.StreetCredDelta, out int streetCred)
                || !TryAdjust(inputs.Notoriety, adjustment.NotorietyDelta, out int notoriety)
                || !TryAdjust(inputs.PublicAwareness, adjustment.PublicAwarenessDelta, out int awareness))
                return false;
            var changed = inputs with { StreetCred = streetCred, Notoriety = notoriety, PublicAwareness = awareness };
            if (changed == inputs || !TryProject(changed, out CharacterCareerReputationProjection? after)) return false;
            quote = new(CharacterCareerReputationOperation.AdjustManualAwards, before!, after!);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    public static bool TryQuoteBurnStreetCred(
        CharacterCareerReputationInputs inputs,
        out CharacterCareerReputationQuote? quote)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        quote = null;
        if (!TryProject(inputs, out CharacterCareerReputationProjection? before) || !before!.CanBurnStreetCred)
            return false;
        try
        {
            var changed = inputs with { BurntStreetCred = checked(inputs.BurntStreetCred + StreetCredBurnCost) };
            if (!TryProject(changed, out CharacterCareerReputationProjection? after)) return false;
            quote = new(CharacterCareerReputationOperation.BurnStreetCred, before, after!);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    private static bool TryAdjust(int before, int? delta, out int after)
    {
        after = delta.HasValue ? checked(before + delta.Value) : before;
        return !delta.HasValue || after is >= MinimumManualEntry and <= MaximumManualEntry;
    }
}
