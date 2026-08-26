using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public sealed record CharacterAfterRunSettlementReceiptLedgerEntry(
    string ContractName,
    Guid TransactionId,
    long ExpectedWorkspaceRevision,
    long CommittedWorkspaceRevision,
    string CommandDigest,
    string BindingDigest,
    string AppliedResultDigest,
    string CharacterPayloadDigestBefore,
    string CharacterPayloadDigestAfter,
    CharacterAfterRunSettlementQuote ReviewedQuote,
    CharacterAfterRunSettlementReceipt Receipt,
    string EntryDigest);

/// <summary>
/// Integrity rules for the immutable workspace-owned After Run receipt lane.
/// Character payload digests bind the one appended receipt to the same atomic
/// saved-character replacement without placing audit metadata in a .chum5 file.
/// </summary>
public static class CharacterAfterRunSettlementReceiptLedgerIntegrity
{
    public const string EntryV1 =
        "chummer.core.sr5-after-run-settlement-receipt-ledger-entry/v1";
    public const int MaximumEntries = 4096;

    public static bool TryCreateEntry(
        CharacterWorkspaceId workspaceId,
        long expectedWorkspaceRevision,
        long committedWorkspaceRevision,
        string? commandDigest,
        string? characterPayloadBefore,
        string? characterPayloadAfter,
        CharacterAfterRunSettlementQuote? reviewedQuote,
        CharacterAfterRunSettlementReceipt? receipt,
        out CharacterAfterRunSettlementReceiptLedgerEntry entry)
    {
        entry = null!;
        if (expectedWorkspaceRevision <= 0
            || expectedWorkspaceRevision == long.MaxValue
            || committedWorkspaceRevision != expectedWorkspaceRevision + 1
            || characterPayloadBefore is null
            || characterPayloadAfter is null
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(commandDigest)
            || !IsReceiptBound(reviewedQuote, receipt)
            || !CharacterAfterRunSettlementServiceIntegrity.TryComputeBindingDigest(
                workspaceId,
                expectedWorkspaceRevision,
                reviewedQuote,
                out string bindingDigest))
        {
            return false;
        }

        string beforeDigest = PayloadDigest(characterPayloadBefore);
        string afterDigest = PayloadDigest(characterPayloadAfter);
        var unsignedResult = new CharacterAfterRunSettlementResult(
            CharacterAfterRunSettlementServiceSchemas.ResultV1,
            CharacterAfterRunSettlementServiceOutcome.Applied,
            workspaceId,
            expectedWorkspaceRevision,
            committedWorkspaceRevision,
            reviewedQuote!.Identity,
            receipt!.TransactionId,
            commandDigest!,
            reviewedQuote,
            receipt,
            [],
            string.Empty);
        if (!CharacterAfterRunSettlementServiceIntegrity.TryComputeResultDigest(
                unsignedResult,
                out string resultDigest))
        {
            return false;
        }

        var unsigned = new CharacterAfterRunSettlementReceiptLedgerEntry(
            EntryV1,
            receipt.TransactionId,
            expectedWorkspaceRevision,
            committedWorkspaceRevision,
            commandDigest!,
            bindingDigest,
            resultDigest,
            beforeDigest,
            afterDigest,
            reviewedQuote,
            receipt,
            string.Empty);
        entry = unsigned with { EntryDigest = CalculateEntryDigest(unsigned) };
        return IsCoherent(workspaceId, entry);
    }

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long currentWorkspaceRevision,
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? entries)
    {
        if (entries is null)
        {
            return true;
        }
        if (currentWorkspaceRevision <= 0 || entries.Count > MaximumEntries)
        {
            return false;
        }

        HashSet<Guid> transactionIds = [];
        HashSet<Guid> proposalIds = [];
        long previousCommittedRevision = 0;
        foreach (CharacterAfterRunSettlementReceiptLedgerEntry entry in entries)
        {
            if (!IsCoherent(workspaceId, entry)
                || entry.CommittedWorkspaceRevision > currentWorkspaceRevision
                || entry.CommittedWorkspaceRevision <= previousCommittedRevision
                || !transactionIds.Add(entry.TransactionId)
                || !proposalIds.Add(entry.ReviewedQuote.Identity.ProposalId))
            {
                return false;
            }
            previousCommittedRevision = entry.CommittedWorkspaceRevision;
        }
        return true;
    }

    public static bool IsValidAppendTransition(
        CharacterWorkspaceId workspaceId,
        long previousWorkspaceRevision,
        long nextWorkspaceRevision,
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? current,
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? replacement,
        string? characterPayloadBefore,
        string? characterPayloadAfter)
    {
        if (nextWorkspaceRevision != previousWorkspaceRevision + 1
            || replacement is null
            || replacement.Count != (current?.Count ?? 0) + 1
            || !IsValidLedger(workspaceId, nextWorkspaceRevision, replacement))
        {
            return false;
        }
        if (current is not null
            && !current.Zip(replacement.Take(current.Count)).All(pair =>
                FixedEquals(pair.First.EntryDigest, pair.Second.EntryDigest)))
        {
            return false;
        }

        CharacterAfterRunSettlementReceiptLedgerEntry appended = replacement[^1];
        return appended.ExpectedWorkspaceRevision == previousWorkspaceRevision
            && appended.CommittedWorkspaceRevision == nextWorkspaceRevision
            && FixedEquals(
                appended.CharacterPayloadDigestBefore,
                PayloadDigest(characterPayloadBefore))
            && FixedEquals(
                appended.CharacterPayloadDigestAfter,
                PayloadDigest(characterPayloadAfter));
    }

    public static bool IsCoherent(
        CharacterWorkspaceId workspaceId,
        CharacterAfterRunSettlementReceiptLedgerEntry? entry)
    {
        if (entry is null
            || !string.Equals(entry.ContractName, EntryV1, StringComparison.Ordinal)
            || entry.TransactionId == Guid.Empty
            || entry.ExpectedWorkspaceRevision <= 0
            || entry.ExpectedWorkspaceRevision == long.MaxValue
            || entry.CommittedWorkspaceRevision
                != entry.ExpectedWorkspaceRevision + 1
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(entry.CommandDigest)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(entry.BindingDigest)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(entry.AppliedResultDigest)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                entry.CharacterPayloadDigestBefore)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                entry.CharacterPayloadDigestAfter)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(entry.EntryDigest)
            || !IsReceiptBound(entry.ReviewedQuote, entry.Receipt)
            || entry.Receipt.TransactionId != entry.TransactionId
            || !CharacterAfterRunSettlementServiceIntegrity.TryComputeBindingDigest(
                workspaceId,
                entry.ExpectedWorkspaceRevision,
                entry.ReviewedQuote,
                out string bindingDigest)
            || !FixedEquals(bindingDigest, entry.BindingDigest))
        {
            return false;
        }

        var unsignedResult = new CharacterAfterRunSettlementResult(
            CharacterAfterRunSettlementServiceSchemas.ResultV1,
            CharacterAfterRunSettlementServiceOutcome.Applied,
            workspaceId,
            entry.ExpectedWorkspaceRevision,
            entry.CommittedWorkspaceRevision,
            entry.ReviewedQuote.Identity,
            entry.TransactionId,
            entry.CommandDigest,
            entry.ReviewedQuote,
            entry.Receipt,
            [],
            string.Empty);
        return CharacterAfterRunSettlementServiceIntegrity.TryComputeResultDigest(
                   unsignedResult,
                   out string resultDigest)
               && FixedEquals(resultDigest, entry.AppliedResultDigest)
               && FixedEquals(CalculateEntryDigest(entry), entry.EntryDigest);
    }

    private static bool IsReceiptBound(
        CharacterAfterRunSettlementQuote? reviewed,
        CharacterAfterRunSettlementReceipt? receipt)
        => CharacterAfterRunSettlementRules.IsCoherent(reviewed)
            && CharacterAfterRunSettlementRules.IsCoherent(receipt)
            && reviewed!.Identity == receipt!.Identity
            && FixedEquals(receipt.LogicalDigestBefore, reviewed.LogicalDigest)
            && FixedEquals(receipt.SourceDigest, reviewed.SourceDigest)
            && FixedEquals(receipt.CustomDataDigest, reviewed.CustomDataDigest)
            && FixedEquals(receipt.GmPolicyDigest, reviewed.GmPolicyDigest)
            && FixedEquals(receipt.RuntimeDigest, reviewed.RuntimeDigest)
            && FixedEquals(receipt.GmReviewDigest, reviewed.GmReviewDigest)
            && FixedEquals(receipt.OwnerReviewDigest, reviewed.OwnerReviewDigest);

    private static string CalculateEntryDigest(
        CharacterAfterRunSettlementReceiptLedgerEntry entry)
        => Sha256(Canonical(
            entry.ContractName,
            entry.TransactionId.ToString("D"),
            entry.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            entry.CommittedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            entry.CommandDigest,
            entry.BindingDigest,
            entry.AppliedResultDigest,
            entry.CharacterPayloadDigestBefore,
            entry.CharacterPayloadDigestAfter,
            entry.ReviewedQuote.LogicalDigest,
            entry.Receipt.ReceiptDigest));

    private static string PayloadDigest(string? payload)
        => Sha256(payload ?? string.Empty);

    private static string Canonical(params string[] values)
        => string.Join('\0', values.Select(value => string.Concat(
            value.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            value)));

    private static string Sha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
