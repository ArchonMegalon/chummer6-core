using System.Reflection;
using System.Security;
using System.Text;
using System.Text.Json;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationRuleSourceCaptureTests
{
    [TestMethod]
    [DataRow("/chummer/a|/home/private")]
    [DataRow("/chummer/a[/home/private]")]
    [DataRow("/chummer/a | /home/private")]
    [DataRow("/chummer/a|\t/home/private")]
    [DataRow("/chummer/a|\n/home/private")]
    [DataRow("count(/chummer/a)+count(/home/private)")]
    [DataRow("/chummer/a=/home/private")]
    [DataRow("/chummer/a!=/home/private")]
    [DataRow("/chummer/a</home/private")]
    [DataRow("/chummer/a>/home/private")]
    [DataRow("/chummer/a+/home/private")]
    [DataRow("/chummer/a -/home/private")]
    [DataRow("/chummer/a*/home/private")]
    [DataRow("/chummer/a div/home/private")]
    [DataRow("a div/home/private")]
    [DataRow("a div&#47;home/private")]
    [DataRow("/chummer/a div&#47;home/private")]
    [DataRow("/chummer/a mod/home/private")]
    [DataRow("/chummer/a and/home/private")]
    [DataRow("/chummer/a or/home/private")]
    [DataRow("concat(/chummer/a,/home/private)")]
    [DataRow("/chummer/a|(/home/private)")]
    [DataRow("/chummer/a|//home/private")]
    [DataRow("/chummer/a[name='/home/private']")]
    [DataRow("/chummer/a[name=\"/home/private\"]")]
    [DataRow("/chummer/a[name='/character/private']")]
    [DataRow("/chummer/a[name='/chummer/private']")]
    [DataRow("/chummer/a[name='/spell/private']")]
    [DataRow("/chummer/a[name='/ private']")]
    [DataRow("/chummer/a[name=' \t/ private']")]
    [DataRow("/chummer/a[name='&#47; private']")]
    [DataRow("/chummer/a[name='x|/home/private']")]
    [DataRow("/chummer/a[name='x[/home/private]']")]
    [DataRow("/chummer/a[name='&#47;home/private']")]
    [DataRow("/chummer/a[name='&#x2f;home/private']")]
    [DataRow("/chummer/a[name='C:\\Users\\private']")]
    [DataRow("/chummer/a[name='\\\\server\\private']")]
    [DataRow("/chummer/a[name='file:///private']")]
    public void PrivateHistory723_all_expression_operands_and_literals_keep_the_path_gate(string expression)
    {
        string escaped = SecurityElement.Escape(expression)!;
        Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(
            Source723("<selecttext xpath=\"" + escaped + "\" />")));
        Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(
            Source723("<specificskill><condition>" + escaped + "</condition></specificskill>")));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t")]
    [DataRow("\n")]
    [DataRow("|")]
    [DataRow("[")]
    [DataRow("(")]
    [DataRow(")")]
    [DataRow("]")]
    [DataRow("=")]
    [DataRow("!")]
    [DataRow("<")]
    [DataRow(">")]
    [DataRow(",")]
    [DataRow("+")]
    [DataRow("-")]
    [DataRow("*")]
    [DataRow("'")]
    [DataRow("\"")]
    public void PrivateHistory723_generic_text_recognizes_the_same_path_boundaries(string boundary)
    {
        foreach (string path in new[] { "/home/private", "&#47;home/private", "&#x2f;home/private" })
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                RetainedCharacterRuleSourceInputs.ValidatePortableTransferText(
                    (boundary.Length == 0 ? "" : "prefix" + boundary) + path));
    }

    [TestMethod]
    [DataRow("/chummer/a|/chummer/b")]
    [DataRow("/chummer/a[/character/created]")]
    [DataRow("count(/chummer/a)+count(/chummer/b)")]
    [DataRow("/chummer/a div/character/created")]
    [DataRow("/chummer/a*/character/created")]
    [DataRow("/chummer/*/name|/chummer//name")]
    [DataRow("/chummer/a[name='ordinary relative/name']")]
    [DataRow("not(/spell/alchemical)")]
    public void PrivateHistory723_source_expression_classification_preserves_valid_token_context(string expression)
    {
        string escaped = SecurityElement.Escape(expression)!;
        ValidateSourceXml712(Source723("<selecttext xpath=\"" + escaped + "\" />"));
        ValidateSourceXml712(Source723("<specificskill><condition>" + escaped + "</condition></specificskill>"));
    }

    [TestMethod]
    [DataRow("<notes><condition>/character/private</condition></notes>")]
    [DataRow("<character><notes><condition>/character/private</condition></notes></character>")]
    [DataRow("<chummer><condition>/character/private</condition></chummer>")]
    [DataRow("<chummer><gears><gear><notes><condition>/character/private</condition></notes></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><notes><condition>/character/private</condition></notes></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><notes><gears><gear><bonus><selecttext xpath='/chummer/a'/></bonus></gear></gears></notes></chummer>")]
    [DataRow("<chummer><notes><selecttext xpath='/chummer/a'/></notes></chummer>")]
    [DataRow("<chummer><gears xmlns='urn:private'><gear><bonus><selecttext xmlns='' xpath='/chummer/a'/></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xmlns:p='urn:private' p:xpath='/chummer/a'/></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><!-- <notes><condition>/character/private</condition></notes> --></chummer>")]
    [DataRow("<chummer><!-- <gears><gear><bonus><selecttext xpath='/chummer/a'/></bonus></gear></gears> --></chummer>")]
    public void PrivateHistory723_source_expression_context_requires_exact_schema_ancestry(string xml)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(xml));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RetainedCharacterRuleSourceInputs.ValidatePortableTransferDocumentText(xml));
    }

    [TestMethod]
    [DataRow("<notes><condition>/character/private</condition></notes>")]
    [DataRow("<notes><condition>&#47;character/private</condition></notes>")]
    [DataRow("<notes><condition><![CDATA[/character/private]]></condition></notes>")]
    [DataRow("<notes><condition><![CDATA[/]]>character/private</condition></notes>")]
    [DataRow("<notes><condition>/<!-- split -->character/private</condition></notes>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath='/chummer/a'/></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>/character/private</condition></specificskill></bonus></gear></gears></chummer>")]
    public void PrivateHistory723_generic_JSON_and_character_XML_never_receive_source_admission(string xml)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { notes = new[] { xml } });
        MethodInfo visitor = typeof(WorkspacePrivateRuleSourceClosure).GetMethod("ValidatePrivateText",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var error = Assert.ThrowsExactly<TargetInvocationException>(() => visitor.Invoke(null, [json, null]));
        Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
        Assert.AreEqual("Private rule inputs could not be admitted.", error.InnerException!.Message);

        using var fixture = new Fixture();
        string character = fixture.Character.Replace("</character>", xml + "</character>", StringComparison.Ordinal);
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(character, out var capture));
        byte[] bytes = capture!.ExportPrivateInputs();
        var retained = RetainedCharacterRuleSourceInputs.Import(bytes, capture.IntegrityDigest);
        Assert.IsFalse(retained.IsPortable);
        Assert.AreEqual(character, retained.CharacterXml);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            bytes, capture.IntegrityDigest, out var recovered));
        CollectionAssert.AreEqual(bytes, recovered!.ExportPrivateInputs());
    }

    [TestMethod]
    [DataRow("<selecttext xpath='/chummer/a|/home/private'/>")]
    [DataRow("<selecttext xpath='/chummer/a[/home/private]'/>")]
    [DataRow("<selecttext xpath='/chummer/a|&#47;home/private'/>")]
    [DataRow("<selecttext xpath='/chummer/a[&#x2f;home/private]'/>")]
    [DataRow("<specificskill><condition>'<![CDATA[/character/private]]>'</condition></specificskill>")]
    [DataRow("<specificskill><condition>/chummer/a<![CDATA[|/home/private]]></condition></specificskill>")]
    public void PrivateHistory723_refused_source_capture_retains_exact_recoverable_V1_bytes(string bonus)
    {
        string xml = Source723(bonus);
        Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(xml));
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Data("gear.xml"), xml);
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] bytes = capture!.ExportPrivateInputs();
        var retained = RetainedCharacterRuleSourceInputs.Import(bytes, capture.IntegrityDigest);
        Assert.IsFalse(retained.IsPortable);
        Assert.ThrowsExactly<InvalidOperationException>(() => retained.ToPortable());
        Assert.IsTrue(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(xml)) >= 0);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            bytes, capture.IntegrityDigest, out var recovered));
        CollectionAssert.AreEqual(bytes, recovered!.ExportPrivateInputs());
    }

    [TestMethod]
    public void PrivateHistory723_CDATA_source_expression_preserves_exact_portable_archive_bytes()
    {
        string xml = Source723("<specificskill><condition>/character/created"
            + "<![CDATA[ = false]]></condition></specificskill>");
        ValidateSourceXml712(xml);
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Data("gear.xml"), xml);
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] bytes = capture!.ExportPrivateInputs();
        Assert.IsTrue(RetainedCharacterRuleSourceInputs.Import(bytes, capture.IntegrityDigest).IsPortable);
        Assert.IsTrue(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(xml)) >= 0);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            bytes, capture.IntegrityDigest, out var recovered));
        CollectionAssert.AreEqual(bytes, recovered!.ExportPrivateInputs());
    }

    [TestMethod]
    public void PrivateHistory723_commented_markup_stays_strict_without_mistaking_closing_tags_for_paths()
    {
        const string xml = "<chummer><!-- Disabled <bonus><specificskill><condition>create"
            + "</condition></specificskill></bonus> --></chummer>";
        ValidateSourceXml712(xml);
        RetainedCharacterRuleSourceInputs.ValidatePortableTransferDocumentText(xml);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RetainedCharacterRuleSourceInputs.ValidatePortableTransferText("x</home/private"));
    }

    [TestMethod]
    public void PrivateHistory723_non_expression_condition_prose_uses_the_unmasked_text_gate()
    {
        const string prose = "Opposed checks and/or tests";
        RetainedCharacterRuleSourceInputs.ValidatePortableTransferText(prose);
        ValidateSourceXml712(Source723("<specificskill><condition>" + prose + "</condition></specificskill>"));
        Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(
            Source723("<specificskill><condition>" + prose + " /home/private</condition></specificskill>")));
    }

    private static string Source723(string bonus)
        => Gear(10).Replace("</gear>", "<bonus>" + bonus + "</bonus></gear>", StringComparison.Ordinal);
}
