#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chummer.Contracts.Api;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr4;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class WorkspaceServiceTests
{
    [TestMethod]
    public void Import_does_not_create_workspace_when_summary_parse_fails()
    {
        TrackingWorkspaceStore store = new();
        WorkspaceService workspaceService = CreateWorkspaceService(
            store,
            new ThrowingCharacterFileQueries(),
            new NoopCharacterSectionQueries(),
            new NoopCharacterMetadataCommands());

        Assert.ThrowsExactly<FormatException>(() => workspaceService.Import(new WorkspaceImportDocument(
            "<character><name>Broken</name></character>",
            RulesetDefaults.Sr5,
            WorkspaceDocumentFormat.NativeXml)));
        Assert.AreEqual(0, store.CreateCallCount);
    }

    [TestMethod]
    public void Import_with_owner_scope_routes_create_through_owner_scoped_store()
    {
        TrackingWorkspaceStore store = new();
        WorkspaceService workspaceService = CreateWorkspaceService(
            store,
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));

        WorkspaceImportResult imported = workspaceService.Import(
            new OwnerScope("Alice@example.com"),
            new WorkspaceImportDocument(
                "<character><name>Scoped</name><alias>Owner</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>",
                RulesetDefaults.Sr5,
                WorkspaceDocumentFormat.NativeXml));

        Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Id.Value));
        Assert.AreEqual("alice@example.com", store.LastCreateOwner?.NormalizedValue);
    }

    [TestMethod]
    public void Import_with_local_single_user_scope_routes_through_the_unscoped_store_lane()
    {
        TrackingWorkspaceStore store = new();
        WorkspaceService workspaceService = CreateWorkspaceService(
            store,
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));

        WorkspaceImportResult imported = workspaceService.Import(
            OwnerScope.LocalSingleUser,
            CreateScopedImportDocument("Local"));

        Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Id.Value));
        Assert.IsNull(store.LastCreateOwner);
        CollectionAssert.Contains(
            workspaceService.List(OwnerScope.LocalSingleUser).Select(static workspace => workspace.Id.Value).ToArray(),
            imported.Id.Value);
        Assert.HasCount(0, workspaceService.List(new OwnerScope("alice@example.com")));
    }

    [TestMethod]
    public void Import_with_blank_owner_scope_remains_rejected()
    {
        WorkspaceService workspaceService = CreateWorkspaceService(
            new InMemoryWorkspaceStore(),
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));

        InvalidOperationException failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            workspaceService.Import(new OwnerScope("  "), CreateScopedImportDocument("Blank")));

        Assert.AreEqual("Owner scope is invalid.", failure.Message);
        Assert.HasCount(0, workspaceService.List());
    }

    [TestMethod]
    public void Import_with_named_owner_scopes_keeps_two_owner_and_local_lanes_isolated()
    {
        WorkspaceService workspaceService = CreateWorkspaceService(
            new InMemoryWorkspaceStore(),
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        OwnerScope alice = new("Alice@example.com");
        OwnerScope bob = new("Bob@example.com");

        WorkspaceImportResult aliceImport = workspaceService.Import(alice, CreateScopedImportDocument("Alice"));
        WorkspaceImportResult bobImport = workspaceService.Import(bob, CreateScopedImportDocument("Bob"));

        CollectionAssert.AreEqual(
            new[] { aliceImport.Id.Value },
            workspaceService.List(alice).Select(static workspace => workspace.Id.Value).ToArray());
        CollectionAssert.AreEqual(
            new[] { bobImport.Id.Value },
            workspaceService.List(bob).Select(static workspace => workspace.Id.Value).ToArray());
        Assert.HasCount(0, workspaceService.List(OwnerScope.LocalSingleUser));
    }

    private static WorkspaceImportDocument CreateScopedImportDocument(string name)
        => new(
            $"<character><name>{name}</name><alias>Owner</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>",
            RulesetDefaults.Sr5,
            WorkspaceDocumentFormat.NativeXml);

    [TestMethod]
    public void Import_get_profile_update_and_save_roundtrip()
    {
        const string xml = "<character><name>Neo</name><alias>The One</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>15</karma><nuyen>2500</nuyen><created>True</created><gameedition>SR5</gameedition><settings>default.xml</settings><gameplayoption>Standard</gameplayoption><gameplayoptionqualitylimit>25</gameplayoptionqualitylimit><maxnuyen>10</maxnuyen><maxkarma>25</maxkarma><contactmultiplier>3</contactmultiplier><walk>2/1/0</walk><run>4/0/0</run><sprint>2/1/0</sprint><walkalt>2/1/0</walkalt><runalt>4/0/0</runalt><sprintalt>2/1/0</sprintalt><magenabled>False</magenabled><resenabled>False</resenabled><depenabled>False</depenabled><newskills><skills><skill><guid>s1</guid><suid>suid1</suid><skillcategory>Combat</skillcategory><isknowledge>False</isknowledge><base>6</base><karma>0</karma></skill></skills></newskills></character>";

        IWorkspaceStore store = new InMemoryWorkspaceStore();
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        WorkspaceService workspaceService = CreateWorkspaceService(store, fileQueries, sectionQueries, metadataCommands);

        WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(xml, RulesetId: RulesetDefaults.Sr5, Format: WorkspaceDocumentFormat.NativeXml));
        Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Id.Value));
        Assert.AreEqual("Neo", imported.Summary.Name);
        Assert.AreEqual("sr5", imported.RulesetId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(imported.ImportReceiptId));
        Assert.AreEqual(WorkspacePortabilityCompatibilityStates.Compatible, imported.Portability?.CompatibilityState);
        Assert.AreEqual(WorkspacePortabilityOutputKinds.NativeWorkspaceXml, imported.Portability?.OutputKind);
        Assert.AreEqual(WorkspacePortabilityLossStates.None, imported.Portability?.Loss?.State);
        Assert.AreEqual(imported.ImportReceiptId, imported.Portability?.Provenance?.ReceiptId);
        Assert.AreEqual(2, imported.Portability?.Lineage?.Count);
        Assert.IsNotNull(imported.WorkflowDeterministicReceipt);
        Assert.AreEqual("governed", imported.WorkflowDeterministicReceipt?.WorkflowStatePosture);
        Assert.IsFalse(string.IsNullOrWhiteSpace(imported.WorkflowDeterministicReceipt?.ReceiptScopeId));
        CollectionAssert.AreEqual(
            new[]
            {
                "workflow:workflow-state",
                "workflow:contacts",
                "workflow:lifestyles",
                "workflow:notes"
            },
            imported.WorkflowDeterministicReceipt?.CoveredWorkflowRouteIds.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), imported.WorkflowDeterministicReceipt?.MissingWorkflowRouteIds.ToArray());
        StringAssert.Contains(imported.Portability?.ReceiptSummary ?? string.Empty, "Portable import completed");
        StringAssert.Contains(imported.Portability?.ProvenanceSummary ?? string.Empty, imported.Id.Value);
        IReadOnlyList<WorkspaceListItem> listed = workspaceService.List();
        Assert.IsTrue(listed.Any(item => string.Equals(item.Id.Value, imported.Id.Value, StringComparison.Ordinal)));
        Assert.AreEqual("sr5", listed.First(item => string.Equals(item.Id.Value, imported.Id.Value, StringComparison.Ordinal)).RulesetId);

        var profile = workspaceService.GetProfile(imported.Id);
        Assert.IsNotNull(profile);
        Assert.AreEqual("Neo", profile.Name);

        var rules = workspaceService.GetRules(imported.Id);
        Assert.IsNotNull(rules);
        Assert.AreEqual("SR5", rules.GameEdition);

        var movement = workspaceService.GetMovement(imported.Id);
        Assert.IsNotNull(movement);
        Assert.AreEqual("2/1/0", movement.Walk);

        var build = workspaceService.GetBuild(imported.Id);
        Assert.IsNotNull(build);
        Assert.AreEqual("Priority", build.BuildMethod);

        var awakening = workspaceService.GetAwakening(imported.Id);
        Assert.IsNotNull(awakening);
        Assert.IsFalse(awakening.MagEnabled);

        var section = workspaceService.GetSection(imported.Id, "skills") as CharacterSkillsSection;
        Assert.IsNotNull(section);
        Assert.AreEqual(1, section.Count);

        var update = workspaceService.UpdateMetadata(imported.Id, new UpdateWorkspaceMetadata("Updated", "Alias", "Notes"));
        Assert.IsTrue(update.Success);
        Assert.AreEqual("Updated", update.Value?.Name);

        var save = workspaceService.Save(imported.Id);
        Assert.IsTrue(save.Success);
        Assert.AreEqual(imported.Id, save.Value?.Id);
        Assert.IsGreaterThan(0, save.Value?.DocumentLength ?? 0);
        Assert.AreEqual("sr5", save.Value?.RulesetId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(save.Value?.ReceiptId));
        Assert.IsNotNull(save.Value?.WorkflowDeterministicReceipt);

        var download = workspaceService.Download(imported.Id);
        Assert.IsTrue(download.Success);
        Assert.AreEqual("sr5", download.Value?.RulesetId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(download.Value?.ReceiptId));
        Assert.IsNotNull(download.Value?.WorkflowDeterministicReceipt);

        var export = workspaceService.Export(imported.Id);
        Assert.IsTrue(export.Success);
        Assert.AreEqual("sr5", export.Value?.RulesetId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Value?.PackageId));
        Assert.IsNotNull(export.Value?.WorkflowDeterministicReceipt);
        Assert.IsNotNull(export.Value?.ExchangeDeterministicReceipt);
        Assert.AreEqual("export", export.Value?.ExchangeDeterministicReceipt?.SurfaceKind);
        Assert.AreEqual("governed", export.Value?.ExchangeDeterministicReceipt?.RuleEnvironmentPosture);
        Assert.AreEqual("default.xml", export.Value?.ExchangeDeterministicReceipt?.SettingsProfile);
        Assert.AreEqual("Standard", export.Value?.ExchangeDeterministicReceipt?.GameplayOption);

        var print = workspaceService.Print(imported.Id);
        Assert.IsTrue(print.Success);
        Assert.AreEqual("sr5", print.Value?.RulesetId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(print.Value?.ReceiptId));
        Assert.IsNotNull(print.Value?.WorkflowDeterministicReceipt);
        Assert.IsNotNull(print.Value?.ExchangeDeterministicReceipt);
        Assert.AreEqual("print", print.Value?.ExchangeDeterministicReceipt?.SurfaceKind);
        Assert.AreEqual(
            export.Value?.ExchangeDeterministicReceipt?.RuleEnvironmentFingerprint,
            print.Value?.ExchangeDeterministicReceipt?.RuleEnvironmentFingerprint);

        bool closed = workspaceService.Close(imported.Id);
        Assert.IsTrue(closed);
        Assert.IsFalse(workspaceService.List().Any(item => string.Equals(item.Id.Value, imported.Id.Value, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Import_get_build_lab_projection_from_sr5_workspace()
    {
        const string xml = "<character><name>Neo</name><alias>The One</alias><metatype>Human</metatype><concept>Street Samurai</concept><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>15</karma><nuyen>2500</nuyen><created>True</created><gameedition>SR5</gameedition><settings>default.xml</settings><gameplayoption>Standard</gameplayoption><gameplayoptionqualitylimit>25</gameplayoptionqualitylimit><maxnuyen>10</maxnuyen><maxkarma>25</maxkarma><contactmultiplier>3</contactmultiplier><walk>2/1/0</walk><run>4/0/0</run><sprint>2/1/0</sprint><walkalt>2/1/0</walkalt><runalt>4/0/0</runalt><sprintalt>2/1/0</sprintalt><magenabled>False</magenabled><resenabled>False</resenabled><depenabled>False</depenabled><newskills><skills><skill><guid>s1</guid><suid>suid1</suid><skillcategory>Combat</skillcategory><isknowledge>False</isknowledge><base>6</base><karma>0</karma></skill></skills></newskills></character>";

        IWorkspaceStore store = new InMemoryWorkspaceStore();
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        WorkspaceService workspaceService = CreateWorkspaceService(store, fileQueries, sectionQueries, metadataCommands);

        WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(xml, RulesetDefaults.Sr5, WorkspaceDocumentFormat.NativeXml));
        BuildLabConceptIntakeProjection? projection = workspaceService.GetSection(imported.Id, "build-lab") as BuildLabConceptIntakeProjection;

        Assert.IsNotNull(projection);
        Assert.AreEqual(imported.Id.Value, projection.WorkspaceId);
        Assert.AreEqual("workflow.build-lab", projection.WorkflowId);
        Assert.AreEqual("sr5", projection.RulesetId);
        Assert.AreEqual("Priority", projection.BuildMethod);
        Assert.IsTrue(projection.Variants.Count > 0);
        Assert.IsTrue(projection.ProgressionTimelines.Count > 0);
        Assert.IsTrue(projection.Actions.Any(action => string.Equals(action.ActionId, "next-variants", StringComparison.Ordinal)));
        Assert.IsTrue(projection.Actions.Any(action => string.Equals(action.ActionId, "open-json-exchange", StringComparison.Ordinal)));
        Assert.IsTrue(projection.Actions.Any(action => string.Equals(action.ActionId, "open-print-pdf-export", StringComparison.Ordinal)));
        Assert.IsTrue(projection.Actions.Any(action => string.Equals(action.ActionId, "open-replay-timeline", StringComparison.Ordinal)));
        Assert.IsTrue(projection.Actions.Any(action => string.Equals(action.ActionId, "open-session-recap", StringComparison.Ordinal)));
        Assert.IsTrue(projection.Actions.Any(action => string.Equals(action.ActionId, "open-run-module", StringComparison.Ordinal)));
        Assert.IsTrue(projection.ExportTargets?.Any(target => string.Equals(target.TargetId, "target.json-exchange", StringComparison.Ordinal)
            && string.Equals(target.WorkflowId, "workflow.exchange.json", StringComparison.Ordinal)) == true);
        Assert.IsTrue(projection.ExportTargets?.Any(target => string.Equals(target.TargetId, "target.print-pdf-export", StringComparison.Ordinal)
            && string.Equals(target.WorkflowId, "workflow.export.pdf", StringComparison.Ordinal)) == true);
        Assert.IsTrue(projection.ExportTargets?.Any(target => string.Equals(target.TargetId, "target.replay-timeline", StringComparison.Ordinal)
            && string.Equals(target.WorkflowId, "workflow.replay.timeline", StringComparison.Ordinal)) == true);
        Assert.IsTrue(projection.ExportTargets?.Any(target => string.Equals(target.TargetId, "target.session-recap", StringComparison.Ordinal)
            && string.Equals(target.WorkflowId, "workflow.recap.session", StringComparison.Ordinal)) == true);
        Assert.IsTrue(projection.ExportTargets?.Any(target => string.Equals(target.TargetId, "target.run-module", StringComparison.Ordinal)
            && string.Equals(target.WorkflowId, "workflow.module.run", StringComparison.Ordinal)) == true);
    }

    [TestMethod]
    public void Import_accepts_xml_with_utf8_bom_prefix()
    {
        const string xml = "\uFEFF<character><name>BOM Runner</name><alias>BOM</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>";

        IWorkspaceStore store = new InMemoryWorkspaceStore();
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        WorkspaceService workspaceService = CreateWorkspaceService(store, fileQueries, sectionQueries, metadataCommands);

        WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(xml, RulesetDefaults.Sr5, WorkspaceDocumentFormat.NativeXml));
        Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Id.Value));
        Assert.AreEqual("BOM Runner", imported.Summary.Name);
        Assert.AreEqual("BOM", imported.Summary.Alias);
    }

    [TestMethod]
    public void Import_json_marks_portable_dossier_review_posture()
    {
        TrackingWorkspaceStore store = new();
        RecordingWorkspaceCodec codec = new();
        WorkspaceService workspaceService = new(store, new RulesetWorkspaceCodecResolver([codec]), new WorkspaceImportRulesetDetector());

        WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(
            "{\"name\":\"Codec Runner\"}",
            "sr6",
            WorkspaceDocumentFormat.Json));

        Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Id.Value));
        Assert.AreEqual("sr6", imported.RulesetId);
        Assert.AreEqual(WorkspacePortabilityOutputKinds.PortableDossier, imported.Portability?.OutputKind);
        Assert.AreEqual(WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings, imported.Portability?.CompatibilityState);
        Assert.AreEqual(WorkspacePortabilityLossStates.BoundedLoss, imported.Portability?.Loss?.State);
        Assert.AreEqual(WorkspacePortabilityRevocationStates.Revocable, imported.Portability?.Revocation?.State);
        Assert.AreEqual("workspace-portability:portable-dossier", imported.Portability?.Revocation?.FamilyId);
        CollectionAssert.AreEqual(new[] { "format-review-required" }, imported.Portability?.Compatibility?.WarningCodes?.ToArray());
        CollectionAssert.AreEqual(new[] { "native-workspace-review" }, imported.Portability?.Loss?.AffectedSections?.ToArray());
        Assert.AreEqual(imported.ImportReceiptId, imported.Portability?.Provenance?.ReceiptId);
        Assert.AreEqual(2, imported.Portability?.Lineage?.Count);
        StringAssert.Contains(imported.Portability?.PortabilityEnvelope?.Summary ?? string.Empty, "Inspect-first import posture");
        StringAssert.Contains(imported.Portability?.NextSafeAction ?? string.Empty, "export a fresh portable package");
    }

    [TestMethod]
    public void Import_reuses_content_addressed_receipt_ids_across_distinct_workspace_instances()
    {
        const string xml = "<character><name>Repeatable</name><alias>Receipt</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>15</karma><nuyen>2500</nuyen><created>True</created><gameedition>SR5</gameedition><settings>default.xml</settings><gameplayoption>Standard</gameplayoption><notes>Stable notes</notes><gamenotes>Stable game notes</gamenotes><contacts><contact><name>Fixer</name></contact></contacts><lifestyles><lifestyle><name>Middle</name></lifestyle></lifestyles></character>";

        WorkspaceService workspaceService = CreateWorkspaceService(
            new InMemoryWorkspaceStore(),
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));

        WorkspaceImportResult first = workspaceService.Import(new WorkspaceImportDocument(xml, RulesetDefaults.Sr5, WorkspaceDocumentFormat.NativeXml));
        WorkspaceImportResult second = workspaceService.Import(new WorkspaceImportDocument(xml, RulesetDefaults.Sr5, WorkspaceDocumentFormat.NativeXml));

        Assert.AreNotEqual(first.Id.Value, second.Id.Value);
        Assert.AreEqual(first.ImportReceiptId, second.ImportReceiptId);
        Assert.AreEqual(first.Portability?.Provenance?.ReceiptId, second.Portability?.Provenance?.ReceiptId);
        Assert.AreEqual(first.WorkflowDeterministicReceipt?.ReceiptId, second.WorkflowDeterministicReceipt?.ReceiptId);
        Assert.AreEqual(first.WorkflowDeterministicReceipt?.ReceiptScopeId, second.WorkflowDeterministicReceipt?.ReceiptScopeId);

        CommandResult<WorkspaceSaveReceipt> firstSave = workspaceService.Save(first.Id);
        CommandResult<WorkspaceSaveReceipt> secondSave = workspaceService.Save(second.Id);
        Assert.IsTrue(firstSave.Success);
        Assert.IsTrue(secondSave.Success);
        Assert.AreEqual(firstSave.Value?.ReceiptId, secondSave.Value?.ReceiptId);
        Assert.AreEqual(firstSave.Value?.WorkflowDeterministicReceipt?.ReceiptId, secondSave.Value?.WorkflowDeterministicReceipt?.ReceiptId);
        Assert.AreEqual(firstSave.Value?.WorkflowDeterministicReceipt?.ReceiptScopeId, secondSave.Value?.WorkflowDeterministicReceipt?.ReceiptScopeId);

        CommandResult<WorkspaceDownloadReceipt> firstDownload = workspaceService.Download(first.Id);
        CommandResult<WorkspaceDownloadReceipt> secondDownload = workspaceService.Download(second.Id);
        Assert.IsTrue(firstDownload.Success);
        Assert.IsTrue(secondDownload.Success);
        Assert.AreEqual(firstDownload.Value?.ReceiptId, secondDownload.Value?.ReceiptId);
        Assert.AreEqual(firstDownload.Value?.WorkflowDeterministicReceipt?.ReceiptId, secondDownload.Value?.WorkflowDeterministicReceipt?.ReceiptId);
        Assert.AreEqual(firstDownload.Value?.WorkflowDeterministicReceipt?.ReceiptScopeId, secondDownload.Value?.WorkflowDeterministicReceipt?.ReceiptScopeId);

        CommandResult<WorkspaceExportReceipt> firstExport = workspaceService.Export(first.Id);
        CommandResult<WorkspaceExportReceipt> secondExport = workspaceService.Export(second.Id);
        Assert.IsTrue(firstExport.Success);
        Assert.IsTrue(secondExport.Success);
        Assert.AreEqual(firstExport.Value?.PackageId, secondExport.Value?.PackageId);
        Assert.AreEqual(firstExport.Value?.WorkflowDeterministicReceipt?.ReceiptId, secondExport.Value?.WorkflowDeterministicReceipt?.ReceiptId);
        Assert.AreEqual(firstExport.Value?.WorkflowDeterministicReceipt?.ReceiptScopeId, secondExport.Value?.WorkflowDeterministicReceipt?.ReceiptScopeId);

        CommandResult<WorkspacePrintReceipt> firstPrint = workspaceService.Print(first.Id);
        CommandResult<WorkspacePrintReceipt> secondPrint = workspaceService.Print(second.Id);
        Assert.IsTrue(firstPrint.Success);
        Assert.IsTrue(secondPrint.Success);
        Assert.AreEqual(firstPrint.Value?.ReceiptId, secondPrint.Value?.ReceiptId);
        Assert.AreEqual(firstPrint.Value?.WorkflowDeterministicReceipt?.ReceiptId, secondPrint.Value?.WorkflowDeterministicReceipt?.ReceiptId);
        Assert.AreEqual(firstPrint.Value?.WorkflowDeterministicReceipt?.ReceiptScopeId, secondPrint.Value?.WorkflowDeterministicReceipt?.ReceiptScopeId);
    }

    [TestMethod]
    public void Import_save_download_export_and_section_parse_support_every_checked_in_chummer5_fixture()
    {
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        string[] sectionIds =
        [
            "profile",
            "progress",
            "rules",
            "build",
            "movement",
            "awakening",
            "spelldefense",
            "skills",
            "attributes",
            "inventory",
            "gear",
            "weapons",
            "weaponaccessories",
            "armors",
            "armormods",
            "cyberwares",
            "vehicles",
            "vehiclemods",
            "spells",
            "powers",
            "complexforms",
            "spirits",
            "sprites",
            "foci",
            "aiprograms",
            "martialarts",
            "metamagics",
            "arts",
            "initiationgrades",
            "critterpowers",
            "mentorspirits",
            "qualities",
            "contacts",
            "relationships",
            "enemies",
            "pets",
            "lifestyles",
            "sources",
            "expenses",
            "calendar",
            "improvements",
            "customdatadirectorynames",
            "karmasummary",
            "conditionmonitor",
            "build-lab"
        ];

        foreach (string fileName in LegacyChummer5FixtureCorpus.FileNames)
        {
            string xml = File.ReadAllText(LegacyChummer5FixtureCorpus.ResolvePath(fileName));
            InMemoryWorkspaceStore store = new();
            WorkspaceService workspaceService = CreateWorkspaceService(
                store,
                fileQueries,
                sectionQueries,
                metadataCommands,
                new Sr4WorkspaceCodec());

            CharacterValidationResult validation = fileQueries.Validate(new CharacterDocument(xml));
            Assert.IsTrue(validation.IsValid, $"{fileName} should remain a valid import fixture.");

            WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(
                xml,
                string.Empty,
                WorkspaceDocumentFormat.NativeXml));

            Assert.AreEqual(RulesetDefaults.Sr5, imported.RulesetId, $"{fileName} should import onto the SR5 lane.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Id.Value), $"{fileName} should create a workspace id.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Summary.Name), $"{fileName} should retain a character name.");
            Assert.AreEqual(WorkspacePortabilityCompatibilityStates.Compatible, imported.Portability?.CompatibilityState, $"{fileName} should import as native governed XML.");
            Assert.IsNotNull(imported.WorkflowDeterministicReceipt, $"{fileName} should emit workflow-state proof on import.");

            foreach (string sectionId in sectionIds)
            {
                object? section = workspaceService.GetSection(imported.Id, sectionId);
                Assert.IsNotNull(section, $"{fileName} should parse section '{sectionId}'.");
            }

            Assert.IsNotNull(workspaceService.GetProfile(imported.Id), $"{fileName} should expose profile data.");
            Assert.IsNotNull(workspaceService.GetProgress(imported.Id), $"{fileName} should expose progress data.");
            Assert.IsNotNull(workspaceService.GetRules(imported.Id), $"{fileName} should expose rules data.");
            Assert.IsNotNull(workspaceService.GetBuild(imported.Id), $"{fileName} should expose build data.");
            Assert.IsNotNull(workspaceService.GetMovement(imported.Id), $"{fileName} should expose movement data.");
            Assert.IsNotNull(workspaceService.GetAwakening(imported.Id), $"{fileName} should expose awakening data.");
            Assert.IsNotNull(workspaceService.GetSkills(imported.Id), $"{fileName} should expose skills data.");

            CommandResult<WorkspaceSaveReceipt> save = workspaceService.Save(imported.Id);
            Assert.IsTrue(save.Success, $"{fileName} should save after import.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(save.Value?.ReceiptId), $"{fileName} save should produce a receipt.");
            Assert.IsNotNull(save.Value?.WorkflowDeterministicReceipt, $"{fileName} save should emit workflow-state proof.");

            CommandResult<WorkspaceDownloadReceipt> download = workspaceService.Download(imported.Id);
            Assert.IsTrue(download.Success, $"{fileName} should download after import.");
            Assert.AreEqual(WorkspaceDocumentFormat.NativeXml, download.Value?.Format, $"{fileName} should remain a .chum5-native document.");
            Assert.IsTrue((download.Value?.DocumentLength ?? 0) > 0, $"{fileName} download should contain payload bytes.");
            Assert.IsNotNull(download.Value?.WorkflowDeterministicReceipt, $"{fileName} download should emit workflow-state proof.");

            CommandResult<WorkspaceExportReceipt> export = workspaceService.Export(imported.Id);
            Assert.IsTrue(export.Success, $"{fileName} should export after import.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(export.Value?.PackageId), $"{fileName} export should produce a portable package id.");
            Assert.IsNotNull(export.Value?.Portability, $"{fileName} export should include portability guidance.");
            Assert.IsNotNull(export.Value?.WorkflowDeterministicReceipt, $"{fileName} export should emit workflow-state proof.");

            Assert.IsTrue(workspaceService.Close(imported.Id), $"{fileName} should close cleanly after roundtrip verification.");
        }
    }

    [TestMethod]
    public void Import_save_download_export_and_section_parse_support_every_checked_in_chummer4_fixture()
    {
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        string[] sectionIds =
        [
            "profile",
            "progress",
            "rules",
            "build",
            "movement",
            "awakening",
            "spelldefense",
            "skills",
            "attributes",
            "inventory",
            "gear",
            "weapons",
            "weaponaccessories",
            "armors",
            "armormods",
            "cyberwares",
            "vehicles",
            "vehiclemods",
            "spells",
            "powers",
            "complexforms",
            "spirits",
            "sprites",
            "foci",
            "aiprograms",
            "martialarts",
            "metamagics",
            "arts",
            "initiationgrades",
            "critterpowers",
            "mentorspirits",
            "qualities",
            "contacts",
            "relationships",
            "enemies",
            "pets",
            "lifestyles",
            "sources",
            "expenses",
            "calendar",
            "improvements",
            "customdatadirectorynames",
            "karmasummary",
            "conditionmonitor",
            "build-lab"
        ];

        foreach (string fileName in LegacyChummer4FixtureCorpus.FileNames)
        {
            string xml = File.ReadAllText(LegacyChummer4FixtureCorpus.ResolvePath(fileName));
            InMemoryWorkspaceStore store = new();
            WorkspaceService workspaceService = CreateWorkspaceService(
                store,
                fileQueries,
                sectionQueries,
                metadataCommands,
                new Sr4WorkspaceCodec(fileQueries, sectionQueries, metadataCommands));

            CharacterValidationResult validation = fileQueries.Validate(new CharacterDocument(xml));
            Assert.IsTrue(validation.IsValid, $"{fileName} should remain a valid SR4 import fixture.");

            WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(
                xml,
                string.Empty,
                WorkspaceDocumentFormat.NativeXml));

            Assert.AreEqual(RulesetDefaults.Sr4, imported.RulesetId, $"{fileName} should import onto the SR4 lane.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Id.Value), $"{fileName} should create a workspace id.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(imported.Summary.Name), $"{fileName} should retain a character name.");
            Assert.AreEqual(WorkspacePortabilityCompatibilityStates.Compatible, imported.Portability?.CompatibilityState, $"{fileName} should import as native governed XML.");
            Assert.IsNotNull(imported.WorkflowDeterministicReceipt, $"{fileName} should emit workflow-state proof on import.");

            foreach (string sectionId in sectionIds)
            {
                object? section = workspaceService.GetSection(imported.Id, sectionId);
                Assert.IsNotNull(section, $"{fileName} should parse section '{sectionId}'.");
            }

            CharacterRulesSection? rules = workspaceService.GetRules(imported.Id);
            CharacterBuildSection? build = workspaceService.GetBuild(imported.Id);
            CharacterAttributesSection? attributes = workspaceService.GetSection(imported.Id, "attributes") as CharacterAttributesSection;
            CharacterSkillsSection? skills = workspaceService.GetSkills(imported.Id);
            CharacterInventorySection? inventory = workspaceService.GetSection(imported.Id, "inventory") as CharacterInventorySection;
            BuildLabConceptIntakeProjection? buildLab = workspaceService.GetSection(imported.Id, "build-lab") as BuildLabConceptIntakeProjection;

            Assert.IsNotNull(rules, $"{fileName} should expose SR4 rules data.");
            Assert.AreEqual("SR4", rules!.GameEdition, $"{fileName} should preserve the SR4 game-edition marker.");
            Assert.IsNotNull(build, $"{fileName} should expose SR4 build data.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(build!.BuildMethod), $"{fileName} should preserve a build method.");
            Assert.IsNotNull(attributes, $"{fileName} should expose SR4 attribute data.");
            Assert.IsTrue(attributes!.Count > 0, $"{fileName} should keep populated SR4 attributes.");
            Assert.IsNotNull(skills, $"{fileName} should expose SR4 skill data.");
            Assert.IsTrue(skills!.Count > 0, $"{fileName} should keep populated SR4 skills.");
            Assert.IsNotNull(inventory, $"{fileName} should expose SR4 inventory data.");
            Assert.IsTrue(inventory!.WeaponCount + inventory.GearCount + inventory.ArmorCount + inventory.CyberwareCount + inventory.VehicleCount > 0, $"{fileName} should keep populated SR4 inventory.");
            Assert.IsNotNull(buildLab, $"{fileName} should expose SR4 Build Lab data.");
            Assert.AreEqual(RulesetDefaults.Sr4, buildLab!.RulesetId, $"{fileName} should keep the SR4 Build Lab projection.");

            CommandResult<WorkspaceSaveReceipt> save = workspaceService.Save(imported.Id);
            Assert.IsTrue(save.Success, $"{fileName} should save after import.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(save.Value?.ReceiptId), $"{fileName} save should emit a deterministic receipt id.");
            Assert.IsNotNull(save.Value?.WorkflowDeterministicReceipt, $"{fileName} save should emit workflow-state proof.");
            CommandResult<WorkspaceDownloadReceipt> download = workspaceService.Download(imported.Id);
            Assert.IsTrue(download.Success, $"{fileName} should download after import.");
            Assert.AreEqual(WorkspaceDocumentFormat.NativeXml, download.Value?.Format, $"{fileName} should remain a .chum4-native document.");
            StringAssert.EndsWith(download.Value?.FileName ?? string.Empty, ".chum4");
            Assert.IsNotNull(download.Value?.WorkflowDeterministicReceipt, $"{fileName} download should emit workflow-state proof.");
            CommandResult<WorkspaceExportReceipt> export = workspaceService.Export(imported.Id);
            Assert.IsTrue(export.Success, $"{fileName} should export after import.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(export.Value?.PackageId), $"{fileName} export should produce a portable package id.");
            Assert.IsNotNull(export.Value?.WorkflowDeterministicReceipt, $"{fileName} export should emit workflow-state proof.");

            Assert.IsTrue(workspaceService.Close(imported.Id), $"{fileName} should close cleanly after roundtrip verification.");
        }
    }

    [TestMethod]
    public void Import_detects_sr4_ruleset_from_starter_fixture_when_ruleset_id_is_blank()
    {
        const string xml = "<character><name>Starter Shadow</name><alias>Starter</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>4.0</createdversion><appversion>4.0</appversion><karma>0</karma><nuyen>5000</nuyen><created>True</created><gameedition>SR4</gameedition></character>";

        IWorkspaceStore store = new InMemoryWorkspaceStore();
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        WorkspaceService workspaceService = CreateWorkspaceService(
            store,
            fileQueries,
            sectionQueries,
            metadataCommands,
            new Sr4WorkspaceCodec());

        WorkspaceImportResult imported = workspaceService.Import(new WorkspaceImportDocument(
            xml,
            string.Empty,
            WorkspaceDocumentFormat.NativeXml));

        Assert.AreEqual("sr4", imported.RulesetId);
        Assert.AreEqual("Starter Shadow", imported.Summary.Name);
        CharacterRulesSection? rules = workspaceService.GetRules(imported.Id);
        Assert.IsNotNull(rules);
        Assert.AreEqual("SR4", rules.GameEdition);
    }

    [TestMethod]
    public void Import_requires_explicit_or_detectable_ruleset()
    {
        IWorkspaceStore store = new InMemoryWorkspaceStore();
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        WorkspaceService workspaceService = CreateWorkspaceService(store, fileQueries, sectionQueries, metadataCommands);

        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() => workspaceService.Import(
            new WorkspaceImportDocument(
                "<character><name>No Ruleset</name></character>",
                string.Empty,
                WorkspaceDocumentFormat.NativeXml)));

        Assert.AreEqual("Workspace ruleset is required or must be detectable from import content.", ex.Message);
    }

    [TestMethod]
    public void List_honors_maxCount_parameter()
    {
        const string xmlTemplate = "<character><name>{0}</name><alias>{0}</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>";
        IWorkspaceStore store = new InMemoryWorkspaceStore();
        ICharacterFileQueries fileQueries = new XmlCharacterFileQueries(new CharacterFileService());
        ICharacterSectionQueries sectionQueries = new XmlCharacterSectionQueries(new CharacterSectionService());
        ICharacterMetadataCommands metadataCommands = new XmlCharacterMetadataCommands(new CharacterFileService());
        WorkspaceService workspaceService = CreateWorkspaceService(store, fileQueries, sectionQueries, metadataCommands);

        workspaceService.Import(new WorkspaceImportDocument(string.Format(xmlTemplate, "One"), RulesetDefaults.Sr5, WorkspaceDocumentFormat.NativeXml));
        workspaceService.Import(new WorkspaceImportDocument(string.Format(xmlTemplate, "Two"), RulesetDefaults.Sr5, WorkspaceDocumentFormat.NativeXml));
        workspaceService.Import(new WorkspaceImportDocument(string.Format(xmlTemplate, "Three"), RulesetDefaults.Sr5, WorkspaceDocumentFormat.NativeXml));

        IReadOnlyList<WorkspaceListItem> fullList = workspaceService.List();
        IReadOnlyList<WorkspaceListItem> cappedList = workspaceService.List(maxCount: 2);

        Assert.HasCount(3, fullList);
        Assert.HasCount(2, cappedList);
        Assert.IsTrue(cappedList.All(item => fullList.Any(full => string.Equals(full.Id.Value, item.Id.Value, StringComparison.Ordinal))));
    }

    [TestMethod]
    public void GetSummary_uses_codec_defaults_when_document_envelope_metadata_is_incomplete()
    {
        InMemoryWorkspaceStore store = new();
        CharacterWorkspaceId id = store.Create(new WorkspaceDocument(
            PayloadEnvelope: new WorkspacePayloadEnvelope(
                RulesetId: "sr6",
                SchemaVersion: 0,
                PayloadKind: string.Empty,
                Payload: "<codec-payload/>"),
            Format: WorkspaceDocumentFormat.NativeXml));
        RecordingWorkspaceCodec codec = new();
        WorkspaceService workspaceService = new(store, new RulesetWorkspaceCodecResolver([codec]), new WorkspaceImportRulesetDetector());

        CharacterFileSummary? summary = workspaceService.GetSummary(id);

        Assert.IsNotNull(summary);
        Assert.IsNotNull(codec.LastSummaryEnvelope);
        Assert.AreEqual("sr6", codec.LastSummaryEnvelope.RulesetId);
        Assert.AreEqual(7, codec.LastSummaryEnvelope.SchemaVersion);
        Assert.AreEqual("sr6/custom-payload", codec.LastSummaryEnvelope.PayloadKind);
        Assert.AreEqual("<codec-payload/>", codec.LastSummaryEnvelope.Payload);
    }

    [TestMethod]
    public void Download_delegates_file_shape_to_ruleset_codec()
    {
        InMemoryWorkspaceStore store = new();
        CharacterWorkspaceId id = store.Create(new WorkspaceDocument(
            PayloadEnvelope: new WorkspacePayloadEnvelope(
                RulesetId: "sr6",
                SchemaVersion: 0,
                PayloadKind: string.Empty,
                Payload: "<codec-download/>"),
            Format: WorkspaceDocumentFormat.NativeXml));
        RecordingWorkspaceCodec codec = new();
        WorkspaceService workspaceService = new(store, new RulesetWorkspaceCodecResolver([codec]), new WorkspaceImportRulesetDetector());

        CommandResult<WorkspaceDownloadReceipt> result = workspaceService.Download(id);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Value);
        Assert.IsNotNull(codec.LastDownloadEnvelope);
        Assert.AreEqual("codec-export.sr6pkg", result.Value.FileName);
        Assert.AreEqual("sr6", result.Value.RulesetId);
        Assert.AreEqual(7, codec.LastDownloadEnvelope.SchemaVersion);
        Assert.AreEqual("sr6/custom-payload", codec.LastDownloadEnvelope.PayloadKind);
        Assert.AreEqual(16, result.Value.DocumentLength);
    }

    [TestMethod]
    public void Export_builds_receipt_from_ruleset_codec_sections()
    {
        InMemoryWorkspaceStore store = new();
        CharacterWorkspaceId id = store.Create(new WorkspaceDocument(
            PayloadEnvelope: new WorkspacePayloadEnvelope(
                RulesetId: "sr6",
                SchemaVersion: 0,
                PayloadKind: string.Empty,
                Payload: "<codec-export/>"),
            Format: WorkspaceDocumentFormat.NativeXml));
        RecordingWorkspaceCodec codec = new();
        WorkspaceService workspaceService = new(store, new RulesetWorkspaceCodecResolver([codec]), new WorkspaceImportRulesetDetector());

        CommandResult<WorkspaceExportReceipt> result = workspaceService.Export(id);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Value);
        Assert.IsNotNull(codec.LastExportEnvelope);
        Assert.AreEqual("sr6", codec.LastExportEnvelope.RulesetId);
        Assert.AreEqual(7, codec.LastExportEnvelope.SchemaVersion);
        Assert.AreEqual("sr6/custom-payload", codec.LastExportEnvelope.PayloadKind);
        Assert.AreEqual("Codec Runner-export.json", result.Value.FileName);
        Assert.AreEqual(WorkspaceDocumentFormat.Json, result.Value.Format);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Value.PackageId));
        Assert.AreEqual(WorkspacePortabilityFormatIds.PortableDossierV1, result.Value.Portability?.FormatId);
        Assert.AreEqual(WorkspacePortabilityCompatibilityStates.Compatible, result.Value.Portability?.CompatibilityState);
        Assert.AreEqual(WorkspacePortabilityOutputKinds.PortableDossier, result.Value.Portability?.OutputKind);
        Assert.AreEqual(WorkspacePortabilityLossStates.None, result.Value.Portability?.Loss?.State);
        Assert.AreEqual(result.Value.PackageId, result.Value.Portability?.Provenance?.ReceiptId);
        Assert.AreEqual(WorkspacePortabilityRevocationStates.Revocable, result.Value.Portability?.Revocation?.State);
        Assert.AreEqual("workspace-portability:portable-dossier", result.Value.Portability?.Revocation?.FamilyId);
        CollectionAssert.AreEqual(new[] { id.Value }, result.Value.Portability?.Revocation?.SupersedesArtifactIds?.ToArray());
        Assert.AreEqual(2, result.Value.Portability?.Lineage?.Count);
        Assert.AreEqual(5, result.Value.Portability?.RelatedOutputs?.Count);
        string packageId = result.Value.PackageId;
        CollectionAssert.AreEquivalent(
            new[]
            {
                WorkspacePortabilityOutputKinds.PortableDossier,
                WorkspacePortabilityOutputKinds.CampaignBundle,
                WorkspacePortabilityOutputKinds.ReplayTimeline,
                WorkspacePortabilityOutputKinds.SessionRecap,
                WorkspacePortabilityOutputKinds.ExternalExchange
            },
            result.Value.Portability?.RelatedOutputs?.Select(static output => output.OutputKind).ToArray());
        Assert.IsTrue(
            result.Value.Portability?.RelatedOutputs?.Any(static output => output.OutputKind == WorkspacePortabilityOutputKinds.PortableDossier
                && string.Equals(output.Provenance.SourceArtifactId, output.Lineage[0].ArtifactId, StringComparison.Ordinal)
                && string.Equals(output.Provenance.SourceFormatId, WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1, StringComparison.Ordinal)
                && string.Equals(output.Lineage[2].FormatId, WorkspacePortabilityFormatIds.PortableDossierV1, StringComparison.Ordinal)) == true);
        Assert.IsTrue(
            result.Value.Portability?.RelatedOutputs?.Where(static output => output.OutputKind != WorkspacePortabilityOutputKinds.PortableDossier)
                .All(output =>
                    string.Equals(output.Provenance.SourceArtifactId, packageId, StringComparison.Ordinal)
                    && string.Equals(output.Provenance.SourceFormatId, WorkspacePortabilityFormatIds.PortableDossierV1, StringComparison.Ordinal)
                    && string.Equals(output.Lineage[1].ArtifactId, packageId, StringComparison.Ordinal)) == true);
        Assert.IsTrue(
            result.Value.Portability?.RelatedOutputs?.All(static output => output.Lineage.Count == 3
                && output.Compatibility.State == WorkspacePortabilityCompatibilityStates.Compatible
                && output.Loss.State == WorkspacePortabilityLossStates.None
                && output.Provenance.ReceiptId.Length > 0
                && output.PortabilityEnvelope.SupportedExchangeModes.Count > 0
                && output.Revocation.State == WorkspacePortabilityRevocationStates.Revocable
                && string.Equals(output.Revocation.Scope, "governed-replace", StringComparison.Ordinal)) == true);
        Assert.IsTrue(
            result.Value.Portability?.RelatedOutputs?.Any(static output => output.OutputKind == WorkspacePortabilityOutputKinds.CampaignBundle
                && string.Equals(output.WorkflowId, "workflow.campaign.bundle", StringComparison.Ordinal)
                && string.Equals(output.Lineage[2].FormatId, WorkspacePortabilityFormatIds.CampaignBundleV1, StringComparison.Ordinal)
                && string.Equals(output.Revocation.FamilyId, "workspace-portability:campaign-bundle", StringComparison.Ordinal)
                && output.Summary.Contains("Campaign federation", StringComparison.Ordinal)) == true);
        Assert.IsTrue(
            result.Value.Portability?.RelatedOutputs?.Any(static output => output.OutputKind == WorkspacePortabilityOutputKinds.ReplayTimeline
                && string.Equals(output.WorkflowId, "workflow.replay.timeline", StringComparison.Ordinal)
                && string.Equals(output.Lineage[2].FormatId, WorkspacePortabilityFormatIds.ReplayTimelineV1, StringComparison.Ordinal)
                && output.PortabilityEnvelope.SupportedExchangeModes.SequenceEqual(
                    new[] { WorkspacePortabilityExchangeModes.InspectOnly, WorkspacePortabilityExchangeModes.Merge })) == true);
        Assert.IsTrue(
            result.Value.Portability?.RelatedOutputs?.Any(static output => output.OutputKind == WorkspacePortabilityOutputKinds.SessionRecap
                && string.Equals(output.WorkflowId, "workflow.recap.session", StringComparison.Ordinal)
                && string.Equals(output.Lineage[2].FormatId, WorkspacePortabilityFormatIds.SessionRecapV1, StringComparison.Ordinal)
                && output.Summary.Contains("Session recap", StringComparison.Ordinal)) == true);
        Assert.IsTrue(
            result.Value.Portability?.RelatedOutputs?.Any(static output => output.OutputKind == WorkspacePortabilityOutputKinds.ExternalExchange
                && string.Equals(output.WorkflowId, "workflow.exchange.external", StringComparison.Ordinal)
                && string.Equals(output.Lineage[2].FormatId, WorkspacePortabilityFormatIds.ExternalExchangeV1, StringComparison.Ordinal)
                && output.Summary.Contains("External exchange", StringComparison.Ordinal)) == true);
        StringAssert.Contains(result.Value.Portability?.ReceiptSummary ?? string.Empty, "Portable export is ready");
        StringAssert.Contains(result.Value.Portability?.ProvenanceSummary ?? string.Empty, id.Value);
        string payload = Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.ContentBase64));
        StringAssert.Contains(payload, "\"Name\": \"Codec Runner\"");
        StringAssert.Contains(payload, "\"Reaction\"");
        StringAssert.Contains(payload, "\"Fixer\"");
    }

    [TestMethod]
    public void Export_marks_bounded_loss_when_portable_sections_are_missing()
    {
        InMemoryWorkspaceStore store = new();
        CharacterWorkspaceId id = store.Create(new WorkspaceDocument(
            PayloadEnvelope: new WorkspacePayloadEnvelope(
                RulesetId: "sr6",
                SchemaVersion: 0,
                PayloadKind: string.Empty,
                Payload: "<codec-export/>"),
            Format: WorkspaceDocumentFormat.NativeXml));
        RecordingWorkspaceCodec codec = new(includeContacts: false);
        WorkspaceService workspaceService = new(store, new RulesetWorkspaceCodecResolver([codec]), new WorkspaceImportRulesetDetector());

        CommandResult<WorkspaceExportReceipt> result = workspaceService.Export(id);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Value?.Portability);
        Assert.AreEqual(WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings, result.Value.Portability.CompatibilityState);
        Assert.AreEqual(WorkspacePortabilityLossStates.BoundedLoss, result.Value.Portability.Loss?.State);
        CollectionAssert.AreEqual(new[] { "contacts" }, result.Value.Portability.Loss?.AffectedSections?.ToArray());
        CollectionAssert.AreEqual(new[] { "missing-sections" }, result.Value.Portability.Compatibility?.WarningCodes?.ToArray());
        StringAssert.Contains(result.Value.Portability.PortabilityEnvelope?.Summary ?? string.Empty, "Inspect-first dossier portability");
        Assert.IsTrue(
            result.Value.Portability.RelatedOutputs?.All(static output =>
                output.Compatibility.State == WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings
                && output.Loss.State == WorkspacePortabilityLossStates.BoundedLoss
                && output.Loss.AffectedSections.SequenceEqual(new[] { "contacts" })
                && output.Revocation.State == WorkspacePortabilityRevocationStates.Revocable) == true);
    }

    [TestMethod]
    public void GetSection_rebinds_workspace_id_for_build_lab_projection_from_codec()
    {
        InMemoryWorkspaceStore store = new();
        CharacterWorkspaceId id = store.Create(new WorkspaceDocument(
            PayloadEnvelope: new WorkspacePayloadEnvelope(
                RulesetId: "sr6",
                SchemaVersion: 0,
                PayloadKind: string.Empty,
                Payload: "<codec-build-lab/>"),
            Format: WorkspaceDocumentFormat.NativeXml));
        RecordingWorkspaceCodec codec = new();
        WorkspaceService workspaceService = new(store, new RulesetWorkspaceCodecResolver([codec]), new WorkspaceImportRulesetDetector());

        BuildLabConceptIntakeProjection? projection = workspaceService.GetSection(id, "build-lab") as BuildLabConceptIntakeProjection;

        Assert.IsNotNull(projection);
        Assert.AreEqual(id.Value, projection.WorkspaceId);
        Assert.AreEqual("workflow.build-lab", projection.WorkflowId);
        Assert.AreEqual("Codec Runner Build Lab Intake", projection.Title);
    }

    [TestMethod]
    public void Sr4_and_sr6_codecs_expose_build_lab_sections()
    {
        const string xml = "<character><name>Codec Runner</name><alias>Runner</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>";
        WorkspacePayloadEnvelope sr4Envelope = new(RulesetDefaults.Sr4, 1, Sr4WorkspaceCodec.Sr4PayloadKind, xml);
        WorkspacePayloadEnvelope sr6Envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, xml);

        BuildLabConceptIntakeProjection? sr4Projection = new Sr4WorkspaceCodec().ParseSection("build-lab", sr4Envelope) as BuildLabConceptIntakeProjection;
        BuildLabConceptIntakeProjection? sr6Projection = CreateSr6WorkspaceCodec().ParseSection("build-lab", sr6Envelope) as BuildLabConceptIntakeProjection;

        Assert.IsNotNull(sr4Projection);
        Assert.IsNotNull(sr6Projection);
        Assert.AreEqual("sr4", sr4Projection.RulesetId);
        Assert.AreEqual("sr6", sr6Projection.RulesetId);
        Assert.IsTrue(sr4Projection.Variants.Count > 0);
        Assert.IsTrue(sr6Projection.Variants.Count > 0);
    }

    [TestMethod]
    public void Missing_workspace_operations_return_null_or_not_found_results()
    {
        TrackingWorkspaceStore store = new();
        WorkspaceService workspaceService = new(
            store,
            new RulesetWorkspaceCodecResolver([new RecordingWorkspaceCodec()]),
            new WorkspaceImportRulesetDetector());
        CharacterWorkspaceId missingId = new("missing-workspace");

        Assert.IsNull(workspaceService.GetSection(missingId, "profile"));
        Assert.IsNull(workspaceService.GetSummary(missingId));
        Assert.IsNull(workspaceService.Validate(missingId));
        Assert.IsNull(workspaceService.GetProfile(missingId));
        Assert.IsNull(workspaceService.GetProgress(missingId));
        Assert.IsNull(workspaceService.GetSkills(missingId));
        Assert.IsNull(workspaceService.GetRules(missingId));
        Assert.IsNull(workspaceService.GetBuild(missingId));
        Assert.IsNull(workspaceService.GetMovement(missingId));
        Assert.IsNull(workspaceService.GetAwakening(missingId));

        CommandResult<CharacterProfileSection> update = workspaceService.UpdateMetadata(missingId, new UpdateWorkspaceMetadata("Name", "Alias", "Notes"));
        CommandResult<WorkspaceSaveReceipt> save = workspaceService.Save(missingId);
        CommandResult<WorkspaceDownloadReceipt> download = workspaceService.Download(missingId);
        CommandResult<WorkspaceExportReceipt> export = workspaceService.Export(missingId);
        CommandResult<WorkspacePrintReceipt> print = workspaceService.Print(missingId);

        Assert.IsFalse(update.Success);
        Assert.AreEqual("Workspace not found.", update.Error);
        Assert.IsFalse(save.Success);
        Assert.AreEqual("Workspace not found.", save.Error);
        Assert.IsFalse(download.Success);
        Assert.AreEqual("Workspace not found.", download.Error);
        Assert.IsFalse(export.Success);
        Assert.AreEqual("Workspace not found.", export.Error);
        Assert.IsFalse(print.Success);
        Assert.AreEqual("Workspace not found.", print.Error);
        Assert.IsFalse(workspaceService.Close(missingId));
    }

    [TestMethod]
    public void Validate_rejects_well_formed_xml_that_the_canonical_ruleset_cannot_open()
    {
        InMemoryWorkspaceStore store = new();
        CharacterWorkspaceId id = store.Create(new WorkspaceDocument(
            "<character><name>Syntax Only</name></character>",
            RulesetDefaults.Sr5,
            WorkspaceDocumentFormat.NativeXml));
        WorkspaceService workspaceService = CreateWorkspaceService(
            store,
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));

        CharacterValidationResult? validation = workspaceService.Validate(id);

        Assert.IsNotNull(validation);
        Assert.IsFalse(validation.IsValid);
        Assert.IsGreaterThan(0, validation.Issues.Count);
    }

    [DataTestMethod]
    [DataRow("schema")]
    [DataRow("ruleset")]
    [DataRow("payload-kind")]
    public void Validate_rejects_valid_syntax_with_noncanonical_envelope(string invalidField)
    {
        WorkspaceDocument baseline = new(
            "<character><name>Envelope Runner</name><alias>ENVELOPE</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>",
            RulesetDefaults.Sr5,
            WorkspaceDocumentFormat.NativeXml);
        WorkspaceDocument invalid = invalidField switch
        {
            "schema" => baseline with
            {
                State = baseline.State with { SchemaVersion = baseline.SchemaVersion + 1 }
            },
            "ruleset" => baseline with
            {
                State = baseline.State with { RulesetId = "sr999" }
            },
            "payload-kind" => baseline with
            {
                State = baseline.State with { PayloadKind = "sr5/not-canonical" }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField))
        };
        InMemoryWorkspaceStore store = new();
        CharacterWorkspaceId id = store.Create(invalid);
        WorkspaceService workspaceService = CreateWorkspaceService(
            store,
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));

        CharacterValidationResult? validation = workspaceService.Validate(id);

        Assert.IsNotNull(validation);
        Assert.IsFalse(validation.IsValid);
        Assert.IsGreaterThan(0, validation.Issues.Count);
    }

    [TestMethod]
    public void UpdateMetadata_returns_error_when_codec_does_not_return_profile_section()
    {
        InMemoryWorkspaceStore store = new();
        CharacterWorkspaceId id = store.Create(new WorkspaceDocument(
            PayloadEnvelope: new WorkspacePayloadEnvelope(
                RulesetId: "sr6",
                SchemaVersion: 1,
                PayloadKind: "sr6/custom-payload",
                Payload: "<codec-update/>"),
            Format: WorkspaceDocumentFormat.NativeXml));
        WorkspaceService workspaceService = new(
            store,
            new RulesetWorkspaceCodecResolver([new ProfilelessUpdateWorkspaceCodec()]),
            new WorkspaceImportRulesetDetector());

        CommandResult<CharacterProfileSection> result = workspaceService.UpdateMetadata(
            id,
            new UpdateWorkspaceMetadata("Updated", "Alias", "Notes"));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Profile section was not available after metadata update.", result.Error);
    }

    [TestMethod]
    public void List_skips_missing_entries_uses_fallback_summary_and_treats_nonpositive_max_as_unbounded()
    {
        ListWorkspaceStore store = new();
        CharacterWorkspaceId goodId = new("good-workspace");
        CharacterWorkspaceId badId = new("bad-workspace");
        CharacterWorkspaceId missingId = new("missing-workspace");

        store.Seed(goodId, new WorkspaceDocument(
            PayloadEnvelope: new WorkspacePayloadEnvelope("sr6", 1, "sr6/custom-payload", "<good/>"),
            Format: WorkspaceDocumentFormat.NativeXml));
        store.Seed(badId, new WorkspaceDocument(
            PayloadEnvelope: new WorkspacePayloadEnvelope("sr6", 1, "sr6/custom-payload", "<bad/>"),
            Format: WorkspaceDocumentFormat.NativeXml));
        store.SeedMissing(missingId);

        WorkspaceService workspaceService = new(
            store,
            new RulesetWorkspaceCodecResolver([new FlakySummaryWorkspaceCodec()]),
            new WorkspaceImportRulesetDetector());

        IReadOnlyList<WorkspaceListItem> zeroList = workspaceService.List(maxCount: 0);
        IReadOnlyList<WorkspaceListItem> negativeList = workspaceService.List(maxCount: -5);

        Assert.HasCount(2, zeroList);
        Assert.HasCount(2, negativeList);
        Assert.IsFalse(zeroList.Any(item => string.Equals(item.Id.Value, missingId.Value, StringComparison.Ordinal)));
        Assert.AreEqual("Codec Runner", zeroList.Single(item => string.Equals(item.Id.Value, goodId.Value, StringComparison.Ordinal)).Summary.Name);
        Assert.AreEqual($"Workspace {badId.Value}", zeroList.Single(item => string.Equals(item.Id.Value, badId.Value, StringComparison.Ordinal)).Summary.Name);
    }

    private sealed class TrackingWorkspaceStore : IWorkspaceStore
    {
        private readonly InMemoryWorkspaceStore _inner = new();

        public int CreateCallCount { get; private set; }

        public OwnerScope? LastCreateOwner { get; private set; }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
        {
            CreateCallCount++;
            LastCreateOwner = null;
            return _inner.CreateWorkspaceDocument(document);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document)
        {
            CreateCallCount++;
            LastCreateOwner = owner;
            return _inner.CreateWorkspaceDocument(owner, document);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
        {
            CreateCallCount++;
            LastCreateOwner = null;
            return _inner.CreateWorkspaceDocument(id, document);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            WorkspaceDocument document)
        {
            CreateCallCount++;
            LastCreateOwner = owner;
            return _inner.CreateWorkspaceDocument(owner, id, document);
        }

        public IReadOnlyList<WorkspaceStoreEntry> List()
        {
            return _inner.List();
        }

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner)
        {
            return _inner.List(owner);
        }

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => _inner.Get(id);

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => _inner.Get(owner, id);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
            => _inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
            => _inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision)
            => _inner.SaveCheckpoint(id, expectedContentRevision);

        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
            => _inner.SaveCheckpoint(owner, id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision)
            => _inner.Delete(id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
            => _inner.Delete(owner, id, expectedContentRevision);
    }

    private sealed class ThrowingCharacterFileQueries : ICharacterFileQueries
    {
        public CharacterFileSummary ParseSummary(CharacterDocument document)
        {
            throw new FormatException("Malformed summary payload.");
        }

        public CharacterValidationResult Validate(CharacterDocument document)
        {
            return new CharacterValidationResult(false, []);
        }
    }

    private sealed class NoopCharacterSectionQueries : ICharacterSectionQueries
    {
        public object ParseSection(string sectionId, CharacterDocument document)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NoopCharacterMetadataCommands : ICharacterMetadataCommands
    {
        public UpdateCharacterMetadataResult UpdateMetadata(UpdateCharacterMetadataCommand command)
        {
            throw new NotSupportedException();
        }
    }

    private static WorkspaceService CreateWorkspaceService(
        IWorkspaceStore workspaceStore,
        ICharacterFileQueries fileQueries,
        ICharacterSectionQueries sectionQueries,
        ICharacterMetadataCommands metadataCommands,
        params IRulesetWorkspaceCodec[] additionalCodecs)
    {
        IRulesetWorkspaceCodec[] codecs =
        [
            new Sr5WorkspaceCodec(
                fileQueries,
                sectionQueries,
                metadataCommands),
            new Sr6WorkspaceCodec(
                fileQueries,
                sectionQueries,
                metadataCommands),
            .. additionalCodecs
        ];
        IRulesetWorkspaceCodecResolver resolver = new RulesetWorkspaceCodecResolver(
            codecs);
        return new WorkspaceService(workspaceStore, resolver, new WorkspaceImportRulesetDetector());
    }

    private static Sr6WorkspaceCodec CreateSr6WorkspaceCodec()
        => new(
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));

    private sealed class RecordingWorkspaceCodec : IRulesetWorkspaceCodec
    {
        private readonly bool _includeContacts;

        public RecordingWorkspaceCodec(bool includeContacts = true)
        {
            _includeContacts = includeContacts;
        }

        public string RulesetId => "sr6";

        public int SchemaVersion => 7;

        public string PayloadKind => "sr6/custom-payload";

        public WorkspacePayloadEnvelope? LastSummaryEnvelope { get; private set; }

        public WorkspacePayloadEnvelope? LastDownloadEnvelope { get; private set; }

        public WorkspacePayloadEnvelope? LastExportEnvelope { get; private set; }

        public WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document)
        {
            return new WorkspacePayloadEnvelope(
                RulesetId: RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty,
                SchemaVersion: SchemaVersion,
                PayloadKind: PayloadKind,
                Payload: document.Content);
        }

        public CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope)
        {
            LastSummaryEnvelope = envelope;
            return new CharacterFileSummary(
                Name: "Codec Runner",
                Alias: "SR6",
                Metatype: string.Empty,
                BuildMethod: string.Empty,
                CreatedVersion: string.Empty,
                AppVersion: string.Empty,
                Karma: 0m,
                Nuyen: 0m,
                Created: false);
        }

        public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope)
        {
            return sectionId switch
            {
                "build-lab" => new BuildLabConceptIntakeProjection(
                    WorkspaceId: "pending-workspace",
                    WorkflowId: "workflow.build-lab",
                    Title: "Codec Runner Build Lab Intake",
                    Summary: "Codec-provided Build Lab payload.",
                    RulesetId: "sr6",
                    BuildMethod: "Priority",
                    IntakeFields:
                    [
                        new BuildLabIntakeField("concept", "Concept", BuildLabFieldKinds.Text, "Codec Runner")
                    ],
                    RoleBadges:
                    [
                        new BuildLabBadge("street-samurai", "Street Samurai", BuildLabBadgeKinds.Role, true)
                    ],
                    ConstraintBadges: [],
                    ProvenanceBadges: [],
                    Variants: [],
                    ProgressionTimelines: [],
                    Actions: []),
                "profile" => new CharacterProfileSection(
                    Name: "Codec Runner",
                    Alias: "SR6",
                    PlayerName: string.Empty,
                    Metatype: "Human",
                    Metavariant: string.Empty,
                    Sex: string.Empty,
                    Age: string.Empty,
                    Height: string.Empty,
                    Weight: string.Empty,
                    Hair: string.Empty,
                    Eyes: string.Empty,
                    Skin: string.Empty,
                    Concept: string.Empty,
                    Description: string.Empty,
                    Background: string.Empty,
                    CreatedVersion: string.Empty,
                    AppVersion: string.Empty,
                    BuildMethod: "Priority",
                    GameplayOption: string.Empty,
                    Created: true,
                    Adept: false,
                    Magician: false,
                    Technomancer: false,
                    AI: false,
                    MainMugshotIndex: 0,
                    MugshotCount: 0),
                "progress" => new CharacterProgressSection(0m, 0m, 0m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6m, 0, 0, false, false, false),
                "attributes" => new CharacterAttributesSection(1, [new CharacterAttributeSummary("Reaction", 5, 7)]),
                "skills" => new CharacterSkillsSection(1, 0, [new CharacterSkillSummary("skill-1", string.Empty, "Combat", false, 6, 0, ["Pistols"])]),
                "inventory" => new CharacterInventorySection(1, 0, 0, 0, 0, ["Medkit"], [], [], [], []),
                "qualities" => new CharacterQualitiesSection(1, [new CharacterQualitySummary("First Impression", "Core", 11)]),
                "contacts" => new CharacterContactsSection(1, [new CharacterContactSummary("Fixer", "Broker", "Seattle", 4, 3)]),
                _ => throw new NotSupportedException()
            };
        }

        public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
        {
            throw new NotSupportedException();
        }

        public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command)
        {
            throw new NotSupportedException();
        }

        public WorkspaceDownloadReceipt BuildDownload(CharacterWorkspaceId id, WorkspacePayloadEnvelope envelope, WorkspaceDocumentFormat format)
        {
            LastDownloadEnvelope = envelope;
            return new WorkspaceDownloadReceipt(
                Id: id,
                Format: format,
                ContentBase64: Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("codec-download")),
                FileName: "codec-export.sr6pkg",
                DocumentLength: 16,
                RulesetId: envelope.RulesetId);
        }

        public DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope)
        {
            LastExportEnvelope = envelope;
            return new DataExportBundle(
                Summary: ParseSummary(envelope),
                Profile: (CharacterProfileSection)ParseSection("profile", envelope),
                Progress: (CharacterProgressSection)ParseSection("progress", envelope),
                Attributes: (CharacterAttributesSection)ParseSection("attributes", envelope),
                Skills: (CharacterSkillsSection)ParseSection("skills", envelope),
                Inventory: (CharacterInventorySection)ParseSection("inventory", envelope),
                Qualities: (CharacterQualitiesSection)ParseSection("qualities", envelope),
                Contacts: _includeContacts ? (CharacterContactsSection)ParseSection("contacts", envelope) : null);
        }
    }

    private sealed class ProfilelessUpdateWorkspaceCodec : IRulesetWorkspaceCodec
    {
        public string RulesetId => "sr6";

        public int SchemaVersion => 1;

        public string PayloadKind => "sr6/custom-payload";

        public WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document)
            => new(RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty, SchemaVersion, PayloadKind, document.Content);

        public CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope)
            => new("Codec Runner", "Alias", "Human", "Priority", "1.0", "1.0", 0m, 0m, true);

        public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope)
            => sectionId switch
            {
                "profile" => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["status"] = "missing-profile"
                },
                _ => new object()
            };

        public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
            => new(true, []);

        public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command)
            => envelope with { Payload = "<updated/>" };

        public WorkspaceDownloadReceipt BuildDownload(CharacterWorkspaceId id, WorkspacePayloadEnvelope envelope, WorkspaceDocumentFormat format)
            => throw new NotSupportedException();

        public DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope)
            => throw new NotSupportedException();
    }

    private sealed class FlakySummaryWorkspaceCodec : IRulesetWorkspaceCodec
    {
        public string RulesetId => "sr6";

        public int SchemaVersion => 1;

        public string PayloadKind => "sr6/custom-payload";

        public WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document)
            => new(RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty, SchemaVersion, PayloadKind, document.Content);

        public CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope)
        {
            if (string.Equals(envelope.Payload, "<bad/>", StringComparison.Ordinal))
            {
                throw new FormatException("bad summary");
            }

            return new CharacterFileSummary("Codec Runner", "SR6", "Human", "Priority", "1.0", "1.0", 0m, 0m, false);
        }

        public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope) => throw new NotSupportedException();

        public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope) => new(true, []);

        public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command) => envelope;

        public WorkspaceDownloadReceipt BuildDownload(CharacterWorkspaceId id, WorkspacePayloadEnvelope envelope, WorkspaceDocumentFormat format) => throw new NotSupportedException();

        public DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope) => throw new NotSupportedException();
    }

    private sealed class ListWorkspaceStore : IWorkspaceStore
    {
        private readonly Dictionary<string, WorkspaceStoredDocument> _documents = new(StringComparer.Ordinal);
        private readonly List<WorkspaceStoreEntry> _entries = [];

        public void Seed(CharacterWorkspaceId id, WorkspaceDocument document)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            _documents[id.Value] = new WorkspaceStoredDocument(id, document, 1, 0, now);
            _entries.Add(new WorkspaceStoreEntry(id, now, 1, 0));
        }

        public void SeedMissing(CharacterWorkspaceId id)
        {
            _entries.Add(new WorkspaceStoreEntry(id, DateTimeOffset.UtcNow, 1, 0));
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => CreateWorkspaceDocument(OwnerScope.LocalSingleUser, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document)
        {
            CharacterWorkspaceId id = new(Guid.NewGuid().ToString("N"));
            Seed(id, document);
            return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Success, _entries[^1]);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => CreateWorkspaceDocument(OwnerScope.LocalSingleUser, id, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            WorkspaceDocument document)
        {
            if (_documents.ContainsKey(id.Value))
            {
                return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Conflict);
            }

            Seed(id, document);
            return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Success, _entries[^1]);
        }

        public IReadOnlyList<WorkspaceStoreEntry> List() => List(OwnerScope.LocalSingleUser);

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => _entries;

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => Get(OwnerScope.LocalSingleUser, id);

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
            => _documents.TryGetValue(id.Value, out WorkspaceStoredDocument? document)
                ? new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Success, document)
                : new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Missing);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
            => UnsupportedMutation();

        private static WorkspaceStoreMutationResult UnsupportedMutation()
            => new(WorkspaceOperationOutcome.Unavailable, Error: "List fixture does not support mutations.");
    }
}
