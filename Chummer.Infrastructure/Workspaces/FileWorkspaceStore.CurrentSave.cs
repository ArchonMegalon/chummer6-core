using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Sr5;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : IWorkspaceCurrentSaveStoreCapability
{
    public CommandResult<WorkspaceSaveReceipt> SaveCurrent(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, long expectedContentRevision, IRulesetWorkspaceCodecResolver unboundCodecs,
        Func<WorkspaceStoredDocument, Chummer.Contracts.Api.DataExportBundle, WorkspaceSaveReceipt> buildReceipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buildReceipt);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!IsCurrentSaveOwner(owners, expectedOwner, ownerLease)) return CurrentSaveRefused();
            var owner = expectedOwner.Owner;
            if (TryGetPath(owner, id) is not { } path || !TrySecureExistingWorkspaceDirectory(owner))
                return CurrentSaveRefused(WorkspaceOperationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            if (File.Exists(path) && ReadPrivateRecord(path).RetainedRuleSources is null)
                RemoveStaleTempFiles(path);
            // This existing mutation admission holds the exact owner, record, slot and
            // retained capture. It refuses custody-only records and never retries ambient
            // reads after an active environment fails admission.
            var read = ReadGearWorkspaceUnderLease(owner, id, path, owners, ownerLease, expectedOwner,
                out var ledger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
                return CurrentSaveRefused(read.Outcome);
            if (expectedContentRevision <= 0 || saved.ContentRevision != expectedContentRevision)
                return CurrentSaveRefused(WorkspaceOperationOutcome.Conflict);

            string before = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            string? slot = ReadContinuationSlotUnderLease(path);
            var envelope = saved.Document.PayloadEnvelope;
            IRulesetWorkspaceCodec codec;
            if (current is null)
            {
                codec = unboundCodecs.Resolve(envelope.RulesetId);
                envelope = envelope with
                {
                    SchemaVersion = envelope.SchemaVersion > 0 ? envelope.SchemaVersion : codec.SchemaVersion,
                    PayloadKind = string.IsNullOrWhiteSpace(envelope.PayloadKind) ? codec.PayloadKind : envelope.PayloadKind
                };
            }
            else
            {
                if (saved.Document.Format != WorkspaceDocumentFormat.NativeXml || envelope.RulesetId != "sr5"
                    || envelope.SchemaVersion != 1 || envelope.PayloadKind != "sr5/chum5-xml")
                    return CurrentSaveRefused(WorkspaceOperationOutcome.Corrupt);
                // The canonical receipt codec sees only this operation's retained rules.
                codec = new Sr5WorkspaceCodec(_retainedCharacterQueries!,
                    new XmlCharacterSectionQueries(new CharacterSectionService(current.Capture.CreateReplayResolver())),
                    new XmlCharacterMetadataCommands(new CharacterFileService()));
            }
            var receipt = buildReceipt(saved, codec.BuildExportBundle(envelope));
            if (receipt is null || receipt.Id != id || receipt.ContentRevision != saved.ContentRevision
                || receipt.SavedRevision != saved.ContentRevision)
                return CurrentSaveRefused();

            void ValidateOriginal()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentSaveOwner(owners, expectedOwner, ownerLease)
                    || File.GetLastWriteTimeUtc(path) != saved.LastUpdatedUtc.UtcDateTime
                    || ReadContinuationSlotUnderLease(path) != slot
                    || CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != before)
                    throw new IOException("Workspace changed before its checkpoint could be saved.");
                current?.ValidateFinalFence();
            }

            ValidateOriginal();
            if (saved.SavedRevision != saved.ContentRevision)
            {
                // No supplied document, content revision, package selection, receipt or
                // history can be substituted. The shared writer retains the manifest and
                // revalidates the proposed saved-revision binding before atomic replacement.
                var record = BuildPersistedRecord(saved.Document, saved.ContentRevision, saved.ContentRevision,
                    saved.LocalHistory!, ledger, saved.DelegatedGmHistorySegmentStarts);
                WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                    beforeTargetReplace: ValidateOriginal);
            }
            // No cancellation check after publication: preserve the joined commit result.
            return new(true, receipt, null, WorkspaceOperationOutcome.Success);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (IsRetainedRuleSourceFailure(error))
        {
            return CurrentSaveRefused();
        }
    }

    private static bool IsCurrentSaveOwner(IOwnerContextAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease ownerLease)
        => owners is IOwnerContextLeaseAccessor authority && expected.IsValid
           && !(expected.Owner.UsesLocalSingleUserValue && !expected.Owner.IsLocalSingleUser)
           && ownerLease is not null && ownerLease.Stamp == expected && authority.Capture() == expected;

    private static CommandResult<WorkspaceSaveReceipt> CurrentSaveRefused(
        WorkspaceOperationOutcome outcome = WorkspaceOperationOutcome.Unavailable)
        => new(false, null, "Workspace checkpoint is unavailable for the current owner, revision and rule environment.",
            outcome == WorkspaceOperationOutcome.Success ? WorkspaceOperationOutcome.Unavailable : outcome);
}
