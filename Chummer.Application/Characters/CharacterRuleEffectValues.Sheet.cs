using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterRuleEffectValues2912
{
    internal static CharacterRuleEffectValue Number(string id, string label, CharacterRuleEffectMetric metric,
        decimal? value, string qualifier = "", string? reason = null)
        => value.HasValue ? new(id, label, metric, qualifier, CharacterRuleEffectValueState.Known, Number: value)
            : new(id, label, metric, qualifier, CharacterRuleEffectValueState.Unavailable,
                UnavailableReason: reason ?? "The canonical calculation did not produce this value.");
    internal static CharacterRuleEffectValue Text(string id, string label, CharacterRuleEffectMetric metric,
        string value, string qualifier = "")
        => new(id, label, metric, qualifier, CharacterRuleEffectValueState.Known, Text: value);
    internal static CharacterRuleEffectValue Flag(string id, string label, CharacterRuleEffectMetric metric,
        bool? value, string qualifier = "", string? reason = null)
        => value.HasValue ? new(id, label, metric, qualifier, CharacterRuleEffectValueState.Known, Flag: value)
            : new(id, label, metric, qualifier, CharacterRuleEffectValueState.Unavailable,
                UnavailableReason: reason ?? "The canonical calculation did not determine availability.");
    internal static CharacterRuleEffectSectionProjection Section(CharacterRuleEffectSection section,
        IEnumerable<CharacterRuleEffectValue> values, IEnumerable<string>? reasons = null)
    {
        var rows = values.ToArray();
        var failures = (reasons ?? []).Concat(rows.Where(v => v.UnavailableReason is not null)
            .Select(v => v.UnavailableReason!)).Distinct(StringComparer.Ordinal).ToArray();
        return new(section, failures.Length == 0 ? CharacterRuleEffectCoverage.Calculated
            : rows.Any(v => v.State == CharacterRuleEffectValueState.Known)
                ? CharacterRuleEffectCoverage.Partial : CharacterRuleEffectCoverage.Unavailable, rows, failures);
    }

    internal static List<CharacterRuleEffectSectionProjection> Sheet(CharacterSheetDerivedSection sheet)
    {
        string[] reasons = sheet.UnavailableReason is { } reason ? [reason] : [];
        var result = new List<CharacterRuleEffectSectionProjection>();
        var attributes = new List<CharacterRuleEffectValue>();
        foreach (var row in sheet.Attributes)
        {
            attributes.Add(Number(row.Name, row.Name, CharacterRuleEffectMetric.Total, row.Total));
            attributes.Add(Number(row.Name, row.Name, CharacterRuleEffectMetric.Unaugmented, row.Unaugmented));
            attributes.Add(Number(row.Name, row.Name, CharacterRuleEffectMetric.AllocatedBase, row.AllocatedBase));
            attributes.Add(Number(row.Name, row.Name, CharacterRuleEffectMetric.AllocatedKarma, row.AllocatedKarma));
            attributes.Add(Number(row.Name, row.Name, CharacterRuleEffectMetric.AugmentedModifier, row.AugmentedModifier));
        }
        result.Add(Section(CharacterRuleEffectSection.Attributes, attributes, reasons));
        var skills = new List<CharacterRuleEffectValue>();
        foreach (var row in sheet.Skills)
        {
            skills.Add(Text(row.Guid, row.Name, CharacterRuleEffectMetric.Attribute, row.Attribute));
            skills.Add(Flag(row.Guid, row.Name, CharacterRuleEffectMetric.NativeLanguage, row.IsNativeLanguage));
            string? unavailable = row.IsNativeLanguage ? "A native language has no numerical rating or test pool." : row.UnavailableReason;
            skills.Add(Number(row.Guid, row.Name, CharacterRuleEffectMetric.Rating, row.EffectiveRating, reason: unavailable));
            skills.Add(Number(row.Guid, row.Name, CharacterRuleEffectMetric.LearnedRating, row.LearnedRating, reason: unavailable));
            skills.Add(Number(row.Guid, row.Name, CharacterRuleEffectMetric.DicePool, row.DicePool, reason: unavailable));
            foreach (var specialization in row.Specializations)
            {
                skills.Add(Number(row.Guid, row.Name, CharacterRuleEffectMetric.SpecializationBonus,
                    specialization.Bonus, specialization.Name));
                skills.Add(Number(row.Guid, row.Name, CharacterRuleEffectMetric.DicePool,
                    specialization.DicePool, specialization.Name));
            }
        }
        result.Add(Section(CharacterRuleEffectSection.Skills, skills,
            reasons.Concat(sheet.Skills.Where(s => s.UnavailableReason is not null).Select(s => s.UnavailableReason!))));
        var stats = new List<CharacterRuleEffectValue>
        {
            Number("runner", "Runner", CharacterRuleEffectMetric.Defense, sheet.DefensePool, reason: sheet.DefenseUnavailableReason ?? sheet.UnavailableReason),
            Number("runner", "Runner", CharacterRuleEffectMetric.PhysicalLimit, sheet.PhysicalLimit),
            Number("runner", "Runner", CharacterRuleEffectMetric.MentalLimit, sheet.MentalLimit),
            Number("runner", "Runner", CharacterRuleEffectMetric.SocialLimit, sheet.SocialLimit),
            Number("runner", "Runner", CharacterRuleEffectMetric.Essence, sheet.Essence)
        };
        if (sheet.PhysicalInitiative is { } initiative)
        {
            stats.AddRange(new[]
            {
                Number("runner", "Physical initiative", CharacterRuleEffectMetric.Initiative, initiative.Value),
                Number("runner", "Physical initiative", CharacterRuleEffectMetric.InitiativeDice, initiative.Dice),
                Number("runner", "Physical initiative", CharacterRuleEffectMetric.InitiativeBaseDice, initiative.BaseDice),
                Number("runner", "Physical initiative", CharacterRuleEffectMetric.InitiativeModifier, initiative.InitiativeImprovement),
                Number("runner", "Physical initiative", CharacterRuleEffectMetric.InitiativeDiceModifier, initiative.DiceImprovement),
                Number("runner", "Physical initiative", CharacterRuleEffectMetric.InitiativeAdditiveDice, initiative.AdditiveDiceImprovement)
            });
        }
        else stats.Add(Number("runner", "Physical initiative", CharacterRuleEffectMetric.Initiative, null, reason: sheet.UnavailableReason));
        if (sheet.Condition is { } condition)
        {
            stats.AddRange(new[]
            {
                Number("runner", "Condition", CharacterRuleEffectMetric.PhysicalMaximum, condition.PhysicalMaximum),
                Number("runner", "Condition", CharacterRuleEffectMetric.StunMaximum, condition.StunMaximum),
                Number("runner", "Condition", CharacterRuleEffectMetric.WoundThreshold, condition.WoundThreshold),
                Number("runner", "Condition", CharacterRuleEffectMetric.PhysicalThreshold, condition.PhysicalThresholdOffset),
                Number("runner", "Condition", CharacterRuleEffectMetric.StunThreshold, condition.StunThresholdOffset),
                Number("runner", "Condition", CharacterRuleEffectMetric.WoundModifier, condition.WoundModifier),
                Number("runner", "Condition", CharacterRuleEffectMetric.PhysicalRecovery, condition.PhysicalRecoveryPool),
                Number("runner", "Condition", CharacterRuleEffectMetric.StunRecovery, condition.StunRecoveryPool),
                Number("runner", "Condition", CharacterRuleEffectMetric.OverflowModifier, condition.OverflowModifier)
            });
        }
        else stats.Add(Number("runner", "Condition", CharacterRuleEffectMetric.PhysicalMaximum, null, reason: sheet.UnavailableReason));
        stats.AddRange(sheet.OtherPoolsAndModifiers.Select(pair => Number("runner", pair.Key, CharacterRuleEffectMetric.Pool, pair.Value, pair.Key)));
        result.Add(Section(CharacterRuleEffectSection.Statistics, stats, reasons));
        var movement = new List<CharacterRuleEffectValue>();
        AddMovement("ground", sheet.Movement?.Ground);
        AddMovement("swim", sheet.Movement?.Swim);
        AddMovement("fly", sheet.Movement?.Fly);
        result.Add(Section(CharacterRuleEffectSection.Movement, movement, reasons));
        void AddMovement(string mode, CharacterSheetMovementMode? value)
        {
            string? unavailable = value?.UnavailableReason ?? sheet.UnavailableReason;
            movement.Add(Flag(mode, mode, CharacterRuleEffectMetric.Available, value?.IsAvailable, reason: unavailable));
            movement.Add(Number(mode, mode, CharacterRuleEffectMetric.Walk, value?.WalkMetersPerTurn, reason: unavailable));
            movement.Add(Number(mode, mode, CharacterRuleEffectMetric.Run, value?.RunMetersPerTurn, reason: unavailable));
            movement.Add(Number(mode, mode, CharacterRuleEffectMetric.Sprint, value?.SprintMetersPerHit, reason: unavailable));
        }
        var traits = new List<CharacterRuleEffectValue>();
        var traitReasons = new List<string>(reasons);
        foreach (var trait in sheet.GrantedTraits)
        {
            string id = $"{trait.Kind}/{trait.OwnerId:D}/{trait.SourceId:D}/{trait.Selection}";
            traits.Add(Text(id, trait.Name, CharacterRuleEffectMetric.Selected, trait.Kind));
            traits.Add(Text(id, trait.Name, CharacterRuleEffectMetric.SourceBook, trait.SourceBook));
            traits.Add(Text(id, trait.Name, CharacterRuleEffectMetric.Selection, trait.Selection));
            traits.Add(Number(id, trait.Name, CharacterRuleEffectMetric.Rating, trait.Rating));
            traits.Add(Text(id, trait.Name, CharacterRuleEffectMetric.ParentMetatype, trait.ParentMetatypeId.ToString("D")));
            traits.Add(Text(id, trait.Name, CharacterRuleEffectMetric.SelectedMetatype, trait.SelectedMetatypeId.ToString("D")));
            if (trait.MechanicsUnavailableReason is { } mechanics) traitReasons.Add(mechanics);
            if (trait.ActionUnavailableReason is { } action) traitReasons.Add(action);
        }
        foreach (var skill in sheet.UnlockedSkills)
            traits.Add(Text($"skill/{skill.OwnerId:D}/{skill.SourceId:D}", skill.Name, CharacterRuleEffectMetric.Selected, "skill unlock"));
        result.Add(Section(CharacterRuleEffectSection.Traits, traits, traitReasons));
        var constraints = new List<CharacterRuleEffectValue>();
        var constraintReasons = new List<string>(reasons);
        foreach (var group in sheet.OwnedConditionalLimits.GroupBy(r => SemanticEntity2912(
            "conditional-limit", r.Limit, r.Condition)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            int occurrence = 0;
            foreach (var row in group)
            {
                constraints.Add(Number(group.Key, row.Limit, CharacterRuleEffectMetric.Modifier, row.Modifier)
                    with { Occurrence = occurrence });
                constraints.Add(Text(group.Key, row.Limit, CharacterRuleEffectMetric.Condition, row.Condition)
                    with { Occurrence = occurrence });
                constraints.Add(Flag(group.Key, row.Limit, CharacterRuleEffectMetric.Enabled, row.Enabled)
                    with { Occurrence = occurrence });
                occurrence++;
            }
        }
        foreach (var group in (sheet.RuleModifiers ?? []).GroupBy(r => SemanticEntity2912(
            "rule-modifier", r.OwnerId.ToString("D"), r.Kind.ToString(), r.Target, r.Condition,
            r.Exclude, r.AppliesToRating ? "rating" : "pool")).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            int occurrence = 0;
            foreach (var row in group)
            {
                constraints.Add(Number(group.Key, row.Target, CharacterRuleEffectMetric.Modifier, row.Value, row.Kind.ToString())
                    with { Occurrence = occurrence });
                constraints.Add(Text(group.Key, row.Target, CharacterRuleEffectMetric.Condition, row.Condition)
                    with { Occurrence = occurrence });
                constraints.Add(Text(group.Key, row.Target, CharacterRuleEffectMetric.Exclude, row.Exclude)
                    with { Occurrence = occurrence });
                constraints.Add(Flag(group.Key, row.Target, CharacterRuleEffectMetric.AppliesToRating, row.AppliesToRating)
                    with { Occurrence = occurrence });
                if (row.ActionUnavailableReason is { } action) constraintReasons.Add(action);
                occurrence++;
            }
        }
        if (sheet.RuleModifiers is null) constraintReasons.Add("The sheet consumer did not expose rule modifiers.");
        result.Add(Section(CharacterRuleEffectSection.Constraints, constraints, constraintReasons));
        return result;
    }

    // Length-delimited semantic fields, never calculated values or global positions.
    // The hash bounds the display key; it is not a calculated effect or mutation authority.
    private static string SemanticEntity2912(string kind, params string[] fields)
        => kind + "/" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(fields)));
}
