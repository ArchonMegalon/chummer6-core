using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Pure arithmetic, deliberately incapable of issuing source/Complete evidence.
// Application and retained Infrastructure use the same result when compiling
// the candidate and when projecting final native XML.
public static class CharacterCreationEssenceRules1222
{
    // Native allocation components, never reconstructed from a previously calculated total.
    public sealed record Attribute(string Id, bool Enabled, int Minimum, int Maximum,
        int AugmentedMaximum, int Base, int Karma, bool UnclampMinimum,
        int FreeBase = 0, int MinimumModifier = 0, int MaximumModifier = 0,
        int ValueModifier = 0, decimal AugmentedModifier = 0, int AugmentedMaximumModifier = 0,
        bool MaximumClamp = false);
    public sealed record NativeValue(int RawMinimum, int Minimum, int Maximum,
        int Value, int Current, int AugmentedMaximum);
    public sealed record Loss(string Id, int MinimumReduction, int MaximumReduction,
        int AugmentedReduction, int Current, int Maximum, int Minimum, int RawMinimum,
        int Value, int AugmentedMaximum, int BeforeCurrent);
    public sealed record Result(decimal Essence, decimal MinimumEssence, bool Valid,
        IReadOnlyList<Loss> Attributes, int PowerPointLoss, decimal NonEssencePowerPoints = 0m);

    public static Result Calculate(decimal maximum, decimal ware, decimal penalty,
        decimal magicPenalty, decimal resonancePenalty, decimal depthPenalty,
        decimal acquisition, decimal additiveBurn, decimal totalBurn, int decimals,
        bool unrounded, bool maximumOnly, bool shownValue, bool cyborg,
        bool mysticPowerPoints, IReadOnlyList<Attribute> attributes)
    {
        if (decimals is < 2 or > 28 || attributes.Select(a => a.Id).Distinct().Count() != attributes.Count
            || attributes.Any(a => a.Id is not ("MAG" or "MAGAdept" or "RES" or "DEP")
                || a.Minimum < 0 || a.Maximum < a.Minimum || a.AugmentedMaximum < a.Maximum
                || a.Base < 0 || a.Karma < 0))
            throw new InvalidDataException("Invalid exact Essence inputs.");
        decimal essence = cyborg ? 0.1m : checked(maximum + penalty - ware);
        decimal quantum = 1m;
        for (int i = 0; i < decimals; i++) quantum /= 10m;
        bool valid = maximum <= 0 || (unrounded ? essence > 0 : essence >= quantum);
        var losses = new List<Loss>();
        int powerPointLoss = 0;
        foreach (var attribute in attributes)
        {
            if (!attribute.Enabled) continue; // Executed enabled branch, not coverage from a Mundane label.
            if (acquisition == decimal.MinValue)
                throw new InvalidDataException("Enabled special attribute has no acquisition Essence.");
            decimal specialEssence = cyborg ? essence : checked(essence + (attribute.Id switch
            {
                "MAG" or "MAGAdept" => magicPenalty,
                "RES" => resonancePenalty,
                _ => depthPenalty
            }));
            if (!unrounded) specialEssence = decimal.Round(specialEssence, decimals, MidpointRounding.AwayFromZero);
            // Legacy StandardRound is ceiling for positive values and floor for
            // negative values. It is NOT nearest-integer rounding.
            int maximumLoss = Round(checked((maximum - specialEssence) * additiveBurn * totalBurn));
            int minimumLoss = Round(checked((acquisition - specialEssence) * additiveBurn * totalBurn));
            if (maximumOnly)
            {
                // Native MinimumMaximumNoEssenceLoss also includes non-Essence effects.
                int noLossMinimum = Math.Max(0, checked(attribute.Minimum + attribute.MinimumModifier));
                int noLossMaximum = Math.Max(noLossMinimum, checked(attribute.Maximum + attribute.MaximumModifier));
                minimumLoss = Math.Max(0, checked(minimumLoss + noLossMinimum - noLossMaximum));
            }
            int augmentedLoss = shownValue ? maximumLoss : 0;
            var before = Native(attribute);
            var after = Native(attribute, shownValue ? 0 : minimumLoss, shownValue ? 0 : maximumLoss, augmentedLoss);
            losses.Add(new(attribute.Id, shownValue ? 0 : minimumLoss, shownValue ? 0 : maximumLoss,
                augmentedLoss, after.Current, after.Maximum, after.Minimum, after.RawMinimum,
                after.Value, after.AugmentedMaximum, before.Current));
            if (shownValue && mysticPowerPoints && attribute.Id == "MAG") powerPointLoss = maximumLoss;
        }
        return new(essence, unrounded ? 0m : quantum, valid, losses.AsReadOnly(), powerPointLoss);
    }

    // Attribute.Core.cs: Value, RawMinimum, TotalMinimum, TotalMaximum,
    // AttributeModifiers and TotalValue. Special attributes allow zero.
    public static NativeValue Native(Attribute attribute, int minimumLoss = 0,
        int maximumLoss = 0, int augmentedLoss = 0)
    {
        checked
        {
            int raw = attribute.Minimum + attribute.MinimumModifier - minimumLoss;
            if (!attribute.UnclampMinimum) raw = Math.Max(0, raw);
            int minimum = Math.Max(0, raw);
            int maximum = Math.Max(0, attribute.Maximum + attribute.MaximumModifier - maximumLoss);
            int value = Math.Min(Math.Max(attribute.Base + attribute.FreeBase + raw
                + attribute.ValueModifier, minimum) + attribute.Karma, maximum);
            int clamp = attribute.AugmentedMaximum - attribute.Maximum + attribute.AugmentedMaximumModifier;
            if (attribute.MaximumClamp) clamp = Math.Min(clamp, maximum - value);
            int current = Math.Max(0, value + Math.Min(Round(attribute.AugmentedModifier - augmentedLoss), clamp));
            int augmentedMaximum = attribute.MaximumClamp ? maximum : Math.Max(0,
                attribute.AugmentedMaximum + attribute.MaximumModifier - maximumLoss + attribute.AugmentedMaximumModifier);
            return new(raw, minimum, maximum, value, current, augmentedMaximum);
        }
    }

    public sealed record NonRetroactiveSnapshot(decimal Additive, decimal Multiplicative);

    // Creation loads ignore the save-time extras. The Career handoff snapshots
    // current effects for every descendant. Forcegrade None retains default extras.
    public static NonRetroactiveSnapshot Snapshot(bool forceNone,
        IEnumerable<decimal> additivePercentages, IEnumerable<decimal> multiplicativePercentages)
        => forceNone ? new(0m, 1m) : new(
            additivePercentages.Aggregate(0m, (sum, value) => checked(sum - (1m - value / 100m))),
            multiplicativePercentages.Aggregate(1m, (product, value) => checked(product * value / 100m)));

    public static int Round(decimal value) => checked((int)(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value)));

    public static decimal Ware(decimal source, decimal additive, decimal multiplicative,
        int decimals, bool unrounded)
    {
        decimal value = checked(source * Math.Max(0, checked(additive * multiplicative)));
        return unrounded ? value : decimal.Round(value, decimals, MidpointRounding.AwayFromZero);
    }
}
