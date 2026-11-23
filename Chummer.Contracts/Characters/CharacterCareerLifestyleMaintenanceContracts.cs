using Chummer.Contracts.Workspaces;
using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerLifestyleMaintenanceKind { Create, Edit, Delete }
public enum CharacterCareerLifestyleMaintenanceOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict, Conflict,
    Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}

// No prices, paid-interval count, expense date or Nuyen adjustment can be supplied by a caller.
public sealed record CharacterCareerLifestyleMaintenanceRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid LifestyleId,
    CharacterCareerLifestyleMaintenanceKind Kind, string BaseLifestyleOptionId,
    string Name, decimal Percentage = 100m, int Roommates = 0,
    bool SplitCostWithRoommates = false, bool TrustFund = false)
{
    // Selection only. The service checks identity/count against the saved target and quotes all economics.
    // Absence retains the original command JSON and preserves the saved configuration on basic edits.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLifestyleConfiguration? Configuration { get; init; }
}
public sealed record CharacterCareerLifestyleMaintenanceBinding(
    string OwnerId, CharacterWorkspaceId WorkspaceId, long WorkspaceRevision,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest, string IncarnationId);
public sealed record CharacterCareerLifestyleMaintenanceCommand(
    CharacterCareerLifestyleMaintenanceRequest Request, CharacterCareerLifestyleMaintenanceBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);
public sealed record CharacterCareerLifestyleMaintenanceQuote(
    Guid SourceId, string BaseLifestyleName, string SourceBook, string Page,
    string Name, decimal BaseCost, decimal CostPerInterval, int Increments,
    decimal? PreviousCostPerInterval, bool CoveredByTrustFund)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLifestyleProjection? Projection { get; init; }
}
public sealed record CharacterCareerLifestyleMaintenancePreview(
    CharacterCareerLifestyleMaintenanceCommand Command, CharacterCareerLifestyleMaintenanceQuote Quote, string PreviewDigest);
public sealed record CharacterCareerLifestyleMaintenanceReceipt(
    CharacterCareerLifestyleMaintenanceCommand Command, CharacterCareerLifestyleMaintenanceQuote Quote,
    string CommandDigest, long CommittedWorkspaceRevision, string CharacterPayloadDigestAfter,
    string PreviousReceiptDigest, string ReceiptDigest);
public sealed record CharacterCareerLifestyleMaintenancePreviewResult(
    CharacterCareerLifestyleMaintenanceOutcome Outcome, CharacterCareerLifestyleMaintenancePreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
public sealed record CharacterCareerLifestyleMaintenanceResult(
    CharacterCareerLifestyleMaintenanceOutcome Outcome, CharacterCareerLifestyleMaintenanceReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
public sealed record CharacterCareerLifestyleMaintenanceEntry(
    Guid LifestyleId, string Name, CharacterCreationLifestyleConfiguration? Configuration,
    decimal? CostPerInterval, string? UnavailableReason);
public sealed record CharacterCareerLifestyleMaintenanceState(
    IReadOnlyList<CharacterCreationLifestyleCatalogOption> Options,
    IReadOnlyList<CharacterCareerLifestyleMaintenanceEntry> Entries, int TrustFundLevel)
{
    public CharacterCreationLifestylesAuthority? Authority { get; init; }
}
public sealed record CharacterCareerLifestyleMaintenanceLoadResult(
    CharacterCareerLifestyleMaintenanceOutcome Outcome, CharacterCareerLifestyleMaintenanceState? State = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
