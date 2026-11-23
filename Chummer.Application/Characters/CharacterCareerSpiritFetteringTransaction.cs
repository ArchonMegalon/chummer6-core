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

public sealed class CharacterCareerSpiritFetteringSnapshot
{
    internal CharacterCareerSpiritFetteringSnapshot(CharacterCareerSpiritFetteringBinding binding,
        CharacterCareerSpiritFetteringQuote quote, ICharacterSourceDataContext context)
        => (Binding, Quote, Context) = (binding, quote, context);
    public CharacterCareerSpiritFetteringBinding Binding { get; }
    public CharacterCareerSpiritFetteringQuote Quote { get; }
    internal ICharacterSourceDataContext Context { get; }
    internal CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? Current5724 { get; init; }
}

public static partial class CharacterCareerSpiritFetteringTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 8 * 1024 * 1024;
    private static string LedgerRoot => Hash("chummer.core.sr5-career-spirit-fettering-ledger/v1");
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => CharacterCareerContactsTransaction.IsDigest(value);
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerSpiritFetteringCommand command)
        => Hash("chummer.core.sr5-career-spirit-fettering-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerSpiritFetteringReceipt receipt)
        => Hash("chummer.core.sr5-career-spirit-fettering-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    public static Guid ExpenseId(CharacterCareerSpiritFetteringRequest request)
        => new(Hash(request.WorkspaceId.Value + "\0" + request.OperationId.ToString("D") + "\0spirit-fettering-expense")[..32]);
    public static string SourceDigest(WorkspaceDocument doc) => Hash(JsonSerializer.Serialize(new
        { doc.RulesetId, doc.SchemaVersion, doc.PayloadKind, PayloadDigest = Hash(doc.Content) }));
    public static bool IsValidRequest(CharacterCareerSpiritFetteringRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
        && request.OperationId != Guid.Empty && request.SpiritId != Guid.Empty
        && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
        && request.ExpenseDateLocal > DateTime.MinValue && request.ExpenseDateLocal < DateTime.MaxValue
        && (request.RequestedName is null || !string.IsNullOrWhiteSpace(request.RequestedName)
            && request.RequestedName.Length <= CharacterSpiritNameChoiceRules.MaximumNameLength
            && request.RequestedName.IndexOfAny(['\r', '\n', '\0']) < 0);
    public static bool IsBoundCommand(CharacterCareerSpiritFetteringCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } b
        && b.OwnerId is { Length: > 0 and <= 512 } && new OwnerScope(b.OwnerId).NormalizedValue == b.OwnerId
        && !string.IsNullOrWhiteSpace(b.OwnerAuthorityInstanceId) && b.OwnerTransitionRevision >= 0
        && b.WorkspaceId == command.Request.WorkspaceId && b.WorkspaceRevision is > 0 and < long.MaxValue
        && Guid.TryParseExact(b.LocalHistoryIncarnationId, "N", out var incarnation) && incarnation != Guid.Empty
        && incarnation.ToString("N") == b.LocalHistoryIncarnationId
        && IsDigest(b.SourceDigest) && IsDigest(b.AuxiliaryStateDigest) && IsDigest(b.RuleStateDigest) && IsDigest(b.RuntimeDigest);
    public static bool IsConfirmedCommand(CharacterCareerSpiritFetteringCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);
    public static bool OwnerMatches(OwnerContextStamp owner, CharacterCareerSpiritFetteringCommand command)
        => owner.IsValid && CharacterCareerContactsTransaction.IsValidOwner(owner.Owner) && IsConfirmedCommand(command) && owner.Owner.NormalizedValue == command.Binding.OwnerId
        && owner.Owner.IsLocalSingleUser == command.Binding.LocalSingleUser
        && owner.AuthorityInstanceId == command.Binding.OwnerAuthorityInstanceId
        && owner.TransitionRevision == command.Binding.OwnerTransitionRevision;
    public static bool SameOwner(OwnerContextStamp owner, CharacterCareerSpiritFetteringCommand command)
        => owner.IsValid && CharacterCareerContactsTransaction.IsValidOwner(owner.Owner) && IsConfirmedCommand(command) && owner.Owner.NormalizedValue == command.Binding.OwnerId
        && owner.Owner.IsLocalSingleUser == command.Binding.LocalSingleUser;
    public static CharacterCareerSpiritFetteringPreview CreatePreview(CharacterCareerSpiritFetteringCommand command,
        CharacterCareerSpiritFetteringQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-career-spirit-fettering-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }
    public static bool TryPreview(CharacterCareerSpiritFetteringSnapshot snapshot, CharacterCareerSpiritFetteringRequest request,
        out CharacterCareerSpiritFetteringPreview? preview)
    {
        preview = null;
        var command = new CharacterCareerSpiritFetteringCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || !IsValidQuote(request, snapshot.Quote)) return false;
        preview = CreatePreview(command, snapshot.Quote); return true;
    }
    public static bool IsValidQuote(CharacterCareerSpiritFetteringRequest request, CharacterCareerSpiritFetteringQuote q)
    {
        var s = q.State;
        if (request.RequestedName is { } name)
            return s.SpiritId == request.SpiritId && s.Created && s.EntityType is "Spirit" or "Sprite"
                && s.Force >= 0 && s.Services >= 0 && request.Fettered == s.Fettered
                && q.TargetName == name && q.ChangesState == (q.Name != name)
                && q.KarmaCost == 0 && q.KarmaBefore == s.AvailableKarma && q.KarmaAfter == q.KarmaBefore;
        return q.TargetName is null && s.SpiritId == request.SpiritId && s.Created && s.EntityType is "Spirit" or "Sprite"
            && s.Force >= 0 && s.Services >= 0 && s.ActivationCostExact && q.ProfileKarmaSpiritFettering >= 0
            && s.ActivationKarmaCost == (s.EntityType == "Sprite" ? s.Force : checked(s.Force * q.ProfileKarmaSpiritFettering))
            && CharacterSpiritFetteringRules.CanSet(s, request.Fettered)
            && q.ChangesState == (s.Fettered != request.Fettered)
            && q.KarmaCost == (q.ChangesState && request.Fettered ? s.ActivationKarmaCost : 0)
            && q.KarmaBefore == s.AvailableKarma && q.KarmaAfter == checked(q.KarmaBefore - q.KarmaCost);
    }
    public static bool IsCoherent(CharacterCareerSpiritFetteringReceipt? receipt)
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
                && receipt.ExpenseId == (q.ChangesState && receipt.Command.Request.RequestedName is null && receipt.Command.Request.Fettered ? ExpenseId(receipt.Command.Request) : (Guid?)null)
                && receipt.CommittedWorkspaceRevision == receipt.Command.Binding.WorkspaceRevision + 1;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, IReadOnlyList<CharacterCareerSpiritFetteringReceipt>? receipts)
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
        var rows = saved.Document.AuxiliaryState.CharacterCareerSpiritFetteringReceipts;
        return saved.LocalHistory is not null && IsValidLedger(saved.Id, saved.ContentRevision, rows)
            && (rows is not { Count: > 0 } || saved.SavedRevision >= rows[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != rows[^1].CommittedWorkspaceRevision || Hash(saved.Document.Content) == rows[^1].CharacterPayloadDigestAfter));
    }
    public static CharacterCareerSpiritFetteringResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved, Guid operationId, string digest)
    {
        if (!CharacterCareerContactsTransaction.IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerSpiritFetteringOutcome.Corrupt);
        var row = saved.Document.AuxiliaryState.CharacterCareerSpiritFetteringReceipts?.SingleOrDefault(x => x.Command.Request.OperationId == operationId);
        if (row is null) return new(CharacterCareerSpiritFetteringOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(row.CommittedWorkspaceRevision) && row.Command.Binding.OwnerId == owner.NormalizedValue
            && row.Command.Binding.LocalSingleUser == owner.IsLocalSingleUser && row.CommandDigest == digest
            && row.Command.Binding.LocalHistoryIncarnationId == saved.LocalHistory!.IncarnationId
            ? new(row.NeverApplied ? CharacterCareerSpiritFetteringOutcome.NeverApplied : CharacterCareerSpiritFetteringOutcome.Replayed, row, saved.ContentRevision)
            : new(CharacterCareerSpiritFetteringOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision);
    }
    public static bool TryBuild(OwnerContextStamp owner, WorkspaceStoredDocument saved, CharacterCareerSpiritFetteringSnapshot snapshot,
        CharacterCareerSpiritFetteringCommand command, out WorkspaceDocument? replacement, out CharacterCareerSpiritFetteringReceipt? receipt)
    {
        replacement = null; receipt = null;
        try
        {
            if (!OwnerMatches(owner, command) || !IsValidHistory(saved) || !CharacterCareerPowerGovernance.Allows(saved)
                || saved.Id != command.Request.WorkspaceId || saved.ContentRevision != command.Binding.WorkspaceRevision
                || saved.SavedRevision != saved.ContentRevision || saved.LocalHistory!.IncarnationId != command.Binding.LocalHistoryIncarnationId
                || saved.Document.AuxiliaryStateDigest != command.Binding.AuxiliaryStateDigest || SourceDigest(saved.Document) != command.Binding.SourceDigest
                || !TryPreview(snapshot, command.Request, out var preview) || preview!.Command with { ExplicitlyConfirmed = true } != command) return false;
            if (snapshot.Current5724 is { } current && !current.Matches(saved, snapshot.Context)) return false;
            string payload = Apply(saved.Document.Content, command.Request, snapshot.Quote);
            if (snapshot.Current5724 is not null && snapshot.Quote.ChangesState)
                payload = CharacterCareerLearningDocument5703.PreserveEnvelope(saved.Document.Content,
                    Parse(payload, "character").ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
            return Append(saved, command, snapshot.Quote, payload, false, out replacement, out receipt);
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    public static bool TryBuildNeverApplied(OwnerContextStamp owner, WorkspaceStoredDocument saved, CharacterCareerSpiritFetteringCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerSpiritFetteringReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!SameOwner(owner, command) || !IsValidHistory(saved) || saved.Id != command.Request.WorkspaceId
            || saved.LocalHistory!.IncarnationId != command.Binding.LocalHistoryIncarnationId
            || saved.ContentRevision < command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || !saved.CanReplayReceipt(command.Binding.WorkspaceRevision)) return false;
        return Append(saved, command, null, saved.Document.Content, true, out replacement, out receipt);
    }
    private static bool Append(WorkspaceStoredDocument saved, CharacterCareerSpiritFetteringCommand command,
        CharacterCareerSpiritFetteringQuote? quote, string payload, bool neverApplied,
        out WorkspaceDocument? replacement, out CharacterCareerSpiritFetteringReceipt? receipt)
    {
        replacement = null; receipt = null;
        var history = saved.Document.AuxiliaryState.CharacterCareerSpiritFetteringReceipts;
        if (saved.ContentRevision == long.MaxValue || payload.Length > 67_108_864 || history is { Count: >= MaximumReceipts }
            || history?.Any(x => x.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        var candidate = new CharacterCareerSpiritFetteringReceipt(command, quote, CommandDigest(command),
            quote is { ChangesState: true } && command.Request.RequestedName is null && command.Request.Fettered ? ExpenseId(command.Request) : null,
            saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "") { NeverApplied = neverApplied };
        candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
        var rows = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, rows)) return false;
        replacement = saved.Document with { State = saved.Document.State with { Payload = payload,
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerSpiritFetteringReceipts = Array.AsReadOnly(rows) } } };
        receipt = candidate; return true;
    }
    private static bool Malformed(Exception e) => e is XmlException or InvalidDataException or FormatException
        or OverflowException or ArgumentException or InvalidOperationException or NullReferenceException;
}
