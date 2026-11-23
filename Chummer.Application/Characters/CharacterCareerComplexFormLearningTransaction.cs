using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Only canonical source evaluation constructs this in-process capability.</summary>
public sealed class CharacterCareerComplexFormLearningSnapshot
{
    internal CharacterCareerComplexFormLearningSnapshot(CharacterCareerComplexFormLearningBinding binding,
        CharacterCareerComplexFormLearningQuote quote, ICharacterSourceDataContext context)
        => (Binding, Quote, Context) = (binding, quote, context);
    public CharacterCareerComplexFormLearningBinding Binding { get; }
    public CharacterCareerComplexFormLearningQuote Quote { get; }
    internal ICharacterSourceDataContext Context { get; }
    internal CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? Current5703 { get; init; }
}

public static partial class CharacterCareerComplexFormLearningTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 8 * 1024 * 1024;
    private const int MaximumContentLength = 67_108_864;
    private static string LedgerRoot => Hash("chummer.core.sr5-career-complex-form-learning-ledger/v1");
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => CharacterCareerContactsTransaction.IsDigest(value);
    public static bool IsValidOwner(OwnerScope owner) => CharacterCareerContactsTransaction.IsValidOwner(owner);
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerComplexFormLearningCommand command)
        => Hash("chummer.core.sr5-career-complex-form-learning-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerComplexFormLearningReceipt receipt)
        => Hash("chummer.core.sr5-career-complex-form-learning-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    public static Guid InstanceId(CharacterCareerComplexFormLearningRequest request, string kind)
        => new(Hash("chummer.core.sr5-career-complex-form-learning-instance/v1\0" + request.WorkspaceId.Value
            + "\0" + request.OperationId.ToString("D") + "\0" + kind)[..32]);

    public static bool IsValidRequest(CharacterCareerComplexFormLearningRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && request.SourceId != Guid.Empty
            && IsValidCatalogBinding(request.CatalogBinding, request.WorkspaceId)
            && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && request.ExpenseDateLocal != DateTime.MinValue && request.ExpenseDateLocal != DateTime.MaxValue;

    private static bool IsBoundCommand(CharacterCareerComplexFormLearningCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } b
            && b.OwnerId is { Length: > 0 and <= 512 } && new OwnerScope(b.OwnerId).NormalizedValue == b.OwnerId
            && b.WorkspaceId == command.Request.WorkspaceId && b.WorkspaceRevision is > 0 and < long.MaxValue
            && IsDigest(b.SourceDigest) && IsDigest(b.AuxiliaryStateDigest) && IsDigest(b.RuleStateDigest) && IsDigest(b.RuntimeDigest);
    public static bool IsConfirmedCommand(CharacterCareerComplexFormLearningCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);

    private static string SourceDigest(WorkspaceDocument doc) => Hash(JsonSerializer.Serialize(new
        { doc.Format, doc.RulesetId, doc.SchemaVersion, doc.PayloadKind, PayloadDigest = Hash(doc.Content) }));

    public static bool TryPreview(CharacterCareerComplexFormLearningSnapshot snapshot, CharacterCareerComplexFormLearningRequest request,
        out CharacterCareerComplexFormLearningPreview? preview, out string error)
    {
        preview = null;
        error = "complex_form_learning_request_conflict";
        var command = new CharacterCareerComplexFormLearningCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || !IsValidQuote(request, snapshot.Quote)) return false;
        preview = CreatePreview(command, snapshot.Quote);
        error = "";
        return true;
    }

    private static CharacterCareerComplexFormLearningPreview CreatePreview(CharacterCareerComplexFormLearningCommand command,
        CharacterCareerComplexFormLearningQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-career-complex-form-learning-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }

    public static bool IsCoherent(CharacterCareerComplexFormLearningReceipt? receipt)
    {
        try
        {
            return receipt is not null && IsConfirmedCommand(receipt.Command)
                && IsValidQuote(receipt.Command.Request, receipt.Quote)
                && receipt.CommandDigest == CommandDigest(receipt.Command)
                && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, receipt.Quote).PreviewDigest
                && receipt.ComplexFormInstanceId == InstanceId(receipt.Command.Request, "complexform")
                && receipt.ExpenseId == InstanceId(receipt.Command.Request, "expense")
                && receipt.CommittedWorkspaceRevision == receipt.Command.Binding.WorkspaceRevision + 1
                && IsDigest(receipt.CharacterPayloadDigestAfter) && IsDigest(receipt.PreviousReceiptDigest)
                && receipt.ReceiptDigest == ReceiptDigest(receipt);
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerComplexFormLearningReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        var operations = new HashSet<Guid>();
        var identities = new HashSet<Guid>();
        string previous = LedgerRoot;
        long lastRevision = 0, bytes = 2;
        foreach (var row in receipts)
        {
            if (!IsCoherent(row) || row.Command.Request.WorkspaceId != id
                || row.CommittedWorkspaceRevision > revision || row.CommittedWorkspaceRevision <= lastRevision
                || row.PreviousReceiptDigest != previous || !operations.Add(row.Command.Request.OperationId)
                || !identities.Add(row.ComplexFormInstanceId) || !identities.Add(row.ExpenseId)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(row).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            previous = row.ReceiptDigest;
            lastRevision = row.CommittedWorkspaceRevision;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var rows = saved.Document.AuxiliaryState.CharacterCareerComplexFormLearningReceipts;
        return IsValidLedger(saved.Id, saved.ContentRevision, rows)
            && (rows is not { Count: > 0 } || saved.SavedRevision >= rows[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != rows[^1].CommittedWorkspaceRevision
                    || Hash(saved.Document.Content) == rows[^1].CharacterPayloadDigestAfter));
    }

    public static CharacterCareerComplexFormLearningResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operationId, string commandDigest)
    {
        if (!IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerComplexFormLearningOutcome.Corrupt, Error: "complex_form_learning_history_invalid");
        var row = saved.Document.AuxiliaryState.CharacterCareerComplexFormLearningReceipts?
            .SingleOrDefault(r => r.Command.Request.OperationId == operationId);
        if (row is null) return new(CharacterCareerComplexFormLearningOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(row.CommittedWorkspaceRevision) && row.Command.Binding.OwnerId == owner.NormalizedValue
            && row.CommandDigest == commandDigest
            ? new(CharacterCareerComplexFormLearningOutcome.Replayed, row, saved.ContentRevision)
            : new(CharacterCareerComplexFormLearningOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "complex_form_learning_operation_conflict");
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerComplexFormLearningSnapshot snapshot, CharacterCareerComplexFormLearningCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerComplexFormLearningReceipt? receipt)
    {
        replacement = null;
        receipt = null;
        if (!IsValidOwner(owner) || !IsConfirmedCommand(command) || command.Binding.OwnerId != owner.NormalizedValue
            || !IsValidHistory(saved) || !CharacterCareerPowerGovernance.Allows(saved)
            || saved.Id != command.Request.WorkspaceId || saved.ContentRevision != command.Binding.WorkspaceRevision
            || saved.SavedRevision != saved.ContentRevision || saved.Document.AuxiliaryStateDigest != command.Binding.AuxiliaryStateDigest
            || SourceDigest(saved.Document) != command.Binding.SourceDigest || snapshot.Binding != command.Binding
            || !TryPreview(snapshot, command.Request, out var preview, out _)
            || preview!.Command with { ExplicitlyConfirmed = true } != command) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerComplexFormLearningReceipts;
        if (history is { Count: >= MaximumReceipts }
            || history?.Any(r => r.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        try
        {
            string payload = Apply(saved.Document.Content, command.Request, snapshot.Quote);
            if (snapshot.Current5703 is not null)
                payload = CharacterCareerLearningDocument5703.PreserveEnvelope(saved.Document.Content, payload);
            if (payload.Length > MaximumContentLength) return false;
            var candidate = new CharacterCareerComplexFormLearningReceipt(command, snapshot.Quote, CommandDigest(command),
                InstanceId(command.Request, "complexform"), InstanceId(command.Request, "expense"), saved.ContentRevision + 1,
                Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
            candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
            var appended = (history ?? []).Append(candidate).ToArray();
            if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, appended)) return false;
            replacement = saved.Document with { State = saved.Document.State with
            {
                Payload = payload,
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerComplexFormLearningReceipts = Array.AsReadOnly(appended) }
            } };
            receipt = candidate;
            return true;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    private static bool Malformed(Exception e) => e is XmlException or InvalidDataException or FormatException
        or OverflowException or ArgumentException or InvalidOperationException or NullReferenceException;
}
