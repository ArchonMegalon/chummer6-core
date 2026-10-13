using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerQualityTransaction
{
    public static string CorrectionLedgerRoot => Hash("chummer.core.sr5-career-quality-corrections/v1");
    public static string CorrectionEntryDigest(CharacterCareerQualityCorrectionEntry entry)
        => Hash("chummer.core.sr5-career-quality-correction-entry/v1\0" + JsonSerializer.Serialize(entry with { EntryDigest = "" }));

    public static bool IsValidCorrection(CharacterCareerQualityCorrectionPlan? correction)
    {
        try { return CharacterCareerQualityRules.IsCoherent(correction); }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException
            or InvalidOperationException or OverflowException) { return false; }
    }

    public static bool IsValidLedgers(CharacterWorkspaceId workspace, long revision,
        IReadOnlyList<CharacterCareerQualityTransactionEntry>? transactions,
        IReadOnlyList<CharacterCareerQualityCorrectionEntry>? corrections)
    {
        if (!IsValidLedger(workspace, revision, transactions)) return false;
        if (corrections is null) return true;
        if (corrections.Count > MaximumEntries || corrections.Count > (transactions?.Count ?? 0)) return false;
        var byId = (transactions ?? []).ToDictionary(row => row.Plan.TransactionId);
        var usedIds = byId.Keys.ToHashSet();
        var corrected = new HashSet<Guid>();
        var revisions = (transactions ?? []).Select(row => row.Plan.TargetWorkspaceRevision).ToHashSet();
        string previous = CorrectionLedgerRoot;
        long lastRevision = 0, bytes = 2;
        foreach (var entry in corrections)
        {
            if (entry is null || !IsValidCorrection(entry.Correction)
                || !Digest(entry.RestoredCharacterDigest) || !Digest(entry.EntryDigest)
                || entry.PreviousEntryDigest != previous
                || entry.Correction.WorkspaceId != workspace.Value
                || entry.Correction.TargetWorkspaceRevision > revision
                || entry.Correction.TargetWorkspaceRevision <= lastRevision
                || !usedIds.Add(entry.Correction.CorrectionId)
                || !corrected.Add(entry.Correction.OriginalTransactionId)
                || !revisions.Add(entry.Correction.TargetWorkspaceRevision)
                || !byId.TryGetValue(entry.Correction.OriginalTransactionId, out var original)
                || !CorrectionMatchesOriginal(original, entry.Correction)
                || !CorrectionObservationMatches(original, entry)
                || CorrectionEntryDigest(entry) != entry.EntryDigest) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(entry).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            lastRevision = entry.Correction.TargetWorkspaceRevision;
            previous = entry.EntryDigest;
        }
        return true;
    }

    private static bool CorrectionMatchesOriginal(CharacterCareerQualityTransactionEntry original,
        CharacterCareerQualityCorrectionPlan correction)
    {
        // Historical coherence only. Before a write, the original post-state
        // must also be observed from the current stored XML below.
        return CharacterCareerQualityRules.TryCreateStateObservation(original.Plan.Identity, original.Plan.Definition,
            original.Plan.Extra, original.Plan.SourceName, original.Plan.InstancesAfter, original.Plan.SavedCharacterKarma,
            original.Quote.Binding with { WorkspaceRevision = original.Plan.TargetWorkspaceRevision,
                SavedRevision = original.Plan.TargetSavedRevision }, JsonSerializer.Serialize(original.Source),
            original.Quote.RuleDigest, out var post)
            && CharacterCareerQualityRules.TryPlanCorrection(original.Receipt, post, ExpectedExpense(original.Plan),
                correction.CorrectionId, correction.Reason, false, false, original.Receipt.ReceiptDigest, out var expected)
            && Same(correction, expected);
    }

    private static bool CorrectionObservationMatches(CharacterCareerQualityTransactionEntry original,
        CharacterCareerQualityCorrectionEntry entry)
        => CharacterCareerQualityRules.TryCreateStateObservation(original.Plan.Identity, original.Plan.Definition,
            original.Plan.Extra, original.Plan.SourceName, original.Receipt.InstancesBefore, original.Receipt.CharacterKarmaBefore,
            original.Quote.Binding with { WorkspaceRevision = entry.Correction.TargetWorkspaceRevision,
                SavedRevision = entry.Correction.TargetSavedRevision }, JsonSerializer.Serialize(original.Source),
            original.Quote.RuleDigest, out var expected)
            && Same(expected, entry.ObservedRestoredState)
            && Same(entry.ObservedExpense, new CharacterCareerQualityExpenseObservation(
                0, Guid.Empty, DateTime.MinValue, 0, "", "", false, false, "", "", "", 0, ""));

    public static CharacterCareerQualityCorrectionResult LookupCorrection(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerQualityCorrectionPlan correction)
    {
        if (!IsValidOwner(owner) || !IsValidCorrection(correction) || owner.NormalizedValue != correction.OwnerId
            || saved.Id.Value != correction.WorkspaceId) return new(CharacterCareerQualityCommitOutcome.Conflict);
        if (!IsValidHistory(saved)) return new(CharacterCareerQualityCommitOutcome.Corrupt, Error: "quality_history_invalid");
        var entries = saved.Document.AuxiliaryState.CharacterCareerQualityCorrections;
        var entry = entries?.SingleOrDefault(row => row.Correction.CorrectionId == correction.CorrectionId);
        if (entry is null)
        {
            bool reserved = entries?.Any(row => row.Correction.OriginalTransactionId == correction.OriginalTransactionId) == true
                || saved.Document.AuxiliaryState.CharacterCareerQualityTransactions?.Any(row => row.Plan.TransactionId == correction.CorrectionId) == true;
            return new(reserved ? CharacterCareerQualityCommitOutcome.IdempotencyConflict : CharacterCareerQualityCommitOutcome.NotFound,
                CurrentRevision: saved.ContentRevision);
        }
        if (!Same(entry.Correction, correction) || !saved.CanReplayReceipt(correction.TargetWorkspaceRevision))
            return new(CharacterCareerQualityCommitOutcome.IdempotencyConflict, CurrentRevision: saved.ContentRevision);
        if (saved.ContentRevision == correction.TargetWorkspaceRevision)
        {
            var original = saved.Document.AuxiliaryState.CharacterCareerQualityTransactions!
                .Single(row => row.Plan.TransactionId == correction.OriginalTransactionId);
            if (!CharacterCareerQualityGraph.TryObserve(saved.Document.Content, original.Source, original.Plan.Identity,
                    original.ResolvedArmCount, original.Quote.Binding with { WorkspaceRevision = saved.ContentRevision,
                        SavedRevision = saved.SavedRevision }, original.Quote.RuleDigest, original.Plan.TransactionId,
                    out var state, out var expense)
                || state!.AvailableKarma != original.Receipt.CharacterKarmaBefore
                || !Same(state.Instances, original.Receipt.InstancesBefore) || expense!.MatchingEntryCount != 0)
                return new(CharacterCareerQualityCommitOutcome.Corrupt, Error: "quality_correction_readback_invalid");
        }
        return new(CharacterCareerQualityCommitOutcome.Replayed, entry, saved.ContentRevision);
    }

    public static bool TryBuildCorrection(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerQualityCorrectionPlan correction, string runtimeFingerprint,
        out WorkspaceDocument? replacement, out CharacterCareerQualityCorrectionEntry? entry)
    {
        replacement = null;
        entry = null;
        if (LookupCorrection(owner, saved, correction).Outcome != CharacterCareerQualityCommitOutcome.NotFound
            || saved.ContentRevision != correction.ExpectedWorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || saved.SavedRevision != correction.ExpectedSavedRevision || correction.ExpectedRuntimeFingerprint != runtimeFingerprint
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml || saved.Document.RulesetId != "sr5"
            || saved.Document.SchemaVersion != 1 || saved.Document.PayloadKind is not ("workspace" or "sr5/chum5-xml")) return false;
        var original = saved.Document.AuxiliaryState.CharacterCareerQualityTransactions?
            .SingleOrDefault(row => row.Plan.TransactionId == correction.OriginalTransactionId);
        var history = saved.Document.AuxiliaryState.CharacterCareerQualityCorrections;
        if (original is null || history is { Count: >= MaximumEntries }
            || !saved.CanReplayReceipt(original.Plan.TargetWorkspaceRevision)
            || !CharacterCareerQualityGraph.TryApplyCorrection(saved.Document.Content, original, correction,
                out var payload, out var state, out var expense)) return false;
        var candidate = new CharacterCareerQualityCorrectionEntry(correction, state!, expense!, Hash(payload!),
            history is { Count: > 0 } ? history[^1].EntryDigest : CorrectionLedgerRoot, "");
        candidate = candidate with { EntryDigest = CorrectionEntryDigest(candidate) };
        var appended = (history ?? []).Append(candidate).ToArray();
        if (!IsValidLedgers(saved.Id, correction.TargetWorkspaceRevision,
                saved.Document.AuxiliaryState.CharacterCareerQualityTransactions, appended)) return false;
        replacement = saved.Document with { State = saved.Document.State with { Payload = payload!,
            AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerQualityCorrections = Array.AsReadOnly(appended) } } };
        entry = candidate;
        return true;
    }
}
