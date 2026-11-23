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

public static class CharacterCareerGearSaleTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool IsGeneration(string? value) => Guid.TryParseExact(value, "N", out var id)
        && id != Guid.Empty && id.ToString("N") == value;
    public static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerGearSaleCommand command)
        => Hash("chummer.gear-sale.command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerGearSaleReceipt receipt)
        => Hash("chummer.gear-sale.receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    // Detach mutable identity path collections before validating or hashing an original.
    public static CharacterCareerGearSaleCommand? FreezeConfirmedCommand(CharacterCareerGearSaleCommand command)
    {
        try
        {
            var frozen = JsonSerializer.Deserialize<CharacterCareerGearSaleCommand>(JsonSerializer.SerializeToUtf8Bytes(command));
            return IsConfirmedCommand(frozen) ? frozen : null;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        { return null; }
    }
    public static bool MatchesOwner(OwnerScope owner, CharacterCareerGearSaleBinding binding)
        => !string.IsNullOrWhiteSpace(owner.NormalizedValue) && owner.NormalizedValue.Length <= 512
            && owner.NormalizedValue == binding.OwnerId && owner.IsLocalSingleUser == binding.TrustedLocalOwner
            && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool IsConfirmedCommand(CharacterCareerGearSaleCommand? c)
        => c is { ExplicitlyConfirmed: true, Request: not null, Binding: not null, Quote: not null }
            && c.Request.OperationId != Guid.Empty
            && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(c.Request.WorkspaceId)
            && c.Request.WorkspaceId == c.Binding.WorkspaceId
            && c.Binding.OwnerId is { Length: > 0 and <= 512 } owner && owner == owner.Trim().ToLowerInvariant()
            && (owner == OwnerScope.LocalSingleUser.NormalizedValue) == c.Binding.TrustedLocalOwner
            && c.Binding.ContentRevision is > 0 and < long.MaxValue && c.Binding.SavedRevision == c.Binding.ContentRevision
            && IsDigest(c.Binding.SourceDigest) && IsDigest(c.Binding.AuxiliaryDigest)
            && IsDigest(c.Binding.EnvironmentDigest) && IsDigest(c.Binding.RuntimeDigest)
            && IsGeneration(c.Binding.IncarnationId) && IsGeneration(c.Binding.SlotGeneration)
            && c.Binding.OwnerAuthorityInstanceId is { Length: > 0 and <= 512 }
            && c.Binding.OwnerTransitionRevision >= 0
            && CharacterCareerGearSaleAuthority.IsRequest(c.Request)
            && CharacterCareerGearSaleAuthority.IsQuote(c.Request, c.Quote);


    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        string runtime, out CharacterCareerGearSaleCatalog? catalog, string? slotGeneration = null)
    {
        catalog = null;
        if (sources is not ICharacterCreationRuleSourceCapture authority
            || !authority.TryCaptureRuleSources(saved.Document.Content, out var capture) || capture is null
            || !TryCapturedEnvironment(capture, capture.CreateReplayResolver(), saved.Document.Content, out var environment)) return false;
        return TryReadBound(owner, saved, runtime, () => environment, capture.CreateReplayResolver(), out catalog, slotGeneration);
    }

    // An opaque canonical execution, never a caller-supplied environment digest.
    // The store retains owner, workspace and source custody around this pure projection.
    public static bool TryReadCurrentEnvironment(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCreationRuleSourceCapture capture, CharacterCurrentRuleEnvironmentExecution execution,
        ICharacterFileQueries queries, string runtime, out CharacterCareerGearSaleCatalog? catalog,
        string? slotGeneration, CancellationToken cancellationToken = default)
    {
        catalog = null;
        if (!capture.MatchesOriginalCharacter(saved.Document.Content)
            || execution.QueryCatalog(saved, capture, queries, CharacterCurrentRuleDomain.Gear, cancellationToken) is null)
            return false;
        var resolution = capture.CreateCurrentEnvironmentResolution(execution.ExecutionProfile, execution.ExecutionProfileFingerprint);
        if (resolution is null || !TryCapturedEnvironment(capture, resolution.Resolver, saved.Document.Content, out string environment)
            || !TryReadBound(owner, saved, runtime, () => environment, resolution.Resolver, out catalog, slotGeneration)) return false;
        if (execution.Revalidate(saved, capture, queries, cancellationToken)) return true;
        catalog = null;
        return false;
    }

    private static bool TryReadBound(OwnerScope owner, WorkspaceStoredDocument saved, string runtime,
        Func<string?> readEnvironment, ICharacterSourceDataResolver resolver, out CharacterCareerGearSaleCatalog? catalog, string? slotGeneration)
    {
        catalog = null;
        if (saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || !IsDigest(runtime) || !IsValidHistory(saved) || !IsGeneration(slotGeneration)
            || saved.LocalHistory is not { } local || !local.IsValid(saved.ContentRevision)) return false;
        var document = saved.Document;
        if (document.Format != WorkspaceDocumentFormat.NativeXml || document.RulesetId != "sr5"
            || document.SchemaVersion != 1 || document.PayloadKind is not ("workspace" or "sr5/chum5-xml")) return false;
        try
        {
            var nodes = CharacterCareerGearSaleAuthority.ReadInstalled(document.Content, resolver);
            string? environment = readEnvironment();
            if (environment is null) return false;
            var binding = new CharacterCareerGearSaleBinding(owner.NormalizedValue, owner.IsLocalSingleUser,
                saved.Id, saved.ContentRevision, saved.SavedRevision, SourceDigest(document),
                document.AuxiliaryStateDigest, environment, runtime, local.IncarnationId, slotGeneration!);
            if (!MatchesOwner(owner, binding)) return false;
            catalog = new(binding, nodes);
            return true;
        }
        catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException
            or IOException or UnauthorizedAccessException or FormatException) { return false; }
    }

    public static bool TryEnvironment(ICharacterSourceDataResolver sources, string xml, out string digest)
    {
        digest = "";
        // Use the existing Core private source capture, including selected profile,
        // source ordering and effective policy. Never accept a UI-supplied environment digest.
        if (sources is not ICharacterCreationRuleSourceCapture captureAuthority
            || !captureAuthority.TryCaptureRuleSources(xml, out var capture) || capture is null
            || !capture.MatchesOriginalCharacter(xml) || !CharacterCreationFinalizationDigest.IsCanonical(capture.IntegrityDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(capture.EffectivePolicyDigest)) return false;
        return TryCapturedEnvironment(capture, capture.CreateReplayResolver(), xml, out digest);
    }

    private static bool TryCapturedEnvironment(CharacterCreationRuleSourceCapture capture,
        ICharacterSourceDataResolver resolver, string xml, out string digest)
    {
        digest = "";
        if (!capture.MatchesOriginalCharacter(xml) || !CharacterCreationFinalizationDigest.IsCanonical(capture.IntegrityDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(capture.EffectivePolicyDigest)) return false;
        var context = resolver.TryCreateContext(xml);
        if (context is null || !context.TryResolveCareerSettingsProfile(out string profile, out string state)
            || string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(state)
            || !context.TryResolveCareerSettingsProfile(out string again, out string stateAgain)
            || profile != again || state != stateAgain) return false;
        digest = Hash("chummer.gear-sale.environment/v1\0" + JsonSerializer.Serialize(new
            { capture.IntegrityDigest, capture.EffectivePolicyDigest, profile, state }));
        return true;
    }

    private static string SourceDigest(WorkspaceDocument d) => Hash(JsonSerializer.Serialize(new
        { d.Format, d.RulesetId, d.SchemaVersion, d.PayloadKind, PayloadDigest = Hash(d.Content) }));

    public static bool TryPlan(CharacterCareerGearSaleCatalog catalog, CharacterCareerGearSaleRequest request,
        out CharacterCareerGearSaleCommand? command)
    {
        command = null;
        if (!CharacterCareerGearSaleAuthority.IsRequest(request)) return false;
        var matches = catalog.Nodes.Where(n => n.InstanceId == request.InstanceId).ToArray();
        if (matches.Length != 1 || !CharacterCareerGearSaleAuthority.TryQuote(matches[0], request, out var quote)) return false;
        var candidate = new CharacterCareerGearSaleCommand(request, catalog.Binding, quote!, true);
        if (!IsConfirmedCommand(candidate)) return false;
        command = candidate with { ExplicitlyConfirmed = false };
        return true;
    }

    public static CharacterCareerGearSaleReceipt Seal(CharacterCareerGearSaleCommand command,
        CharacterCareerGearSaleOutcome outcome, long revision, string? payload = null, string? rejection = null)
    {
        var entry = new CharacterCareerGearSaleReceipt(command, CommandDigest(command), outcome, revision,
            outcome == CharacterCareerGearSaleOutcome.Applied ? revision : null,
            payload is null ? null : Hash(payload), rejection, "");
        return entry with { ReceiptDigest = ReceiptDigest(entry) };
    }
    public static bool IsCoherent(CharacterCareerGearSaleReceipt? r)
        => r is not null && IsConfirmedCommand(r.Command) && r.RecordedRevision > 0
            && r.CommandDigest == CommandDigest(r.Command) && r.ReceiptDigest == ReceiptDigest(r)
            && (r.Outcome switch
            {
                CharacterCareerGearSaleOutcome.Applied => r.RecordedRevision == r.Command.Binding.ContentRevision + 1
                    && r.CommittedRevision == r.RecordedRevision && IsDigest(r.PayloadDigestAfter) && r.Rejection is null,
                CharacterCareerGearSaleOutcome.Pending => r.RecordedRevision == r.Command.Binding.ContentRevision
                    && r.CommittedRevision is null && r.PayloadDigestAfter is null && r.Rejection is null,
                CharacterCareerGearSaleOutcome.Rejected => r.CommittedRevision is null && r.PayloadDigestAfter is null
                    && r.Rejection is { Length: > 0 and <= 128 },
                _ => false
            });
    public static CharacterCareerGearSaleOriginalOutcome Original(CharacterCareerGearSaleReceipt r)
    {
        var c = r.Command; var b = c.Binding;
        return new(b.OwnerId, b.TrustedLocalOwner, b.WorkspaceId, b.IncarnationId, b.SlotGeneration,
            c.Request.OperationId, r.CommandDigest, b.ContentRevision, r.Outcome, r.RecordedRevision,
            r.CommittedRevision, r.Outcome == CharacterCareerGearSaleOutcome.Pending ? null : r.ReceiptDigest);
    }
    public static bool IsCoherentOriginal(CharacterCareerGearSaleOriginalOutcome? p)
        => p is not null && p.OwnerId is { Length: > 0 and <= 512 }
            && p.OwnerId == p.OwnerId.Trim().ToLowerInvariant()
            && (p.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue) == p.TrustedLocalOwner
            && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(p.WorkspaceId)
            && IsGeneration(p.IncarnationId) && IsGeneration(p.SlotGeneration) && p.OperationId != Guid.Empty
            && IsDigest(p.CommandDigest) && p.ExpectedRevision is > 0 and < long.MaxValue && p.RecordedRevision > 0
            && (p.Outcome switch
            {
                // NeverApplied's revision is the original binding, not today's saved revision.
                CharacterCareerGearSaleOutcome.Pending or CharacterCareerGearSaleOutcome.NeverApplied
                    => p.RecordedRevision == p.ExpectedRevision
                    && p.CommittedRevision is null && p.ReceiptDigest is null,
                CharacterCareerGearSaleOutcome.Applied => p.RecordedRevision == p.ExpectedRevision + 1
                    && p.CommittedRevision == p.RecordedRevision && IsDigest(p.ReceiptDigest),
                CharacterCareerGearSaleOutcome.Rejected => p.CommittedRevision is null && IsDigest(p.ReceiptDigest),
                _ => false
            });
    public static bool MatchesOriginal(CharacterCareerGearSaleCommand c, CharacterCareerGearSaleOriginalOutcome? p)
        => IsCoherentOriginal(p) && p!.OwnerId == c.Binding.OwnerId && p.TrustedLocalOwner == c.Binding.TrustedLocalOwner
            && p.WorkspaceId == c.Request.WorkspaceId && p.IncarnationId == c.Binding.IncarnationId
            && p.SlotGeneration == c.Binding.SlotGeneration && p.OperationId == c.Request.OperationId
            && p.CommandDigest == CommandDigest(c) && p.ExpectedRevision == c.Binding.ContentRevision;

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerGearSaleReceipt>? entries)
    {
        if (entries is null) return true;
        if (entries.Count > MaximumReceipts) return false;
        var ids = new HashSet<Guid>();
        var expenses = new HashSet<Guid>();
        var sold = new HashSet<Guid>();
        long bytes = 2;
        foreach (var r in entries)
        {
            if (!IsCoherent(r) || r.Command.Request.WorkspaceId != id || r.RecordedRevision > revision
                || !ids.Add(r.Command.Request.OperationId)) return false;
            if (r.Outcome == CharacterCareerGearSaleOutcome.Applied)
            {
                // One operation namespace/custody ledger across kinds. Remove has no expense
                // identity; protect every removed child identity, not just the selected root.
                if (r.Command.Request.OperationKind == CharacterCareerGearOperationKind.Sale)
                {
                    if (!expenses.Add(r.Command.Request.ExpenseId) || !sold.Add(r.Command.Request.InstanceId)) return false;
                }
                else if (r.Command.Quote.RemovedItems!.Any(item => !sold.Add(item.InstanceId))) return false;
            }
            bytes += JsonSerializer.SerializeToUtf8Bytes(r).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
        }
        return entries.Count(r => r.Outcome == CharacterCareerGearSaleOutcome.Pending) <= 1;
    }
    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var history = saved.Document.AuxiliaryState.CharacterCareerGearSaleReceipts;
        return WorkspaceDocumentAuxiliaryState.HasSafeExtensionData(saved.Document.AuxiliaryState)
            && IsValidLedger(saved.Id, saved.ContentRevision, history)
            && (history ?? []).All(r => r.CommittedRevision is not { } revision || saved.SavedRevision >= revision)
            && (history ?? []).All(r => r.Outcome != CharacterCareerGearSaleOutcome.Pending
                || r.RecordedRevision == saved.ContentRevision)
            && (history ?? []).All(r => r.CommittedRevision != saved.ContentRevision
                || r.PayloadDigestAfter == Hash(saved.Document.Content));
    }
    public static CharacterCareerGearSaleResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operation, string digest)
    {
        if (!IsValidHistory(saved)) return new(CharacterCareerGearSaleOutcome.Corrupt);
        var entry = saved.Document.AuxiliaryState.CharacterCareerGearSaleReceipts?
            .SingleOrDefault(r => r.Command.Request.OperationId == operation);
        if (entry is null) return new(CharacterCareerGearSaleOutcome.NotFound, CurrentRevision: saved.ContentRevision);
        if (!MatchesOwner(owner, entry.Command.Binding) || entry.CommandDigest != digest
            || saved.LocalHistory?.IncarnationId != entry.Command.Binding.IncarnationId
            || entry.Outcome == CharacterCareerGearSaleOutcome.Applied && !saved.CanReplayReceipt(entry.RecordedRevision))
            return new(CharacterCareerGearSaleOutcome.Conflict);
        return new(entry.Outcome == CharacterCareerGearSaleOutcome.Applied
            ? CharacterCareerGearSaleOutcome.Recorded : entry.Outcome, entry, saved.ContentRevision);
    }

    public static bool TryBuild(WorkspaceStoredDocument original, CharacterCareerGearSaleCatalog current,
        CharacterCareerGearSaleCommand command, out WorkspaceDocument? replacement,
        out CharacterCareerGearSaleReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!IsConfirmedCommand(command) || !Same(current.Binding, command.Binding)
            || original.LocalHistory?.IncarnationId != current.Binding.IncarnationId
            || original.Id != current.Binding.WorkspaceId || original.ContentRevision != current.Binding.ContentRevision
            || original.SavedRevision != current.Binding.SavedRevision || SourceDigest(original.Document) != current.Binding.SourceDigest
            || original.Document.AuxiliaryStateDigest != current.Binding.AuxiliaryDigest || !IsValidHistory(original)
            || original.Document.AuxiliaryState.CharacterCareerGearSaleReceipts?.Any(
                r => r.Command.Request.OperationId == command.Request.OperationId || r.Outcome == CharacterCareerGearSaleOutcome.Pending) == true
            || !TryPlan(current, command.Request, out var plan)
            || !Same(plan! with { ExplicitlyConfirmed = true }, command)) return false;
        string payload;
        try { payload = CharacterCareerGearSaleAuthority.Materialize(original.Document.Content, command); }
        catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
        receipt = Seal(command, CharacterCareerGearSaleOutcome.Applied, original.ContentRevision + 1, payload);
        var history = original.Document.AuxiliaryState.CharacterCareerGearSaleReceipts ?? [];
        var entries = history.Append(receipt).ToArray();
        if (!IsValidLedger(original.Id, receipt.RecordedRevision, entries)) return false;
        replacement = original.Document with { State = original.Document.State with { Payload = payload,
            AuxiliaryState = original.Document.AuxiliaryState with { CharacterCareerGearSaleReceipts = entries } } };
        return true;
    }
}
