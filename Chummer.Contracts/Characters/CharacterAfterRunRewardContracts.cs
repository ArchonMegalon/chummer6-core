using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterAfterRunRewardKind
{
    Award = 0,
    NoAward = 1
}

public enum CharacterAfterRunRewardOutcome
{
    Available,
    Applied,
    Replayed,
    NotFound,
    IdempotencyConflict,
    Conflict,
    Corrupt,
    Unavailable,
    Missing
}

/// <summary>
/// Exact saved expense identity and editable fields. A local association of an
/// existing positive ManualAdd entry does not certify its original provenance,
/// GM approval, or whether an earlier operation also exchanged another currency.
/// </summary>
public sealed record CharacterAfterRunRewardExpense(
    Guid ExpenseId,
    string Type,
    DateTime ExpenseDateLocal,
    decimal Amount,
    string Reason,
    bool Refund,
    bool ForceCareerVisible,
    string? KarmaUndoType,
    string? NuyenUndoType,
    string? UndoObjectId,
    decimal? UndoQuantity,
    string? UndoExtra);

/// <summary>
/// SourceDigest binds the complete saved character payload and its format,
/// ruleset, schema and payload kind, not a rulebook or remotely signed run.
/// Expenses includes an authoritative empty collection.
/// </summary>
public sealed record CharacterAfterRunRewardSnapshot(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string SourceDigest,
    string AuxiliaryStateDigest,
    int AvailableKarma,
    decimal AvailableNuyen,
    IReadOnlyList<CharacterAfterRunRewardExpense> Expenses);

public sealed record CharacterAfterRunRewardReadResult(
    CharacterAfterRunRewardOutcome Outcome,
    CharacterAfterRunRewardSnapshot? Snapshot = null,
    string? Error = null);

/// <summary>
/// Unconfirmed local intent. Core derives all saved-source bindings.
/// ExpenseDateLocal is an explicit wall-clock value: Unspecified kind and whole
/// seconds only. The host chooses this value before Preview; Core never silently
/// converts timezones, relabels UTC/local values, or truncates quoted precision.
/// </summary>
public sealed record CharacterAfterRunRewardPreviewRequest(
    CharacterWorkspaceId WorkspaceId,
    Guid OperationId,
    Guid RewardId,
    int KarmaAmount,
    int NuyenAmount,
    DateTime ExpenseDateLocal,
    string Reason,
    Guid? ExistingKarmaExpenseId = null,
    Guid? ExistingNuyenExpenseId = null,
    CharacterAfterRunRewardKind Kind = CharacterAfterRunRewardKind.Award);

/// <summary>
/// Read-only, unreserved Core quote. Command remains unconfirmed. After showing
/// these exact outcomes and obtaining local confirmation, the host sets only
/// ExplicitlyConfirmed=true and durably persists the complete command before
/// Commit. The fingerprint binds this quote, not server or actor authorization.
/// </summary>
public sealed record CharacterAfterRunRewardPreview(
    CharacterAfterRunRewardCommand Command,
    int KarmaBefore,
    int KarmaAfter,
    decimal NuyenBefore,
    decimal NuyenAfter,
    Guid? KarmaExpenseId,
    Guid? NuyenExpenseId,
    bool KarmaAlreadyRecorded,
    bool NuyenAlreadyRecorded,
    string PreviewDigest)
{
    public CharacterAfterRunRewardKind Kind => Command.Kind;
}

public sealed record CharacterAfterRunRewardPreviewResult(
    CharacterAfterRunRewardOutcome Outcome,
    CharacterAfterRunRewardPreview? Preview = null,
    long? CurrentWorkspaceRevision = null,
    string? Error = null);

/// <summary>
/// Compact immutable transaction evidence. SourceDigest and revision bind the
/// complete original document; SelectedExpenses contains only explicitly reused
/// entries (at most two) and is not an authoritative expense-ledger snapshot.
/// Collision checks and append authorization require the full live projection.
/// </summary>
public sealed record CharacterAfterRunRewardBeforeEvidence(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string SourceDigest,
    string AuxiliaryStateDigest,
    int AvailableKarma,
    decimal AvailableNuyen,
    IReadOnlyList<CharacterAfterRunRewardExpense> SelectedExpenses);

/// <summary>
/// A locally confirmed offline manual reward or association. RewardId identifies
/// the caller-owned logical reward; OperationId identifies this complete command.
/// The host must durably persist both IDs and the complete command before Commit,
/// retaining them unchanged for unknown-result lookup and retries. Exactly-once
/// handling is scoped to those persistent identities; deliberately granting a
/// new manual reward under new IDs remains possible. These fields establish no
/// server, actor, signed GM, or authenticated run authority.
/// ExpectedPreviewDigest must retain the genuine Core preview fingerprint;
/// confirmation changes only ExplicitlyConfirmed, never the previewed inputs.
/// ExpenseDateLocal must retain the preview's Unspecified whole-second value,
/// whose JSON representation is independent of the device's current timezone.
/// NoAward is an explicit zero-currency decision: both amounts and selections
/// must be empty. Its confirmed receipt changes no balances or expense entries.
/// </summary>
public sealed record CharacterAfterRunRewardCommand(
    CharacterWorkspaceId WorkspaceId,
    Guid OperationId,
    Guid RewardId,
    long ExpectedWorkspaceRevision,
    string ExpectedSourceDigest,
    string ExpectedAuxiliaryStateDigest,
    int KarmaAmount,
    int NuyenAmount,
    DateTime ExpenseDateLocal,
    string Reason,
    Guid? ExistingKarmaExpenseId = null,
    Guid? ExistingNuyenExpenseId = null,
    bool ExplicitlyConfirmed = false,
    string? ExpectedPreviewDigest = null,
    CharacterAfterRunRewardKind Kind = CharacterAfterRunRewardKind.Award)
{
    public string CommandDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "chummer.core.sr5-after-run-reward-command/v1\0" + JsonSerializer.Serialize(this))));
}

public sealed record CharacterAfterRunRewardReceipt(
    Guid OperationId,
    Guid RewardId,
    string CommandDigest,
    long ExpectedWorkspaceRevision,
    long CommittedWorkspaceRevision,
    int KarmaBefore,
    int KarmaAfter,
    decimal NuyenBefore,
    decimal NuyenAfter,
    Guid? KarmaExpenseId,
    Guid? NuyenExpenseId,
    CharacterAfterRunRewardCommand Command,
    CharacterAfterRunRewardBeforeEvidence Before,
    string CharacterPayloadDigestAfter,
    string ReceiptDigest,
    string Schema = "chummer.core.sr5-after-run-reward-receipt/v1")
{
    public CharacterAfterRunRewardKind Kind => Command.Kind;
}

public sealed record CharacterAfterRunRewardResult(
    CharacterAfterRunRewardOutcome Outcome,
    CharacterAfterRunRewardReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null,
    string? Error = null);
