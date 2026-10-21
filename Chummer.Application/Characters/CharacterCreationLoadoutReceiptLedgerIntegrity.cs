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
            || !Digest(draft.RawCharacterXmlDigest)
            || !(draft.ResourcesDraftRevision > 0 && Digest(draft.ResourcesDraftDigest)
                || draft.ResourcesDraftRevision == 0 && draft.ResourcesDraftDigest == string.Empty && draft.CompositePurchase is not null)
            || draft.CompositePurchase is not null && !CharacterCreationCompositePurchaseEdit.IsValid(draft.CompositePurchase)
            || !CharacterCreationLoadoutRules.IsValidSelectedAuthority(draft.Authority)
            || !Same(draft.Authority.OriginalSelections, previous?.Selections ?? CharacterCreationLoadoutSelections.Empty)
            || !Same(draft.Authority.ProposedSelections, draft.Selections)
            || draft.CompositePurchase is null && !Preserves(previous?.Selections ?? CharacterCreationLoadoutSelections.Empty, draft.Selections)
            || !CharacterCreationLoadoutRules.TryProjectSelectedBasket(draft.Selections, draft.Authority,
                out var lines, out decimal cost, out _)
            || !Same(draft.Lines, lines) || draft.PurchaseCost != cost
            || !Same(draft.Instances, CreateInstances(id, revision, lines, previous?.Instances))
            || draft.DraftDigest != CharacterCreationLoadoutDraftRules.DraftDigest(draft)) return false;
        return draft.CompositePurchase is not null || previous is null || previous.Lines.All(line => lines.Any(current => Same(line, current)));
    }

    internal static IReadOnlyList<CharacterCreationLoadoutInstance> CreateInstances(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCreationLoadoutLine> lines, IReadOnlyList<CharacterCreationLoadoutInstance>? previous)
        => lines.Select(line => new CharacterCreationLoadoutInstance(line.OptionId,
            Enumerable.Range(0, line.Quantity).Select(index =>
                previous?.SingleOrDefault(row => row.OptionId == line.OptionId)?.InstanceIds.ElementAtOrDefault(index) is Guid retained
                    && retained != Guid.Empty ? retained
                    : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
                        $"creation-manual-loadout:{id.Value}:{revision}:{line.OptionId}:{index}")))[..16])).ToArray())).ToArray();

    internal static bool Preserves(CharacterCreationLoadoutSelections original, CharacterCreationLoadoutSelections proposed)
        => original.Weapons.All(proposed.Weapons.Contains) && original.Armor.All(proposed.Armor.Contains);

    internal static bool MatchesLive(CharacterCreationLoadoutDraft? draft, CharacterCreationLoadoutAuthority authority,
        string rawDigest)
        => draft is null || draft.RawCharacterXmlDigest == rawDigest
            && (draft.Authority.AuthorityDigest == authority.AuthorityDigest
                || MatchesFormerExclusionCatalog(draft.Authority, authority))
            && CharacterCreationLoadoutRules.TryProjectBasket(draft.Selections, authority, out var lines, out decimal cost, out _)
            && cost == draft.PurchaseCost && Same(lines, draft.Lines);

    // Only the exact former R/F exclusion transform is compatible. All source,
    // profile, availability, runtime and selected-price checks remain in force;
    // saved drafts and receipts are never rewritten or repriced.
    internal static bool MatchesFormerExclusionCatalog(CharacterCreationSelectedLoadoutAuthority saved,
        CharacterCreationLoadoutAuthority current)
    {
        foreach (var switches in new[] { (true, false), (false, true), (true, true) })
        {
            var policy = new CharacterCreationLoadoutLegalityPolicy(current.SettingsProfileId,
                current.ProfileDigest, switches.Item1, switches.Item2, string.Empty);
            if (policy.ComputeRulesDigest(current.MaximumAvailability) != current.RulesDigest) continue;
            var options = current.Options.Select(option =>
            {
                if (!policy.RequiresUnsupportedPrice(option.Legality)) return option;
                var excluded = option with
                {
                    IsSelectable = false, PricingIsExact = false,
                    Blockers = option.Blockers.Append(CharacterCreationLoadoutBlockers.UnsupportedSemantics)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                };
                return excluded with { OptionDigest = CharacterCreationLoadoutRules.ComputeOptionDigest(excluded) };
            }).ToArray();
            return saved.AuthorityDigest == CharacterCreationLoadoutRules.ComputeAuthorityDigest(current with { Options = options });
        }
        return false;
    }

    // Selected replay has no full catalog to rehash. It binds the immutable saved
    // closure to the same source context; its caller also reprojects every saved
    // line and checks its exact original price against that closure.
    internal static bool MatchesSelectedSourceContext(CharacterCreationSelectedLoadoutAuthority saved,
        CharacterCreationSelectedLoadoutAuthority current)
        => saved.RulesetId == current.RulesetId && saved.SettingsProfileId == current.SettingsProfileId
            && saved.SourceDigest == current.SourceDigest && saved.ProfileDigest == current.ProfileDigest
            && saved.RulesDigest == current.RulesDigest && saved.RuntimeDigest == current.RuntimeDigest
            && saved.MaximumAvailability == current.MaximumAvailability
            && saved.MaximumBasketLines == current.MaximumBasketLines
            && saved.MaximumQuantityPerLine == current.MaximumQuantityPerLine
            && Same(saved.SourceAnchorIds, current.SourceAnchorIds)
            && saved.Options.All(option => current.Options.Any(candidate => Same(option, candidate)))
            && new[] { (true, false), (false, true), (true, true) }.Any(switches =>
                new CharacterCreationLoadoutLegalityPolicy(current.SettingsProfileId, current.ProfileDigest,
                    switches.Item1, switches.Item2, string.Empty).ComputeRulesDigest(current.MaximumAvailability) == current.RulesDigest);

    internal static bool HasFunding(CharacterCreationLoadoutDraft? draft, CharacterCreationResourcesDraft? funding,
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? receipts)
        => draft is null || draft.CompositePurchase is { } composite
            && CharacterCreationCompositePurchaseEdit.IsValid(composite) && composite.TotalStartingNuyen >= draft.PurchaseCost
            || draft is not null && funding is not null && receipts is not null && receipts.Any(entry => entry?.Receipt is { } receipt
            && receipt.DraftRevision == draft.ResourcesDraftRevision && receipt.DraftDigest == draft.ResourcesDraftDigest
            && receipt.WorkspaceRevision <= draft.BaseContentRevision
            && receipt.RawCharacterXmlDigest == draft.RawCharacterXmlDigest
            && receipt.AuthorityDigest == funding.AuthorityDigest
            && receipt.PrerequisiteDraftDigest == funding.PrerequisiteDraftDigest
            && receipt.TotalStartingNuyen >= draft.PurchaseCost);

    internal static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static bool Digest(string value) => CharacterCreationFinalizationDigest.IsCanonical(value);
}
