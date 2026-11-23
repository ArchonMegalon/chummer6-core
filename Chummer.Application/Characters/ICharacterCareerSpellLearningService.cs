using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerSpellLearningService
{
    CharacterCareerSpellLearningCatalogResult Catalog(OwnerScope owner, CharacterCareerSpellLearningCatalogRequest request);
    CharacterCareerSpellLearningPreviewResult Preview(OwnerScope owner, CharacterCareerSpellLearningRequest request);
    CharacterCareerSpellLearningResult Commit(OwnerScope owner, CharacterCareerSpellLearningCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerSpellLearningResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest);
}

/// <summary>Revalidate and write payload plus ledger under the common owner/workspace lease.</summary>
public interface ICharacterCareerSpellLearningAtomicCommitCapability
{
    bool CareerSpellLearningGuardsInstalled { get; }
    string CareerSpellLearningRuntimeDigest { get; }
    CharacterCareerSpellLearningResult CommitCareerSpellLearning(OwnerScope owner,
        CharacterCareerSpellLearningCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional Career capability; it admits exact enabled effective sources, never a creation draft.</summary>
public interface ICharacterCareerSpellLearningSourceContext
{
    bool TryResolveCareerSpellLearningSource(Guid spellId, Guid traditionId,
        out CharacterCareerSpellLearningSource? source);
}

/// <summary>Application-local detached source evidence, not a caller-supplied price or permission.</summary>
public sealed record CharacterCareerSpellLearningSource(string SpellXml, string TraditionXml, string AuthorityState);

/// <summary>Optional enumeration on the same retained source context used by Preview.</summary>
public interface ICharacterCareerSpellLearningCatalogSourceContext
{
    bool TryResolveCareerSpellLearningCatalog(out CharacterCareerSpellLearningSourceCatalog? catalog);
}

public sealed record CharacterCareerSpellLearningCatalogSource(
    Guid SourceId, string Name, string Category, string Book, string Page, string SpellXml);

public sealed record CharacterCareerSpellLearningSourceCatalog(
    IReadOnlyList<CharacterCareerSpellLearningCatalogSource> Sources,
    IReadOnlyList<string> EnabledSourcebooks, string AuthorityState);

/// <summary>Typed learning operations evaluated within the real owner/workspace leases.</summary>
public interface ICharacterCareerSpellLearningCurrentEnvironmentCapability
{
    WorkspaceStoreReadResult ReadCareerSpellLearningWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id);
    CharacterCareerSpellLearningCatalogResult CatalogCareerSpellLearning(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterCareerSpellLearningCatalogRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerSpellLearningPreviewResult PreviewCareerSpellLearning(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterCareerSpellLearningRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerSpellLearningResult CommitCareerSpellLearning(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterCareerSpellLearningCommand command, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
}

/// <summary>Expected owner generation is checked by the service before it acquires its lease.</summary>
public interface IOwnerBoundCharacterCareerSpellLearningService5703
{
    CharacterCareerSpellLearningCatalogResult Catalog(OwnerContextStamp expected, CharacterCareerSpellLearningCatalogRequest request);
    CharacterCareerSpellLearningPreviewResult Preview(OwnerContextStamp expected, CharacterCareerSpellLearningRequest request);
    CharacterCareerSpellLearningResult Commit(OwnerContextStamp expected, CharacterCareerSpellLearningCommand command, CancellationToken cancellationToken = default);
    CharacterCareerSpellLearningResult Lookup(OwnerContextStamp expected, CharacterWorkspaceId id, Guid operationId, string commandDigest);
}
