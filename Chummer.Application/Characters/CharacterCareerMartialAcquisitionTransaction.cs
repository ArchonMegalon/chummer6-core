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

public sealed class CharacterCareerMartialAcquisitionSnapshot
{
    internal CharacterCareerMartialAcquisitionSnapshot(CharacterCareerMartialAcquisitionBinding binding,
        CharacterCareerMartialAcquisitionQuote quote, ICharacterSourceDataContext context)
        => (Binding, Quote, Context) = (binding, quote, context);
    public CharacterCareerMartialAcquisitionBinding Binding { get; }
    public CharacterCareerMartialAcquisitionQuote Quote { get; }
    internal ICharacterSourceDataContext Context { get; }
}

public static partial class CharacterCareerMartialAcquisitionTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 8 * 1024 * 1024;
    private static string LedgerRoot => Hash("chummer.core.sr5-career-martial-acquisition-ledger/v1");
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => CharacterCareerContactsTransaction.IsDigest(value);
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerMartialAcquisitionCommand command)
        => Hash("chummer.core.sr5-career-martial-acquisition-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerMartialAcquisitionReceipt receipt)
        => Hash("chummer.core.sr5-career-martial-acquisition-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    public static Guid InstanceId(CharacterCareerMartialAcquisitionRequest request, string domain)
        => new(Hash("sr5-career-martial-acquisition-instance/v1\0" + request.WorkspaceId.Value + "\0"
            + request.OperationId.ToString("D") + "\0" + domain)[..32]);
    public static Guid ExpenseId(CharacterCareerMartialAcquisitionRequest request)
        => new(Hash(request.WorkspaceId.Value + "\0" + request.OperationId.ToString("D") + "\0martial-acquisition-expense")[..32]);
    public static string SourceDigest(WorkspaceDocument doc) => Hash(JsonSerializer.Serialize(new
        { doc.RulesetId, doc.SchemaVersion, doc.PayloadKind, PayloadDigest = Hash(doc.Content) }));
    public static bool IsValidRequest(CharacterCareerMartialAcquisitionRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
        && request.OperationId != Guid.Empty && request.SourceId != Guid.Empty && Enum.IsDefined(request.Kind)
        && (request.Kind == CharacterCareerMartialAcquisitionKind.Art ? request.ParentArtId is null : request.ParentArtId is { } parent && parent != Guid.Empty)
        && IsValidCatalogBinding(request.CatalogBinding, request.WorkspaceId)
        && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
        && request.ExpenseDateLocal > DateTime.MinValue && request.ExpenseDateLocal < DateTime.MaxValue;
    public static bool IsBoundCommand(CharacterCareerMartialAcquisitionCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } b
        && b.OwnerId is { Length: > 0 and <= 512 } && new OwnerScope(b.OwnerId).NormalizedValue == b.OwnerId
        && !string.IsNullOrWhiteSpace(b.OwnerAuthorityInstanceId) && b.OwnerTransitionRevision >= 0
        && b.WorkspaceId == command.Request.WorkspaceId && b.WorkspaceRevision is > 0 and < long.MaxValue
        && Guid.TryParseExact(b.LocalHistoryIncarnationId, "N", out var incarnation) && incarnation != Guid.Empty
        && incarnation.ToString("N") == b.LocalHistoryIncarnationId
        && IsDigest(b.SourceDigest) && IsDigest(b.AuxiliaryStateDigest) && IsDigest(b.RuleStateDigest) && IsDigest(b.RuntimeDigest);
    public static bool IsConfirmedCommand(CharacterCareerMartialAcquisitionCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);
    public static bool OwnerMatches(OwnerContextStamp owner, CharacterCareerMartialAcquisitionCommand command)
        => owner.IsValid && CharacterCareerContactsTransaction.IsValidOwner(owner.Owner) && IsConfirmedCommand(command) && owner.Owner.NormalizedValue == command.Binding.OwnerId
        && owner.Owner.IsLocalSingleUser == command.Binding.LocalSingleUser
        && owner.AuthorityInstanceId == command.Binding.OwnerAuthorityInstanceId
        && owner.TransitionRevision == command.Binding.OwnerTransitionRevision;
    public static bool SameOwner(OwnerContextStamp owner, CharacterCareerMartialAcquisitionCommand command)
        => owner.IsValid && CharacterCareerContactsTransaction.IsValidOwner(owner.Owner) && IsConfirmedCommand(command) && owner.Owner.NormalizedValue == command.Binding.OwnerId
        && owner.Owner.IsLocalSingleUser == command.Binding.LocalSingleUser;
    public static CharacterCareerMartialAcquisitionPreview CreatePreview(CharacterCareerMartialAcquisitionCommand command,
        CharacterCareerMartialAcquisitionQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-career-martial-acquisition-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }
    public static bool TryPreview(CharacterCareerMartialAcquisitionSnapshot snapshot, CharacterCareerMartialAcquisitionRequest request,
        out CharacterCareerMartialAcquisitionPreview? preview)
    {
        preview = null;
        var command = new CharacterCareerMartialAcquisitionCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || !IsValidQuote(request, snapshot.Quote)) return false;
        preview = CreatePreview(command, snapshot.Quote); return true;
    }
    public static bool IsValidQuote(CharacterCareerMartialAcquisitionRequest request, CharacterCareerMartialAcquisitionQuote q)
    {
        try
        {
            return IsValidRequest(request) && q.SourceId == request.SourceId && q.Kind == request.Kind
                && q.ParentArtId == request.ParentArtId && q.Name.Length is > 0 and <= 512
                && q.Book.Length is > 0 and <= 128 && q.Page.Length is > 0 and <= 128
                && q.KarmaCost >= 0 && q.KarmaBefore >= q.KarmaCost && q.KarmaAfter == q.KarmaBefore - q.KarmaCost
                && q.TechniqueRate >= 0 && q.SourceXml.Length is > 0 and <= 262144
                && GuidValue(Parse(q.SourceXml, request.Kind == CharacterCareerMartialAcquisitionKind.Art ? "martialart" : "technique"), "id") == request.SourceId
                && (request.Kind == CharacterCareerMartialAcquisitionKind.Art ? q.ParentSourceXml == "" && !q.FirstTechnique
                    : q.ParentName.Length > 0 && q.ParentSourceXml.Length is > 0 and <= 262144 && q.KarmaCost == (q.FirstTechnique ? 0 : q.TechniqueRate))
                && q.Effects.Count <= 256 && q.Effects.All(e => e.Type == "UnarmedReach"
                    ? e.SkillId is null && e.SkillKey == "" && e.SpecializationId is null
                    : e.Type is "SkillSpecializationOption" or "SkillSpecialization" && e.SkillId is { } id && id != Guid.Empty
                        && e.SkillKey.Length > 0 && e.Specialization.Length > 0 && e.Value == 0
                        && (e.Type == "SkillSpecialization" ? e.SpecializationId is { } spec && spec != Guid.Empty : e.SpecializationId is null));
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }

    public static bool IsCoherent(CharacterCareerMartialAcquisitionReceipt? receipt)
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
                && receipt.ExpenseId == ExpenseId(receipt.Command.Request)
                && receipt.CommittedWorkspaceRevision == receipt.Command.Binding.WorkspaceRevision + 1;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, IReadOnlyList<CharacterCareerMartialAcquisitionReceipt>? receipts)
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
        var rows = saved.Document.AuxiliaryState.CharacterCareerMartialAcquisitionReceipts;
        return saved.LocalHistory is not null && IsValidLedger(saved.Id, saved.ContentRevision, rows)
            && (rows is not { Count: > 0 } || saved.SavedRevision >= rows[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != rows[^1].CommittedWorkspaceRevision || Hash(saved.Document.Content) == rows[^1].CharacterPayloadDigestAfter));
    }
    public static CharacterCareerMartialAcquisitionResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved, Guid operationId, string digest)
    {
        if (!CharacterCareerContactsTransaction.IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerMartialAcquisitionOutcome.Corrupt);
        var row = saved.Document.AuxiliaryState.CharacterCareerMartialAcquisitionReceipts?.SingleOrDefault(x => x.Command.Request.OperationId == operationId);
        if (row is null) return new(CharacterCareerMartialAcquisitionOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(row.CommittedWorkspaceRevision) && row.Command.Binding.OwnerId == owner.NormalizedValue
            && row.Command.Binding.LocalSingleUser == owner.IsLocalSingleUser && row.CommandDigest == digest
            && row.Command.Binding.LocalHistoryIncarnationId == saved.LocalHistory!.IncarnationId
            ? new(row.NeverApplied ? CharacterCareerMartialAcquisitionOutcome.NeverApplied : CharacterCareerMartialAcquisitionOutcome.Replayed, row, saved.ContentRevision)
            : new(CharacterCareerMartialAcquisitionOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision);
    }
    public static bool TryBuild(OwnerContextStamp owner, WorkspaceStoredDocument saved, CharacterCareerMartialAcquisitionSnapshot snapshot,
        CharacterCareerMartialAcquisitionCommand command, out WorkspaceDocument? replacement, out CharacterCareerMartialAcquisitionReceipt? receipt)
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
    public static bool TryBuildNeverApplied(OwnerContextStamp owner, WorkspaceStoredDocument saved, CharacterCareerMartialAcquisitionCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerMartialAcquisitionReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!SameOwner(owner, command) || !IsValidHistory(saved) || saved.Id != command.Request.WorkspaceId
            || saved.LocalHistory!.IncarnationId != command.Binding.LocalHistoryIncarnationId
            || saved.ContentRevision < command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || !saved.CanReplayReceipt(command.Binding.WorkspaceRevision)) return false;
        return Append(saved, command, null, saved.Document.Content, true, out replacement, out receipt);
    }
    private static bool Append(WorkspaceStoredDocument saved, CharacterCareerMartialAcquisitionCommand command,
        CharacterCareerMartialAcquisitionQuote? quote, string payload, bool neverApplied,
        out WorkspaceDocument? replacement, out CharacterCareerMartialAcquisitionReceipt? receipt)
    {
        replacement = null; receipt = null;
        var history = saved.Document.AuxiliaryState.CharacterCareerMartialAcquisitionReceipts;
        if (saved.ContentRevision == long.MaxValue || payload.Length > 67_108_864 || history is { Count: >= MaximumReceipts }
            || history?.Any(x => x.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        var candidate = new CharacterCareerMartialAcquisitionReceipt(command, quote, CommandDigest(command),
            quote is not null ? ExpenseId(command.Request) : null,
            saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "") { NeverApplied = neverApplied };
        candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
        var rows = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, rows)) return false;
        replacement = saved.Document with { State = saved.Document.State with { Payload = payload,
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerMartialAcquisitionReceipts = Array.AsReadOnly(rows) } } };
        receipt = candidate; return true;
    }
    private static bool Malformed(Exception e) => e is XmlException or InvalidDataException or FormatException
        or OverflowException or ArgumentException or InvalidOperationException or NullReferenceException;
}
