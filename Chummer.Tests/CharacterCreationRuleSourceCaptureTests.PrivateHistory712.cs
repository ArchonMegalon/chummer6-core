using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationRuleSourceCaptureTests
{
    [TestMethod]
    [DataRow("qualities.xml")]
    [DataRow("powers.xml")]
    [DataRow("complexforms.xml")]
    [DataRow("gear.xml")]
    [DataRow("lifestyles.xml")]
    [DataRow("mentors.xml")]
    [DataRow("programs.xml")]
    [DataRow("critterpowers.xml")]
    [DataRow("cyberware.xml")]
    [DataRow("bioware.xml")]
    [DataRow("echoes.xml")]
    [DataRow("vehicles.xml")]
    public void PrivateHistory712_shipped_XML_rule_selectors_are_portable_metadata(string source)
    {
        for (DirectoryInfo? directory = new(AppDomain.CurrentDomain.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Chummer", "data", source);
            if (!File.Exists(candidate)) continue;
            // Full shipped source bytes, including unselected entries and comments.
            ValidateSourceXml712(File.ReadAllText(candidate));
            return;
        }
        Assert.Fail("stage=source712;outcome=shipped-source-missing");
    }

    [TestMethod]
    [DataRow("<chummer><gears><gear><bonus><selecttext xml=\"traditions.xml\" xpath=\"/chummer/spirits/spirit[not(contains(name, 'Watcher'))]\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"/chummer/skills/skill/name | /chummer/knowledgeskills/skill/name\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>/character/created = false</condition></specificskill><specificskill><condition>not(/spell/alchemical)</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition><![CDATA[not(/spell/alchemical)]]></condition></specificskill></bonus></gear></gears></chummer>")]
    public void PrivateHistory712_expression_privacy_check_does_not_execute_or_rewrite_selectors(string xml)
    {
        ValidateSourceXml712(xml);
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Data("gear.xml"), xml);
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        var retained = RetainedCharacterRuleSourceInputs.Import(capture!.ExportPrivateInputs(), capture.IntegrityDigest);
        Assert.IsTrue(retained.IsPortable);
        Assert.IsTrue(capture.ExportPrivateInputs().AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(xml)) >= 0);
    }

    [TestMethod]
    [DataRow("<chummer><gears><gear><bonus><notes>/chummer/spirits/spirit</notes></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><!-- /chummer/spirits/spirit --></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext other=\"/chummer/spirits/spirit\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer xmlns:p=\"urn:private\"><gears><gear><bonus><selecttext p:xpath=\"/chummer/spirits/spirit\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer xmlns:p=\"urn:private\"><gears><gear><bonus><p:condition>/character/created</p:condition></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition note=\"/character/created\">create</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><notes>&lt;condition&gt;/character/created&lt;/condition&gt;</notes></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"/home/runner/private\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"/chummer/spirits/spirit | /home/runner/private\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"/chummer/spirits/spirit[name = '/home/runner/private']\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"/chummer/spirits/spirit[name = '&#47;home/runner/private']\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"/chummer/spirits/spirit[name = '&amp;#47;home/runner/private']\" /></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>/character/created = 'C:\\Users\\runner\\private'</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>not(/spell/alchemical) or 'file:///private'</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>not(/spell/alchemical) or '\\\\server\\private'</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>not(/spell/alchemical</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"/chummer/spirits/[\" /></bonus></gear></gears></chummer>")]
    public void PrivateHistory712_expression_fields_still_reject_private_literals_and_invalid_syntax(string xml)
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => ValidateSourceXml712(xml));
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Data("gear.xml"), xml);
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] bytes = capture!.ExportPrivateInputs();
        Assert.IsFalse(RetainedCharacterRuleSourceInputs.Import(bytes, capture.IntegrityDigest).IsPortable);
        Assert.IsTrue(bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(xml)) >= 0);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(bytes, capture.IntegrityDigest, out var recovered));
        CollectionAssert.AreEqual(bytes, recovered!.ExportPrivateInputs());
    }

    [TestMethod]
    public void PrivateHistory712_expression_syntax_budget_and_plain_text_path_fences_remain_closed()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => RetainedCharacterRuleSourceInputs.ValidatePortableTransferText("/chummer/spirits/spirit"));
        string[] expressions =
        [
            new string('(', 33) + "/character/created" + new string(')', 33),
            "/chummer/" + new string('a', 4096)
        ];
        foreach (string expression in expressions)
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                ValidateSourceXml712(
                    "<chummer><gears><gear><bonus><specificskill><condition>" + expression + "</condition></specificskill></bonus></gear></gears></chummer>"));
    }

    private static void ValidateSourceXml712(string xml)
    {
        RetainedCharacterRuleSourceInputs.ValidateCharacter(xml);
        using var text = new StringReader(xml);
        using var reader = System.Xml.XmlReader.Create(text, RetainedCharacterRuleSourceInputs.XmlSettings());
        RetainedCharacterRuleSourceInputs.ValidatePortableRuleSourceXml(reader);
    }

}
