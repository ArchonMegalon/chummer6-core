namespace Chummer.Contracts.Characters;

public static class CharacterCreationKarmaFinalizationBudgetBlockers
{
    public const string PolicyUnavailable = "creation-karma-completion-policy-unavailable";
    public const string StartingCashUnavailable = "creation-karma-completion-starting-cash-unavailable";
    public const string BudgetInvalid = "creation-karma-completion-budget-invalid";
}

/// <summary>Profile-owned carryover limits, separate from creation purchasing power.</summary>
public sealed record CharacterCreationKarmaCarryoverPolicy(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    int MaximumKarma,
    decimal MaximumNuyen,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_carryover_policy.v1";

    // A retained receipt compares by value after deserialization. IReadOnlyList
    // otherwise contributes array identity to a record's generated equality.
    public bool Equals(CharacterCreationKarmaCarryoverPolicy? other)
        => ReferenceEquals(this, other) || other is not null
            && Schema == other.Schema && SettingsProfileId == other.SettingsProfileId
            && RawProfileInputsDigest == other.RawProfileInputsDigest
            && MaximumKarma == other.MaximumKarma && MaximumNuyen == other.MaximumNuyen
            && AuthorityDigest == other.AuthorityDigest
            && (ReferenceEquals(SourceAnchorIds, other.SourceAnchorIds)
                || SourceAnchorIds is not null && other.SourceAnchorIds is not null
                    && SourceAnchorIds.SequenceEqual(other.SourceAnchorIds, StringComparer.Ordinal));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Schema, StringComparer.Ordinal);
        hash.Add(SettingsProfileId, StringComparer.Ordinal);
        hash.Add(RawProfileInputsDigest, StringComparer.Ordinal);
        hash.Add(MaximumKarma);
        hash.Add(MaximumNuyen);
        hash.Add(AuthorityDigest, StringComparer.Ordinal);
        if (SourceAnchorIds is not null)
            foreach (string anchor in SourceAnchorIds) hash.Add(anchor, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Source-bound starting-cash terms. Resolving a row does not establish ownership
/// of a purchased lifestyle, apply its effects, or authorize character completion.
/// </summary>
public sealed record CharacterCreationStartingNuyenSource(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    string SourceInputsDigest,
    string SourceId,
    string Name,
    int Dice,
    decimal Multiplier,
    string SourceBook,
    string Page,
    string SourceNodeXml,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_starting_nuyen_source.v1";
}

/// <summary>
/// Read-only financial contribution to Karma completion. Starting cash cannot
/// fund creation purchases. The dice total is an explicit choice, never rolled
/// again during preview, confirmation, or recovery. No Created flag is implied.
/// </summary>
public sealed record CharacterCreationKarmaFinalizationBudgetQuote(
    string Schema,
    string KarmaQuoteDigest,
    CharacterCreationKarmaCarryoverPolicy Policy,
    CharacterCreationStartingNuyenSource StartingCashSource,
    int DiceTotal,
    decimal ResourceKarmaRoundingAdjustment,
    int KarmaBeforeCarryover,
    int KarmaCarried,
    int KarmaDiscarded,
    decimal NuyenBeforeCarryover,
    decimal NuyenCarried,
    decimal NuyenDiscarded,
    decimal LifestyleStartingNuyen,
    decimal CareerNuyen,
    IReadOnlyList<string> SourceAnchorIds,
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_finalization_budget_quote.v1";
}
