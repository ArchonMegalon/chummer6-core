using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Shared raw saved-value invariants for Chummer5 Matrix attribute combo handlers.
/// Typed equipment authorities remain responsible for identity and XML ownership.
/// </summary>
internal static class CharacterMatrixPermutationAuthority
{
    internal const int RevisionHexLength = 64;

    internal static bool HasExactRawState(
        string? attack,
        string? sleaze,
        string? dataProcessing,
        string? firewall,
        string? attributeArray,
        bool canSwapAttributes)
        => canSwapAttributes
            && !string.IsNullOrEmpty(attack)
            && !string.IsNullOrEmpty(sleaze)
            && !string.IsNullOrEmpty(dataProcessing)
            && !string.IsNullOrEmpty(firewall)
            && !string.IsNullOrEmpty(attributeArray);

    internal static bool TryValidatePermutation(
        string? currentRevision,
        string? expectedRevision,
        string? changedValue,
        string? targetValue,
        string? attributeArray,
        bool canSwapAttributes)
        => canSwapAttributes
            && !string.IsNullOrEmpty(attributeArray)
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal)
            && !string.Equals(changedValue, targetValue, StringComparison.Ordinal);

    internal static string CalculateRevision(
        Guid rootId,
        string phase,
        string attack,
        string sleaze,
        string dataProcessing,
        string firewall,
        string attributeArray,
        bool canSwapAttributes)
    {
        string payload = string.Join("\0", rootId.ToString("D"), phase,
            attack, sleaze, dataProcessing, firewall, attributeArray,
            canSwapAttributes ? "1" : "0");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
