using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCareerImplantCommerceTransaction
{
    public const int MaximumReceipts = 1024;
    private static string LedgerRoot => Hash("chummer.sr5-career-implant-commerce-ledger/v1");
    public static string Hash(string value) => CharacterCyberwarePurchaseRules.ComputeCharacterDigest(value);
    public static string Digest<T>(T value) => Hash(JsonSerializer.Serialize(value));
    public static bool IsDigest(string? value) => CharacterCyberwarePurchaseRules.IsCanonicalDigest(value);
    public static string CommandDigest(CharacterCareerImplantCommerceCommand command) => Digest(command);
    public static string ReceiptDigest(CharacterCareerImplantCommerceReceipt receipt) => Digest(receipt with { ReceiptDigest = "" });

    // Receipt/expense identity is fixed by review. Revalidation must never allocate a new GUID.
    public static Guid BookkeepingInstanceId(Guid expenseId, string sourceId)
        => Guid.ParseExact(Hash("chummer.sr5-career-implant-bookkeeping/v1\0" + expenseId.ToString("D") + "\0" + sourceId)[..32], "N");

    public static CharacterCyberwareCommerceQuote? QuoteCanonical(CharacterCyberwareCommerceSnapshot snapshot,
        string action, decimal refund, bool createHole = false, bool createAntiHole = false)
    {
        if (createHole && snapshot.EssenceHoleRating.HasValue || createAntiHole && snapshot.EssenceAntiHoleRating.HasValue)
            return null;
        // Zero is the initial balance of an authenticated, newly materialized object, not a new formula.
        // Keep the saved snapshot's absence in the receipt; only the existing planner sees that balance.
        var planning = snapshot with
        {
            EssenceHoleRating = createHole ? 0 : snapshot.EssenceHoleRating,
            EssenceAntiHoleRating = createAntiHole ? 0 : snapshot.EssenceAntiHoleRating
        };
        var semantics = new CharacterCyberwareCommerceSemantics(true, "", true, "", planning);
        if (action.StartsWith("rating:", StringComparison.Ordinal))
        {
            string value = action[7..];
            return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int rating)
                && value == rating.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && rating > snapshot.CurrentRating && rating >= snapshot.MinimumRating && rating <= snapshot.MaximumRating
                ? CharacterCyberwareCommerceRules.QuoteUpgrade(semantics, snapshot.CurrentGradeId, rating, refund, false)
                : null;
        }
        return action == "sell" ? CharacterCyberwareCommerceRules.QuoteSale(semantics, refund)
            : action.StartsWith("grade:", StringComparison.Ordinal) && action[6..] != snapshot.CurrentGradeId
                && snapshot.GradeOptions.Any(g => g.Id == action[6..])
                ? CharacterCyberwareCommerceRules.QuoteUpgrade(semantics, action[6..], snapshot.CurrentRating, refund, false)
                : null;
    }
    // Higher-cost replacement may consume an already saved Hole, but cannot invent
    // an Anti-Hole to conceal new Essence loss. Total Essence must remain exactly equal.
    public static bool PreservesEssence(CharacterCyberwareCommerceSnapshot snapshot, CharacterCyberwareCommerceQuote quote)
        => (quote.EssenceDelta <= 0m
            || quote.EssenceDelta <= snapshot.EssenceHoleRating.GetValueOrDefault() / 100m
                && quote.NewEssenceAntiHoleRating == snapshot.EssenceAntiHoleRating)
            && checked(quote.CurrentEssence + (snapshot.EssenceHoleRating.GetValueOrDefault()
                - (decimal)snapshot.EssenceAntiHoleRating.GetValueOrDefault()) / 100m)
                == checked(quote.NewEssence + (quote.NewEssenceHoleRating.GetValueOrDefault()
                    - (decimal)quote.NewEssenceAntiHoleRating.GetValueOrDefault()) / 100m);

    public static bool IsOwner(OwnerScope owner) => CharacterCareerGearPurchaseTransaction.IsValidOwner(owner);
    public static bool IsRequest(CharacterCareerImplantCommerceRequest? r) => r is not null
        && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(r.WorkspaceId)
        && r.OperationId != Guid.Empty && r.InstanceId != Guid.Empty && r.ExpenseId != Guid.Empty
        && new[] { r.OperationId, r.InstanceId, r.ExpenseId }.Distinct().Count() == 3
        && r.ExpenseDate.Offset == TimeSpan.Zero && r.ExpenseDate.Ticks % TimeSpan.TicksPerSecond == 0
        && r.ActionId is { Length: > 0 and <= 64 }
        && CharacterCyberwareCommerceRules.TryNormalizeRefundPercentage(r.RefundPercentage, out _);
    public static bool IsCommand(CharacterCareerImplantCommerceCommand? command) => command is not null
        && IsRequest(command.Request) && command.Binding is { } b && command.ExplicitlyConfirmed
        && b.OwnerId is { Length: > 0 and <= 512 } && b.OwnerAuthorityInstanceId is { Length: > 0 and <= 512 }
        && b.OwnerTransitionRevision >= 0 && b.WorkspaceId == command.Request.WorkspaceId
        && b.ContentRevision is > 0 and < long.MaxValue && b.SavedRevision == b.ContentRevision
        && (b.LocalHistoryIncarnationId is null || Guid.TryParseExact(b.LocalHistoryIncarnationId, "N", out var incarnation)
            && incarnation != Guid.Empty && incarnation.ToString("N") == b.LocalHistoryIncarnationId)
        && !string.IsNullOrWhiteSpace(b.SettingsProfileId)
        && new[] { command.PreviewDigest, b.DocumentDigest, b.AuxiliaryDigest, b.CatalogDigest, b.SourceDigest, b.RuntimeDigest }.All(IsDigest);
    public static bool StampMatches(OwnerContextStamp stamp, CharacterCareerImplantCommerceBinding b)
        => stamp.IsValid && IsOwner(stamp.Owner) && stamp.Owner.NormalizedValue == b.OwnerId
            && stamp.Owner.IsLocalSingleUser == b.LocalSingleUser && stamp.AuthorityInstanceId == b.OwnerAuthorityInstanceId
            && stamp.TransitionRevision == b.OwnerTransitionRevision;

    // Cold finalization admits a NEW current stamp for the SAME owner, never an old stamp or a different owner.
    public static bool OwnerMatches(OwnerContextStamp current, CharacterCareerImplantCommerceCommand? command)
        => IsCommand(command) && current.IsValid && IsOwner(current.Owner)
            && current.Owner.NormalizedValue == command!.Binding.OwnerId
            && current.Owner.IsLocalSingleUser == command.Binding.LocalSingleUser;

    private static string DocumentDigest(WorkspaceDocument d) => Digest(new { d.Format, d.RulesetId, d.SchemaVersion, d.PayloadKind, d.Content });
    public static CharacterCareerImplantCommerceBinding Bind(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerImplantCommercePreparation preparation, string runtime)
        => new(owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser, owner.AuthorityInstanceId, owner.TransitionRevision,
            saved.Id, saved.ContentRevision, saved.SavedRevision, DocumentDigest(saved.Document), saved.Document.AuxiliaryStateDigest,
            preparation.CatalogDigest, preparation.SettingsProfileId, preparation.SourceDigest, runtime)
            { LocalHistoryIncarnationId = saved.LocalHistory?.IncarnationId };

    public static bool WorkspaceCurrent(WorkspaceStoredDocument saved, CharacterCareerImplantCommerceBinding b)
        => saved.Id == b.WorkspaceId && saved.ContentRevision == b.ContentRevision && saved.SavedRevision == b.SavedRevision
            && saved.SavedRevision == saved.ContentRevision && DocumentDigest(saved.Document) == b.DocumentDigest
            && saved.Document.AuxiliaryStateDigest == b.AuxiliaryDigest;
    public static bool IsWorkspace(WorkspaceStoredDocument saved) => saved.LocalHistory is not null
        && saved.LocalHistory.IsValid(saved.ContentRevision) && saved.ContentRevision is > 0 and < long.MaxValue
        && saved.SavedRevision == saved.ContentRevision && saved.Document.Format == WorkspaceDocumentFormat.NativeXml
        && saved.Document.RulesetId == "sr5" && saved.Document.SchemaVersion == 1
        && saved.Document.PayloadKind is "workspace" or "sr5/chum5-xml" && IsValidHistory(saved);

    public static string PreviewDigest(CharacterCareerImplantCommerceRequest request,
        CharacterCareerImplantCommerceBinding binding, CharacterCareerImplantCommerceQuote quote)
        => Digest(new { Schema = "chummer.sr5-career-implant-commerce-preview/v1", Request = request, Binding = binding, Quote = quote });

    public static bool IsCoherent(CharacterCareerImplantCommerceReceipt? receipt)
    {
        try
        {
            if (receipt is null || !IsCommand(receipt.Command)
                || !IsDigest(receipt.PayloadDigestAfter) || !IsDigest(receipt.PreviousReceiptDigest)
                || receipt.CommandDigest != CommandDigest(receipt.Command) || receipt.ReceiptDigest != ReceiptDigest(receipt)) return false;
            if (receipt.NeverApplied)
                return receipt.Quote is null && receipt.Command.Binding.LocalHistoryIncarnationId is not null
                    && receipt.CommittedRevision > receipt.Command.Binding.ContentRevision;
            if (receipt.Quote is not { Item: { Snapshot: { } } } q
                || receipt.CommittedRevision != receipt.Command.Binding.ContentRevision + 1
                || receipt.Command.PreviewDigest != PreviewDigest(receipt.Command.Request, receipt.Command.Binding, q)) return false;
            var r = receipt.Command.Request;
            var s = q.Item.Snapshot;
            if (q.Item.InstanceId != r.InstanceId || s.CyberwareId != r.InstanceId || s.Name != q.Item.Name
                || q.Item.SourceId == Guid.Empty || q.NuyenBefore != s.AvailableNuyen || s.ParentGuid.Length != 0
                || q.NuyenAfter != checked(q.NuyenBefore + q.Canonical.NuyenDelta) || q.NuyenAfter < 0m) return false;
            var expected = QuoteCanonical(s, r.ActionId, r.RefundPercentage,
                q.CreatedEssenceHoleXml is not null, q.CreatedEssenceAntiHoleXml is not null);
            if (expected is not { Exact: true } || expected != q.Canonical || !PreservesEssence(s, expected)) return false;
            if (!IsBookkeepingCreation(q.CreatedEssenceHoleXml, "b57eadaa-7c3b-4b80-8d79-cbbd922c1196",
                    s.EssenceHoleRating, expected.NewEssenceHoleRating, r)
                || !IsBookkeepingCreation(q.CreatedEssenceAntiHoleXml, "961eac53-0c43-4b19-8741-2872177a3a4c",
                    s.EssenceAntiHoleRating, expected.NewEssenceAntiHoleRating, r)) return false;
            var before = XElement.Parse(q.Item.InstalledXml);
            if ((string?)before.Element("guid") != r.InstanceId.ToString("D")
                || (string?)before.Element("sourceid") != q.Item.SourceId.ToString("D")) return false;
            if (r.ActionId == "sell") { if (q.ReplacementImplantXml is not null) return false; }
            else
            {
                before.Elements("grade").Single().Value = q.Canonical.GradeName;
                if (r.ActionId.StartsWith("rating:", StringComparison.Ordinal))
                    before.Elements("rating").Single().Value = q.Canonical.Rating.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (q.ReplacementImplantXml is null || !XNode.DeepEquals(before, XElement.Parse(q.ReplacementImplantXml))) return false;
            }
            var expense = XElement.Parse(q.ExpenseXml);
            return expense.Name == "expense" && expense.Elements("guid").Single().Value == r.ExpenseId.ToString("D")
                && decimal.Parse(expense.Elements("amount").Single().Value, System.Globalization.CultureInfo.InvariantCulture) == q.Canonical.NuyenDelta
                && expense.Elements("type").Single().Value == "Nuyen" && !expense.Elements("undo").Any();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException
            or FormatException or System.Xml.XmlException or NullReferenceException) { return false; }
    }

    private static bool IsBookkeepingCreation(string? xml, string sourceId, int? before, int? after,
        CharacterCareerImplantCommerceRequest request)
    {
        if (before.HasValue || after is null or 0) return xml is null;
        if (xml is null || after is < 1 or > 600) return false;
        var ware = XElement.Parse(xml);
        Guid id = BookkeepingInstanceId(request.ExpenseId, sourceId);
        bool hole = sourceId == "b57eadaa-7c3b-4b80-8d79-cbbd922c1196";
        string name = hole ? "Essence Hole" : "Essence Antihole";
        if (id == Guid.Empty || id == request.InstanceId || id == request.ExpenseId || id == request.OperationId
            || ware.Name != "cyberware" || ware.HasAttributes
            || ware.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)) return false;
        foreach (var element in ware.Elements())
        {
            if (element.HasAttributes) return false;
            if (element.Name == "pairinclude" || element.Name == "wirelesspairinclude")
            {
                if (!XNode.DeepEquals(element, new XElement(element.Name, new XElement("name", name)))) return false;
            }
            else if (element.HasElements) return false;
        }
        return (string?)ware.Element("guid") == id.ToString("D") && (string?)ware.Element("sourceid") == sourceId
            && (string?)ware.Element("name") == name && (string?)ware.Element("grade") == "None"
            && (string?)ware.Element("forcegrade") == "None" && (string?)ware.Element("improvementsource") == "Cyberware"
            && (string?)ware.Element("rating") == after.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            && (string?)ware.Element("ess") == (hole ? "Rating * 0.01" : "Rating * -0.01")
            && (string?)ware.Element("source") == "SR5" && (string?)ware.Element("maxrating") == "600"
            && new[] { "cost", "capacity", "avail", "page", "essdiscount", "extraessadditivemultiplier" }
                .All(field => (string?)ware.Element(field) == "0")
            && (string?)ware.Element("extraessmultiplicativemultiplier") == "1"
            && new[] { "bonus", "pairbonus", "wirelessbonus", "wirelesspairbonus", "parentid", "plugsintomodularmount" }
                .All(field => (string?)ware.Element(field) == "");
    }

    public static bool IsValidLedger(CharacterWorkspaceId workspace, long revision,
        IReadOnlyList<CharacterCareerImplantCommerceReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (receipts.Count > MaximumReceipts) return false;
        string previous = LedgerRoot;
        long lastRevision = 0, bytes = 0;
        HashSet<Guid> operations = [], expenses = [], sold = [];
        foreach (var row in receipts)
        {
            if (!IsCoherent(row) || row.Command.Request.WorkspaceId != workspace
                || row.CommittedRevision > revision || row.CommittedRevision <= lastRevision || row.PreviousReceiptDigest != previous
                || !operations.Add(row.Command.Request.OperationId) || !expenses.Add(row.Command.Request.ExpenseId)
                || !row.NeverApplied && sold.Contains(row.Command.Request.InstanceId)) return false;
            if (!row.NeverApplied && row.Command.Request.ActionId == "sell") sold.Add(row.Command.Request.InstanceId);
            bytes += JsonSerializer.SerializeToUtf8Bytes(row).Length;
            if (bytes > 4 * 1024 * 1024) return false;
            previous = row.ReceiptDigest; lastRevision = row.CommittedRevision;
        }
        return true;
    }
    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var receipts = saved.Document.AuxiliaryState.CharacterCareerImplantCommerceReceipts;
        return IsValidLedger(saved.Id, saved.ContentRevision, receipts)
            && (receipts is not { Count: > 0 } || saved.SavedRevision >= receipts[^1].CommittedRevision
                && (saved.ContentRevision != receipts[^1].CommittedRevision || Hash(saved.Document.Content) == receipts[^1].PayloadDigestAfter));
    }
    public static CharacterCareerImplantCommerceResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operation, string commandDigest)
    {
        if (!IsOwner(owner) || !IsValidHistory(saved) || saved.LocalHistory is null)
            return new(CharacterCareerImplantCommerceOutcome.Corrupt, Error: "implant_history_unverified");
        var receipt = saved.Document.AuxiliaryState.CharacterCareerImplantCommerceReceipts?
            .SingleOrDefault(r => r.Command.Request.OperationId == operation);
        // This is intentionally not a NotFound authorization to resend an uncertain operation.
        if (receipt is null) return new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_original_receipt_not_observed");
        return saved.CanReplayReceipt(receipt.CommittedRevision) && receipt.Command.Binding.OwnerId == owner.NormalizedValue
            && receipt.Command.Binding.LocalSingleUser == owner.IsLocalSingleUser && receipt.CommandDigest == commandDigest
            && (!receipt.NeverApplied || receipt.Command.Binding.LocalHistoryIncarnationId == saved.LocalHistory.IncarnationId)
            ? new(receipt.NeverApplied ? CharacterCareerImplantCommerceOutcome.NeverApplied
                : CharacterCareerImplantCommerceOutcome.Replayed, receipt)
            : new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_foreign_or_conflicting_operation");
    }
    public static bool HasOperation(WorkspaceStoredDocument saved, Guid operation)
        => saved.Document.AuxiliaryState.CharacterCareerImplantCommerceReceipts?.Any(r => r.Command.Request.OperationId == operation) == true;

    public static bool TryBuild(OwnerContextStamp stamp, WorkspaceStoredDocument saved,
        CharacterCareerImplantCommerceCommand command, ICharacterCareerImplantCommerceAuthority authority, string runtime,
        out WorkspaceDocument? replacement, out CharacterCareerImplantCommerceReceipt? receipt, out string error)
    {
        replacement = null; receipt = null; error = "implant_stale_or_unconfirmed_command";
        if (!IsCommand(command) || !StampMatches(stamp, command.Binding) || !IsWorkspace(saved)
            || !WorkspaceCurrent(saved, command.Binding) || command.Binding.RuntimeDigest != runtime
            || HasOperation(saved, command.Request.OperationId)) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerImplantCommerceReceipts;
        if (history is { Count: >= MaximumReceipts } || history?.Any(r => !r.NeverApplied && r.Command.Request.InstanceId == command.Request.InstanceId
            && r.Command.Request.ActionId == "sell") == true) return false;
        var preparation = authority.ReadInstalled(saved.Document.Content, saved.ContentRevision);
        if (Bind(stamp, saved, preparation, runtime) != command.Binding) return false;
        var quote = authority.Quote(preparation, command.Request, out error);
        if (quote is null || PreviewDigest(command.Request, command.Binding, quote) != command.PreviewDigest) return false;
        string payload;
        try { payload = authority.Materialize(saved.Document.Content, quote); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or System.Xml.XmlException or IOException or UnauthorizedAccessException)
        { error = ex.Message; return false; }
        if (payload.Length > 67_108_864) { error = "implant_payload_too_large"; return false; }
        var candidate = new CharacterCareerImplantCommerceReceipt(command, quote, CommandDigest(command), saved.ContentRevision + 1,
            Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
        candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
        var appended = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedger(saved.Id, candidate.CommittedRevision, appended)) { error = "implant_receipt_invalid"; return false; }
        replacement = saved.Document with { State = saved.Document.State with { Payload = payload,
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerImplantCommerceReceipts = Array.AsReadOnly(appended) } } };
        receipt = candidate; error = ""; return true;
    }

    // Called only while the typed store holds the current-owner and workspace operation leases.
    // Source/rules and the original process stamp cannot authorize a cold purchase; they are retained verbatim here.
    public static bool TryBuildNeverApplied(OwnerContextStamp current, WorkspaceStoredDocument saved,
        CharacterCareerImplantCommerceCommand command, out WorkspaceDocument? replacement,
        out CharacterCareerImplantCommerceReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!OwnerMatches(current, command) || !IsWorkspace(saved) || saved.Id != command.Request.WorkspaceId
            || command.Binding.LocalHistoryIncarnationId is null || command.Binding.LocalHistoryIncarnationId != saved.LocalHistory!.IncarnationId
            || saved.ContentRevision < command.Binding.ContentRevision || !saved.CanReplayReceipt(command.Binding.ContentRevision)
            || HasOperation(saved, command.Request.OperationId)) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerImplantCommerceReceipts;
        if (history is { Count: >= MaximumReceipts }) return false;
        var candidate = new CharacterCareerImplantCommerceReceipt(command, null, CommandDigest(command), saved.ContentRevision + 1,
            Hash(saved.Document.Content), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "") { NeverApplied = true };
        candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
        var appended = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedger(saved.Id, candidate.CommittedRevision, appended)) return false;
        replacement = saved.Document with { State = saved.Document.State with {
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerImplantCommerceReceipts = Array.AsReadOnly(appended) } } };
        receipt = candidate;
        return true;
    }
}
