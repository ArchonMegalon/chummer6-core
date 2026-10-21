namespace Chummer.Contracts.Characters;

public sealed record CharacterCareerMagicBinding(string OwnerId, string WorkspaceId,
    string OwnerAuthorityInstanceId, long OwnerTransitionRevision, long WorkspaceRevision,
    long SavedRevision, string CharacterDigest, string AuxiliaryDigest);
public sealed record CharacterCareerMagicEffect(string Kind, string Target, decimal Value);
public sealed record CharacterCareerMagicOwnedPower(Guid? SourceId, string Name, string ChoiceLabel,
    int? TotalRating, IReadOnlyList<CharacterCareerMagicEffect> Effects, IReadOnlyList<string> Blockers);
public sealed record CharacterCareerMagicPowerTarget(CharacterCareerPowerPlan Plan,
    IReadOnlyList<CharacterCareerMagicEffect> Effects);
public sealed record CharacterCareerMagicPowerChoice(string Name, string ChoiceLabel,
    IReadOnlyList<CharacterCareerMagicPowerTarget> Targets);
public sealed record CharacterCareerMagicRead(CharacterCareerMagicBinding Binding, bool Applicable,
    bool AllowsOwnerManagedTransactions, bool IsAdept, decimal? RemainingPowerPoints,
    IReadOnlyList<CharacterCareerMagicOwnedPower> OwnedPowers,
    IReadOnlyList<CharacterCareerMagicPowerChoice> Choices, IReadOnlyList<string> Blockers);
