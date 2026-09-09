using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Pure command/receipt preparation; only the dedicated store may persist its result.</summary>
public static class CharacterCareerReputationTransaction
{
    public const int MaximumReasonLength = 512;
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public static string LedgerRoot => Hash("chummer.core.sr5-reputation-ledger-root/v1");

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string CommandDigest(CharacterCareerReputationCommand command)
        => Hash("chummer.core.sr5-reputation-command/v1\0" + JsonSerializer.Serialize(command));
    public static string SnapshotDigest(CharacterCareerReputationSnapshot snapshot)
        => Hash("chummer.core.sr5-reputation-snapshot/v1\0" + JsonSerializer.Serialize(snapshot));
    public static string ReceiptDigest(CharacterCareerReputationReceipt receipt)
        => Hash("chummer.core.sr5-reputation-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));

    public static bool IsValidRequest(CharacterCareerReputationRequest? request)
        => request is not null
            && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && request.Reason is { Length: <= MaximumReasonLength }
            && (request.Operation switch
            {
                CharacterCareerReputationOperation.BurnStreetCred => request.Adjustment is null,
                CharacterCareerReputationOperation.AdjustManualAwards => request.Adjustment is { } adjustment
                    && (adjustment.StreetCredDelta is not null || adjustment.NotorietyDelta is not null
                        || adjustment.PublicAwarenessDelta is not null),
                _ => false
            });

    public static bool IsBoundCommand(CharacterCareerReputationCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } binding
            && binding.WorkspaceId == command.Request.WorkspaceId && binding.WorkspaceRevision is > 0 and < long.MaxValue
            && IsDigest(binding.SourceDigest) && IsDigest(binding.AuxiliaryStateDigest)
            && IsDigest(binding.RuleStateDigest) && IsDigest(binding.SnapshotDigest) && IsDigest(binding.RuntimeDigest);

    public static bool IsConfirmedCommand(CharacterCareerReputationCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);

    public static bool TryQuote(CharacterCareerReputationRequest request, CharacterCareerReputationInputs inputs,
        out CharacterCareerReputationQuote? quote)
    {
        quote = null;
        if (!IsValidRequest(request)) return false;
        return request.Operation == CharacterCareerReputationOperation.BurnStreetCred
            ? CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out quote)
            : CharacterCareerReputationRules.TryQuoteAdjustment(inputs, request.Adjustment!, out quote);
    }

    public static bool TryPreview(CharacterCareerReputationSnapshot snapshot,
        CharacterCareerReputationRequest request, string runtimeDigest, out CharacterCareerReputationPreview? preview)
    {
        preview = null;
        if (!IsValidRequest(request) || snapshot.WorkspaceId != request.WorkspaceId
            || !IsDigest(runtimeDigest) || !TryQuote(request, snapshot.Reputation.Inputs, out var quote)) return false;
        var binding = new CharacterCareerReputationBinding(snapshot.WorkspaceId, snapshot.ContentRevision,
            snapshot.SourceDigest, snapshot.AuxiliaryStateDigest, snapshot.RuleStateDigest, SnapshotDigest(snapshot), runtimeDigest);
        var command = new CharacterCareerReputationCommand(request, binding);
        if (!IsBoundCommand(command)) return false;
        preview = CreatePreview(command, quote!);
        return true;
    }

    private static CharacterCareerReputationPreview CreatePreview(CharacterCareerReputationCommand command,
        CharacterCareerReputationQuote quote)
    {
        var normalized = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-reputation-preview/v1\0" + JsonSerializer.Serialize(new { Command = normalized, Quote = quote }));
        return new(normalized with { ExpectedPreviewDigest = digest }, quote, digest);
    }

    public static bool IsCoherent(CharacterCareerReputationReceipt? receipt)
    {
        if (receipt is null || !IsConfirmedCommand(receipt.Command)
            || receipt.Quote?.Before?.Inputs is not { } inputs || inputs.RulesetId != "sr5"
            || receipt.Quote.After?.Inputs is not { RulesetId: "sr5" }
            || !IsDigest(receipt.CommandDigest) || !IsDigest(receipt.CharacterPayloadDigestAfter)
            || !IsDigest(receipt.PreviousReceiptDigest) || !IsDigest(receipt.ReceiptDigest)
            || receipt.CommittedWorkspaceRevision != receipt.Command.Binding.WorkspaceRevision + 1
            || !TryQuote(receipt.Command.Request, inputs, out var expected) || receipt.Quote != expected)
            return false;
        return receipt.CommandDigest == CommandDigest(receipt.Command)
            && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, expected!).PreviewDigest
            && receipt.ReceiptDigest == ReceiptDigest(receipt);
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerReputationReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        HashSet<Guid> operations = [];
        string previous = LedgerRoot;
        long lastRevision = 0;
        long bytes = 2;
        foreach (var receipt in receipts)
        {
            // All fields are bounded/coherent before serializing one compact
            // entry. Never allocate the entire ledger just to measure admission.
            if (!IsCoherent(receipt) || receipt.Command.Binding.WorkspaceId != id
                || receipt.CommittedWorkspaceRevision > revision || receipt.CommittedWorkspaceRevision <= lastRevision
                || receipt.PreviousReceiptDigest != previous || !operations.Add(receipt.Command.Request.OperationId))
                return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(receipt).Length + (lastRevision == 0 ? 0 : 1);
            if (bytes > MaximumLedgerBytes) return false;
            previous = receipt.ReceiptDigest;
            lastRevision = receipt.CommittedWorkspaceRevision;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var history = saved.Document.AuxiliaryState.CharacterCareerReputationReceipts;
        if (!IsValidLedger(saved.Id, saved.ContentRevision, history)) return false;
        return history is not { Count: > 0 } || (saved.SavedRevision >= history[^1].CommittedWorkspaceRevision
            && (saved.ContentRevision != history[^1].CommittedWorkspaceRevision
                || history[^1].CharacterPayloadDigestAfter == Hash(saved.Document.Content)));
    }

    public static CharacterCareerReputationResult Lookup(WorkspaceStoredDocument saved, Guid operationId, string commandDigest)
    {
        if (!IsValidHistory(saved)) return new(CharacterCareerReputationOutcome.Corrupt, Error: "reputation_history_invalid");
        var receipt = saved.Document.AuxiliaryState.CharacterCareerReputationReceipts?
            .SingleOrDefault(row => row.Command.Request.OperationId == operationId);
        if (receipt is null) return new(CharacterCareerReputationOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(receipt.CommittedWorkspaceRevision)
            && receipt.CommandDigest == commandDigest
            ? new(CharacterCareerReputationOutcome.Replayed, receipt, saved.ContentRevision)
            : new(CharacterCareerReputationOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "reputation_operation_conflict");
    }

    public static bool TryBuild(WorkspaceStoredDocument saved, CharacterCareerReputationSnapshot snapshot,
        CharacterCareerReputationCommand command, string runtimeDigest,
        out WorkspaceDocument? replacement, out CharacterCareerReputationReceipt? receipt)
    {
        replacement = null;
        receipt = null;
        if (!IsConfirmedCommand(command) || !IsValidHistory(saved)
            || !TryPreview(snapshot, command.Request, runtimeDigest, out var preview)
            || preview!.Command with { ExplicitlyConfirmed = true } != command
            || saved.Id != snapshot.WorkspaceId || saved.ContentRevision != snapshot.ContentRevision
            || saved.SavedRevision != saved.ContentRevision || saved.Document.AuxiliaryStateDigest != snapshot.AuxiliaryStateDigest)
            return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerReputationReceipts;
        if (history is { Count: >= MaximumReceipts }
            || history?.Any(row => row.Command.Request.OperationId == command.Request.OperationId) == true)
            return false;
        XDocument document = XDocument.Parse(saved.Document.Content, LoadOptions.PreserveWhitespace);
        XElement root = document.Root!;
        var before = preview.Quote.Before.Inputs;
        var after = preview.Quote.After.Inputs;
        SetChanged(root, "streetcred", before.StreetCred, after.StreetCred);
        SetChanged(root, "notoriety", before.Notoriety, after.Notoriety);
        SetChanged(root, "publicawareness", before.PublicAwareness, after.PublicAwareness);
        SetChanged(root, "burntstreetcred", before.BurntStreetCred, after.BurntStreetCred);
        string payload = document.ToString(SaveOptions.DisableFormatting);
        if (payload.Length > CharacterCareerReputationProjector.MaximumCharacterXmlLength) return false;
        var candidate = new CharacterCareerReputationReceipt(command, preview.Quote, CommandDigest(command),
            saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
        candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
        var appended = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedger(saved.Id, saved.ContentRevision + 1, appended)) return false;
        replacement = saved.Document with { State = saved.Document.State with { Payload = payload,
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerReputationReceipts = Array.AsReadOnly(appended) } } };
        receipt = candidate;
        return true;
    }

    private static void SetChanged(XElement root, string name, int before, int after)
    {
        if (before != after) root.Element(name)!.Value = after.ToString(CultureInfo.InvariantCulture);
    }
}
