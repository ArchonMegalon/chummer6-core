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

public sealed record CharacterCareerLifestyleMaintenanceSnapshot(
    CharacterCareerLifestyleMaintenanceBinding Binding, CharacterCareerLifestyleMaintenanceRequest Request,
    CharacterCreationLifestylesAuthority Authority, CharacterCreationLifestyleProjection After,
    decimal? PreviousCostPerInterval);

/// <summary>Pure preparation and durable evidence checks. Only the dedicated leased store may persist a maintenance change.</summary>
public static class CharacterCareerLifestyleMaintenanceTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public const int MaximumContentLength = 67_108_864;
    private static string LedgerRoot => Hash("chummer.core.sr5-lifestyle-maintenance-ledger/v1");

    public static T? Freeze<T>(T value) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) { return null; }
    }

    public static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string CommandDigest(CharacterCareerLifestyleMaintenanceCommand command)
        => Hash("chummer.core.sr5-lifestyle-maintenance-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerLifestyleMaintenanceReceipt receipt)
        => Hash("chummer.core.sr5-lifestyle-maintenance-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));

    public static bool IsValidOwner(OwnerScope owner) => owner.NormalizedValue is { Length: > 0 and <= 512 }
        && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool IsValidRequest(CharacterCareerLifestyleMaintenanceRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && request.LifestyleId != Guid.Empty && Enum.IsDefined(request.Kind)
            && request.BaseLifestyleOptionId is { Length: > 0 and <= 512 }
            && request.Name is { Length: > 0 and <= 200 } && request.Name == request.Name.Trim()
            && request.Name.All(c => !char.IsControl(c)) && request.Percentage is > 0 and <= 1000
            && request.Roommates is >= 0 and <= 100
            && (!request.SplitCostWithRoommates || request.Roommates > 0)
            && (!request.TrustFund || request.Roommates == 0 && !request.SplitCostWithRoommates)
            && (request.Configuration is null || request.Configuration is { Qualities: { Count: <= 128 } } config
                && config.LifestyleId == request.LifestyleId && config.BaseLifestyleOptionId == request.BaseLifestyleOptionId
                && config.Name == request.Name && config.Percentage == request.Percentage && config.Roommates == request.Roommates
                && config.SplitCostWithRoommates == request.SplitCostWithRoommates && config.TrustFund == request.TrustFund);

    public static bool IsBoundCommand(CharacterCareerLifestyleMaintenanceCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } binding
            && binding.OwnerId is { Length: > 0 and <= 512 }
            && new OwnerScope(binding.OwnerId).NormalizedValue == binding.OwnerId
            && binding.WorkspaceId == command.Request.WorkspaceId
            && binding.WorkspaceRevision is > 0 and < long.MaxValue
            && IsDigest(binding.SourceDigest) && IsDigest(binding.AuxiliaryStateDigest)
            && IsDigest(binding.RuleStateDigest) && IsDigest(binding.RuntimeDigest)
            && Guid.TryParseExact(binding.IncarnationId, "N", out var incarnation) && incarnation != Guid.Empty
            && incarnation.ToString("N") == binding.IncarnationId;

    public static bool IsConfirmedCommand(CharacterCareerLifestyleMaintenanceCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);

    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerLifestyleMaintenanceRequest request, ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerLifestyleMaintenanceSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "lifestyle_maintenance_workspace_invalid";
        if (!IsValidOwner(owner) || !IsValidRequest(request) || request.WorkspaceId != saved.Id || !IsDigest(runtimeDigest)
            || saved.LocalHistory is null || !saved.LocalHistory.IsValid(saved.ContentRevision)) return false;
        try
        {
            var root = CharacterCareerLifestyleMaintenanceProjection.ReadDocument(saved).Root!;
            error = "lifestyle_maintenance_source_or_effect_authority_unavailable";
            if (!CharacterCareerLifestyleMaintenanceProjection.TryAuthority(saved.Document.Content, sources, out var authority, out string rules, out error)) return false;
            var option = authority.LifestyleOptions.SingleOrDefault(o => o.OptionId == request.BaseLifestyleOptionId);
            error = "lifestyle_maintenance_source_option_unavailable";
            if (option is null || !option.IsSelectable || !option.EligibilityIsExact || option.Blockers.Count != 0) return false;
            if (!CharacterCareerLifestyleMaintenanceProjection.TrySourcePeriod(saved.Document.Content, sources, option, out error)) return false;
            var matches = root.Element("lifestyles")?.Elements("lifestyle")
                .Where(x => x.Elements("guid").Any(g => Guid.TryParse(g.Value, out var id) && id == request.LifestyleId)).ToArray() ?? [];
            CharacterCreationLifestyleProjection? before = null;
            if (request.Kind == CharacterCareerLifestyleMaintenanceKind.Create)
            { error = "lifestyle_maintenance_identity_already_exists"; if (matches.Length != 0) return false; }
            else
            {
                error = "lifestyle_maintenance_identity_missing_or_ambiguous";
                if (matches.Length != 1 || !CharacterCareerLifestyleMaintenanceProjection.TryExistingForMaintenance(matches[0],
                    request.LifestyleId, authority, out before, out error)) return false;
            }
            // Maintenance never purchases intervals, resets quality selections, or upgrades a retained profile.
            int increments = before?.Configuration.Increments ?? 1;
            var config = request.Configuration ?? (before?.Configuration ?? new CharacterCreationLifestyleConfiguration(
                request.LifestyleId, option.OptionId, request.Name, CharacterCreationLifestyleStyleIds.Standard,
                option.DefaultIncrementId, increments, request.Percentage, request.Roommates,
                request.SplitCostWithRoommates, request.TrustFund, 0, 0, 0, 0, "", "", "", [])) with
            {
                BaseLifestyleOptionId = request.BaseLifestyleOptionId, Name = request.Name,
                Percentage = request.Percentage, Roommates = request.Roommates,
                SplitCostWithRoommates = request.SplitCostWithRoommates, TrustFund = request.TrustFund
            };
            error = "lifestyle_maintenance_interval_count_read_only";
            if (config.Increments != increments) return false;
            CharacterCreationLifestyleProjection? after;
            if (request.Kind == CharacterCareerLifestyleMaintenanceKind.Delete)
            {
                error = "lifestyle_maintenance_delete_target_changed";
                if (before is null || !Same(config, before.Configuration)) return false;
                after = before;
            }
            else
            {
                if (!CharacterCareerLifestyleMaintenanceProjection.TryProject(config, authority, out after, out error)) return false;
                if (before is not null && before.LifestyleDigest == after!.LifestyleDigest)
                { error = "lifestyle_maintenance_no_change"; return false; }
            }
            if (!CharacterCareerLifestyleMaintenanceProjection.TryAuthority(saved.Document.Content, sources, out _, out string final) || rules != final)
            { error = "lifestyle_maintenance_source_changed"; return false; }
            snapshot = new(new(owner.NormalizedValue, saved.Id, saved.ContentRevision, SourceDigest(saved.Document),
                saved.Document.AuxiliaryStateDigest, rules, runtimeDigest, saved.LocalHistory.IncarnationId), request, authority, after!, before?.Economics.CostPerIncrement);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (CharacterCareerLifestyleMaintenanceProjection.IsInputException(ex))
        { error = "lifestyle_maintenance_input_unresolved"; return false; }
    }

    public static bool IsSourceCurrent(WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerLifestyleMaintenanceBinding binding)
        => SourceDigest(saved.Document) == binding.SourceDigest
            && CharacterCareerLifestyleMaintenanceProjection.TryAuthority(saved.Document.Content, sources, out _, out string rules)
            && rules == binding.RuleStateDigest;

    private static string SourceDigest(WorkspaceDocument document)
        => Hash("chummer.core.sr5-lifestyle-maintenance-source/v1\0" + JsonSerializer.Serialize(new
        { document.Format, document.RulesetId, document.SchemaVersion, document.PayloadKind, PayloadDigest = Hash(document.Content) }));

    public static bool TryPreview(CharacterCareerLifestyleMaintenanceSnapshot snapshot,
        CharacterCareerLifestyleMaintenanceRequest request, out CharacterCareerLifestyleMaintenancePreview? preview, out string error)
    {
        preview = null;
        error = "lifestyle_maintenance_request_conflict";
        var command = new CharacterCareerLifestyleMaintenanceCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || !Same(request, snapshot.Request)) return false;
        var option = snapshot.Authority.LifestyleOptions.Single(o => o.OptionId == request.BaseLifestyleOptionId);
        var after = snapshot.After;
        var quote = new CharacterCareerLifestyleMaintenanceQuote(after.SourceId, after.BaseLifestyleName,
            after.SourceBook, after.Page, after.Configuration.Name, option.BaseCost, after.Economics.CostPerIncrement,
            after.Configuration.Increments, snapshot.PreviousCostPerInterval, after.Economics.CoveredByTrustFund)
        { Projection = after };
        preview = CreatePreview(command, quote);
        error = string.Empty;
        return true;
    }

    private static CharacterCareerLifestyleMaintenancePreview CreatePreview(CharacterCareerLifestyleMaintenanceCommand command,
        CharacterCareerLifestyleMaintenanceQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-lifestyle-maintenance-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }

    public static bool IsCoherent(CharacterCareerLifestyleMaintenanceReceipt? receipt)
    {
        if (receipt is null || !IsConfirmedCommand(receipt.Command) || receipt.Quote is not { } quote
            || quote.SourceId == Guid.Empty || quote.Name != receipt.Command.Request.Name
            || quote.SourceBook is not { Length: > 0 and <= 64 } || quote.BaseLifestyleName is not { Length: > 0 and <= 200 }
            || quote.Page is not { Length: > 0 and <= 32 } || quote.BaseCost < 0 || quote.CostPerInterval < 0
            || quote.PreviousCostPerInterval is < 0
            || receipt.Command.Request.Kind == CharacterCareerLifestyleMaintenanceKind.Create
                && (quote.Increments != 1 || quote.PreviousCostPerInterval is not null)
            || receipt.Command.Request.Kind != CharacterCareerLifestyleMaintenanceKind.Create && quote.PreviousCostPerInterval is null
            || !IsDigest(receipt.CommandDigest) || !IsDigest(receipt.CharacterPayloadDigestAfter)
            || !IsDigest(receipt.PreviousReceiptDigest) || !IsDigest(receipt.ReceiptDigest)
            || receipt.CommittedWorkspaceRevision != receipt.Command.Binding.WorkspaceRevision + 1) return false;
        if (quote.Projection is { } projection
            && (projection.LifestyleDigest != CharacterCreationLifestylesRules.ComputeProjectionDigest(projection)
                || projection.SourceId != quote.SourceId || projection.SourceBook != quote.SourceBook || projection.Page != quote.Page
                || projection.BaseLifestyleName != quote.BaseLifestyleName || projection.Configuration.Name != quote.Name
                || projection.Configuration.LifestyleId != receipt.Command.Request.LifestyleId
                || projection.Configuration.BaseLifestyleOptionId != receipt.Command.Request.BaseLifestyleOptionId
                || projection.Configuration.Increments != quote.Increments || projection.Economics.CostPerIncrement != quote.CostPerInterval
                || projection.Economics.CoveredByTrustFund != quote.CoveredByTrustFund || projection.Economics.Blockers.Count != 0)) return false;
        return receipt.CommandDigest == CommandDigest(receipt.Command)
            && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, quote).PreviewDigest
            && receipt.ReceiptDigest == ReceiptDigest(receipt);
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerLifestyleMaintenanceReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        HashSet<Guid> operations = [];
        string previous = LedgerRoot;
        long lastRevision = 0, bytes = 2;
        foreach (var receipt in receipts)
        {
            if (!IsCoherent(receipt) || receipt.Command.Request.WorkspaceId != id
                || receipt.CommittedWorkspaceRevision > revision || receipt.CommittedWorkspaceRevision <= lastRevision
                || receipt.PreviousReceiptDigest != previous || !operations.Add(receipt.Command.Request.OperationId)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(receipt).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            lastRevision = receipt.CommittedWorkspaceRevision;
            previous = receipt.ReceiptDigest;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var history = saved.Document.AuxiliaryState.CharacterCareerLifestyleMaintenanceReceipts;
        return IsValidLedger(saved.Id, saved.ContentRevision, history)
            && (history is not { Count: > 0 } || (saved.SavedRevision >= history[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != history[^1].CommittedWorkspaceRevision
                    || Hash(saved.Document.Content) == history[^1].CharacterPayloadDigestAfter)));
    }

    public static CharacterCareerLifestyleMaintenanceResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerLifestyleMaintenanceCommand command)
    {
        if (!IsConfirmedCommand(command) || command.Binding.OwnerId != owner.NormalizedValue
            || command.Request.WorkspaceId != saved.Id || saved.ContentRevision < command.Binding.WorkspaceRevision
            || saved.LocalHistory?.IncarnationId != command.Binding.IncarnationId)
            return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, Error: "lifestyle_maintenance_incarnation_or_identity_changed");
        Guid operationId = command.Request.OperationId;
        string commandDigest = CommandDigest(command);
        if (!IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerLifestyleMaintenanceOutcome.Corrupt, Error: "lifestyle_maintenance_history_invalid");
        var receipt = saved.Document.AuxiliaryState.CharacterCareerLifestyleMaintenanceReceipts?
            .SingleOrDefault(row => row.Command.Request.OperationId == operationId);
        if (receipt is null) return new(CharacterCareerLifestyleMaintenanceOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(receipt.CommittedWorkspaceRevision)
            && receipt.Command.Binding.OwnerId == owner.NormalizedValue && receipt.CommandDigest == commandDigest
            ? new(CharacterCareerLifestyleMaintenanceOutcome.Replayed, receipt, saved.ContentRevision)
            : new(CharacterCareerLifestyleMaintenanceOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "lifestyle_maintenance_operation_conflict");
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerLifestyleMaintenanceSnapshot snapshot, CharacterCareerLifestyleMaintenanceCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerLifestyleMaintenanceReceipt? receipt)
    {
        replacement = null;
        receipt = null;
        if (!IsValidOwner(owner) || !IsConfirmedCommand(command) || command.Binding.OwnerId != owner.NormalizedValue
            || !IsValidHistory(saved) || saved.Id != command.Request.WorkspaceId
            || saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || saved.LocalHistory?.IncarnationId != command.Binding.IncarnationId
            || saved.Document.AuxiliaryStateDigest != command.Binding.AuxiliaryStateDigest
            || SourceDigest(saved.Document) != command.Binding.SourceDigest
            || !TryPreview(snapshot, command.Request, out var preview, out _)
            || !Same(preview!.Command with { ExplicitlyConfirmed = true }, command)) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerLifestyleMaintenanceReceipts;
        if (history is { Count: >= MaximumReceipts }
            || history?.Any(row => row.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        try
        {
            XDocument document = CharacterCareerLifestyleMaintenanceProjection.ReadDocument(saved);
            XElement root = document.Root!;
            var lifestyles = root.Element("lifestyles");
            if (lifestyles is null) { lifestyles = new XElement("lifestyles"); root.Add(lifestyles); }
            var existing = lifestyles.Elements("lifestyle")
                .SingleOrDefault(row => Guid.TryParse(row.Element("guid")?.Value, out var id) && id == command.Request.LifestyleId);
            if (command.Request.Kind == CharacterCareerLifestyleMaintenanceKind.Delete)
            {
                if (existing is null) return false;
                existing.Remove();
            }
            else
            {
                var updated = CharacterCreationLifestylesService.BuildLifestyleElement(snapshot.After, existing, snapshot.Authority);
                // Rewriting mechanics must not reset retained quality display metadata.
                foreach (var quality in updated.Element("lifestylequalities")?.Elements("lifestylequality") ?? [])
                {
                    var retained = existing?.Element("lifestylequalities")?.Elements("lifestylequality")
                        .SingleOrDefault(q => q.Element("guid")?.Value == quality.Element("guid")?.Value);
                    if (retained is null) continue;
                    foreach (string field in new[] { "notes", "notesColor", "customname", "sortorder", "print" })
                    {
                        quality.Elements(field).Remove();
                        if (retained.Element(field) is { } value) quality.Add(new XElement(value));
                    }
                }
                updated.SetElementValue("totalmonthlycost", snapshot.After.Economics.CostPerIncrement.ToString(CultureInfo.InvariantCulture));
                updated.SetElementValue("totalcost", snapshot.After.Economics.TotalCost.ToString(CultureInfo.InvariantCulture));
                if (existing is null) lifestyles.Add(updated); else existing.ReplaceWith(updated);
            }
            // No Nuyen, expenses, improvements, sibling lifestyle, or historical receipt mutation.
            string payload = document.ToString(SaveOptions.DisableFormatting);
            if (payload.Length > MaximumContentLength) return false;
            var candidate = new CharacterCareerLifestyleMaintenanceReceipt(command, preview.Quote, CommandDigest(command),
                saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
            candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
            var appended = (history ?? []).Append(candidate).ToArray();
            if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, appended)) return false;
            replacement = saved.Document with { State = saved.Document.State with
            {
                Payload = payload,
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerLifestyleMaintenanceReceipts = Array.AsReadOnly(appended) }
            } };
            receipt = candidate;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException or OverflowException
            or ArgumentException or InvalidOperationException) { return false; }
    }

}
