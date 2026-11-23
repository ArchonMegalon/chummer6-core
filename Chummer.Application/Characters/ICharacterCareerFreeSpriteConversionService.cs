using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerFreeSpriteConversionService
{
    CharacterCareerFreeSpriteConversionPreviewResult Preview(OwnerContextStamp owner, CharacterCareerFreeSpriteConversionRequest request);
    CharacterCareerFreeSpriteConversionResult Commit(OwnerContextStamp owner, CharacterCareerFreeSpriteConversionCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerFreeSpriteConversionResult Lookup(OwnerContextStamp owner, CharacterWorkspaceId workspaceId,
        Guid originalOperationId, string originalCommandDigest);
}

// Only Core's persisted writer may issue this result. Public capabilities and
// matching receipt DTOs alone cannot authorize reuse of its completed readback.
internal interface ICharacterCareerFreeSpriteConversionReadbackCommit
{
    (CharacterCareerFreeSpriteConversionResult Result, CharacterCareerFreeSpriteConversionCommitPhase Phase)
        CommitCareerFreeSpriteConversion(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFreeSpriteConversionCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken);
}

// This phase proves only whether the conversion writer was entered; ordinary
// legacy reads may still perform their existing migration housekeeping.
internal enum CharacterCareerFreeSpriteConversionCommitPhase
{
    BeforeConversionWriter,
    ConversionWriterEntered
}

public interface ICharacterCareerFreeSpriteConversionAtomicCommitCapability
{
    bool CareerFreeSpriteConversionGuardsInstalled { get; }
    string CareerFreeSpriteConversionRuntimeDigest { get; }
    CharacterCareerFreeSpriteConversionResult CommitCareerFreeSpriteConversion(IOwnerContextLease ownerLease,
        CharacterCareerFreeSpriteConversionCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
}

// Issued and consumed under the caller's existing owner lease. Ordinary Get/save stay closed.
public interface ICharacterCareerFreeSpriteConversionCurrentEnvironmentCapability
{
    WorkspaceStoreReadResult ReadCareerFreeSpriteConversionWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id);
    CharacterCareerFreeSpriteConversionPreviewResult PreviewCareerFreeSpriteConversion(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFreeSpriteConversionRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerFreeSpriteConversionResult CommitCareerFreeSpriteConversion(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFreeSpriteConversionCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
}
