using System.Xml.Linq;
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
    private const string CanonicalLifeModulesDigest =
        "sha256:e70a1484e29fb991a3b82ec7215770f1203dc3a065e7294e9f45140604904a66";
    private const string CanonicalSkillsDigest =
        "sha256:8425a4800fb8309e60ee3fa7e96ed8510f1dced0b578f95b9d43fc93e019fc84";
    private const string ComputerSkillId = "1c14bf0d-cc69-4126-9a95-1f2429c11aa5";
    private const string EtiquetteSkillId = "b20acd11-f102-40f3-a641-e3c420fbdb91";

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

    [TestMethod]
    public void Finalization_preview_compiles_exact_Tir_ledger_in_version_then_module_order()
    {
        string directory = CreateTempDirectory();
        try
        {
            CharacterWorkspaceId id = new("foundation-finalization-tir");
            string xml = CharacterXml("Human");
            FileWorkspaceStore store = new(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            CharacterCreationFoundationService service = CreateService(store);
            CharacterCreationFoundationPreview draftPreview = Preview(
                service,
                Load(service, id).Binding,
                TirModuleId,
                TirHumanElfVersionId);
            Assert.AreEqual(
                CharacterCreationFoundationOutcomes.Success,
                Confirm(service, draftPreview).Outcome);
            CharacterCreationFoundationState state = Load(service, id);
            CharacterCreationFoundationDraftLedger draft = state.PendingDraft!;
            string targetPath = WorkspacePath(directory, id);
            byte[] before = File.ReadAllBytes(targetPath);

            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>
                result = service.PreviewFinalization(
                    new CharacterCreationFoundationFinalizationPreviewRequest(
                        state.Binding,
                        draft.DraftRevision,
                        draft.DraftDigest));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            Assert.IsNotNull(result.Value);
            CharacterCreationFoundationFinalizationPreview preview = result.Value;
            Assert.IsFalse(preview.CanConfirm);
            Assert.IsFalse(preview.CanApply);
            Assert.IsFalse(preview.CharacterEffectsApplied);
            Assert.IsFalse(preview.CharacterCreated);
            Assert.IsFalse(preview.Compilation.IsCompleteLedgerSupported);
            Assert.AreEqual(10, preview.Compilation.Effects.Count);
            CollectionAssert.AreEqual(
                Enumerable.Range(1, 10).ToList(),
                preview.Compilation.Effects.Select(effect => effect.Order).ToList());
            CollectionAssert.AreEqual(
                new[]
                {
                    "skilllevel",
                    "attributelevel",
                    "knowledgeskilllevel",
                    "knowledgeskilllevel",
                    "knowledgeskilllevel",
                    "knowledgeskilllevel",
                    "skilllevel",
                    "pushtext",
                    "freenegativequalities",
                    "qualitylevel"
                },
                preview.Compilation.Effects.Select(effect => effect.EffectKind).ToArray());
            Assert.IsTrue(preview.Compilation.Effects.Take(2).All(effect =>
                effect.SourcePhase == CharacterCreationFoundationEffectSourcePhases.Version));
            Assert.IsTrue(preview.Compilation.Effects.Skip(2).All(effect =>
                effect.SourcePhase == CharacterCreationFoundationEffectSourcePhases.Module));
            CharacterCreationFoundationEffectInstruction[] supportedEffects = preview
                .Compilation.Effects.Where(effect => effect.CompilationStatus
                    == CharacterCreationFoundationEffectCompilationStatuses.Supported)
                .ToArray();
            Assert.HasCount(5, supportedEffects);
            CharacterCreationFoundationEffectInstruction supportedAttribute = supportedEffects
                .Single(effect => effect.EffectKind == "attributelevel");
            Assert.AreEqual("attributelevel", supportedAttribute.EffectKind);
            Assert.AreEqual(CharacterCreationFoundationEffectSourcePhases.Version,
                supportedAttribute.SourcePhase);
            Assert.IsTrue(supportedEffects.Skip(1).All(effect =>
                effect.EffectKind == "knowledgeskilllevel"
                && effect.SourcePhase == CharacterCreationFoundationEffectSourcePhases.Module
                && effect.TargetBinding?.TargetKind == "free-knowledge-skill-pool"
                && effect.TargetBinding.SourceId == "FreeKnowledgeSkills"
                && effect.TargetBinding.SourceDigest == draft.SourceDigest));
            Assert.IsTrue(preview.Compilation.Effects
                .Except(supportedEffects)
                .All(effect => effect.CompilationStatus
                    == CharacterCreationFoundationEffectCompilationStatuses.Unsupported));
            Assert.IsTrue(preview.Compilation.Effects.All(effect =>
                effect.SourceAnchorIds.Count > 0
                && effect.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
                && IsCanonicalDigest(effect.InstructionDigest)));
            Assert.AreEqual(1, preview.Compilation.Requirements.Count);
            Assert.AreEqual(
                CharacterCreationFoundationEffectCompilationStatuses.Supported,
                preview.Compilation.Requirements[0].CompilationStatus);
            CollectionAssert.Contains(
                preview.FinalizationBlocked.ToList(),
                CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete);
            CollectionAssert.Contains(
                preview.FinalizationBlocked.ToList(),
                CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
            CollectionAssert.DoesNotContain(
                preview.FinalizationBlocked.ToList(),
                CharacterCreationFoundationBlockers.FinalizationPromptRequired);
            Assert.IsTrue(IsCanonicalDigest(preview.Compilation.CompilerRuntimeDigest));
            Assert.IsTrue(IsCanonicalDigest(preview.Compilation.CompilationDigest));
            Assert.IsTrue(IsCanonicalDigest(preview.PreviewDigest));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));

            CharacterCreationFoundationService reopenedService = CreateService(
                new FileWorkspaceStore(directory));
            CharacterCreationFoundationState reopenedState = Load(reopenedService, id);
            CharacterCreationFoundationFinalizationPreview reopened = reopenedService
                .PreviewFinalization(new CharacterCreationFoundationFinalizationPreviewRequest(
                    reopenedState.Binding,
                    reopenedState.PendingDraft!.DraftRevision,
                    reopenedState.PendingDraft.DraftDigest))
                .Value!;
            Assert.AreEqual(preview.PreviewDigest, reopened.PreviewDigest);
            Assert.AreEqual(
                preview.Compilation.CompilationDigest,
                reopened.Compilation.CompilationDigest);
            Assert.AreEqual(
                preview.Compilation.CompilerRuntimeDigest,
                reopened.Compilation.CompilerRuntimeDigest);
            Assert.AreEqual(xml, store.Get(id).Value!.Document.Content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Attributelevel_write_plan_matches_legacy_quality_graph_and_is_deterministic()
    {
        const string moduleId = "110f9504-0a54-4fd3-95f2-e24f744ff439";
        const string versionId = "35673f69-3fc1-4c89-801b-71920ff06874";
        string directory = CreateTempDirectory();
        try
        {
            string catalogPath = Path.Combine(directory, "lifemodules.xml");
            File.WriteAllText(
                catalogPath,
                "<chummer><stages><stage order=\"1\">Nationality</stage></stages>"
                + "<modules><module>"
                + $"<id>{moduleId}</id><stage>Nationality</stage><category>LifeModule</category>"
                + "<name>Writer Fixture</name><karma>15</karma><source>RF</source><page>77</page>"
                + $"<versions><version><id>{versionId}</id><name>Writer Version</name>"
                + "<bonus><attributelevel><name>LOG</name><val>2.00</val></attributelevel></bonus>"
                + "</version></versions>"
                + "<bonus><attributelevel><name>CHA</name></attributelevel>"
                + "<attributelevel><name>INT</name><val>999999999999999999999</val></attributelevel>"
                + "</bonus>"
                + "</module></modules></chummer>");
            XmlLifeModulesCatalogService catalog = new(catalogPath);
            LifeModuleLegalOptionDto module = catalog
                .GetOptionProjections("Nationality", ["RF"])
                .Single();
            LifeModuleVersionProjectionDto version = module.Versions.Single();
            LifeModuleEffectProjectionDto[] effects =
            [
                .. version.Effects,
                .. module.Effects
            ];
            CharacterWorkspaceId workspaceId = new("attributelevel-write-plan");
            var draft = new CharacterCreationFoundationDraftLedger(
                Schema: CharacterCreationFoundationSchemas.DraftLedgerV1,
                WorkspaceId: workspaceId,
                DraftRevision: 1,
                BaseContentRevision: 1,
                BaseRawCharacterXmlDigest: "sha256:" + new string('1', 64),
                SourceDigest: catalog.GetAuthority().RawXmlDigest,
                RequestedMetatype: "Human",
                Selection: new CharacterCreationFoundationSelection(moduleId, versionId),
                RequirementEvaluations: [],
                ProjectedEffects: effects,
                FollowUpValues: new Dictionary<string, string>(),
                SourceAnchorIds: version.SourceAnchorIds,
                CompilationStatus: CharacterCreationFoundationDraftStatuses.PendingFinalization,
                CharacterEffectsApplied: false,
                DraftDigest: string.Empty);
            draft = draft with
            {
                DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft)
            };
            XElement effectiveSource = new(
                "version",
                new XElement("id", versionId),
                new XElement("name", version.Label),
                new XElement("karma", version.KarmaRaw),
                new XElement("category", "LifeModule"),
                new XElement("source", version.Source),
                new XElement("page", version.PageReference),
                new XElement("stage", module.StageId),
                new XElement("notesColor", "Chocolate"),
                new XElement(
                    "bonus",
                    effects.Select(effect => XElement.Parse(effect.RawXml))));

            CharacterCreationFoundationEffectCompilation compilation =
                CharacterCreationFoundationEffectCompiler.Compile(
                    RulesetDefaults.Sr5,
                    draft,
                    module,
                    version);
            CharacterCreationFoundationEffectWritePlanResult first =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    workspaceId,
                    RulesetDefaults.Sr5,
                    draft,
                    module,
                    version,
                    effectiveSource.ToString(SaveOptions.DisableFormatting),
                    catalog.GetAuthority().RawXmlDigest,
                    "Chocolate");
            CharacterCreationFoundationEffectWritePlanResult duplicate =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    workspaceId,
                    RulesetDefaults.Sr5,
                    draft,
                    module,
                    version,
                    effectiveSource.ToString(SaveOptions.DisableFormatting),
                    catalog.GetAuthority().RawXmlDigest,
                    "Chocolate");

            Assert.IsFalse(compilation.IsCompleteLedgerSupported);
            CollectionAssert.Contains(
                compilation.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete);
            Assert.IsTrue(first.IsReady, string.Join(",", first.Blockers));
            Assert.IsTrue(duplicate.IsReady, string.Join(",", duplicate.Blockers));
            CharacterCreationFoundationEffectWritePlan plan = first.Plan!;
            Assert.AreEqual(plan.PlanDigest, duplicate.Plan!.PlanDigest);
            Assert.AreEqual(plan.QualityId, duplicate.Plan.QualityId);
            Assert.AreEqual(plan.QualityXml, duplicate.Plan.QualityXml);
            CollectionAssert.AreEqual(
                plan.ImprovementXml.ToList(),
                duplicate.Plan.ImprovementXml.ToList());
            Assert.IsTrue(Guid.TryParseExact(plan.QualityId, "D", out Guid qualityId));
            Assert.AreNotEqual(Guid.Empty, qualityId);
            Assert.IsTrue(IsCanonicalDigest(plan.WriterRuntimeDigest));
            Assert.IsTrue(IsCanonicalDigest(plan.PlanDigest));
            Assert.AreEqual(workspaceId, plan.WorkspaceId);
            Assert.AreEqual(draft.DraftRevision, plan.DraftRevision);
            Assert.AreEqual(draft.DraftDigest, plan.DraftDigest);
            Assert.AreEqual(compilation.CompilationDigest, plan.CompilationDigest);
            Assert.AreEqual(catalog.GetAuthority().RawXmlDigest, plan.SourceAuthorityDigest);
            Assert.AreEqual(versionId, plan.SourceId);
            CollectionAssert.AreEqual(
                compilation.Effects.Select(effect => effect.InstructionDigest).ToList(),
                plan.InstructionDigests.ToList());
            CollectionAssert.AreEqual(
                compilation.Effects.Select(effect => effect.Order).ToList(),
                plan.EffectProvenance.Select(effect => effect.Order).ToList());
            CollectionAssert.AreEqual(
                new[]
                {
                    CharacterCreationFoundationEffectSourcePhases.Version,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module
                },
                plan.EffectProvenance.Select(effect => effect.SourcePhase).ToArray());
            Assert.IsTrue(plan.EffectProvenance.Zip(compilation.Effects).All(pair =>
                pair.First.EffectId == pair.Second.EffectId
                && pair.First.InstructionDigest == pair.Second.InstructionDigest
                && pair.First.SourceAnchorIds.SequenceEqual(
                    pair.Second.SourceAnchorIds,
                    StringComparer.Ordinal)));

            XElement quality = XElement.Parse(plan.QualityXml);
            CollectionAssert.AreEqual(
                new[]
                {
                    "sourceid", "guid", "name", "extra", "bp", "implemented",
                    "contributetobp", "contributetolimit", "stagedpurchase", "doublecareer",
                    "canbuywithspellpoints", "metagenic", "print", "qualitytype",
                    "qualitysource", "mutant", "source", "page", "sourcename", "bonus",
                    "firstlevelbonus", "naturalweapons", "notes", "notesColor", "stage"
                },
                quality.Elements().Select(element => element.Name.LocalName).ToArray());
            Assert.AreEqual(versionId, quality.Element("sourceid")!.Value);
            Assert.AreEqual(plan.QualityId, quality.Element("guid")!.Value);
            Assert.AreEqual("Writer Version", quality.Element("name")!.Value);
            Assert.AreEqual("15", quality.Element("bp")!.Value);
            Assert.AreEqual("True", quality.Element("implemented")!.Value);
            Assert.AreEqual("False", quality.Element("stagedpurchase")!.Value);
            Assert.AreEqual("LifeModule", quality.Element("qualitytype")!.Value);
            Assert.AreEqual("LifeModule", quality.Element("qualitysource")!.Value);
            Assert.AreEqual("RF", quality.Element("source")!.Value);
            Assert.AreEqual("77", quality.Element("page")!.Value);
            Assert.AreEqual("Chocolate", quality.Element("notesColor")!.Value);
            Assert.AreEqual("Nationality", quality.Element("stage")!.Value);
            CollectionAssert.AreEqual(
                new[] { "LOG", "CHA", "INT" },
                quality.Element("bonus")!.Elements()
                    .Select(effect => effect.Element("name")!.Value)
                    .ToArray());

            Assert.AreEqual(3, plan.ImprovementXml.Count);
            XElement firstImprovement = XElement.Parse(plan.ImprovementXml[0]);
            XElement secondImprovement = XElement.Parse(plan.ImprovementXml[1]);
            XElement thirdImprovement = XElement.Parse(plan.ImprovementXml[2]);
            CollectionAssert.AreEqual(
                new[]
                {
                    "target", "improvedname", "sourcename", "min", "max", "aug",
                    "augmax", "val", "rating", "exclude", "condition", "improvementttype",
                    "improvementsource", "custom", "customname", "customid", "customgroup",
                    "addtorating", "enabled", "order", "notes", "notesColor"
                },
                firstImprovement.Elements().Select(element => element.Name.LocalName).ToArray());
            Assert.AreEqual("LOG", firstImprovement.Element("improvedname")!.Value);
            Assert.AreEqual("2", firstImprovement.Element("val")!.Value);
            Assert.AreEqual("CHA", secondImprovement.Element("improvedname")!.Value);
            Assert.AreEqual("1", secondImprovement.Element("val")!.Value);
            Assert.AreEqual("INT", thirdImprovement.Element("improvedname")!.Value);
            Assert.AreEqual("1", thirdImprovement.Element("val")!.Value);
            Assert.AreEqual(
                2,
                CharacterCreationFoundationEffectCompiler.ParseLegacyAttributeLevelValue("2.00"));
            Assert.AreEqual(
                1,
                CharacterCreationFoundationEffectCompiler.ParseLegacyAttributeLevelValue(null));
            Assert.AreEqual(
                1,
                CharacterCreationFoundationEffectCompiler.ParseLegacyAttributeLevelValue(
                    "999999999999999999999"));
            Assert.IsTrue(plan.ImprovementXml.All(xml =>
                XElement.Parse(xml).Element("sourcename")!.Value == plan.QualityId));
            Assert.IsTrue(plan.ImprovementXml.All(xml =>
                XElement.Parse(xml).Element("improvementttype")!.Value == "Attributelevel"
                && XElement.Parse(xml).Element("improvementsource")!.Value == "Quality"));

            XElement tamperedSource = new(effectiveSource);
            tamperedSource.Element("bonus")!.Elements().First().Element("val")!.Value = "3";
            CharacterCreationFoundationEffectWritePlanResult tampered =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    workspaceId,
                    RulesetDefaults.Sr5,
                    draft,
                    module,
                    version,
                    tamperedSource.ToString(SaveOptions.DisableFormatting),
                    catalog.GetAuthority().RawXmlDigest,
                    "Chocolate");
            Assert.IsFalse(tampered.IsReady);
            Assert.IsNull(tampered.Plan);
            CollectionAssert.Contains(
                tampered.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);

            CharacterCreationFoundationEffectWritePlanResult wrongWorkspace =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    new CharacterWorkspaceId("attributelevel-write-plan-other"),
                    RulesetDefaults.Sr5,
                    draft,
                    module,
                    version,
                    effectiveSource.ToString(SaveOptions.DisableFormatting),
                    catalog.GetAuthority().RawXmlDigest,
                    "Chocolate");
            CharacterCreationFoundationEffectWritePlanResult wrongSourceDigest =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    workspaceId,
                    RulesetDefaults.Sr5,
                    draft,
                    module,
                    version,
                    effectiveSource.ToString(SaveOptions.DisableFormatting),
                    "sha256:" + new string('0', 64),
                    "Chocolate");
            Assert.IsFalse(wrongWorkspace.IsReady);
            Assert.IsNull(wrongWorkspace.Plan);
            Assert.IsFalse(wrongSourceDigest.IsReady);
            Assert.IsNull(wrongSourceDigest.Plan);
            CollectionAssert.Contains(
                wrongWorkspace.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
            CollectionAssert.Contains(
                wrongSourceDigest.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Canonical_named_life_module_skilllevels_all_resolve_to_digest_bound_active_skills()
    {
        string coreRoot = FindCoreRoot();
        string lifeModulesXml = File.ReadAllText(
            Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml"));
        string skillsXml = File.ReadAllText(
            Path.Combine(coreRoot, "Chummer", "data", "skills.xml"));
        Assert.AreEqual(
            CanonicalLifeModulesDigest,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                lifeModulesXml));
        Assert.AreEqual(
            CanonicalSkillsDigest,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                skillsXml));
        Assert.IsTrue(CharacterCreationFoundationSkillSourceAuthority.TryCreate(
            skillsXml,
            CanonicalSkillsDigest,
            out CharacterCreationFoundationSkillSourceAuthority? authority));

        XElement lifeModules = XElement.Parse(lifeModulesXml, LoadOptions.None);
        string[] namedOccurrences = lifeModules
            .Descendants("skilllevel")
            .Select(effect => effect.Element("name")?.Value)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        Assert.HasCount(1216, namedOccurrences);
        Assert.AreEqual(65, namedOccurrences.Distinct(StringComparer.Ordinal).Count());

        foreach (string canonicalName in namedOccurrences)
        {
            Assert.IsTrue(
                authority!.TryResolveExactActive(
                    canonicalName,
                    out CharacterCreationFoundationEffectTargetBinding? binding),
                canonicalName);
            Assert.IsNotNull(binding);
            Assert.AreEqual("active-skill", binding.TargetKind);
            Assert.AreEqual(canonicalName, binding.CanonicalName);
            Assert.AreEqual(CanonicalSkillsDigest, binding.SourceDigest);
            Assert.IsTrue(Guid.TryParseExact(binding.SourceId, "D", out Guid sourceId));
            Assert.AreNotEqual(Guid.Empty, sourceId);
        }
    }

    [TestMethod]
    public void Canonical_knowledge_skill_levels_are_digest_bound_pool_grants_or_fail_closed_prompts()
    {
        string coreRoot = FindCoreRoot();
        string lifeModulesXml = File.ReadAllText(
            Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml"));
        string skillsXml = File.ReadAllText(
            Path.Combine(coreRoot, "Chummer", "data", "skills.xml"));
        Assert.AreEqual(
            CanonicalLifeModulesDigest,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                lifeModulesXml));
        Assert.AreEqual(
            CanonicalSkillsDigest,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                skillsXml));

        XElement canonicalLifeModules = XElement.Parse(lifeModulesXml, LoadOptions.None);
        XElement canonicalSkills = XElement.Parse(skillsXml, LoadOptions.None);
        string[] literalKnowledgeNames = canonicalLifeModules
            .Descendants("knowledgeskilllevel")
            .Select(effect => effect.Element("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                           && !name.Contains("[", StringComparison.Ordinal)
                           && !name.Contains("]", StringComparison.Ordinal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        HashSet<string> catalogKnowledgeNames = canonicalSkills.Element("knowledgeskills")!
            .Elements("skill")
            .Select(skill => skill.Element("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.HasCount(168, literalKnowledgeNames);
        Assert.AreEqual(
            113,
            literalKnowledgeNames.Count(name => !catalogKnowledgeNames.Contains(name)));

        XmlLifeModulesCatalogService catalog = CreateCatalog();
        var compiledByEffectId = new Dictionary<
            string,
            CharacterCreationFoundationEffectInstruction>(StringComparer.Ordinal);
        foreach (LifeModuleLegalOptionDto module in catalog.GetOptionProjections())
        {
            LifeModuleVersionProjectionDto?[] versions = module.Versions.Count == 0
                ? [null]
                : module.Versions.Cast<LifeModuleVersionProjectionDto?>().ToArray();
            foreach (LifeModuleVersionProjectionDto? version in versions)
            {
                LifeModuleEffectProjectionDto[] effects =
                [
                    .. version?.Effects ?? [],
                    .. module.Effects
                ];
                LifeModuleRequirementProjectionDto[] requirements =
                [
                    .. module.Requirements,
                    .. version?.Requirements ?? []
                ];
                var workspaceId = new CharacterWorkspaceId(
                    $"canonical-knowledge-{module.ModuleId}-{version?.VersionId ?? "base"}");
                var ledger = new CharacterCreationFoundationDraftLedger(
                    Schema: CharacterCreationFoundationSchemas.DraftLedgerV1,
                    WorkspaceId: workspaceId,
                    DraftRevision: 1,
                    BaseContentRevision: 1,
                    BaseRawCharacterXmlDigest: "sha256:" + new string('1', 64),
                    SourceDigest: CanonicalLifeModulesDigest,
                    RequestedMetatype: "Human",
                    Selection: new CharacterCreationFoundationSelection(
                        module.ModuleId,
                        version?.VersionId),
                    RequirementEvaluations: requirements,
                    ProjectedEffects: effects,
                    FollowUpValues: new Dictionary<string, string>(),
                    SourceAnchorIds: version?.SourceAnchorIds ?? module.SourceAnchorIds,
                    CompilationStatus:
                        CharacterCreationFoundationDraftStatuses.PendingFinalization,
                    CharacterEffectsApplied: false,
                    DraftDigest: string.Empty);
                ledger = ledger with
                {
                    DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(
                        ledger)
                };

                CharacterCreationFoundationEffectCompilation compilation =
                    CharacterCreationFoundationEffectCompiler.Compile(
                        RulesetDefaults.Sr5,
                        ledger,
                        module,
                        version);
                foreach (CharacterCreationFoundationEffectInstruction instruction in compilation
                             .Effects.Where(effect =>
                                 effect.EffectKind == "knowledgeskilllevel"))
                {
                    if (compiledByEffectId.TryGetValue(
                            instruction.EffectId,
                            out CharacterCreationFoundationEffectInstruction? prior))
                    {
                        Assert.AreEqual(
                            prior.CompilationStatus,
                            instruction.CompilationStatus);
                        Assert.AreEqual(prior.TargetBinding, instruction.TargetBinding);
                        CollectionAssert.AreEquivalent(
                            prior.PromptIds.ToList(),
                            instruction.PromptIds.ToList());
                    }
                    else
                    {
                        compiledByEffectId.Add(instruction.EffectId, instruction);
                    }
                }
            }
        }

        Assert.HasCount(506, compiledByEffectId);
        CharacterCreationFoundationEffectInstruction[] supported = compiledByEffectId.Values
            .Where(effect => effect.CompilationStatus
                == CharacterCreationFoundationEffectCompilationStatuses.Supported)
            .ToArray();
        CharacterCreationFoundationEffectInstruction[] prompts = compiledByEffectId.Values
            .Where(effect => effect.CompilationStatus
                == CharacterCreationFoundationEffectCompilationStatuses.PromptRequired)
            .ToArray();
        Assert.HasCount(250, supported);
        Assert.HasCount(256, prompts);
        Assert.IsFalse(compiledByEffectId.Values.Any(effect => effect.CompilationStatus
            == CharacterCreationFoundationEffectCompilationStatuses.Unsupported));
        Assert.AreEqual(12, prompts.Count(effect => effect.PromptIds.Count == 0));

        foreach (CharacterCreationFoundationEffectInstruction instruction in supported)
        {
            Assert.IsNotNull(instruction.TargetBinding);
            Assert.AreEqual(
                "free-knowledge-skill-pool",
                instruction.TargetBinding.TargetKind);
            Assert.AreEqual("FreeKnowledgeSkills", instruction.TargetBinding.SourceId);
            Assert.AreEqual("FreeKnowledgeSkills", instruction.TargetBinding.CanonicalName);
            Assert.AreEqual(CanonicalLifeModulesDigest, instruction.TargetBinding.SourceDigest);
            Assert.AreEqual(
                instruction.TargetId,
                instruction.IgnoredSourceMetadata["legacy-ignored-literal-name"]);
            if (instruction.Parameters.TryGetValue("group", out string? group))
            {
                Assert.AreEqual(
                    group,
                    instruction.IgnoredSourceMetadata["legacy-ignored-literal-group"]);
            }
            if (instruction.Parameters.TryGetValue("spec", out string? spec))
            {
                Assert.AreEqual(
                    spec,
                    instruction.IgnoredSourceMetadata["legacy-ignored-literal-spec"]);
            }
            if (instruction.Parameters.TryGetValue("val", out string? rawValue))
            {
                Assert.IsTrue(decimal.TryParse(
                    rawValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _));
            }
        }
    }

    [TestMethod]
    public void Tir_and_State_University_level_plans_share_one_quality_and_preserve_duplicate_order()
    {
        string directory = CreateTempDirectory();
        try
        {
            string skillsXml = File.ReadAllText(
                Path.Combine(FindCoreRoot(), "Chummer", "data", "skills.xml"));
            LevelWritePlanFixture tir = CreateLevelWritePlanFixture(
                Path.Combine(directory, "tir"),
                workspaceValue: "tir-level-plan",
                moduleId: TirModuleId,
                moduleName: "Tír Tairngire",
                stage: "Nationality",
                karma: 15,
                page: "67",
                versionId: TirHumanElfVersionId,
                versionName: "Elves/Humans",
                versionBonusXml:
                    "<skilllevel><name>Computer</name><val>2</val></skilllevel>"
                    + "<attributelevel><name>CHA</name></attributelevel>",
                moduleBonusXml:
                    "<knowledgeskilllevel><name>Sperethiel</name><group>Language</group>"
                    + "<val>0</val></knowledgeskilllevel>"
                    + "<knowledgeskilllevel><name>English</name><group>Language</group>"
                    + "<val>2</val></knowledgeskilllevel>"
                    + "<knowledgeskilllevel><name>History</name><group>Academic</group>"
                    + "</knowledgeskilllevel>"
                    + "<knowledgeskilllevel><name>Tír Tairngire</name><group>Street</group>"
                    + "</knowledgeskilllevel>"
                    + "<skilllevel><name>Etiquette</name></skilllevel>");
            CharacterCreationFoundationEffectWritePlanResult tirResult =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    tir.WorkspaceId,
                    RulesetDefaults.Sr5,
                    tir.Ledger,
                    tir.Module,
                    tir.Version,
                    tir.EffectiveSourceXml,
                    tir.SourceDigest,
                    "Chocolate",
                    skillsXml,
                    CanonicalSkillsDigest);

            Assert.IsTrue(tirResult.IsReady, string.Join(",", tirResult.Blockers));
            CharacterCreationFoundationEffectWritePlan tirPlan = tirResult.Plan!;
            Assert.AreEqual(CanonicalSkillsDigest, tirPlan.SkillSourceAuthorityDigest);
            Assert.AreEqual(TirHumanElfVersionId, tirPlan.SourceId);
            CollectionAssert.AreEqual(
                new[]
                {
                    CharacterCreationFoundationEffectSourcePhases.Version,
                    CharacterCreationFoundationEffectSourcePhases.Version,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module
                },
                tirPlan.EffectProvenance.Select(effect => effect.SourcePhase).ToArray());
            CollectionAssert.AreEqual(
                new[] { "Computer", "CHA", "", "", "", "", "Etiquette" },
                tirPlan.ImprovementXml.Select(xml =>
                    XElement.Parse(xml).Element("improvedname")!.Value).ToArray());
            CollectionAssert.AreEqual(
                new[]
                {
                    "SkillLevel", "Attributelevel", "FreeKnowledgeSkills",
                    "FreeKnowledgeSkills", "FreeKnowledgeSkills", "FreeKnowledgeSkills",
                    "SkillLevel"
                },
                tirPlan.ImprovementXml.Select(xml =>
                    XElement.Parse(xml).Element("improvementttype")!.Value).ToArray());
            CollectionAssert.AreEqual(
                new[] { "2", "1", "0", "2", "1", "1", "1" },
                tirPlan.ImprovementXml.Select(xml =>
                    XElement.Parse(xml).Element("val")!.Value).ToArray());
            Assert.AreEqual(ComputerSkillId, tirPlan.EffectProvenance[0].TargetBinding!.SourceId);
            Assert.IsNull(tirPlan.EffectProvenance[1].TargetBinding);
            Assert.IsTrue(tirPlan.EffectProvenance.Skip(2).Take(4).All(effect =>
                effect.TargetBinding?.SourceId == "FreeKnowledgeSkills"
                && effect.TargetBinding.SourceDigest == tir.SourceDigest));
            Assert.AreEqual(EtiquetteSkillId, tirPlan.EffectProvenance[6].TargetBinding!.SourceId);
            Assert.IsTrue(tirPlan.ImprovementXml.All(xml =>
                XElement.Parse(xml).Element("sourcename")!.Value == tirPlan.QualityId));

            LevelWritePlanFixture state = CreateLevelWritePlanFixture(
                Path.Combine(directory, "state"),
                workspaceValue: "state-mathematics-level-plan",
                moduleId: "142d7a1b-c676-4bf6-a71f-bc2d29617a14",
                moduleName: "State University",
                stage: "Further Education",
                karma: 65,
                page: "73",
                versionId: "dd5b8431-a443-483d-845f-86c5b9aea4a1",
                versionName: "Mathematics",
                versionBonusXml:
                    "<skilllevel><name>Computer</name></skilllevel>"
                    + "<skilllevel><name>Etiquette</name></skilllevel>"
                    + "<skilllevel><name>Software</name><val>2</val></skilllevel>"
                    + "<knowledgeskilllevel><name>Mathematics</name>"
                    + "<group>Academic</group><val>5</val></knowledgeskilllevel>",
                moduleBonusXml:
                    "<attributelevel><name>LOG</name></attributelevel>"
                    + "<attributelevel><name>WIL</name></attributelevel>"
                    + "<skilllevel><name>Computer</name></skilllevel>"
                    + "<skilllevel><name>Perception</name></skilllevel>"
                    + "<skilllevel><name>Etiquette</name></skilllevel>"
                    + "<knowledgeskilllevel><name>Mathematics</name>"
                    + "<group>Academic</group><val>4</val></knowledgeskilllevel>");
            CharacterCreationFoundationEffectWritePlanResult stateResult =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    state.WorkspaceId,
                    RulesetDefaults.Sr5,
                    state.Ledger,
                    state.Module,
                    state.Version,
                    state.EffectiveSourceXml,
                    state.SourceDigest,
                    "Chocolate",
                    skillsXml,
                    CanonicalSkillsDigest);

            Assert.IsTrue(stateResult.IsReady, string.Join(",", stateResult.Blockers));
            CharacterCreationFoundationEffectWritePlan statePlan = stateResult.Plan!;
            Assert.HasCount(10, statePlan.ImprovementXml);
            CollectionAssert.AreEqual(
                new[]
                {
                    "Computer", "Etiquette", "Software", "", "LOG", "WIL",
                    "Computer", "Perception", "Etiquette", ""
                },
                statePlan.ImprovementXml.Select(xml =>
                    XElement.Parse(xml).Element("improvedname")!.Value).ToArray());
            CollectionAssert.AreEqual(
                new[]
                {
                    CharacterCreationFoundationEffectSourcePhases.Version,
                    CharacterCreationFoundationEffectSourcePhases.Version,
                    CharacterCreationFoundationEffectSourcePhases.Version,
                    CharacterCreationFoundationEffectSourcePhases.Version,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module,
                    CharacterCreationFoundationEffectSourcePhases.Module
                },
                statePlan.EffectProvenance.Select(effect => effect.SourcePhase).ToArray());
            Assert.AreEqual(2, statePlan.ImprovementXml.Count(xml =>
                XElement.Parse(xml).Element("improvedname")!.Value == "Computer"));
            Assert.AreEqual(2, statePlan.ImprovementXml.Count(xml =>
                XElement.Parse(xml).Element("improvedname")!.Value == "Etiquette"));
            Assert.AreEqual(
                ComputerSkillId,
                statePlan.EffectProvenance[0].TargetBinding!.SourceId);
            Assert.AreEqual(
                ComputerSkillId,
                statePlan.EffectProvenance[6].TargetBinding!.SourceId);
            Assert.AreNotEqual(
                statePlan.EffectProvenance[0].InstructionDigest,
                statePlan.EffectProvenance[6].InstructionDigest);
            Assert.AreEqual(2, statePlan.ImprovementXml.Count(xml =>
                XElement.Parse(xml).Element("improvementttype")!.Value
                == "FreeKnowledgeSkills"));
            Assert.AreNotEqual(
                statePlan.EffectProvenance[3].InstructionDigest,
                statePlan.EffectProvenance[9].InstructionDigest);
            Assert.IsTrue(new[] { 3, 9 }.All(index =>
                statePlan.EffectProvenance[index].TargetBinding?.SourceId
                == "FreeKnowledgeSkills"));
            Assert.IsTrue(statePlan.ImprovementXml.All(xml =>
                XElement.Parse(xml).Element("sourcename")!.Value == statePlan.QualityId));
            Assert.AreEqual(
                1,
                XElement.Parse(statePlan.QualityXml).Elements("guid").Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Skilllevel_plan_records_ignored_literal_spec_and_fails_closed_for_other_shapes()
    {
        string directory = CreateTempDirectory();
        try
        {
            string skillsXml = File.ReadAllText(
                Path.Combine(FindCoreRoot(), "Chummer", "data", "skills.xml"));
            LevelWritePlanFixture literal = CreateLevelWritePlanFixture(
                Path.Combine(directory, "literal"),
                "literal-spec-level-plan",
                TirModuleId,
                "Tír Tairngire",
                "Nationality",
                15,
                "67",
                TirHumanElfVersionId,
                "Elves/Humans",
                "<skilllevel><name>Computer</name><spec>Matrix Search</spec>"
                + "<val>2.00</val></skilllevel>",
                string.Empty);
            CharacterCreationFoundationEffectWritePlanResult literalResult =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    literal.WorkspaceId,
                    RulesetDefaults.Sr5,
                    literal.Ledger,
                    literal.Module,
                    literal.Version,
                    literal.EffectiveSourceXml,
                    literal.SourceDigest,
                    "Chocolate",
                    skillsXml,
                    CanonicalSkillsDigest);

            Assert.IsTrue(literalResult.IsReady, string.Join(",", literalResult.Blockers));
            CharacterCreationFoundationEffectWriteProvenance provenance =
                literalResult.Plan!.EffectProvenance.Single();
            Assert.AreEqual(
                "Matrix Search",
                provenance.IgnoredSourceMetadata["legacy-ignored-literal-spec"]);
            Assert.AreEqual(
                "2",
                XElement.Parse(literalResult.Plan.ImprovementXml.Single())
                    .Element("val")!.Value);

            string[] rejectedEffects =
            [
                "<skilllevel name=\"Computer\" />",
                "<skilllevel><name /></skilllevel>",
                "<skilllevel><selectskill /></skilllevel>",
                "<skilllevel><options><computer>Computer</computer></options></skilllevel>",
                "<skilllevel><name>Computer</name><spec>[Any]</spec></skilllevel>",
                "<skilllevel><name>Not A Canonical Skill</name></skilllevel>",
                "<skilllevel><name>Pilot Exotic Vehicle</name></skilllevel>",
                "<skilllevel><name>Administration</name></skilllevel>",
                "<skilllevel xmlns=\"urn:unsupported\"><name>Computer</name></skilllevel>"
            ];
            for (int index = 0; index < rejectedEffects.Length; index++)
            {
                LevelWritePlanFixture rejected = CreateLevelWritePlanFixture(
                    Path.Combine(directory, $"rejected-{index}"),
                    $"rejected-skilllevel-{index}",
                    TirModuleId,
                    "Tír Tairngire",
                    "Nationality",
                    15,
                    "67",
                    TirHumanElfVersionId,
                    "Elves/Humans",
                    rejectedEffects[index],
                    string.Empty);
                CharacterCreationFoundationEffectWritePlanResult rejectedResult =
                    CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                        rejected.WorkspaceId,
                        RulesetDefaults.Sr5,
                        rejected.Ledger,
                        rejected.Module,
                        rejected.Version,
                        rejected.EffectiveSourceXml,
                        rejected.SourceDigest,
                        "Chocolate",
                        skillsXml,
                        CanonicalSkillsDigest);
                Assert.IsFalse(rejectedResult.IsReady, rejectedEffects[index]);
                Assert.IsNull(rejectedResult.Plan, rejectedEffects[index]);
            }

            const string ambiguousSkillsXml =
                "<chummer><skills>"
                + "<skill><id>00000000-0000-0000-0000-000000000001</id>"
                + "<name>Computer</name></skill>"
                + "<skill><id>00000000-0000-0000-0000-000000000002</id>"
                + "<name>Computer</name></skill>"
                + "</skills></chummer>";
            string ambiguousDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeRawCharacterXmlDigest(ambiguousSkillsXml);
            Assert.IsTrue(CharacterCreationFoundationSkillSourceAuthority.TryCreate(
                ambiguousSkillsXml,
                ambiguousDigest,
                out CharacterCreationFoundationSkillSourceAuthority? ambiguousAuthority));
            Assert.IsFalse(ambiguousAuthority!.TryResolveExactActive(
                "Computer",
                out CharacterCreationFoundationEffectTargetBinding? ambiguousBinding));
            Assert.IsNull(ambiguousBinding);

            CharacterCreationFoundationDraftLedger mismatchedLedger = literal.Ledger with
            {
                ProjectedEffects = literal.Ledger.ProjectedEffects
                    .Select((effect, index) => index == 0
                        ? effect with { TargetId = "Hacking" }
                        : effect)
                    .ToArray(),
                DraftDigest = string.Empty
            };
            mismatchedLedger = mismatchedLedger with
            {
                DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(
                    mismatchedLedger)
            };
            CharacterCreationFoundationEffectWritePlanResult mismatchResult =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    literal.WorkspaceId,
                    RulesetDefaults.Sr5,
                    mismatchedLedger,
                    literal.Module,
                    literal.Version,
                    literal.EffectiveSourceXml,
                    literal.SourceDigest,
                    "Chocolate",
                    skillsXml,
                    CanonicalSkillsDigest);
            Assert.IsFalse(mismatchResult.IsReady);
            Assert.IsNull(mismatchResult.Plan);
            CollectionAssert.Contains(
                mismatchResult.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Knowledge_skill_pool_plan_preserves_decimal_and_metadata_and_fails_closed()
    {
        string directory = CreateTempDirectory();
        try
        {
            LevelWritePlanFixture literal = CreateLevelWritePlanFixture(
                Path.Combine(directory, "literal"),
                "literal-knowledge-pool-plan",
                TirModuleId,
                "Tír Tairngire",
                "Nationality",
                15,
                "67",
                TirHumanElfVersionId,
                "Elves/Humans",
                "<knowledgeskilllevel><name>Agriculture</name>"
                + "<group>Professional</group><spec>Urban Agriculture</spec>"
                + "<val> 2.50 </val></knowledgeskilllevel>",
                "<knowledgeskilllevel><name>Agriculture</name>"
                + "<group>Professional</group></knowledgeskilllevel>");
            CharacterCreationFoundationEffectWritePlanResult literalResult =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    literal.WorkspaceId,
                    RulesetDefaults.Sr5,
                    literal.Ledger,
                    literal.Module,
                    literal.Version,
                    literal.EffectiveSourceXml,
                    literal.SourceDigest,
                    "Chocolate");

            Assert.IsTrue(literalResult.IsReady, string.Join(",", literalResult.Blockers));
            CharacterCreationFoundationEffectWritePlan plan = literalResult.Plan!;
            Assert.HasCount(2, plan.ImprovementXml);
            CollectionAssert.AreEqual(
                new[] { "", "" },
                plan.ImprovementXml.Select(xml =>
                    XElement.Parse(xml).Element("improvedname")!.Value).ToArray());
            CollectionAssert.AreEqual(
                new[] { "2.50", "1" },
                plan.ImprovementXml.Select(xml =>
                    XElement.Parse(xml).Element("val")!.Value).ToArray());
            Assert.IsTrue(plan.ImprovementXml.All(xml =>
                XElement.Parse(xml).Element("improvementttype")!.Value
                == "FreeKnowledgeSkills"));
            Assert.IsTrue(plan.ImprovementXml.All(xml =>
            {
                XElement improvement = XElement.Parse(xml);
                return improvement.Element("sourcename")!.Value == plan.QualityId
                       && improvement.Element("improvementsource")!.Value == "Quality"
                       && improvement.Element("rating")!.Value == "1"
                       && improvement.Element("order")!.Value == "0"
                       && improvement.Element("unique") is null;
            }));
            Assert.IsTrue(plan.EffectProvenance.All(effect =>
                effect.TargetBinding?.TargetKind == "free-knowledge-skill-pool"
                && effect.TargetBinding.SourceId == "FreeKnowledgeSkills"
                && effect.TargetBinding.SourceDigest == literal.SourceDigest));
            Assert.AreEqual(
                "Agriculture",
                plan.EffectProvenance[0]
                    .IgnoredSourceMetadata["legacy-ignored-literal-name"]);
            Assert.AreEqual(
                "Professional",
                plan.EffectProvenance[0]
                    .IgnoredSourceMetadata["legacy-ignored-literal-group"]);
            Assert.AreEqual(
                "Urban Agriculture",
                plan.EffectProvenance[0]
                    .IgnoredSourceMetadata["legacy-ignored-literal-spec"]);
            Assert.AreNotEqual(
                plan.EffectProvenance[0].InstructionDigest,
                plan.EffectProvenance[1].InstructionDigest);
            Assert.AreEqual(
                2.50m,
                CharacterCreationFoundationEffectCompiler
                    .ParseLegacyKnowledgeSkillLevelValue(" 2.50 "));
            Assert.AreEqual(
                1m,
                CharacterCreationFoundationEffectCompiler
                    .ParseLegacyKnowledgeSkillLevelValue(null));
            Assert.AreEqual(
                0m,
                CharacterCreationFoundationEffectCompiler
                    .ParseLegacyKnowledgeSkillLevelValue("Rating"));

            string[] rejectedEffects =
            [
                "<knowledgeskilllevel name=\"History\" />",
                "<knowledgeskilllevel><name /></knowledgeskilllevel>",
                "<knowledgeskilllevel><val>2</val></knowledgeskilllevel>",
                "<knowledgeskilllevel><name>History</name><name>Law</name>"
                + "</knowledgeskilllevel>",
                "<knowledgeskilllevel><name>History</name><val>1</val><val>2</val>"
                + "</knowledgeskilllevel>",
                "<knowledgeskilllevel><name>History</name><val /></knowledgeskilllevel>",
                "<knowledgeskilllevel><name>History</name><val>Rating</val>"
                + "</knowledgeskilllevel>",
                "<knowledgeskilllevel><name>History</name><unknown>2</unknown>"
                + "</knowledgeskilllevel>",
                "<knowledgeskilllevel><name>[Any]</name><group>Academic</group>"
                + "</knowledgeskilllevel>",
                "<knowledgeskilllevel><name>Area Knowledge: [City]</name>"
                + "<group>Street</group></knowledgeskilllevel>",
                "<knowledgeskilllevel><options><history>History</history></options>"
                + "<group>Academic</group></knowledgeskilllevel>",
                "<knowledgeskilllevel><selectskill knowledgeskills=\"true\" />"
                + "</knowledgeskilllevel>",
                "<knowledgeskilllevel><name>History</name><group><options>"
                + "<academic>Academic</academic></options></group></knowledgeskilllevel>",
                "<knowledgeskilllevel xmlns=\"urn:unsupported\"><name>History</name>"
                + "</knowledgeskilllevel>"
            ];
            for (int index = 0; index < rejectedEffects.Length; index++)
            {
                LevelWritePlanFixture rejected = CreateLevelWritePlanFixture(
                    Path.Combine(directory, $"rejected-knowledge-{index}"),
                    $"rejected-knowledge-{index}",
                    TirModuleId,
                    "Tír Tairngire",
                    "Nationality",
                    15,
                    "67",
                    TirHumanElfVersionId,
                    "Elves/Humans",
                    rejectedEffects[index],
                    string.Empty);
                CharacterCreationFoundationEffectWritePlanResult rejectedResult =
                    CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                        rejected.WorkspaceId,
                        RulesetDefaults.Sr5,
                        rejected.Ledger,
                        rejected.Module,
                        rejected.Version,
                        rejected.EffectiveSourceXml,
                        rejected.SourceDigest,
                        "Chocolate");
                Assert.IsFalse(rejectedResult.IsReady, rejectedEffects[index]);
                Assert.IsNull(rejectedResult.Plan, rejectedEffects[index]);
                if (index is >= 8 and <= 12)
                {
                    Assert.IsTrue(
                        rejectedResult.Blockers.Contains(
                            CharacterCreationFoundationBlockers.FinalizationPromptRequired),
                        rejectedEffects[index]);
                }
            }

            CharacterCreationFoundationDraftLedger mismatchedLedger = literal.Ledger with
            {
                ProjectedEffects = literal.Ledger.ProjectedEffects
                    .Select((effect, index) => index == 0
                        ? effect with { TargetId = "Law" }
                        : effect)
                    .ToArray(),
                DraftDigest = string.Empty
            };
            mismatchedLedger = mismatchedLedger with
            {
                DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(
                    mismatchedLedger)
            };
            CharacterCreationFoundationEffectWritePlanResult mismatchResult =
                CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(
                    literal.WorkspaceId,
                    RulesetDefaults.Sr5,
                    mismatchedLedger,
                    literal.Module,
                    literal.Version,
                    literal.EffectiveSourceXml,
                    literal.SourceDigest,
                    "Chocolate");
            Assert.IsFalse(mismatchResult.IsReady);
            Assert.IsNull(mismatchResult.Plan);
            CollectionAssert.Contains(
                mismatchResult.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Finalization_prompt_effects_are_typed_and_confirm_is_repeatable_zero_write()
    {
        string directory = CreateTempDirectory();
        try
        {
            CharacterWorkspaceId id = new("foundation-finalization-prompt");
            string xml = CharacterXml("Human");
            FileWorkspaceStore store = new(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            CharacterCreationFoundationService service = CreateService(store);
            CharacterCreationFoundationPreview draftPreview = Preview(
                service,
                Load(service, id).Binding,
                UcasModuleId,
                UcasVersionId);
            Assert.AreEqual(
                CharacterCreationFoundationOutcomes.Success,
                Confirm(service, draftPreview).Outcome);
            CharacterCreationFoundationState state = Load(service, id);
            CharacterCreationFoundationDraftLedger draft = state.PendingDraft!;
            CharacterCreationFoundationFinalizationPreview preview = service
                .PreviewFinalization(new CharacterCreationFoundationFinalizationPreviewRequest(
                    state.Binding,
                    draft.DraftRevision,
                    draft.DraftDigest))
                .Value!;
            Assert.IsTrue(preview.Compilation.Effects.Any(effect =>
                effect.CompilationStatus
                == CharacterCreationFoundationEffectCompilationStatuses.PromptRequired
                && effect.PromptIds.Count > 0));
            CollectionAssert.Contains(
                preview.FinalizationBlocked.ToList(),
                CharacterCreationFoundationBlockers.FinalizationPromptRequired);
            string targetPath = WorkspacePath(directory, id);
            byte[] before = File.ReadAllBytes(targetPath);
            DateTime beforeWrite = File.GetLastWriteTimeUtc(targetPath);

            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>
                first = ConfirmFinalization(service, preview);
            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>
                duplicate = ConfirmFinalization(service, preview);

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, first.Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, duplicate.Outcome);
            Assert.IsNull(first.Value);
            Assert.IsNull(duplicate.Value);
            CollectionAssert.Contains(
                first.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationPromptRequired);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));
            Assert.AreEqual(beforeWrite, File.GetLastWriteTimeUtc(targetPath));
            WorkspaceStoredDocument reopened = new FileWorkspaceStore(directory).Get(id).Value!;
            Assert.AreEqual(xml, reopened.Document.Content);
            Assert.IsFalse(reopened.Document.AuxiliaryState
                .CharacterCreationFoundationDraft!.CharacterEffectsApplied);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Finalization_confirmation_rejects_tamper_stale_and_write_fault_without_partial_xml()
    {
        string directory = CreateTempDirectory();
        try
        {
            CharacterWorkspaceId id = new("foundation-finalization-conflict");
            string xml = CharacterXml("Human");
            FileWorkspaceStore healthy = new(directory);
            Assert.IsTrue(healthy.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            CharacterCreationFoundationService service = CreateService(healthy);
            CharacterCreationFoundationPreview draftPreview = Preview(
                service,
                Load(service, id).Binding,
                TirModuleId,
                TirHumanElfVersionId);
            Assert.AreEqual(
                CharacterCreationFoundationOutcomes.Success,
                Confirm(service, draftPreview).Outcome);
            CharacterCreationFoundationState state = Load(service, id);
            CharacterCreationFoundationDraftLedger draft = state.PendingDraft!;
            CharacterCreationFoundationFinalizationPreview preview = service
                .PreviewFinalization(new CharacterCreationFoundationFinalizationPreviewRequest(
                    state.Binding,
                    draft.DraftRevision,
                    draft.DraftDigest))
                .Value!;
            string targetPath = WorkspacePath(directory, id);
            byte[] before = File.ReadAllBytes(targetPath);

            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>
                unconfirmed = service.ConfirmFinalization(
                    new CharacterCreationFoundationFinalizationConfirmRequest(
                        preview.Binding,
                        draft.DraftRevision,
                        draft.DraftDigest,
                        preview.PreviewDigest,
                        ExplicitlyConfirmed: false));
            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>
                tampered = service.ConfirmFinalization(
                    new CharacterCreationFoundationFinalizationConfirmRequest(
                        preview.Binding,
                        draft.DraftRevision,
                        draft.DraftDigest,
                        "sha256:" + new string('0', 64),
                        ExplicitlyConfirmed: true));
            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>
                draftDigestTampered = service.PreviewFinalization(
                    new CharacterCreationFoundationFinalizationPreviewRequest(
                        preview.Binding,
                        draft.DraftRevision,
                        "sha256:" + new string('0', 64)));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, unconfirmed.Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, tampered.Outcome);
            Assert.AreEqual(
                CharacterCreationFoundationOutcomes.Conflict,
                draftDigestTampered.Outcome);
            CollectionAssert.Contains(
                tampered.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            CollectionAssert.Contains(
                draftDigestTampered.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationDraftDigestConflict);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));

            WorkspaceStoredDocument current = healthy.Get(id).Value!;
            Assert.IsTrue(healthy.ReplaceWorkspaceDocument(
                id,
                current.ContentRevision,
                current.Document).Success);
            byte[] afterExternalWrite = File.ReadAllBytes(targetPath);
            CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>
                stale = ConfirmFinalization(service, preview);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
            CollectionAssert.Contains(
                stale.Blockers.ToList(),
                CharacterCreationFoundationBlockers.StaleWorkspaceRevision);
            CollectionAssert.AreEqual(afterExternalWrite, File.ReadAllBytes(targetPath));

            var faulty = new FileWorkspaceStore(
                directory,
                new ThrowingFaultInjector(FileWorkspaceStoreFaultStage.AfterTempFileFlushed));
            CharacterCreationFoundationService faultyService = CreateService(faulty);
            CharacterCreationFoundationState fresh = Load(faultyService, id);
            CharacterCreationFoundationFinalizationPreview blocked = faultyService
                .PreviewFinalization(new CharacterCreationFoundationFinalizationPreviewRequest(
                    fresh.Binding,
                    fresh.PendingDraft!.DraftRevision,
                    fresh.PendingDraft.DraftDigest))
                .Value!;
            byte[] beforeFaultBoundary = File.ReadAllBytes(targetPath);
            Assert.AreEqual(
                CharacterCreationFoundationOutcomes.Blocked,
                ConfirmFinalization(faultyService, blocked).Outcome);
            CollectionAssert.AreEqual(beforeFaultBoundary, File.ReadAllBytes(targetPath));
            WorkspaceStoredDocument reopened = new FileWorkspaceStore(directory).Get(id).Value!;
            Assert.AreEqual(xml, reopened.Document.Content);
            Assert.IsFalse(reopened.Document.AuxiliaryState
                .CharacterCreationFoundationDraft!.CharacterEffectsApplied);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    private static CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>
        ConfirmFinalization(
            ICharacterCreationFoundationService service,
            CharacterCreationFoundationFinalizationPreview preview)
    {
        return service.ConfirmFinalization(
            new CharacterCreationFoundationFinalizationConfirmRequest(
                preview.Binding,
                preview.Compilation.DraftRevision,
                preview.Compilation.DraftDigest,
                preview.PreviewDigest,
                ExplicitlyConfirmed: true));
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

    private static LevelWritePlanFixture CreateLevelWritePlanFixture(
        string directory,
        string workspaceValue,
        string moduleId,
        string moduleName,
        string stage,
        int karma,
        string page,
        string versionId,
        string versionName,
        string versionBonusXml,
        string moduleBonusXml)
    {
        Directory.CreateDirectory(directory);
        string catalogPath = Path.Combine(directory, "lifemodules.xml");
        File.WriteAllText(
            catalogPath,
            "<chummer><stages><stage order=\"1\">"
            + stage
            + "</stage></stages><modules><module><id>"
            + moduleId
            + "</id><stage>"
            + stage
            + "</stage><category>LifeModule</category><name>"
            + moduleName
            + "</name><karma>"
            + karma.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "</karma><versions><version><id>"
            + versionId
            + "</id><name>"
            + versionName
            + "</name><bonus>"
            + versionBonusXml
            + "</bonus></version></versions><bonus>"
            + moduleBonusXml
            + "</bonus><source>RF</source><page>"
            + page
            + "</page></module></modules></chummer>");
        XmlLifeModulesCatalogService catalog = new(catalogPath);
        LifeModuleLegalOptionDto module = catalog
            .GetOptionProjections(stage, ["RF"])
            .Single();
        LifeModuleVersionProjectionDto version = module.Versions.Single();
        LifeModuleEffectProjectionDto[] effects =
        [
            .. version.Effects,
            .. module.Effects
        ];
        CharacterWorkspaceId workspaceId = new(workspaceValue);
        var draft = new CharacterCreationFoundationDraftLedger(
            Schema: CharacterCreationFoundationSchemas.DraftLedgerV1,
            WorkspaceId: workspaceId,
            DraftRevision: 1,
            BaseContentRevision: 1,
            BaseRawCharacterXmlDigest: "sha256:" + new string('1', 64),
            SourceDigest: catalog.GetAuthority().RawXmlDigest,
            RequestedMetatype: "Human",
            Selection: new CharacterCreationFoundationSelection(moduleId, versionId),
            RequirementEvaluations: [],
            ProjectedEffects: effects,
            FollowUpValues: new Dictionary<string, string>(),
            SourceAnchorIds: version.SourceAnchorIds,
            CompilationStatus: CharacterCreationFoundationDraftStatuses.PendingFinalization,
            CharacterEffectsApplied: false,
            DraftDigest: string.Empty);
        draft = draft with
        {
            DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft)
        };
        XElement effectiveSource = new(
            "version",
            new XElement("id", versionId),
            new XElement("name", version.Label),
            new XElement("karma", version.KarmaRaw),
            new XElement("category", "LifeModule"),
            new XElement("source", version.Source),
            new XElement("page", version.PageReference),
            new XElement("stage", module.StageId),
            new XElement("notesColor", "Chocolate"),
            new XElement(
                "bonus",
                effects.Select(effect => XElement.Parse(effect.RawXml))));
        return new LevelWritePlanFixture(
            workspaceId,
            module,
            version,
            draft,
            effectiveSource.ToString(SaveOptions.DisableFormatting),
            catalog.GetAuthority().RawXmlDigest);
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

    private static bool IsCanonicalDigest(string value)
    {
        return value is { Length: 71 }
               && value.StartsWith("sha256:", StringComparison.Ordinal)
               && value.AsSpan(7).ToArray().All(character =>
                   character is >= '0' and <= '9' or >= 'a' and <= 'f');
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

    private sealed record LevelWritePlanFixture(
        CharacterWorkspaceId WorkspaceId,
        LifeModuleLegalOptionDto Module,
        LifeModuleVersionProjectionDto Version,
        CharacterCreationFoundationDraftLedger Ledger,
        string EffectiveSourceXml,
        string SourceDigest);

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
