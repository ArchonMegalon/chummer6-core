using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

/// <summary>Core-owned natural advancement. Raw XML projections cannot issue this binding.</summary>
public sealed record Sr6CareerAttributeQuote(
    string Schema, string OwnerId, CharacterWorkspaceId WorkspaceId, long WorkspaceRevision,
    string SourceDigest, string RuleDigest, string AttributeId,
    int NaturalRating, int TargetNaturalRating, int AugmentationBonus, int NaturalMaximum,
    int AvailableKarma, int KarmaCost, string? Blocker, IReadOnlyList<string> SourceAnchorIds,
    string BindingDigest)
{
    public bool CanAdvance => Blocker is null;
    public int EffectiveRating => NaturalRating + AugmentationBonus;
}

/// <summary>Keep this exact command and operation ID after a lost or uncertain response.</summary>
public sealed record Sr6CareerAttributeCommand(Sr6CareerAttributeQuote Quote, Guid OperationId,
    DateTime ExpenseDateLocal, bool ExplicitlyConfirmed);

public sealed record Sr6CareerAttributeReceipt(Sr6CareerAttributeCommand Command,
    long CommittedRevision, int KarmaAfter, Guid ExpenseId, int ExpenseAmount, string ReceiptDigest);

public enum Sr6CareerAttributeOutcome
{
    Available, Applied, Replayed, Blocked, Conflict, IdempotencyConflict, Missing, Unavailable, Corrupt, NotFound
}

public sealed record Sr6CareerAttributeResult(Sr6CareerAttributeOutcome Outcome,
    Sr6CareerAttributeQuote? Quote = null, Sr6CareerAttributeReceipt? Receipt = null, string? Error = null);

public static class Sr6CareerAttributeSchemas
{
    public const string QuoteV1 = "chummer.core.sr6-career-attribute-quote/v1";
}
