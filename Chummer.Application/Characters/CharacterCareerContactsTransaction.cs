using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed record CharacterCareerContactsSnapshot(
    CharacterCareerContactsBinding Binding, CharacterCareerContactState? Before,
    IReadOnlyList<CharacterContactRuleEffect> Effects);

/// <summary>Pure preparation and durable evidence checks. Only the dedicated leased store may persist a contact edit.</summary>
public static partial class CharacterCareerContactsTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public const int MaximumContentLength = 67_108_864;
    private static string LedgerRoot => Hash("chummer.core.sr5-career-contacts-ledger/v1");

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string CommandDigest(CharacterCareerContactsCommand command)
        => Hash("chummer.core.sr5-career-contacts-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerContactsReceipt receipt)
        => Hash("chummer.core.sr5-career-contacts-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));

    public static bool IsValidOwner(OwnerScope owner) => owner.NormalizedValue is { Length: > 0 and <= 512 }
        && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool IsValidRequest(CharacterCareerContactsRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && CharacterCareerContactsRules.IsValidChange(request.Change);

    public static bool IsBoundCommand(CharacterCareerContactsCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } binding
            && binding.OwnerId is { Length: > 0 and <= 512 }
            && new OwnerScope(binding.OwnerId).NormalizedValue == binding.OwnerId
            && binding.WorkspaceId == command.Request.WorkspaceId
            && binding.WorkspaceRevision is > 0 and < long.MaxValue
            && IsDigest(binding.SourceDigest) && IsDigest(binding.AuxiliaryStateDigest)
            && IsDigest(binding.RuleStateDigest) && IsDigest(binding.RuntimeDigest) && IsDigest(binding.TargetDigest);

    public static bool IsConfirmedCommand(CharacterCareerContactsCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);

    public static bool IsSourceCurrent(WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerContactsBinding binding)
        => SourceDigest(saved.Document) == binding.SourceDigest
            && TryReadRuleState(sources.TryCreateContext(saved.Document.Content), out string rules)
            && rules == binding.RuleStateDigest;

    private static bool TryReadRuleState(ICharacterSourceDataContext? context, out string digest)
    {
        digest = string.Empty;
        if (context is null || !context.TryResolveCareerSettingsProfile(out string profile, out string authority)
            || string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(authority)) return false;
        digest = Hash("chummer.core.sr5-career-contacts-rules/v1\0" + JsonSerializer.Serialize(new { profile, authority, CharacterCareerContactsRules.Version, CharacterCareerContactsRules.SourceAnchors }));
        return true;
    }

    private static string SourceDigest(WorkspaceDocument document)
        => Hash("chummer.core.sr5-career-contacts-source/v1\0" + JsonSerializer.Serialize(new
        { document.Format, document.RulesetId, document.SchemaVersion, document.PayloadKind, PayloadDigest = Hash(document.Content) }));

    public static bool TryPreview(CharacterCareerContactsSnapshot snapshot,
        CharacterCareerContactsRequest request, out CharacterCareerContactsPreview? preview, out string error)
    {
        preview = null;
        error = "contact_request_conflict";
        var command = new CharacterCareerContactsCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || !CharacterCareerContactsRules.TryQuote(request.Change, snapshot.Before,
            snapshot.Effects, out var quote, out error)) return false;
        preview = CreatePreview(command, quote!);
        error = string.Empty;
        return true;
    }

    private static CharacterCareerContactsPreview CreatePreview(CharacterCareerContactsCommand command,
        CharacterCareerContactsQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-career-contacts-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }

    public static bool IsCoherent(CharacterCareerContactsReceipt? receipt)
    {
        if (receipt is null || !IsConfirmedCommand(receipt.Command) || receipt.Quote is null
            || (receipt.Quote.After is { } after && !CharacterCareerContactsRules.IsValidIdentity(after.Identity))
            || !IsDigest(receipt.CommandDigest) || !IsDigest(receipt.CharacterPayloadDigestAfter)
            || !IsDigest(receipt.PreviousReceiptDigest) || !IsDigest(receipt.ReceiptDigest)
            || receipt.CommittedWorkspaceRevision != receipt.Command.Binding.WorkspaceRevision + 1
            || !CharacterCareerContactsRules.TryQuote(receipt.Command.Request.Change, receipt.Quote.Before,
                receipt.Quote.Effects, out var expected, out _)) return false;
        return JsonSerializer.Serialize(receipt.Quote) == JsonSerializer.Serialize(expected)
            && receipt.CommandDigest == CommandDigest(receipt.Command)
            && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, expected!).PreviewDigest
            && receipt.ReceiptDigest == ReceiptDigest(receipt);
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerContactsReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        HashSet<Guid> operations = [];
        HashSet<Guid> addedContacts = [];
        string previous = LedgerRoot;
        long lastRevision = 0, bytes = 2;
        foreach (var receipt in receipts)
        {
            if (!IsCoherent(receipt) || receipt.Command.Request.WorkspaceId != id
                || receipt.CommittedWorkspaceRevision > revision || receipt.CommittedWorkspaceRevision <= lastRevision
                || receipt.PreviousReceiptDigest != previous || !operations.Add(receipt.Command.Request.OperationId)
                || (receipt.Command.Request.Change.Kind == CharacterCareerContactChangeKind.Add
                    && !addedContacts.Add(receipt.Command.Request.Change.ContactId))) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(receipt).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            lastRevision = receipt.CommittedWorkspaceRevision;
            previous = receipt.ReceiptDigest;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var history = saved.Document.AuxiliaryState.CharacterCareerContactsReceipts;
        return IsValidLedger(saved.Id, saved.ContentRevision, history)
            && (history is not { Count: > 0 } || (saved.SavedRevision >= history[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != history[^1].CommittedWorkspaceRevision
                    || Hash(saved.Document.Content) == history[^1].CharacterPayloadDigestAfter)));
    }

    public static CharacterCareerContactsResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operationId, string commandDigest)
    {
        if (!IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerContactsOutcome.Corrupt, Error: "contact_history_invalid");
        var receipt = saved.Document.AuxiliaryState.CharacterCareerContactsReceipts?
            .SingleOrDefault(row => row.Command.Request.OperationId == operationId);
        if (receipt is null) return new(CharacterCareerContactsOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(receipt.CommittedWorkspaceRevision)
            && receipt.Command.Binding.OwnerId == owner.NormalizedValue && receipt.CommandDigest == commandDigest
            ? new(CharacterCareerContactsOutcome.Replayed, receipt, saved.ContentRevision)
            : new(CharacterCareerContactsOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "contact_operation_conflict");
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerContactsSnapshot snapshot, CharacterCareerContactsCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerContactsReceipt? receipt)
    {
        replacement = null;
        receipt = null;
        if (!IsValidOwner(owner) || !IsConfirmedCommand(command) || command.Binding.OwnerId != owner.NormalizedValue
            || !IsValidHistory(saved) || saved.Id != command.Request.WorkspaceId
            || saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || saved.Document.AuxiliaryStateDigest != command.Binding.AuxiliaryStateDigest
            || SourceDigest(saved.Document) != command.Binding.SourceDigest
            || !TryPreview(snapshot, command.Request, out var preview, out _)
            || preview!.Command with { ExplicitlyConfirmed = true } != command) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerContactsReceipts;
        if (history is { Count: >= MaximumReceipts }
            || history?.Any(row => row.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        try
        {
            if (!TryApply(saved.Document.Content, command.Request.Change, preview.Quote, out string payload)) return false;
            if (payload.Length > MaximumContentLength) return false;
            var candidate = new CharacterCareerContactsReceipt(command, preview.Quote, CommandDigest(command),
                saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
            candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
            var appended = (history ?? []).Append(candidate).ToArray();
            if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, appended)) return false;
            replacement = saved.Document with { State = saved.Document.State with
            {
                Payload = payload,
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerContactsReceipts = Array.AsReadOnly(appended) }
            } };
            receipt = candidate;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException or OverflowException
            or ArgumentException or InvalidOperationException) { return false; }
    }

}
