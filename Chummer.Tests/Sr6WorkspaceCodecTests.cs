#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Text;
using Chummer.Application.BuildLab;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
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
        Sr6WorkspaceCodec codec = new();

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
        Sr6WorkspaceCodec codec = new();
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
        Sr6WorkspaceCodec codec = new();
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
        Sr6WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, "<character>");

        CharacterValidationResult result = codec.Validate(envelope);

        Assert.IsFalse(result.IsValid);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual("sr6.invalid_xml", result.Issues[0].Code);
        Assert.AreEqual("/character", result.Issues[0].Path);
    }

    [TestMethod]
    public void Update_metadata_repairs_missing_contract_fields_and_creates_elements()
    {
        Sr6WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(
            RulesetDefaults.Sr6,
            SchemaVersion: 0,
            PayloadKind: string.Empty,
            Payload: "<character><alias>Existing Alias</alias></character>");

        WorkspacePayloadEnvelope updated = codec.UpdateMetadata(
            envelope,
            new UpdateWorkspaceMetadata("  Updated Name  ", null, "  Notes Here  "));

        Assert.AreEqual(Sr6WorkspaceCodec.SchemaVersion, updated.SchemaVersion);
        Assert.AreEqual(Sr6WorkspaceCodec.Sr6PayloadKind, updated.PayloadKind);
        StringAssert.Contains(updated.Payload, "<name>Updated Name</name>");
        StringAssert.Contains(updated.Payload, "<alias></alias>");
        StringAssert.Contains(updated.Payload, "<notes>Notes Here</notes>");
    }

    [TestMethod]
    public void Build_download_throws_for_unsupported_format()
    {
        Sr6WorkspaceCodec codec = new();
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
        Sr6WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, CanonicalXml);

        DataExportBundle bundle = codec.BuildExportBundle(envelope);

        Assert.AreEqual("Codec Runner", bundle.Summary.Name);
        Assert.AreEqual("Switchback", bundle.Profile?.Alias);
        Assert.AreEqual(11m, bundle.Progress?.Karma);
        Assert.AreEqual(2750m, bundle.Progress?.Nuyen);
        Assert.IsNotNull(bundle.Inventory);
        Assert.IsNotNull(bundle.Qualities);
        Assert.IsNotNull(bundle.Contacts);
        Assert.IsNull(bundle.Lifestyles);
    }

    [TestMethod]
    public void Build_download_emits_native_xml_receipt()
    {
        Sr6WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr6, 1, Sr6WorkspaceCodec.Sr6PayloadKind, CanonicalXml);

        WorkspaceDownloadReceipt receipt = codec.BuildDownload(
            new CharacterWorkspaceId("ws-sr6"),
            envelope,
            WorkspaceDocumentFormat.NativeXml);

        Assert.AreEqual("ws-sr6.chum6", receipt.FileName);
        Assert.AreEqual(RulesetDefaults.Sr6, receipt.RulesetId);
        Assert.AreEqual(CanonicalXml, Encoding.UTF8.GetString(Convert.FromBase64String(receipt.ContentBase64)));
    }
}
