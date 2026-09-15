using Chummer.Application.Owners;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Explain;
using Chummer.Infrastructure.Owners;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// One restored workspace and one immutable nonlocal issuer, privately owned for
/// a synchronous rule-query lifetime. It exposes no store, owner accessor, lease,
/// provider, or mutation interface. Construction does not authorize a Hub account
/// or consent, and an already returned result is not continuing authorization.
/// </summary>
public sealed class PrivateWorkspaceRuleRuntime : IDisposable
{
    private readonly object _operationGate = new();
    private readonly RequestOwnerContextAccessor _owners;
    private readonly OwnedWorkspaceScratchDirectory _scratch;
    private readonly WorkspaceRuleQuestionService _questions;
    private readonly OwnerContextStamp _ownerStamp;
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly long _savedRevision;
    private readonly WorkspaceContinuationRestoreReceipt _restoreReceipt;
    private int _closing;

    internal PrivateWorkspaceRuleRuntime(RequestOwnerContextAccessor owners,
        OwnedWorkspaceScratchDirectory scratch, WorkspaceRuleQuestionService questions,
        OwnerContextStamp ownerStamp, CharacterWorkspaceId workspaceId,
        long contentRevision, long savedRevision, WorkspaceContinuationRestoreReceipt restoreReceipt)
    {
        _owners = owners;
        _scratch = scratch;
        _questions = questions;
        _ownerStamp = ownerStamp;
        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _savedRevision = savedRevision;
        _restoreReceipt = restoreReceipt;
    }

    public OwnerContextStamp OwnerStamp => ReadObservation(() => _ownerStamp);
    public CharacterWorkspaceId WorkspaceId => ReadObservation(() => _workspaceId);
    public long ContentRevision => ReadObservation(() => _contentRevision);
    public long SavedRevision => ReadObservation(() => _savedRevision);
    public WorkspaceContinuationRestoreReceipt RestoreReceipt => ReadObservation(() => _restoreReceipt);

    // Existing friend tests can inspect the real files; this is not a public
    // path, store, or authorization capability offered to hosting consumers.
    internal string ScratchDirectoryPath => _scratch.DirectoryPath;

    /// <summary>
    /// Resolves against this exact restored identity/revision and actual issuer.
    /// Cancellation is checked around synchronous Core work; it cannot preempt
    /// XML parsing, source reads, the store's lock wait, or its existing leases.
    /// </summary>
    public WorkspaceRuleQuestionResult Resolve(OwnerContextStamp expectedOwner,
        WorkspaceRuleQuestionRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_operationGate)
        {
            ThrowIfClosing();
            cancellationToken.ThrowIfCancellationRequested();
            if (request.WorkspaceId != _workspaceId || request.ExpectedContentRevision != _contentRevision)
                throw new ArgumentException("The question must name this restored workspace and content revision.", nameof(request));

            // Core owns admission and its lease. Do not turn a captured stamp
            // into a poll-only authority or add an outer/nested owner lease.
            WorkspaceRuleQuestionResult result = _questions.Resolve(expectedOwner, request);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfClosing();
            return result;
        }
    }

    /// <summary>
    /// Stops new admissions, drains admitted synchronous work, and removes only
    /// the owned scratch child. It does not preempt work or securely erase data.
    /// A cleanup failure leaves admission terminal and may be explicitly retried
    /// after the host resolves the storage problem. There is no automatic retry.
    /// </summary>
    public void Dispose()
    {
        // A reentrant call must not close the runtime and then deadlock or
        // delete files still in use by this very invocation.
        if (Monitor.IsEntered(_operationGate))
            throw new InvalidOperationException("A private workspace runtime cannot end during its own operation.");

        Interlocked.Exchange(ref _closing, 1);
        // Never hold the operation gate while waiting for the owner lease. A
        // facade invocation admitted before closing may still need to leave it.
        _owners.Dispose();
        lock (_operationGate)
        {
            // Every concurrent disposer crosses both barriers, and the helper
            // serializes cleanup. Never delete in a finally if ending failed.
            _scratch.Dispose();
        }
    }

    private T ReadObservation<T>(Func<T> read)
    {
        ThrowIfClosing();
        lock (_operationGate)
        {
            ThrowIfClosing();
            return read();
        }
    }

    private void ThrowIfClosing()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing) != 0, this);
}
