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

        var download = workspaceService.Download(imported.Id);
        Assert.IsTrue(download.Success);
        Assert.AreEqual("sr5", download.Value?.RulesetId);

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
        Assert.IsTrue(projection.ExportTargets?.Any(target => string.Equals(target.TargetId, "target.json-exchange", StringComparison.Ordinal)
            && string.Equals(target.WorkflowId, "workflow.exchange.json", StringComparison.Ordinal)) == true);
        Assert.IsTrue(projection.ExportTargets?.Any(target => string.Equals(target.TargetId, "target.print-pdf-export", StringComparison.Ordinal)
            && string.Equals(target.WorkflowId, "workflow.export.pdf", StringComparison.Ordinal)) == true);
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
            "lifestyles",
            "sources",
            "expenses",
            "calendar",
            "improvements",
            "customdatadirectorynames",
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

            CommandResult<WorkspaceDownloadReceipt> download = workspaceService.Download(imported.Id);
            Assert.IsTrue(download.Success, $"{fileName} should download after import.");
            Assert.AreEqual(WorkspaceDocumentFormat.NativeXml, download.Value?.Format, $"{fileName} should remain a .chum5-native document.");
            Assert.IsTrue((download.Value?.DocumentLength ?? 0) > 0, $"{fileName} download should contain payload bytes.");

            CommandResult<WorkspaceExportReceipt> export = workspaceService.Export(imported.Id);
            Assert.IsTrue(export.Success, $"{fileName} should export after import.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(export.Value?.PackageId), $"{fileName} export should produce a portable package id.");
            Assert.IsNotNull(export.Value?.Portability, $"{fileName} export should include portability guidance.");

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
            "lifestyles",
            "sources",
            "expenses",
            "calendar",
            "improvements",
            "customdatadirectorynames",
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
            CommandResult<WorkspaceDownloadReceipt> download = workspaceService.Download(imported.Id);
            Assert.IsTrue(download.Success, $"{fileName} should download after import.");
            Assert.AreEqual(WorkspaceDocumentFormat.NativeXml, download.Value?.Format, $"{fileName} should remain a .chum4-native document.");
            StringAssert.EndsWith(download.Value?.FileName ?? string.Empty, ".chum4");
            CommandResult<WorkspaceExportReceipt> export = workspaceService.Export(imported.Id);
            Assert.IsTrue(export.Success, $"{fileName} should export after import.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(export.Value?.PackageId), $"{fileName} export should produce a portable package id.");

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
        StringAssert.Contains(result.Value.Portability?.ReceiptSummary ?? string.Empty, "Portable export is ready");
        StringAssert.Contains(result.Value.Portability?.ProvenanceSummary ?? string.Empty, id.Value);
        string payload = Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.ContentBase64));
        StringAssert.Contains(payload, "\"Name\": \"Codec Runner\"");
        StringAssert.Contains(payload, "\"Reaction\"");
        StringAssert.Contains(payload, "\"Fixer\"");
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
        BuildLabConceptIntakeProjection? sr6Projection = new Sr6WorkspaceCodec().ParseSection("build-lab", sr6Envelope) as BuildLabConceptIntakeProjection;

        Assert.IsNotNull(sr4Projection);
        Assert.IsNotNull(sr6Projection);
        Assert.AreEqual("sr4", sr4Projection.RulesetId);
        Assert.AreEqual("sr6", sr6Projection.RulesetId);
        Assert.IsTrue(sr4Projection.Variants.Count > 0);
        Assert.IsTrue(sr6Projection.Variants.Count > 0);
    }

    private sealed class TrackingWorkspaceStore : IWorkspaceStore
    {
        public int CreateCallCount { get; private set; }

        public OwnerScope? LastCreateOwner { get; private set; }

        public CharacterWorkspaceId Create(WorkspaceDocument document)
        {
            return Create(OwnerScope.LocalSingleUser, document);
        }

        public CharacterWorkspaceId Create(OwnerScope owner, WorkspaceDocument document)
        {
            CreateCallCount++;
            LastCreateOwner = owner;
            return new CharacterWorkspaceId(Guid.NewGuid().ToString("N"));
        }

        public bool TryGet(CharacterWorkspaceId id, out WorkspaceDocument document)
        {
            return TryGet(OwnerScope.LocalSingleUser, id, out document);
        }

        public bool TryGet(OwnerScope owner, CharacterWorkspaceId id, out WorkspaceDocument document)
        {
            document = null!;
            return false;
        }

        public IReadOnlyList<WorkspaceStoreEntry> List()
        {
            return List(OwnerScope.LocalSingleUser);
        }

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner)
        {
            return [];
        }

        public void Save(CharacterWorkspaceId id, WorkspaceDocument document)
        {
            Save(OwnerScope.LocalSingleUser, id, document);
        }

        public void Save(OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document)
        {
        }

        public bool Delete(CharacterWorkspaceId id)
        {
            return Delete(OwnerScope.LocalSingleUser, id);
        }

        public bool Delete(OwnerScope owner, CharacterWorkspaceId id)
        {
            return false;
        }
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
            new Sr6WorkspaceCodec(),
            .. additionalCodecs
        ];
        IRulesetWorkspaceCodecResolver resolver = new RulesetWorkspaceCodecResolver(
            codecs);
        return new WorkspaceService(workspaceStore, resolver, new WorkspaceImportRulesetDetector());
    }

    private sealed class RecordingWorkspaceCodec : IRulesetWorkspaceCodec
    {
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
                Contacts: (CharacterContactsSection)ParseSection("contacts", envelope));
        }
    }
}
