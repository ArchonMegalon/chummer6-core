#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chummer.Application.BuildLab;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class Sr6WorkspaceCodecTests
{
    private const string CanonicalXml = """
        <character>
          <name>Codec Runner</name>
          <alias>Switchback</alias>
          <metatype>Human</metatype>
          <buildmethod>Priority</buildmethod>
          <createdversion>1.0</createdversion>
          <appversion>6.1</appversion>
          <karma>11</karma>
          <nuyen>2750</nuyen>
          <created>true</created>
        </character>
        """;

    [TestMethod]
    public void Wrap_import_strips_utf8_bom_for_native_xml()
    {
        Sr6WorkspaceCodec codec = CreateCodec();

        WorkspacePayloadEnvelope envelope = codec.WrapImport(
            RulesetDefaults.Sr6,
            new WorkspaceImportDocument("\uFEFF<character><name>Ghost</name></character>", RulesetDefaults.Sr6));

        Assert.AreEqual("<character><name>Ghost</name></character>", envelope.Payload);
        Assert.AreEqual(Sr6WorkspaceCodec.SchemaVersion, envelope.SchemaVersion);
        Assert.AreEqual(Sr6WorkspaceCodec.Sr6PayloadKind, envelope.PayloadKind);
    }

    [TestMethod]
    public void Parse_section_returns_fallback_dictionary_for_unknown_section()
    {
        Sr6WorkspaceCodec codec = CreateCodec();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, CanonicalXml);

        object section = codec.ParseSection("mystery-section", envelope);

        Dictionary<string, object?>? fallback = section as Dictionary<string, object?>;
        Assert.IsNotNull(fallback);
        Assert.AreEqual("mystery-section", fallback["sectionId"]);
        Assert.AreEqual(RulesetDefaults.Sr6, fallback["rulesetId"]);
    }

    [TestMethod]
    public void Parse_section_build_lab_projects_sr6_ruleset_context()
    {
        Sr6WorkspaceCodec codec = CreateCodec();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, CanonicalXml);

        BuildLabConceptIntakeProjection? projection = codec.ParseSection("build-lab", envelope) as BuildLabConceptIntakeProjection;

        Assert.IsNotNull(projection);
        Assert.AreEqual(RulesetDefaults.Sr6, projection.RulesetId);
        Assert.AreEqual("workflow.build-lab", projection.WorkflowId);
        Assert.AreEqual("Priority", projection.BuildMethod);
        StringAssert.Contains(projection.Title, "Codec Runner");
        Assert.AreEqual("Switchback", projection.IntakeFields[0].Value);
        Assert.IsTrue(projection.CanContinue);
    }

    [TestMethod]
    public void Validate_returns_invalid_xml_issue_for_malformed_payload()
    {
        Sr6WorkspaceCodec codec = CreateCodec();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, "<character>");

        CharacterValidationResult result = codec.Validate(envelope);

        Assert.IsFalse(result.IsValid);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual("InvalidXml", result.Issues[0].Code);
        Assert.AreEqual("/", result.Issues[0].Path);
    }

    [TestMethod]
    public void Update_metadata_repairs_missing_contract_fields_and_creates_elements()
    {
        Sr6WorkspaceCodec codec = CreateCodec();
        WorkspacePayloadEnvelope envelope = new(
            RulesetDefaults.Sr6,
            SchemaVersion: 0,
            PayloadKind: string.Empty,
            Payload: "<character><alias>Existing Alias</alias></character>");

        WorkspacePayloadEnvelope updated = codec.UpdateMetadata(
            envelope,
            new UpdateWorkspaceMetadata("  Updated Name  ", null, "  Notes Here  ")
            {
                GameNotes = "  Game Notes  ",
                GroupNotes = "  Group Notes  "
            });

        Assert.AreEqual(Sr6WorkspaceCodec.SchemaVersion, updated.SchemaVersion);
        Assert.AreEqual(Sr6WorkspaceCodec.Sr6PayloadKind, updated.PayloadKind);
        StringAssert.Contains(updated.Payload, "<name>Updated Name</name>");
        StringAssert.Contains(updated.Payload, "<alias></alias>");
        StringAssert.Contains(updated.Payload, "<notes>Notes Here</notes>");
        StringAssert.Contains(updated.Payload, "<gamenotes>Game Notes</gamenotes>");
        StringAssert.Contains(updated.Payload, "<groupnotes>Group Notes</groupnotes>");
    }

    [TestMethod]
    public void Build_download_throws_for_unsupported_format()
    {
        Sr6WorkspaceCodec codec = CreateCodec();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, CanonicalXml);

        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            codec.BuildDownload(
                new CharacterWorkspaceId("ws-sr6"),
                envelope,
                (WorkspaceDocumentFormat)999));

        StringAssert.Contains(ex.Message, "not supported");
    }

    [TestMethod]
    public void Build_export_bundle_projects_summary_and_known_sections()
    {
        Sr6WorkspaceCodec codec = CreateCodec();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, CanonicalXml);

        DataExportBundle bundle = codec.BuildExportBundle(envelope);

        Assert.AreEqual("Codec Runner", bundle.Summary.Name);
        Assert.AreEqual("Switchback", bundle.Profile?.Alias);
        Assert.AreEqual(11m, bundle.Progress?.Karma);
        Assert.AreEqual(2750m, bundle.Progress?.Nuyen);
        Assert.IsNotNull(bundle.Inventory);
        Assert.IsNotNull(bundle.Qualities);
        Assert.IsNotNull(bundle.Contacts);
        Assert.IsNotNull(bundle.Lifestyles);
    }

    [TestMethod]
    public void Parse_section_projects_typed_shared_sections_for_representative_workbench_families()
    {
        string overviewXml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        string supportXml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        string timelineXml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        Sr6WorkspaceCodec codec = CreateCodec();

        WorkspacePayloadEnvelope overviewEnvelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, overviewXml);
        WorkspacePayloadEnvelope supportEnvelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, supportXml);
        WorkspacePayloadEnvelope timelineEnvelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, timelineXml);

        CharacterAttributesSection? attributes = codec.ParseSection("attributes", overviewEnvelope) as CharacterAttributesSection;
        CharacterSkillsSection? skills = codec.ParseSection("skills", overviewEnvelope) as CharacterSkillsSection;
        CharacterInventorySection? inventory = codec.ParseSection("inventory", overviewEnvelope) as CharacterInventorySection;
        CharacterQualitiesSection? qualities = codec.ParseSection("qualities", overviewEnvelope) as CharacterQualitiesSection;
        CharacterContactsSection? contacts = codec.ParseSection("contacts", overviewEnvelope) as CharacterContactsSection;
        CharacterContactsSection? relationships = codec.ParseSection("relationships", overviewEnvelope) as CharacterContactsSection;
        CharacterContactsSection? enemies = codec.ParseSection("enemies", overviewEnvelope) as CharacterContactsSection;
        CharacterContactsSection? pets = codec.ParseSection("pets", overviewEnvelope) as CharacterContactsSection;
        CharacterProgressSection? karmaSummary = codec.ParseSection("karmasummary", overviewEnvelope) as CharacterProgressSection;
        CharacterConditionMonitorSection? conditionMonitor = codec.ParseSection("conditionmonitor", overviewEnvelope) as CharacterConditionMonitorSection;
        CharacterSpellDefenseSection? spellDefense = codec.ParseSection("spelldefense", overviewEnvelope) as CharacterSpellDefenseSection;
        CharacterLifestylesSection? lifestyles = codec.ParseSection("lifestyles", overviewEnvelope) as CharacterLifestylesSection;
        CharacterImprovementsSection? improvements = codec.ParseSection("improvements", supportEnvelope) as CharacterImprovementsSection;
        CharacterCalendarSection? calendar = codec.ParseSection("calendar", timelineEnvelope) as CharacterCalendarSection;

        Assert.IsNotNull(attributes);
        Assert.IsNotNull(skills);
        Assert.IsNotNull(inventory);
        Assert.IsNotNull(qualities);
        Assert.IsNotNull(contacts);
        Assert.IsNotNull(relationships);
        Assert.IsNotNull(enemies);
        Assert.IsNotNull(pets);
        Assert.IsNotNull(karmaSummary);
        Assert.IsNotNull(conditionMonitor);
        Assert.IsNotNull(spellDefense);
        Assert.IsNotNull(lifestyles);
        Assert.IsNotNull(improvements);
        Assert.IsNotNull(calendar);

        Assert.IsGreaterThan(0, attributes!.Count);
        Assert.IsGreaterThan(0, skills!.Count);
        Assert.IsGreaterThanOrEqualTo(0, inventory!.GearCount);
        Assert.IsGreaterThan(0, qualities!.Count);
        Assert.IsGreaterThan(0, contacts!.Count);
        Assert.IsGreaterThanOrEqualTo(0, relationships!.Count);
        Assert.IsGreaterThanOrEqualTo(0, enemies!.Count);
        Assert.IsGreaterThanOrEqualTo(0, pets!.Count);
        Assert.IsGreaterThanOrEqualTo(0m, karmaSummary!.Karma);
        Assert.IsGreaterThanOrEqualTo(0, conditionMonitor!.PhysicalTrack);
        Assert.AreEqual(17, spellDefense!.Count);
        Assert.IsGreaterThan(0, lifestyles!.Count);
        Assert.IsGreaterThan(0, improvements!.Count);
        Assert.IsGreaterThanOrEqualTo(0, calendar!.Count);
    }

    [TestMethod]
    public void Build_download_emits_native_xml_receipt()
    {
        Sr6WorkspaceCodec codec = CreateCodec();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, CanonicalXml);

        WorkspaceDownloadReceipt receipt = codec.BuildDownload(
            new CharacterWorkspaceId("ws-sr6"),
            envelope,
            WorkspaceDocumentFormat.NativeXml);

        Assert.AreEqual("ws-sr6.chum6", receipt.FileName);
        Assert.AreEqual(RulesetDefaults.Sr6, receipt.RulesetId);
        Assert.AreEqual(CanonicalXml, Encoding.UTF8.GetString(Convert.FromBase64String(receipt.ContentBase64)));
    }

    private static string FindTestFilePath(string fileName)
    {
        DirectoryInfo current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (true)
        {
            string candidate = Path.Combine(current.FullName, "Chummer.Tests", "TestFiles", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (current.Parent == null)
            {
                break;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate test character file.", fileName);
    }

    private static Sr6WorkspaceCodec CreateCodec()
        => new(
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
}
