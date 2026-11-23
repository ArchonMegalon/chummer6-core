using System.Security.Cryptography;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Tx = Chummer.Application.Characters.CharacterSr5NaturalRecoveryTransaction;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterSr5NaturalRecoveryStore
{
    // Test-only deterministic entropy at the existing production store boundary.
    // The public service and native request never accept dice or hit counts.
    private readonly Func<int>? _naturalRecoveryTestDie;
    internal FileWorkspaceStore(string directory, IFileWorkspaceStoreFaultInjector faults, Func<int> naturalRecoveryTestDie)
        : this(directory, faults)
    { _naturalRecoveryTestDie = naturalRecoveryTestDie ?? throw new ArgumentNullException(nameof(naturalRecoveryTestDie)); }
    // Root must install the shared generic-transition guard before implementing
    // this hook. Uncomposed source cannot advertise mutation support.
    static partial void VerifyNaturalRecoveryGuards(ref bool installed);
    public bool SupportsNaturalRecovery { get { bool installed = false; VerifyNaturalRecoveryGuards(ref installed); return installed; } }
    public string NaturalRecoveryRuntimeDigest => Tx.Digest(new
    {
        Schema = "sr5-natural-recovery/v1", Rules = typeof(CharacterSr5DowntimeHealingRules).Module.ModuleVersionId,
        Transaction = typeof(Tx).Module.ModuleVersionId, Store = typeof(FileWorkspaceStore).Module.ModuleVersionId
    });

    public Sr5RecoveryResult ExecuteNaturalRecovery(OwnerScope owner, Sr5RecoveryCommand command,
        ICharacterSourceDataResolver sources, CancellationToken token)
    {
        if (!SupportsNaturalRecovery || !Tx.ValidOwner(owner) || !Tx.ValidCommand(command) || command.OwnerId != owner.NormalizedValue)
            return new(Sr5RecoveryOutcome.Conflict);
        var id = command.Request.WorkspaceId; string? path = TryGetPath(owner, id);
        if (path is null) return new(Sr5RecoveryOutcome.Unavailable);
        bool written = false;
        try
        {
            using WorkspaceOperationLease lease = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return new(Sr5RecoveryOutcome.Unavailable);
            var prior = Tx.Lookup(owner, saved, command.Request.OperationId, Tx.Digest(command));
            if (prior.Outcome != Sr5RecoveryOutcome.NotFound) return prior;
            token.ThrowIfCancellationRequested();
            // Use a fresh source scope for each readback. A cached operation scope
            // must not hide a source change between the review and target replace.
            Sr5RecoveryReview CurrentReview()
            {
                using var scope = (sources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                return Tx.Review(owner, saved, command.Request, scope ?? sources, NaturalRecoveryRuntimeDigest);
            }
            var review = CurrentReview();
            if (command != review.Command with { ExplicitlyConfirmed = true }) return new(Sr5RecoveryOutcome.Conflict);
            var next = Tx.Apply(saved, review, command);
            void Write(Chummer.Contracts.Workspaces.WorkspaceDocument document, Sr5RecoveryOperation operation)
            {
                if (!IsValidAuxiliaryState(id, operation.RecordedRevision, document.AuxiliaryState))
                    throw new InvalidDataException("Recovery history is invalid.");
                // Reservation/start/roll are durable pending-transaction metadata:
                // character and committed calendar revisions stay unchanged until
                // completion/cancellation. Auxiliary digest is the CAS at every step.
                // All metadata and the terminal character/calendar result use the
                // same leased, fsynced replacement; no second character save exists.
                var record = BuildPersistedRecord(document, operation.RecordedRevision, operation.RecordedRevision,
                    saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
                WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
                {
                    token.ThrowIfCancellationRequested();
                    if (CurrentReview().Command != review.Command) throw new IOException("Recovery authority changed before replace.");
                });
            }
            Write(next.Document, next.Operation);
            if (command.Request.Action == Sr5RecoveryAction.Roll)
            {
                // No retry after this durable fence, including faults, cancellation,
                // source drift and crashes. Same command returns the saved fence.
                token.ThrowIfCancellationRequested();
                int[] dice = Enumerable.Range(0, next.Operation.Activity.Quote.DicePool)
                    .Select(_ => _naturalRecoveryTestDie?.Invoke() ?? RandomNumberGenerator.GetInt32(1, 7)).ToArray();
                var rolled = Tx.FinishRoll(next.Document, next.Operation, dice);
                Write(rolled.Document, rolled.Operation);
            }
            written = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException or System.Xml.XmlException or OperationCanceledException)
        { /* Read the original result after releasing the lease. Never replay. */ }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is null) return new(Sr5RecoveryOutcome.Unknown);
        var result = Tx.Lookup(owner, observed.Value, command.Request.OperationId, Tx.Digest(command));
        return result.Outcome == Sr5RecoveryOutcome.Recovered && written ? result with { Outcome = Sr5RecoveryOutcome.Applied } : result;
    }
}
