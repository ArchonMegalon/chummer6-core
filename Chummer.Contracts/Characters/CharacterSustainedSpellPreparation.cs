using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

// This is the native sustained-object registration operation for a resolved
// spell, not spellcasting, drain settlement or a spell-specific effect evaluator.
public sealed record CharacterSustainedSpellOption(Guid SpellId, Guid SourceId,
    string DisplayName, string SourceBook, string SourcePage);

public sealed record CharacterSustainedSpellPreparationState(
    IReadOnlyList<CharacterSustainedSpellOption> Options,
    IReadOnlyList<CharacterSustainedObjectState> Objects,
    int PenalizedObjectCount, int DicePoolPenalty);

public sealed record CharacterSustainedSpellPreparationRequest(Guid SpellId, int Force, int NetHits);

public sealed record CharacterSustainedSpellPreparationBinding(string OwnerId, bool TrustedLocalOwner,
    CharacterWorkspaceId WorkspaceId, long ContentRevision, long SavedRevision,
    string SourceDigest, string AuxiliaryDigest, string EnvironmentDigest);

public sealed record CharacterSustainedSpellPreparationReview(
    CharacterSustainedSpellPreparationBinding Binding,
    CharacterSustainedSpellPreparationRequest Request,
    CharacterSustainedObjectIdentity AddedIdentity, string DisplayName,
    int DicePoolPenaltyBefore, int DicePoolPenaltyAfter,
    string SheetAuthorityBefore, string SheetAuthorityAfter);
