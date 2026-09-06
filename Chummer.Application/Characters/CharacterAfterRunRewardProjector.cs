using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Projects saved expense facts and composes only the confirmed reward changes.
/// New awards reuse Core's manual gain quotes with exchange disabled. Existing
/// entries are explicitly associated local evidence and never change balances.
/// </summary>
public static class CharacterAfterRunRewardProjector
{
    public const string ReceiptSchema = "chummer.core.sr5-after-run-reward-receipt/v1";
    public const long MaximumCharacterXmlLength = 67_108_864;
    private static readonly DateTime MinimumDate = new(1753, 1, 1);
    private static readonly DateTime MaximumDate = new(9998, 12, 31, 23, 59, 59);

    public static bool IsValidWorkspaceId(CharacterWorkspaceId workspaceId)
        => CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(workspaceId);

    public static bool IsDigest(string? value)
        => value is { Length: 64 }
           && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string PayloadDigest(string payload)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public static string SourceDigest(WorkspaceDocument document)
        => PayloadDigest("chummer.core.sr5-after-run-reward-source/v1\0" + JsonSerializer.Serialize(new
        {
            document.Format,
            document.RulesetId,
            document.SchemaVersion,
            document.PayloadKind,
            CharacterPayloadDigest = PayloadDigest(document.Content)
        }));

    public static bool IsSupportedDocument(WorkspaceDocument document)
        => document.Format == WorkspaceDocumentFormat.NativeXml
           && string.Equals(document.RulesetId, "sr5", StringComparison.Ordinal)
           && document.SchemaVersion == 1
           && string.Equals(document.PayloadKind, "workspace", StringComparison.Ordinal);

    public static string ReceiptDigest(CharacterAfterRunRewardReceipt receipt)
        => PayloadDigest(ReceiptSchema + "\0" + JsonSerializer.Serialize(
            receipt with { ReceiptDigest = string.Empty }));

    /// <summary>
    /// Canonical quote fingerprint. Confirmation and both copies of this
    /// fingerprint are excluded to avoid a cycle. All input/source bindings and
    /// displayed outcomes remain bound. This is not a signature or permission.
    /// </summary>
    public static string ComputePreviewDigest(CharacterAfterRunRewardPreview preview)
        => PayloadDigest("chummer.core.sr5-after-run-reward-preview/v1\0" + JsonSerializer.Serialize(
            preview with
            {
                Command = preview.Command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null },
                PreviewDigest = string.Empty
            }));

    public static bool CanonicalEquals<T>(T first, T second)
        => string.Equals(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second), StringComparison.Ordinal);

    public static bool IsValidCommand(CharacterAfterRunRewardCommand? command)
        => command is not null && IsValidCommandData(command) && command.ExplicitlyConfirmed
           && IsDigest(command.ExpectedPreviewDigest);

    /// <summary>
    /// Bounded input-only admission before any store read or JSON/hash allocation.
    /// Source bindings are intentionally absent and remain Core-derived.
    /// </summary>
    public static bool IsValidPreviewRequest(CharacterAfterRunRewardPreviewRequest? request)
        => request is not null && IsValidIntentFields(
            request.WorkspaceId, request.OperationId, request.RewardId, request.KarmaAmount, request.NuyenAmount,
            request.ExpenseDateLocal, request.Reason, request.ExistingKarmaExpenseId,
            request.ExistingNuyenExpenseId, request.Kind);

    private static bool IsValidCommandData(CharacterAfterRunRewardCommand? command)
        => command is not null
           && IsValidIntentFields(command.WorkspaceId, command.OperationId, command.RewardId,
               command.KarmaAmount, command.NuyenAmount, command.ExpenseDateLocal, command.Reason,
               command.ExistingKarmaExpenseId, command.ExistingNuyenExpenseId, command.Kind)
           && command.ExpectedWorkspaceRevision is > 0 and < long.MaxValue
           && IsDigest(command.ExpectedSourceDigest)
           && IsDigest(command.ExpectedAuxiliaryStateDigest);

    private static bool IsValidIntentFields(
        CharacterWorkspaceId workspaceId, Guid operationId, Guid rewardId,
        int karmaAmount, int nuyenAmount, DateTime expenseDateLocal, string? reason,
        Guid? existingKarmaExpenseId, Guid? existingNuyenExpenseId, CharacterAfterRunRewardKind kind)
        => reason is { Length: <= CharacterCareerManualKarmaRules.MaximumReasonLength }
           && IsValidWorkspaceId(workspaceId)
           && operationId != Guid.Empty && rewardId != Guid.Empty
           && karmaAmount is >= 0 and <= CharacterCareerManualKarmaRules.MaximumAmount
           && nuyenAmount is >= 0 and <= CharacterCareerManualNuyenRules.MaximumAmount
           && (kind switch
           {
               CharacterAfterRunRewardKind.Award => karmaAmount > 0 || nuyenAmount > 0,
               CharacterAfterRunRewardKind.NoAward => karmaAmount == 0 && nuyenAmount == 0
                   && existingKarmaExpenseId is null && existingNuyenExpenseId is null,
               _ => false
           })
           && expenseDateLocal.Kind == DateTimeKind.Unspecified
           && expenseDateLocal.Ticks % TimeSpan.TicksPerSecond == 0
           && expenseDateLocal >= MinimumDate && expenseDateLocal <= MaximumDate
           && existingKarmaExpenseId != Guid.Empty && existingNuyenExpenseId != Guid.Empty
           && (karmaAmount != 0 || existingKarmaExpenseId is null)
           && (nuyenAmount != 0 || existingNuyenExpenseId is null);

    public static bool TryRead(
        WorkspaceStoredDocument saved,
        out CharacterAfterRunRewardSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        error = "reward_workspace_corrupt";
        if (!IsValidWorkspaceId(saved.Id) || saved.ContentRevision <= 0)
            return false;
        if (saved.SavedRevision != saved.ContentRevision)
        {
            error = "reward_workspace_not_clean";
            return false;
        }
        if (!IsSupportedDocument(saved.Document))
        {
            error = "reward_workspace_not_sr5";
            return false;
        }
        try
        {
            XElement root = Parse(saved.Document.Content).Root!;
            if (!ReadBool(root, "created", required: true))
            {
                error = "reward_workspace_not_career";
                return false;
            }
            snapshot = new CharacterAfterRunRewardSnapshot(
                saved.Id,
                saved.ContentRevision,
                saved.SavedRevision,
                SourceDigest(saved.Document),
                saved.Document.AuxiliaryStateDigest,
                ReadInt(root, "karma"),
                ReadDecimal(root, "nuyen", required: true),
                Array.AsReadOnly(ReadExpenses(root)));
            if (!IsValidSnapshot(snapshot))
            {
                snapshot = null!;
                return false;
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsProjectionFailure(exception))
        {
            snapshot = null!;
            return false;
        }
    }

    public static bool IsValidSnapshot(CharacterAfterRunRewardSnapshot? snapshot)
    {
        if (snapshot is null || !IsValidWorkspaceId(snapshot.WorkspaceId)
            || snapshot.ContentRevision <= 0 || snapshot.SavedRevision != snapshot.ContentRevision
            || !IsDigest(snapshot.SourceDigest) || !IsDigest(snapshot.AuxiliaryStateDigest)
            || snapshot.Expenses is null)
            return false;
        HashSet<Guid> identities = [];
        return snapshot.Expenses.All(expense => IsValidExpense(expense) && identities.Add(expense.ExpenseId));
    }

    public static bool IsValidBeforeEvidence(CharacterAfterRunRewardBeforeEvidence? evidence)
    {
        if (evidence is null || !IsValidWorkspaceId(evidence.WorkspaceId)
            || evidence.ContentRevision <= 0 || evidence.SavedRevision != evidence.ContentRevision
            || !IsDigest(evidence.SourceDigest) || !IsDigest(evidence.AuxiliaryStateDigest)
            || evidence.SelectedExpenses is null || evidence.SelectedExpenses.Count > 2)
            return false;
        HashSet<Guid> identities = [];
        return evidence.SelectedExpenses.All(expense => IsValidExpense(expense) && identities.Add(expense.ExpenseId));
    }

    public static bool TryBuild(
        WorkspaceStoredDocument saved,
        CharacterAfterRunRewardCommand command,
        out WorkspaceDocument replacement,
        out CharacterAfterRunRewardReceipt receipt,
        out string error)
    {
        replacement = null!;
        receipt = null!;
        error = "reward_command_invalid";
        return IsValidCommand(command)
               && TryPrepare(saved, command, out replacement, out receipt, out _, out error);
    }

    /// <summary>
    /// Pure preparation, including prospective output and receipt capacity.
    /// No receipt or replacement escapes this preview boundary and no write or
    /// reservation occurs. Public input must remain genuinely unconfirmed.
    /// </summary>
    public static bool TryPreview(
        WorkspaceStoredDocument saved,
        CharacterAfterRunRewardCommand command,
        out CharacterAfterRunRewardPreview preview,
        out string error)
    {
        preview = null!;
        error = "reward_command_invalid";
        return command is { ExplicitlyConfirmed: false, ExpectedPreviewDigest: null or "" }
               && TryPrepare(saved, command, out _, out _, out preview, out error);
    }

    private static bool TryPrepare(
        WorkspaceStoredDocument saved,
        CharacterAfterRunRewardCommand command,
        out WorkspaceDocument replacement,
        out CharacterAfterRunRewardReceipt receipt,
        out CharacterAfterRunRewardPreview preview,
        out string error)
    {
        replacement = null!;
        receipt = null!;
        preview = null!;
        error = "reward_command_invalid";
        if (!IsValidCommandData(command) || !TryRead(saved, out var before, out error))
            return false;
        if (!Matches(command, before))
        {
            error = "reward_binding_conflict";
            return false;
        }
        IReadOnlyList<CharacterAfterRunRewardReceipt>? existing = saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts;
        if (!CharacterAfterRunRewardReceiptLedgerIntegrity.IsValidLedger(saved.Id, saved.ContentRevision, existing))
        {
            error = "reward_receipt_ledger_corrupt";
            return false;
        }
        if (existing is { Count: >= CharacterAfterRunRewardReceiptLedgerIntegrity.MaximumEntries })
        {
            error = "reward_receipt_capacity_exhausted";
            return false;
        }
        if (existing?.Any(item => item.OperationId == command.OperationId
                || item.RewardId == command.RewardId) == true)
        {
            error = "reward_already_associated";
            return false;
        }
        if (!TryEvaluate(command, before, out int karmaAfter, out decimal nuyenAfter,
                out Guid? karmaId, out Guid? nuyenId, out error))
            return false;
        if (existing?.Any(item => IsAssociated(item, karmaId) || IsAssociated(item, nuyenId)) == true)
        {
            error = "reward_expense_already_associated";
            return false;
        }

        CharacterAfterRunRewardPreview preparedPreview = CreatePreview(command,
            before.AvailableKarma, karmaAfter, before.AvailableNuyen, nuyenAfter, karmaId, nuyenId);
        if (command.ExplicitlyConfirmed
            && !string.Equals(command.ExpectedPreviewDigest, preparedPreview.PreviewDigest, StringComparison.Ordinal))
        {
            error = "reward_preview_binding_conflict";
            return false;
        }

        try
        {
            XDocument document = Parse(saved.Document.Content);
            XElement root = document.Root!;
            if (command.KarmaAmount > 0 && command.ExistingKarmaExpenseId is null)
            {
                SetNumber(root, "karma", karmaAfter.ToString(CultureInfo.InvariantCulture));
                AddExpense(root, NewExpense(command, "Karma", karmaId!.Value));
            }
            if (command.NuyenAmount > 0 && command.ExistingNuyenExpenseId is null)
            {
                SetNumber(root, "nuyen", nuyenAfter.ToString(CultureInfo.InvariantCulture));
                AddExpense(root, NewExpense(command, "Nuyen", nuyenId!.Value));
            }
            // Pure association and explicit NoAward preserve the exact character
            // payload, including formatting; only receipt/auxiliary state changes.
            bool hasNewGain = (command.KarmaAmount > 0 && command.ExistingKarmaExpenseId is null)
                              || (command.NuyenAmount > 0 && command.ExistingNuyenExpenseId is null);
            string payload = hasNewGain ? document.ToString(SaveOptions.DisableFormatting) : saved.Document.Content;
            if (payload.Length > MaximumCharacterXmlLength)
            {
                error = "reward_output_size_exceeded";
                return false;
            }
            var evidence = new CharacterAfterRunRewardBeforeEvidence(
                before.WorkspaceId, before.ContentRevision, before.SavedRevision,
                before.SourceDigest, before.AuxiliaryStateDigest,
                before.AvailableKarma, before.AvailableNuyen,
                Array.AsReadOnly(before.Expenses.Where(expense =>
                    expense.ExpenseId == command.ExistingKarmaExpenseId
                    || expense.ExpenseId == command.ExistingNuyenExpenseId).ToArray()));
            // This is prospective serialization for capacity admission, not an
            // authorization decision. Preview never publishes this scratch
            // receipt. Only TryBuild's confirmed, fingerprint-checked boundary
            // can return it to the service's atomic commit path.
            CharacterAfterRunRewardCommand committedCommand = preparedPreview.Command with { ExplicitlyConfirmed = true };
            var unsigned = new CharacterAfterRunRewardReceipt(
                command.OperationId, command.RewardId, committedCommand.CommandDigest(),
                before.ContentRevision, checked(before.ContentRevision + 1),
                before.AvailableKarma, karmaAfter, before.AvailableNuyen, nuyenAfter,
                karmaId, nuyenId, committedCommand, evidence, PayloadDigest(payload), string.Empty);
            receipt = unsigned with { ReceiptDigest = ReceiptDigest(unsigned) };
            CharacterAfterRunRewardReceipt[] ledger = [.. existing ?? [], receipt];
            if (!CharacterAfterRunRewardReceiptLedgerIntegrity.TryMeasureLedgerUtf8Bytes(ledger, out _))
            {
                receipt = null!;
                error = "reward_receipt_capacity_exhausted";
                return false;
            }
            replacement = saved.Document with
            {
                State = saved.Document.State with
                {
                    Payload = payload,
                    AuxiliaryState = saved.Document.AuxiliaryState with
                    {
                        CharacterAfterRunRewardReceipts = Array.AsReadOnly(ledger)
                    }
                }
            };
            preview = preparedPreview;
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsProjectionFailure(exception))
        {
            replacement = null!;
            receipt = null!;
            error = "reward_projection_failed";
            return false;
        }
    }

    public static bool Matches(CharacterAfterRunRewardCommand command, CharacterAfterRunRewardSnapshot before)
        => command.WorkspaceId == before.WorkspaceId
           && command.ExpectedWorkspaceRevision == before.ContentRevision
           && string.Equals(command.ExpectedSourceDigest, before.SourceDigest, StringComparison.Ordinal)
           && string.Equals(command.ExpectedAuxiliaryStateDigest, before.AuxiliaryStateDigest, StringComparison.Ordinal);

    private static bool Matches(CharacterAfterRunRewardCommand command, CharacterAfterRunRewardBeforeEvidence before)
        => command.WorkspaceId == before.WorkspaceId
           && command.ExpectedWorkspaceRevision == before.ContentRevision
           && string.Equals(command.ExpectedSourceDigest, before.SourceDigest, StringComparison.Ordinal)
           && string.Equals(command.ExpectedAuxiliaryStateDigest, before.AuxiliaryStateDigest, StringComparison.Ordinal);

    internal static CharacterAfterRunRewardPreview CreatePreview(
        CharacterAfterRunRewardCommand command,
        int karmaBefore, int karmaAfter, decimal nuyenBefore, decimal nuyenAfter,
        Guid? karmaExpenseId, Guid? nuyenExpenseId)
    {
        var unsigned = new CharacterAfterRunRewardPreview(
            command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null },
            karmaBefore, karmaAfter, nuyenBefore, nuyenAfter, karmaExpenseId, nuyenExpenseId,
            command.ExistingKarmaExpenseId.HasValue, command.ExistingNuyenExpenseId.HasValue, string.Empty);
        string digest = ComputePreviewDigest(unsigned);
        return unsigned with
        {
            Command = unsigned.Command with { ExpectedPreviewDigest = digest },
            PreviewDigest = digest
        };
    }

    /// <summary>
    /// Checks a detached quote's internal data, calculations, identities and
    /// fingerprint. It does not prove current source, selected-entry existence,
    /// receipt capacity, atomic availability, or local human confirmation.
    /// Those are re-evaluated against the complete saved document by the service.
    /// </summary>
    public static bool IsCoherentPreview(CharacterAfterRunRewardPreview? preview)
    {
        if (preview is null || !IsValidCommandData(preview.Command)
            || preview.Command.ExplicitlyConfirmed
            || !IsDigest(preview.PreviewDigest)
            || preview.Command.ExpectedPreviewDigest != preview.PreviewDigest
            || !TryQuoteBalances(preview.Command, preview.KarmaBefore, preview.NuyenBefore,
                out int karmaAfter, out decimal nuyenAfter, out _))
            return false;
        Guid? karmaId = preview.Command.KarmaAmount == 0 ? null
            : preview.Command.ExistingKarmaExpenseId ?? ExpenseIdentity(preview.Command, "Karma");
        Guid? nuyenId = preview.Command.NuyenAmount == 0 ? null
            : preview.Command.ExistingNuyenExpenseId ?? ExpenseIdentity(preview.Command, "Nuyen");
        return (karmaId is null || karmaId != nuyenId)
               && preview.KarmaAfter == karmaAfter && preview.NuyenAfter == nuyenAfter
               && preview.KarmaExpenseId == karmaId && preview.NuyenExpenseId == nuyenId
               && preview.KarmaAlreadyRecorded == preview.Command.ExistingKarmaExpenseId.HasValue
               && preview.NuyenAlreadyRecorded == preview.Command.ExistingNuyenExpenseId.HasValue
               && preview.PreviewDigest == ComputePreviewDigest(preview);
    }

    public static bool TryEvaluate(
        CharacterAfterRunRewardCommand command,
        CharacterAfterRunRewardSnapshot before,
        out int karmaAfter,
        out decimal nuyenAfter,
        out Guid? karmaExpenseId,
        out Guid? nuyenExpenseId,
        out string error)
    {
        karmaAfter = before.AvailableKarma;
        nuyenAfter = before.AvailableNuyen;
        karmaExpenseId = null;
        nuyenExpenseId = null;
        error = "reward_command_invalid";
        if (!IsValidCommandData(command) || !IsValidSnapshot(before) || !Matches(command, before))
            return false;

        return TryEvaluateCore(command, before.AvailableKarma, before.AvailableNuyen, before.Expenses,
            out karmaAfter, out nuyenAfter, out karmaExpenseId, out nuyenExpenseId, out error);
    }

    /// <summary>
    /// Validates claimed historical quote results, not append authority or global
    /// expense-ID absence. The store must reconstruct against the full document.
    /// </summary>
    public static bool TryEvaluateEvidence(
        CharacterAfterRunRewardCommand command,
        CharacterAfterRunRewardBeforeEvidence before,
        out int karmaAfter,
        out decimal nuyenAfter,
        out Guid? karmaExpenseId,
        out Guid? nuyenExpenseId,
        out string error)
    {
        karmaAfter = before.AvailableKarma;
        nuyenAfter = before.AvailableNuyen;
        karmaExpenseId = null;
        nuyenExpenseId = null;
        error = "reward_evidence_invalid";
        if (!IsValidCommand(command) || !IsValidBeforeEvidence(before) || !Matches(command, before))
            return false;
        int selectedCount = (command.ExistingKarmaExpenseId.HasValue ? 1 : 0)
                            + (command.ExistingNuyenExpenseId.HasValue ? 1 : 0);
        if (before.SelectedExpenses.Count != selectedCount
            || before.SelectedExpenses.Any(expense => expense.ExpenseId != command.ExistingKarmaExpenseId
                && expense.ExpenseId != command.ExistingNuyenExpenseId))
            return false;
        return TryEvaluateCore(command, before.AvailableKarma, before.AvailableNuyen, before.SelectedExpenses,
            out karmaAfter, out nuyenAfter, out karmaExpenseId, out nuyenExpenseId, out error);
    }

    private static bool TryEvaluateCore(
        CharacterAfterRunRewardCommand command,
        int karmaBefore,
        decimal nuyenBefore,
        IReadOnlyList<CharacterAfterRunRewardExpense> expenses,
        out int karmaAfter,
        out decimal nuyenAfter,
        out Guid? karmaExpenseId,
        out Guid? nuyenExpenseId,
        out string error)
    {
        karmaAfter = karmaBefore;
        nuyenAfter = nuyenBefore;
        karmaExpenseId = null;
        nuyenExpenseId = null;
        error = "reward_command_invalid";

        if (command.KarmaAmount > 0)
        {
            karmaExpenseId = command.ExistingKarmaExpenseId ?? ExpenseIdentity(command, "Karma");
            if (!ValidateSelection(expenses, command.ExistingKarmaExpenseId, karmaExpenseId.Value,
                    "Karma", command.KarmaAmount))
            {
                error = "reward_karma_expense_conflict";
                return false;
            }
        }
        if (command.NuyenAmount > 0)
        {
            nuyenExpenseId = command.ExistingNuyenExpenseId ?? ExpenseIdentity(command, "Nuyen");
            if (!ValidateSelection(expenses, command.ExistingNuyenExpenseId, nuyenExpenseId.Value,
                    "Nuyen", command.NuyenAmount) || nuyenExpenseId == karmaExpenseId)
            {
                error = "reward_nuyen_expense_conflict";
                return false;
            }
        }
        return TryQuoteBalances(command, karmaBefore, nuyenBefore, out karmaAfter, out nuyenAfter, out error);
    }

    private static bool TryQuoteBalances(
        CharacterAfterRunRewardCommand command,
        int karmaBefore, decimal nuyenBefore,
        out int karmaAfter, out decimal nuyenAfter, out string error)
    {
        karmaAfter = karmaBefore;
        nuyenAfter = nuyenBefore;
        if (command.Kind == CharacterAfterRunRewardKind.NoAward)
        {
            // Explicit zero-currency decisions are not synthetic manual quotes.
            error = string.Empty;
            return true;
        }
        if (command.KarmaAmount > 0 && command.ExistingKarmaExpenseId is null)
        {
            // Exchange is disabled. These neutral unused values do not claim
            // that a saved settings profile resolved either exchange rate.
            var state = new CharacterCareerManualKarmaState(karmaAfter, nuyenAfter, 1m, 1m);
            if (!CharacterCareerManualKarmaRules.TryQuote(state, CharacterCareerManualKarmaAction.Gain,
                    command.KarmaAmount, karmaNuyenExchange: false, out var quote) || quote is null)
            {
                error = "reward_karma_quote_unavailable";
                return false;
            }
            karmaAfter = quote.UpdatedKarma;
            nuyenAfter = quote.UpdatedNuyen;
        }
        if (command.NuyenAmount > 0 && command.ExistingNuyenExpenseId is null)
        {
            var state = new CharacterCareerManualNuyenState(karmaAfter, nuyenAfter, 1m, 1m);
            if (!CharacterCareerManualNuyenRules.TryQuote(state, CharacterCareerManualNuyenAction.Gain,
                    command.NuyenAmount, percent: 100m, karmaNuyenExchange: false, out var quote) || quote is null)
            {
                error = "reward_nuyen_quote_unavailable";
                return false;
            }
            karmaAfter = quote.UpdatedKarma;
            nuyenAfter = quote.UpdatedNuyen;
        }
        error = string.Empty;
        return true;
    }

    public static bool IsAssociated(CharacterAfterRunRewardReceipt receipt, Guid? expenseId)
        => expenseId.HasValue && (receipt.KarmaExpenseId == expenseId || receipt.NuyenExpenseId == expenseId);

    private static bool ValidateSelection(IReadOnlyList<CharacterAfterRunRewardExpense> expenses, Guid? selected,
        Guid targetId, string type, decimal amount)
    {
        CharacterAfterRunRewardExpense? expense = expenses.SingleOrDefault(item => item.ExpenseId == targetId);
        if (selected is null)
            return expense is null;
        // This certifies the selected saved entry, not the provenance of the
        // historical operation that produced it. No balance is awarded here.
        return expense is not null && expense.Type == type && expense.Amount == amount
               && !expense.Refund && !expense.ForceCareerVisible
               && expense.KarmaUndoType == (type == "Karma" ? "ManualAdd" : "ImproveAttribute")
               && expense.NuyenUndoType == (type == "Nuyen" ? "ManualAdd" : "AddCyberware")
               && expense.UndoObjectId == string.Empty && expense.UndoQuantity == 0m
               && expense.UndoExtra == string.Empty;
    }

    private static Guid ExpenseIdentity(CharacterAfterRunRewardCommand command, string type)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"chummer.core.after-run-reward-expense/v1\0{command.WorkspaceId.Value}\0{command.OperationId:D}\0{type}"));
        bytes[6] = (byte)((bytes[6] & 15) | 128);
        bytes[8] = (byte)((bytes[8] & 63) | 128);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }

    private static CharacterAfterRunRewardExpense NewExpense(CharacterAfterRunRewardCommand command, string type, Guid id)
        => new(id, type, command.ExpenseDateLocal,
            type == "Karma" ? command.KarmaAmount : command.NuyenAmount,
            command.Reason, false, false,
            type == "Karma" ? "ManualAdd" : "ImproveAttribute",
            type == "Nuyen" ? "ManualAdd" : "AddCyberware", string.Empty, 0m, string.Empty);

    private static void AddExpense(XElement root, CharacterAfterRunRewardExpense expense)
    {
        XElement? container = OptionalSingle(root, "expenses");
        if (container is null)
        {
            container = new XElement("expenses");
            root.Add(container);
        }
        XElement node = new("expense",
            new XElement("guid", expense.ExpenseId.ToString("D")),
            new XElement("date", expense.ExpenseDateLocal.ToString("s", CultureInfo.InvariantCulture)),
            new XElement("amount", expense.Amount.ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", expense.Reason), new XElement("type", expense.Type),
            new XElement("refund", "False"), new XElement("forcecareervisible", "False"),
            new XElement("undo", new XElement("karmatype", expense.KarmaUndoType),
                new XElement("nuyentype", expense.NuyenUndoType), new XElement("objectid", string.Empty),
                new XElement("qty", "0"), new XElement("extra", string.Empty)));
        XElement? next = container.Elements("expense")
            .FirstOrDefault(candidate => ReadDate(candidate) > expense.ExpenseDateLocal);
        if (next is null) container.Add(node);
        else next.AddBeforeSelf(node);
    }

    private static bool IsValidExpense(CharacterAfterRunRewardExpense? expense)
        => expense is not null && expense.ExpenseId != Guid.Empty
           && expense.Type is "Karma" or "Nuyen"
           && expense.ExpenseDateLocal >= MinimumDate && expense.ExpenseDateLocal <= MaximumDate
           && expense.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
           && expense.Reason is { Length: <= CharacterCareerManualKarmaRules.MaximumReasonLength }
           && (expense.UndoQuantity is null or >= 0m);

    private static CharacterAfterRunRewardExpense[] ReadExpenses(XElement root)
    {
        XElement? container = OptionalSingle(root, "expenses");
        if (container is null) return [];
        if (container.Elements().Any(node => node.Name != "expense")
            || container.Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value)))
            throw new InvalidDataException("reward_expense_container_invalid");
        List<CharacterAfterRunRewardExpense> entries = [];
        HashSet<Guid> identities = [];
        foreach (XElement expense in container.Elements("expense"))
        {
            if (!Guid.TryParseExact(RequiredText(expense, "guid").Trim(), "D", out Guid id)
                || id == Guid.Empty || !identities.Add(id))
                throw new InvalidDataException("reward_expense_identity_invalid");
            XElement? undo = OptionalSingle(expense, "undo");
            string? quantity = undo is null ? null : OptionalText(undo, "qty");
            var entry = new CharacterAfterRunRewardExpense(id,
                RequiredText(expense, "type"), ReadDate(expense),
                ReadDecimal(expense, "amount", required: true), OptionalText(expense, "reason") ?? string.Empty,
                ReadBool(expense, "refund"), ReadBool(expense, "forcecareervisible"),
                undo is null ? null : OptionalText(undo, "karmatype"),
                undo is null ? null : OptionalText(undo, "nuyentype"),
                undo is null ? null : OptionalText(undo, "objectid"),
                quantity is null ? null : ParseDecimal(quantity),
                undo is null ? null : OptionalText(undo, "extra"));
            if (!IsValidExpense(entry)) throw new InvalidDataException("reward_expense_invalid");
            entries.Add(entry);
        }
        return entries.ToArray();
    }

    private static XDocument Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaximumCharacterXmlLength)
            throw new InvalidDataException("reward_character_size_invalid");
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumCharacterXmlLength
        });
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "character") throw new InvalidDataException("reward_character_root_invalid");
        return document;
    }

    private static XElement? OptionalSingle(XElement parent, string name)
    {
        XElement[] elements = parent.Elements(name).Take(2).ToArray();
        if (elements.Length > 1) throw new InvalidDataException("reward_duplicate_" + name);
        return elements.SingleOrDefault();
    }

    private static string? OptionalText(XElement parent, string name)
    {
        XElement? element = OptionalSingle(parent, name);
        if (element?.HasElements == true) throw new InvalidDataException("reward_nested_" + name);
        return element?.Value;
    }

    private static string RequiredText(XElement parent, string name)
        => OptionalText(parent, name) ?? throw new InvalidDataException("reward_missing_" + name);

    private static bool ReadBool(XElement parent, string name, bool required = false)
    {
        string? value = OptionalText(parent, name);
        if (value is null && !required) return false;
        if (!bool.TryParse(value?.Trim(), out bool parsed)) throw new InvalidDataException("reward_invalid_" + name);
        return parsed;
    }

    private static int ReadInt(XElement parent, string name)
    {
        string value = RequiredText(parent, name);
        if (!int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
            throw new InvalidDataException("reward_invalid_" + name);
        return parsed;
    }

    private static decimal ReadDecimal(XElement parent, string name, bool required)
    {
        string? value = OptionalText(parent, name);
        if (value is null && !required) return 0m;
        return ParseDecimal(value ?? throw new InvalidDataException("reward_missing_" + name));
    }

    private static decimal ParseDecimal(string text)
        => decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal value)
            ? value : throw new InvalidDataException("reward_decimal_invalid");

    private static DateTime ReadDate(XElement expense)
        => DateTime.TryParseExact(RequiredText(expense, "date").Trim(), "s", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime value)
            ? DateTime.SpecifyKind(value, DateTimeKind.Unspecified)
            : throw new InvalidDataException("reward_expense_date_invalid");

    private static void SetNumber(XElement root, string name, string value)
    {
        XElement? element = OptionalSingle(root, name);
        if (element is null) root.Add(new XElement(name, value));
        else element.Value = value;
    }

    private static bool IsProjectionFailure(Exception exception)
        => exception is XmlException or InvalidDataException or ArgumentException or OverflowException;
}
