using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class OwnerBoundCharacterCareerQualityService
{
    public CharacterCareerQualityWorkspaceSnapshot? Read(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspace, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace.Value)
            || !CharacterCareerQualityTransaction.IsValidOwner(expectedOwner.Owner)
            || store is not ICharacterCareerQualityAtomicCommitCapability atomic
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease)) return null;
        using (lease)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (store is ICharacterCareerQualityCurrentCapability current)
                    return current.ReadCurrentCareerQuality(ownerContext, expectedOwner, lease!, workspace, authority, cancellationToken);
                var read = ReadStored();
                if (!read.Success || read.Value is not { } saved || saved.Id != workspace) return null;
                var snapshot = ReadSaved(expectedOwner.Owner, saved, atomic.CareerQualityRuntimeFingerprint, authority, cancellationToken);
                var after = ReadStored();
                return after.Success && JsonSerializer.Serialize(after.Value) == JsonSerializer.Serialize(saved) ? snapshot : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or OperationCanceledException) { return null; }
        }

        WorkspaceStoreReadResult ReadStored() => expectedOwner.Owner.IsLocalSingleUser
            ? store.Get(workspace) : store.Get(expectedOwner.Owner, workspace);
    }

    /// <summary>
    /// Read-only recovery of the exact locally committed receipt, including
    /// after unrelated later revisions. A binary update retires mutation authority, not durable
    /// evidence of a paid transaction. This never calls Commit or Correct and
    /// never substitutes the old runtime for current catalog admission.
    /// </summary>
    public CharacterCareerQualityCommitResult RecoverReceipt(OwnerContextStamp expectedOwner,
        CharacterCareerQualityPlan plan, CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerQualityTransaction.IsValidPlan(plan)
            || !CharacterCareerQualityTransaction.IsValidOwner(expectedOwner.Owner)
            || plan.OwnerId != expectedOwner.Owner.NormalizedValue
            || store is not ICharacterCareerQualityAtomicCommitCapability atomic
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_recovery_owner_rejected");
        using (lease)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var workspace = new CharacterWorkspaceId(plan.WorkspaceId);
                WorkspaceStoreReadResult ReadStored() => expectedOwner.Owner.IsLocalSingleUser
                    ? store.Get(workspace) : store.Get(expectedOwner.Owner, workspace);
                if (store is ICharacterCareerQualityCurrentCapability current)
                    return current.RecoverCurrentCareerQuality(ownerContext, expectedOwner, lease!, plan, authority, cancellationToken);
                var read = ReadStored();
                if (!read.Success || read.Value is not { } saved)
                    return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_recovery_workspace_unavailable");
                var recovered = RecoverSavedReceipt(expectedOwner.Owner, saved, plan, atomic.CareerQualityRuntimeFingerprint, authority, cancellationToken);
                var after = ReadStored();
                return after.Success && JsonSerializer.Serialize(after.Value) == JsonSerializer.Serialize(saved) ? recovered
                    : new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_recovery_workspace_changed");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or OperationCanceledException)
            { return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_recovery_unavailable"); }
        }
    }


    // Pure domain projections. The caller holds the actual owner/workspace lease,
    // selects exact admitted sources and fences the observation before returning.
    public static CharacterCareerQualityWorkspaceSnapshot? ReadSaved(Chummer.Contracts.Owners.OwnerScope owner,
        WorkspaceStoredDocument saved, string runtimeFingerprint, ICharacterCareerQualityAuthorityResolver authority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (saved.Document.Format != WorkspaceDocumentFormat.NativeXml
            || saved.Document.RulesetId != "sr5" || saved.ContentRevision != saved.SavedRevision
            || !CharacterCareerQualityTransaction.IsValidHistory(saved)) return null;
        using var admission = authority.Read(owner, saved, runtimeFingerprint);
        if (admission is null || admission.Binding != new CharacterCareerQualityExecutionBinding(
                owner.NormalizedValue, saved.Id.Value, saved.ContentRevision, saved.SavedRevision,
                runtimeFingerprint, admission.Binding.ContentDigest)
            || !CharacterCareerReputationTransaction.IsDigest(admission.Binding.ContentDigest)
            || admission.Candidates is null || admission.Candidates.Count > 16_384
            || admission.Candidates.Any(row => row is null || row.Binding != admission.Binding
                || !CharacterCareerQualityRules.TryCreateQuote(row, out _))
            || admission.Candidates.Select(row => (row.Operation, row.Identity)).Distinct().Count()
                != admission.Candidates.Count) return null;

        var allTransactions = saved.Document.AuxiliaryState.CharacterCareerQualityTransactions ?? [];
        var allCorrections = saved.Document.AuxiliaryState.CharacterCareerQualityCorrections ?? [];
        var transactions = new List<CharacterCareerQualityTransactionEntry>();
        var corrections = new List<CharacterCareerQualityCorrectionEntry>();
        var reserved = allTransactions.Select(row => row.Plan.TransactionId)
            .Concat(allCorrections.Select(row => row.Correction.CorrectionId)).ToHashSet();
        foreach (var entry in allTransactions)
        {
            // Foreign, imported or retired source/runtime histories can
            // suppress replay, never become a locally recoverable receipt.
            if (!saved.CanReplayReceipt(entry.Plan.TargetWorkspaceRevision)
                || !Belongs(entry.ObservedPostState.Binding, admission.Binding)) continue;
            if (entry.Plan.TargetWorkspaceRevision == saved.ContentRevision
                && !ObserveCurrent(saved, entry, entry.ObservedPostState, entry.ObservedExpense)) return null;
            var correction = allCorrections.SingleOrDefault(row =>
                row.Correction.OriginalTransactionId == entry.Plan.TransactionId);
            if (correction is not null)
            {
                if (!saved.CanReplayReceipt(correction.Correction.TargetWorkspaceRevision)
                    || !Belongs(correction.ObservedRestoredState.Binding, admission.Binding)) continue;
                if (correction.Correction.TargetWorkspaceRevision == saved.ContentRevision
                    && !ObserveCurrent(saved, entry, correction.ObservedRestoredState, correction.ObservedExpense)) return null;
                corrections.Add(correction);
                reserved.Remove(correction.Correction.CorrectionId);
            }
            transactions.Add(entry);
            reserved.Remove(entry.Plan.TransactionId);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!admission.IsCurrent()) return null;
        return new(admission.Binding, admission.Candidates, Array.AsReadOnly(transactions.ToArray()),
            Array.AsReadOnly(corrections.ToArray()), Array.AsReadOnly(reserved.Order().ToArray()));
    }

    public static CharacterCareerQualityCommitResult RecoverSavedReceipt(Chummer.Contracts.Owners.OwnerScope owner,
        WorkspaceStoredDocument saved, CharacterCareerQualityPlan plan, string runtimeFingerprint,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (saved.Id.Value != plan.WorkspaceId
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml
            || saved.Document.RulesetId != "sr5" || saved.Document.SchemaVersion != 1
            || saved.Document.PayloadKind is not ("workspace" or "sr5/chum5-xml")
            || saved.ContentRevision < plan.TargetWorkspaceRevision
            || saved.SavedRevision < plan.TargetSavedRevision
            || saved.ContentRevision != saved.SavedRevision
            || saved.LocalHistory is null || !saved.LocalHistory.IsValid(saved.ContentRevision)
            || !saved.CanReplayReceipt(plan.TargetWorkspaceRevision))
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_recovery_revision_or_custody_rejected");

        // Lookup checks the complete retained plan/ledger and observes exact
        // post-state at the target revision. Later history only suppresses a
        // write there; receipt recovery additionally proves its owned state below.
        var recovered = CharacterCareerQualityTransaction.Lookup(owner, saved, plan);
        if (recovered.Outcome != CharacterCareerQualityCommitOutcome.Replayed || recovered.Entry is null)
            return recovered with { Entry = null };
        if (saved.Document.AuxiliaryState.CharacterCareerQualityCorrections?.Any(row =>
                row.Correction.OriginalTransactionId == plan.TransactionId) == true)
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_recovery_already_corrected");
        bool laterRevision = saved.ContentRevision > plan.TargetWorkspaceRevision;
        if (laterRevision)
        {
            var sameSource = (saved.Document.AuxiliaryState.CharacterCareerQualityTransactions ?? [])
                .Where(row => row.Plan.Identity.SourceId == plan.Identity.SourceId).ToArray();
            var sameSourceIds = sameSource.Select(row => row.Plan.TransactionId).ToHashSet();
            if (sameSource.Any(row => row.Plan.TargetWorkspaceRevision > plan.TargetWorkspaceRevision)
                || saved.Document.AuxiliaryState.CharacterCareerQualityCorrections?.Any(row =>
                    row.Correction.TargetWorkspaceRevision > plan.TargetWorkspaceRevision
                    && sameSourceIds.Contains(row.Correction.OriginalTransactionId)) == true)
                return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_recovery_superseded");
        }

        // The fresh catalog binds rules, profile, enabled sources and
        // anatomy generation. The historical runtime is used only by
        // Lookup's receipt observation; it never authorizes a new quote.
        using var admission = authority.Read(owner, saved, runtimeFingerprint);
        if (admission is null || admission.Binding != new CharacterCareerQualityExecutionBinding(
                plan.OwnerId, plan.WorkspaceId, saved.ContentRevision, saved.SavedRevision,
                runtimeFingerprint, plan.ExpectedContentDigest)
            || admission.Candidates is null || admission.Candidates.Count > 16_384
            || admission.Candidates.Any(row => row is null || row.Binding != admission.Binding
                || !CharacterCareerQualityRules.TryCreateQuote(row, out _))
            || admission.Candidates.Select(row => (row.Operation, row.Identity)).Distinct().Count()
                != admission.Candidates.Count)
            return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_recovery_source_authority_changed");
        // Require an actually compiled current candidate from the exact
        // retained source bytes too. It need not be purchasable again.
        if (!admission.Candidates.Any(row => row.Identity.SourceId == plan.Identity.SourceId
                && row.RawSourceState == JsonSerializer.Serialize(recovered.Entry.Source)))
            return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_recovery_source_not_projected");
        if (laterRevision && !ObserveLaterReceipt(saved, recovered.Entry, admission.Binding))
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_recovery_later_effect_or_expense_changed");
        cancellationToken.ThrowIfCancellationRequested();
        if (!admission.IsCurrent())
            return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_recovery_source_authority_changed");
        return recovered;
    }

    private static bool ObserveLaterReceipt(WorkspaceStoredDocument saved, CharacterCareerQualityTransactionEntry entry,
        CharacterCareerQualityExecutionBinding currentBinding)
        // Recompile the real saved owner and every owned effect with the exact
        // retained source. Later unrelated Karma/revision changes are not the
        // original post-state; compare only its Quality projection and expense.
        // Return the original persisted entry unchanged, never a rebuilt receipt.
        => CharacterCareerQualityGraph.TryObserve(saved.Document.Content, entry.Source, entry.Plan.Identity,
            entry.ResolvedArmCount, currentBinding, entry.Quote.RuleDigest, entry.Plan.TransactionId,
            out var observed, out var expense)
            && observed is not null
            && observed.Identity == entry.ObservedPostState.Identity
            && JsonSerializer.Serialize(observed.Definition) == JsonSerializer.Serialize(entry.ObservedPostState.Definition)
            && observed.Extra == entry.ObservedPostState.Extra && observed.SourceName == entry.ObservedPostState.SourceName
            && JsonSerializer.Serialize(observed.Instances) == JsonSerializer.Serialize(entry.ObservedPostState.Instances)
            && observed.SourceRevision == entry.ObservedPostState.SourceRevision
            && observed.RuleDigest == entry.ObservedPostState.RuleDigest
            && JsonSerializer.Serialize(expense) == JsonSerializer.Serialize(entry.ObservedExpense);

    private static bool Belongs(CharacterCareerQualityExecutionBinding historical,
        CharacterCareerQualityExecutionBinding current)
        => historical.OwnerId == current.OwnerId && historical.WorkspaceId == current.WorkspaceId
            && historical.RuntimeFingerprint == current.RuntimeFingerprint && historical.ContentDigest == current.ContentDigest
            && historical.WorkspaceRevision <= current.WorkspaceRevision && historical.SavedRevision <= current.SavedRevision;

    private static bool ObserveCurrent(WorkspaceStoredDocument saved, CharacterCareerQualityTransactionEntry entry,
        CharacterCareerQualityStateObservation retainedState, CharacterCareerQualityExpenseObservation retainedExpense)
        => CharacterCareerQualityGraph.TryObserve(saved.Document.Content, entry.Source, entry.Plan.Identity,
            entry.ResolvedArmCount, retainedState.Binding, entry.Quote.RuleDigest, entry.Plan.TransactionId,
            out var observed, out var expense)
            && JsonSerializer.Serialize(observed) == JsonSerializer.Serialize(retainedState)
            && JsonSerializer.Serialize(expense) == JsonSerializer.Serialize(retainedExpense);
}
