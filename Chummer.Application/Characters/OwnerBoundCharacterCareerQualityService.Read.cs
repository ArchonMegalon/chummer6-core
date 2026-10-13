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
                var read = ReadStored();
                if (!read.Success || read.Value is not { } saved || saved.Id != workspace
                    || saved.Document.Format != WorkspaceDocumentFormat.NativeXml
                    || saved.Document.RulesetId != "sr5" || saved.ContentRevision != saved.SavedRevision
                    || !CharacterCareerQualityTransaction.IsValidHistory(saved)) return null;
                using var admission = authority.Read(expectedOwner.Owner, saved, atomic.CareerQualityRuntimeFingerprint);
                if (admission is null || admission.Binding != new CharacterCareerQualityExecutionBinding(
                        expectedOwner.Owner.NormalizedValue, workspace.Value, saved.ContentRevision, saved.SavedRevision,
                        atomic.CareerQualityRuntimeFingerprint, admission.Binding.ContentDigest)
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
                var after = ReadStored();
                if (!after.Success || JsonSerializer.Serialize(after.Value) != JsonSerializer.Serialize(saved)) return null;
                return new(admission.Binding, admission.Candidates, Array.AsReadOnly(transactions.ToArray()),
                    Array.AsReadOnly(corrections.ToArray()), Array.AsReadOnly(reserved.Order().ToArray()));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or OperationCanceledException) { return null; }
        }

        WorkspaceStoreReadResult ReadStored() => expectedOwner.Owner.IsLocalSingleUser
            ? store.Get(workspace) : store.Get(expectedOwner.Owner, workspace);
    }

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
