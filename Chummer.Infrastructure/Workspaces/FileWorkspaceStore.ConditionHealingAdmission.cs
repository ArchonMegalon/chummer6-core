using Chummer.Application.Characters;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // 5461/Root hook: call only inside the existing source-validated natural-recovery write lease,
    // after WithNaturalRecoveryCondition has produced the exact terminal document. This is not a public grant.
    private IDisposable AdmitNaturalRecoveryCondition5460(string path, WorkspaceStoredDocument before,
        WorkspaceDocument after, Sr5RecoveryOperation operation)
    {
        var previous = before.Document.AuxiliaryState.Sr5NaturalRecovery?.Operations ?? [];
        if (operation.Command.Request.Action != Sr5RecoveryAction.Complete || operation.Activity.Phase != Sr5RecoveryPhase.Completed
            || operation.Command.WorkspaceRevision != before.ContentRevision || operation.RecordedRevision != before.ContentRevision + 1
            || operation.Command.AuxiliaryDigest != before.Document.AuxiliaryStateDigest
            || operation.PayloadDigest != CharacterSr5NaturalRecoveryTransaction.Hash(after.Content)
            || !CharacterSr5NaturalRecoveryTransaction.IsValidLedger(before.Id, operation.RecordedRevision, after.AuxiliaryState.Sr5NaturalRecovery)
            || !CharacterCareerGearEquipmentTransaction.Same(after.AuxiliaryState.Sr5NaturalRecovery?.Operations, previous.Append(operation).ToArray())
            || !CharacterCareerGearEquipmentTransaction.Same(after.AuxiliaryState with { Sr5NaturalRecovery = before.Document.AuxiliaryState.Sr5NaturalRecovery,
                SessionCondition = before.Document.AuxiliaryState.SessionCondition }, before.Document.AuxiliaryState)
            || !CharacterCareerGearEquipmentTransaction.Same(after, SessionConditionTransferRules.WithNaturalRecoveryCondition(before.Document, after, operation)))
            throw new IOException("condition_healing_requires_original_completion");
        return new ConditionHealingAdmission5460(this, path, before, after, operation.RecordedRevision);
    }
    [ThreadStatic] private static ConditionHealingAdmission5460? _conditionHealingAdmission5460;
    private sealed class ConditionHealingAdmission5460 : IDisposable
    {
        private readonly ConditionHealingAdmission5460? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        private readonly string _beforeDigest;
        private readonly WorkspaceDocument _after;
        private readonly long _revision;
        internal ConditionHealingAdmission5460(FileWorkspaceStore store, string path, WorkspaceStoredDocument before, WorkspaceDocument after, long revision)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); _after = after; _revision = revision;
            var record = store.ReadPrivateRecord(path);
            if (record.ContentRevision != before.ContentRevision || record.LocalHistory?.IncarnationId != before.LocalHistory?.IncarnationId)
                throw new IOException("condition_healing_preimage_changed");
            _beforeDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _conditionHealingAdmission5460; _conditionHealingAdmission5460 = this;
        }
        internal void Validate(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(Store.ReadPrivateRecord(Path)) != _beforeDigest
                || record.ContentRevision != _revision || record.SavedRevision != _revision
                || !CharacterCareerGearEquipmentTransaction.Same(record.AuxiliaryState, _after.AuxiliaryState)
                || record.Envelope?.Payload != _after.Content) throw new IOException("condition_healing_record_changed");
        }
        public void Dispose() => _conditionHealingAdmission5460 = _previous;
    }
    private ConditionHealingAdmission5460? ConditionHealingAdmissionFor5460(string path)
        => _conditionHealingAdmission5460 is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;
}
