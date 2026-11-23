using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerFocusBondingOutcome
{
    Available, Applied, Replayed, NeverApplied, NotFound, IdempotencyConflict,
    Conflict, Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}

public sealed record CharacterCareerFocusBondingRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid GearId,
    bool Bonded, DateTime ExpenseDateLocal);

public sealed record CharacterCareerFocusBondingBinding(
    string OwnerId, bool LocalSingleUser, string OwnerAuthorityInstanceId, long OwnerTransitionRevision,
    CharacterWorkspaceId WorkspaceId, long WorkspaceRevision, string LocalHistoryIncarnationId,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest);

public sealed record CharacterCareerFocusBondingCommand(
    CharacterCareerFocusBondingRequest Request, CharacterCareerFocusBondingBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

public sealed record CharacterCareerFocusBondingState(
    Guid GearId, Guid SourceId, Guid? FocusId, string Name, string Extra, int Force,
    bool Bonded, bool Equipped, int BondedCount, int TotalForce,
    int Magic, int? AdeptMagic, bool IgnoreRules);

public sealed record CharacterCareerFocusBondingQuote(
    CharacterCareerFocusBondingState State, string PriceField, int ProfileMultiplier,
    decimal EffectiveMultiplier, decimal Addition, int BindingKarma,
    int KarmaCost, int KarmaBefore, int KarmaAfter, bool ChangesState,
    string SourceNodeDigest, string OwnedEffectsDigest);

public sealed record CharacterCareerFocusBondingPreview(
    CharacterCareerFocusBondingCommand Command, CharacterCareerFocusBondingQuote Quote, string PreviewDigest);

public sealed record CharacterCareerFocusBondingReceipt(
    CharacterCareerFocusBondingCommand Command, CharacterCareerFocusBondingQuote? Quote,
    string CommandDigest, Guid? ExpenseId, long CommittedWorkspaceRevision,
    string CharacterPayloadDigestAfter, string PreviousReceiptDigest, string ReceiptDigest)
{
    // Missing/false remains an applied receipt. A terminal non-application has no economics.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NeverApplied { get; init; }
}

public sealed record CharacterCareerFocusBondingPreviewResult(
    CharacterCareerFocusBondingOutcome Outcome, CharacterCareerFocusBondingPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerFocusBondingResult(
    CharacterCareerFocusBondingOutcome Outcome, CharacterCareerFocusBondingReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerFocusBondingEntry(Guid GearId, string Name, int Force, bool Bonded,
    string? UnsupportedReason = null);
public sealed record CharacterCareerFocusBondingListResult(CharacterCareerFocusBondingOutcome Outcome,
    IReadOnlyList<CharacterCareerFocusBondingEntry>? Entries = null, long? CurrentWorkspaceRevision = null, string? Error = null);
