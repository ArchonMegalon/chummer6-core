using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public static class RunnerLibraryCanonical
{
    public const string ReceiptSchema = "chummer.runner-library-mutation-receipt/v1";
    public const int MaximumDisplayNameLength = 200;
    public const int MaximumIdempotencyKeyLength = 256;

    public static bool TryNormalizeDisplayName(string? value, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        return normalized.Length is > 0 and <= MaximumDisplayNameLength
               && normalized.All(character => !char.IsControl(character));
    }

    public static bool TryNormalizeIdempotencyKey(string? value, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        return normalized.Length is > 0 and <= MaximumIdempotencyKeyLength
               && normalized.All(character => !char.IsControl(character));
    }

    public static bool IsSupportedRunnerId(CharacterWorkspaceId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            return false;
        }

        return id.Value.All(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_');
    }

    public static string ComputeIdempotencyKeyDigest(string normalizedIdempotencyKey)
    {
        return ComputeDigest([normalizedIdempotencyKey]);
    }

    public static string ComputeCommandDigest(
        RunnerLibraryMutationKind kind,
        CharacterWorkspaceId runnerId,
        CharacterWorkspaceId? newRunnerId,
        long expectedLifecycleRevision,
        long expectedContentRevision,
        string expectedContentDigestSha256,
        string? displayName,
        string idempotencyKeyDigestSha256)
    {
        return ComputeDigest(
        [
            ((int)kind).ToString(CultureInfo.InvariantCulture),
            runnerId.Value,
            newRunnerId?.Value ?? string.Empty,
            expectedLifecycleRevision.ToString(CultureInfo.InvariantCulture),
            expectedContentRevision.ToString(CultureInfo.InvariantCulture),
            expectedContentDigestSha256,
            displayName ?? string.Empty,
            idempotencyKeyDigestSha256
        ]);
    }

    public static string ComputeContentDigest(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ComputeDigest(
        [
            ((int)document.Format).ToString(CultureInfo.InvariantCulture),
            document.RulesetId,
            document.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            document.PayloadKind,
            document.Content,
            document.AuxiliaryStateDigest
        ]);
    }

    public static string ComputeStateDigest(
        CharacterWorkspaceId runnerId,
        string displayName,
        RunnerLibraryLifecycle lifecycle,
        RunnerLibraryLifecycle? lifecycleBeforeDelete,
        long lifecycleRevision,
        string contentDigestSha256,
        RunnerLibraryProvenance? provenance)
    {
        return ComputeDigest(
        [
            runnerId.Value,
            displayName,
            ((int)lifecycle).ToString(CultureInfo.InvariantCulture),
            lifecycleBeforeDelete is null
                ? string.Empty
                : ((int)lifecycleBeforeDelete.Value).ToString(CultureInfo.InvariantCulture),
            lifecycleRevision.ToString(CultureInfo.InvariantCulture),
            contentDigestSha256,
            provenance?.SourceRunnerId.Value ?? string.Empty,
            provenance?.SourceContentRevision.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            provenance?.SourceContentDigestSha256 ?? string.Empty
        ]);
    }

    public static string ComputeReceiptDigest(RunnerLibraryMutationReceipt receipt)
    {
        return ComputeDigest(
        [
            receipt.Schema,
            ((int)receipt.Kind).ToString(CultureInfo.InvariantCulture),
            receipt.RunnerId.Value,
            receipt.SourceRunnerId?.Value ?? string.Empty,
            receipt.IdempotencyKeyDigestSha256,
            receipt.CommandDigestSha256,
            receipt.BeforeStateDigestSha256,
            receipt.AfterStateDigestSha256,
            receipt.BeforeDisplayName,
            receipt.AfterDisplayName,
            ((int)receipt.BeforeLifecycle).ToString(CultureInfo.InvariantCulture),
            ((int)receipt.AfterLifecycle).ToString(CultureInfo.InvariantCulture),
            receipt.BeforeLifecycleBeforeDelete is null
                ? string.Empty
                : ((int)receipt.BeforeLifecycleBeforeDelete.Value).ToString(
                    CultureInfo.InvariantCulture),
            receipt.AfterLifecycleBeforeDelete is null
                ? string.Empty
                : ((int)receipt.AfterLifecycleBeforeDelete.Value).ToString(
                    CultureInfo.InvariantCulture),
            receipt.BeforeLifecycleRevision.ToString(CultureInfo.InvariantCulture),
            receipt.AfterLifecycleRevision.ToString(CultureInfo.InvariantCulture),
            receipt.BeforeProvenance?.SourceRunnerId.Value ?? string.Empty,
            receipt.BeforeProvenance?.SourceContentRevision.ToString(
                CultureInfo.InvariantCulture) ?? string.Empty,
            receipt.BeforeProvenance?.SourceContentDigestSha256 ?? string.Empty,
            receipt.AfterProvenance?.SourceRunnerId.Value ?? string.Empty,
            receipt.AfterProvenance?.SourceContentRevision.ToString(
                CultureInfo.InvariantCulture) ?? string.Empty,
            receipt.AfterProvenance?.SourceContentDigestSha256 ?? string.Empty,
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture),
            receipt.ContentDigestSha256,
            receipt.CommittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        ]);
    }

    public static bool IsSha256(string? value)
    {
        return value is { Length: 64 }
               && value.All(character => character is >= '0' and <= '9'
                   or >= 'a' and <= 'f');
    }

    public static bool IsValidStoreMutation(RunnerLibraryStoreMutation mutation)
    {
        if (!Enum.IsDefined(mutation.Kind)
            || !IsSupportedRunnerId(mutation.RunnerId)
            || mutation.ExpectedLifecycleRevision <= 0
            || mutation.ExpectedContentRevision <= 0
            || !IsSha256(mutation.ExpectedContentDigestSha256)
            || !IsSha256(mutation.IdempotencyKeyDigestSha256)
            || !IsSha256(mutation.CommandDigestSha256))
        {
            return false;
        }

        bool duplicate = mutation.Kind == RunnerLibraryMutationKind.Duplicate;
        if (duplicate != (mutation.NewRunnerId is not null)
            || (mutation.NewRunnerId is CharacterWorkspaceId target
                && (!IsSupportedRunnerId(target) || target == mutation.RunnerId)))
        {
            return false;
        }

        bool needsDisplayName = mutation.Kind is RunnerLibraryMutationKind.Rename
            or RunnerLibraryMutationKind.Duplicate;
        if (needsDisplayName)
        {
            if (!TryNormalizeDisplayName(mutation.DisplayName, out string normalized)
                || !string.Equals(normalized, mutation.DisplayName, StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if (mutation.DisplayName is not null)
        {
            return false;
        }

        string expectedCommandDigest = ComputeCommandDigest(
            mutation.Kind,
            mutation.RunnerId,
            mutation.NewRunnerId,
            mutation.ExpectedLifecycleRevision,
            mutation.ExpectedContentRevision,
            mutation.ExpectedContentDigestSha256,
            mutation.DisplayName,
            mutation.IdempotencyKeyDigestSha256);
        return string.Equals(
            expectedCommandDigest,
            mutation.CommandDigestSha256,
            StringComparison.Ordinal);
    }

    private static string ComputeDigest(IReadOnlyList<string> fields)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (string field in fields)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(field);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
