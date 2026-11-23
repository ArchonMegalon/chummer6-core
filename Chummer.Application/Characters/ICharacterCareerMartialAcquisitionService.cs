using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerMartialAcquisitionService
{
    CharacterCareerMartialAcquisitionListResult List(OwnerContextStamp owner, CharacterWorkspaceId workspaceId, long expectedRevision);
    CharacterCareerMartialAcquisitionPreviewResult Preview(OwnerContextStamp owner, CharacterCareerMartialAcquisitionRequest request);
    CharacterCareerMartialAcquisitionResult Commit(OwnerContextStamp owner, CharacterCareerMartialAcquisitionCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerMartialAcquisitionResult Lookup(OwnerContextStamp owner, CharacterWorkspaceId workspaceId,
        Guid originalOperationId, string originalCommandDigest);
    CharacterCareerMartialAcquisitionResult FinalizeDispatch(OwnerContextStamp owner,
        CharacterCareerMartialAcquisitionCommand originalCommand, CancellationToken cancellationToken = default);
}

public interface ICharacterCareerMartialAcquisitionAtomicCommitCapability
{
    bool CareerMartialAcquisitionGuardsInstalled { get; }
    string CareerMartialAcquisitionRuntimeDigest { get; }
    // Observe the workspace through the same admitted retained-rule lane as the write.
    // Reuse the caller's owner lease; no resolver or execution authority escapes.
    WorkspaceStoreReadResult ReadCareerMartialAcquisitionWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease ownerLease, CharacterWorkspaceId workspace);
    // Read-only recovery of an already applied receipt from a locally admitted private
    // closure. Portable history alone cannot grant this capability or authorize a write.
    CharacterCareerMartialAcquisitionResult RecoverRestoredCareerMartialAcquisitionReceipt(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease ownerLease, WorkspaceStoredDocument expected, Guid operationId, string commandDigest)
        => new(CharacterCareerMartialAcquisitionOutcome.IdempotencyConflict, CurrentWorkspaceRevision: expected.ContentRevision);
    CharacterCareerMartialAcquisitionListResult ListCareerMartialAcquisition(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease ownerLease, CharacterWorkspaceId workspace, long expectedRevision, ICharacterSourceDataResolver sources);
    CharacterCareerMartialAcquisitionPreviewResult PreviewCareerMartialAcquisition(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease ownerLease, CharacterCareerMartialAcquisitionRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerMartialAcquisitionResult CommitCareerMartialAcquisition(IOwnerContextLease ownerLease,
        CharacterCareerMartialAcquisitionCommand command, ICharacterSourceDataResolver sources,
        IOwnerContextLeaseAccessor owners, CancellationToken cancellationToken = default);
    CharacterCareerMartialAcquisitionResult FinalizeCareerMartialAcquisitionDispatch(IOwnerContextLease ownerLease,
        CharacterCareerMartialAcquisitionCommand originalCommand, IOwnerContextLeaseAccessor owners,
        CancellationToken cancellationToken = default);
}

/// <summary>Effective enabled source rows from the retained context, never caller supplied rules.</summary>
public interface ICharacterCareerMartialAcquisitionSourceContext
{
    bool TryResolveCareerMartialAcquisitionSource(Guid sourceId, CharacterCareerMartialAcquisitionKind kind,
        out CharacterCareerMartialAcquisitionSource? source);
    bool TryResolveCareerMartialAcquisitionCatalog(out CharacterCareerMartialAcquisitionSourceCatalog? catalog);
}
public sealed record CharacterCareerMartialAcquisitionSource(string Xml, string AuthorityState);
public sealed record CharacterCareerMartialAcquisitionCatalogSource(Guid SourceId,
    CharacterCareerMartialAcquisitionKind Kind, string Xml);
public sealed record CharacterCareerMartialAcquisitionSourceCatalog(
    IReadOnlyList<CharacterCareerMartialAcquisitionCatalogSource> Sources, string AuthorityState);
