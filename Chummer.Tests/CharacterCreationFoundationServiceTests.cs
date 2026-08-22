using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationFoundationServiceTests
{
    private static readonly CharacterWorkspaceId s_WorkspaceId = new("foundation-test");

    [TestMethod]
    public void Load_projects_revision_digest_and_honest_resume_boundaries_across_restart()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chummer-foundation-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var firstStore = new FileWorkspaceStore(directory);
            WorkspaceStoreMutationResult created = firstStore.CreateWorkspaceDocument(
                s_WorkspaceId,
                CreateWorkspaceDocument());
            Assert.IsTrue(created.Success);
            var catalog = new StubLifeModulesCatalogService(CreateNationality());
            CharacterCreationFoundationState first = AssertSuccess(
                CreateService(firstStore, catalog).Load(new CharacterCreationFoundationLoadRequest(
                    s_WorkspaceId,
                    ["RF"]))).Value!;

            var restartedStore = new FileWorkspaceStore(directory);
            CharacterCreationFoundationState restarted = AssertSuccess(
                CreateService(restartedStore, catalog).Load(new CharacterCreationFoundationLoadRequest(
                    s_WorkspaceId,
                    ["rf"]))).Value!;

            Assert.AreEqual(1, first.Binding.ContentRevision);
            Assert.AreEqual(0, first.Binding.SavedRevision);
            Assert.AreEqual(first.Binding.RawCharacterXmlDigest, restarted.Binding.RawCharacterXmlDigest);
            Assert.AreEqual(first.Binding.SourceDigest, restarted.Binding.SourceDigest);
            Assert.AreEqual(
                CharacterCreationFoundationDigestSemantics.RawCharacterXmlSha256,
                restarted.Binding.CharacterDigestSemantics);
            Assert.AreEqual(
                CharacterCreationFoundationDigestSemantics.RawSourceInputsSha256,
                restarted.Binding.SourceDigestSemantics);
            Assert.AreEqual(first.SnapshotDigest, restarted.SnapshotDigest);
            Assert.AreEqual("Human", restarted.CurrentMetatype);
            Assert.AreEqual(CharacterCreationBuildMethods.LifeModules, restarted.BuildMethod);
            Assert.IsFalse(restarted.CharacterCreated);
            Assert.HasCount(1, restarted.NationalityOptions);
            Assert.IsEmpty(restarted.MetatypeOptions);
            Assert.IsNull(restarted.PendingDraft);
            Assert.AreEqual(
                CharacterCreationFoundationResumeStatuses.AuthorityRequired,
                restarted.ResumeStatus);
            CollectionAssert.Contains(
                restarted.AuthorityBlockers.ToList(),
                CharacterCreationFoundationBlockers.MetatypeCatalogAuthorityRequired);
            CollectionAssert.Contains(
                restarted.AuthorityBlockers.ToList(),
                CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Preview_evaluates_explicit_metatype_requirement_and_returns_non_mutating_typed_diff()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        store.ResetMutationCounts();
        var catalog = new StubLifeModulesCatalogService(CreateNationality());
        ICharacterCreationFoundationService service = CreateService(store, catalog);
        CharacterCreationFoundationState state = AssertSuccess(
            service.Load(new CharacterCreationFoundationLoadRequest(s_WorkspaceId, ["RF"]))).Value!;

        CharacterCreationFoundationResult<CharacterCreationFoundationPreview> result = service.Preview(
            CreatePreviewRequest(state.Binding));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        CharacterCreationFoundationPreview preview = result.Value!;
        Assert.IsNotNull(preview);
        Assert.IsFalse(preview.CanApply);
        Assert.IsFalse(preview.CanConfirm);
        Assert.IsFalse(preview.CharacterEffectsApplied);
        Assert.IsTrue(preview.RequiresExplicitConfirmation);
        Assert.IsTrue(preview.PreviewDigest.StartsWith("sha256:", StringComparison.Ordinal));
        LifeModuleRequirementProjectionDto requirement = AssertExactlyOne(preview.RequirementEvaluations);
        Assert.IsTrue(requirement.IsMet);
        Assert.IsFalse(requirement.RequiresCharacterAuthority);
        Assert.IsNull(requirement.DisableReasonKey);
        Assert.IsTrue(preview.Diff.Any(item =>
            item.Domain == "life-module-selection"
            && item.AfterValue == "nationality-module/nationality-version"));
        Assert.IsTrue(preview.Diff.Any(item =>
            item.Domain == "attribute" && item.TargetId == "LOG"));
        Assert.IsTrue(preview.Diff.All(item =>
            !item.CanApply
            && item.Phase == CharacterCreationFoundationDiffPhases.DraftLedger
            && !item.AppliesToCharacterDocument));
        CollectionAssert.Contains(
            preview.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired);
        CollectionAssert.Contains(
            preview.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.LifeModuleEffectApplicationAuthorityRequired);
        CollectionAssert.Contains(
            preview.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);
        CollectionAssert.Contains(
            preview.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.MetatypeCatalogAuthorityRequired);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Preview_rejects_metatype_requirement_that_current_character_does_not_meet()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument(metatype: "Ork"));
        store.ResetMutationCounts();
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality()));
        CharacterCreationFoundationState state = AssertSuccess(
            service.Load(new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;

        CharacterCreationFoundationPreview preview = service.Preview(
            CreatePreviewRequest(state.Binding, requestedMetatype: "Ork")).Value!;

        LifeModuleRequirementProjectionDto requirement = AssertExactlyOne(preview.RequirementEvaluations);
        Assert.IsFalse(requirement.IsMet);
        Assert.IsFalse(requirement.RequiresCharacterAuthority);
        Assert.AreEqual(
            CharacterCreationFoundationBlockers.LifeModuleRequirementNotMet,
            requirement.DisableReasonKey);
        CollectionAssert.Contains(
            preview.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.LifeModuleRequirementNotMet);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Preview_evaluates_requirement_against_requested_metatype_but_keeps_legality_blocked()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument(metatype: "Ork"));
        store.ResetMutationCounts();
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality()));
        CharacterCreationFoundationState state = AssertSuccess(
            service.Load(new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;

        CharacterCreationFoundationPreview preview = service.Preview(
            CreatePreviewRequest(state.Binding, requestedMetatype: "Elf")).Value!;

        Assert.IsTrue(AssertExactlyOne(preview.RequirementEvaluations).IsMet);
        CollectionAssert.Contains(
            preview.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.MetatypeLegalityAuthorityRequired);
        Assert.IsFalse(preview.CanApply);
        Assert.IsFalse(preview.CanConfirm);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Preview_rejects_missing_or_unknown_nested_version()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        store.ResetMutationCounts();
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality()));
        CharacterCreationFoundationState state = AssertSuccess(
            service.Load(new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;

        CharacterCreationFoundationPreview missing = service.Preview(
            CreatePreviewRequest(state.Binding, versionId: null)).Value!;
        CharacterCreationFoundationPreview unknown = service.Preview(
            CreatePreviewRequest(state.Binding, versionId: "not-a-version")).Value!;

        CollectionAssert.Contains(
            missing.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.NationalityVersionRequired);
        CollectionAssert.Contains(
            unknown.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.NationalityVersionNotFound);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Confirm_requires_explicit_confirmation_and_exact_preview_digest_without_writes()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        store.ResetMutationCounts();
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality()));
        CharacterCreationFoundationState state = AssertSuccess(
            service.Load(new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;
        CharacterCreationFoundationPreview preview = service.Preview(
            CreatePreviewRequest(state.Binding)).Value!;

        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> notConfirmed =
            service.Confirm(CreateConfirmRequest(preview, explicitlyConfirmed: false));
        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> tampered =
            service.Confirm(CreateConfirmRequest(
                preview,
                explicitlyConfirmed: true,
                previewDigest: "sha256:" + new string('0', 64)));
        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> exact =
            service.Confirm(CreateConfirmRequest(preview, explicitlyConfirmed: true));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, notConfirmed.Outcome);
        CollectionAssert.Contains(
            notConfirmed.Blockers.ToList(),
            CharacterCreationFoundationBlockers.ExplicitConfirmationRequired);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, tampered.Outcome);
        CollectionAssert.Contains(
            tampered.Blockers.ToList(),
            CharacterCreationFoundationBlockers.PreviewDigestMismatch);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, exact.Outcome);
        Assert.IsNull(exact.Value);
        Assert.AreEqual(0, store.TotalMutationCalls);
        WorkspaceStoredDocument current = store.Get(s_WorkspaceId).Value!;
        Assert.AreEqual(1, current.ContentRevision);
        Assert.AreEqual(0, current.SavedRevision);
    }

    [TestMethod]
    public void Empty_metatype_options_keep_confirm_disabled_even_if_apply_authority_claims_ready()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        store.ResetMutationCounts();
        var applyAuthority = new RecordingApplyAuthority();
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality()),
            applyAuthority: applyAuthority);
        CharacterCreationFoundationState state = AssertSuccess(service.Load(
            new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;

        CharacterCreationFoundationPreview preview = service.Preview(
            CreatePreviewRequest(state.Binding)).Value!;
        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> confirmed =
            service.Confirm(CreateConfirmRequest(preview, explicitlyConfirmed: true));

        Assert.IsEmpty(state.MetatypeOptions);
        CollectionAssert.Contains(
            preview.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.MetatypeCatalogAuthorityRequired);
        Assert.IsFalse(preview.CanApply);
        Assert.IsFalse(preview.CanConfirm);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, confirmed.Outcome);
        Assert.AreEqual(0, applyAuthority.ApplyCalls);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Confirm_rejects_stale_workspace_revision_before_authority_boundary_without_writes()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality()));
        CharacterCreationFoundationState state = AssertSuccess(
            service.Load(new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;
        CharacterCreationFoundationPreview preview = service.Preview(
            CreatePreviewRequest(state.Binding)).Value!;
        WorkspaceStoreMutationResult advanced = store.ReplaceWorkspaceDocument(
            s_WorkspaceId,
            expectedContentRevision: 1,
            CreateWorkspaceDocument(name: "Changed Elsewhere"));
        Assert.IsTrue(advanced.Success);
        store.ResetMutationCounts();

        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> result =
            service.Confirm(CreateConfirmRequest(preview, explicitlyConfirmed: true));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToList(),
            CharacterCreationFoundationBlockers.StaleWorkspaceRevision);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Confirm_rejects_changed_source_digest_without_writes()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        store.ResetMutationCounts();
        var catalog = new StubLifeModulesCatalogService(CreateNationality());
        ICharacterCreationFoundationService service = CreateService(store, catalog);
        CharacterCreationFoundationState state = AssertSuccess(
            service.Load(new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;
        CharacterCreationFoundationPreview preview = service.Preview(
            CreatePreviewRequest(state.Binding)).Value!;
        catalog.RawXmlDigest = "sha256:" + new string('b', 64);

        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> result =
            service.Confirm(CreateConfirmRequest(preview, explicitlyConfirmed: true));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToList(),
            CharacterCreationFoundationBlockers.SourceDigestConflict);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Confirm_rejects_changed_raw_settings_profile_digest_without_writes()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        store.ResetMutationCounts();
        var sourceResolver = new StubCharacterSourceDataResolver(["RF"]);
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality()),
            sourceResolver);
        CharacterCreationFoundationState state = AssertSuccess(service.Load(
            new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;
        CharacterCreationFoundationPreview preview = service.Preview(
            CreatePreviewRequest(state.Binding)).Value!;
        sourceResolver.RawProfileInputsDigest = "sha256:" + new string('d', 64);

        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> result =
            service.Confirm(CreateConfirmRequest(preview, explicitlyConfirmed: true));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToList(),
            CharacterCreationFoundationBlockers.SourceDigestConflict);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Confirm_rejects_same_revision_content_digest_drift_without_writes()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        store.ResetMutationCounts();
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality()));
        CharacterCreationFoundationState state = AssertSuccess(
            service.Load(new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;
        CharacterCreationFoundationPreview preview = service.Preview(
            CreatePreviewRequest(state.Binding)).Value!;
        store.ReadDocumentOverride = CreateWorkspaceDocument(name: "Same Revision Drift");

        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> result =
            service.Confirm(CreateConfirmRequest(preview, explicitlyConfirmed: true));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
        CollectionAssert.Contains(
            result.Blockers.ToList(),
            CharacterCreationFoundationBlockers.StaleRawCharacterXmlDigest);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Draft_ledger_contract_roundtrips_pending_selection_without_effect_application_claim()
    {
        LifeModuleLegalOptionDto nationality = CreateNationality();
        LifeModuleVersionProjectionDto version = nationality.Versions[0];
        var draft = new CharacterCreationFoundationDraftLedger(
            Schema: CharacterCreationFoundationSchemas.DraftLedgerV1,
            WorkspaceId: s_WorkspaceId,
            DraftRevision: 3,
            BaseContentRevision: 7,
            BaseRawCharacterXmlDigest: "sha256:base",
            SourceDigest: "sha256:source",
            RequestedMetatype: "Human",
            Selection: new CharacterCreationFoundationSelection(
                nationality.ModuleId,
                version.VersionId),
            RequirementEvaluations: version.Requirements,
            ProjectedEffects: nationality.Effects.Concat(version.Effects).ToArray(),
            FollowUpValues: new Dictionary<string, string> { ["city"] = "Seattle" },
            SourceAnchorIds: nationality.SourceAnchorIds.Concat(version.SourceAnchorIds).ToArray(),
            CompilationStatus: CharacterCreationFoundationDraftStatuses.PendingFinalization,
            CharacterEffectsApplied: false,
            DraftDigest: "sha256:draft");

        string json = JsonSerializer.Serialize(draft);
        CharacterCreationFoundationDraftLedger? resumed =
            JsonSerializer.Deserialize<CharacterCreationFoundationDraftLedger>(json);

        Assert.IsNotNull(resumed);
        Assert.AreEqual(draft.Selection, resumed.Selection);
        Assert.AreEqual(3, resumed.DraftRevision);
        Assert.HasCount(2, resumed.ProjectedEffects);
        Assert.AreEqual("Seattle", resumed.FollowUpValues["city"]);
        Assert.AreEqual(
            CharacterCreationFoundationDraftStatuses.PendingFinalization,
            resumed.CompilationStatus);
        Assert.IsFalse(resumed.CharacterEffectsApplied);
    }

    [TestMethod]
    public void Load_exposes_ruleset_build_method_and_created_state_as_fail_closed_blockers()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(
            s_WorkspaceId,
            CreateWorkspaceDocument(
                buildMethod: CharacterCreationBuildMethods.Priority,
                created: true,
                rulesetId: RulesetDefaults.Sr6));
        store.ResetMutationCounts();

        CharacterCreationFoundationState state = AssertSuccess(CreateService(
            store,
            new StubLifeModulesCatalogService(CreateNationality())).Load(
                new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;

        CollectionAssert.Contains(
            state.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.RulesetSr5Required);
        CollectionAssert.Contains(
            state.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.LifeModuleBuildMethodRequired);
        CollectionAssert.Contains(
            state.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.CharacterAlreadyCreated);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    [TestMethod]
    public void Load_never_treats_null_as_all_and_intersects_requested_sources_with_saved_profile()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        var catalog = new StubLifeModulesCatalogService(CreateNationality());
        var sourceResolver = new StubCharacterSourceDataResolver(["RF"]);
        ICharacterCreationFoundationService service = CreateService(store, catalog, sourceResolver);

        CharacterCreationFoundationState unfiltered = AssertSuccess(service.Load(
            new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;
        Assert.HasCount(1, unfiltered.NationalityOptions);
        CollectionAssert.AreEqual(new[] { "RF" }, catalog.LastEnabledSources!.ToArray());
        Assert.IsFalse(unfiltered.Binding.SourceFilterApplied);

        CharacterCreationFoundationState unauthorized = AssertSuccess(service.Load(
            new CharacterCreationFoundationLoadRequest(s_WorkspaceId, ["CORE"]))).Value!;
        Assert.IsEmpty(unauthorized.NationalityOptions);
        Assert.IsNotNull(catalog.LastEnabledSources);
        Assert.IsEmpty(catalog.LastEnabledSources);
        Assert.IsTrue(unauthorized.Binding.SourceFilterApplied);
        Assert.IsEmpty(unauthorized.Binding.EnabledSources);
    }

    [TestMethod]
    public void Load_without_saved_source_profile_authority_exposes_no_modules_and_blocks_confirm()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        var catalog = new StubLifeModulesCatalogService(CreateNationality());
        ICharacterCreationFoundationService service = CreateService(
            store,
            catalog,
            new StubCharacterSourceDataResolver(enabledSources: null));

        CharacterCreationFoundationState state = AssertSuccess(service.Load(
            new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;

        Assert.IsEmpty(state.NationalityOptions);
        Assert.IsNull(catalog.LastEnabledSources);
        CollectionAssert.Contains(
            state.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.EnabledSourceAuthorityRequired);
        Assert.IsEmpty(state.MetatypeOptions);
    }

    [TestMethod]
    public void Preview_binds_and_validates_follow_up_values_without_applying_them()
    {
        var store = new CountingWorkspaceStore();
        store.CreateWorkspaceDocument(s_WorkspaceId, CreateWorkspaceDocument());
        store.ResetMutationCounts();
        LifeModuleFollowUpPromptDto prompt = new(
            PromptId: "nationality-module:effect:1:follow-up:1",
            Label: "Region",
            InputKind: "single-select",
            IsRequired: true,
            Options:
            [
                new LifeModuleFollowUpOptionDto(
                    OptionId: "north",
                    Label: "North",
                    IsEnabled: true,
                    DisableReasonKey: null,
                    DisableReasonArguments: new Dictionary<string, string>(),
                    SourceValue: "North")
            ],
            SourceAnchorIds: ["lifemodules.xml#module:nationality-module"],
            EffectId: "nationality-module:effect:1",
            ValuePath: "attributelevel/name");
        LifeModuleLegalOptionDto option = CreateNationality() with { FollowUps = [prompt] };
        ICharacterCreationFoundationService service = CreateService(
            store,
            new StubLifeModulesCatalogService(option));
        CharacterCreationFoundationState state = AssertSuccess(service.Load(
            new CharacterCreationFoundationLoadRequest(s_WorkspaceId))).Value!;

        CharacterCreationFoundationPreview missing = service.Preview(
            CreatePreviewRequest(state.Binding)).Value!;
        CharacterCreationFoundationPreview invalid = service.Preview(
            CreatePreviewRequest(
                state.Binding,
                followUps: new Dictionary<string, string> { [prompt.PromptId] = "South" })).Value!;
        CharacterCreationFoundationPreview valid = service.Preview(
            CreatePreviewRequest(
                state.Binding,
                followUps: new Dictionary<string, string> { [prompt.PromptId] = "North" })).Value!;

        CollectionAssert.Contains(
            missing.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.LifeModuleFollowUpRequired);
        CollectionAssert.Contains(
            invalid.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.LifeModuleFollowUpOptionInvalid);
        Assert.AreEqual("North", valid.FollowUpValues[prompt.PromptId]);
        Assert.IsTrue(valid.Diff.Any(item =>
            item.Domain == "life-module-follow-up" && item.AfterValue == "North"));
        Assert.IsFalse(valid.CanApply);
        Assert.IsFalse(valid.CanConfirm);
        Assert.AreEqual(0, store.TotalMutationCalls);
    }

    private static CharacterCreationFoundationService CreateService(
        IWorkspaceStore store,
        ILifeModulesCatalogService catalog,
        ICharacterSourceDataResolver? sourceResolver = null,
        ICharacterCreationFoundationApplyAuthority? applyAuthority = null)
    {
        return new CharacterCreationFoundationService(
            store,
            new XmlCharacterFileQueries(new CharacterFileService()),
            sourceResolver ?? new StubCharacterSourceDataResolver(["RF"]),
            catalog,
            applyAuthority ?? new UnavailableCharacterCreationFoundationApplyAuthority());
    }

    private static CharacterCreationFoundationPreviewRequest CreatePreviewRequest(
        CharacterCreationFoundationBinding binding,
        string requestedMetatype = "Human",
        string? versionId = "nationality-version",
        IReadOnlyDictionary<string, string>? followUps = null)
    {
        return new CharacterCreationFoundationPreviewRequest(
            Binding: binding,
            RequestedMetatype: requestedMetatype,
            Selection: new CharacterCreationFoundationSelection(
                ModuleId: "nationality-module",
                VersionId: versionId),
            FollowUpValues: followUps);
    }

    private static CharacterCreationFoundationConfirmRequest CreateConfirmRequest(
        CharacterCreationFoundationPreview preview,
        bool explicitlyConfirmed,
        string? previewDigest = null)
    {
        return new CharacterCreationFoundationConfirmRequest(
            Binding: preview.Binding,
            RequestedMetatype: preview.RequestedMetatype,
            Selection: preview.Selection,
            PreviewDigest: previewDigest ?? preview.PreviewDigest,
            ExplicitlyConfirmed: explicitlyConfirmed,
            FollowUpValues: preview.FollowUpValues);
    }

    private static WorkspaceDocument CreateWorkspaceDocument(
        string name = "Foundation Runner",
        string metatype = "Human",
        string buildMethod = CharacterCreationBuildMethods.LifeModules,
        bool created = false,
        string rulesetId = RulesetDefaults.Sr5)
    {
        string content = $"""
                          <character>
                            <name>{name}</name>
                            <alias>Foundation</alias>
                            <metatype>{metatype}</metatype>
                            <buildmethod>{buildMethod}</buildmethod>
                            <createdversion>5.225.0</createdversion>
                            <appversion>5.225.0</appversion>
                            <karma>25</karma>
                            <nuyen>0</nuyen>
                            <created>{created}</created>
                          </character>
                          """;
        return new WorkspaceDocument(content, rulesetId);
    }

    private static LifeModuleLegalOptionDto CreateNationality()
    {
        LifeModuleRequirementProjectionDto requirement = new(
            RequirementId: "nationality-version:requirement:1",
            Label: "oneof metatype: Human | Elf",
            IsMet: false,
            DisableReasonKey: CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired,
            DisableReasonArguments: new Dictionary<string, string>
            {
                ["operator"] = "oneof",
                ["subjectKind"] = "metatype"
            },
            SourceAnchorIds: ["lifemodules.xml#version:nationality-version"],
            Operator: "oneof",
            SubjectKind: "metatype",
            AcceptedValues: ["Human", "Elf"],
            RawXml: "<oneof><metatype>Human</metatype><metatype>Elf</metatype></oneof>",
            RequiresCharacterAuthority: true);
        LifeModuleEffectProjectionDto moduleEffect = CreateEffect(
            "nationality-module:effect:1",
            "attribute",
            "LOG",
            "1");
        LifeModuleEffectProjectionDto versionEffect = CreateEffect(
            "nationality-version:effect:1",
            "active-skill",
            "Etiquette",
            "1");
        LifeModuleVersionProjectionDto version = new(
            VersionId: "nationality-version",
            Label: "General Nation",
            IsEnabled: false,
            Requirements: [requirement],
            Effects: [versionEffect],
            FollowUps: [],
            SourceAnchorIds: ["lifemodules.xml#version:nationality-version"],
            StoryTemplate: "$real was born in the nation.",
            KarmaCost: 15,
            KarmaRaw: "15",
            KarmaIsExact: true,
            Source: "RF",
            Page: 66,
            PageReference: "66",
            AuthorityBlockers: [CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired]);
        return new LifeModuleLegalOptionDto(
            ModuleId: "nationality-module",
            StageOrder: LifeModuleJourneyStageOrders.Nationality,
            Name: "Nation",
            KarmaCost: 15,
            Source: "RF",
            Page: 66,
            StoryTemplate: "$real was born in the nation.",
            IsEnabled: false,
            Requirements: [],
            Versions: [version],
            Effects: [moduleEffect],
            FollowUps: [],
            SourceAnchorIds: ["lifemodules.xml#module:nationality-module"],
            StageId: "Nationality",
            CanRepeat: false,
            KarmaRaw: "15",
            KarmaIsExact: true,
            PageReference: "66",
            AuthorityBlockers: [CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired]);
    }

    private static LifeModuleEffectProjectionDto CreateEffect(
        string id,
        string domain,
        string target,
        string value)
    {
        return new LifeModuleEffectProjectionDto(
            EffectId: id,
            Domain: domain,
            TargetId: target,
            BeforeValue: null,
            AfterValue: value,
            BudgetId: null,
            BudgetDelta: 0,
            SourceAnchorIds: ["lifemodules.xml#module:nationality-module"],
            Parameters: new Dictionary<string, string> { ["val"] = value },
            RawXml: $"<effect><name>{target}</name><val>{value}</val></effect>",
            IsFullyTyped: true,
            AuthorityBlocker: XmlLifeModulesCatalogService.EffectApplicationAuthorityRequired);
    }

    private static CharacterCreationFoundationResult<T> AssertSuccess<T>(
        CharacterCreationFoundationResult<T> result)
        where T : class
    {
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result;
    }

    private static T AssertExactlyOne<T>(IReadOnlyList<T> values)
    {
        Assert.HasCount(1, values);
        return values[0];
    }

    private sealed class StubLifeModulesCatalogService : ILifeModulesCatalogService
    {
        public StubLifeModulesCatalogService(LifeModuleLegalOptionDto option)
        {
            Option = option;
        }

        public LifeModuleLegalOptionDto Option { get; set; }
        public string RawXmlDigest { get; set; } = "sha256:" + new string('a', 64);
        public IReadOnlyCollection<string>? LastEnabledSources { get; private set; }

        public LifeModuleCatalogAuthorityDto GetAuthority() => new(
            Schema: LifeModuleJourneySchemas.CatalogAuthorityV1,
            RawXmlDigest: RawXmlDigest,
            SourceAnchorIds: ["lifemodules.xml"]);

        public IReadOnlyList<LifeModuleStageDto> GetStages() =>
            [new LifeModuleStageDto(1, "Nationality")];

        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null) => [];

        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(
            string? stage = null,
            IReadOnlyCollection<string>? enabledSources = null)
        {
            LastEnabledSources = enabledSources?.ToArray();
            if (!string.IsNullOrWhiteSpace(stage)
                && !string.Equals(stage, Option.StageId, StringComparison.Ordinal))
            {
                return [];
            }

            if (enabledSources is not null
                && !enabledSources.Contains(Option.Source, StringComparer.OrdinalIgnoreCase))
            {
                return [];
            }

            return [Option];
        }
    }

    private sealed class StubCharacterSourceDataResolver : ICharacterSourceDataResolver
    {
        private readonly IReadOnlyList<string>? _enabledSources;

        public StubCharacterSourceDataResolver(IReadOnlyList<string>? enabledSources)
        {
            _enabledSources = enabledSources;
        }

        public string RawProfileInputsDigest { get; set; } = "sha256:" + new string('c', 64);

        public ICharacterSourceDataContext? TryCreateContext(string characterXml) =>
            _enabledSources is null ? null : new Context(_enabledSources, this);

        private sealed class Context : ICharacterSourceDataContext
        {
            private readonly IReadOnlyList<string> _enabledSources;
            private readonly StubCharacterSourceDataResolver _owner;

            public Context(
                IReadOnlyList<string> enabledSources,
                StubCharacterSourceDataResolver owner)
            {
                _enabledSources = enabledSources;
                _owner = owner;
            }

            public bool TryResolveCreationSourceProfile(
                out CharacterCreationSourceProfileAuthority authority)
            {
                authority = new CharacterCreationSourceProfileAuthority(
                    SettingsProfileId: "test-profile",
                    EnabledSourcebooks: _enabledSources,
                    RawProfileInputsDigest: _owner.RawProfileInputsDigest,
                    SourceAnchorIds: ["settings.xml#setting:test-profile"]);
                return true;
            }

            public bool TryResolveCyberwareGradeDeviceRating(
                string gradeName,
                string improvementSource,
                out int deviceRating)
            {
                deviceRating = 0;
                return false;
            }

            public bool TryResolveVehicleModBonuses(
                string sourceId,
                string name,
                out CharacterVehicleModSourceBonuses bonuses)
            {
                bonuses = CharacterVehicleModSourceBonuses.Empty;
                return false;
            }
        }
    }

    private sealed class RecordingApplyAuthority : ICharacterCreationFoundationApplyAuthority
    {
        public int ApplyCalls { get; private set; }

        public CharacterCreationFoundationAuthorityPreview Preview(
            CharacterCreationFoundationAuthorityContext context) => new(
                Diff: [],
                Blockers: [],
                CanApply: true,
                AuthorityPlanDigest: "sha256:" + new string('e', 64));

        public CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> ApplyAndCheckpoint(
            CharacterCreationFoundationAuthorityContext context,
            string previewDigest)
        {
            ApplyCalls++;
            throw new AssertFailedException("Apply must not run while metatype authority is unavailable.");
        }
    }

    private sealed class CountingWorkspaceStore : IWorkspaceStore
    {
        private readonly InMemoryWorkspaceStore _inner = new();

        public int CreateCalls { get; private set; }
        public int ReplaceCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int TotalMutationCalls => CreateCalls + ReplaceCalls + SaveCalls + DeleteCalls;
        public WorkspaceDocument? ReadDocumentOverride { get; set; }

        public void ResetMutationCounts()
        {
            CreateCalls = 0;
            ReplaceCalls = 0;
            SaveCalls = 0;
            DeleteCalls = 0;
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
        {
            CreateCalls++;
            return _inner.CreateWorkspaceDocument(document);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            WorkspaceDocument document)
        {
            CreateCalls++;
            return _inner.CreateWorkspaceDocument(owner, document);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
        {
            CreateCalls++;
            return _inner.CreateWorkspaceDocument(id, document);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            WorkspaceDocument document)
        {
            CreateCalls++;
            return _inner.CreateWorkspaceDocument(owner, id, document);
        }

        public IReadOnlyList<WorkspaceStoreEntry> List() => _inner.List();

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => _inner.List(owner);

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            WorkspaceStoreReadResult read = _inner.Get(id);
            return read.Value is WorkspaceStoredDocument value && ReadDocumentOverride is not null
                ? read with { Value = value with { Document = ReadDocumentOverride } }
                : read;
        }

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) =>
            _inner.Get(owner, id);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
        {
            ReplaceCalls++;
            return _inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);
        }

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
        {
            ReplaceCalls++;
            return _inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);
        }

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision)
        {
            SaveCalls++;
            return _inner.SaveCheckpoint(id, expectedContentRevision);
        }

        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
        {
            SaveCalls++;
            return _inner.SaveCheckpoint(owner, id, expectedContentRevision);
        }

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision)
        {
            DeleteCalls++;
            return _inner.Delete(id, expectedContentRevision);
        }

        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
        {
            DeleteCalls++;
            return _inner.Delete(owner, id, expectedContentRevision);
        }
    }
}
