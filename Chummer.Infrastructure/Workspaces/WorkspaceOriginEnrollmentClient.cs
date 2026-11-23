using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public interface IWorkspaceOriginEnrollmentTransport
{
    Task<T> SendAsync<T>(string action, WorkspaceOriginRequest request, WorkspaceOriginProof? proof, CancellationToken ct);
}

// Shared orchestration. Platform adapters below use the real current account
// grant and owner authority. An arbitrary transport cannot forge issuer evidence.
public static class WorkspaceOriginEnrollmentClient
{
    // One original possession operation for this exact owner/origin/grant. A
    // changed grant gets a new operation; uncertain old operations are recovered,
    // never hidden behind a replacement operation or key.
    public static Task EnsureEnrolledAsync(FileWorkspaceStore.OriginTrustDomain domain,
        IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp, string installationId,
        string grantId, IWorkspaceOriginEnrollmentTransport transport, CancellationToken ct = default)
    {
        if (!WorkspaceOriginProtocol.Token(grantId, 256)) throw new UnauthorizedAccessException("origin-grant-unavailable");
        var origin = domain.ExportOwnerOrigin(owners, stamp);
        byte[] digest = System.Security.Cryptography.SHA256.HashData(WorkspaceOriginProtocol.Bytes("enrollment-operation",
            new { owner = stamp.Owner.NormalizedValue, origin.OriginId, installationId, grantId }));
        return EnrollAsync(domain, owners, stamp, installationId, new Guid(digest.AsSpan(0, 16)), transport, ct);
    }

    public static async Task EnrollAsync(FileWorkspaceStore.OriginTrustDomain domain,
        IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp, string installationId,
        Guid originalOperation, IWorkspaceOriginEnrollmentTransport transport, CancellationToken ct = default)
    {
        var request = domain.PrepareOriginRequest(owners, stamp, installationId, originalOperation);
        bool first = domain.MarkOriginDispatch(owners, stamp, request, "begin");
        var operation = await transport.SendAsync<WorkspaceOriginOperation>(first ? "begin" : "recover", request, null, ct);
        RequireOwner(owners, stamp);
        if (operation.Challenge.Payload.Request != request) throw new InvalidDataException("origin-operation-changed");
        if (operation.Completed)
        {
            await RefreshAsync(domain, owners, stamp, installationId, Guid.NewGuid(), "", transport, ct);
            return;
        }
        var proof = domain.ProveOrigin(owners, stamp, operation.Challenge);
        if (!domain.MarkOriginDispatch(owners, stamp, request, "prove"))
            throw new IOException("origin-proof-outcome-unresolved; recover original operation only");
        operation = await transport.SendAsync<WorkspaceOriginOperation>("prove", request, proof, ct);
        RequireOwner(owners, stamp);
        if (!operation.Completed || operation.Challenge.Payload != proof.Challenge)
            throw new IOException("origin-proof-outcome-unresolved");
        await RefreshAsync(domain, owners, stamp, installationId, Guid.NewGuid(), "", transport, ct);
    }

    public static async Task<FileWorkspaceStore.OriginTrustDomain.VerifiedOriginEnrollment> RefreshAsync(
        FileWorkspaceStore.OriginTrustDomain domain, IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp,
        string installationId, Guid operation, string exactHistoryDigest, IWorkspaceOriginEnrollmentTransport transport,
        CancellationToken ct = default)
    {
        var request = domain.PrepareOriginRequest(owners, stamp, installationId, operation, exactHistoryDigest);
        var signed = await transport.SendAsync<WorkspaceOriginSigned<WorkspaceOriginSnapshot>>("snapshot", request, null, ct);
        RequireOwner(owners, stamp);
        return domain.AcceptOriginSnapshot(owners, stamp, signed);
    }
    private static void RequireOwner(IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp)
    {
        if (!owners.TryAcquire(stamp, out var lease) || lease is null) throw new UnauthorizedAccessException("origin-owner-changed");
        using (lease) if (owners.Capture() != stamp || lease.Stamp != stamp) throw new UnauthorizedAccessException("origin-owner-changed");
    }
}
