using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private Lazy<OriginTrustDomain> _originTrust = null!;
    private OriginTrustDomain AuthenticatedOriginTrust => _originTrust.Value;

    private static string HistoryDigest(WorkspaceAuthenticatedTransition transition)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(transition)));

    private static WorkspaceHistoricalState HistoricalState(WorkspaceDocument document)
        => new(document.Format.ToString(), JsonSerializer.Serialize(document.PayloadEnvelope),
            JsonSerializer.Serialize(document.AuxiliaryState),
            CharacterCareerMartialAcquisitionTransaction.SourceDigest(document),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(document.AuxiliaryState),
            CharacterCareerMartialAcquisitionTransaction.Hash(document.Content));

    // Only the typed transaction calls this, after building and admitting the actual
    // after-capture. The signed observation stays inside the atomic workspace record;
    // neither proposed captures nor a temp file can be exported as a committed result.
    private PersistedWorkspaceRecord BindMartialTerminal(OwnerScope owner, WorkspaceStoredDocument saved,
        PersistedWorkspaceRecord before, PersistedWorkspaceRecord after,
        CharacterCareerMartialAcquisitionReceipt receipt)
    {
        if (before.RetainedRuleSources?.CurrentEnvironment is not { } current
            || after.RetainedRuleSources?.CurrentEnvironment is not { } next)
            return after with { AuthenticatedHistory = before.AuthenticatedHistory }; // Legacy local replay only.
        if (!TryMaterializeRecord(after, out var document, out long revision, out _, out bool migration) || migration)
            throw new IOException("martial-history-after-unavailable");
        var prior = before.AuthenticatedHistory?.Transitions ?? [];
        if (prior.Length != 0 && (prior[^1].Observation.After != HistoricalState(saved.Document)
            || prior[^1].Observation.AfterRevision != saved.ContentRevision
            || prior[^1].Observation.AfterSavedRevision != saved.SavedRevision))
            throw new IOException("martial-history-branch-changed");
        var observation = new WorkspaceHistoricalObservation(1, AuthenticatedOriginTrust.OriginId,
            owner.NormalizedValue, owner.IsLocalSingleUser, saved.Id.Value, saved.LocalHistory!.IncarnationId,
            prior.Length == 0 ? "" : HistoryDigest(prior[^1]), saved.ContentRevision, revision, saved.SavedRevision, after.SavedRevision!.Value,
            HistoricalState(saved.Document), HistoricalState(document), current.CaptureDigest,
            next.CaptureDigest, JsonSerializer.Serialize(receipt));
        return after with { AuthenticatedHistory = AppendHistory(prior, AuthenticatedOriginTrust.Sign(observation)) };
    }

    private static WorkspaceAuthenticatedHistory AppendHistory(WorkspaceAuthenticatedTransition[] before,
        WorkspaceAuthenticatedTransition next)
    {
        if (before.Length >= WorkspaceAuthenticatedHistory.MaximumTransitions)
            throw new IOException("workspace-history-full");
        var history = new WorkspaceAuthenticatedHistory([.. before, next]);
        if (JsonSerializer.SerializeToUtf8Bytes(history).Length > WorkspaceAuthenticatedHistory.MaximumBytes)
            throw new IOException("workspace-history-full");
        return history;
    }

    // Extend the established writer so every later document/auxiliary mutation keeps
    // exact ancestry. It cannot mint a martial terminal; that needs typed admission.
    private PersistedWorkspaceRecord PreserveAuthenticatedHistory(string path, PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition, PrivateClosureRestoreAdmission? closureAdmission,
        CharacterCareerMartialAcquisitionReceipt? admitted)
    {
        if (closureAdmission is not null) return record; // Imported proof is never re-signed/enrolled here.
        var before = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        if (admitted is not null)
        {
            if (MartialAcquisitionAdmissionFor(path) is null) throw new IOException("martial-history-admission-missing");
            return record;
        }
        var history = before?.AuthenticatedHistory;
        if (record.AuthenticatedHistory is not null && JsonSerializer.Serialize(record.AuthenticatedHistory) != JsonSerializer.Serialize(history))
            throw new IOException("workspace-history-replacement-rejected");
        record = record with { AuthenticatedHistory = history };
        if (history is null || history.Transitions.Length == 0) return record;
        if (!TryMaterializeRecord(before!, out var previous, out long beforeRevision, out long beforeSavedRevision, out bool beforeMigration)
            || !TryMaterializeRecord(record, out var next, out long afterRevision, out long afterSavedRevision, out bool afterMigration)
            || beforeMigration || afterMigration) throw new IOException("workspace-history-state-unavailable");
        var previousState = HistoricalState(previous);
        var nextState = HistoricalState(next);
        if (previousState == nextState && beforeRevision == afterRevision && beforeSavedRevision == afterSavedRevision) return record;
        var head = history.Transitions[^1].Observation;
        if (head.After != previousState || head.AfterRevision != beforeRevision || head.AfterSavedRevision != beforeSavedRevision
            || afterRevision < beforeRevision || afterRevision - beforeRevision > 1)
            throw new IOException("workspace-history-branch-changed");
        // Existing private custody, not an imported claim, supplies the append's owner.
        var manifest = before!.RetainedRuleSources ?? throw new IOException("workspace-history-owner-missing");
        var owner = manifest.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
            ? OwnerScope.LocalSingleUser : new OwnerScope(manifest.OwnerId);
        if (!PathComparer.Equals(TryGetPath(owner, new CharacterWorkspaceId(manifest.WorkspaceId)), path))
            throw new IOException("workspace-history-owner-path-changed");
        if (owner.NormalizedValue != manifest.OwnerId || head.OwnerId != owner.NormalizedValue
            || head.LocalSingleUser != owner.IsLocalSingleUser) throw new IOException("workspace-history-owner-changed");
        var observation = new WorkspaceHistoricalObservation(1, AuthenticatedOriginTrust.OriginId,
            owner.NormalizedValue, owner.IsLocalSingleUser, manifest.WorkspaceId, before.LocalHistory!.IncarnationId,
            HistoryDigest(history.Transitions[^1]), beforeRevision, afterRevision, beforeSavedRevision, afterSavedRevision, previousState, nextState,
            manifest.CurrentEnvironment?.CaptureDigest, record.RetainedRuleSources?.CurrentEnvironment?.CaptureDigest, null);
        return record with { AuthenticatedHistory = AppendHistory(history.Transitions, AuthenticatedOriginTrust.Sign(observation)) };
    }

    private bool VerifyAuthenticatedHistory(OwnerScope owner, CharacterWorkspaceId id,
        WorkspaceDocument document, long revision, long savedRevision, WorkspaceAuthenticatedHistory? history,
        Func<string, CharacterCreationRuleSourceCapture?> capture,
        CharacterCareerMartialAcquisitionReceipt? requested = null)
    {
        if (history?.Transitions is not { Length: > 0 } transitions
            || transitions.Length > WorkspaceAuthenticatedHistory.MaximumTransitions) return false;
        bool found = requested is null;
        WorkspaceHistoricalObservation? previous = null;
        string previousDigest = "";
        foreach (var transition in transitions)
        {
            if (transition?.Observation is not { } observation || observation.Before is null || observation.After is null
                || observation.WorkspaceId != id.Value || observation.PreviousDigest != previousDigest
                || observation.BeforeRevision < 1 || observation.AfterRevision < observation.BeforeRevision
                || observation.AfterRevision - observation.BeforeRevision > 1 && !IsUnchangedRuleResolutionCheckpoint(observation)
                || observation.BeforeSavedRevision < 0 || observation.BeforeSavedRevision > observation.BeforeRevision
                || observation.AfterSavedRevision < 0 || observation.AfterSavedRevision > observation.AfterRevision
                || previous is not null && (observation.Before != previous.After || observation.BeforeRevision != previous.AfterRevision
                    || observation.BeforeSavedRevision != previous.AfterSavedRevision)
                || !AuthenticatedOriginTrust.Verify(owner, transition)) return false;
            if (observation.MartialReceiptJson is { } json)
            {
                var receipt = JsonSerializer.Deserialize<CharacterCareerMartialAcquisitionReceipt>(json);
                if (receipt is null || JsonSerializer.Serialize(receipt) != json
                    || !CharacterCareerMartialAcquisitionTransaction.IsCoherent(receipt)
                    || receipt.Command.Binding.OwnerId != owner.NormalizedValue
                    || receipt.Command.Binding.LocalSingleUser != owner.IsLocalSingleUser
                    || receipt.Command.Binding.WorkspaceId != id
                    || receipt.Command.Binding.LocalHistoryIncarnationId != observation.IncarnationId
                    || receipt.CommittedWorkspaceRevision != observation.AfterRevision
                    || observation.AfterRevision != observation.BeforeRevision + 1
                    || observation.AfterSavedRevision != observation.AfterRevision
                    || !receipt.NeverApplied && (receipt.Command.Binding.WorkspaceRevision != observation.BeforeRevision
                        || receipt.Command.Binding.SourceDigest != observation.Before.SourceDigest
                        || receipt.Command.Binding.AuxiliaryStateDigest != observation.Before.AuxiliaryDigest)
                    || receipt.CharacterPayloadDigestAfter != observation.After.CharacterDigest
                    || !MatchesCapture(observation.BeforeCaptureDigest, observation.Before, capture)
                    || !MatchesCapture(observation.AfterCaptureDigest, observation.After, capture)) return false;
                if (requested is not null && receipt.Command.Request.OperationId == requested.Command.Request.OperationId)
                {
                    if (receipt.NeverApplied || json != JsonSerializer.Serialize(requested)) return false;
                    found = true;
                }
            }
            previous = observation;
            previousDigest = HistoryDigest(transition);
        }
        return found && previous!.After == HistoricalState(document) && previous.AfterRevision == revision
            && previous.AfterSavedRevision == savedRevision;
    }

    // An explicitly reviewed saved branch may become a new local revision above
    // a diverged target. Its signed checkpoint changes neither character nor
    // auxiliary history. Ordinary edits still advance by at most one revision.
    private static bool IsUnchangedRuleResolutionCheckpoint(WorkspaceHistoricalObservation observation)
        => observation.MartialReceiptJson is null && observation.Before == observation.After
            && observation.BeforeSavedRevision == observation.BeforeRevision
            && observation.AfterSavedRevision == observation.AfterRevision
            && observation.AfterRevision > observation.BeforeRevision
            && IsRetainedCaptureDigest(observation.BeforeCaptureDigest ?? "")
            && IsRetainedCaptureDigest(observation.AfterCaptureDigest ?? "");

    internal WorkspaceAuthenticatedHistory? PrepareRuleResolutionHistory(
        WorkspacePrivateRuleSourceClosure.Decoded chosen, WorkspaceContinuationSnapshot resolved,
        WorkspaceCurrentRuleEnvironmentBinding environment, WorkspaceAuthenticatedHistory? prepared,
        bool confirming)
    {
        if (chosen.AuthenticatedHistory is not { } history)
        {
            if (prepared is not null) throw RetainedRuleSourceRejected();
            return null;
        }
        var source = chosen.Snapshot.Workspace;
        var target = resolved.Workspace;
        var owner = chosen.Snapshot.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
            ? OwnerScope.LocalSingleUser : new OwnerScope(chosen.Snapshot.OwnerId);
        if (resolved.OwnerId != chosen.Snapshot.OwnerId || source.Id != target.Id
            || source.ContentRevision != source.SavedRevision || target.ContentRevision <= source.ContentRevision
            || target.ContentRevision != target.SavedRevision
            || HistoricalState(source.Document) != HistoricalState(target.Document)
            || !VerifyAuthenticatedHistory(owner, source.Id, source.Document, source.ContentRevision,
                source.SavedRevision, history, digest => chosen.Captures.GetValueOrDefault(digest)))
            throw RetainedRuleSourceRejected();
        var head = history.Transitions[^1].Observation;
        var state = HistoricalState(source.Document);
        var observation = new WorkspaceHistoricalObservation(1, AuthenticatedOriginTrust.OriginId,
            owner.NormalizedValue, owner.IsLocalSingleUser, source.Id.Value, head.IncarnationId,
            HistoryDigest(history.Transitions[^1]), source.ContentRevision, target.ContentRevision,
            source.SavedRevision, target.SavedRevision, state, state,
            head.AfterCaptureDigest, environment.CaptureDigest, null);
        if (!IsUnchangedRuleResolutionCheckpoint(observation)) throw RetainedRuleSourceRejected();
        if (!confirming) return AppendHistory(history.Transitions, AuthenticatedOriginTrust.Sign(observation));
        // ECDSA signatures are not deterministic. Confirmation verifies and reuses
        // the exact privately issued review tail; it never re-signs imported proof.
        if (prepared?.Transitions is not { } transitions || transitions.Length != history.Transitions.Length + 1
            || transitions[^1].Observation != observation || !AuthenticatedOriginTrust.Verify(owner, transitions[^1])
            || !JsonSerializer.SerializeToUtf8Bytes(transitions[..^1]).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(history.Transitions)))
            throw RetainedRuleSourceRejected();
        return prepared;
    }

    private static bool MatchesCapture(string? digest, WorkspaceHistoricalState state,
        Func<string, CharacterCreationRuleSourceCapture?> recover)
    {
        if (digest is null || recover(digest) is not { } capture || capture.IntegrityDigest != digest) return false;
        var inputs = FileSystemCharacterSourceDataResolver.ImportCaptureInputs(capture.ExportPrivateInputs(), digest);
        return CharacterCareerMartialAcquisitionTransaction.Hash(inputs.CharacterXml) == state.CharacterDigest;
    }
}
