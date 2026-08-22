using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationFoundationDraftApplyAuthorityTests
{
    private const string CanonicalLifeModuleSettingsId = "8a31af6d-7137-4284-872b-7d8087e156c6";
    private const string HumanId = "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7";
    private const string ElfId = "b3259991-b315-4dbe-ae3c-51f71a1116e2";
    private const string TirModuleId = "83c132b5-fcf5-4a43-b9de-6c8ab206a586";
    private const string TirHumanElfVersionId = "604831d9-0fdc-4579-aa7e-bc5d99bcee5d";
    private const string UcasModuleId = "f35ba316-dd0f-48ab-9f06-d7329305a44e";
    private const string UcasVersionId = "f9e684bb-d7fa-4fc7-87e0-d140cc6fc64d";

    [TestMethod]
    public void Exact_Tir_Human_and_Elf_drafts_preserve_legacy_effect_order_and_raw_xml()
    {
        CharacterCreationFoundationDraftLedger human = ApplyExactTirDraft("Human");
        CharacterCreationFoundationDraftLedger elf = ApplyExactTirDraft("Elf");

        Assert.AreEqual(TirModuleId, human.Selection.ModuleId);
        Assert.AreEqual(TirHumanElfVersionId, human.Selection.VersionId);
        Assert.AreEqual("Human", human.RequestedMetatype);
        Assert.AreEqual("Elf", elf.RequestedMetatype);
        Assert.IsTrue(human.RequirementEvaluations.All(requirement => requirement.IsMet));
        Assert.IsTrue(elf.RequirementEvaluations.All(requirement => requirement.IsMet));
        CollectionAssert.AreEqual(
            human.ProjectedEffects.Select(effect => effect.EffectId).ToList(),
            elf.ProjectedEffects.Select(effect => effect.EffectId).ToList());
        CollectionAssert.Contains(
            human.SourceAnchorIds.ToList(),
            $"metatypes.xml#metatype:{HumanId}");
        CollectionAssert.Contains(
            elf.SourceAnchorIds.ToList(),
            $"metatypes.xml#metatype:{ElfId}");
        Assert.AreEqual(
            CharacterCreationFoundationDraftStatuses.PendingFinalization,
            human.CompilationStatus);
        Assert.IsFalse(human.CharacterEffectsApplied);
    }

    [TestMethod]
    public void Duplicate_is_zero_write_and_exact_update_advances_draft_and_checkpoint_once()
    {
        string directory = CreateTempDirectory();
        try
        {
            CharacterWorkspaceId id = new("foundation-update");
            string xml = CharacterXml("Human");
            FileWorkspaceStore store = new(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            CharacterCreationFoundationService service = CreateService(store);
            CharacterCreationFoundationState initial = Load(service, id);
            CharacterCreationFoundationPreview first = Preview(
                service,
                initial.Binding,
                TirModuleId,
                TirHumanElfVersionId);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                Confirm(service, first).Outcome);

            CharacterCreationFoundationState resumed = Load(service, id);
            Assert.AreEqual(15m, resumed.LifeModuleBudget.Used);
            string targetPath = WorkspacePath(directory, id);
            byte[] beforeDuplicate = File.ReadAllBytes(targetPath);
            DateTime beforeDuplicateWrite = File.GetLastWriteTimeUtc(targetPath);
            CharacterCreationFoundationResult<CharacterCreationFoundationPreview> duplicateResult =
                service.Preview(new CharacterCreationFoundationPreviewRequest(
                    resumed.Binding,
                    "Human",
                    new CharacterCreationFoundationSelection(
                        TirModuleId,
                        TirHumanElfVersionId)));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, duplicateResult.Outcome);
            CollectionAssert.Contains(
                duplicateResult.Blockers.ToList(),
                CharacterCreationFoundationBlockers.PendingDraftDuplicate);
            CollectionAssert.AreEqual(beforeDuplicate, File.ReadAllBytes(targetPath));
            Assert.AreEqual(beforeDuplicateWrite, File.GetLastWriteTimeUtc(targetPath));

            CharacterCreationFoundationPreview update = Preview(
                service,
                resumed.Binding,
                UcasModuleId,
                UcasVersionId,
                metatype: "Elf");
            Assert.AreEqual(15m, update.LifeModuleBudgetBefore.Used);
            Assert.AreEqual(55m, update.LifeModuleBudgetAfter.Used);
            Assert.AreEqual(695m, update.LifeModuleBudgetAfter.Remaining);
            CharacterCreationFoundationApplyReceipt receipt = Confirm(service, update).Value!;
            Assert.AreEqual(2L, receipt.DraftRevision);
            Assert.AreEqual(3L, receipt.ContentRevision);
            Assert.AreEqual(3L, receipt.SavedRevision);

            WorkspaceStoredDocument reopened = new FileWorkspaceStore(directory).Get(id).Value!;
            CharacterCreationFoundationDraftLedger draft = reopened.Document.AuxiliaryState
                .CharacterCreationFoundationDraft!;
            Assert.AreEqual(2L, draft.DraftRevision);
            Assert.AreEqual(2L, draft.BaseContentRevision);
            Assert.AreEqual("Elf", draft.RequestedMetatype);
            Assert.AreEqual(UcasModuleId, draft.Selection.ModuleId);
            Assert.AreEqual(xml, reopened.Document.Content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Tampered_preview_and_persisted_draft_digest_are_rejected_without_effect_claims()
    {
        string directory = CreateTempDirectory();
        try
        {
            CharacterWorkspaceId id = new("foundation-tamper");
            FileWorkspaceStore store = new(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
            CharacterCreationFoundationService service = CreateService(store);
            CharacterCreationFoundationPreview preview = Preview(
                service,
                Load(service, id).Binding,
                TirModuleId,
                TirHumanElfVersionId);
            string targetPath = WorkspacePath(directory, id);
            byte[] before = File.ReadAllBytes(targetPath);

            CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> tampered =
                service.Confirm(new CharacterCreationFoundationConfirmRequest(
                    preview.Binding,
                    preview.RequestedMetatype,
                    preview.Selection,
                    "sha256:" + new string('0', 64),
                    ExplicitlyConfirmed: true,
                    preview.FollowUpValues));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, tampered.Outcome);
            CollectionAssert.Contains(
                tampered.Blockers.ToList(),
                CharacterCreationFoundationBlockers.PreviewDigestMismatch);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                Confirm(service, preview).Outcome);
            WorkspaceStoredDocument committed = store.Get(id).Value!;
            string originalDigest = committed.Document.AuxiliaryState
                .CharacterCreationFoundationDraft!.DraftDigest;
            string forgedDigest = "sha256:" + new string(
                originalDigest.EndsWith('f') ? 'e' : 'f',
                64);
            string persistedJson = File.ReadAllText(targetPath);
            File.WriteAllText(
                targetPath,
                persistedJson.Replace(originalDigest, forgedDigest, StringComparison.Ordinal));

            CharacterCreationFoundationState reopened = Load(
                CreateService(new FileWorkspaceStore(directory)),
                id);
            Assert.IsNull(reopened.PendingDraft);
            Assert.AreEqual(
                CharacterCreationFoundationResumeStatuses.AuthorityRequired,
                reopened.ResumeStatus);
            CollectionAssert.Contains(
                reopened.AuthorityBlockers.ToList(),
                CharacterCreationFoundationBlockers.PendingDraftInvalid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Atomic_write_fault_rolls_back_and_returns_typed_blocker()
    {
        string directory = CreateTempDirectory();
        try
        {
            CharacterWorkspaceId id = new("foundation-fault");
            string xml = CharacterXml("Human");
            FileWorkspaceStore healthy = new(directory);
            Assert.IsTrue(healthy.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            var faulty = new FileWorkspaceStore(
                directory,
                new ThrowingFaultInjector(FileWorkspaceStoreFaultStage.AfterTempFileFlushed));
            CharacterCreationFoundationService service = CreateService(faulty);
            CharacterCreationFoundationPreview preview = Preview(
                service,
                Load(service, id).Binding,
                TirModuleId,
                TirHumanElfVersionId);
            string targetPath = WorkspacePath(directory, id);
            byte[] before = File.ReadAllBytes(targetPath);

            CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> result =
                Confirm(service, preview);

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            CollectionAssert.Contains(
                result.Blockers.ToList(),
                CharacterCreationFoundationBlockers.WorkspaceUnavailable);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));
            WorkspaceStoredDocument reopened = new FileWorkspaceStore(directory).Get(id).Value!;
            Assert.AreEqual(1L, reopened.ContentRevision);
            Assert.AreEqual(0L, reopened.SavedRevision);
            Assert.IsTrue(reopened.Document.AuxiliaryState.IsEmpty);
            Assert.AreEqual(xml, reopened.Document.Content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Noncapable_store_and_DI_compositions_remain_unavailable_at_preview()
    {
        CharacterWorkspaceId id = new("foundation-noncapable");
        InMemoryWorkspaceStore store = new();
        Assert.IsTrue(store.CreateWorkspaceDocument(
            id,
            new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
        CharacterCreationFoundationService service = CreateService(
            store,
            new CharacterCreationFoundationDraftApplyAuthority(store));

        CharacterCreationFoundationPreview preview = service.Preview(
            new CharacterCreationFoundationPreviewRequest(
                Load(service, id).Binding,
                "Human",
                new CharacterCreationFoundationSelection(
                    TirModuleId,
                    TirHumanElfVersionId))).Value!;

        Assert.IsFalse(preview.CanApply);
        CollectionAssert.Contains(
            preview.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);

        using ServiceProvider noncapableProvider = new ServiceCollection()
            .AddSingleton<IWorkspaceStore>(new InMemoryWorkspaceStore())
            .AddCharacterCreationFoundationDraftPersistence()
            .BuildServiceProvider();
        Assert.IsInstanceOfType<UnavailableCharacterCreationFoundationApplyAuthority>(
            noncapableProvider.GetRequiredService<ICharacterCreationFoundationApplyAuthority>());

        string directory = CreateTempDirectory();
        try
        {
            using ServiceProvider capableProvider = new ServiceCollection()
                .AddSingleton<IWorkspaceStore>(new FileWorkspaceStore(directory))
                .AddCharacterCreationFoundationDraftPersistence()
                .BuildServiceProvider();
            Assert.IsInstanceOfType<CharacterCreationFoundationDraftApplyAuthority>(
                capableProvider.GetRequiredService<ICharacterCreationFoundationApplyAuthority>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Atomic_auxiliary_conflict_is_typed_and_writes_nothing()
    {
        ConflictCapabilityStore store = new();
        CharacterWorkspaceId id = new("foundation-aux-conflict");
        Assert.IsTrue(store.CreateWorkspaceDocument(
            id,
            new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
        WorkspaceStoredDocument workspace = store.Get(id).Value!;
        LifeModuleLegalOptionDto nationality = GetTirNationality();
        LifeModuleVersionProjectionDto version = nationality.Versions.Single(item =>
            item.VersionId == TirHumanElfVersionId);
        LifeModuleRequirementProjectionDto[] requirements = version.Requirements
            .Select(requirement => requirement with
            {
                IsMet = true,
                DisableReasonKey = null,
                RequiresCharacterAuthority = false
            })
            .ToArray();
        CharacterCreationBudgetState before = ExactBudget(used: 0);
        CharacterCreationBudgetState after = ExactBudget(used: 15);
        CharacterCreationLegalOption selectedMetatype = Load(
                CreateService(store),
                id)
            .MetatypeOptions.Single(option => option.OptionId == HumanId);
        var context = new CharacterCreationFoundationAuthorityContext(
            workspace,
            new CharacterFileSummary(
                "Runner", "Runner", "Human", CharacterCreationBuildMethods.LifeModules,
                "5.225.0", "5.225.0", 0, 0, false),
            "Human",
            selectedMetatype,
            new CharacterCreationFoundationSelection(TirModuleId, TirHumanElfVersionId),
            nationality,
            version,
            requirements,
            new Dictionary<string, string>(),
            before,
            new CharacterCreationChoiceCost(CharacterCreationBudgetIds.LifeModules, 15, "karma"),
            after,
            "sha256:" + new string('a', 64));
        var authority = new CharacterCreationFoundationDraftApplyAuthority(store);
        Assert.IsTrue(authority.Preview(context).CanApply);

        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> result =
            authority.ApplyAndCheckpoint(context, "sha256:" + new string('b', 64));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToList(),
            CharacterCreationFoundationBlockers.PendingDraftConflict);
        Assert.AreEqual(1, store.AtomicCalls);
        Assert.AreEqual(0, store.SuccessfulAtomicWrites);
        Assert.AreEqual(1L, store.Get(id).Value?.ContentRevision);
    }

    private static CharacterCreationFoundationDraftLedger ApplyExactTirDraft(string metatype)
    {
        string directory = CreateTempDirectory();
        try
        {
            CharacterWorkspaceId id = new("tir-" + metatype.ToLowerInvariant());
            // The canonical character remains a Human placeholder even when the
            // authoritative draft selection is Elf.
            string xml = CharacterXml("Human");
            FileWorkspaceStore store = new(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            CharacterCreationFoundationService service = CreateService(store);
            CharacterCreationFoundationState initial = Load(service, id);
            CharacterCreationFoundationPreview preview = Preview(
                service,
                initial.Binding,
                TirModuleId,
                TirHumanElfVersionId,
                metatype);
            Assert.IsTrue(preview.CanApply);
            decimal expectedUsed = string.Equals(metatype, "Elf", StringComparison.Ordinal)
                ? 55m
                : 15m;
            Assert.AreEqual(0m, preview.LifeModuleBudgetBefore.Used);
            Assert.AreEqual(expectedUsed, preview.LifeModuleBudgetAfter.Used);
            Assert.AreEqual(750m - expectedUsed, preview.LifeModuleBudgetAfter.Remaining);
            CharacterCreationFoundationDiffEntry metatypeChoice = preview.Diff.Single(item =>
                item.DiffId == "foundation:requested-metatype");
            Assert.AreEqual(metatype, metatypeChoice.AfterValue);
            Assert.IsTrue(metatypeChoice.IsAuthoritative);
            Assert.IsFalse(metatypeChoice.AppliesToCharacterDocument);
            CharacterCreationFoundationDiffEntry metatypeCost = preview.Diff.Single(item =>
                item.DiffId == "foundation:metatype-cost");
            Assert.AreEqual(
                string.Equals(metatype, "Elf", StringComparison.Ordinal) ? "40" : "0",
                metatypeCost.AfterValue);
            Assert.IsTrue(preview.Diff.All(item =>
                item.Phase == CharacterCreationFoundationDiffPhases.DraftLedger
                && !item.AppliesToCharacterDocument));
            LifeModuleEffectProjectionDto[] expectedEffects =
            [
                .. preview.NationalityVersion!.Effects,
                .. preview.Nationality!.Effects
            ];

            CharacterCreationFoundationApplyReceipt receipt = Confirm(service, preview).Value!;
            Assert.AreEqual(1L, receipt.DraftRevision);
            Assert.AreEqual(2L, receipt.ContentRevision);
            Assert.AreEqual(2L, receipt.SavedRevision);
            Assert.AreEqual(initial.Binding.RawCharacterXmlDigest, receipt.RawCharacterXmlDigest);
            Assert.AreEqual(initial.Binding.SourceDigest, receipt.SourceDigest);
            Assert.IsFalse(receipt.CharacterEffectsApplied);
            WorkspaceStoredDocument reopened = new FileWorkspaceStore(directory).Get(id).Value!;
            Assert.AreEqual(xml, reopened.Document.Content);
            CharacterCreationFoundationDraftLedger draft = reopened.Document.AuxiliaryState
                .CharacterCreationFoundationDraft!;
            Assert.AreEqual(1L, draft.BaseContentRevision);
            CollectionAssert.AreEqual(
                expectedEffects.Select(effect => effect.EffectId).ToList(),
                draft.ProjectedEffects.Select(effect => effect.EffectId).ToList());
            Assert.IsTrue(draft.ProjectedEffects[0].EffectId.StartsWith(
                TirHumanElfVersionId + ":effect:",
                StringComparison.Ordinal));
            Assert.IsTrue(draft.ProjectedEffects[preview.NationalityVersion.Effects.Count]
                .EffectId.StartsWith(TirModuleId + ":effect:", StringComparison.Ordinal));

            CharacterCreationFoundationState resumed = Load(
                CreateService(new FileWorkspaceStore(directory)),
                id);
            Assert.AreEqual(
                CharacterCreationFoundationResumeStatuses.PendingDraft,
                resumed.ResumeStatus);
            Assert.IsNotNull(resumed.PendingDraft);
            Assert.IsFalse(resumed.PendingDraft.CharacterEffectsApplied);
            Assert.AreEqual(expectedUsed, resumed.LifeModuleBudget.Used);
            Assert.AreEqual(750m - expectedUsed, resumed.LifeModuleBudget.Remaining);
            Assert.IsTrue(resumed.LifeModuleBudget.IsExact);
            return draft;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CharacterCreationFoundationService CreateService(
        IWorkspaceStore store,
        ICharacterCreationFoundationApplyAuthority? authority = null)
    {
        return new CharacterCreationFoundationService(
            store,
            new XmlCharacterFileQueries(new CharacterFileService()),
            new FileSystemCharacterSourceDataResolver(CreateOverlays()),
            CreateCatalog(),
            authority ?? new CharacterCreationFoundationDraftApplyAuthority(store));
    }

    private static XmlLifeModulesCatalogService CreateCatalog()
    {
        return new XmlLifeModulesCatalogService(Path.Combine(
            FindCoreRoot(),
            "Chummer",
            "data",
            "lifemodules.xml"));
    }

    private static FileSystemContentOverlayCatalogService CreateOverlays()
    {
        string root = FindCoreRoot();
        return new FileSystemContentOverlayCatalogService(root, root, null);
    }

    private static LifeModuleLegalOptionDto GetTirNationality()
    {
        return CreateCatalog().GetOptionProjections("Nationality", ["RF"])
            .Single(option => option.ModuleId == TirModuleId);
    }

    private static CharacterCreationFoundationState Load(
        ICharacterCreationFoundationService service,
        CharacterWorkspaceId id)
    {
        CharacterCreationFoundationResult<CharacterCreationFoundationState> result =
            service.Load(new CharacterCreationFoundationLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static CharacterCreationFoundationPreview Preview(
        ICharacterCreationFoundationService service,
        CharacterCreationFoundationBinding binding,
        string moduleId,
        string versionId,
        string metatype = "Human")
    {
        LifeModuleLegalOptionDto selectedModule = CreateCatalog()
            .GetOptionProjections("Nationality", ["RF"])
            .Single(option => option.ModuleId == moduleId);
        LifeModuleVersionProjectionDto? selectedVersion = selectedModule.Versions
            .FirstOrDefault(version => version.VersionId == versionId);
        IReadOnlyDictionary<string, string> followUps = selectedModule.FollowUps
            .Concat(selectedVersion?.FollowUps ?? [])
            .ToDictionary(
                prompt => prompt.PromptId,
                prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue
                          ?? "Confirmed",
                StringComparer.Ordinal);
        CharacterCreationFoundationResult<CharacterCreationFoundationPreview> result =
            service.Preview(new CharacterCreationFoundationPreviewRequest(
                binding,
                metatype,
                new CharacterCreationFoundationSelection(moduleId, versionId),
                followUps));
        Assert.AreEqual(
            CharacterCreationFoundationOutcomes.Success,
            result.Outcome,
            string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> Confirm(
        ICharacterCreationFoundationService service,
        CharacterCreationFoundationPreview preview)
    {
        return service.Confirm(new CharacterCreationFoundationConfirmRequest(
            preview.Binding,
            preview.RequestedMetatype,
            preview.Selection,
            preview.PreviewDigest,
            ExplicitlyConfirmed: true,
            preview.FollowUpValues));
    }

    private static CharacterCreationBudgetState ExactBudget(decimal used)
    {
        return new CharacterCreationBudgetState(
            CharacterCreationBudgetIds.LifeModules,
            "Life Modules Karma",
            750,
            used,
            750 - used,
            IsExact: true,
            Blockers: [],
            Unit: "karma");
    }

    private static string CharacterXml(string metatype)
    {
        return $"<character><name>Foundation Runner</name><alias>Foundation</alias><metatype>{metatype}</metatype><buildmethod>{CharacterCreationBuildMethods.LifeModules}</buildmethod><createdversion>5.225.0</createdversion><appversion>5.225.0</appversion><karma>25</karma><nuyen>0</nuyen><created>False</created><settings>{CanonicalLifeModuleSettingsId}</settings></character>";
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "chummer-foundation-authority-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WorkspacePath(string directory, CharacterWorkspaceId id) =>
        Path.Combine(directory, "workspaces", $"{id.Value}.json");

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "lifemodules.xml")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate canonical lifemodules.xml.");
    }

    private sealed class ThrowingFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        private readonly FileWorkspaceStoreFaultStage _stage;

        public ThrowingFaultInjector(FileWorkspaceStoreFaultStage stage)
        {
            _stage = stage;
        }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == _stage)
                throw new IOException("Injected draft authority write fault.");
        }
    }

    private sealed class ConflictCapabilityStore :
        IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        private readonly InMemoryWorkspaceStore _inner = new();

        public int AtomicCalls { get; private set; }
        public int SuccessfulAtomicWrites { get; private set; }
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            string expectedAuxiliaryStateDigest,
            WorkspaceDocument document)
        {
            AtomicCalls++;
            return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Conflict);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) =>
            _inner.CreateWorkspaceDocument(document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            WorkspaceDocument document) => _inner.CreateWorkspaceDocument(owner, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document) => _inner.CreateWorkspaceDocument(id, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            WorkspaceDocument document) => _inner.CreateWorkspaceDocument(owner, id, document);

        public IReadOnlyList<WorkspaceStoreEntry> List() => _inner.List();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => _inner.List(owner);
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => _inner.Get(id);
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) =>
            _inner.Get(owner, id);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document) =>
            _inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document) =>
            _inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision) => _inner.SaveCheckpoint(id, expectedContentRevision);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision) =>
            _inner.SaveCheckpoint(owner, id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision) => _inner.Delete(id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision) => _inner.Delete(owner, id, expectedContentRevision);
    }
}
