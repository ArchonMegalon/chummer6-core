using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public static class AiResponseCacheKeys
{
    public static AiResponseCacheLookup CreateLookup(
        string routeType,
        string? message,
        string? runtimeFingerprint,
        string? characterId,
        IReadOnlyList<string>? attachmentIds = null,
        string? workspaceId = null)
        => new(
            RouteType: NormalizeRequired(routeType).ToLowerInvariant(),
            NormalizedPrompt: NormalizePrompt(message),
            RuntimeFingerprint: NormalizeOptional(runtimeFingerprint),
            CharacterId: NormalizeOptional(characterId),
            AttachmentKey: NormalizeAttachmentKey(attachmentIds),
            WorkspaceId: NormalizeOptional(workspaceId));

    public static string CreateCacheKey(AiResponseCacheLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        string material = string.Join(
            "\n",
            lookup.RouteType,
            lookup.RuntimeFingerprint ?? string.Empty,
            lookup.CharacterId ?? string.Empty,
            lookup.WorkspaceId ?? string.Empty,
            lookup.AttachmentKey ?? string.Empty,
            lookup.NormalizedPrompt);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static string NormalizePrompt(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        string[] tokens = message
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', tokens).ToLowerInvariant();
    }

    public static string? NormalizeAttachmentKey(IReadOnlyList<string>? attachmentIds)
    {
        if (attachmentIds is not { Count: > 0 })
        {
            return null;
        }

        string[] normalized = attachmentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeRequired)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return normalized.Length == 0
            ? null
            : string.Join('|', normalized);
    }

    public static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    public static string NormalizeRequired(string value)
        => NormalizeOptional(value)
            ?? throw new ArgumentException("Value is required.", nameof(value));
}
