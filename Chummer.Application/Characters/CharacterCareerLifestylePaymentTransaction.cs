using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed record CharacterCareerLifestylePaymentSnapshot(
    CharacterCareerLifestylePaymentBinding Binding, CharacterLifestyleIncrementState Inputs,
    IReadOnlyList<Guid> ExpenseIds);

/// <summary>Pure preparation and durable evidence checks. Only the dedicated leased store may persist a payment.</summary>
public static class CharacterCareerLifestylePaymentTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public const int MaximumContentLength = 67_108_864;
    private static string LedgerRoot => Hash("chummer.core.sr5-lifestyle-payment-ledger/v1");

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string CommandDigest(CharacterCareerLifestylePaymentCommand command)
        => Hash("chummer.core.sr5-lifestyle-payment-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerLifestylePaymentReceipt receipt)
        => Hash("chummer.core.sr5-lifestyle-payment-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));

    public static bool IsValidOwner(OwnerScope owner) => owner.NormalizedValue is { Length: > 0 and <= 512 }
        && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool IsValidRequest(CharacterCareerLifestylePaymentRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && request.LifestyleId != Guid.Empty
            && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && request.ExpenseDateLocal.Ticks % TimeSpan.TicksPerSecond == 0;

    public static bool IsBoundCommand(CharacterCareerLifestylePaymentCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } binding
            && binding.OwnerId is { Length: > 0 and <= 512 }
            && new OwnerScope(binding.OwnerId).NormalizedValue == binding.OwnerId
            && binding.WorkspaceId == command.Request.WorkspaceId
            && binding.WorkspaceRevision is > 0 and < long.MaxValue
            && IsDigest(binding.SourceDigest) && IsDigest(binding.AuxiliaryStateDigest)
            && IsDigest(binding.RuleStateDigest) && IsDigest(binding.RuntimeDigest)
            && (binding.IncarnationId is null || Guid.TryParseExact(binding.IncarnationId, "N", out var incarnation)
                && incarnation != Guid.Empty && incarnation.ToString("N") == binding.IncarnationId);

    public static bool IsConfirmedCommand(CharacterCareerLifestylePaymentCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);

    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, Guid lifestyleId,
        ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerLifestylePaymentSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "lifestyle_workspace_invalid";
        if (!IsValidOwner(owner) || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(saved.Id)
            || saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || lifestyleId == Guid.Empty || !IsDigest(runtimeDigest)
            || saved.LocalHistory is null || !saved.LocalHistory.IsValid(saved.ContentRevision)) return false;
        var document = saved.Document;
        error = "lifestyle_requires_saved_sr5_career";
        if (document.Format != WorkspaceDocumentFormat.NativeXml || document.RulesetId != "sr5"
            || document.SchemaVersion != 1 || document.PayloadKind is not ("workspace" or "sr5/chum5-xml")) return false;
        try
        {
            XElement root = ReadXml(document.Content).Root!;
            if (!bool.TryParse(Scalar(root, "created"), out bool career) || !career) return false;
            decimal nuyen = Decimal(root, "nuyen");
            XElement[] lifestyles = Container(root, "lifestyles", required: true)!.Elements("lifestyle").ToArray();
            if (lifestyles.Length > 100_000) throw new InvalidDataException();
            var identities = lifestyles.Select(row => Guid.Parse(Scalar(row, "guid"))).ToArray();
            if (identities.Any(id => id == Guid.Empty) || identities.Distinct().Count() != identities.Length)
                throw new InvalidDataException();
            int index = Array.IndexOf(identities, lifestyleId);
            error = "lifestyle_existing_identity_missing";
            if (index < 0) return false;
            var lifestyle = lifestyles[index];
            error = "lifestyle_source_unavailable";
            if (!CharacterCareerLifestyleMaintenanceProjection.TryAuthority(document.Content, sources, out var authority,
                    out string rules, out error)
                || !CharacterCareerLifestyleMaintenanceProjection.TryExistingForMaintenance(lifestyle, lifestyleId, authority,
                    out var projection, out error)) return false;
            var configuration = projection!.Configuration;
            var option = authority.LifestyleOptions.Single(o => o.OptionId == configuration.BaseLifestyleOptionId);
            if (!CharacterCareerLifestyleMaintenanceProjection.TrySourcePeriod(document.Content, sources, option, out error)) return false;
            string unitText = configuration.IncrementId switch
            {
                CharacterCreationLifestyleIncrementIds.Day => "Day",
                CharacterCreationLifestyleIncrementIds.Week => "Week",
                CharacterCreationLifestyleIncrementIds.Month => "Month",
                _ => throw new InvalidDataException("lifestyle_period_unresolved")
            };
            var inputs = new CharacterLifestyleIncrementState(lifestyleId, configuration.Increments,
                CharacterLifestyleIncrementRules.ParseUnit(unitText), true, nuyen, true,
                projection.Economics.CostPerIncrement, true, configuration.Name);
            XElement[] expenses = Container(root, "expenses", required: false)?.Elements("expense").ToArray() ?? [];
            if (expenses.Length >= 100_000) throw new InvalidDataException();
            var expenseIds = expenses.Select(row => Guid.Parse(Scalar(row, "guid"))).ToArray();
            if (expenseIds.Any(id => id == Guid.Empty) || expenseIds.Distinct().Count() != expenseIds.Length)
                throw new InvalidDataException();
            error = "lifestyle_source_unavailable";
            var binding = new CharacterCareerLifestylePaymentBinding(owner.NormalizedValue, saved.Id,
                saved.ContentRevision, SourceDigest(document), document.AuxiliaryStateDigest, rules, runtimeDigest)
            { IncarnationId = saved.LocalHistory!.IncarnationId };
            if (!CharacterCareerLifestyleMaintenanceProjection.TryAuthority(document.Content, sources, out _, out string finalRules)
                || rules != finalRules)
            { error = "lifestyle_source_changed"; return false; }
            snapshot = new(binding, inputs, Array.AsReadOnly(expenseIds));
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException
            or OverflowException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "lifestyle_input_unresolved"; return false; }
    }

    public static bool IsSourceCurrent(WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerLifestylePaymentBinding binding)
        => saved.LocalHistory?.IncarnationId == binding.IncarnationId
            && SourceDigest(saved.Document) == binding.SourceDigest
            && CharacterCareerLifestyleMaintenanceProjection.TryAuthority(saved.Document.Content, sources, out _, out string rules)
            && rules == binding.RuleStateDigest;

    private static string SourceDigest(WorkspaceDocument document)
        => Hash("chummer.core.sr5-lifestyle-payment-source/v1\0" + JsonSerializer.Serialize(new
        { document.Format, document.RulesetId, document.SchemaVersion, document.PayloadKind, PayloadDigest = Hash(document.Content) }));

    public static bool TryPreview(CharacterCareerLifestylePaymentSnapshot snapshot,
        CharacterCareerLifestylePaymentRequest request, out CharacterCareerLifestylePaymentPreview? preview, out string error)
    {
        preview = null;
        error = "lifestyle_request_conflict";
        var command = new CharacterCareerLifestylePaymentCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || snapshot.Inputs.LifestyleId != request.LifestyleId) return false;
        var quote = Quote(command, snapshot.Inputs);
        if (!quote.Increment.Exact) { error = quote.Increment.Blocker ?? "lifestyle_quote_unavailable"; return false; }
        // The next saved count must remain readable even without an optional aggregate cache.
        try { _ = checked(quote.Inputs.TotalIncrementCost * quote.Increment.UpdatedIncrements); }
        catch (OverflowException) { error = "lifestyle_total_cost_overflow"; return false; }
        if (snapshot.ExpenseIds.Contains(quote.ExpenseId)) { error = "lifestyle_expense_identity_conflict"; return false; }
        preview = CreatePreview(command, quote);
        error = string.Empty;
        return true;
    }

    private static CharacterCareerLifestylePaymentQuote Quote(CharacterCareerLifestylePaymentCommand command,
        CharacterLifestyleIncrementState inputs)
    {
        var increment = CharacterLifestyleIncrementRules.Quote(inputs, CharacterLifestyleIncrementAction.IncreaseCareer);
        string identity = Hash("chummer.core.sr5-lifestyle-payment-expense/v1\0" + JsonSerializer.Serialize(new
        { command.Binding.OwnerId, command.Request.WorkspaceId, command.Request.OperationId }));
        return new(inputs, increment, inputs.Nuyen + increment.NuyenDelta, new Guid(Convert.FromHexString(identity[..32])));
    }

    private static CharacterCareerLifestylePaymentPreview CreatePreview(CharacterCareerLifestylePaymentCommand command,
        CharacterCareerLifestylePaymentQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-lifestyle-payment-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }

    public static bool IsCoherent(CharacterCareerLifestylePaymentReceipt? receipt)
    {
        if (receipt is null || !IsConfirmedCommand(receipt.Command) || receipt.Quote?.Inputs is not { } inputs
            || inputs.LifestyleId != receipt.Command.Request.LifestyleId || !inputs.CareerMode
            || !inputs.NuyenExact || !inputs.TotalIncrementCostExact || inputs.Nuyen < 0 || inputs.TotalIncrementCost < 0
            || !Enum.IsDefined(inputs.Unit) || inputs.DisplayName is not { Length: > 0 and <= 1024 }
            || receipt.Quote.Increment is not { Exact: true } || !IsDigest(receipt.CommandDigest)
            || !IsDigest(receipt.CharacterPayloadDigestAfter) || !IsDigest(receipt.PreviousReceiptDigest)
            || !IsDigest(receipt.ReceiptDigest) || receipt.CommittedWorkspaceRevision != receipt.Command.Binding.WorkspaceRevision + 1)
            return false;
        var expected = Quote(receipt.Command, inputs);
        return receipt.Quote == expected && expected.Increment.Exact
            && receipt.CommandDigest == CommandDigest(receipt.Command)
            && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, expected).PreviewDigest
            && receipt.ReceiptDigest == ReceiptDigest(receipt);
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerLifestylePaymentReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        HashSet<Guid> operations = [];
        HashSet<Guid> expenses = [];
        string previous = LedgerRoot;
        long lastRevision = 0, bytes = 2;
        foreach (var receipt in receipts)
        {
            if (!IsCoherent(receipt) || receipt.Command.Request.WorkspaceId != id
                || receipt.CommittedWorkspaceRevision > revision || receipt.CommittedWorkspaceRevision <= lastRevision
                || receipt.PreviousReceiptDigest != previous || !operations.Add(receipt.Command.Request.OperationId)
                || !expenses.Add(receipt.Quote.ExpenseId)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(receipt).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            lastRevision = receipt.CommittedWorkspaceRevision;
            previous = receipt.ReceiptDigest;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var history = saved.Document.AuxiliaryState.CharacterCareerLifestylePaymentReceipts;
        return IsValidLedger(saved.Id, saved.ContentRevision, history)
            && (history is not { Count: > 0 } || (saved.SavedRevision >= history[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != history[^1].CommittedWorkspaceRevision
                    || Hash(saved.Document.Content) == history[^1].CharacterPayloadDigestAfter)));
    }

    public static CharacterCareerLifestylePaymentResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operationId, string commandDigest)
    {
        if (!IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerLifestylePaymentOutcome.Corrupt, Error: "lifestyle_history_invalid");
        var receipt = saved.Document.AuxiliaryState.CharacterCareerLifestylePaymentReceipts?
            .SingleOrDefault(row => row.Command.Request.OperationId == operationId);
        if (receipt is null) return new(CharacterCareerLifestylePaymentOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(receipt.CommittedWorkspaceRevision)
            && receipt.Command.Binding.OwnerId == owner.NormalizedValue && receipt.CommandDigest == commandDigest
            && (receipt.Command.Binding.IncarnationId is null || receipt.Command.Binding.IncarnationId == saved.LocalHistory?.IncarnationId)
            ? new(CharacterCareerLifestylePaymentOutcome.Replayed, receipt, saved.ContentRevision)
            : new(CharacterCareerLifestylePaymentOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "lifestyle_operation_conflict");
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerLifestylePaymentSnapshot snapshot, CharacterCareerLifestylePaymentCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerLifestylePaymentReceipt? receipt)
    {
        replacement = null;
        receipt = null;
        if (!IsValidOwner(owner) || !IsConfirmedCommand(command) || command.Binding.OwnerId != owner.NormalizedValue
            || !IsValidHistory(saved) || saved.Id != command.Request.WorkspaceId
            || saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || command.Binding.IncarnationId is null || saved.LocalHistory?.IncarnationId != command.Binding.IncarnationId
            || saved.Document.AuxiliaryStateDigest != command.Binding.AuxiliaryStateDigest
            || SourceDigest(saved.Document) != command.Binding.SourceDigest
            || !TryPreview(snapshot, command.Request, out var preview, out _)
            || preview!.Command with { ExplicitlyConfirmed = true } != command) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerLifestylePaymentReceipts;
        if (history is { Count: >= MaximumReceipts }
            || history?.Any(row => row.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        try
        {
            XDocument document = ReadXml(saved.Document.Content);
            XElement root = document.Root!;
            var lifestyle = root.Element("lifestyles")!.Elements("lifestyle")
                .Single(row => Guid.Parse(Scalar(row, "guid")) == command.Request.LifestyleId);
            lifestyle.Element("months")!.Value = preview.Quote.Increment.UpdatedIncrements.ToString(CultureInfo.InvariantCulture);
            lifestyle.SetElementValue("totalmonthlycost", preview.Quote.Inputs.TotalIncrementCost.ToString(CultureInfo.InvariantCulture));
            root.Element("nuyen")!.Value = preview.Quote.NuyenAfter.ToString(CultureInfo.InvariantCulture);
            // Legacy saves may carry the derived aggregate. Preserve its presence and recalculate from exact interval cost.
            var totalCost = lifestyle.Elements("totalcost").ToArray();
            if (totalCost.Length > 1 || totalCost.Any(e => e.HasElements || e.HasAttributes)) return false;
            if (totalCost.Length == 1) totalCost[0].Value = checked(preview.Quote.Inputs.TotalIncrementCost
                * preview.Quote.Increment.UpdatedIncrements).ToString(CultureInfo.InvariantCulture);
            var expenses = root.Element("expenses");
            if (expenses is null) { expenses = new XElement("expenses"); root.Add(expenses); }
            expenses.Add(new XElement("expense",
                new XElement("guid", preview.Quote.ExpenseId.ToString("D")),
                new XElement("date", command.Request.ExpenseDateLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XElement("type", "Nuyen"), new XElement("amount", preview.Quote.Increment.NuyenDelta.ToString(CultureInfo.InvariantCulture)),
                new XElement("reason", "Lifestyle payment: " + preview.Quote.Inputs.DisplayName),
                new XElement("refund", "False"), new XElement("forcecareervisible", "False"),
                new XElement("undo", new XElement("karmatype", "ImproveAttribute"),
                    new XElement("nuyentype", "IncreaseLifestyle"),
                    new XElement("objectid", command.Request.LifestyleId.ToString("D")),
                    new XElement("qty", "0"), new XElement("extra", string.Empty))));
            string payload = document.ToString(SaveOptions.DisableFormatting);
            if (payload.Length > MaximumContentLength) return false;
            var candidate = new CharacterCareerLifestylePaymentReceipt(command, preview.Quote, CommandDigest(command),
                saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
            candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
            var appended = (history ?? []).Append(candidate).ToArray();
            if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, appended)) return false;
            replacement = saved.Document with { State = saved.Document.State with
            {
                Payload = payload,
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerLifestylePaymentReceipts = Array.AsReadOnly(appended) }
            } };
            receipt = candidate;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException or OverflowException
            or ArgumentException or InvalidOperationException) { return false; }
    }

    private static XDocument ReadXml(string content)
    {
        if (string.IsNullOrEmpty(content) || content.Length > MaximumContentLength) throw new InvalidDataException();
        using var text = new StringReader(content);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumContentLength });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "character" || document.Root.HasAttributes) throw new InvalidDataException();
        return document;
    }

    private static XElement? Container(XElement parent, string name, bool required)
    {
        var nodes = parent.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1 || (required && nodes.Length != 1) || nodes.Any(node => node.HasAttributes))
            throw new InvalidDataException();
        return nodes.SingleOrDefault();
    }

    private static string Scalar(XElement parent, string name, bool required = true)
    {
        var node = Container(parent, name, required);
        if (node?.HasElements == true) throw new InvalidDataException();
        return node?.Value ?? string.Empty;
    }

    private static decimal Decimal(XElement parent, string name)
        => decimal.Parse(Scalar(parent, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
}
