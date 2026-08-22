using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal static class CharacterCreationAttributesDraftIntegrity
{
    public static string ComputeDigest(CharacterCreationAttributesDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            draft with { DraftDigest = string.Empty });
    }

    public static bool IsStructurallyValidPending(
        CharacterCreationAttributesDraft? draft,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision,
        string rawCharacterXmlDigest,
        CharacterCreationPrerequisiteDraft prerequisite)
    {
        return draft is not null
               && string.Equals(draft.Schema, CharacterCreationAttributesSchemas.DraftV1, StringComparison.Ordinal)
               && draft.WorkspaceId == workspaceId
               && draft.DraftRevision > 0
               && draft.BaseContentRevision > 0
               && draft.BaseContentRevision < persistedContentRevision
               && CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(draft.BaseRawCharacterXmlDigest)
               && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                   draft.BaseRawCharacterXmlDigest,
                   rawCharacterXmlDigest)
               && draft.PrerequisiteDraftRevision == prerequisite.DraftRevision
               && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                   draft.PrerequisiteDraftDigest,
                   prerequisite.DraftDigest)
               && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                   draft.PrerequisiteAuthorityDigest,
                   prerequisite.AuthorityDigest)
               && string.Equals(
                   draft.MetatypeSourceId,
                   prerequisite.HeritageSelection?.MetatypeSourceId,
                   StringComparison.Ordinal)
               && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                   draft.MetatypeSourceNodeDigest,
                   prerequisite.HeritageSelection?.MetatypeSourceNodeDigest)
               && draft.HalvesNormalAttributePoints == prerequisite.HeritageSelection?.HalvesNormalAttributePoints
               && draft.NormalPointTotal == prerequisite.EffectiveNormalAttributePoints
               && draft.SpecialPointTotal == prerequisite.TotalSpecialAttributePoints
               && draft.NormalPointUsed >= 0
               && draft.NormalPointUsed <= draft.NormalPointTotal
               && draft.SpecialPointUsed >= 0
               && draft.SpecialPointUsed <= draft.SpecialPointTotal
               && draft.CreationKarmaTotal == prerequisite.CreationKarmaTotal
               && draft.CreationKarmaUsed >= prerequisite.CreationKarmaUsed
               && draft.CreationKarmaUsed <= draft.CreationKarmaTotal
               && draft.Allocations is not null
               && draft.Attributes is not null
               && draft.SourceAnchorIds is { Count: > 0 }
               && !draft.CharacterEffectsApplied
               && CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(draft.DraftDigest)
               && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                   draft.DraftDigest,
                   ComputeDigest(draft));
    }

    public static bool HasSameLogicalPayload(
        CharacterCreationAttributesDraft left,
        CharacterCreationAttributesDraft right) =>
        CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            left with { DraftRevision = 0, BaseContentRevision = 0, DraftDigest = string.Empty },
            right with { DraftRevision = 0, BaseContentRevision = 0, DraftDigest = string.Empty });
}
