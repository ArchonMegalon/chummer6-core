namespace Chummer.Contracts.Characters;

// Display values only. None of these types is an admission, receipt or mutation permit.
public enum CharacterRuleEffectSection
{
    Attributes, Skills, Statistics, Movement, Traits, Constraints, Equipment,
    Profile, Metatypes, Priorities, Lifestyles, Gear, Qualities, SkillCosts,
    Traditions, Streams, Powers, Spells, ComplexForms, Weapons, Armor
}

public enum CharacterRuleEffectCoverage { Calculated, Partial, NotSelected, NotApplicable, Unavailable }
public enum CharacterRuleEffectValueState { Known, Unavailable }
public enum CharacterRuleEffectMetric
{
    Total, Unaugmented, AllocatedBase, AllocatedKarma, AugmentedModifier,
    Rating, LearnedRating, DicePool, Attribute, NativeLanguage, SpecializationBonus,
    Defense, PhysicalLimit, MentalLimit, SocialLimit, Essence, Initiative, InitiativeDice,
    InitiativeBaseDice, InitiativeModifier, InitiativeDiceModifier, InitiativeAdditiveDice,
    WoundModifier, PhysicalMaximum, StunMaximum, WoundThreshold, PhysicalThreshold,
    StunThreshold, PhysicalRecovery, StunRecovery, OverflowModifier, Pool,
    Available, Walk, Run, Sprint, Selected, SourceBook, ParentMetatype, SelectedMetatype,
    Selection, Modifier, Condition, Enabled, Exclude, AppliesToRating,
    Minimum, Maximum, AugmentedMaximum, PriorityCost, KarmaCost, RuleKarmaCost, KarmaDelta,
    BudgetTotal, BudgetUsed, BudgetRemaining, Overspend, PriorityNuyen, KarmaInvestment,
    NuyenFromKarma, StartingNuyen, PurchaseCost, RemainingNuyen, CarryoverLimit, CarryoverExcess,
    Quantity, PackageQuantity, UnitCost, TotalCost, Availability, Legality,
    PositiveKarma, NegativeKarma, KarmaRemaining, KnowledgePoints, KnowledgePointsUsed,
    NativeLanguages, PowerPoints, RulePointCost, Magic, Resonance, DrainPool, FadePool,
    ArmorRating, ArmorCapacity, CapacityUsed, Encumbrance, Accuracy, Reach, Damage, AmmoRemaining, RoundsPerAction
}

/// <summary>One scalar emitted by a named canonical calculation. Null means unknown, never zero.
/// Occurrence retains separate rows with the same semantic Entity. It is a display ordinal,
/// not source identity, and must not be used to pair duplicate effects across projections.</summary>
public sealed record CharacterRuleEffectValue(
    string Entity, string Label, CharacterRuleEffectMetric Metric, string Qualifier,
    CharacterRuleEffectValueState State, decimal? Number = null, string? Text = null,
    bool? Flag = null, string? UnavailableReason = null, int Occurrence = 0);

public sealed class CharacterRuleEffectSectionProjection
{
    public CharacterRuleEffectSectionProjection(CharacterRuleEffectSection section,
        CharacterRuleEffectCoverage coverage, IReadOnlyList<CharacterRuleEffectValue> values,
        IReadOnlyList<string> reasons)
    {
        Section = section;
        Coverage = coverage;
        Values = Array.AsReadOnly(values.OrderBy(v => v.Entity, StringComparer.Ordinal)
            .ThenBy(v => v.Occurrence).ThenBy(v => v.Metric).ThenBy(v => v.Qualifier, StringComparer.Ordinal).ToArray());
        Reasons = Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        if (Values.Count > 8192 || Reasons.Count > 256
            || Values.Select(v => (v.Entity, v.Occurrence, v.Metric, v.Qualifier)).Distinct().Count() != Values.Count
            || Values.Any(v => v.Occurrence < 0 || v.Entity.Length > 512 || v.Label.Length > 1024 || v.Qualifier.Length > 2048
                || (v.Text?.Length ?? 0) > 4096 || (v.UnavailableReason?.Length ?? 0) > 4096
                || (v.State == CharacterRuleEffectValueState.Known
                    ? (v.Number.HasValue ? 1 : 0) + (v.Text is null ? 0 : 1) + (v.Flag.HasValue ? 1 : 0) != 1
                        || v.UnavailableReason is not null
                    : v.Number is not null || v.Text is not null || v.Flag is not null || string.IsNullOrWhiteSpace(v.UnavailableReason)))
            || Reasons.Any(r => string.IsNullOrWhiteSpace(r) || r.Length > 4096)
            || coverage == CharacterRuleEffectCoverage.Calculated
                && (Reasons.Count != 0 || Values.Any(v => v.State != CharacterRuleEffectValueState.Known))
            || coverage is CharacterRuleEffectCoverage.NotSelected or CharacterRuleEffectCoverage.NotApplicable
                && Values.Count != 0)
            throw new ArgumentException("Invalid bounded rule effect section.");
    }
    public CharacterRuleEffectSection Section { get; }
    public CharacterRuleEffectCoverage Coverage { get; }
    public IReadOnlyList<CharacterRuleEffectValue> Values { get; }
    public IReadOnlyList<string> Reasons { get; }
}

public sealed record CharacterRuleEffectBinding(string WorkspaceId, long ContentRevision, long SavedRevision,
    string CurrentBindingDigest, string CharacterDigest, string AuxiliaryDigest, string CaptureDigest,
    string RequiredPackageSetDigest, string ExecutionProfile, string ExecutionProfileFingerprint,
    string CanonicalOutcomeDigest);

public sealed class CharacterRuleEffectProjection
{
    public CharacterRuleEffectProjection(CharacterRuleEffectBinding binding,
        IReadOnlyList<CharacterRuleEffectSectionProjection> sections)
    {
        Binding = binding;
        Sections = Array.AsReadOnly(sections.OrderBy(s => s.Section).ToArray());
        if (Sections.Count != Enum.GetValues<CharacterRuleEffectSection>().Length
            || Sections.Select(s => s.Section).Distinct().Count() != Sections.Count
            || Sections.Sum(s => s.Values.Count) > 32768)
            throw new ArgumentException("Incomplete or excessive section inventory.");
    }
    public CharacterRuleEffectBinding Binding { get; }
    public IReadOnlyList<CharacterRuleEffectSectionProjection> Sections { get; }
    public bool IsComplete => Sections.All(s => s.Coverage is CharacterRuleEffectCoverage.Calculated
        or CharacterRuleEffectCoverage.NotApplicable);
}

// A missing side is absence only for Calculated/NotSelected coverage; otherwise it is unknown.
// AmbiguousIdentity marks unpaired occurrences in a duplicate semantic group. These rows
// describe its changed multiset, not a proven one-to-one addition/removal or numeric delta.
public sealed record CharacterRuleEffectChange(CharacterRuleEffectSection Section,
    CharacterRuleEffectValue? Before, CharacterRuleEffectValue? After,
    CharacterRuleEffectCoverage BeforeCoverage, CharacterRuleEffectCoverage AfterCoverage,
    bool AmbiguousIdentity = false);

/// <summary>Transport-safe display. Construction/deserialization grants no calculation or write authority.</summary>
public sealed class CharacterRuleEffectComparisonDisplay
{
    public CharacterRuleEffectComparisonDisplay(CharacterRuleEffectProjection before, CharacterRuleEffectProjection after,
        IReadOnlyList<CharacterRuleEffectChange> changes, IReadOnlyList<string> sourceChanges)
    {
        Before = before; After = after;
        Changes = Array.AsReadOnly(changes.ToArray());
        SourceChanges = Array.AsReadOnly(sourceChanges.ToArray());
        if (Changes.Count > 65536 || SourceChanges.Count > 4096
            || SourceChanges.Sum(value => (long)value.Length) > 256 * 1024)
            throw new ArgumentException("Rule comparison exceeds display bound.");
    }
    public CharacterRuleEffectProjection Before { get; }
    public CharacterRuleEffectProjection After { get; }
    public IReadOnlyList<CharacterRuleEffectChange> Changes { get; }
    public IReadOnlyList<string> SourceChanges { get; }
    public bool IsComplete => Before.IsComplete && After.IsComplete;
}
