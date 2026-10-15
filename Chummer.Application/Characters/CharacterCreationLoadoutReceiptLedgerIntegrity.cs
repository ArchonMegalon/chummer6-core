using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Persisted shape and chain validation, never a purchase or write capability.</summary>
public static class CharacterCreationLoadoutReceiptLedgerIntegrity
{
    public const int MaximumEntries = 128;

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        CharacterCreationLoadoutDraft? draft, IReadOnlyList<CharacterCreationLoadoutReceipt>? receipts)
    {
        receipts ??= [];
        if (receipts.Count == 0) return draft is null;
        if (draft is null || receipts.Count > MaximumEntries || revision <= 0) return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        CharacterCreationLoadoutReceipt? previous = null;
        foreach (var receipt in receipts)
        {
            if (receipt is null || receipt.Schema != CharacterCreationLoadoutReceipt.SchemaV1
                || receipt.WorkspaceId != id || receipt.PreviousContentRevision is <= 0 or long.MaxValue
                || receipt.ContentRevision != receipt.PreviousContentRevision + 1 || receipt.ContentRevision > revision
                || receipt.PreviousSavedRevision != receipt.PreviousContentRevision || receipt.SavedRevision != receipt.ContentRevision
                || !CharacterCareerReputationTransaction.IsDigest(receipt.PreviousAuxiliaryStateDigest)
                || !Digest(receipt.IdempotencyKeyDigest) || !Digest(receipt.CommandDigest) || !Digest(receipt.PreviewDigest)
                || !keys.Add(receipt.IdempotencyKeyDigest)
                || receipt.PreviousReceiptDigest != (previous?.ReceiptDigest ?? CharacterCreationLoadoutDraftRules.RootReceiptDigest)
                || previous is not null && receipt.PreviousContentRevision < previous.ContentRevision
                || !ValidDraft(id, receipt.PreviousContentRevision, receipt.Draft, previous?.Draft)
                || receipt.ReceiptDigest != CharacterCreationLoadoutDraftRules.ReceiptDigest(receipt)) return false;
            previous = receipt;
        }
        return Same(draft, receipts[^1].Draft);
    }

    private static bool ValidDraft(CharacterWorkspaceId id, long revision, CharacterCreationLoadoutDraft? draft,
        CharacterCreationLoadoutDraft? previous)
    {
        if (draft is null || draft.Schema != CharacterCreationLoadoutDraft.SchemaV1 || draft.WorkspaceId != id
            || draft.BaseContentRevision != revision || draft.DraftRevision != (previous?.DraftRevision ?? 0) + 1
            || !Digest(draft.RawCharacterXmlDigest) || draft.ResourcesDraftRevision <= 0 || !Digest(draft.ResourcesDraftDigest)
            || !CharacterCreationLoadoutRules.IsValidSelectedAuthority(draft.Authority)
            || !Same(draft.Authority.OriginalSelections, previous?.Selections ?? CharacterCreationLoadoutSelections.Empty)
            || !Same(draft.Authority.ProposedSelections, draft.Selections)
            || !Preserves(previous?.Selections ?? CharacterCreationLoadoutSelections.Empty, draft.Selections)
            || !CharacterCreationLoadoutRules.TryProjectSelectedBasket(draft.Selections, draft.Authority,
                out var lines, out decimal cost, out _)
            || !Same(draft.Lines, lines) || draft.PurchaseCost != cost
            || !Same(draft.Instances, CreateInstances(id, revision, lines, previous?.Instances))
            || draft.DraftDigest != CharacterCreationLoadoutDraftRules.DraftDigest(draft)) return false;
        return previous is null || previous.Lines.All(line => lines.Any(current => Same(line, current)));
    }

    internal static IReadOnlyList<CharacterCreationLoadoutInstance> CreateInstances(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCreationLoadoutLine> lines, IReadOnlyList<CharacterCreationLoadoutInstance>? previous)
        => lines.Select(line => previous?.SingleOrDefault(row => row.OptionId == line.OptionId)
            ?? new CharacterCreationLoadoutInstance(line.OptionId, Enumerable.Range(0, line.Quantity).Select(index =>
                new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
                    $"creation-manual-loadout:{id.Value}:{revision}:{line.OptionId}:{index}")))[..16])).ToArray())).ToArray();

    internal static bool Preserves(CharacterCreationLoadoutSelections original, CharacterCreationLoadoutSelections proposed)
        => original.Weapons.All(proposed.Weapons.Contains) && original.Armor.All(proposed.Armor.Contains);

    internal static bool MatchesLive(CharacterCreationLoadoutDraft? draft, CharacterCreationLoadoutAuthority authority,
        string rawDigest)
        => draft is null || draft.RawCharacterXmlDigest == rawDigest
            && draft.Authority.AuthorityDigest == authority.AuthorityDigest
            && CharacterCreationLoadoutRules.TryProjectBasket(draft.Selections, authority, out var lines, out decimal cost, out _)
            && cost == draft.PurchaseCost && Same(lines, draft.Lines);

    internal static bool HasFunding(CharacterCreationLoadoutDraft? draft, CharacterCreationResourcesDraft? funding,
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? receipts)
        => draft is null || funding is not null && receipts is not null && receipts.Any(entry => entry?.Receipt is { } receipt
            && receipt.DraftRevision == draft.ResourcesDraftRevision && receipt.DraftDigest == draft.ResourcesDraftDigest
            && receipt.WorkspaceRevision <= draft.BaseContentRevision
            && receipt.RawCharacterXmlDigest == draft.RawCharacterXmlDigest
            && receipt.AuthorityDigest == funding.AuthorityDigest
            && receipt.PrerequisiteDraftDigest == funding.PrerequisiteDraftDigest
            && receipt.TotalStartingNuyen >= draft.PurchaseCost);

    internal static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static bool Digest(string value) => CharacterCreationFinalizationDigest.IsCanonical(value);
}
