using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCreationLifestyleDeleteIdentity(Guid LifestyleId);

public sealed record CharacterCreationLifestyleDeleteEconomics(
    decimal NuyenDelta,
    int ExpenseRecordDelta,
    bool RemovesLifestyleCost);

public sealed record CharacterCreationLifestyleDeleteState(
    CharacterCreationLifestyleDeleteIdentity Identity,
    bool Created,
    string DisplayName,
    int LifestyleQualityCount,
    int LinkedImprovementCount,
    string Revision,
    CharacterCreationLifestyleDeleteEconomics Economics)
{
    public bool CanDelete => !Created;
}

/// <summary>
/// Exact Creation authority for CharacterCreate.cmdDeleteLifestyle. Lifestyle.RemoveAsync removes the
/// selected Lifestyle and Improvements sourced by its LifestyleQuality identities. The handler neither
/// changes Nuyen nor appends an expense; removing the Lifestyle removes its monthly cost from derived totals.
/// </summary>
public static class CharacterCreationLifestyleDeleteRules
{
    public const int RevisionHexLength = 64;

    public static bool IsValidIdentity(CharacterCreationLifestyleDeleteIdentity? identity)
        => identity is { LifestyleId: var lifestyleId } && lifestyleId != Guid.Empty;

    public static bool TryCreateState(
        CharacterCreationLifestyleDeleteIdentity? identity,
        bool created,
        string? displayName,
        int lifestyleQualityCount,
        int linkedImprovementCount,
        string? lifestyleState,
        string? improvementState,
        out CharacterCreationLifestyleDeleteState state)
    {
        state = Unavailable();
        if (!IsValidIdentity(identity)
            || displayName is null
            || lifestyleQualityCount < 0
            || linkedImprovementCount < 0
            || lifestyleState is null
            || improvementState is null)
        {
            return false;
        }

        CharacterCreationLifestyleDeleteEconomics economics = new(
            NuyenDelta: 0m,
            ExpenseRecordDelta: 0,
            RemovesLifestyleCost: true);
        state = new CharacterCreationLifestyleDeleteState(
            identity!,
            created,
            displayName,
            lifestyleQualityCount,
            linkedImprovementCount,
            CalculateRevision(
                identity!,
                created,
                displayName,
                lifestyleQualityCount,
                linkedImprovementCount,
                lifestyleState,
                improvementState),
            economics);
        return true;
    }

    public static bool CanDelete(
        CharacterCreationLifestyleDeleteState? current,
        CharacterCreationLifestyleDeleteIdentity? identity,
        string? expectedRevision,
        bool confirmed)
        => confirmed
            && current is not null
            && identity is not null
            && IsValidIdentity(identity)
            && current.Identity == identity
            && current.CanDelete
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Economics is { NuyenDelta: 0m, ExpenseRecordDelta: 0 };

    private static string CalculateRevision(
        CharacterCreationLifestyleDeleteIdentity identity,
        bool created,
        string displayName,
        int lifestyleQualityCount,
        int linkedImprovementCount,
        string lifestyleState,
        string improvementState)
    {
        string payload = string.Join('\0',
            identity.LifestyleId.ToString("D"),
            created.ToString(),
            displayName,
            lifestyleQualityCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            linkedImprovementCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            lifestyleState,
            improvementState);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static CharacterCreationLifestyleDeleteState Unavailable()
        => new(
            new CharacterCreationLifestyleDeleteIdentity(Guid.Empty),
            Created: true,
            string.Empty,
            0,
            0,
            string.Empty,
            new CharacterCreationLifestyleDeleteEconomics(0m, 0, false));
}
