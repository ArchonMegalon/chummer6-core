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

public sealed class CharacterCareerFreeSpriteConversionSnapshot
{
    internal CharacterCareerFreeSpriteConversionSnapshot(CharacterCareerFreeSpriteConversionBinding binding,
        CharacterCareerFreeSpriteConversionQuote quote, ICharacterSourceDataContext context)
        => (Binding, Quote, Context) = (binding, quote, context);
    public CharacterCareerFreeSpriteConversionBinding Binding { get; }
    public CharacterCareerFreeSpriteConversionQuote Quote { get; }
    internal ICharacterSourceDataContext Context { get; }
    internal CharacterCurrentRuleEnvironmentContext? Current5732 { get; init; }
}

public static partial class CharacterCareerFreeSpriteConversionTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 8 * 1024 * 1024;
    private static string LedgerRoot => Hash("chummer.core.sr5-career-free-sprite-conversion-ledger/v1");
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => CharacterCareerContactsTransaction.IsDigest(value);
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerFreeSpriteConversionCommand command)
        => Hash("chummer.core.sr5-career-free-sprite-conversion-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerFreeSpriteConversionReceipt receipt)
        => Hash("chummer.core.sr5-career-free-sprite-conversion-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    public static string SourceDigest(WorkspaceDocument doc) => Hash(JsonSerializer.Serialize(new
        { doc.RulesetId, doc.SchemaVersion, doc.PayloadKind, PayloadDigest = Hash(doc.Content) }));
    public static bool IsValidRequest(CharacterCareerFreeSpriteConversionRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
        && request.OperationId != Guid.Empty && request.CritterPowerId != Guid.Empty
        && request.ExpectedWorkspaceRevision is > 0 and < long.MaxValue;
    public static bool IsBoundCommand(CharacterCareerFreeSpriteConversionCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } b
        && b.OwnerId is { Length: > 0 and <= 512 } && new OwnerScope(b.OwnerId).NormalizedValue == b.OwnerId
        && !string.IsNullOrWhiteSpace(b.OwnerAuthorityInstanceId) && b.OwnerTransitionRevision >= 0
        && b.WorkspaceId == command.Request.WorkspaceId && b.WorkspaceRevision is > 0 and < long.MaxValue
        && b.WorkspaceRevision == command.Request.ExpectedWorkspaceRevision
        && Guid.TryParseExact(b.LocalHistoryIncarnationId, "N", out var incarnation) && incarnation != Guid.Empty
        && incarnation.ToString("N") == b.LocalHistoryIncarnationId
        && IsDigest(b.SourceDigest) && IsDigest(b.AuxiliaryStateDigest) && IsDigest(b.RuleStateDigest) && IsDigest(b.RuntimeDigest);
    public static bool IsConfirmedCommand(CharacterCareerFreeSpriteConversionCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);
    public static bool OwnerMatches(OwnerContextStamp owner, CharacterCareerFreeSpriteConversionCommand command)
        => owner.IsValid && CharacterCareerContactsTransaction.IsValidOwner(owner.Owner) && IsConfirmedCommand(command) && owner.Owner.NormalizedValue == command.Binding.OwnerId
        && owner.Owner.IsLocalSingleUser == command.Binding.LocalSingleUser
        && owner.AuthorityInstanceId == command.Binding.OwnerAuthorityInstanceId
        && owner.TransitionRevision == command.Binding.OwnerTransitionRevision;
    public static CharacterCareerFreeSpriteConversionPreview CreatePreview(CharacterCareerFreeSpriteConversionCommand command,
        CharacterCareerFreeSpriteConversionQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-career-free-sprite-conversion-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }
    public static bool TryPreview(CharacterCareerFreeSpriteConversionSnapshot snapshot, CharacterCareerFreeSpriteConversionRequest request,
        out CharacterCareerFreeSpriteConversionPreview? preview)
    {
        preview = null;
        var command = new CharacterCareerFreeSpriteConversionCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || !IsValidQuote(request, snapshot.Quote)) return false;
        preview = CreatePreview(command, snapshot.Quote); return true;
    }
    public static bool IsValidQuote(CharacterCareerFreeSpriteConversionRequest request, CharacterCareerFreeSpriteConversionQuote q)
        => q.State.Created && q.Identity.CritterPowerId == request.CritterPowerId
        && CharacterFreeSpriteConversionRules.TryValidateMutation(q.State, q.Identity, q.State.Revision)
        && q.CategoryAfter == CharacterFreeSpriteConversionRules.FreeSpriteCategory
        && q.DenialSourceId == CharacterFreeSpriteConversionRules.DenialSourceId
        && !string.IsNullOrWhiteSpace(q.DenialName) && !string.IsNullOrWhiteSpace(q.DenialSourceBook)
        && q.KarmaAfter == q.KarmaBefore && q.NuyenAfter == q.NuyenBefore
        && IsDigest(q.DenialSourceDigest) && !string.IsNullOrWhiteSpace(q.DenialPowerXml)
        && q.DenialEffectsXml is not null;
    public static bool IsCoherent(CharacterCareerFreeSpriteConversionReceipt? receipt)
    {
        try
        {
            if (receipt is null || !IsConfirmedCommand(receipt.Command)
                || receipt.CommandDigest != CommandDigest(receipt.Command)
                || !IsDigest(receipt.CharacterPayloadDigestAfter) || !IsDigest(receipt.PreviousReceiptDigest)
                || receipt.ReceiptDigest != ReceiptDigest(receipt)) return false;
            return receipt.Quote is { } q && IsValidQuote(receipt.Command.Request, q)
                && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, q).PreviewDigest
                && receipt.CommittedWorkspaceRevision == receipt.Command.Binding.WorkspaceRevision + 1;
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, IReadOnlyList<CharacterCareerFreeSpriteConversionReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        var operations = new HashSet<Guid>();
        string previous = LedgerRoot; long lastRevision = 0, bytes = 2;
        foreach (var row in receipts)
        {
            if (!IsCoherent(row) || row.Command.Request.WorkspaceId != id || row.CommittedWorkspaceRevision > revision
                || row.CommittedWorkspaceRevision <= lastRevision || row.PreviousReceiptDigest != previous
                || !operations.Add(row.Command.Request.OperationId)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(row).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            previous = row.ReceiptDigest; lastRevision = row.CommittedWorkspaceRevision;
        }
        return true;
    }
    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var rows = saved.Document.AuxiliaryState.CharacterCareerFreeSpriteConversionReceipts;
        return saved.LocalHistory is not null && IsValidLedger(saved.Id, saved.ContentRevision, rows)
            && (rows is not { Count: > 0 } || saved.SavedRevision >= rows[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != rows[^1].CommittedWorkspaceRevision || Hash(saved.Document.Content) == rows[^1].CharacterPayloadDigestAfter));
    }
    public static CharacterCareerFreeSpriteConversionResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved, Guid operationId, string digest)
    {
        if (!CharacterCareerContactsTransaction.IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerFreeSpriteConversionOutcome.Corrupt);
        var row = saved.Document.AuxiliaryState.CharacterCareerFreeSpriteConversionReceipts?.SingleOrDefault(x => x.Command.Request.OperationId == operationId);
        if (row is null) return new(CharacterCareerFreeSpriteConversionOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(row.CommittedWorkspaceRevision) && row.Command.Binding.OwnerId == owner.NormalizedValue
            && row.Command.Binding.LocalSingleUser == owner.IsLocalSingleUser && row.CommandDigest == digest
            && row.Command.Binding.LocalHistoryIncarnationId == saved.LocalHistory!.IncarnationId
            ? new(CharacterCareerFreeSpriteConversionOutcome.Replayed, row, saved.ContentRevision)
            : new(CharacterCareerFreeSpriteConversionOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision);
    }
    public static bool TryBuild(OwnerContextStamp owner, WorkspaceStoredDocument saved, CharacterCareerFreeSpriteConversionSnapshot snapshot,
        CharacterCareerFreeSpriteConversionCommand command, out WorkspaceDocument? replacement, out CharacterCareerFreeSpriteConversionReceipt? receipt)
    {
        replacement = null; receipt = null;
        try
        {
            if (!OwnerMatches(owner, command) || !IsValidHistory(saved) || !CharacterCareerPowerGovernance.Allows(saved)
                || saved.Id != command.Request.WorkspaceId || saved.ContentRevision != command.Binding.WorkspaceRevision
                || saved.SavedRevision != saved.ContentRevision || saved.LocalHistory!.IncarnationId != command.Binding.LocalHistoryIncarnationId
                || saved.Document.AuxiliaryStateDigest != command.Binding.AuxiliaryStateDigest || SourceDigest(saved.Document) != command.Binding.SourceDigest
                || !TryPreview(snapshot, command.Request, out var preview) || preview!.Command with { ExplicitlyConfirmed = true } != command) return false;
            if (snapshot.Current5732 is { } current && (!ReferenceEquals(current.Current, saved) || !current.IsUnchanged || !current.VerifyRetainedArchive())) return false;
            string payload = Apply(saved.Document.Content, command.Request, snapshot.Quote);
            return Append(saved, command, snapshot.Quote, payload, out replacement, out receipt);
        }
        catch (Exception e) when (Malformed(e)) { return false; }
    }
    private static bool Append(WorkspaceStoredDocument saved, CharacterCareerFreeSpriteConversionCommand command,
        CharacterCareerFreeSpriteConversionQuote quote, string payload,
        out WorkspaceDocument? replacement, out CharacterCareerFreeSpriteConversionReceipt? receipt)
    {
        replacement = null; receipt = null;
        var history = saved.Document.AuxiliaryState.CharacterCareerFreeSpriteConversionReceipts;
        if (saved.ContentRevision == long.MaxValue || payload.Length > 67_108_864 || history is { Count: >= MaximumReceipts }
            || history?.Any(x => x.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        var candidate = new CharacterCareerFreeSpriteConversionReceipt(command, quote, CommandDigest(command),
            saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
        candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
        var rows = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, rows)) return false;
        replacement = saved.Document with { State = saved.Document.State with { Payload = payload,
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerFreeSpriteConversionReceipts = Array.AsReadOnly(rows) } } };
        receipt = candidate; return true;
    }
    private static bool Malformed(Exception e) => e is XmlException or InvalidDataException or FormatException
        or OverflowException or ArgumentException or InvalidOperationException or NullReferenceException;
}
