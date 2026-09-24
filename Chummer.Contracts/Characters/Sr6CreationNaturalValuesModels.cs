namespace Chummer.Contracts.Characters;

/// <summary>Saved pool and Karma values combined, before powers, equipment and situational effects.
/// Null domains have not been saved. This is not a finalized character or an effective dice pool.</summary>
public sealed record Sr6CreationNaturalValues(
    IReadOnlyList<Sr6CreationNaturalAttributeValue>? Attributes,
    IReadOnlyList<Sr6CreationNaturalSkill>? Skills,
    Sr6CreationNaturalKnowledge? Knowledge);

public sealed record Sr6CreationNaturalAttributeValue(string AttributeId, int BaseValue,
    int AttributePoints, int AdjustmentPoints, int KarmaIncrease, int Rating, int Maximum);

public sealed record Sr6CreationNaturalSkill(string SkillId, int PoolRating, int KarmaIncrease,
    int Rating, bool Available, IReadOnlyList<Sr6CreationNaturalSpecialization> Specializations);

/// <summary>Exotic weapon permissions have zero bonus; ordinary specialties apply only to their subject.</summary>
public sealed record Sr6CreationNaturalSpecialization(string Subject, int DicePoolBonus);

public sealed record Sr6CreationNaturalKnowledge(string NativeLanguage,
    IReadOnlyList<Sr6CreationKnowledgeEntry> KnowledgeSkills,
    IReadOnlyList<Sr6CreationNaturalLanguage> Languages);

public sealed record Sr6CreationNaturalLanguage(Guid Id, string Name, string Level, int ComprehensionBonus);
