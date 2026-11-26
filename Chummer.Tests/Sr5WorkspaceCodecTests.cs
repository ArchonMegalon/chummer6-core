#nullable enable
using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr5WorkspaceCodecTests
{
    [TestMethod]
    [DataRow("none")]
    [DataRow("unspecified")]
    [DataRow("utf-8")]
    [DataRow("UTF-8")]
    [DataRow("utf-16")]
    [DataRow("utf-16BE")]
    [DataRow("utf-32")]
    public void Build_download_reopens_unicode_xml_without_changing_payload(string declaration)
    {
        string prefix = declaration switch
        {
            "none" => string.Empty,
            "unspecified" => "<?xml version='1.0'?>\r\n",
            _ => $"<?xml version='1.0' encoding='{declaration}' standalone='yes'?>\r\n"
        };
        AssertDownloadRoundTrip(prefix + """
            <?xml-stylesheet type="text/xsl" href="sheet.xsl"?>
            <!-- Preserve comments, spacing, unknown extensions and source metadata. -->
            <character custom="retained">
              <name>Grüße 東京 🐉</name>
              <notes><![CDATA[Literal utf-8 and utf-16; <tags> & accents é]]></notes>
              <retainedsources><source fingerprint="synthetic-identity" enabled="true" /></retainedsources>
              <extension xml:space="preserve">  α &amp; β  </extension>
            </character>
            """);
    }

    [TestMethod]
    [DataRow("iso-8859-1")]
    [DataRow("us-ascii")]
    public void Build_download_preserves_supported_alternate_declarations(string declaration)
    {
        string name = declaration == "iso-8859-1" ? "Grüße café" : "Runner";
        AssertDownloadRoundTrip(
            $"<?xml version=\"1.0\" encoding=\"{declaration}\"?><character><name>{name}</name></character>");
    }

    [TestMethod]
    public void Build_download_does_not_replace_unrepresentable_characters()
    {
        Sr5WorkspaceCodec codec = new(null!, null!, null!);
        WorkspacePayloadEnvelope envelope = Envelope(
            "<?xml version='1.0' encoding='us-ascii'?><character><name>東京</name></character>");

        Assert.ThrowsExactly<EncoderFallbackException>(() => codec.BuildDownload(
            new CharacterWorkspaceId("encoding-regression"), envelope, WorkspaceDocumentFormat.NativeXml));
    }

    [TestMethod]
    public void Build_download_preserves_utf8_bom_import_semantics()
    {
        AssertDownloadRoundTrip("\uFEFF<?xml version='1.0' encoding='utf-16'?><character><name>é</name></character>");
    }

    private static void AssertDownloadRoundTrip(string input)
    {
        Sr5WorkspaceCodec codec = new(null!, null!, null!);
        WorkspacePayloadEnvelope envelope = codec.WrapImport(
            RulesetDefaults.Sr5, new WorkspaceImportDocument(input, RulesetDefaults.Sr5));
        string expected = input.TrimStart('\uFEFF');
        CharacterWorkspaceId id = new("encoding-regression");
        WorkspaceDownloadReceipt receipt = codec.BuildDownload(id, envelope, WorkspaceDocumentFormat.NativeXml);
        byte[] bytes = Convert.FromBase64String(receipt.ContentBase64);
        string path = Path.Combine(Path.GetTempPath(), $"sr5-download-{Guid.NewGuid():N}.chum5");
        try
        {
            File.WriteAllBytes(path, bytes);
            using (FileStream stream = File.OpenRead(path))
            {
                XDocument reopened = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                Assert.IsTrue(XNode.DeepEquals(XDocument.Parse(expected, LoadOptions.PreserveWhitespace), reopened));
            }

            string encodingName = XDocument.Parse(expected).Declaration?.Encoding ?? "utf-8";
            string reopenedText;
            using (StreamReader reader = new(path, Encoding.GetEncoding(encodingName), detectEncodingFromByteOrderMarks: true))
            {
                reopenedText = reader.ReadToEnd();
            }

            WorkspacePayloadEnvelope imported = codec.WrapImport(
                RulesetDefaults.Sr5, new WorkspaceImportDocument(reopenedText, RulesetDefaults.Sr5));
            Assert.AreEqual(expected, imported.Payload);
            Assert.AreEqual(expected, envelope.Payload);
            Assert.AreEqual(expected.Length, receipt.DocumentLength);
            Assert.AreEqual("encoding-regression.chum5", receipt.FileName);
            Assert.AreEqual(RulesetDefaults.Sr5, receipt.RulesetId);
            Assert.AreEqual(WorkspaceDocumentFormat.NativeXml, receipt.Format);
            Assert.AreEqual(id, receipt.Id);

            if (Encoding.GetEncoding(encodingName).CodePage == Encoding.UTF8.CodePage)
            {
                CollectionAssert.AreEqual(new UTF8Encoding(false, true).GetBytes(expected), bytes);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static WorkspacePayloadEnvelope Envelope(string xml)
        => new(RulesetDefaults.Sr5, Sr5WorkspaceCodec.SchemaVersion, Sr5WorkspaceCodec.Sr5PayloadKind, xml);
}
