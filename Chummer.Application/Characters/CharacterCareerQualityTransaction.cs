using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Pure preparation and history validation; never a generic-write authorization.</summary>
public static partial class CharacterCareerQualityTransaction
{
    public const int MaximumEntries = 1024;
    public const int MaximumLedgerBytes = 8 * 1024 * 1024;
    public const int MaximumRemovedNodeLength = 1024 * 1024;
    public static string LedgerRoot => Hash("chummer.core.sr5-career-quality-ledger/v1");
    private static string Hash(string value) => CharacterCareerQualityGraph.Hash(value);
    private static bool Digest(string? value) => CharacterCareerReputationTransaction.IsDigest(value);
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string EntryDigest(CharacterCareerQualityTransactionEntry entry)
        => Hash("chummer.core.sr5-career-quality-entry/v1\0" + JsonSerializer.Serialize(entry with { EntryDigest = "" }));

    public static bool IsValidOwner(OwnerScope owner) => !string.IsNullOrWhiteSpace(owner.Value)
        && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);

    public static bool IsValidPlan(CharacterCareerQualityPlan? plan)
    {
        try { return CharacterCareerQualityRules.IsCoherent(plan); }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException
            or InvalidOperationException or OverflowException) { return false; }
    }

    public static bool IsCoherent(CharacterCareerQualityTransactionEntry? entry)
    {
        try
        {
            if (entry is null || !IsValidPlan(entry.Plan)
                || !CharacterCareerQualityRules.IsCoherent(entry.Quote)
                || !CharacterCareerQualityRules.IsCoherent(entry.Receipt)
                || !CharacterCareerQualityRules.IsCoherent(entry.ObservedPostState)
                || entry.ObservedExpense is null
                || entry.Source?.RawSourceXml is not { Length: > 0 and <= CharacterCreationQualitiesRules.MaximumSourceNodeLength }
                || entry.Source.Name is not { Length: > 0 and <= CharacterCareerQualityRules.MaximumTextLength }
                || entry.Source.SourceBook is not { Length: > 0 and <= CharacterCareerQualityRules.MaximumTextLength }
                || !SourceDigest(entry.Source.SourceInputsDigest) || !SourceDigest(entry.Source.ProfileInputsDigest)
                || !SourceDigest(entry.Source.CustomDataInputsDigest)
                || entry.Source.SourceId != entry.Plan.Identity.SourceId
                || entry.RemovedNodes is null || entry.RemovedNodes.Count > 100_000
                || entry.ResolvedArmCount is < 0 or > 1000
                || !Digest(entry.CharacterDigestBefore) || !Digest(entry.CharacterDigestAfter)
                || !Digest(entry.AuxiliaryDigestBefore) || !Digest(entry.PreviousEntryDigest) || !Digest(entry.EntryDigest))
                return false;
            var positions = new HashSet<(string, int)>();
            long bytes = 0;
            foreach (var node in entry.RemovedNodes)
            {
                if (node is null || node.Container is not ("qualities" or "improvements")
                    || node.Index is < 0 or >= CharacterCareerReputationProjector.MaximumRowsPerContainer
                    || node.Xml is not { Length: > 0 and <= MaximumRemovedNodeLength }
                    || !positions.Add((node.Container, node.Index))) return false;
                bytes += System.Text.Encoding.UTF8.GetByteCount(node.Xml);
                if (bytes > MaximumLedgerBytes) return false;
            }
            if (entry.Plan.Operation == CharacterCareerQualityOperation.AcquireLevel && entry.RemovedNodes.Count != 0)
                return false;
            if (entry.Quote.SourceRevision != Hash(JsonSerializer.Serialize(entry.Source))
                || !CharacterCareerQualityRules.TryPlan(entry.Quote, entry.Plan.ExpectedLogicalRevision,
                    entry.Plan.ExpectedSourceRevision, entry.Plan.ExpectedRuleDigest, entry.Plan.ExpectedRuntimeFingerprint,
                    entry.Plan.ExpectedContentDigest, entry.Plan.ExpectedWorkspaceRevision, entry.Plan.ExpectedSavedRevision,
                    true, false, entry.Plan.TransactionId, entry.Plan.ExpenseDateLocal, out var expected)
                || !Same(entry.Plan, expected)) return false;
            // Validate historical receipt arithmetic/coherence without claiming
            // it describes the current workspace. Lookup checks current bytes.
            var binding = entry.Quote.Binding with { WorkspaceRevision = entry.Plan.TargetWorkspaceRevision,
                SavedRevision = entry.Plan.TargetSavedRevision };
            if (!CharacterCareerQualityRules.TryCreateStateObservation(entry.Plan.Identity, entry.Plan.Definition,
                    entry.Plan.Extra, entry.Plan.SourceName, entry.Plan.InstancesAfter, entry.Plan.SavedCharacterKarma,
                    binding, JsonSerializer.Serialize(entry.Source), entry.Quote.RuleDigest, out var state)
                || !Same(state, entry.ObservedPostState) || !Same(ExpectedExpense(entry.Plan), entry.ObservedExpense)
                || !CharacterCareerQualityRules.TryCreateReceipt(entry.Plan.TransactionId, entry.Quote, entry.Plan,
                    entry.ObservedPostState, entry.ObservedExpense, out var receipt) || !Same(entry.Receipt, receipt)) return false;
            return EntryDigest(entry) == entry.EntryDigest;
        }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException
            or InvalidOperationException or OverflowException) { return false; }
    }

    public static bool IsValidLedger(CharacterWorkspaceId workspace, long revision,
        IReadOnlyList<CharacterCareerQualityTransactionEntry>? entries)
    {
        if (entries is null) return true;
        if (revision <= 0 || entries.Count > MaximumEntries) return false;
        string previous = LedgerRoot;
        long lastRevision = 0, bytes = 2;
        var ids = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (!IsCoherent(entry) || entry.Plan.WorkspaceId != workspace.Value
                || entry.Plan.TargetWorkspaceRevision > revision || entry.Plan.TargetWorkspaceRevision <= lastRevision
                || entry.PreviousEntryDigest != previous || !ids.Add(entry.Plan.TransactionId)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(entry).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            previous = entry.EntryDigest;
            lastRevision = entry.Plan.TargetWorkspaceRevision;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var entries = saved.Document.AuxiliaryState.CharacterCareerQualityTransactions;
        var corrections = saved.Document.AuxiliaryState.CharacterCareerQualityCorrections;
        if (!IsValidLedgers(saved.Id, saved.ContentRevision, entries, corrections)) return false;
        long lastRevision = entries is { Count: > 0 } ? entries[^1].Plan.TargetWorkspaceRevision : 0;
        string? lastDigest = entries is { Count: > 0 } ? entries[^1].CharacterDigestAfter : null;
        if (corrections is { Count: > 0 } && corrections[^1].Correction.TargetWorkspaceRevision > lastRevision)
        {
            lastRevision = corrections[^1].Correction.TargetWorkspaceRevision;
            lastDigest = corrections[^1].RestoredCharacterDigest;
        }
        return saved.SavedRevision >= lastRevision
            && (lastRevision == 0 || saved.ContentRevision != lastRevision || Hash(saved.Document.Content) == lastDigest);
    }

    public static CharacterCareerQualityCommitResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerQualityPlan plan)
    {
        if (!IsValidOwner(owner) || !IsValidPlan(plan) || owner.NormalizedValue != plan.OwnerId
            || saved.Id.Value != plan.WorkspaceId) return new(CharacterCareerQualityCommitOutcome.Conflict);
        if (!IsValidHistory(saved)) return new(CharacterCareerQualityCommitOutcome.Corrupt, Error: "quality_history_invalid");
        var entry = saved.Document.AuxiliaryState.CharacterCareerQualityTransactions?
            .SingleOrDefault(row => row.Plan.TransactionId == plan.TransactionId);
        if (entry is null) return new(CharacterCareerQualityCommitOutcome.NotFound, CurrentRevision: saved.ContentRevision);
        if (!Same(entry.Plan, plan) || !saved.CanReplayReceipt(entry.Plan.TargetWorkspaceRevision))
            return new(CharacterCareerQualityCommitOutcome.IdempotencyConflict, CurrentRevision: saved.ContentRevision);
        if (saved.ContentRevision == entry.Plan.TargetWorkspaceRevision)
        {
            if (!CharacterCareerQualityGraph.TryObserve(saved.Document.Content, entry.Source, plan.Identity,
                    entry.ResolvedArmCount, entry.Quote.Binding with { WorkspaceRevision = saved.ContentRevision,
                        SavedRevision = saved.SavedRevision }, entry.Quote.RuleDigest, plan.TransactionId,
                    out var state, out var expense)
                || !CharacterCareerQualityRules.TryRecoverReceipt(entry.Receipt, plan.TransactionId, state,
                    expense, entry.Receipt.ReceiptDigest, out _))
                return new(CharacterCareerQualityCommitOutcome.Corrupt, CurrentRevision: saved.ContentRevision,
                    Error: "quality_receipt_readback_invalid");
        }
        // Later revisions retain historical replay suppression, not a claim
        // that the current character still equals this transaction's post-state.
        return new(CharacterCareerQualityCommitOutcome.Replayed, entry, saved.ContentRevision);
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerQualityPlan plan,
        ICharacterCareerQualityAdmission admission, string runtimeFingerprint,
        out WorkspaceDocument? replacement, out CharacterCareerQualityTransactionEntry? entry)
    {
        replacement = null;
        entry = null;
        if (!IsValidPlan(plan) || !IsValidOwner(owner) || !IsValidHistory(saved)
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml || saved.Document.RulesetId != "sr5"
            || saved.Document.SchemaVersion != 1 || saved.Document.PayloadKind is not ("workspace" or "sr5/chum5-xml")
            || saved.Id.Value != plan.WorkspaceId || owner.NormalizedValue != plan.OwnerId
            || saved.ContentRevision != plan.ExpectedWorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || plan.ExpectedSavedRevision != saved.SavedRevision || plan.ExpectedRuntimeFingerprint != runtimeFingerprint)
            return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerQualityTransactions;
        if (history is { Count: >= MaximumEntries } || history?.Any(row => row.Plan.TransactionId == plan.TransactionId) == true
            || saved.Document.AuxiliaryState.CharacterCareerQualityCorrections?.Any(row => row.Correction.CorrectionId == plan.TransactionId) == true)
            return false;
        var input = admission.Input;
        var source = admission.Source;
        int? arms = admission.ResolvedArmCount;
        if (input is null || input.Binding != new CharacterCareerQualityExecutionBinding(owner.NormalizedValue,
                saved.Id.Value, saved.ContentRevision, saved.SavedRevision, runtimeFingerprint, plan.ExpectedContentDigest)
            || !CharacterCareerQualityRules.TryCreateQuote(input, out var quote)
            || !CharacterCareerQualityGraph.TryProject(saved.Document.Content, source, plan.Identity, plan.Operation, arms, out var graph)
            || !CharacterCareerQualityGraph.TryApply(saved.Document.Content, graph!, quote, plan, out var payload)
            || !CharacterCareerQualityGraph.TryObserve(payload!, source, plan.Identity, arms,
                input.Binding with { WorkspaceRevision = plan.TargetWorkspaceRevision, SavedRevision = plan.TargetSavedRevision },
                quote.RuleDigest, plan.TransactionId, out var state, out var expense)
            || !CharacterCareerQualityRules.TryCreateReceipt(plan.TransactionId, quote, plan, state, expense, out var receipt))
            return false;
        var removed = CaptureRemovedNodes(saved.Document.Content, plan);
        if (removed is null || !admission.IsCurrent()) return false;
        var candidate = new CharacterCareerQualityTransactionEntry(quote, plan, receipt, state!, expense!, source, arms, removed,
            Hash(saved.Document.Content), Hash(payload!), saved.Document.AuxiliaryStateDigest,
            history is { Count: > 0 } ? history[^1].EntryDigest : LedgerRoot, "");
        candidate = candidate with { EntryDigest = EntryDigest(candidate) };
        var appended = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedgers(saved.Id, plan.TargetWorkspaceRevision, appended,
                saved.Document.AuxiliaryState.CharacterCareerQualityCorrections)) return false;
        replacement = saved.Document with { State = saved.Document.State with { Payload = payload!,
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerQualityTransactions = Array.AsReadOnly(appended) } } };
        entry = candidate;
        return true;
    }

    private static IReadOnlyList<CharacterCareerQualityRemovedNode>? CaptureRemovedNodes(string xml, CharacterCareerQualityPlan plan)
    {
        if (plan.Operation == CharacterCareerQualityOperation.AcquireLevel) return [];
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = CharacterCareerReputationProjector.MaximumCharacterXmlLength });
        var root = XElement.Load(reader);
        var result = new List<CharacterCareerQualityRemovedNode>();
        long bytes = 0;
        foreach (var (container, field) in new[] { ("qualities", "guid"), ("improvements", "sourcename") })
        {
            int index = 0;
            foreach (XElement node in root.Element(container)!.Elements())
            {
                if (Guid.TryParse(node.Element(field)?.Value, out var id) && plan.AffectedInternalIds.Contains(id))
                {
                    string fragment = node.ToString(SaveOptions.DisableFormatting);
                    bytes += System.Text.Encoding.UTF8.GetByteCount(fragment);
                    if (fragment.Length > MaximumRemovedNodeLength || bytes > MaximumLedgerBytes) return null;
                    result.Add(new(container, index, fragment));
                }
                index++;
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static CharacterCareerQualityExpenseObservation ExpectedExpense(CharacterCareerQualityPlan plan)
        => plan.CreatesExpense ? new(1, plan.ExpenseId, plan.ExpenseDateLocal, plan.ExpenseAmount, plan.ExpenseReason,
            plan.ExpenseType, plan.ExpenseRefund, plan.ForceCareerVisible, plan.KarmaUndoType, plan.NuyenUndoType,
            plan.UndoObjectId, plan.UndoQuantity, plan.UndoExtra)
            : new(0, Guid.Empty, DateTime.MinValue, 0, "", "", false, false, "", "", "", 0, "");

    private static bool SourceDigest(string? value) => value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal) && Digest(value[7..]);
}
