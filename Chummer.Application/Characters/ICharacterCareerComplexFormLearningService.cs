using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerComplexFormLearningService
{
    CharacterCareerComplexFormLearningCatalogResult Catalog(OwnerScope owner, CharacterCareerComplexFormLearningCatalogRequest request);
    CharacterCareerComplexFormLearningPreviewResult Preview(OwnerScope owner, CharacterCareerComplexFormLearningRequest request);
    CharacterCareerComplexFormLearningResult Commit(OwnerScope owner, CharacterCareerComplexFormLearningCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerComplexFormLearningResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest);
}

/// <summary>Revalidate under the common workspace lease, then atomically replace payload and ledger.</summary>
public interface ICharacterCareerComplexFormLearningAtomicCommitCapability
{
    bool CareerComplexFormLearningGuardsInstalled { get; }
    string CareerComplexFormLearningRuntimeDigest { get; }
    CharacterCareerComplexFormLearningResult CommitCareerComplexFormLearning(OwnerScope owner,
        CharacterCareerComplexFormLearningCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default);
}

/// <summary>All methods belong to the retained ICharacterSourceDataContext, never a UI supplied authority.</summary>
public interface ICharacterCareerComplexFormLearningSourceContext
{
    bool TryResolveCareerComplexFormLearningSource(Guid sourceId, out CharacterCareerComplexFormLearningSource? source);
    bool TryResolveCareerResonanceSource(out CharacterCareerResonanceSource? source);
}

public sealed record CharacterCareerComplexFormLearningSource(string ComplexFormXml, string AuthorityState);
public sealed record CharacterCareerResonanceQualitySource(Guid InstanceId, Guid SourceId, string QualityXml);
public sealed record CharacterCareerResonanceSource(string MetatypeXml,
    IReadOnlyList<CharacterCareerResonanceQualitySource> Qualities, string AuthorityState);

public interface ICharacterCareerComplexFormLearningCatalogSourceContext
{
    bool TryResolveCareerComplexFormLearningCatalog(out CharacterCareerComplexFormLearningSourceCatalog? catalog);
}

public sealed record CharacterCareerComplexFormLearningCatalogSource(
    Guid SourceId, string Name, string Book, string Page, string ComplexFormXml);
public sealed record CharacterCareerComplexFormLearningSourceCatalog(
    IReadOnlyList<CharacterCareerComplexFormLearningCatalogSource> Sources,
    IReadOnlyList<string> EnabledSourcebooks, string AuthorityState);

/// <summary>Typed learning operations evaluated within the real owner/workspace leases.</summary>
public interface ICharacterCareerComplexFormLearningCurrentEnvironmentCapability
{
    WorkspaceStoreReadResult ReadCareerComplexFormLearningWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id);
    CharacterCareerComplexFormLearningCatalogResult CatalogCareerComplexFormLearning(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterCareerComplexFormLearningCatalogRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerComplexFormLearningPreviewResult PreviewCareerComplexFormLearning(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterCareerComplexFormLearningRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerComplexFormLearningResult CommitCareerComplexFormLearning(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterCareerComplexFormLearningCommand command, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
}

/// <summary>Expected owner generation is checked by the service before it acquires its lease.</summary>
public interface IOwnerBoundCharacterCareerComplexFormLearningService5703
{
    CharacterCareerComplexFormLearningCatalogResult Catalog(OwnerContextStamp expected, CharacterCareerComplexFormLearningCatalogRequest request);
    CharacterCareerComplexFormLearningPreviewResult Preview(OwnerContextStamp expected, CharacterCareerComplexFormLearningRequest request);
    CharacterCareerComplexFormLearningResult Commit(OwnerContextStamp expected, CharacterCareerComplexFormLearningCommand command, CancellationToken cancellationToken = default);
    CharacterCareerComplexFormLearningResult Lookup(OwnerContextStamp expected, CharacterWorkspaceId id, Guid operationId, string commandDigest);
}
