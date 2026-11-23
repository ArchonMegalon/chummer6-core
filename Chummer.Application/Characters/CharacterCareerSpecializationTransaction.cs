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

public static class CharacterCareerSpecializationTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool IsGeneration(string? value) => Guid.TryParseExact(value, "N", out var id)
        && id != Guid.Empty && id.ToString("N") == value;
    public static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerSpecializationCommand command)
        => Hash("chummer.specialization.command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerSpecializationReceipt receipt)
        => Hash("chummer.specialization.receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    // Detach caller-owned DTOs before validating or hashing an original.
    public static CharacterCareerSpecializationCommand? FreezeConfirmedCommand(CharacterCareerSpecializationCommand command)
    {
        try
        {
            var frozen = JsonSerializer.Deserialize<CharacterCareerSpecializationCommand>(JsonSerializer.SerializeToUtf8Bytes(command));
            return IsConfirmedCommand(frozen) ? frozen : null;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        { return null; }
    }
    public static bool MatchesOwner(OwnerScope owner, CharacterCareerSpecializationBinding binding)
        => !string.IsNullOrWhiteSpace(owner.NormalizedValue) && owner.NormalizedValue.Length <= 512
            && owner.NormalizedValue == binding.OwnerId && owner.IsLocalSingleUser == binding.TrustedLocalOwner
            && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool IsConfirmedCommand(CharacterCareerSpecializationCommand? c)
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
            && CharacterCareerSpecializationAuthority.IsRequest(c.Request)
            && CharacterCareerSpecializationAuthority.IsQuote(c.Request, c.Quote);


    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        string runtime, out CharacterCareerSpecializationCatalog? catalog, string? slotGeneration = null)
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
        ICharacterFileQueries queries, string runtime, out CharacterCareerSpecializationCatalog? catalog,
        string? slotGeneration, CancellationToken cancellationToken = default)
    {
        catalog = null;
        if (!capture.MatchesOriginalCharacter(saved.Document.Content)
            || execution.QueryCatalog(saved, capture, queries, CharacterCurrentRuleDomain.Skills, cancellationToken) is null)
            return false;
        var inputs = execution.ReadCareerSkillSpecializationInputs2559(saved, capture, queries, cancellationToken);
        var resolution = capture.CreateCurrentEnvironmentResolution(execution.ExecutionProfile, execution.ExecutionProfileFingerprint);
        if (inputs is null || resolution is null
            || !TryCapturedEnvironment(capture, resolution.Resolver, saved.Document.Content, out string environment)
            || !TryReadBound(owner, saved, runtime, () => environment, resolution.Resolver, out catalog, slotGeneration, inputs)) return false;
        if (execution.Revalidate(saved, capture, queries, cancellationToken)) return true;
        catalog = null;
        return false;
    }

    private static bool TryReadBound(OwnerScope owner, WorkspaceStoredDocument saved, string runtime,
        Func<string?> readEnvironment, ICharacterSourceDataResolver resolver, out CharacterCareerSpecializationCatalog? catalog,
        string? slotGeneration, IReadOnlyList<CharacterCareerSkillSpecializationInput>? currentInputs = null)
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
            var projection = currentInputs is null
                ? SpecializationSourceProjector.ProjectInputs(document.Content, resolver)
                : (Inputs: currentInputs, OmittedSkillCount: 0);
            string? environment = readEnvironment();
            if (environment is null) return false;
            var binding = new CharacterCareerSpecializationBinding(owner.NormalizedValue, owner.IsLocalSingleUser,
                saved.Id, saved.ContentRevision, saved.SavedRevision, SourceDigest(document),
                document.AuxiliaryStateDigest, environment, runtime, local.IncarnationId, slotGeneration!);
            if (!MatchesOwner(owner, binding)) return false;
            catalog = new(binding, projection.Inputs, projection.OmittedSkillCount);
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
        digest = Hash("chummer.specialization.environment/v1\0" + JsonSerializer.Serialize(new
            { capture.IntegrityDigest, capture.EffectivePolicyDigest, profile, state }));
        return true;
    }

    private static string SourceDigest(WorkspaceDocument d) => Hash(JsonSerializer.Serialize(new
        { d.Format, d.RulesetId, d.SchemaVersion, d.PayloadKind, PayloadDigest = Hash(d.Content) }));

    public static bool TryPlan(CharacterCareerSpecializationCatalog catalog, CharacterCareerSpecializationRequest request,
        out CharacterCareerSpecializationCommand? command)
    {
        command = null;
        if (!CharacterCareerSpecializationAuthority.IsRequest(request)) return false;
        var matches = catalog.Skills.Where(n => n.Identity == request.SkillIdentity).ToArray();
        if (matches.Length != 1 || !CharacterCareerSkillSpecializationRules.TryCreateQuote(
                matches[0] with { Selection = request.Selection }, out var quote) || !quote.CanAdd) return false;
        var candidate = new CharacterCareerSpecializationCommand(request, catalog.Binding, quote!, true);
        if (!IsConfirmedCommand(candidate)) return false;
        command = candidate with { ExplicitlyConfirmed = false };
        return true;
    }

    public static CharacterCareerSpecializationReceipt Seal(CharacterCareerSpecializationCommand command,
        CharacterCareerSpecializationOutcome outcome, long revision, string? payload = null, string? rejection = null)
    {
        var entry = new CharacterCareerSpecializationReceipt(command, CommandDigest(command), outcome, revision,
            outcome == CharacterCareerSpecializationOutcome.Applied ? revision : null,
            payload is null ? null : Hash(payload), rejection, "");
        return entry with { ReceiptDigest = ReceiptDigest(entry) };
    }
    public static bool IsCoherent(CharacterCareerSpecializationReceipt? r)
        => r is not null && IsConfirmedCommand(r.Command) && r.RecordedRevision > 0
            && r.CommandDigest == CommandDigest(r.Command) && r.ReceiptDigest == ReceiptDigest(r)
            && (r.Outcome switch
            {
                CharacterCareerSpecializationOutcome.Applied => r.RecordedRevision == r.Command.Binding.ContentRevision + 1
                    && r.CommittedRevision == r.RecordedRevision && IsDigest(r.PayloadDigestAfter) && r.Rejection is null,
                CharacterCareerSpecializationOutcome.Pending => r.RecordedRevision == r.Command.Binding.ContentRevision
                    && r.CommittedRevision is null && r.PayloadDigestAfter is null && r.Rejection is null,
                CharacterCareerSpecializationOutcome.Rejected => r.CommittedRevision is null && r.PayloadDigestAfter is null
                    && r.Rejection is { Length: > 0 and <= 128 },
                _ => false
            });
    public static CharacterCareerSpecializationOriginalOutcome Original(CharacterCareerSpecializationReceipt r)
    {
        var c = r.Command; var b = c.Binding;
        return new(b.OwnerId, b.TrustedLocalOwner, b.WorkspaceId, b.IncarnationId, b.SlotGeneration,
            c.Request.OperationId, r.CommandDigest, b.ContentRevision, r.Outcome, r.RecordedRevision,
            r.CommittedRevision, r.Outcome == CharacterCareerSpecializationOutcome.Pending ? null : r.ReceiptDigest);
    }
    public static bool IsCoherentOriginal(CharacterCareerSpecializationOriginalOutcome? p)
        => p is not null && p.OwnerId is { Length: > 0 and <= 512 }
            && p.OwnerId == p.OwnerId.Trim().ToLowerInvariant()
            && (p.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue) == p.TrustedLocalOwner
            && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(p.WorkspaceId)
            && IsGeneration(p.IncarnationId) && IsGeneration(p.SlotGeneration) && p.OperationId != Guid.Empty
            && IsDigest(p.CommandDigest) && p.ExpectedRevision is > 0 and < long.MaxValue && p.RecordedRevision > 0
            && (p.Outcome switch
            {
                // NeverApplied's revision is the original binding, not today's saved revision.
                CharacterCareerSpecializationOutcome.Pending or CharacterCareerSpecializationOutcome.NeverApplied
                    => p.RecordedRevision == p.ExpectedRevision
                    && p.CommittedRevision is null && p.ReceiptDigest is null,
                CharacterCareerSpecializationOutcome.Applied => p.RecordedRevision == p.ExpectedRevision + 1
                    && p.CommittedRevision == p.RecordedRevision && IsDigest(p.ReceiptDigest),
                CharacterCareerSpecializationOutcome.Rejected => p.CommittedRevision is null && IsDigest(p.ReceiptDigest),
                _ => false
            });
    public static bool MatchesOriginal(CharacterCareerSpecializationCommand c, CharacterCareerSpecializationOriginalOutcome? p)
        => IsCoherentOriginal(p) && p!.OwnerId == c.Binding.OwnerId && p.TrustedLocalOwner == c.Binding.TrustedLocalOwner
            && p.WorkspaceId == c.Request.WorkspaceId && p.IncarnationId == c.Binding.IncarnationId
            && p.SlotGeneration == c.Binding.SlotGeneration && p.OperationId == c.Request.OperationId
            && p.CommandDigest == CommandDigest(c) && p.ExpectedRevision == c.Binding.ContentRevision;

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerSpecializationReceipt>? entries)
    {
        if (entries is null) return true;
        if (entries.Count > MaximumReceipts) return false;
        var ids = new HashSet<Guid>();
        var expenses = new HashSet<Guid>();
        var specializations = new HashSet<Guid>();
        long bytes = 2;
        foreach (var r in entries)
        {
            if (!IsCoherent(r) || r.Command.Request.WorkspaceId != id || r.RecordedRevision > revision
                || !ids.Add(r.Command.Request.OperationId)) return false;
            if (r.Outcome == CharacterCareerSpecializationOutcome.Applied)
            {
                if (!expenses.Add(r.Command.Request.ExpenseId) || !specializations.Add(r.Command.Request.SpecializationId))
                    return false;
            }
            bytes += JsonSerializer.SerializeToUtf8Bytes(r).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
        }
        return entries.Count(r => r.Outcome == CharacterCareerSpecializationOutcome.Pending) <= 1;
    }
    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var history = saved.Document.AuxiliaryState.CharacterCareerSpecializationReceipts;
        return WorkspaceDocumentAuxiliaryState.HasSafeExtensionData(saved.Document.AuxiliaryState)
            && IsValidLedger(saved.Id, saved.ContentRevision, history)
            && (history ?? []).All(r => r.CommittedRevision is not { } revision || saved.SavedRevision >= revision)
            && (history ?? []).All(r => r.Outcome != CharacterCareerSpecializationOutcome.Pending
                || r.RecordedRevision == saved.ContentRevision)
            && (history ?? []).All(r => r.CommittedRevision != saved.ContentRevision
                || r.PayloadDigestAfter == Hash(saved.Document.Content));
    }
    public static CharacterCareerSpecializationResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operation, string digest)
    {
        if (!IsValidHistory(saved)) return new(CharacterCareerSpecializationOutcome.Corrupt);
        var entry = saved.Document.AuxiliaryState.CharacterCareerSpecializationReceipts?
            .SingleOrDefault(r => r.Command.Request.OperationId == operation);
        if (entry is null) return new(CharacterCareerSpecializationOutcome.NotFound, CurrentRevision: saved.ContentRevision);
        if (!MatchesOwner(owner, entry.Command.Binding) || entry.CommandDigest != digest
            || saved.LocalHistory?.IncarnationId != entry.Command.Binding.IncarnationId
            || entry.Outcome == CharacterCareerSpecializationOutcome.Applied && !saved.CanReplayReceipt(entry.RecordedRevision))
            return new(CharacterCareerSpecializationOutcome.Conflict);
        return new(entry.Outcome == CharacterCareerSpecializationOutcome.Applied
            ? CharacterCareerSpecializationOutcome.Recorded : entry.Outcome, entry, saved.ContentRevision);
    }

    public static bool TryBuild(WorkspaceStoredDocument original, CharacterCareerSpecializationCatalog current,
        CharacterCareerSpecializationCommand command, out WorkspaceDocument? replacement,
        out CharacterCareerSpecializationReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!IsConfirmedCommand(command) || !Same(current.Binding, command.Binding)
            || original.LocalHistory?.IncarnationId != current.Binding.IncarnationId
            || original.Id != current.Binding.WorkspaceId || original.ContentRevision != current.Binding.ContentRevision
            || original.SavedRevision != current.Binding.SavedRevision || SourceDigest(original.Document) != current.Binding.SourceDigest
            || original.Document.AuxiliaryStateDigest != current.Binding.AuxiliaryDigest || !IsValidHistory(original)
            || original.Document.AuxiliaryState.CharacterCareerSpecializationReceipts?.Any(
                r => r.Command.Request.OperationId == command.Request.OperationId || r.Outcome == CharacterCareerSpecializationOutcome.Pending) == true
            || !TryPlan(current, command.Request, out var plan)
            || !Same(plan! with { ExplicitlyConfirmed = true }, command)) return false;
        string payload;
        try { payload = CharacterCareerSpecializationAuthority.Materialize(original.Document.Content, command); }
        catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
        receipt = Seal(command, CharacterCareerSpecializationOutcome.Applied, original.ContentRevision + 1, payload);
        var history = original.Document.AuxiliaryState.CharacterCareerSpecializationReceipts ?? [];
        var entries = history.Append(receipt).ToArray();
        if (!IsValidLedger(original.Id, receipt.RecordedRevision, entries)) return false;
        replacement = original.Document with { State = original.Document.State with { Payload = payload,
            AuxiliaryState = original.Document.AuxiliaryState with { CharacterCareerSpecializationReceipts = entries } } };
        return true;
    }
}
