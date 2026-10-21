using System.Security;
using System.Text;
using System.Xml;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationRuleSourceCaptureTests
{
    [TestMethod]
    [DataRow("a div/home/private +")]
    [DataRow("a mod/home/private +")]
    [DataRow("a and/home/private +")]
    [DataRow("a or/home/private +")]
    [DataRow("a div&#47;home/private +")]
    [DataRow("a div&#x2f;home/private +")]
    [DataRow("a div/home/private[")]
    [DataRow("a mod/home/private)")]
    [DataRow("a and/home/private '")]
    [DataRow("ns:* div/home/private +")]
    [DataRow("child::ns:* mod/home/private +")]
    [DataRow("Opposed checks and/or tests div/home/private +")]
    [DataRow("Opposed checks and/or/home/private")]
    [DataRow("Opposed checks and/or /home/private")]
    [DataRow("Opposed checks and/or ' /home/private'")]
    [DataRow("Opposed checks and/or tests C:\\Users\\private")]
    [DataRow("Opposed checks and/or tests file:///private")]
    public void PrivateHistory734_syntax_failure_never_forgives_private_operands(string expression)
    {
        string escaped = SecurityElement.Escape(expression)!;
        foreach (string bonus in new[]
        {
            "<selecttext xpath=\"" + escaped + "\"/>",
            "<specificskill><condition>" + escaped + "</condition></specificskill>"
        })
        {
            var error = Assert.ThrowsExactly<InvalidOperationException>(
                () => ValidateSourceXml712(Source723(bonus)));
            Assert.AreEqual("Private rule inputs could not be admitted.", error.Message);
        }
    }

    // Exact review731 counterexamples: XML decoding, additional HTML decoding,
    // CDATA and comment boundaries all reach real capture and cold recovery.
    [TestMethod]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"a div/home/private +\"/></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>a div/home/private +</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"a mod&#47;home/private +\"/></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>a and<![CDATA[/home/private]]> +</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>a or<!-- split -->/home/private +</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><selecttext xpath=\"a div&amp;#47;home/private +\"/></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>a d<![CDATA[iv/ho]]>me/private +</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>a mo<!-- split -->d/ho<!-- split -->me/private +</condition></specificskill></bonus></gear></gears></chummer>")]
    [DataRow("<chummer><gears><gear><bonus><specificskill><condition>a div&amp;#47;home/private +</condition></specificskill></bonus></gear></gears></chummer>")]
    public void PrivateHistory734_review731_sources_remain_exact_local_V1(string xml)
        => CaptureLocal734(xml);

    internal static byte[] CaptureLocal734(string xml)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(xml));
        Assert.AreEqual("Private rule inputs could not be admitted.", error.Message);
        using var fixture = new Fixture();
        // The private operand is unrelated to fixture.Root; root-spelling checks
        // must not accidentally supply the refusal asserted by this regression.
        Assert.IsFalse(xml.Contains(fixture.Root, StringComparison.Ordinal));
        File.WriteAllText(fixture.Data("gear.xml"), xml);
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] bytes = capture!.ExportPrivateInputs();
        var retained = RetainedCharacterRuleSourceInputs.Import(bytes, capture.IntegrityDigest);
        Assert.IsFalse(retained.IsPortable);
        Assert.ThrowsExactly<InvalidOperationException>(() => retained.ToPortable());
        Assert.IsTrue(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(xml)) >= 0);
        Directory.Delete(fixture.Root, recursive: true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            bytes, capture.IntegrityDigest, out var recovered));
        Assert.AreEqual(capture.IntegrityDigest, recovered!.IntegrityDigest);
        CollectionAssert.AreEqual(bytes, recovered.ExportPrivateInputs());
        Assert.IsFalse(RetainedCharacterRuleSourceInputs.Import(
            recovered.ExportPrivateInputs(), recovered.IntegrityDigest).IsPortable);
        return bytes;
    }

    [TestMethod]
    [DataRow("ordinary/relative/name")]
    [DataRow("div/home/private")]
    [DataRow("and/home/private")]
    [DataRow("a/div/home/private")]
    [DataRow("child::div/home/private")]
    [DataRow("../name")]
    [DataRow("./name")]
    [DataRow("*/name")]
    [DataRow("child::*/name")]
    [DataRow("descendant-or-self::node()/child::name")]
    [DataRow("ns:*/name")]
    [DataRow("child::ns:*/name")]
    [DataRow("ns:*/div/name")]
    [DataRow("/chummer/child::*/name")]
    [DataRow("/chummer/ns:*/name")]
    [DataRow("/chummer/child::ns:*/name")]
    [DataRow("/chummer/a[name='ordinary relative/name']")]
    [DataRow("/chummer/a[name=\"ordinary relative/name\"]")]
    public void PrivateHistory734_relative_steps_axes_and_QName_wildcards_are_portable(string expression)
    {
        string escaped = SecurityElement.Escape(expression)!;
        ValidateSourceXml712(Source723("<selecttext xpath=\"" + escaped + "\"/>"));
        ValidateSourceXml712(Source723("<specificskill><condition>" + escaped + "</condition></specificskill>"));
    }

    [TestMethod]
    public void PrivateHistory734_shipped_conjunction_prose_is_condition_only_and_retained_exactly()
    {
        string? source = null;
        for (DirectoryInfo? directory = new(AppDomain.CurrentDomain.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Chummer", "data", "qualities.xml");
            if (File.Exists(candidate)) { source = candidate; break; }
        }
        Assert.IsNotNull(source, "stage=source734;outcome=shipped-source-missing");
        var document = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(source!, RetainedCharacterRuleSourceInputs.XmlSettings()))
            document.Load(reader);
        var conditions = document.SelectNodes("/chummer/qualities/quality/bonus/specificskill/condition")!
            .Cast<XmlNode>().Select(node => node.InnerText).Where(text => text.Contains("and/or", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.IsTrue(conditions.Length > 0);
        foreach (string prose in conditions)
        {
            string escaped = SecurityElement.Escape(prose)!;
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                ValidateSourceXml712(Source723("<selecttext xpath=\"" + escaped + "\"/>")));
            string xml = Source723("<specificskill><condition>" + escaped + "</condition></specificskill>");
            ValidateSourceXml712(xml);
            using var fixture = new Fixture();
            File.WriteAllText(fixture.Data("gear.xml"), xml);
            Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
            byte[] bytes = capture!.ExportPrivateInputs();
            Assert.IsTrue(RetainedCharacterRuleSourceInputs.Import(bytes, capture.IntegrityDigest).IsPortable);
            Assert.IsTrue(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(xml)) >= 0);
            Directory.Delete(fixture.Root, recursive: true);
            Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
                bytes, capture.IntegrityDigest, out var restored));
            CollectionAssert.AreEqual(bytes, restored!.ExportPrivateInputs());
        }
    }

    [TestMethod]
    [DataRow("Opposed checks and/or tests +")]
    [DataRow("Opposed checks and/or tests[")]
    [DataRow("Opposed checks and/or tests (/character/created)")]
    public void PrivateHistory734_condition_prose_does_not_hide_expression_syntax(string text)
    {
        string escaped = SecurityElement.Escape(text)!;
        Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(
            Source723("<specificskill><condition>" + escaped + "</condition></specificskill>")));
        Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(
            Source723("<selecttext xpath=\"" + escaped + "\"/>")));
    }

    [TestMethod]
    public void PrivateHistory734_expression_and_prose_budgets_remain_bounded()
    {
        foreach (string expression in new[]
        {
            new string('(', 32) + "/character/created" + new string(')', 32),
            "/chummer/" + new string('a', 4096 - "/chummer/".Length)
        })
            ValidateSourceXml712(Source723("<selecttext xpath=\"" + expression + "\"/>"));
        foreach (string expression in new[]
        {
            new string('(', 33) + "/character/created" + new string(')', 33),
            "/chummer/" + new string('a', 4097 - "/chummer/".Length)
        })
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                ValidateSourceXml712(Source723("<selecttext xpath=\"" + expression + "\"/>")));
        string prose = new string('a', 4096 - " and/or tests".Length) + " and/or tests";
        ValidateSourceXml712(Source723("<specificskill><condition>" + prose + "</condition></specificskill>"));
        Assert.ThrowsExactly<InvalidOperationException>(() => ValidateSourceXml712(
            Source723("<specificskill><condition>" + prose + "s</condition></specificskill>")));
    }
}

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void PrivateHistory734_real_local_capture_is_refused_by_private_wire_without_resealing_history()
    {
        using var context = ReputationContext414(preserved: false);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var export = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, 4 * 1024 * 1024);
        byte[] original = export.ExportPrivateBytes(), record = File.ReadAllBytes(Record418(context));
        var parts = ReadWire654(original);
        // Hostile transport fixture only: neither local archive nor stored history
        // is rewritten. A recomputed envelope digest cannot grant V1 copy rights.
        parts.Archives[0] = CharacterCreationRuleSourceCaptureTests.CaptureLocal734(
            "<chummer><gears><gear><bonus><selecttext xpath=\"a div/home/private +\"/></bonus></gear></gears></chummer>");
        byte[] bytes = WriteWire654(parts);
        var expected = export.Binding with
        {
            ClosureDigest = RetainedCharacterRuleSourceInputs.Digest(bytes),
            ArchiveCount = parts.Archives.Count,
            ArchiveBytes = parts.Archives.Sum(archive => (long)archive.Length),
            CampaignCount = parts.Campaigns.Count
        };
        Assert.ThrowsExactly<WorkspacePrivateRuleSourceClosureLegacyFormatException>(() =>
            WorkspacePrivateRuleSourceClosure.Import(bytes, expected, 4 * 1024 * 1024, context.Queries));
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(export.Binding, WorkspacePrivateRuleSourceClosure.Import(
            original, export.Binding, 4 * 1024 * 1024, context.Queries).Binding);
    }
}
