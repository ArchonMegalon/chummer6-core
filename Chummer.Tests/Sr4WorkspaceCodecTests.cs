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
using Chummer.Rulesets.Sr4;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class Sr4WorkspaceCodecTests
{
    private const string CanonicalXml = """
        <character>
          <name>Codec Veteran</name>
          <alias>Wireglass</alias>
          <metatype>Human</metatype>
          <buildmethod>Priority</buildmethod>
          <createdversion>4.0</createdversion>
          <appversion>4.2</appversion>
          <karma>8</karma>
          <nuyen>1250</nuyen>
          <created>true</created>
          <gameedition>SR4</gameedition>
        </character>
        """;

    [TestMethod]
    public void Wrap_import_strips_utf8_bom_for_native_xml()
    {
        Sr4WorkspaceCodec codec = new();

        WorkspacePayloadEnvelope envelope = codec.WrapImport(
            RulesetDefaults.Sr4,
            new WorkspaceImportDocument("\uFEFF<character><name>Ghost</name></character>", RulesetDefaults.Sr4));

        Assert.AreEqual("<character><name>Ghost</name></character>", envelope.Payload);
        Assert.AreEqual(Sr4WorkspaceCodec.SchemaVersion, envelope.SchemaVersion);
        Assert.AreEqual(Sr4WorkspaceCodec.Sr4PayloadKind, envelope.PayloadKind);
    }

    [TestMethod]
    public void Parse_section_returns_fallback_dictionary_for_unknown_section()
    {
        Sr4WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr4, 1, Sr4WorkspaceCodec.Sr4PayloadKind, CanonicalXml);

        object section = codec.ParseSection("mystery-section", envelope);

        Dictionary<string, object?>? fallback = section as Dictionary<string, object?>;
        Assert.IsNotNull(fallback);
        Assert.AreEqual("mystery-section", fallback["sectionId"]);
        Assert.AreEqual(RulesetDefaults.Sr4, fallback["rulesetId"]);
    }

    [TestMethod]
    public void Parse_section_build_lab_projects_sr4_ruleset_context()
    {
        Sr4WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr4, 1, Sr4WorkspaceCodec.Sr4PayloadKind, CanonicalXml);

        BuildLabConceptIntakeProjection? projection = codec.ParseSection("build-lab", envelope) as BuildLabConceptIntakeProjection;

        Assert.IsNotNull(projection);
        Assert.AreEqual(RulesetDefaults.Sr4, projection.RulesetId);
        Assert.AreEqual("workflow.build-lab", projection.WorkflowId);
        Assert.AreEqual("Priority", projection.BuildMethod);
        StringAssert.Contains(projection.Title, "Codec Veteran");
        Assert.AreEqual("Wireglass", projection.IntakeFields[0].Value);
        Assert.IsTrue(projection.CanContinue);
    }

    [TestMethod]
    public void Validate_returns_invalid_xml_issue_for_malformed_payload()
    {
        Sr4WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr4, 1, Sr4WorkspaceCodec.Sr4PayloadKind, "<character>");

        CharacterValidationResult result = codec.Validate(envelope);

        Assert.IsFalse(result.IsValid);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual("InvalidXml", result.Issues[0].Code);
        Assert.AreEqual("/", result.Issues[0].Path);
    }

    [TestMethod]
    public void Update_metadata_repairs_missing_contract_fields_and_creates_elements()
    {
        Sr4WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(
            RulesetDefaults.Sr4,
            SchemaVersion: 0,
            PayloadKind: string.Empty,
            Payload: """
                <character>
                  <name>Legacy Name</name>
                  <alias>Existing Alias</alias>
                  <metatype>Human</metatype>
                  <buildmethod>Priority</buildmethod>
                  <createdversion>4.0</createdversion>
                  <appversion>4.2</appversion>
                  <karma>0</karma>
                  <nuyen>0</nuyen>
                  <created>true</created>
                  <gameedition>SR4</gameedition>
                </character>
                """);

        WorkspacePayloadEnvelope updated = codec.UpdateMetadata(
            envelope,
            new UpdateWorkspaceMetadata("  Updated Name  ", null, "  Notes Here  "));

        Assert.AreEqual(Sr4WorkspaceCodec.SchemaVersion, updated.SchemaVersion);
        Assert.AreEqual(Sr4WorkspaceCodec.Sr4PayloadKind, updated.PayloadKind);
        StringAssert.Contains(updated.Payload, "<name>  Updated Name  </name>");
        StringAssert.Contains(updated.Payload, "<alias>Existing Alias</alias>");
        StringAssert.Contains(updated.Payload, "<notes>  Notes Here  </notes>");
    }

    [TestMethod]
    public void Build_download_throws_for_unsupported_format()
    {
        Sr4WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr4, 1, Sr4WorkspaceCodec.Sr4PayloadKind, CanonicalXml);

        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            codec.BuildDownload(
                new CharacterWorkspaceId("ws-sr4"),
                envelope,
                (WorkspaceDocumentFormat)999));

        StringAssert.Contains(ex.Message, "not supported");
    }

    [TestMethod]
    public void Build_export_bundle_projects_summary_and_known_sections()
    {
        Sr4WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr4, 1, Sr4WorkspaceCodec.Sr4PayloadKind, CanonicalXml);

        DataExportBundle bundle = codec.BuildExportBundle(envelope);

        Assert.AreEqual("Codec Veteran", bundle.Summary.Name);
        Assert.AreEqual("Wireglass", bundle.Profile?.Alias);
        Assert.AreEqual(8m, bundle.Progress?.Karma);
        Assert.AreEqual(1250m, bundle.Progress?.Nuyen);
        Assert.IsNotNull(bundle.Inventory);
        Assert.IsNotNull(bundle.Qualities);
        Assert.IsNotNull(bundle.Contacts);
        Assert.IsNotNull(bundle.Lifestyles);
    }

    [TestMethod]
    public void Build_download_emits_native_xml_receipt()
    {
        Sr4WorkspaceCodec codec = new();
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr4, 1, Sr4WorkspaceCodec.Sr4PayloadKind, CanonicalXml);

        WorkspaceDownloadReceipt receipt = codec.BuildDownload(
            new CharacterWorkspaceId("ws-sr4"),
            envelope,
            WorkspaceDocumentFormat.NativeXml);

        Assert.AreEqual("ws-sr4.chum4", receipt.FileName);
        Assert.AreEqual(RulesetDefaults.Sr4, receipt.RulesetId);
        Assert.AreEqual(CanonicalXml, Encoding.UTF8.GetString(Convert.FromBase64String(receipt.ContentBase64)));
    }
}
