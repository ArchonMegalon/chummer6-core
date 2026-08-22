using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal static class CharacterCreationPrerequisiteDraftIntegrity
{
    public static string ComputeDigest(CharacterCreationPrerequisiteDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            draft with { DraftDigest = string.Empty });
    }

    public static bool IsValidPending(
        CharacterCreationPrerequisiteDraft? draft,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision,
        string rawCharacterXmlDigest,
        CharacterCreationPrerequisiteAuthority authority)
    {
        if (draft is null
            || !string.Equals(
                draft.Schema,
                CharacterCreationPrerequisiteSchemas.DraftV1,
                StringComparison.Ordinal)
            || draft.WorkspaceId != workspaceId
            || draft.DraftRevision <= 0
            || draft.BaseContentRevision <= 0
            || draft.BaseContentRevision >= persistedContentRevision
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                draft.BaseRawCharacterXmlDigest)
            || !FixedTimeEquals(draft.BaseRawCharacterXmlDigest, rawCharacterXmlDigest)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                draft.AuthorityDigest)
            || !FixedTimeEquals(draft.AuthorityDigest, authority.AuthorityDigest)
            || !string.Equals(draft.BuildMethod, authority.BuildMethod, StringComparison.Ordinal)
            || !string.Equals(
                draft.SettingsProfileId,
                authority.SettingsProfileId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(draft.PriorityTable, authority.PriorityTable, StringComparison.Ordinal)
            || draft.PriorityArray is null
            || !draft.PriorityArray.SequenceEqual(authority.PriorityArray, StringComparer.Ordinal)
            || draft.SumToTenTarget != authority.SumToTenTarget
            || draft.Assignments is null
            || draft.Assignments.Count != CharacterCreationPriorityCategoryIds.Ordered.Count
            || draft.CreationKarmaTotal != authority.CreationKarmaTotal
            || draft.CreationKarmaUsed != 0
            || draft.CreationKarmaTotal < 0
            || draft.SourceAnchorIds is null
            || draft.SourceAnchorIds.Any(anchor => !IsNormalizedNonEmpty(anchor))
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                draft.DraftDigest)
            || !FixedTimeEquals(draft.DraftDigest, ComputeDigest(draft)))
        {
            return false;
        }

        for (int index = 0; index < draft.Assignments.Count; index++)
        {
            CharacterCreationPriorityAssignment? assignment = draft.Assignments[index];
            if (assignment is null
                || assignment.Order != index
                || !string.Equals(
                    assignment.CategoryId,
                    CharacterCreationPriorityCategoryIds.Ordered[index],
                    StringComparison.Ordinal)
                || !IsNormalizedNonEmpty(assignment.Rank)
                || !Guid.TryParseExact(assignment.SourceId, "D", out Guid sourceId)
                || sourceId == Guid.Empty
                || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                    assignment.SourceNodeDigest)
                || assignment.SumToTenValue < 0
                || assignment.SourceAnchorIds is null
                || assignment.SourceAnchorIds.Any(anchor => !IsNormalizedNonEmpty(anchor)))
            {
                return false;
            }

            CharacterCreationPriorityOptionProjection[] matchingOptions = authority.Options
                .Where(option => string.Equals(
                                     option.CategoryId,
                                     assignment.CategoryId,
                                     StringComparison.Ordinal)
                                 && string.Equals(
                                     option.Rank,
                                     assignment.Rank,
                                     StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matchingOptions.Length != 1)
                return false;
            CharacterCreationPriorityOptionProjection option = matchingOptions[0];
            if (!string.Equals(option.SourceId, assignment.SourceId, StringComparison.Ordinal)
                || !FixedTimeEquals(option.SourceNodeDigest, assignment.SourceNodeDigest)
                || option.SumToTenValue != assignment.SumToTenValue
                || option.BaseNormalAttributePoints != assignment.BaseNormalAttributePoints
                || !option.SourceAnchorIds.SequenceEqual(
                    assignment.SourceAnchorIds,
                    StringComparer.Ordinal))
            {
                return false;
            }
        }

        return IsSelectionValid(draft, authority);
    }

    public static bool HasSameLogicalPayload(
        CharacterCreationPrerequisiteDraft left,
        CharacterCreationPrerequisiteDraft right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            left with
            {
                DraftRevision = 0,
                BaseContentRevision = 0,
                DraftDigest = string.Empty
            },
            right with
            {
                DraftRevision = 0,
                BaseContentRevision = 0,
                DraftDigest = string.Empty
            });
    }

    private static bool IsSelectionValid(
        CharacterCreationPrerequisiteDraft draft,
        CharacterCreationPrerequisiteAuthority authority)
    {
        string[] selectedRanks = draft.Assignments.Select(assignment => assignment.Rank).ToArray();
        if (string.Equals(
                authority.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal))
        {
            return selectedRanks.OrderBy(rank => rank, StringComparer.Ordinal).SequenceEqual(
                authority.PriorityArray.OrderBy(rank => rank, StringComparer.Ordinal),
                StringComparer.Ordinal);
        }
        if (!string.Equals(
                authority.BuildMethod,
                CharacterCreationBuildMethods.SumToTen,
                StringComparison.Ordinal)
            || authority.SumToTenTarget is not int target)
        {
            return false;
        }

        try
        {
            return checked(draft.Assignments.Sum(assignment => assignment.SumToTenValue)) == target;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsNormalizedNonEmpty(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
