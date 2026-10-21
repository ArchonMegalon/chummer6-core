namespace Chummer.Contracts.Characters;

/// <summary>
/// The unaugmented SR5 skill action rule used after creation allocations have
/// been evaluated. This is calculation, not authority, graph admission or a
/// replacement for the point, Karma, grant, group and prerequisite consumers.
/// Native languages have no numeric roll; they are never a zero-rating default.
/// </summary>
public static class CharacterCreationActionPoolRules695
{
    public sealed record Input(
        int Attribute, int Rating, bool Enabled, bool NativeLanguage,
        bool CanDefault, bool HasSpecialization, int DiceModifier);

    public sealed record Result(
        bool Available, bool NativeLanguage, int? DicePool,
        int? SpecializedDicePool, int DefaultPenalty, int DiceModifier);

    public sealed record Handedness(
        string PrimaryArm, bool Ambidextrous, int LeftHandModifier, int RightHandModifier);

    // Skill.PoolOtherAttribute: enabled/attribute gates, learned rating or
    // default-1, dice-only modifiers, and clamp. Wounds, sustaining, augmentation,
    // conditional modifiers and default-removal effects must be admitted by a
    // caller that actually implements them; this rule does not silently drop them.
    public static bool TryEvaluate(Input input, out Result? result)
    {
        result = null;
        if (input is null || input.Attribute is < 0 or > 100
            || input.Rating is < 0 or > 100) return false;
        try
        {
            if (input.NativeLanguage)
            {
                if (input.Rating != 0 || input.HasSpecialization || input.DiceModifier != 0) return false;
                result = new(true, true, null, null, 0, 0);
                return true;
            }
            bool available = input.Enabled && input.Attribute > 0
                && (input.Rating > 0 || input.CanDefault);
            int penalty = available && input.Rating == 0 ? -1 : 0;
            int pool = available
                ? Math.Max(0, checked(input.Attribute + input.Rating + input.DiceModifier + penalty)) : 0;
            result = new(available, false, pool,
                available && input.Rating > 0 && input.HasSpecialization
                    ? checked(pool + 2) : null,
                penalty, input.DiceModifier);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    // Native character PrimaryArm defaults to Right. The caller resolves that
    // saved-field default explicitly and validates every Ambidextrous occurrence.
    // A zero-valued presence improvement removes the -2 off-hand action modifier.
    public static bool TryEvaluateHandedness(string primaryArm, IReadOnlyList<bool> enabledEffects,
        out Handedness? result)
    {
        result = null;
        if (primaryArm is not ("Left" or "Right") || enabledEffects is null) return false;
        bool ambidextrous = enabledEffects.Any(enabled => enabled);
        result = new(primaryArm, ambidextrous,
            ambidextrous || primaryArm == "Left" ? 0 : -2,
            ambidextrous || primaryArm == "Right" ? 0 : -2);
        return true;
    }

    public sealed record Resistance(int Body, int Willpower, int Modifier, int DicePool);
    public static bool TryEvaluateResistance(int body, int willpower, IReadOnlyList<int> modifiers,
        out Resistance? result)
    {
        result = null;
        if (body is < 0 or > 100 || willpower is < 0 or > 100 || modifiers is null
            || modifiers.Any(value => value < 0)) return false;
        try
        {
            int modifier = modifiers.Aggregate(0, (total, value) => checked(total + value));
            result = new(body, willpower, modifier, checked(body + willpower + modifier));
            return true;
        }
        catch (OverflowException) { return false; }
    }
}
