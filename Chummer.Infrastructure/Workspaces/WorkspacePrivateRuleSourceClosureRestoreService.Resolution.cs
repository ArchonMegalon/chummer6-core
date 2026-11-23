using System.Security.Cryptography;
using Chummer.Application.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

// A local operation, never an edited imported carrier. The original chosen bytes
// remain the review input and are durably preserved beside the displaced branch.
// Only the existing owner-bound restore service can issue this preparation.
internal sealed class PrivateRuleResolution : IDisposable
{
    private readonly byte[] _bytes;
    internal WorkspacePrivateRuleSourceClosureBinding Binding { get; }
    private readonly ICharacterFileQueries _queries;
    internal WorkspaceAuthenticatedHistory? AuthenticatedHistory { get; }
    internal DateTimeOffset CreatedAtUtc { get; }
    internal int Length => _bytes.Length;
    private PrivateRuleResolution(WorkspacePrivateRuleSourceClosure closure, DateTimeOffset created, ICharacterFileQueries queries, WorkspaceAuthenticatedHistory? history)
        => (_bytes, Binding, CreatedAtUtc, _queries, AuthenticatedHistory)
            = (closure.ExportPrivateBytes(), closure.Binding, created, queries, history);

    internal WorkspaceContinuationExport Export()
        => WorkspacePrivateRuleSourceClosure.DecodeForRestore(_bytes, Binding, _queries, CancellationToken.None) is { } decoded
            ? new(decoded.Snapshot, decoded.Binding.ContinuationDigest) : throw WorkspacePrivateRuleSourceClosure.Rejected();

    internal static PrivateRuleResolution Prepare(WorkspacePrivateRuleSourceClosure.Decoded chosen,
        WorkspaceContinuationRestoreTarget target, DateTimeOffset created, FileWorkspaceStore store, ICharacterFileQueries queries,
        CancellationToken ct, RestoreSemanticOperation3287? operation = null, PrivateRuleResolution? prepared = null)
    {
        if (!target.Exists || target.OwnerId != chosen.Binding.OwnerId || target.WorkspaceId != chosen.Binding.WorkspaceId
            || chosen.CurrentEnvironment is not { } environment || created == default || created.Offset != TimeSpan.Zero
            || !PrivateCurrentRuleEnvironmentAdmission.TryAdmit(chosen, queries, ct, out var sourceAdmission, out _, operation)
            || sourceAdmission is null) throw WorkspacePrivateRuleSourceClosure.Rejected();
        long revision = checked(Math.Max(target.ContentRevision, chosen.Snapshot.Workspace.ContentRevision) + 1);
        // Keep every chosen document/history receipt and segment byte-for-byte.
        // The displaced branch remains a complete alternative, not merged history.
        var snapshot = chosen.Snapshot with
        {
            Workspace = chosen.Snapshot.Workspace with
            { ContentRevision = revision, SavedRevision = revision, LastUpdatedUtc = created }
        };
        var capture = chosen.Captures[environment.CaptureDigest];
        var localEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
            environment.ExecutionProfile, environment.ExecutionProfileFingerprint, environment.AdmissionDiagnostics);
        using var recovery = new WorkspacePrivateRuleSourceClosure.ObservationRecovery(chosen.Captures, chosen.Campaigns, queries);
        if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(snapshot, capture, localEnvironment, queries, ct,
                out var localAdmission, out _, null, operation, RestoreSemanticRole3287.Local, recovery) || localAdmission is null) throw WorkspacePrivateRuleSourceClosure.Rejected();
        var history = store.PrepareRuleResolutionHistory(chosen, snapshot, localEnvironment,
            prepared?.AuthenticatedHistory, prepared is not null);
        var archives = chosen.Captures.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value.ExportPrivateInputs()).ToArray();
        try
        {
            var closure = WorkspacePrivateRuleSourceClosure.Create(snapshot, archives, chosen.Campaigns,
                WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, ct, localEnvironment, history);
            // Exercise the actual historical receipt/source validator on the new record.
            var bytes = closure.ExportPrivateBytes();
            try { _ = WorkspacePrivateRuleSourceClosure.DecodeForRestore(bytes, closure.Binding, queries, ct); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            sourceAdmission.Revalidate(ct);
            localAdmission.Revalidate(ct);
            return new(closure, created, queries, history);
        }
        finally { foreach (var archive in archives) CryptographicOperations.ZeroMemory(archive); }
    }

    internal WorkspacePrivateRuleSourceClosure.Decoded Decode(ICharacterFileQueries queries, CancellationToken ct)
        => WorkspacePrivateRuleSourceClosure.DecodeForRestore(_bytes, Binding, queries, ct);
    public void Dispose() => CryptographicOperations.ZeroMemory(_bytes);
}
