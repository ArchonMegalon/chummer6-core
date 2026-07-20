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
}
