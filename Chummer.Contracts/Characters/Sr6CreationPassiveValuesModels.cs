namespace Chummer.Contracts.Characters;

/// <summary>Saved creation values with permanent adept effects. No worn equipment,
/// temporary activation, wounds, situational Edge or finalized-character authority.
/// A null value is unresolved, never zero.</summary>
public sealed record Sr6CreationPassiveValues(
    IReadOnlyList<Sr6CreationPassiveAttributeValue> Attributes,
    IReadOnlyList<Sr6CreationPassiveSkillValue>? Skills,
    IReadOnlyList<Sr6CreationDerivedValue> Derived,
    IReadOnlyList<string> WarningIds,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record Sr6CreationPassiveAttributeValue(string AttributeId, int NaturalRating,
    int? PermanentBonus, int? Rating);

/// <summary>A noncombat-only increase must not be used by attacks with a mixed-use skill.</summary>
public sealed record Sr6CreationPassiveSkillValue(string SkillId, int NaturalRating,
    int AlwaysBonus, int NoncombatOnlyBonus, int AllUsesRating, int NoncombatRating);

/// <summary>Core-generated equation for the displayed integer, or empty when unresolved.</summary>
public sealed record Sr6CreationDerivedValue(string Id, int? Value, string Calculation, string SourceAnchorId);
