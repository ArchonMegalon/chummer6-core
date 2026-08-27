using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Characters;
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
        CharacterCreationBootstrapSourceSnapshot sourceSnapshot)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(sourceSnapshot);

        if (!TryCaptureLifeModules(sourceSnapshot, out FrozenLifeModulesSnapshot lifeModules))
        {
            throw new InvalidDataException(
                "Life Module source authority changed while Creation activation was captured.");
        }

        var frozenStore = new FrozenWorkspaceStore(
            workspace,
            _authorityStore is IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            });
        var frozenResolver = new FrozenSourceDataResolver(
            workspace.Document.Content,
            sourceSnapshot.CreateFrozenContext());
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
            new FrozenLifeModulesCatalogService(lifeModules),
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

        CharacterCreationBootstrapSourceAuthorityBinding sourceAuthority =
            CreateSourceAuthorityBinding(sourceSnapshot, lifeModules);
        return new CharacterCreationInitialProjection(
            CharacterCreationBootstrapActivationSchemas.InitialProjectionV1,
            sourceAuthority,
            foundationService.Load(new(workspace.Id)),
            prerequisiteService.Load(new(workspace.Id)),
            attributesService.Load(new(workspace.Id)),
            contactsService.Load(new(workspace.Id)),
            qualitiesService.Load(new(workspace.Id)),
            magicService.Load(new(workspace.Id)));
    }

    public bool IsCurrent(
        CharacterCreationInitialProjection projection,
        ICharacterSourceDataContext sourceContext,
        string characterXml)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(sourceContext);
        return CharacterCreationBootstrapSourceSnapshot.TryCapture(
                   sourceContext,
                   characterXml,
                   out CharacterCreationBootstrapSourceSnapshot current)
               && TryCaptureLifeModules(current, out FrozenLifeModulesSnapshot lifeModules)
               && CharacterCreationBootstrapBindingDigest.FixedTimeEquals(
                   projection.SourceAuthority.AggregateDigest,
                   CreateSourceAuthorityBinding(current, lifeModules).AggregateDigest);
    }

    private bool TryCaptureLifeModules(
        CharacterCreationBootstrapSourceSnapshot sourceSnapshot,
        out FrozenLifeModulesSnapshot snapshot)
    {
        snapshot = FrozenLifeModulesSnapshot.Empty;
        string[] sources = sourceSnapshot.SourceProfile.EnabledSourcebooks
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        try
        {
            LifeModuleCatalogAuthorityDto authority = _lifeModulesCatalog.GetAuthority();
            IReadOnlyList<LifeModuleLegalOptionDto> options =
                _lifeModulesCatalog.GetOptionProjections("Nationality", sources);
            var captured = new FrozenLifeModulesSnapshot(
                authority,
                options.ToArray(),
                sources,
                string.Empty);
            snapshot = captured with { SnapshotDigest = ComputeLifeModulesDigest(captured) };
            return CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                snapshot.Authority.RawXmlDigest);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or FormatException
                                           or IOException
                                           or InvalidDataException
                                           or InvalidOperationException
                                           or UnauthorizedAccessException
                                           or System.Xml.XmlException)
        {
            return false;
        }
    }

    private static CharacterCreationBootstrapSourceAuthorityBinding
        CreateSourceAuthorityBinding(
            CharacterCreationBootstrapSourceSnapshot sourceSnapshot,
            FrozenLifeModulesSnapshot lifeModules)
    {
        string sourceProfileDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeCanonicalDigest(sourceSnapshot.SourceProfile);
        var unsigned = new CharacterCreationBootstrapSourceAuthorityBinding(
            CharacterCreationBootstrapActivationSchemas.SourceAuthorityV1,
            sourceSnapshot.RawCharacterXmlDigest,
            sourceSnapshot.SnapshotDigest,
            sourceProfileDigest,
            sourceSnapshot.Metatypes.SourceContext.AuthorityDigest,
            sourceSnapshot.Prerequisite.AuthorityDigest,
            sourceSnapshot.Qualities.AuthorityDigest,
            sourceSnapshot.MagicResonance.AuthorityDigest,
            lifeModules.SnapshotDigest,
            string.Empty);
        return unsigned with
        {
            AggregateDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(unsigned with { AggregateDigest = string.Empty })
        };
    }

    private static string ComputeLifeModulesDigest(FrozenLifeModulesSnapshot snapshot)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            snapshot.Authority,
            snapshot.Nationalities,
            snapshot.EnabledSources
        });

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

    private sealed record FrozenLifeModulesSnapshot(
        LifeModuleCatalogAuthorityDto Authority,
        IReadOnlyList<LifeModuleLegalOptionDto> Nationalities,
        IReadOnlyList<string> EnabledSources,
        string SnapshotDigest)
    {
        public static FrozenLifeModulesSnapshot Empty { get; } = new(
            new LifeModuleCatalogAuthorityDto(string.Empty, string.Empty, []),
            [],
            [],
            string.Empty);
    }

    private sealed class FrozenLifeModulesCatalogService : ILifeModulesCatalogService
    {
        private readonly FrozenLifeModulesSnapshot _snapshot;

        public FrozenLifeModulesCatalogService(FrozenLifeModulesSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public LifeModuleCatalogAuthorityDto GetAuthority() => _snapshot.Authority;

        public IReadOnlyList<LifeModuleStageDto> GetStages() => [];

        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null) => [];

        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(
            string? stage = null,
            IReadOnlyCollection<string>? enabledSources = null)
        {
            string[] requested = (enabledSources ?? [])
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (!string.Equals(stage, "Nationality", StringComparison.Ordinal)
                || !requested.SequenceEqual(
                    _snapshot.EnabledSources,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Frozen Creation activation Life Module authority was queried outside its captured scope.");
            }

            return _snapshot.Nationalities;
        }
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
