using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Defense-in-depth validation at the persistence boundary. The command
/// service is the policy owner; stores still reject incomplete or widened
/// receipts supplied by another in-process caller.
/// </summary>
public static class DelegatedGmCharacterEditLedgerValidator
{
    private const int MaximumIdentifierLength = 200;
    private const int MaximumReasonLength = 500;
    private const int MaximumOperationCount = 3;

    /// <summary>
    /// Validates the complete append-only ledger before any workspace state is
    /// exposed. Revisions may contain gaps because an owner edit does not add a
    /// delegated receipt, but delegated receipts must never overlap or move
    /// backwards through the workspace revision history.
    /// </summary>
    public static bool IsValidLedger(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long currentContentRevision,
        IReadOnlyList<DelegatedGmCharacterEditLedgerEntry>? entries)
    {
        if (entries is null
            || string.IsNullOrWhiteSpace(owner.NormalizedValue)
            || string.IsNullOrWhiteSpace(id.Value)
            || currentContentRevision <= 0)
        {
            return false;
        }

        if (entries.Count == 0)
        {
            return true;
        }

        // Delegated campaign authority must never be smuggled into the trusted
        // process-local owner lane.
        if (owner.UsesLocalSingleUserValue)
        {
            return false;
        }

        HashSet<string> idempotencyKeys = new(StringComparer.Ordinal);
        HashSet<string> receiptIds = new(StringComparer.Ordinal);
        Dictionary<string, DelegationBinding> delegationBindings = new(StringComparer.Ordinal);
        Dictionary<string, AuthorityReceiptBinding> authorityReceiptBindings = new(StringComparer.Ordinal);
        long previousDelegatedRevision = 0;
        DateTimeOffset previousAppliedAtUtc = default;

        foreach (DelegatedGmCharacterEditLedgerEntry? entry in entries)
        {
            if (!IsValidPersistedEntry(owner, id, currentContentRevision, entry))
            {
                return false;
            }

            DelegatedGmCharacterEditAuditReceipt receipt = entry!.Receipt;
            if (!idempotencyKeys.Add(entry.IdempotencyKeySha256)
                || !receiptIds.Add(receipt.ReceiptId)
                || !HasExpectedReceiptId(entry)
                || (previousDelegatedRevision > 0
                    && receipt.PreviousRevision < previousDelegatedRevision)
                || (previousAppliedAtUtc != default
                    && receipt.AppliedAtUtc < previousAppliedAtUtc))
            {
                return false;
            }

            DelegationBinding delegationBinding = new(
                receipt.CampaignId,
                receipt.ActorId,
                receipt.GrantedByCampaignOwnerId,
                receipt.GrantedByCharacterOwnerId);
            if (delegationBindings.TryGetValue(
                    receipt.DelegationId,
                    out DelegationBinding existingDelegationBinding))
            {
                if (existingDelegationBinding.CampaignId != delegationBinding.CampaignId
                    || existingDelegationBinding.ActorId != delegationBinding.ActorId
                    || existingDelegationBinding.GrantedByCampaignOwnerId
                    != delegationBinding.GrantedByCampaignOwnerId
                    || existingDelegationBinding.GrantedByCharacterOwnerId
                    != delegationBinding.GrantedByCharacterOwnerId
                    || receipt.AuthorityRevision
                    < existingDelegationBinding.LastAuthorityRevision)
                {
                    return false;
                }
            }

            delegationBindings[receipt.DelegationId] = delegationBinding with
            {
                LastAuthorityRevision = receipt.AuthorityRevision
            };

            AuthorityReceiptBinding authorityBinding = new(
                receipt.CampaignId,
                receipt.DelegationId,
                receipt.GrantedByCampaignOwnerId,
                receipt.GrantedByCharacterOwnerId,
                receipt.ActorId,
                receipt.AuthorityRevision);
            if (authorityReceiptBindings.TryGetValue(
                    receipt.AuthorityReceiptId,
                    out AuthorityReceiptBinding existingAuthorityBinding)
                && existingAuthorityBinding != authorityBinding)
            {
                return false;
            }

            authorityReceiptBindings[receipt.AuthorityReceiptId] = authorityBinding;
            previousDelegatedRevision = receipt.NewRevision;
            previousAppliedAtUtc = receipt.AppliedAtUtc;
        }

        return true;
    }

    public static bool IsValidForCommit(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        DelegatedGmCharacterEditLedgerEntry? entry)
    {
        return IsStructurallyValid(entry)
            && entry!.Receipt.CharacterId == id
            && string.Equals(
                entry.Receipt.CharacterOwnerId,
                owner.NormalizedValue,
                StringComparison.Ordinal)
            && entry.Receipt.PreviousRevision == expectedContentRevision
            && entry.Receipt.NewRevision == expectedContentRevision + 1;
    }

    public static bool IsValidPersistedEntry(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long currentContentRevision,
        DelegatedGmCharacterEditLedgerEntry? entry)
    {
        return IsStructurallyValid(entry)
            && entry!.Receipt.CharacterId == id
            && string.Equals(
                entry.Receipt.CharacterOwnerId,
                owner.NormalizedValue,
                StringComparison.Ordinal)
            && entry.Receipt.NewRevision <= currentContentRevision;
    }

    public static bool IsStructurallyValid(DelegatedGmCharacterEditLedgerEntry? entry)
    {
        if (entry?.Receipt is not DelegatedGmCharacterEditAuditReceipt receipt
            || !IsSha256(entry.IdempotencyKeySha256)
            || !IsSha256(entry.CommandSha256)
            || !string.Equals(receipt.Contract, DelegatedGmCharacterEditContract.Name, StringComparison.Ordinal)
            || !string.Equals(receipt.IdempotencyKeySha256, entry.IdempotencyKeySha256, StringComparison.Ordinal)
            || !string.Equals(receipt.CommandSha256, entry.CommandSha256, StringComparison.Ordinal)
            || !IsBounded(receipt.ReceiptId, MaximumIdentifierLength)
            || !IsBounded(receipt.CampaignId, MaximumIdentifierLength)
            || !IsBounded(receipt.DelegationId, MaximumIdentifierLength)
            || !IsBounded(receipt.GrantedByCampaignOwnerId, MaximumIdentifierLength)
            || !IsBounded(receipt.GrantedByCharacterOwnerId, MaximumIdentifierLength)
            || !string.Equals(
                receipt.GrantedByCharacterOwnerId,
                receipt.CharacterOwnerId,
                StringComparison.Ordinal)
            || !IsBounded(receipt.AuthorityReceiptId, MaximumIdentifierLength)
            || receipt.AuthorityRevision <= 0
            || !IsBounded(receipt.ActorId, MaximumIdentifierLength)
            || !string.Equals(
                receipt.ActorRole,
                DelegatedGmCharacterEditContract.GameMasterRole,
                StringComparison.Ordinal)
            || !IsBounded(receipt.CharacterOwnerId, MaximumIdentifierLength)
            || new OwnerScope(receipt.CharacterOwnerId).UsesLocalSingleUserValue
            || !IsBounded(receipt.CharacterId.Value, MaximumIdentifierLength)
            || !IsBounded(receipt.Reason, MaximumReasonLength)
            || receipt.PreviousRevision <= 0
            || receipt.NewRevision != receipt.PreviousRevision + 1
            || receipt.AppliedAtUtc == default
            || receipt.Operations.IsDefaultOrEmpty
            || receipt.Operations.Length > MaximumOperationCount)
        {
            return false;
        }

        HashSet<string> paths = new(StringComparer.Ordinal);
        foreach (DelegatedGmCharacterEditAuditOperation operation in receipt.Operations)
        {
            if (operation.Operation != DelegatedGmCharacterPatchOperationKind.Replace
                || !IsAllowedPath(operation.Path)
                || !paths.Add(operation.Path)
                || !IsSha256(operation.ValueSha256)
                || operation.ValueLength < 0
                || operation.ValueLength > MaximumValueLength(operation.Path))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExpectedReceiptId(DelegatedGmCharacterEditLedgerEntry entry)
    {
        DelegatedGmCharacterEditAuditReceipt receipt = entry.Receipt;
        string seed = string.Join(
            "\n",
            entry.CommandSha256,
            receipt.DelegationId,
            receipt.AuthorityReceiptId,
            entry.IdempotencyKeySha256);
        string expectedReceiptId = "gm-edit-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..24].ToLowerInvariant();
        return string.Equals(receipt.ReceiptId, expectedReceiptId, StringComparison.Ordinal);
    }

    public static bool IsSha256(string? value)
    {
        return value is { Length: 64 }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsAllowedPath(string path)
    {
        return path is DelegatedGmCharacterEditContract.ProfileNamePath
            or DelegatedGmCharacterEditContract.ProfileAliasPath
            or DelegatedGmCharacterEditContract.ProfileNotesPath;
    }

    private static int MaximumValueLength(string path)
    {
        return path switch
        {
            DelegatedGmCharacterEditContract.ProfileNamePath => 256,
            DelegatedGmCharacterEditContract.ProfileAliasPath => 256,
            DelegatedGmCharacterEditContract.ProfileNotesPath => 4096,
            _ => -1
        };
    }

    private static bool IsBounded(string? value, int maximumLength)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumLength
            && !value.Any(static character => char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t');
    }

    private readonly record struct DelegationBinding(
        string CampaignId,
        string ActorId,
        string GrantedByCampaignOwnerId,
        string GrantedByCharacterOwnerId,
        long LastAuthorityRevision = 0);

    private readonly record struct AuthorityReceiptBinding(
        string CampaignId,
        string DelegationId,
        string GrantedByCampaignOwnerId,
        string GrantedByCharacterOwnerId,
        string ActorId,
        long AuthorityRevision);
}
