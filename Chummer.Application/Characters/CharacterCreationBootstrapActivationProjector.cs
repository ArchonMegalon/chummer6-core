using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Projects the first Creation screen from the exact document returned by the
/// atomic create. Domain services see one frozen store snapshot and the same
/// already-resolved source context, so their normal fail-closed semantics are
/// preserved without another filesystem read or source-context construction.
/// </summary>
public sealed class CharacterCreationBootstrapActivationProjector :
    ICharacterCreationBootstrapActivationProjector
{
    private readonly IWorkspaceStore _authorityStore;
    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ILifeModulesCatalogService _lifeModulesCatalog;
    private readonly ICharacterCreationFoundationApplyAuthority _foundationApplyAuthority;

    public CharacterCreationBootstrapActivationProjector(
        IWorkspaceStore authorityStore,
        ICharacterFileQueries characterFileQueries,
        ILifeModulesCatalogService lifeModulesCatalog,
        ICharacterCreationFoundationApplyAuthority foundationApplyAuthority)
    {
        _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
        _characterFileQueries = characterFileQueries
            ?? throw new ArgumentNullException(nameof(characterFileQueries));
        _lifeModulesCatalog = lifeModulesCatalog
            ?? throw new ArgumentNullException(nameof(lifeModulesCatalog));
        _foundationApplyAuthority = foundationApplyAuthority
            ?? throw new ArgumentNullException(nameof(foundationApplyAuthority));
    }

    public CharacterCreationInitialProjection Project(
        WorkspaceStoredDocument workspace,
        ICharacterSourceDataContext sourceContext)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(sourceContext);

        var frozenStore = new FrozenWorkspaceStore(
            workspace,
            _authorityStore is IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            });
        var frozenResolver = new FrozenSourceDataResolver(
            workspace.Document.Content,
            sourceContext);
        var prerequisiteService = new CharacterCreationPrerequisiteService(
            frozenStore,
            _characterFileQueries,
            frozenResolver);
        var attributesService = new CharacterCreationAttributesService(
            frozenStore,
            frozenResolver);
        var foundationService = new CharacterCreationFoundationService(
            frozenStore,
            _characterFileQueries,
            frozenResolver,
            _lifeModulesCatalog,
            _foundationApplyAuthority);
        var contactsService = new CharacterCreationContactsService(frozenStore);
        var qualitiesService = new CharacterCreationQualitiesService(
            frozenStore,
            frozenResolver,
            prerequisiteService,
            attributesService);
        var magicService = new CharacterCreationMagicResonanceService(
            frozenStore,
            frozenResolver);

        return new CharacterCreationInitialProjection(
            CharacterCreationBootstrapActivationSchemas.InitialProjectionV1,
            foundationService.Load(new(workspace.Id)),
            prerequisiteService.Load(new(workspace.Id)),
            attributesService.Load(new(workspace.Id)),
            contactsService.Load(new(workspace.Id)),
            qualitiesService.Load(new(workspace.Id)),
            magicService.Load(new(workspace.Id)));
    }

    private sealed class FrozenSourceDataResolver : ICharacterSourceDataResolver
    {
        private readonly string _characterXml;
        private readonly ICharacterSourceDataContext _context;

        public FrozenSourceDataResolver(
            string characterXml,
            ICharacterSourceDataContext context)
        {
            _characterXml = characterXml;
            _context = context;
        }

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => string.Equals(characterXml, _characterXml, StringComparison.Ordinal)
                ? _context
                : null;
    }

    private sealed class FrozenWorkspaceStore :
        IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        private readonly WorkspaceStoredDocument _workspace;

        public FrozenWorkspaceStore(
            WorkspaceStoredDocument workspace,
            bool supportsAuxiliaryStateAtomicCommit)
        {
            _workspace = workspace;
            SupportsWorkspaceAuxiliaryStateAtomicCommit =
                supportsAuxiliaryStateAtomicCommit;
        }

        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit { get; }

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => id == _workspace.Id
                ? new WorkspaceStoreReadResult(
                    WorkspaceOperationOutcome.Success,
                    _workspace)
                : MissingRead();

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
            => Get(id);

        public IReadOnlyList<WorkspaceStoreEntry> List() => [Entry()];

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => [Entry()];

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => UnavailableMutation();

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            WorkspaceDocument document)
            => UnavailableMutation();

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => UnavailableMutation();

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => UnavailableMutation();

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => UnavailableMutation();

        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => UnavailableMutation();

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => UnavailableMutation();

        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => UnavailableMutation();

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            string expectedAuxiliaryStateDigest,
            WorkspaceDocument document)
            => UnavailableMutation();

        private WorkspaceStoreEntry Entry() => new(
            _workspace.Id,
            _workspace.LastUpdatedUtc,
            _workspace.ContentRevision,
            _workspace.SavedRevision);

        private static WorkspaceStoreReadResult MissingRead() => new(
            WorkspaceOperationOutcome.Missing,
            Error: "Frozen creation activation snapshot does not contain that workspace.");

        private static WorkspaceStoreMutationResult UnavailableMutation() => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Frozen creation activation snapshots are read-only.");
    }
}
