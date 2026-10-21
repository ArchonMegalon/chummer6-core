using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : IReservedCharacterCreationAtomicCreateCapability
{
    private const string CreationMarkerSchema = "chummer.local-creation-reservation/v2";
    private sealed record CreationMarker(string Schema, string State, string ReservationDigest,
        string? SlotGeneration, string RecordDigest);

    public WorkspaceStoreMutationResult CreateReservedCharacterCreation(OwnerScope owner,
        WorkspaceDocument document, CharacterCreationReservation reservation, Action beforeCommit)
    {
        ArgumentNullException.ThrowIfNull(beforeCommit);
        if (IsInvalidScopedOwner(owner) || !CharacterCreationReservation.IsValid(reservation)
            || reservation.OwnerId != owner.NormalizedValue
            || !CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(reservation.WorkspaceId, document))
            return UnavailableMutation("Creation reservation is unavailable.");
        string? path = TryGetPath(owner, reservation.WorkspaceId);
        if (path is null) return UnavailableMutation("Creation reservation is unavailable.");
        try
        {
            beforeCommit();
            EnsureWorkspaceDirectory(owner);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            ThrowIfLinkOrReparsePoint(path, "workspace target");
            string markerPath = path + ".creation-reservation";
            ThrowIfLinkOrReparsePoint(markerPath, "creation reservation");
            beforeCommit();
            var history = new WorkspaceLocalHistory(reservation.IncarnationId, 0, null)
                { CreationReservation = reservation };
            var created = BuildPersistedRecord(document, InitialContentRevision, InitialSavedRevision, history);
            string recordDigest = CharacterCreationFinalizationDigest.Compute(created);
            CreationMarker? marker = null;
            if (File.Exists(markerPath))
            {
                marker = ReadCreationMarker(path, reservation); // null means the exact legacy consumed marker
                if (marker is not null && (marker.RecordDigest != recordDigest
                    || marker.SlotGeneration != ReadContinuationSlotUnderLease(path)))
                    return UnavailableMutation("Creation reservation target changed.");
                if (File.Exists(path))
                {
                    var record = ReadLocalIdentityRecord(path);
                    var observed = ReadWorkspaceUnderLease(owner, reservation.WorkspaceId, path, out _);
                    if (!observed.Success || record.LocalHistory?.CreationReservation != reservation
                        || record.LocalHistory.IncarnationId != reservation.IncarnationId
                        || record.LocalHistory.ImportedThroughRevision != 0 || record.LocalHistory.ImportedSnapshotDigest is not null
                        || record.LocalHistory.LastRestore is not null || record.LocalHistory.LocalAdoption is not null
                        || observed.Value!.Document.AuxiliaryState.CharacterCreationBootstrapBinding?.BindingDigest
                            != document.AuxiliaryState.CharacterCreationBootstrapBinding?.BindingDigest)
                        return UnavailableMutation("Creation reservation no longer matches.");
                    beforeCommit();
                    if (marker is { State: "prepared" })
                        WriteCreationMarker(path, marker with { State = "committed" }, replace: true);
                    return SuccessfulMutation(reservation.WorkspaceId, observed.Value.LastUpdatedUtc,
                        InitialContentRevision, InitialSavedRevision);
                }
                // Missing committed (including legacy) is deleted/lost, never permission to recreate.
                if (marker is not { State: "prepared" })
                    return UnavailableMutation("Committed creation reservation has no workspace.");
            }
            else
            {
                if (File.Exists(path))
                    return new(WorkspaceOperationOutcome.Conflict, Error: "Workspace already exists.");
                // A prior slot generation proves use/deletion/restore/adoption, even without a marker.
                if (ReadContinuationSlotUnderLease(path) is not null)
                    return UnavailableMutation("Creation target was previously used.");
                marker = new(CreationMarkerSchema, "prepared", reservation.ReservationDigest, null, recordDigest);
                beforeCommit();
                WriteCreationMarker(path, marker, replace: false);
            }

            // The prepared marker binds the original missing slot. Do NOT rotate it here:
            // doing so before/after prepare creates another unrecoverable two-file window.
            // Existing generic create/restore/adoption/delete rotate it before mutation.
            // In particular deletion while this marker is still prepared invalidates recovery.
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterCreationReservationPrepared, path, markerPath);
            RemoveStaleTempFiles(path);
            var committed = WriteRecordAtomically(path, created, WorkspaceWriteDisposition.CreateNew,
                beforeTargetReplace: beforeCommit);
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterCreationRecordCommitted, path, markerPath);
            WriteCreationMarker(path, marker with { State = "committed" }, replace: true);
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterCreationReservationCommitted, path, markerPath);
            return SuccessfulMutation(reservation.WorkspaceId, committed, InitialContentRevision, InitialSavedRevision);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or InvalidOperationException)
        { return UnavailableMutation("Creation outcome is unknown; reconcile the same reservation."); }
    }

    // Called only under the existing workspace lease; observation does not advance journal state.
    private bool HasCreationReservationMarker(string path, CharacterCreationReservation reservation)
    {
        var marker = ReadCreationMarker(path, reservation);
        return marker is null || marker.SlotGeneration == ReadContinuationSlotUnderLease(path);
    }

    private static CreationMarker? ReadCreationMarker(string path, CharacterCreationReservation reservation)
    {
        string markerPath = path + ".creation-reservation";
        ThrowIfLinkOrReparsePoint(markerPath, "creation reservation");
        using var stream = new FileStream(markerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 71)
        {
            Span<byte> bytes = stackalloc byte[71];
            stream.ReadExactly(bytes);
            if (Encoding.UTF8.GetString(bytes) != reservation.ReservationDigest) throw new InvalidDataException();
            return null; // legacy consumed; never infer a prepared state
        }
        if (stream.Length is <= 0 or > 4096) throw new InvalidDataException();
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
        string[] fields = ["Schema", "State", "ReservationDigest", "SlotGeneration", "RecordDigest"];
        if (json.RootElement.ValueKind != JsonValueKind.Object
            || !json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(fields.OrderBy(n => n, StringComparer.Ordinal))) throw new InvalidDataException();
        var marker = json.RootElement.Deserialize<CreationMarker>();
        if (marker is null || marker.Schema != CreationMarkerSchema || marker.State is not ("prepared" or "committed")
            || marker.ReservationDigest != reservation.ReservationDigest
            || marker.SlotGeneration is not null && !CharacterCreationReservation.Id(marker.SlotGeneration)
            || !CharacterCreationFinalizationDigest.IsCanonical(marker.RecordDigest)) throw new InvalidDataException();
        return marker;
    }

    private static void WriteCreationMarker(string path, CreationMarker marker, bool replace)
    {
        string target = path + ".creation-reservation";
        string temporary = target + TempFileMarker + Guid.NewGuid().ToString("N");
        // Same secure temp/flush/rename discipline as the workspace and slot writers.
        // Only admitted writes clean stale temps; reads and mismatch refusals leave bytes alone.
        ThrowIfLinkOrReparsePoint(target, "creation reservation");
        RemoveStaleTempFiles(target);
        bool moved = false;
        try
        {
            using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, marker);
                stream.Flush(flushToDisk: true);
            }
            ThrowIfLinkOrReparsePoint(target, "creation reservation");
            if (replace) File.Replace(temporary, target, destinationBackupFileName: null);
            else File.Move(temporary, target, overwrite: false);
            moved = true;
        }
        finally
        {
            if (!moved) DeleteRegularFileIfPresent(temporary, "creation reservation temporary file");
        }
        // As for WriteRecordAtomically/RotateContinuationSlotUnderLease, portable Core
        // cannot acknowledge directory fsync. Whole-store rollback/power loss is not proven.
    }
}
