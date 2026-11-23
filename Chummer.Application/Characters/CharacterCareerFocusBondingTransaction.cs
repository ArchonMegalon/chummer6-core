using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerFocusBondingSnapshot
{
    internal CharacterCareerFocusBondingSnapshot(CharacterCareerFocusBondingBinding binding,
        CharacterCareerFocusBondingQuote quote, ICharacterSourceDataContext context)
        => (Binding, Quote, Context) = (binding, quote, context);
    public CharacterCareerFocusBondingBinding Binding { get; }
    public CharacterCareerFocusBondingQuote Quote { get; }
    internal ICharacterSourceDataContext Context { get; }
}

public static partial class CharacterCareerFocusBondingTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 8 * 1024 * 1024;
    private static string LedgerRoot => Hash("chummer.core.sr5-career-focus-bonding-ledger/v1");
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => CharacterCareerContactsTransaction.IsDigest(value);
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerFocusBondingCommand command)
        => Hash("chummer.core.sr5-career-focus-bonding-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerFocusBondingReceipt receipt)
        => Hash("chummer.core.sr5-career-focus-bonding-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    public static Guid ExpenseId(CharacterCareerFocusBondingRequest request)
        => new(Hash(request.WorkspaceId.Value + "\0" + request.OperationId.ToString("D") + "\0focus-bonding-expense")[..32]);
    public static string SourceDigest(WorkspaceDocument doc) => Hash(JsonSerializer.Serialize(new
        { doc.RulesetId, doc.SchemaVersion, doc.PayloadKind, PayloadDigest = Hash(doc.Content) }));
    public static bool IsValidRequest(CharacterCareerFocusBondingRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
        && request.OperationId != Guid.Empty && request.GearId != Guid.Empty
        && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
        && request.ExpenseDateLocal > DateTime.MinValue && request.ExpenseDateLocal < DateTime.MaxValue;
    public static bool IsBoundCommand(CharacterCareerFocusBondingCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } b
        && b.OwnerId is { Length: > 0 and <= 512 } && new OwnerScope(b.OwnerId).NormalizedValue == b.OwnerId
        && !string.IsNullOrWhiteSpace(b.OwnerAuthorityInstanceId) && b.OwnerTransitionRevision >= 0
        && b.WorkspaceId == command.Request.WorkspaceId && b.WorkspaceRevision is > 0 and < long.MaxValue
        && Guid.TryParseExact(b.LocalHistoryIncarnationId, "N", out var incarnation) && incarnation != Guid.Empty
        && incarnation.ToString("N") == b.LocalHistoryIncarnationId
        && IsDigest(b.SourceDigest) && IsDigest(b.AuxiliaryStateDigest) && IsDigest(b.RuleStateDigest) && IsDigest(b.RuntimeDigest);
    public static bool IsConfirmedCommand(CharacterCareerFocusBondingCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);
    public static bool OwnerMatches(OwnerContextStamp owner, CharacterCareerFocusBondingCommand command)
        => owner.IsValid && CharacterCareerContactsTransaction.IsValidOwner(owner.Owner) && IsConfirmedCommand(command) && owner.Owner.NormalizedValue == command.Binding.OwnerId
        && owner.Owner.IsLocalSingleUser == command.Binding.LocalSingleUser
        && owner.AuthorityInstanceId == command.Binding.OwnerAuthorityInstanceId
        && owner.TransitionRevision == command.Binding.OwnerTransitionRevision;
    public static bool SameOwner(OwnerContextStamp owner, CharacterCareerFocusBondingCommand command)
        => owner.IsValid && CharacterCareerContactsTransaction.IsValidOwner(owner.Owner) && IsConfirmedCommand(command) && owner.Owner.NormalizedValue == command.Binding.OwnerId
        && owner.Owner.IsLocalSingleUser == command.Binding.LocalSingleUser;
    public static CharacterCareerFocusBondingPreview CreatePreview(CharacterCareerFocusBondingCommand command,
        CharacterCareerFocusBondingQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-career-focus-bonding-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }
    public static bool TryPreview(CharacterCareerFocusBondingSnapshot snapshot, CharacterCareerFocusBondingRequest request,
        out CharacterCareerFocusBondingPreview? preview)
    {
        preview = null;
        var command = new CharacterCareerFocusBondingCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || !IsValidQuote(request, snapshot.Quote)) return false;
        preview = CreatePreview(command, snapshot.Quote); return true;
    }
    public static bool IsValidQuote(CharacterCareerFocusBondingRequest request, CharacterCareerFocusBondingQuote q)
    {
        var s = q.State;
        decimal price = checked(s.Force * q.EffectiveMultiplier + q.Addition);
        int rounded = decimal.ToInt32(price >= 0 ? decimal.Ceiling(price) : decimal.Floor(price));
        return s.GearId == request.GearId && s.GearId != Guid.Empty && s.SourceId != Guid.Empty
            && s.Force > 0 && s.Bonded == s.FocusId.HasValue && s.FocusId != Guid.Empty
            && SupportedName(s.Name) && s.Extra.Length is > 0 and <= 256 && s.Extra.Trim() == s.Extra
            && CharacterCreationFocusKarma1220.IsOrdinaryPriceField(s.Name, q.PriceField) && q.ProfileMultiplier >= 0
            && q.BindingKarma == rounded && s.BondedCount >= (s.Bonded ? 1 : 0)
            && s.TotalForce >= (s.Bonded ? s.Force : 0) && s.Magic >= 0
            && (s.AdeptMagic is null or >= 0) && IsDigest(q.SourceNodeDigest) && IsDigest(q.OwnedEffectsDigest)
            && CharacterCareerFocusBondingRules.Allows(s, request.Bonded)
            && q.ChangesState == (s.Bonded != request.Bonded)
            && q.KarmaCost == (q.ChangesState && request.Bonded ? q.BindingKarma : 0)
            && (!q.ChangesState || !request.Bonded || q.KarmaBefore >= q.KarmaCost)
            && q.KarmaAfter == checked(q.KarmaBefore - q.KarmaCost);
    }
    public static bool IsCoherent(CharacterCareerFocusBondingReceipt? receipt)
    {
        try
        {
            if (receipt is null || !IsConfirmedCommand(receipt.Command)
                || receipt.CommandDigest != CommandDigest(receipt.Command)
                || !IsDigest(receipt.CharacterPayloadDigestAfter) || !IsDigest(receipt.PreviousReceiptDigest)
                || receipt.ReceiptDigest != ReceiptDigest(receipt)) return false;
            if (receipt.NeverApplied) return receipt.Quote is null && receipt.ExpenseId is null
                && receipt.CommittedWorkspaceRevision > receipt.Command.Binding.WorkspaceRevision;
            return receipt.Quote is { } q && IsValidQuote(receipt.Command.Request, q)
                && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, q).PreviewDigest
                && receipt.ExpenseId == (q.ChangesState && receipt.Command.Request.Bonded ? ExpenseId(receipt.Command.Request) : (Guid?)null)
                && receipt.CommittedWorkspaceRevision == receipt.Command.Binding.WorkspaceRevision + 1;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, IReadOnlyList<CharacterCareerFocusBondingReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        var operations = new HashSet<Guid>(); var expenses = new HashSet<Guid>();
        string previous = LedgerRoot; long lastRevision = 0, bytes = 2;
        foreach (var row in receipts)
        {
            if (!IsCoherent(row) || row.Command.Request.WorkspaceId != id || row.CommittedWorkspaceRevision > revision
                || row.CommittedWorkspaceRevision <= lastRevision || row.PreviousReceiptDigest != previous
                || !operations.Add(row.Command.Request.OperationId) || row.ExpenseId is { } expense && !expenses.Add(expense)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(row).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            previous = row.ReceiptDigest; lastRevision = row.CommittedWorkspaceRevision;
        }
        return true;
    }
    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var rows = saved.Document.AuxiliaryState.CharacterCareerFocusBondingReceipts;
        return saved.LocalHistory is not null && IsValidLedger(saved.Id, saved.ContentRevision, rows)
            && (rows is not { Count: > 0 } || saved.SavedRevision >= rows[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != rows[^1].CommittedWorkspaceRevision || Hash(saved.Document.Content) == rows[^1].CharacterPayloadDigestAfter));
    }
    public static CharacterCareerFocusBondingResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved, Guid operationId, string digest)
    {
        if (!CharacterCareerContactsTransaction.IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerFocusBondingOutcome.Corrupt);
        var row = saved.Document.AuxiliaryState.CharacterCareerFocusBondingReceipts?.SingleOrDefault(x => x.Command.Request.OperationId == operationId);
        if (row is null) return new(CharacterCareerFocusBondingOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(row.CommittedWorkspaceRevision) && row.Command.Binding.OwnerId == owner.NormalizedValue
            && row.Command.Binding.LocalSingleUser == owner.IsLocalSingleUser && row.CommandDigest == digest
            && row.Command.Binding.LocalHistoryIncarnationId == saved.LocalHistory!.IncarnationId
            ? new(row.NeverApplied ? CharacterCareerFocusBondingOutcome.NeverApplied : CharacterCareerFocusBondingOutcome.Replayed, row, saved.ContentRevision)
            : new(CharacterCareerFocusBondingOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision);
    }
    public static bool TryBuild(OwnerContextStamp owner, WorkspaceStoredDocument saved, CharacterCareerFocusBondingSnapshot snapshot,
        CharacterCareerFocusBondingCommand command, out WorkspaceDocument? replacement, out CharacterCareerFocusBondingReceipt? receipt)
    {
        replacement = null; receipt = null;
        try
        {
            if (!OwnerMatches(owner, command) || !IsValidHistory(saved) || !CharacterCareerPowerGovernance.Allows(saved)
                || saved.Id != command.Request.WorkspaceId || saved.ContentRevision != command.Binding.WorkspaceRevision
                || saved.SavedRevision != saved.ContentRevision || saved.LocalHistory!.IncarnationId != command.Binding.LocalHistoryIncarnationId
                || saved.Document.AuxiliaryStateDigest != command.Binding.AuxiliaryStateDigest || SourceDigest(saved.Document) != command.Binding.SourceDigest
                || !TryPreview(snapshot, command.Request, out var preview) || preview!.Command with { ExplicitlyConfirmed = true } != command) return false;
            string payload = Apply(saved.Document.Content, command.Request, snapshot.Quote);
            return Append(saved, command, snapshot.Quote, payload, false, out replacement, out receipt);
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    public static bool TryBuildNeverApplied(OwnerContextStamp owner, WorkspaceStoredDocument saved, CharacterCareerFocusBondingCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerFocusBondingReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!SameOwner(owner, command) || !IsValidHistory(saved) || saved.Id != command.Request.WorkspaceId
            || saved.LocalHistory!.IncarnationId != command.Binding.LocalHistoryIncarnationId
            || saved.ContentRevision < command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || !saved.CanReplayReceipt(command.Binding.WorkspaceRevision)) return false;
        return Append(saved, command, null, saved.Document.Content, true, out replacement, out receipt);
    }
    private static bool Append(WorkspaceStoredDocument saved, CharacterCareerFocusBondingCommand command,
        CharacterCareerFocusBondingQuote? quote, string payload, bool neverApplied,
        out WorkspaceDocument? replacement, out CharacterCareerFocusBondingReceipt? receipt)
    {
        replacement = null; receipt = null;
        var history = saved.Document.AuxiliaryState.CharacterCareerFocusBondingReceipts;
        if (saved.ContentRevision == long.MaxValue || payload.Length > 67_108_864 || history is { Count: >= MaximumReceipts }
            || history?.Any(x => x.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        var candidate = new CharacterCareerFocusBondingReceipt(command, quote, CommandDigest(command),
            quote is { ChangesState: true } && command.Request.Bonded ? ExpenseId(command.Request) : null,
            saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "") { NeverApplied = neverApplied };
        candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
        var rows = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, rows)) return false;
        replacement = saved.Document with { State = saved.Document.State with { Payload = payload,
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerFocusBondingReceipts = Array.AsReadOnly(rows) } } };
        receipt = candidate; return true;
    }
    private static bool Malformed(Exception e) => e is XmlException or InvalidDataException or FormatException
        or OverflowException or ArgumentException or InvalidOperationException or NullReferenceException;
}
