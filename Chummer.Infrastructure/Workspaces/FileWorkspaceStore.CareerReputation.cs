using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // This local mechanics path uses these three loaded owner modules. Binding
    // their actual MVIDs is not a package/publication claim or a substitute for
    // the separate captured profile/effect-graph inputs. A new build invalidates
    // uncommitted previews; historical receipt lookup remains available.
    public string CareerReputationRuntimeDigest => CharacterCareerReputationTransaction.Hash(
        "chummer.core.sr5-reputation-runtime/v1\0"
        + typeof(CharacterCareerReputationRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerReputationProjector).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerReputationResult CommitCareerReputation(
        CharacterCareerReputationCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(sourceResolver);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCareerReputationTransaction.IsConfirmedCommand(command))
            return new(CharacterCareerReputationOutcome.Conflict, Error: "reputation_command_invalid");
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(OwnerScope.LocalSingleUser, id);
        if (path is null) return new(CharacterCareerReputationOutcome.Missing);
        string digest = CharacterCareerReputationTransaction.CommandDigest(command);
        CharacterCareerReputationResult? result = null;
        try
        {
            // The same cross-process/path lease as all generic, creation,
            // reward and delegated edits. No separate check-then-write lock.
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(OwnerScope.LocalSingleUser, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return WorkspaceCharacterCareerReputationService.MapReadFailure(read);
            var prior = CharacterCareerReputationTransaction.Lookup(saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerReputationOutcome.NotFound) return prior;
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerReputationOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "reputation_revision_conflict");
            var captured = new ReputationSourceCapture(sourceResolver);
            if (!CharacterCareerReputationProjector.TryRead(saved, captured, out var snapshot, out string error))
                return new(CharacterCareerReputationOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerReputationTransaction.TryBuild(saved, snapshot!, command, CareerReputationRuntimeDigest,
                    out var replacement, out var receipt))
                return new(CharacterCareerReputationOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "reputation_preview_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState))
                return new(CharacterCareerReputationOutcome.Corrupt, Error: "reputation_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision,
                receipt.CommittedWorkspaceRevision, delegatedLedger);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!captured.IsCurrent(snapshot!.RuleStateDigest))
                        throw new IOException("reputation_source_changed_before_replace");
                });
            result = new(CharacterCareerReputationOutcome.Applied, receipt, receipt.CommittedWorkspaceRevision);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or InvalidOperationException or OperationCanceledException)
        {
            // Do not observe cancellation while recovering a potentially durable
            // result. The same operation identity remains safe to retry.
        }

        // The lease is released before ordinary Get reacquires it. A successful
        // adapter return cannot stand in for reading the actual durable receipt.
        var observed = Get(id);
        if (!observed.Success || observed.Value is not { } current)
            return WorkspaceCharacterCareerReputationService.MapReadFailure(observed);
        var recovered = CharacterCareerReputationTransaction.Lookup(current, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerReputationOutcome.Replayed && result is not null)
            return recovered with { Outcome = CharacterCareerReputationOutcome.Applied };
        return recovered.Outcome == CharacterCareerReputationOutcome.NotFound
            ? new(CharacterCareerReputationOutcome.Unavailable, CurrentWorkspaceRevision: current.ContentRevision,
                Error: "reputation_commit_not_observed")
            : recovered;
    }

    private sealed class ReputationSourceCapture(ICharacterSourceDataResolver resolver) : ICharacterSourceDataResolver
    {
        private ICharacterSourceDataContext? _context;
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => _context = resolver.TryCreateContext(characterXml);

        public bool IsCurrent(string expected)
            => _context is not null
                && _context.TryResolveCareerReputationSettings(out var settings, out string rawRuleState)
                && settings is not null && !string.IsNullOrEmpty(rawRuleState)
                && CharacterCareerReputationTransaction.Hash("chummer.core.sr5-reputation-rules/v1\0"
                    + JsonSerializer.Serialize(new { settings, rawRuleState })) == expected;
    }
}
