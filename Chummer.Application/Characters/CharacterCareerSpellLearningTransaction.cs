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
public sealed class CharacterCareerSpellLearningSnapshot
{
    internal CharacterCareerSpellLearningSnapshot(CharacterCareerSpellLearningBinding binding,
        CharacterCareerSpellLearningQuote quote, ICharacterSourceDataContext context)
        => (Binding, Quote, Context) = (binding, quote, context);
    public CharacterCareerSpellLearningBinding Binding { get; }
    public CharacterCareerSpellLearningQuote Quote { get; }
    internal ICharacterSourceDataContext Context { get; }
    internal CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? Current5703 { get; init; }
}

public static partial class CharacterCareerSpellLearningTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 8 * 1024 * 1024;
    private const int MaximumContentLength = 67_108_864;
    private static string LedgerRoot => Hash("chummer.core.sr5-career-spell-learning-ledger/v1");
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => CharacterCareerContactsTransaction.IsDigest(value);
    public static bool IsValidOwner(OwnerScope owner) => CharacterCareerContactsTransaction.IsValidOwner(owner);
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerSpellLearningCommand command)
        => Hash("chummer.core.sr5-career-spell-learning-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerSpellLearningReceipt receipt)
        => Hash("chummer.core.sr5-career-spell-learning-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    public static Guid InstanceId(CharacterCareerSpellLearningRequest request, string kind)
        => new(Hash("chummer.core.sr5-career-spell-learning-instance/v1\0" + request.WorkspaceId.Value
            + "\0" + request.OperationId.ToString("D") + "\0" + kind)[..32]);

    public static bool IsValidRequest(CharacterCareerSpellLearningRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && request.SourceId != Guid.Empty
            && request.Kind is CharacterCareerSpellLearningKind.Spell or CharacterCareerSpellLearningKind.Ritual
            && IsValidCatalogBinding(request.CatalogBinding, request.WorkspaceId)
            && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && request.ExpenseDateLocal != DateTime.MinValue && request.ExpenseDateLocal != DateTime.MaxValue;

    private static bool IsBoundCommand(CharacterCareerSpellLearningCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } b
            && b.OwnerId is { Length: > 0 and <= 512 } && new OwnerScope(b.OwnerId).NormalizedValue == b.OwnerId
            && b.WorkspaceId == command.Request.WorkspaceId && b.WorkspaceRevision is > 0 and < long.MaxValue
            && IsDigest(b.SourceDigest) && IsDigest(b.AuxiliaryStateDigest) && IsDigest(b.RuleStateDigest) && IsDigest(b.RuntimeDigest);
    public static bool IsConfirmedCommand(CharacterCareerSpellLearningCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);

    private static string SourceDigest(WorkspaceDocument doc) => Hash(JsonSerializer.Serialize(new
        { doc.Format, doc.RulesetId, doc.SchemaVersion, doc.PayloadKind, PayloadDigest = Hash(doc.Content) }));

    public static bool TryPreview(CharacterCareerSpellLearningSnapshot snapshot, CharacterCareerSpellLearningRequest request,
        out CharacterCareerSpellLearningPreview? preview, out string error)
    {
        preview = null;
        error = "spell_learning_request_conflict";
        var command = new CharacterCareerSpellLearningCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || !IsValidQuote(request, snapshot.Quote)) return false;
        preview = CreatePreview(command, snapshot.Quote);
        error = "";
        return true;
    }

    private static CharacterCareerSpellLearningPreview CreatePreview(CharacterCareerSpellLearningCommand command,
        CharacterCareerSpellLearningQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-career-spell-learning-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }

    public static bool IsCoherent(CharacterCareerSpellLearningReceipt? receipt)
    {
        try
        {
            return receipt is not null && IsConfirmedCommand(receipt.Command)
                && IsValidQuote(receipt.Command.Request, receipt.Quote)
                && receipt.CommandDigest == CommandDigest(receipt.Command)
                && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, receipt.Quote).PreviewDigest
                && receipt.SpellInstanceId == InstanceId(receipt.Command.Request, "spell")
                && receipt.ExpenseId == InstanceId(receipt.Command.Request, "expense")
                && receipt.CommittedWorkspaceRevision == receipt.Command.Binding.WorkspaceRevision + 1
                && IsDigest(receipt.CharacterPayloadDigestAfter) && IsDigest(receipt.PreviousReceiptDigest)
                && receipt.ReceiptDigest == ReceiptDigest(receipt);
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerSpellLearningReceipt>? receipts)
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
                || !identities.Add(row.SpellInstanceId) || !identities.Add(row.ExpenseId)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(row).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            previous = row.ReceiptDigest;
            lastRevision = row.CommittedWorkspaceRevision;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var rows = saved.Document.AuxiliaryState.CharacterCareerSpellLearningReceipts;
        return IsValidLedger(saved.Id, saved.ContentRevision, rows)
            && (rows is not { Count: > 0 } || saved.SavedRevision >= rows[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != rows[^1].CommittedWorkspaceRevision
                    || Hash(saved.Document.Content) == rows[^1].CharacterPayloadDigestAfter));
    }

    public static CharacterCareerSpellLearningResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operationId, string commandDigest)
    {
        if (!IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerSpellLearningOutcome.Corrupt, Error: "spell_learning_history_invalid");
        var row = saved.Document.AuxiliaryState.CharacterCareerSpellLearningReceipts?
            .SingleOrDefault(r => r.Command.Request.OperationId == operationId);
        if (row is null) return new(CharacterCareerSpellLearningOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(row.CommittedWorkspaceRevision) && row.Command.Binding.OwnerId == owner.NormalizedValue
            && row.CommandDigest == commandDigest
            ? new(CharacterCareerSpellLearningOutcome.Replayed, row, saved.ContentRevision)
            : new(CharacterCareerSpellLearningOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "spell_learning_operation_conflict");
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerSpellLearningSnapshot snapshot, CharacterCareerSpellLearningCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerSpellLearningReceipt? receipt)
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
        var history = saved.Document.AuxiliaryState.CharacterCareerSpellLearningReceipts;
        if (history is { Count: >= MaximumReceipts }
            || history?.Any(r => r.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        try
        {
            string payload = Apply(saved.Document.Content, command.Request, snapshot.Quote);
            if (snapshot.Current5703 is not null)
                payload = CharacterCareerLearningDocument5703.PreserveEnvelope(saved.Document.Content, payload);
            if (payload.Length > MaximumContentLength) return false;
            var candidate = new CharacterCareerSpellLearningReceipt(command, snapshot.Quote, CommandDigest(command),
                InstanceId(command.Request, "spell"), InstanceId(command.Request, "expense"), saved.ContentRevision + 1,
                Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
            candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
            var appended = (history ?? []).Append(candidate).ToArray();
            if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, appended)) return false;
            replacement = saved.Document with { State = saved.Document.State with
            {
                Payload = payload,
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerSpellLearningReceipts = Array.AsReadOnly(appended) }
            } };
            receipt = candidate;
            return true;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    private static bool Malformed(Exception e) => e is XmlException or InvalidDataException or FormatException
        or OverflowException or ArgumentException or InvalidOperationException or NullReferenceException;
}
