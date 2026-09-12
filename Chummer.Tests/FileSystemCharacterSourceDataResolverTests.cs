#nullable enable annotations

using System;
using System.IO;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class FileSystemCharacterSourceDataResolverTests
{
    private const string SettingsId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string CanonicalLifeModuleSettingsId = "8a31af6d-7137-4284-872b-7d8087e156c6";
    private const string CanonicalSumToTenSettingsId = "3509a807-68ee-4c18-b7d5-b130313b4b77";
    private const string CanonicalImprovedSumToTenSettingsId = "2ef9b098-4cd2-4c2b-8f3d-76164e3f4f8e";
    private const string CanonicalStreetScumSettingsId = "4c34a8ed-2888-410c-afda-024475fa3c76";
    private const string CanonicalPrioritiesDigest =
        "sha256:4b41936b90fdd84a00b060585542eed8eb4d2045eeda1940c1c8a95af3eb91d1";
    private const string CanonicalMetatypesDigest =
        "sha256:ccee5dfabb8d0e193aa980e9905822a0f94fb9bb8093c162f5b694a974946425";
    private const string VehicleModId = "f89a112e-600a-4278-8731-9b14cf3737c9";

    [TestMethod]
    [DataRow(SettingsId)]
    [DataRow(CanonicalSumToTenSettingsId)]
    public void Rule_source_capture_uses_actual_canonical_saved_profile_and_reference_rows(string profileId)
    {
        string root = FindCoreRoot();
        var context = CreateContext(root, $"<character><settings>{profileId}</settings></character>")!;
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryCaptureRuleSources(out var capture));
        Assert.AreEqual(profileId, capture.SettingsProfileId);
        XElement setting = XDocument.Load(Path.Combine(root, "Chummer", "data", "settings.xml"))
            .Descendants("setting").Single(row => row.Element("id")?.Value == profileId);
        CollectionAssert.AreEqual(setting.Element("books")!.Elements("book").Select(row => row.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            capture.EnabledSourcebooks.ToArray());
        XElement[] actual = XDocument.Parse(capture.EffectiveReferencesXml).Root!.Element("rules")!.Elements("rule").ToArray();
        XElement[] expected = XDocument.Load(Path.Combine(root, "Chummer", "data", "references.xml"))
            .Root!.Element("rules")!.Elements("rule").ToArray();
        Assert.HasCount(expected.Length, actual);
        for (int index = 0; index < expected.Length; index++)
            Assert.IsTrue(XNode.DeepEquals(expected[index], actual[index]), $"Reference row {index}");
        AssertRuleCaptureDigests(capture);
    }

    [TestMethod]
    [DataRow(ContentOverlayModes.ReplaceFile)]
    [DataRow(ContentOverlayModes.MergeCatalog)]
    public void Rule_source_capture_applies_overlay_then_only_saved_custom_profile_and_keeps_raw_digests_distinct(string overlayMode)
    {
        string root = CreateRuleCaptureFixture(custom: true);
        string overlayRoot = CreateTempDirectory();
        try
        {
            string overlayData = Path.Combine(overlayRoot, "data");
            Directory.CreateDirectory(overlayData);
            File.WriteAllText(Path.Combine(overlayData, overlayMode == ContentOverlayModes.ReplaceFile
                ? "references.xml" : "references.fragment.xml"), RuleCaptureXml(200));
            string selectedPath = Path.Combine(root, "customdata", "Selected", "override_references.xml");
            File.WriteAllText(selectedPath, RuleCaptureXml(201));
            string other = Path.Combine(root, "customdata", "Not selected");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "override_references.xml"), RuleCaptureXml(999));
            var packs = new List<ContentOverlayPack>
            {
                new("references", "References", overlayRoot, overlayData, overlayData,
                    1, true, overlayMode, "selected overlay")
            };
            var resolver = new FileSystemCharacterSourceDataResolver(new MutableContentOverlayCatalogService(
                Path.Combine(root, "data"), Path.Combine(root, "lang"), packs));
            var context = resolver.TryCreateContext(RuleCaptureCharacterXml(custom: true))!;
            Assert.IsTrue(context.TryCaptureRuleSources(out var capture));
            Assert.AreEqual("201", XDocument.Parse(capture.EffectiveReferencesXml).Descendants("page").Single().Value);
            CollectionAssert.AreEqual(new[] { "SG", "SR5" }, capture.EnabledSourcebooks.ToArray());
            AssertRuleCaptureDigests(capture);

            // Changing shadowed raw input still changes provenance, not the effective XML.
            File.WriteAllText(Path.Combine(root, "data", "references.xml"), RuleCaptureXml(160));
            var fresh = resolver.TryCreateContext(RuleCaptureCharacterXml(custom: true))!;
            Assert.IsTrue(fresh.TryCaptureRuleSources(out var changedBase));
            Assert.AreEqual(capture.EffectiveReferencesXml, changedBase.EffectiveReferencesXml);
            Assert.AreEqual(capture.EffectiveReferencesXmlDigest, changedBase.EffectiveReferencesXmlDigest);
            Assert.AreNotEqual(capture.EffectiveReferencesInputsDigest, changedBase.EffectiveReferencesInputsDigest);
            Assert.AreEqual(capture.SelectedReferencesCustomDataInputsDigest, changedBase.SelectedReferencesCustomDataInputsDigest);

            File.WriteAllText(selectedPath, RuleCaptureXml(202));
            fresh = resolver.TryCreateContext(RuleCaptureCharacterXml(custom: true))!;
            Assert.IsTrue(fresh.TryCaptureRuleSources(out var changedCustom));
            Assert.AreNotEqual(changedBase.SelectedReferencesCustomDataInputsDigest, changedCustom.SelectedReferencesCustomDataInputsDigest);
            Assert.AreEqual(changedBase.EffectiveReferencesInputsDigest, changedCustom.EffectiveReferencesInputsDigest);
            Assert.AreEqual("202", XDocument.Parse(changedCustom.EffectiveReferencesXml).Descendants("page").Single().Value);
        }
        finally { DeleteTempDirectory(overlayRoot); DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("empty")]
    [DataRow("duplicate-id")]
    [DataRow("duplicate-container")]
    [DataRow("duplicate-field")]
    [DataRow("duplicate-custom")]
    [DataRow("duplicate-overlay")]
    [DataRow("nested-field")]
    [DataRow("dtd")]
    [DataRow("oversized")]
    [DataRow("oversized-result")]
    [DataRow("missing-profile")]
    [DataRow("duplicate-profile")]
    public void Rule_source_capture_rejects_missing_ambiguous_and_unsafe_inputs(string failure)
    {
        // A selected directory forces the generic target-merging branch, which
        // would otherwise silently coalesce duplicate base/custom reference IDs.
        string root = CreateRuleCaptureFixture(custom: true);
        try
        {
            string path = Path.Combine(root, "data", "references.xml");
            XElement row = XElement.Parse(RuleCaptureXml()).Element("rules")!.Element("rule")!;
            var packs = new List<ContentOverlayPack>();
            switch (failure)
            {
                case "missing": File.Delete(path); break;
                case "empty": File.WriteAllText(path, "<chummer><rules/></chummer>"); break;
                case "duplicate-id": File.WriteAllText(path, new XElement("chummer", new XElement("rules", row, new XElement(row))).ToString()); break;
                case "duplicate-container": File.WriteAllText(path, new XElement("chummer", new XElement("rules", row), new XElement("rules")).ToString()); break;
                case "duplicate-field": row.Add(new XElement("page", "999")); File.WriteAllText(path, new XElement("chummer", new XElement("rules", row)).ToString()); break;
                case "duplicate-custom": File.WriteAllText(Path.Combine(root, "customdata", "Selected", "override_references.xml"),
                    new XElement("chummer", new XElement("rules", row, new XElement(row))).ToString()); break;
                case "duplicate-overlay":
                    string overlay = Path.Combine(root, "overlay");
                    Directory.CreateDirectory(overlay);
                    File.WriteAllText(Path.Combine(overlay, "references.fragment.xml"),
                        new XElement("chummer", new XElement("rules", row, new XElement(row))).ToString());
                    packs.Add(new ContentOverlayPack("duplicate", "Duplicate", overlay, overlay, overlay,
                        1, true, ContentOverlayModes.MergeCatalog, "duplicate raw input"));
                    break;
                case "nested-field": row.Element("page")!.ReplaceNodes(new XElement("nested", "159"));
                    File.WriteAllText(path, new XElement("chummer", new XElement("rules", row)).ToString()); break;
                case "dtd": File.WriteAllText(path, "<!DOCTYPE chummer [<!ENTITY local 'forged'>]>" + RuleCaptureXml().Replace("Initiative Score", "&local;", StringComparison.Ordinal)); break;
                case "oversized": File.WriteAllText(path, RuleCaptureXml().Replace("Initiative Score", new string('x', 4 * 1024 * 1024), StringComparison.Ordinal)); break;
                case "oversized-result":
                    string largeRow = RuleCaptureXml().Replace("Initiative Score", new string('x', 2 * 1024 * 1024), StringComparison.Ordinal);
                    File.WriteAllText(path, largeRow);
                    File.WriteAllText(Path.Combine(root, "customdata", "Selected", "custom_references.xml"),
                        largeRow.Replace("A5D18354-17D4-4102-9295-03E6D125CB67", "A5D18354-17D4-4102-9295-03E6D125CB68", StringComparison.Ordinal));
                    break;
                case "missing-profile": File.WriteAllText(Path.Combine(root, "data", "settings.xml"), "<chummer><settings/></chummer>"); break;
                case "duplicate-profile":
                    string settingsPath = Path.Combine(root, "data", "settings.xml");
                    XDocument settings = XDocument.Load(settingsPath);
                    settings.Root!.Element("settings")!.Add(new XElement(settings.Descendants("setting").Single()));
                    settings.Save(settingsPath);
                    break;
                default: Assert.Fail($"Unknown failure: {failure}"); break;
            }
            var context = new FileSystemCharacterSourceDataResolver(new MutableContentOverlayCatalogService(
                Path.Combine(root, "data"), Path.Combine(root, "lang"), packs))
                .TryCreateContext(RuleCaptureCharacterXml(custom: true));
            if (failure is "missing-profile" or "duplicate-profile")
                Assert.IsNull(context);
            else
            {
                Assert.IsNotNull(context);
                Assert.IsFalse(context.TryCaptureRuleSources(out var capture));
                Assert.AreSame(CharacterRuleSourceCapture.Unavailable, capture);
                Assert.AreEqual(string.Empty, capture.EffectiveReferencesXml);
            }
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Rule_source_capture_detaches_constructor_books_and_returned_xml_from_source_storage()
    {
        string root = CreateRuleCaptureFixture();
        try
        {
            var context = CreateContext(root, RuleCaptureCharacterXml())!;
            Assert.IsTrue(context.TryCaptureRuleSources(out var capture));
            string[] suppliedBooks = capture.EnabledSourcebooks.ToArray();
            var copied = new CharacterRuleSourceCapture(capture.RawCharacterXmlDigest, capture.SettingsProfileId, suppliedBooks,
                capture.RawProfileInputsDigest, capture.EffectiveReferencesInputsDigest,
                capture.SelectedReferencesCustomDataInputsDigest, capture.EffectiveReferencesXml,
                capture.EffectiveReferencesXmlDigest);
            suppliedBooks[0] = "FORGED";
            Assert.AreNotEqual("FORGED", copied.EnabledSourcebooks[0]);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)capture.EnabledSourcebooks)[0] = "FORGED");
            byte[] callerBytes = System.Text.Encoding.UTF8.GetBytes(capture.EffectiveReferencesXml);
            Array.Fill(callerBytes, (byte)'x');
            Assert.IsTrue(context.TryCaptureRuleSources(out var next));
            Assert.AreNotSame(capture, next);
            Assert.AreNotSame(capture.EnabledSourcebooks, next.EnabledSourcebooks);
            Assert.AreEqual(capture.EffectiveReferencesXml, next.EffectiveReferencesXml);
            AssertRuleCaptureDigests(next);
            File.WriteAllText(Path.Combine(root, "data", "references.xml"), RuleCaptureXml(999));
            Assert.AreEqual("159", XDocument.Parse(capture.EffectiveReferencesXml).Descendants("page").Single().Value);
            Assert.IsFalse(context.TryCaptureRuleSources(out _));
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow("references")]
    [DataRow("settings")]
    [DataRow("custom")]
    [DataRow("catalog")]
    public void Rule_source_capture_drift_is_sticky_but_a_fresh_context_can_recover(string changedInput)
    {
        string root = CreateRuleCaptureFixture(custom: true);
        try
        {
            string customPath = Path.Combine(root, "customdata", "Selected", "override_references.xml");
            File.WriteAllText(customPath, RuleCaptureXml(160));
            var packs = new List<ContentOverlayPack>();
            var resolver = new FileSystemCharacterSourceDataResolver(new MutableContentOverlayCatalogService(
                Path.Combine(root, "data"), Path.Combine(root, "lang"), packs));
            string xml = RuleCaptureCharacterXml(custom: true);
            var context = resolver.TryCreateContext(xml)!;
            Assert.IsTrue(context.TryCaptureRuleSources(out var initial));
            string path = changedInput switch
            {
                "settings" => Path.Combine(root, "data", "settings.xml"),
                "custom" => customPath,
                _ => Path.Combine(root, "data", "references.xml")
            };
            byte[] original = File.ReadAllBytes(path);
            if (changedInput == "catalog")
                packs.Add(new ContentOverlayPack("disabled", "Disabled", root, Path.Combine(root, "data"), root,
                    1, false, ContentOverlayModes.ReplaceFile, "new catalog membership"));
            else
                File.AppendAllText(path, "\n<!-- changed captured input -->");
            Assert.IsFalse(context.TryCaptureRuleSources(out var rejected));
            Assert.AreSame(CharacterRuleSourceCapture.Unavailable, rejected);
            if (changedInput == "catalog") packs.Clear(); else File.WriteAllBytes(path, original);
            Assert.IsFalse(context.TryCaptureRuleSources(out _), "Restore must not revive a drifted snapshot.");
            var fresh = resolver.TryCreateContext(xml)!;
            Assert.IsTrue(fresh.TryCaptureRuleSources(out var recovered));
            Assert.AreEqual(initial.EffectiveReferencesXml, recovered.EffectiveReferencesXml);
            Assert.AreEqual(initial.EffectiveReferencesInputsDigest, recovered.EffectiveReferencesInputsDigest);
            Assert.AreEqual(initial.SelectedReferencesCustomDataInputsDigest, recovered.SelectedReferencesCustomDataInputsDigest);
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow("rewrite")]
    [DataRow("delete")]
    public void Rule_source_capture_rejects_newly_observed_reference_drift_before_publication(string mutation)
    {
        string root = CreateRuleCaptureFixture();
        try
        {
            string path = Path.GetFullPath(Path.Combine(root, "data", "references.xml"));
            int mutations = 0;
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(root, root, null), observed =>
                {
                    if (observed == path && mutations++ == 0)
                    {
                        if (mutation == "delete") File.Delete(path);
                        else File.WriteAllText(path, RuleCaptureXml(999));
                    }
                });
            var context = resolver.TryCreateContext(RuleCaptureCharacterXml())!;
            Assert.IsFalse(context.TryCaptureRuleSources(out var rejected));
            Assert.AreSame(CharacterRuleSourceCapture.Unavailable, rejected);
            Assert.AreEqual(1, mutations);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            Assert.IsFalse(context.TryCaptureRuleSources(out var repeated));
            Assert.AreSame(CharacterRuleSourceCapture.Unavailable, repeated);
            Assert.AreEqual(1, mutations, "A poisoned context must not reread and admit the changed file.");
            File.WriteAllText(path, RuleCaptureXml(999));
            Assert.IsFalse(context.TryCaptureRuleSources(out _), "Restoring bytes must not revive the old context.");
            Assert.AreEqual(1, mutations);
            var fresh = resolver.TryCreateContext(RuleCaptureCharacterXml())!;
            Assert.IsTrue(fresh.TryCaptureRuleSources(out var recovered));
            Assert.AreEqual("999", XDocument.Parse(recovered.EffectiveReferencesXml).Descendants("page").Single().Value);
            AssertRuleCaptureDigests(recovered);
            Assert.AreEqual(2, mutations, "Only the fresh context may capture the now-stable source.");
            Assert.IsFalse(context.TryCaptureRuleSources(out _), "Fresh admission must not revive the old context.");
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow("base")]
    [DataRow("overlay")]
    [DataRow("custom")]
    public void Rule_source_capture_bounds_reference_reads_before_callback_and_xml_parse(string inputKind)
    {
        string root = CreateRuleCaptureFixture(custom: true);
        try
        {
            string path = inputKind == "custom"
                ? Path.Combine(root, "customdata", "Selected", "override_references.xml")
                : Path.Combine(root, inputKind == "overlay" ? "overlay" : "data", "references.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, RuleCaptureXml().Replace("Initiative Score", new string('x', 4 * 1024 * 1024), StringComparison.Ordinal));
            var packs = new List<ContentOverlayPack>();
            if (inputKind == "overlay")
                packs.Add(new ContentOverlayPack("large", "Large", root, Path.GetDirectoryName(path)!, root,
                    1, true, ContentOverlayModes.ReplaceFile, "bounded overlay"));
            int callbacksForOversizedInput = 0;
            var resolver = new FileSystemCharacterSourceDataResolver(new MutableContentOverlayCatalogService(
                Path.Combine(root, "data"), Path.Combine(root, "lang"), packs), observed =>
                {
                    if (observed == Path.GetFullPath(path)) callbacksForOversizedInput++;
                });
            var context = resolver.TryCreateContext(RuleCaptureCharacterXml(custom: true))!;
            Assert.IsNotNull(context);
            Assert.IsFalse(context.TryCaptureRuleSources(out var capture));
            Assert.AreSame(CharacterRuleSourceCapture.Unavailable, capture);
            Assert.AreEqual(0, callbacksForOversizedInput);
            Assert.IsFalse(resolver.LastSourceInputSnapshotDiagnostics!.PhysicalXmlParsesByPath.ContainsKey(Path.GetFullPath(path)));
            Assert.IsFalse(resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadsByPath.ContainsKey(Path.GetFullPath(path)));
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow("id")]
    [DataRow("name")]
    public void Rule_source_capture_supports_exact_partial_legacy_amendments(string identifierKind)
    {
        string root = CreateRuleCaptureFixture(custom: true);
        try
        {
            string identifier = identifierKind == "id"
                ? "<id>A5D18354-17D4-4102-9295-03E6D125CB67</id>" : "<name>Initiative Score</name>";
            File.WriteAllText(Path.Combine(root, "customdata", "Selected", "amend_references.xml"),
                $"<chummer><rules><rule>{identifier}<page>177</page></rule></rules></chummer>");
            var context = CreateContext(root, RuleCaptureCharacterXml(custom: true))!;
            Assert.IsTrue(context.TryCaptureRuleSources(out var capture));
            XElement row = XDocument.Parse(capture.EffectiveReferencesXml).Descendants("rule").Single();
            Assert.AreEqual("A5D18354-17D4-4102-9295-03E6D125CB67", row.Element("id")!.Value);
            Assert.AreEqual("SR5", row.Element("source")!.Value);
            Assert.AreEqual("Initiative Score", row.Element("name")!.Value);
            Assert.AreEqual("177", row.Element("page")!.Value);
            AssertRuleCaptureDigests(capture);
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Rule_source_capture_binds_exact_character_xml_independently_of_same_profile_sources()
    {
        string root = CreateRuleCaptureFixture();
        try
        {
            string firstXml = CharacterXml("<alias>First</alias>");
            string secondXml = CharacterXml("<alias>Second</alias>");
            Assert.IsTrue(CreateContext(root, firstXml)!.TryCaptureRuleSources(out var first));
            Assert.IsTrue(CreateContext(root, secondXml)!.TryCaptureRuleSources(out var second));
            Assert.AreEqual("sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(firstXml))).ToLowerInvariant(), first.RawCharacterXmlDigest);
            Assert.AreEqual("sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secondXml))).ToLowerInvariant(), second.RawCharacterXmlDigest);
            Assert.AreNotEqual(first.RawCharacterXmlDigest, second.RawCharacterXmlDigest);
            Assert.AreEqual(first.SettingsProfileId, second.SettingsProfileId);
            CollectionAssert.AreEqual(first.EnabledSourcebooks.ToArray(), second.EnabledSourcebooks.ToArray());
            Assert.AreEqual(first.RawProfileInputsDigest, second.RawProfileInputsDigest);
            Assert.AreEqual(first.EffectiveReferencesInputsDigest, second.EffectiveReferencesInputsDigest);
            Assert.AreEqual(first.SelectedReferencesCustomDataInputsDigest, second.SelectedReferencesCustomDataInputsDigest);
            Assert.AreEqual(first.EffectiveReferencesXml, second.EffectiveReferencesXml);
            Assert.AreEqual(first.EffectiveReferencesXmlDigest, second.EffectiveReferencesXmlDigest);
        }
        finally { DeleteTempDirectory(root); }
    }

    private static string RuleCaptureXml(int page = 159) =>
        $"<chummer><rules><rule><id>A5D18354-17D4-4102-9295-03E6D125CB67</id><name>Initiative Score</name><source>SR5</source><page>{page}</page></rule></rules></chummer>";

    private static string RuleCaptureCharacterXml(bool custom = false) => CharacterXml(custom
        ? "<customdatadirectorynames><directoryname>Selected</directoryname></customdatadirectorynames>" : "");

    private static string CreateRuleCaptureFixture(bool custom = false)
    {
        string root = CreateTempDirectory();
        WriteBaseContent(root, custom
            ? "<customdatadirectoryname><directoryname>Selected</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>" : "");
        if (custom) Directory.CreateDirectory(Path.Combine(root, "customdata", "Selected"));
        File.WriteAllText(Path.Combine(root, "data", "references.xml"), RuleCaptureXml());
        return root;
    }

    private static void AssertRuleCaptureDigests(CharacterRuleSourceCapture capture)
    {
        byte[] actualBytes = System.Text.Encoding.UTF8.GetBytes(capture.EffectiveReferencesXml);
        Assert.IsLessThanOrEqualTo(4 * 1024 * 1024, actualBytes.Length);
        string expected = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actualBytes)).ToLowerInvariant();
        Assert.AreEqual(expected, capture.EffectiveReferencesXmlDigest);
        foreach (string digest in new[] { capture.RawCharacterXmlDigest, capture.RawProfileInputsDigest, capture.EffectiveReferencesInputsDigest,
                     capture.SelectedReferencesCustomDataInputsDigest, capture.EffectiveReferencesXmlDigest })
            Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(digest));
    }

    [TestMethod]
    public void Canonical_active_skill_source_resolves_exact_saved_source_guid()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;

        Assert.IsTrue(context.TryResolveActiveSkillSource(
            "b52f7575-eebf-41c4-938d-df3397b5ee68",
            out CharacterActiveSkillSource source));
        Assert.AreEqual("b52f7575-eebf-41c4-938d-df3397b5ee68", source.SourceSkillId);
        Assert.AreEqual("Aeronautics Mechanic", source.Name);
        Assert.AreEqual("Technical Active", source.SkillCategory);
        Assert.AreEqual("Engineering", source.SkillGroup);
        Assert.AreEqual("LOG", source.DefaultAttribute);
        Assert.IsFalse(source.IsExotic);
        StringAssert.Contains(source.RawSourceXml, "<skillgroup>Engineering</skillgroup>");

        Assert.IsFalse(context.TryResolveActiveSkillSource(
            "11111111-1111-1111-1111-111111111111",
            out _));
    }

    [TestMethod]
    public void Canonical_knowledge_skill_source_resolves_exact_saved_source_guid()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;

        Assert.IsTrue(context.TryResolveKnowledgeSkillSource(
            "9f348c99-27e8-47ac-a098-a8a6a54c446a",
            out CharacterKnowledgeSkillSource source));
        Assert.AreEqual("9f348c99-27e8-47ac-a098-a8a6a54c446a", source.SourceSkillId);
        Assert.AreEqual("Administration", source.Name);
        Assert.AreEqual("Professional", source.SkillCategory);
        Assert.AreEqual("LOG", source.DefaultAttribute);
        StringAssert.Contains(source.RawSourceXml, "<name>Administration</name>");
        Assert.IsNull(
            XElement.Parse(source.RawSourceXml).Element("source"),
            "Canonical source-less knowledge skills are built-in enabled authority.");

        Assert.IsFalse(context.TryResolveKnowledgeSkillSource(
            "11111111-1111-1111-1111-111111111111",
            out _));
        Assert.IsFalse(context.TryResolveKnowledgeSkillSource(
            Guid.Empty.ToString("D"),
            out _));
    }

    [TestMethod]
    public void Canonical_creation_skill_catalog_sources_resolve_every_row()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;
        XDocument skills = XDocument.Load(Path.Combine(coreRoot, "Chummer", "data", "skills.xml"));

        string[] unresolvedActive = skills.Root?.Element("skills")?.Elements("skill")
            .Where(row => !context.TryResolveCareerSkillSpecializationSource(
                row.Element("id")?.Value ?? string.Empty,
                CharacterCareerSkillKind.Active,
                out _))
            .Select(row => $"{row.Element("id")?.Value}:{row.Element("name")?.Value}")
            .ToArray() ?? [];
        string[] unresolvedKnowledge = skills.Root?.Element("knowledgeskills")?.Elements("skill")
            .Where(row => !context.TryResolveCareerSkillSpecializationSource(
                row.Element("id")?.Value ?? string.Empty,
                CharacterCareerSkillKind.Knowledge,
                out _))
            .Select(row => $"{row.Element("id")?.Value}:{row.Element("name")?.Value}")
            .ToArray() ?? [];

        Assert.AreEqual(0, unresolvedActive.Length, string.Join(",", unresolvedActive));
        Assert.AreEqual(0, unresolvedKnowledge.Length, string.Join(",", unresolvedKnowledge));
    }

    [TestMethod]
    public void Canonical_career_reputation_policy_is_profile_bound()
    {
        ICharacterSourceDataContext context = CreateContext(FindCoreRoot(),
            $"<character><settings>{SettingsId}</settings></character>")!;
        Assert.IsTrue(context.TryResolveCareerReputationSettings(out var settings, out string rawRuleState));
        Assert.IsFalse(settings.UseCalculatedPublicAwareness);
        StringAssert.Contains(rawRuleState, SettingsId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(rawRuleState));
    }

    [TestMethod]
    public void Career_reputation_reads_real_profile_and_saved_document_without_defaulting_missing_policy()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, string.Empty);
            string path = Path.Combine(root, "data", "settings.xml");
            string profile = File.ReadAllText(path);
            File.WriteAllText(path, profile.Replace("</setting>",
                "<usecalculatedpublicawareness>True</usecalculatedpublicawareness></setting>", StringComparison.Ordinal));
            var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
            var saved = new WorkspaceStoredDocument(new CharacterWorkspaceId("real-reputation-profile"),
                new WorkspaceDocument(CharacterXml("""
                    <created>True</created><streetcred>1</streetcred><notoriety>2</notoriety>
                    <publicawareness>1</publicawareness><burntstreetcred>0</burntstreetcred>
                    <expenses><expense><type>Karma</type><amount>30</amount><refund>False</refund></expense></expenses>
                    <improvements/>
                    """), "sr5"), 7, 7, DateTimeOffset.UnixEpoch);
            string storePath = Path.Combine(root, "workspaces");
            var store = new FileWorkspaceStore(storePath);
            Assert.IsTrue(store.CreateWorkspaceDocument(saved.Id, saved.Document).Success);
            Assert.IsTrue(store.SaveCheckpoint(saved.Id, 1).Success);
            saved = new FileWorkspaceStore(storePath).Get(saved.Id).Value!;
            var beforeFiles = Directory.GetFiles(storePath, "*", SearchOption.AllDirectories)
                .ToDictionary(file => file, File.ReadAllBytes, StringComparer.Ordinal);
            Assert.IsTrue(CharacterCareerReputationProjector.TryRead(saved, resolver, out var calculated, out var error), error);
            Assert.AreEqual(30, calculated!.Reputation.Inputs.CareerKarma);
            Assert.AreEqual(3, calculated.Reputation.TotalPublicAwareness);
            File.WriteAllText(path, profile.Replace("</setting>",
                "<usecalculatedpublicawareness>False</usecalculatedpublicawareness></setting>", StringComparison.Ordinal));
            Assert.IsTrue(CharacterCareerReputationProjector.TryRead(saved, resolver, out var manual, out error), error);
            Assert.AreEqual(1, manual!.Reputation.TotalPublicAwareness);
            Assert.AreNotEqual(calculated.RuleStateDigest, manual.RuleStateDigest);
            Assert.AreEqual(calculated.SourceDigest, manual.SourceDigest);
            File.WriteAllText(path, profile);
            Assert.IsFalse(CharacterCareerReputationProjector.TryRead(saved, resolver, out var unavailable, out error));
            Assert.IsNull(unavailable);
            Assert.AreEqual("reputation_source_unavailable", error);
            foreach (var file in beforeFiles)
                CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), "Projection must not rewrite the saved workspace.");
            Assert.AreEqual(beforeFiles.Count, Directory.GetFiles(storePath, "*", SearchOption.AllDirectories).Length);
            Assert.AreEqual(1L, new FileWorkspaceStore(storePath).Get(saved.Id).Value!.ContentRevision);
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Saved_after_run_reward_flows_into_reputation_and_burn_preview_without_recredit_or_mutation()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, string.Empty);
            string profilePath = Path.Combine(root, "data", "settings.xml");
            File.WriteAllText(profilePath, File.ReadAllText(profilePath).Replace("</setting>",
                "<usecalculatedpublicawareness>True</usecalculatedpublicawareness></setting>", StringComparison.Ordinal));
            string storePath = Path.Combine(root, "reward-workspaces");
            var store = new FileWorkspaceStore(storePath);
            var id = new CharacterWorkspaceId("reputation-after-reward");
            var document = new WorkspaceDocument(CharacterXml("""
                <created>True</created><karma>100</karma><nuyen>1000</nuyen>
                <streetcred>1</streetcred><notoriety>2</notoriety><publicawareness>1</publicawareness>
                <burntstreetcred>0</burntstreetcred><expenses/><improvements/><notes>Preserve runner</notes>
                """), "sr5");
            Assert.IsTrue(store.CreateWorkspaceDocument(id, document).Success);
            Assert.IsTrue(store.SaveCheckpoint(id, 1).Success);
            var rewards = new WorkspaceCharacterAfterRunRewardService(store);
            var preview = rewards.Preview(new CharacterAfterRunRewardPreviewRequest(id, Guid.NewGuid(), Guid.NewGuid(),
                30, 12500, new DateTime(2078, 9, 7, 18, 0, 0), "Completed run"));
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, preview.Outcome, preview.Error);
            var command = preview.Preview!.Command with { ExplicitlyConfirmed = true };
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, rewards.Commit(command).Outcome);

            var saved = new FileWorkspaceStore(storePath).Get(id).Value!;
            var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
            string before = System.Text.Json.JsonSerializer.Serialize(saved);
            Assert.IsTrue(CharacterCareerReputationProjector.TryRead(saved, resolver, out var reputation, out var error), error);
            Assert.AreEqual(30, reputation!.Reputation.Inputs.CareerKarma);
            Assert.AreEqual(130, rewards.Read(id).Snapshot!.AvailableKarma);
            Assert.AreEqual(4, reputation.Reputation.TotalStreetCred);
            Assert.AreEqual(3, reputation.Reputation.TotalPublicAwareness);
            Assert.IsTrue(CharacterCareerReputationRules.TryQuoteBurnStreetCred(reputation.Reputation.Inputs, out var burn));
            Assert.AreEqual(2, burn!.After.TotalStreetCred);
            Assert.AreEqual(1, burn.After.TotalNotoriety);
            Assert.AreEqual(2, burn.After.TotalPublicAwareness);
            var coldRewards = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(storePath));
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, coldRewards.Commit(command).Outcome);
            Assert.AreEqual(before, System.Text.Json.JsonSerializer.Serialize(new FileWorkspaceStore(storePath).Get(id).Value));
            Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts!.Count);
            Assert.AreEqual(2L, saved.ContentRevision);
        }
        finally { DeleteTempDirectory(root); }
    }

    [DataRow("<usecalculatedpublicawareness>True</usecalculatedpublicawareness>", true, true)]
    [DataRow("<usecalculatedpublicawareness>False</usecalculatedpublicawareness>", true, false)]
    [DataRow("", false, false)]
    [DataRow("<usecalculatedpublicawareness/>", false, false)]
    [DataRow("<usecalculatedpublicawareness>1</usecalculatedpublicawareness>", false, false)]
    [DataRow("<usecalculatedpublicawareness>yes</usecalculatedpublicawareness>", false, false)]
    [DataRow("<usecalculatedpublicawareness> True </usecalculatedpublicawareness>", false, false)]
    [DataRow("<usecalculatedpublicawareness enabled=\"false\">True</usecalculatedpublicawareness>", false, false)]
    [DataRow("<usecalculatedpublicawareness><value>True</value></usecalculatedpublicawareness>", false, false)]
    [DataRow("<usecalculatedpublicawareness>True</usecalculatedpublicawareness><usecalculatedpublicawareness>False</usecalculatedpublicawareness>", false, false)]
    [DataRow("<usecalculatedpublicawareness>True</usecalculatedpublicawareness><usecalculatedpublicawareness>True</usecalculatedpublicawareness>", false, false)]
    [TestMethod]
    public void Reputation_policy_requires_one_explicit_well_formed_value(string node, bool available, bool expected)
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, string.Empty);
            string path = Path.Combine(root, "data", "settings.xml");
            File.WriteAllText(path, File.ReadAllText(path).Replace("</setting>", node + "</setting>", StringComparison.Ordinal));
            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.AreEqual(available, context.TryResolveCareerReputationSettings(out var settings, out string raw));
            Assert.AreEqual(expected, settings.UseCalculatedPublicAwareness);
            Assert.AreEqual(available, !string.IsNullOrEmpty(raw));
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Retained_reputation_context_rejects_changed_profile_and_new_context_binds_new_bytes()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, string.Empty);
            string path = Path.Combine(root, "data", "settings.xml");
            string original = File.ReadAllText(path).Replace("</setting>",
                "<usecalculatedpublicawareness>False</usecalculatedpublicawareness></setting>", StringComparison.Ordinal);
            File.WriteAllText(path, original);
            ICharacterSourceDataContext old = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(old.TryResolveCareerReputationSettings(out _, out string oldRuleState));
            File.WriteAllText(path, original.Replace("<usecalculatedpublicawareness>False", "<usecalculatedpublicawareness>True", StringComparison.Ordinal));
            Assert.IsFalse(old.TryResolveCareerReputationSettings(out _, out string stale));
            Assert.AreEqual(string.Empty, stale);
            ICharacterSourceDataContext fresh = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(fresh.TryResolveCareerReputationSettings(out var settings, out string currentRuleState));
            Assert.IsTrue(settings.UseCalculatedPublicAwareness);
            Assert.AreNotEqual(oldRuleState, currentRuleState);
            File.WriteAllText(path, File.ReadAllText(path).Replace("#,0.###", "#,0.##", StringComparison.Ordinal));
            ICharacterSourceDataContext samePolicyNewProfile = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(samePolicyNewProfile.TryResolveCareerReputationSettings(out var sameSettings, out string rebound));
            Assert.AreEqual(settings, sameSettings);
            Assert.AreNotEqual(currentRuleState, rebound, "Profile binding must cover raw profile inputs, not only the Boolean.");
            File.WriteAllText(path, original);
            Assert.IsFalse(old.TryResolveCareerReputationSettings(out _, out _), "Drifted context must not revive after A-to-B-to-A source changes.");
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Canonical_career_specialization_settings_preserve_profile_costs_and_default_group_policy()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;

        Assert.IsTrue(context.TryResolveCareerSkillSpecializationSettings(
            out CharacterCareerSkillSpecializationSettings settings,
            out string rawRuleState));
        Assert.AreEqual(7, settings.KarmaActiveSpecialization);
        Assert.AreEqual(7, settings.KarmaKnowledgeSpecialization);
        Assert.IsTrue(settings.SpecializationsBreakSkillGroups,
            "The legacy CharacterSettings default is true when this optional profile node is absent.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(rawRuleState));
        StringAssert.Contains(rawRuleState, SettingsId);
    }

    [TestMethod]
    public void Career_specialization_settings_resolve_nested_costs_and_reject_malformed_profile_values()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, string.Empty);
            string settingsPath = Path.Combine(root, "data", "settings.xml");
            string settingsXml = File.ReadAllText(settingsPath)
                .Replace(
                    "<karmaattribute>5</karmaattribute>",
                    "<karmaattribute>5</karmaattribute><karmaspecialization>7</karmaspecialization>"
                    + "<karmaknospecialization>5</karmaknospecialization>",
                    StringComparison.Ordinal)
                .Replace(
                    "<specializationsbreakskillgroups>True</specializationsbreakskillgroups>",
                    "<specializationsbreakskillgroups>False</specializationsbreakskillgroups>",
                    StringComparison.Ordinal);
            File.WriteAllText(settingsPath, settingsXml);

            ICharacterSourceDataContext exact = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(exact.TryResolveCareerSkillSpecializationSettings(
                out CharacterCareerSkillSpecializationSettings settings,
                out string rawRuleState));
            Assert.AreEqual(7, settings.KarmaActiveSpecialization);
            Assert.AreEqual(5, settings.KarmaKnowledgeSpecialization);
            Assert.IsFalse(settings.SpecializationsBreakSkillGroups);
            Assert.IsFalse(string.IsNullOrWhiteSpace(rawRuleState));

            File.WriteAllText(
                settingsPath,
                settingsXml.Replace(
                    "<karmaknospecialization>5</karmaknospecialization>",
                    "<karmaknospecialization>101</karmaknospecialization>",
                    StringComparison.Ordinal));
            ICharacterSourceDataContext malformed = CreateContext(root, CharacterXml())!;
            Assert.IsFalse(malformed.TryResolveCareerSkillSpecializationSettings(out _, out _));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Canonical_career_specialization_source_is_enabled_profile_and_kind_exact()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;

        Assert.IsTrue(context.TryResolveCareerSkillSpecializationSource(
            "adf31a50-b228-4e09-a09c-46ab9f5e59a1",
            CharacterCareerSkillKind.Active,
            out CharacterCareerSkillSpecializationSource active));
        Assert.AreEqual(CharacterCareerSkillKind.Active, active.Kind);
        Assert.AreEqual("Pistols", active.Name);
        Assert.AreEqual("Combat Active", active.SkillCategory);
        Assert.IsTrue(active.Options.Any(option =>
            option.Kind == CharacterCareerSkillSpecializationOptionKind.SourceCatalog
            && option.Name == "Semi-Automatics"));
        Assert.IsTrue(active.Options.Any(option =>
            option.Kind == CharacterCareerSkillSpecializationOptionKind.CombatWeapon
            && option.Name == "Ares Predator V"));
        Assert.AreEqual(
            active.Options.Count,
            active.Options.Select(option => option.OptionIdentity).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(active.Options.All(option => option.OptionIdentity.Length == 64));
        StringAssert.Contains(active.RawSourceState, "<name>Pistols</name>");
        StringAssert.Contains(active.RawSourceState, "<name>Ares Predator V</name>");

        Assert.IsTrue(context.TryResolveCareerSkillSpecializationSource(
            "9f348c99-27e8-47ac-a098-a8a6a54c446a",
            CharacterCareerSkillKind.Knowledge,
            out CharacterCareerSkillSpecializationSource knowledge));
        Assert.AreEqual(CharacterCareerSkillKind.Knowledge, knowledge.Kind);
        Assert.AreEqual("Administration", knowledge.Name);
        Assert.IsFalse(knowledge.Options.Any(option =>
            option.Kind == CharacterCareerSkillSpecializationOptionKind.CombatWeapon));

        Assert.IsTrue(context.TryResolveCareerSkillSpecializationSource(
            "8db5cc64-5dc4-4ae9-a56b-e0c54a6e2413",
            CharacterCareerSkillKind.Knowledge,
            out CharacterCareerSkillSpecializationSource saederKrupp));
        Assert.IsTrue(saederKrupp.Options.Any(option =>
            option.Name == "Saeder-Krupp Prime"));
        Assert.IsFalse(saederKrupp.Options.Any(option =>
            option.Name != option.Name.Trim()));
        Assert.AreEqual(
            saederKrupp.Options.Count,
            saederKrupp.Options.Select(option => option.Name)
                .Distinct(StringComparer.Ordinal).Count());

        Assert.IsFalse(context.TryResolveCareerSkillSpecializationSource(
            active.SourceSkillId,
            CharacterCareerSkillKind.Knowledge,
            out _));
        Assert.IsFalse(context.TryResolveCareerSkillSpecializationSource(
            Guid.Empty.ToString("D"),
            CharacterCareerSkillKind.Active,
            out _));
        Assert.IsFalse(context.TryResolveCareerSkillSpecializationSource(
            active.SourceSkillId,
            (CharacterCareerSkillKind)99,
            out _));
    }

    [TestMethod]
    public void Career_specialization_source_fails_closed_when_its_book_is_not_enabled()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, string.Empty);
            string settingsPath = Path.Combine(root, "data", "settings.xml");
            File.WriteAllText(
                settingsPath,
                File.ReadAllText(settingsPath).Replace(
                    "<book>SR5</book>",
                    string.Empty,
                    StringComparison.Ordinal));

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsFalse(context.TryResolveCareerSkillSpecializationSource(
                "40c72109-8924-45ca-a4d7-255b75e6a6b0",
                CharacterCareerSkillKind.Active,
                out _));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Canonical_priority_profile_projects_digest_bound_rank_and_creation_karma_authority()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;

        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.AreEqual(CharacterCreationBuildMethods.Priority, authority.BuildMethod);
        Assert.AreEqual(25, authority.CreationKarmaTotal);
        CollectionAssert.AreEqual(
            new[] { "A", "B", "C", "D", "E" },
            authority.PriorityArray.ToArray());
        Assert.AreEqual("Standard", authority.PriorityTable);
        Assert.AreEqual(10, authority.SumToTenTarget);
        Assert.AreEqual(CanonicalPrioritiesDigest, authority.RawPrioritiesXmlDigest);
        Assert.AreEqual(CanonicalMetatypesDigest, authority.RawMetatypesXmlDigest);
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            authority.SelectedCustomDataInputsDigest));
        Assert.AreEqual(1, authority.MaxNumberMaxAttributesCreate);
        Assert.AreEqual(5, authority.KarmaAttribute);
        Assert.IsFalse(authority.AlternateMetatypeAttributeKarma);
        Assert.IsFalse(authority.ReverseAttributePriorityOrder);
        Assert.HasCount(25, authority.Options);
        CharacterCreationPriorityOptionProjection attributesA = authority.Options.Single(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Attributes
            && option.Rank == "A");
        Assert.AreEqual(24, attributesA.BaseNormalAttributePoints);
        int[] expectedActive = [46, 36, 28, 22, 18];
        int[] expectedGroups = [10, 5, 2, 0, 0];
        for (int index = 0; index < authority.PriorityArray.Count; index++)
        {
            CharacterCreationPriorityOptionProjection skills = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Skills
                && option.Rank == authority.PriorityArray[index]);
            Assert.AreEqual(expectedActive[index], skills.BaseActiveSkillPoints);
            Assert.AreEqual(expectedGroups[index], skills.BaseSkillGroupPoints);
        }
        Assert.IsTrue(context.TryResolveCreationSkillsAuthority(
            out CharacterCreationSkillsAuthority skillsAuthority));
        Assert.IsTrue(
            skillsAuthority.IsAuthoritative,
            $"{string.Join(",", skillsAuthority.Blockers)}; active={skillsAuthority.ActiveSkills.Count}; "
            + $"knowledge={skillsAuthority.KnowledgeSkills.Count}; groups={skillsAuthority.SkillGroups.Count}; "
            + $"expression={skillsAuthority.KnowledgePointsExpression}");
        Assert.AreEqual(76, skillsAuthority.ActiveSkills.Count);
        Assert.AreEqual(195, skillsAuthority.KnowledgeSkills.Count);
        Assert.IsTrue(skillsAuthority.ActiveSkills.Concat(skillsAuthority.KnowledgeSkills).All(skill =>
            skill.Specializations.Select(option => option.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() == skill.Specializations.Count));
        CharacterCreationSkillCatalogEntry running = skillsAuthority.ActiveSkills.Single(skill =>
            string.Equals(skill.Name, "Running", StringComparison.Ordinal));
        CharacterCreationSkillCatalogEntry swimming = skillsAuthority.ActiveSkills.Single(skill =>
            string.Equals(skill.Name, "Swimming", StringComparison.Ordinal));
        CharacterCreationSkillCatalogEntry flight = skillsAuthority.ActiveSkills.Single(skill =>
            string.Equals(skill.Name, "Flight", StringComparison.Ordinal));
        Assert.IsTrue(running.RequiresGroundMovement);
        Assert.IsTrue(swimming.RequiresSwimMovement);
        Assert.IsTrue(flight.RequiresFlyMovement);
        Assert.AreEqual("({INTUnaug} + {LOGUnaug}) * 2", skillsAuthority.KnowledgePointsExpression);
        Assert.AreEqual(6, skillsAuthority.MaxActiveSkillRatingCreate);
        Assert.AreEqual(6, skillsAuthority.MaxKnowledgeSkillRatingCreate);
        Assert.AreEqual(6, skillsAuthority.MaxSkillGroupRatingCreate);
        Assert.AreEqual(1, skillsAuthority.BaseNativeLanguageLimit);
        Assert.IsTrue(CharacterCreationSkillsDigest.IsCanonical(skillsAuthority.AuthorityDigest));
        Assert.IsTrue(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(skillsAuthority));
        CharacterCreationPriorityOptionProjection heritageE = authority.Options.Single(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && option.Rank == "E");
        CharacterCreationPriorityHeritageOptionProjection human = heritageE.HeritageOptions.Single(option =>
            option.MetatypeName == "Human" && option.MetavariantName is null);
        Assert.IsTrue(human.IsEnabled, string.Join(",", human.Blockers));
        Assert.AreEqual(1, human.SpecialAttributePoints);
        Assert.IsFalse(human.HalvesNormalAttributePoints);
        Assert.IsTrue(human.Movement.Walk.Ground > 0m);
        Assert.IsTrue(human.Movement.Walk.Swim > 0m);
        Assert.AreEqual(0m, human.Movement.Walk.Fly);
        CharacterCreationPrerequisiteAuthority movementDrift = authority with
        {
            Options = authority.Options.Select(option => option.SourceId == heritageE.SourceId
                ? option with
                {
                    HeritageOptions = option.HeritageOptions.Select(heritage =>
                        heritage.SelectionId == human.SelectionId
                            ? heritage with
                            {
                                Movement = heritage.Movement with
                                {
                                    Walk = heritage.Movement.Walk with
                                    {
                                        Fly = heritage.Movement.Walk.Fly + 1m
                                    }
                                }
                            }
                            : heritage).ToArray()
                }
                : option).ToArray(),
            AuthorityDigest = string.Empty
        };
        Assert.AreNotEqual(
            authority.AuthorityDigest,
            CharacterCreationPrerequisiteAuthorityDigest.Compute(movementDrift));
        Assert.HasCount(13, human.Attributes);
        CharacterCreationPriorityHeritageOptionProjection oni = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && option.Rank == "C")
            .HeritageOptions.Single(option =>
                option.MetatypeName == "Ork" && option.MetavariantName == "Oni");
        Assert.AreEqual(CharacterCreationPriorityChildKinds.Metavariant, oni.Kind);
        Assert.AreEqual(-4, oni.KarmaCost);
        Assert.IsFalse(oni.IsEnabled);
        CollectionAssert.Contains(
            oni.Blockers.ToList(),
            CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported);
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            oni.PriorityChildNodeDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            oni.MetatypeSourceNodeDigest));
        CharacterCreationPriorityOptionProjection talentE = authority.Options.Single(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && option.Rank == "E");
        Assert.IsTrue(talentE.TalentOptions.Single(option => option.Value == "Mundane").IsEnabled);
        Assert.IsFalse(talentE.TalentOptions.Single(option => option.Value == "A.I.").IsEnabled);
        CharacterCreationPriorityHeritageOptionProjection halved = authority.Options
            .Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage)
            .SelectMany(option => option.HeritageOptions)
            .First(option => option.HalvesNormalAttributePoints);
        Assert.IsFalse(halved.IsEnabled);
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            halved.MetatypeSourceNodeDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
            authority.AuthorityDigest,
            CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)));

        ICharacterSourceDataContext duplicateRanks = CreateContext(
            coreRoot,
            $"<character><settings>{CanonicalStreetScumSettingsId}</settings></character>")!;
        Assert.IsTrue(duplicateRanks.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority streetScum));
        Assert.IsTrue(streetScum.IsAuthoritative, string.Join(",", streetScum.Blockers));
        CollectionAssert.AreEqual(
            new[] { "B", "C", "D", "E", "E" },
            streetScum.PriorityArray.ToArray());
        Assert.HasCount(20, streetScum.Options);
    }

    [TestMethod]
    [DataRow(SettingsId)]
    [DataRow(CanonicalSumToTenSettingsId)]
    public void Canonical_heritage_node_digests_remain_exact_for_every_rank_and_variant(string settingsId)
    {
        string root = FindCoreRoot();
        var context = CreateContext(root, $"<character><settings>{settingsId}</settings></character>")!;
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        XDocument priorities = LoadDigestSource(Path.Combine(root, "Chummer", "data", "priorities.xml"));
        XDocument metatypes = LoadDigestSource(Path.Combine(root, "Chummer", "data", "metatypes.xml"));
        AssertHeritageNodeDigests(authority, priorities, metatypes);
        var humans = authority.Options.SelectMany(option => option.HeritageOptions)
            .Where(option => option.MetatypeName == "Human" && option.MetavariantName is null).ToArray();
        Assert.HasCount(5, humans);
        Assert.AreEqual(1, humans.Select(option => option.MetatypeSourceNodeDigest).Distinct().Count());
        Assert.AreEqual(5, humans.Select(option => option.SelectionId).Distinct().Count());
        Assert.IsTrue(humans.Select(option => option.PriorityChildNodeDigest).Distinct().Count() > 1,
            "A shared source node must not replace rank-specific priority child bytes.");
    }

    [TestMethod]
    public void Heritage_node_digest_reuse_does_not_conflate_source_ids_or_survive_source_changes()
    {
        string root = CreateTempDirectory();
        try
        {
            CopyCanonicalDataFiles(root, "settings.xml", "priorities.xml", "metatypes.xml", "skills.xml");
            string path = Path.Combine(root, "data", "metatypes.xml");
            XDocument metatypes = LoadDigestSource(path);
            XElement human = metatypes.Root!.Element("metatypes")!.Elements("metatype")
                .Single(node => node.Element("name")?.Value == "Human");
            XElement elf = metatypes.Root.Element("metatypes")!.Elements("metatype")
                .Single(node => node.Element("name")?.Value == "Elf");
            elf.Element("id")!.Value = human.Element("id")!.Value;
            metatypes.Save(path);
            var originalContext = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(originalContext.TryResolveCreationPrerequisiteAuthority(out var original));
            var options = original.Options.SelectMany(option => option.HeritageOptions).ToArray();
            var originalHuman = options.First(option => option.MetatypeName == "Human" && option.MetavariantName is null);
            var originalElf = options.First(option => option.MetatypeName == "Elf" && option.MetavariantName is null);
            Assert.AreEqual(originalHuman.MetatypeSourceId, originalElf.MetatypeSourceId);
            Assert.AreNotEqual(originalHuman.MetatypeSourceNodeDigest, originalElf.MetatypeSourceNodeDigest,
                "Distinct source objects with identical IDs still bind different XML bytes.");
            AssertHeritageNodeDigests(original, LoadDigestSource(Path.Combine(root, "data", "priorities.xml")), LoadDigestSource(path));

            human.Add(new XComment("source identity remains; nested bytes change: ä ñ"));
            metatypes.Save(path);
            Assert.IsFalse(originalContext.TryResolveCreationPrerequisiteAuthority(out var stale)
                && stale.IsAuthoritative, "Memoization must not mask the existing source-drift rejection.");
            var freshContext = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(freshContext.TryResolveCreationPrerequisiteAuthority(out var changed));
            var freshOptions = changed.Options.SelectMany(option => option.HeritageOptions).ToArray();
            var changedHuman = freshOptions.First(option => option.MetatypeName == "Human" && option.MetavariantName is null);
            var changedElf = freshOptions.First(option => option.MetatypeName == "Elf" && option.MetavariantName is null);
            Assert.AreNotEqual(originalHuman.MetatypeSourceNodeDigest, changedHuman.MetatypeSourceNodeDigest);
            Assert.AreEqual(originalElf.MetatypeSourceNodeDigest, changedElf.MetatypeSourceNodeDigest);
            AssertHeritageNodeDigests(changed, LoadDigestSource(Path.Combine(root, "data", "priorities.xml")), LoadDigestSource(path));
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Heritage_node_digest_reuse_keeps_missing_and_ambiguous_sources_disabled(bool duplicate)
    {
        string root = CreateTempDirectory();
        try
        {
            CopyCanonicalDataFiles(root, "settings.xml", "priorities.xml", "metatypes.xml", "skills.xml");
            string path = Path.Combine(root, "data", "metatypes.xml");
            XDocument metatypes = LoadDigestSource(path);
            XElement human = metatypes.Root!.Element("metatypes")!.Elements("metatype")
                .Single(node => node.Element("name")?.Value == "Human");
            if (duplicate) human.AddAfterSelf(new XElement(human));
            else human.Remove();
            metatypes.Save(path);
            var context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var authority));
            var humans = authority.Options.SelectMany(option => option.HeritageOptions)
                .Where(option => option.MetatypeName == "Human" && option.MetavariantName is null).ToArray();
            Assert.HasCount(5, humans);
            Assert.IsTrue(humans.All(option => !option.IsEnabled && option.MetatypeSourceNodeDigest == string.Empty));
            AssertHeritageNodeDigests(authority, LoadDigestSource(Path.Combine(root, "data", "priorities.xml")), LoadDigestSource(path));
        }
        finally { DeleteTempDirectory(root); }
    }

    private static void AssertHeritageNodeDigests(CharacterCreationPrerequisiteAuthority authority,
        XDocument priorities, XDocument metatypes)
    {
        int observed = 0;
        foreach (var rank in authority.Options.Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage))
        {
            XElement row = priorities.Root!.Element("priorities")!.Elements("priority")
                .Single(node => node.Element("id")?.Value == rank.SourceId);
            XElement[] priorityChildren = row.Element("metatypes")!.Elements("metatype")
                .SelectMany(node => new[] { node }.Concat(node.Element("metavariants")?.Elements("metavariant") ?? []))
                .ToArray();
            Assert.AreEqual(priorityChildren.Length, rank.HeritageOptions.Count);
            for (int index = 0; index < priorityChildren.Length; index++)
            {
                var option = rank.HeritageOptions[index];
                Assert.AreEqual($"{rank.SourceId}:heritage:{index}", option.SelectionId);
                Assert.AreEqual(ExactNodeDigest(priorityChildren[index]), option.PriorityChildNodeDigest);
                XElement[] matches = metatypes.Root!.Element("metatypes")!.Elements("metatype")
                    .Where(node => node.Element("name")?.Value == option.MetatypeName).ToArray();
                if (matches.Length == 1 && option.MetavariantName is not null)
                    matches = matches[0].Element("metavariants")?.Elements("metavariant")
                        .Where(node => node.Element("name")?.Value == option.MetavariantName).ToArray() ?? [];
                Assert.AreEqual(matches.Length == 1 ? ExactNodeDigest(matches[0]) : string.Empty,
                    option.MetatypeSourceNodeDigest, option.SelectionId);
                observed++;
            }
        }
        Assert.AreEqual(853, observed, "Include disabled and metavariant rows, not only the selected Human.");
    }

    private static string ExactNodeDigest(XElement node) => "sha256:" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            node.ToString(SaveOptions.DisableFormatting))));

    private static XDocument LoadDigestSource(string path)
    {
        // Match the input whitespace contract, not the projector implementation:
        // the explicit reader retains whitespace that XDocument.Load(path) omits.
        using var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false
        });
        return XDocument.Load(reader, LoadOptions.None);
    }

    [TestMethod]
    [DataRow(SettingsId)]
    [DataRow(CanonicalSumToTenSettingsId)]
    public void Prerequisite_projection_cache_detaches_every_collection_on_miss_and_hit(string settingsId)
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(root, root, null));
        using var operation = resolver.CreateOperationScope();
        var context = operation.TryCreateContext($"<character><settings>{settingsId}</settings></character>");
        Assert.IsNotNull(context);
        var first = ReadPrerequisiteProjection(context);
        string expected = ProjectionJson(first);
        var firstGraph = CaptureProjectionCollections(first);
        AssertProjectionCollectionCoverage(firstGraph);
        Assert.AreEqual(1, resolver.LastSourceInputSnapshotDiagnostics!.PrerequisiteProjectionCount);
        int validationReads = resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount;

        // Capture the complete graph before nulling outer array elements. Otherwise
        // Options/RankWeights poisoning would silently skip nested grant coverage.
        PoisonProjectionCollections(firstGraph);
        Assert.AreNotEqual(expected, ProjectionJson(first));
        var second = ReadPrerequisiteProjection(context);
        Assert.AreEqual(expected, ProjectionJson(second), "The first return must not expose the private cache graph.");
        var secondGraph = CaptureProjectionCollections(second);
        AssertProjectionCollectionsDetached(firstGraph, secondGraph);
        Assert.AreEqual(1, resolver.LastSourceInputSnapshotDiagnostics.PrerequisiteProjectionCount);
        Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount > validationReads,
            "A projection hit must still perform live input validation.");
        validationReads = resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount;

        PoisonProjectionCollections(secondGraph);
        var third = ReadPrerequisiteProjection(context);
        Assert.AreEqual(expected, ProjectionJson(third), "A hit must not expose the private cache graph either.");
        AssertProjectionCollectionsDetached(secondGraph, CaptureProjectionCollections(third));
        Assert.AreEqual(1, resolver.LastSourceInputSnapshotDiagnostics.PrerequisiteProjectionCount);
        Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount > validationReads);
        AssertHeritageNodeDigests(third,
            LoadDigestSource(Path.Combine(root, "Chummer", "data", "priorities.xml")),
            LoadDigestSource(Path.Combine(root, "Chummer", "data", "metatypes.xml")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Prerequisite_projection_cache_parallel_calls_do_not_share_returned_arrays(bool warm)
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(root, root, null));
        using var operation = resolver.CreateOperationScope();
        var context = operation.TryCreateContext(CharacterXml());
        Assert.IsNotNull(context);
        if (warm) ReadPrerequisiteProjection(context);
        var calls = Enumerable.Range(0, 3).Select(_ => Task.Run(() => ReadPrerequisiteProjection(context))).ToArray();
        var results = await Task.WhenAll(calls); // Join every call before observing/mutating results.
        string expected = ProjectionJson(results[0]);
        var graphs = results.Select(CaptureProjectionCollections).ToArray();
        for (int index = 0; index < results.Length; index++)
        {
            Assert.AreEqual(expected, ProjectionJson(results[index]));
            AssertProjectionCollectionCoverage(graphs[index]);
            for (int other = index + 1; other < results.Length; other++)
                AssertProjectionCollectionsDetached(graphs[index], graphs[other]);
        }
        int builds = resolver.LastSourceInputSnapshotDiagnostics!.PrerequisiteProjectionCount;
        Assert.IsTrue(warm ? builds == 1 : builds >= 1 && builds <= calls.Length,
            "Only concurrent cold misses may perform duplicate equivalent projections.");
        int validationReads = resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount;
        PoisonProjectionCollections(graphs[0]);
        for (int index = 1; index < results.Length; index++)
            Assert.AreEqual(expected, ProjectionJson(results[index]));
        var after = ReadPrerequisiteProjection(context);
        Assert.AreEqual(expected, ProjectionJson(after));
        AssertProjectionCollectionsDetached(graphs[0], CaptureProjectionCollections(after));
        Assert.AreEqual(builds, resolver.LastSourceInputSnapshotDiagnostics.PrerequisiteProjectionCount);
        Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount > validationReads);
    }

    [TestMethod]
    [DataRow("settings.xml")]
    [DataRow("priorities.xml")]
    [DataRow("metatypes.xml")]
    [DataRow("skills.xml")]
    public void Prerequisite_projection_cache_cannot_revive_after_observed_source_drift(string fileName)
    {
        string root = CreateTempDirectory();
        try
        {
            CopyCanonicalDataFiles(root, "settings.xml", "priorities.xml", "metatypes.xml", "skills.xml");
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(root, root, null));
            using var operation = resolver.CreateOperationScope();
            var context = operation.TryCreateContext(CharacterXml());
            Assert.IsNotNull(context);
            string expected = ProjectionJson(ReadPrerequisiteProjection(context));
            Assert.AreEqual(1, resolver.LastSourceInputSnapshotDiagnostics!.PrerequisiteProjectionCount);
            string path = Path.Combine(root, "data", fileName);
            byte[] original = File.ReadAllBytes(path);
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            File.AppendAllText(path, "\n");
            Assert.IsFalse(context.TryResolveCreationPrerequisiteAuthority(out var drifted) && drifted.IsAuthoritative);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics.SourceDriftDetected);
            Assert.IsNull(operation.TryCreateContext(CharacterXml()), "The operation must retain its observed drift.");

            File.WriteAllBytes(path, original);
            File.SetLastWriteTimeUtc(path, timestamp);
            Assert.IsFalse(context.TryResolveCreationPrerequisiteAuthority(out var restored) && restored.IsAuthoritative);
            Assert.IsNull(operation.TryCreateContext(CharacterXml()), "Restoring bytes cannot revive the old operation.");
            using var freshOperation = resolver.CreateOperationScope();
            var fresh = freshOperation.TryCreateContext(CharacterXml());
            Assert.IsNotNull(fresh);
            Assert.AreNotSame(context, fresh);
            Assert.AreEqual(0, resolver.LastSourceInputSnapshotDiagnostics!.PrerequisiteProjectionCount,
                "A fresh action/context must not inherit the previous projection cache.");
            Assert.AreEqual(expected, ProjectionJson(ReadPrerequisiteProjection(fresh)));
            Assert.AreEqual(1, resolver.LastSourceInputSnapshotDiagnostics.PrerequisiteProjectionCount);
            Assert.IsFalse(resolver.LastSourceInputSnapshotDiagnostics.SourceDriftDetected);
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Prerequisite_projection_cache_does_not_store_non_authoritative_results()
    {
        string root = CreateTempDirectory();
        try
        {
            CopyCanonicalDataFiles(root, "settings.xml", "priorities.xml", "metatypes.xml", "skills.xml");
            string path = Path.Combine(root, "data", "priorities.xml");
            XDocument document = LoadDigestSource(path);
            document.Root!.Element("categories")!.Remove();
            document.Save(path);
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(root, root, null));
            var context = resolver.TryCreateContext(CharacterXml());
            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var first));
            Assert.IsFalse(first.IsAuthoritative);
            CollectionAssert.Contains(first.Blockers.ToArray(), CharacterCreationPrerequisiteBlockers.PriorityCategoriesInvalid);
            string expected = ProjectionJson(first);
            ((string[])first.Blockers)[0] = "caller-poison";
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var second));
            Assert.AreEqual(expected, ProjectionJson(second));
            Assert.AreEqual(2, resolver.LastSourceInputSnapshotDiagnostics!.PrerequisiteProjectionCount);
        }
        finally { DeleteTempDirectory(root); }
    }

    private static CharacterCreationPrerequisiteAuthority ReadPrerequisiteProjection(ICharacterSourceDataContext context)
    {
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.AreEqual(authority.AuthorityDigest, CharacterCreationPrerequisiteAuthorityDigest.Compute(authority));
        return authority;
    }

    private static string ProjectionJson(CharacterCreationPrerequisiteAuthority authority) =>
        System.Text.Json.JsonSerializer.Serialize(authority);

    private sealed record ProjectionCollection(string Kind, Array Values);

    private static Dictionary<string, ProjectionCollection> CaptureProjectionCollections(
        CharacterCreationPrerequisiteAuthority authority)
    {
        var result = new Dictionary<string, ProjectionCollection>(StringComparer.Ordinal);
        void Add<T>(string path, string kind, IReadOnlyList<T> values)
        {
            var array = values as T[];
            Assert.IsNotNull(array, $"Expected a detached array at {path}.");
            result.Add(path, new(kind, array));
        }
        Add("PriorityArray", "authority.PriorityArray", authority.PriorityArray);
        Add("RankWeights", "authority.RankWeights", authority.RankWeights);
        Add("Options", "authority.Options", authority.Options);
        Add("SourceAnchorIds", "authority.SourceAnchorIds", authority.SourceAnchorIds);
        Add("Blockers", "authority.Blockers", authority.Blockers);
        for (int rank = 0; rank < authority.RankWeights.Count; rank++)
            Add($"RankWeights[{rank}].SourceAnchorIds", "rank.SourceAnchorIds", authority.RankWeights[rank].SourceAnchorIds);
        for (int optionIndex = 0; optionIndex < authority.Options.Count; optionIndex++)
        {
            var option = authority.Options[optionIndex];
            string optionPath = $"Options[{optionIndex}]";
            Add(optionPath + ".SourceAnchorIds", "priority.SourceAnchorIds", option.SourceAnchorIds);
            Add(optionPath + ".HeritageOptions", "priority.HeritageOptions", option.HeritageOptions);
            Add(optionPath + ".TalentOptions", "priority.TalentOptions", option.TalentOptions);
            for (int index = 0; index < option.HeritageOptions.Count; index++)
            {
                var heritage = option.HeritageOptions[index];
                string path = $"{optionPath}.HeritageOptions[{index}]";
                Add(path + ".Attributes", "heritage.Attributes", heritage.Attributes);
                Add(path + ".Blockers", "heritage.Blockers", heritage.Blockers);
                Add(path + ".SourceAnchorIds", "heritage.SourceAnchorIds", heritage.SourceAnchorIds);
            }
            for (int index = 0; index < option.TalentOptions.Count; index++)
            {
                var talent = option.TalentOptions[index];
                string path = $"{optionPath}.TalentOptions[{index}]";
                Add(path + ".GrantedQualities", "talent.GrantedQualities", talent.GrantedQualities);
                Add(path + ".Blockers", "talent.Blockers", talent.Blockers);
                Add(path + ".SourceAnchorIds", "talent.SourceAnchorIds", talent.SourceAnchorIds);
                if (talent.ActiveSkillGrant is { } active)
                {
                    string grantPath = path + ".ActiveSkillGrant";
                    Add(grantPath + ".Options", "active.Options", active.Options);
                    Add(grantPath + ".Blockers", "active.Blockers", active.Blockers);
                    Add(grantPath + ".SourceAnchorIds", "active.SourceAnchorIds", active.SourceAnchorIds);
                    Add(grantPath + ".SpecificSkillChoiceNames", "active.SpecificSkillChoiceNames", active.SpecificSkillChoiceNames);
                    for (int choice = 0; choice < active.Options.Count; choice++)
                    {
                        string choicePath = $"{grantPath}.Options[{choice}]";
                        Add(choicePath + ".SourceAnchorIds", "activeChoice.SourceAnchorIds", active.Options[choice].SourceAnchorIds);
                        Add(choicePath + ".Blockers", "activeChoice.Blockers", active.Options[choice].Blockers);
                    }
                }
                if (talent.SkillGroupGrant is { } group)
                {
                    string grantPath = path + ".SkillGroupGrant";
                    Add(grantPath + ".Options", "group.Options", group.Options);
                    Add(grantPath + ".Blockers", "group.Blockers", group.Blockers);
                    Add(grantPath + ".SourceAnchorIds", "group.SourceAnchorIds", group.SourceAnchorIds);
                    Add(grantPath + ".RequestedGroupNames", "group.RequestedGroupNames", group.RequestedGroupNames);
                    for (int choice = 0; choice < group.Options.Count; choice++)
                    {
                        string choicePath = $"{grantPath}.Options[{choice}]";
                        Add(choicePath + ".MemberSkillSourceIds", "groupChoice.MemberSkillSourceIds", group.Options[choice].MemberSkillSourceIds);
                        Add(choicePath + ".SourceAnchorIds", "groupChoice.SourceAnchorIds", group.Options[choice].SourceAnchorIds);
                    }
                }
            }
        }
        return result;
    }

    private static void AssertProjectionCollectionCoverage(Dictionary<string, ProjectionCollection> graph)
    {
        string[] expectedKinds =
        [
            "authority.PriorityArray", "authority.RankWeights", "authority.Options", "authority.SourceAnchorIds", "authority.Blockers",
            "rank.SourceAnchorIds", "priority.SourceAnchorIds", "priority.HeritageOptions", "priority.TalentOptions",
            "heritage.Attributes", "heritage.Blockers", "heritage.SourceAnchorIds",
            "talent.GrantedQualities", "talent.Blockers", "talent.SourceAnchorIds",
            "active.Options", "active.Blockers", "active.SourceAnchorIds", "active.SpecificSkillChoiceNames",
            "activeChoice.SourceAnchorIds", "activeChoice.Blockers",
            "group.Options", "group.Blockers", "group.SourceAnchorIds", "group.RequestedGroupNames",
            "groupChoice.MemberSkillSourceIds", "groupChoice.SourceAnchorIds"
        ];
        CollectionAssert.AreEquivalent(expectedKinds, graph.Values.Select(item => item.Kind).Distinct().ToArray());
        Assert.IsTrue(graph.Values.Any(item => item.Values.Length == 0));
        Assert.IsTrue(graph.Values.Any(item => item.Kind == "active.Options" && item.Values.Length > 0));
        Assert.IsTrue(graph.Values.Any(item => item.Kind == "group.Options" && item.Values.Length > 0));
        Console.WriteLine($"prerequisite-projection-collection-coverage kinds={expectedKinds.Length} "
            + $"nonempty={graph.Values.Count(item => item.Values.Length > 0)} empty={graph.Values.Count(item => item.Values.Length == 0)}");
    }

    private static void AssertProjectionCollectionsDetached(Dictionary<string, ProjectionCollection> left,
        Dictionary<string, ProjectionCollection> right)
    {
        CollectionAssert.AreEquivalent(left.Keys.ToArray(), right.Keys.ToArray());
        foreach (var (path, item) in left)
        {
            Assert.AreEqual(item.Values.Length, right[path].Values.Length, path);
            // Array.Empty may be shared: zero-length arrays cannot be poisoned.
            if (item.Values.Length > 0) Assert.AreNotSame(item.Values, right[path].Values, path);
        }
    }

    private static void PoisonProjectionCollections(Dictionary<string, ProjectionCollection> graph)
    {
        int poisoned = 0;
        foreach (var item in graph.Values)
        {
            if (item.Values.Length == 0) continue;
            item.Values.SetValue(item.Values is string[] ? "caller-poison" : null, 0);
            poisoned++;
        }
        Assert.IsTrue(poisoned > 0);
    }

    [TestMethod]
    public void Creation_source_context_captures_and_parses_once_but_revalidates_content_bytes()
    {
        string coreRoot = FindCoreRoot();
        var overlays = new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null);
        var resolver = new FileSystemCharacterSourceDataResolver(overlays);
        ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationSourceProfile(out _));
        Assert.IsTrue(context.TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority metatypes));
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority prerequisite));
        Assert.IsTrue(context.TryResolveCreationQualitiesAuthority(
            out CharacterCreationQualitiesAuthority qualities));
        Assert.IsTrue(context.TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority magic));

        FileSystemCharacterSourceDataResolver.SourceInputSnapshotDiagnostics diagnostics =
            resolver.LastSourceInputSnapshotDiagnostics!;
        Assert.IsNotNull(diagnostics);
        Assert.IsTrue(metatypes.IsAuthoritative, string.Join(",", metatypes.Blockers));
        Assert.IsTrue(prerequisite.IsAuthoritative, string.Join(",", prerequisite.Blockers));
        Assert.IsTrue(qualities.IsAuthoritative, string.Join(",", qualities.Blockers));
        Assert.IsFalse(string.IsNullOrWhiteSpace(magic.Schema));
        Assert.IsTrue(diagnostics.PhysicalReadCount > 0);
        Assert.IsTrue(diagnostics.PhysicalXmlParseCount > 0);
        Assert.IsTrue(diagnostics.CacheHitCount > 0);
        // statx ctime is a timestamp, not a collision-free write generation.
        // Unchanged metadata must never exempt source bytes from validation.
        Assert.IsTrue(diagnostics.ValidationReadCount > 0);
        Assert.IsTrue(diagnostics.ValidationBytesRead > 0L);
        Assert.IsTrue(
            diagnostics.PhysicalReadsByPath.Values.All(count => count == 1),
            string.Join(",", diagnostics.PhysicalReadsByPath.Select(pair => $"{pair.Key}={pair.Value}")));
        Assert.IsTrue(
            diagnostics.PhysicalXmlParsesByPath.Values.All(count => count == 1),
            string.Join(",", diagnostics.PhysicalXmlParsesByPath.Select(pair => $"{pair.Key}={pair.Value}")));
        Assert.IsTrue(
            diagnostics.PhysicalReadsByPath.Keys.Any(path =>
                string.Equals(Path.GetFileName(path), "priorities.xml", StringComparison.Ordinal)));
        Assert.IsTrue(
            diagnostics.PhysicalReadsByPath.Keys.Any(path =>
                string.Equals(Path.GetFileName(path), "metatypes.xml", StringComparison.Ordinal)));
        Assert.IsTrue(
            diagnostics.PhysicalReadsByPath.Keys.Any(path =>
                string.Equals(Path.GetFileName(path), "qualities.xml", StringComparison.Ordinal)));
        Console.WriteLine(
            $"creation-source-input-snapshot reads={diagnostics.PhysicalReadCount} "
            + $"parses={diagnostics.PhysicalXmlParseCount} hits={diagnostics.CacheHitCount} "
            + $"validationReads={diagnostics.ValidationReadCount} "
            + $"directoryValidations={diagnostics.DirectoryValidationCount} "
            + $"elapsedMs={diagnostics.Elapsed.TotalMilliseconds:F3}");
    }

    [TestMethod]
    public void Operation_scope_reuses_exact_XML_but_keeps_actual_validation_and_full_source_queries()
    {
        string coreRoot = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
        using var scope = resolver.CreateOperationScope();
        string xml = CharacterXml();
        var first = scope.TryCreateContext(xml);
        Assert.IsNotNull(first);
        Assert.IsTrue(first.TryResolveCreationPrerequisiteAuthority(out var before));
        Assert.IsTrue(before.IsAuthoritative, string.Join(",", before.Blockers));
        Assert.IsTrue(first.TryResolveCreationMetatypeCatalog(out _));
        Assert.IsTrue(first.TryResolveCreationQualitiesAuthority(out _));
        Assert.IsTrue(first.TryResolveCreationMagicResonanceAuthority(out _));
        Assert.IsTrue(first.TryResolveCreationSkillsAuthority(out _));
        Assert.IsTrue(first.TryResolveCreationResourcesAuthority(out _));
        Assert.IsTrue(first.TryResolveCreationGearAuthority(out _));
        var captured = resolver.LastSourceInputSnapshotDiagnostics!;

        Assert.AreSame(first, scope.TryCreateContext(new string(xml.ToCharArray())));
        Assert.IsTrue(first.TryResolveCreationPrerequisiteAuthority(out var after));
        Assert.AreEqual(before.AuthorityDigest, after.AuthorityDigest);
        var reused = resolver.LastSourceInputSnapshotDiagnostics!;
        Assert.AreEqual(captured.PhysicalReadCount, reused.PhysicalReadCount);
        Assert.AreEqual(captured.PhysicalXmlParseCount, reused.PhysicalXmlParseCount);
        Assert.IsTrue(reused.PhysicalReadsByPath.Values.All(count => count == 1));
        Assert.IsTrue(reused.PhysicalXmlParsesByPath.Values.All(count => count == 1));
        Assert.IsTrue(reused.ValidationReadCount > captured.ValidationReadCount);
        Assert.IsTrue(reused.ValidationBytesRead > captured.ValidationBytesRead);

        // No normalization/digest-only matching, and no source object retained
        // across a new operation even when the character bytes are identical.
        var changed = scope.TryCreateContext(CharacterXml("<created>True</created>"));
        Assert.IsNotNull(changed);
        Assert.AreNotSame(first, changed);
        var whitespaceOnly = scope.TryCreateContext(xml + "\n");
        Assert.IsNotNull(whitespaceOnly);
        Assert.AreNotSame(first, whitespaceOnly, "Even semantically equal XML has distinct exact-byte provenance.");
        Assert.AreSame(first, scope.TryCreateContext(xml));
        using var next = resolver.CreateOperationScope();
        var separate = next.TryCreateContext(xml);
        Assert.IsNotNull(separate);
        Assert.AreNotSame(first, separate);
        Assert.IsTrue(separate.TryResolveCreationPrerequisiteAuthority(out var newAuthority));
        Assert.AreEqual(before.AuthorityDigest, newAuthority.AuthorityDigest);
        scope.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => scope.TryCreateContext(xml));
        Assert.AreSame(separate, next.TryCreateContext(xml));
    }

    [TestMethod]
    [DataRow("restored-time")]
    [DataRow("weak-identity")]
    [DataRow("atomic-replacement")]
    public void Operation_scope_rejects_observed_byte_drift_and_does_not_revive_on_ABA(string change)
    {
        string root = CreateOperationSourceFixture();
        try
        {
            string path = Path.Combine(root, "data", "priorities.xml");
            string original = File.ReadAllText(path);
            DateTime originalTime = File.GetLastWriteTimeUtc(path);
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(root, root, null), null,
                useStrongChangeIdentity: change != "weak-identity");
            using var scope = resolver.CreateOperationScope();
            var context = scope.TryCreateContext(CharacterXml());
            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var before));
            Assert.IsTrue(before.IsAuthoritative, string.Join(",", before.Blockers));
            var captured = resolver.LastSourceInputSnapshotDiagnostics!;
            string replacement = original.Replace("<attributes>24</attributes>",
                "<attributes>23</attributes>", StringComparison.Ordinal);
            Assert.AreNotEqual(original, replacement);
            Assert.AreEqual(original.Length, replacement.Length);
            string target = change == "atomic-replacement" ? Path.Combine(root, "replacement.xml") : path;
            File.WriteAllText(target, replacement);
            File.SetLastWriteTimeUtc(target, originalTime);
            if (target != path) File.Move(target, path, overwrite: true);

            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            Assert.AreEqual(captured.PhysicalReadCount,
                resolver.LastSourceInputSnapshotDiagnostics.PhysicalReadCount);
            if (change == "weak-identity")
                Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount
                    > captured.ValidationReadCount);
            Assert.IsTrue(!context.TryResolveCreationPrerequisiteAuthority(out var stale) || !stale.IsAuthoritative);

            using (var next = resolver.CreateOperationScope())
            {
                var fresh = next.TryCreateContext(CharacterXml());
                Assert.IsNotNull(fresh);
                Assert.IsTrue(fresh.TryResolveCreationPrerequisiteAuthority(out var changed));
                Assert.IsTrue(changed.IsAuthoritative, string.Join(",", changed.Blockers));
                Assert.AreNotEqual(before.AuthorityDigest, changed.AuthorityDigest);
            }
            File.WriteAllText(path, original);
            File.SetLastWriteTimeUtc(path, originalTime);
            Assert.IsNull(scope.TryCreateContext(CharacterXml()), "Observed drift is sticky within the operation.");
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Operation_scope_rejects_new_catalog_root_and_poisoned_entry_survives_catalog_ABA()
    {
        string root = CreateOperationSourceFixture();
        string overlayRoot = CreateTempDirectory();
        try
        {
            string data = Path.Combine(overlayRoot, "data");
            Directory.CreateDirectory(data);
            string original = File.ReadAllText(Path.Combine(root, "data", "priorities.xml"));
            File.WriteAllText(Path.Combine(data, "priorities.xml"), original.Replace(
                "<attributes>24</attributes>", "<attributes>23</attributes>", StringComparison.Ordinal));
            var packs = new List<ContentOverlayPack>();
            var resolver = new FileSystemCharacterSourceDataResolver(new MutableContentOverlayCatalogService(
                Path.Combine(root, "data"), Path.Combine(root, "lang"), packs));
            using var scope = resolver.CreateOperationScope();
            var first = scope.TryCreateContext(CharacterXml());
            Assert.IsNotNull(first);
            Assert.IsTrue(first.TryResolveCreationPrerequisiteAuthority(out var before));
            Assert.IsTrue(before.IsAuthoritative);
            packs.Add(new("late", "Late replacement", overlayRoot, data,
                Path.Combine(overlayRoot, "lang"), 100, true, ContentOverlayModes.ReplaceFile, string.Empty));

            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            CollectionAssert.Contains(resolver.LastSourceInputSnapshotDiagnostics!.DriftedPaths.ToList(),
                "content-overlay-catalog");
            Assert.IsFalse(first.TryResolveCreationSourceProfile(out _));
            using (var next = resolver.CreateOperationScope())
            {
                var fresh = next.TryCreateContext(CharacterXml());
                Assert.IsNotNull(fresh);
                Assert.IsTrue(fresh.TryResolveCreationPrerequisiteAuthority(out var after));
                Assert.IsTrue(after.IsAuthoritative, string.Join(",", after.Blockers));
                Assert.AreNotEqual(before.AuthorityDigest, after.AuthorityDigest);
            }
            packs.Clear();
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
        }
        finally { DeleteTempDirectory(overlayRoot); DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow("order")]
    [DataRow("enabled")]
    [DataRow("metadata")]
    [DataRow("root-path")]
    public void Operation_scope_rejects_catalog_only_changes_even_with_identical_source_bytes(string change)
    {
        string root = CreateOperationSourceFixture();
        string overlayRoot = CreateTempDirectory();
        try
        {
            string data = Path.Combine(overlayRoot, "data");
            Directory.CreateDirectory(data);
            var firstPack = new ContentOverlayPack("first", "First", overlayRoot, data,
                Path.Combine(overlayRoot, "lang"), 1, true, ContentOverlayModes.ReplaceFile, "Original");
            var secondPack = firstPack with { Id = "second", Priority = 2 };
            var packs = new List<ContentOverlayPack> { firstPack, secondPack };
            var resolver = new FileSystemCharacterSourceDataResolver(new MutableContentOverlayCatalogService(
                Path.Combine(root, "data"), Path.Combine(root, "lang"), packs));
            using var scope = resolver.CreateOperationScope();
            var context = scope.TryCreateContext(CharacterXml());
            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var before));
            Assert.IsTrue(before.IsAuthoritative);
            int captures = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            if (change == "order") packs.Reverse();
            else packs[0] = change switch
            {
                "enabled" => firstPack with { Enabled = false },
                "metadata" => firstPack with { Description = "Changed" },
                "root-path" => firstPack with { RootPath = Path.Combine(overlayRoot, "new-root") },
                _ => throw new AssertFailedException("Unknown mutation.")
            };
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            Assert.AreEqual(captures, resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            CollectionAssert.Contains(resolver.LastSourceInputSnapshotDiagnostics.DriftedPaths.ToList(),
                "content-overlay-catalog");
            packs.Clear();
            packs.Add(firstPack);
            packs.Add(secondPack);
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            Assert.IsFalse(context.TryResolveCreationSourceProfile(out _));
        }
        finally { DeleteTempDirectory(overlayRoot); DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Operation_scope_rejects_same_byte_symlink_retarget()
    {
        if (OperatingSystem.IsWindows()) return; // Same convention as the existing source symlink tests.
        string root = CreateOperationSourceFixture();
        try
        {
            string path = Path.Combine(root, "data", "priorities.xml");
            string firstTarget = Path.Combine(root, "first.xml");
            string secondTarget = Path.Combine(root, "second.xml");
            File.Move(path, firstTarget);
            File.Copy(firstTarget, secondTarget);
            File.SetLastWriteTimeUtc(secondTarget, File.GetLastWriteTimeUtc(firstTarget));
            CollectionAssert.AreEqual(File.ReadAllBytes(firstTarget), File.ReadAllBytes(secondTarget));
            File.CreateSymbolicLink(path, firstTarget);
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(root, root, null));
            using var scope = resolver.CreateOperationScope();
            var context = scope.TryCreateContext(CharacterXml());
            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var before));
            Assert.IsTrue(before.IsAuthoritative);
            File.Delete(path);
            File.CreateSymbolicLink(path, secondTarget);
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            CollectionAssert.Contains(resolver.LastSourceInputSnapshotDiagnostics!.DriftedPaths.ToList(), path);
            File.Delete(path);
            File.CreateSymbolicLink(path, firstTarget);
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            using var next = resolver.CreateOperationScope();
            var fresh = next.TryCreateContext(CharacterXml());
            Assert.IsNotNull(fresh);
            Assert.IsTrue(fresh.TryResolveCreationPrerequisiteAuthority(out var after));
            Assert.IsTrue(after.IsAuthoritative);
            Assert.AreEqual(before.AuthorityDigest, after.AuthorityDigest);
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Operation_scope_rejects_new_overlay_membership_and_previously_absent_file(bool replaceFile)
    {
        string root = CreateOperationSourceFixture();
        string overlayRoot = CreateTempDirectory();
        try
        {
            string data = Path.Combine(overlayRoot, "data");
            Directory.CreateDirectory(data);
            var packs = new[] { new ContentOverlayPack("pack", "Pack", overlayRoot, data,
                Path.Combine(overlayRoot, "lang"), 1, true,
                replaceFile ? ContentOverlayModes.ReplaceFile : ContentOverlayModes.MergeCatalog, string.Empty) };
            var resolver = new FileSystemCharacterSourceDataResolver(new MutableContentOverlayCatalogService(
                Path.Combine(root, "data"), Path.Combine(root, "lang"), packs));
            using var scope = resolver.CreateOperationScope();
            var first = scope.TryCreateContext(CharacterXml());
            Assert.IsNotNull(first);
            Assert.IsTrue(first.TryResolveCreationPrerequisiteAuthority(out var before));
            Assert.IsTrue(before.IsAuthoritative);
            string added = Path.Combine(data, replaceFile ? "priorities.xml" : "priorities.fragment.xml");
            File.WriteAllText(added, replaceFile
                ? File.ReadAllText(Path.Combine(root, "data", "priorities.xml"))
                : "<chummer><priorities /></chummer>");
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            Assert.IsFalse(resolver.LastSourceInputSnapshotDiagnostics.PhysicalReadsByPath.ContainsKey(added));
            File.Delete(added);
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
        }
        finally { DeleteTempDirectory(overlayRoot); DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Operation_scope_rejects_new_selected_custom_directory_membership()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customId = "4b3a4c48-d2af-4e46-9d27-9f06eab83c0c";
            WriteBaseContent(root,
                $"<customdatadirectoryname><directoryname>{customId}&gt;1.0</directoryname>"
                + "<order>0</order><enabled>True</enabled></customdatadirectoryname>",
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable><sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string v2 = Path.Combine(root, "customdata", "Rules v2");
            Directory.CreateDirectory(v2);
            File.WriteAllText(Path.Combine(v2, "manifest.xml"),
                $"<manifest><guid>{customId}</guid><version>2.0.0</version></manifest>");
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(root, root, null));
            using var scope = resolver.CreateOperationScope();
            string xml = CharacterXml("<customdatadirectorynames><directoryname>Rules v2</directoryname>"
                + "</customdatadirectorynames>");
            var first = scope.TryCreateContext(xml);
            Assert.IsNotNull(first);
            Assert.IsTrue(first.TryResolveCreationSourceProfile(out _));
            string v3 = Path.Combine(root, "customdata", "Rules v3");
            Directory.CreateDirectory(v3);
            File.WriteAllText(Path.Combine(v3, "manifest.xml"),
                $"<manifest><guid>{customId}</guid><version>3.0.0</version></manifest>");
            Assert.IsNull(scope.TryCreateContext(xml));
            Assert.IsFalse(first.TryResolveCreationSourceProfile(out _));
            Directory.Delete(v3, recursive: true);
            Assert.IsNull(scope.TryCreateContext(xml));
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public void Operation_scope_does_not_memoize_initial_null_or_catalog_exception()
    {
        string root = CreateTempDirectory();
        try
        {
            var catalogs = new OperationCatalog(new FileSystemContentOverlayCatalogService(root, root, null));
            var resolver = new FileSystemCharacterSourceDataResolver(catalogs);
            using var scope = resolver.CreateOperationScope();
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            WriteOperationSourceFixture(root);
            catalogs.ThrowNext = true;
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            var first = scope.TryCreateContext(CharacterXml());
            Assert.IsNotNull(first);
            Assert.IsTrue(first.TryResolveCreationPrerequisiteAuthority(out var authority));
            Assert.IsTrue(authority.IsAuthoritative);
            // Transient catalog unavailability is not observed catalog drift.
            // A retry must acquire a fresh catalog and validate all live bytes.
            catalogs.ThrowNext = true;
            Assert.IsNull(scope.TryCreateContext(CharacterXml()));
            Assert.AreSame(first, scope.TryCreateContext(CharacterXml()));
            Assert.AreEqual(5, catalogs.Calls);
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    public async Task Operation_scopes_do_not_share_contexts_between_parallel_operations()
    {
        string root = CreateOperationSourceFixture();
        try
        {
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(root, root, null));
            using var first = resolver.CreateOperationScope();
            using var second = resolver.CreateOperationScope();
            var contexts = await Task.WhenAll(Task.Run(() => first.TryCreateContext(CharacterXml())),
                Task.Run(() => second.TryCreateContext(CharacterXml())));
            Assert.IsNotNull(contexts[0]);
            Assert.IsNotNull(contexts[1]);
            Assert.AreNotSame(contexts[0], contexts[1]);
            Assert.IsTrue(contexts[0]!.TryResolveCreationPrerequisiteAuthority(out var a));
            Assert.IsTrue(contexts[1]!.TryResolveCreationPrerequisiteAuthority(out var b));
            Assert.IsTrue(a.IsAuthoritative);
            Assert.AreEqual(a.AuthorityDigest, b.AuthorityDigest);
            Assert.AreSame(contexts[0], first.TryCreateContext(CharacterXml()));
            Assert.AreSame(contexts[1], second.TryCreateContext(CharacterXml()));
        }
        finally { DeleteTempDirectory(root); }
    }

    [TestMethod]
    [DataRow("catalog")]
    [DataRow("source-bytes")]
    public void Operation_scope_reentrant_disposal_cannot_return_or_restore_a_context(string boundary)
    {
        string root = CreateOperationSourceFixture();
        ICharacterSourceDataResolverOperationScope? scope = null;
        try
        {
            int disposals = 0;
            void DisposeFromCallback()
            {
                if (disposals != 0) return;
                disposals++;
                Assert.IsNotNull(scope);
                scope.Dispose();
            }
            var catalogs = new OperationCatalog(new FileSystemContentOverlayCatalogService(root, root, null));
            if (boundary == "catalog") catalogs.OnGetCatalog = DisposeFromCallback;
            var resolver = new FileSystemCharacterSourceDataResolver(catalogs,
                _ => { if (boundary == "source-bytes") DisposeFromCallback(); });
            scope = resolver.CreateOperationScope();
            Assert.ThrowsExactly<ObjectDisposedException>(() => scope.TryCreateContext(CharacterXml()));
            Assert.AreEqual(1, disposals, "The intended reentrant callback must execute.");
            int catalogCalls = catalogs.Calls;
            Assert.ThrowsExactly<ObjectDisposedException>(() => scope.TryCreateContext(CharacterXml()));
            Assert.AreEqual(catalogCalls, catalogs.Calls, "Closed scopes reject before fresh source access.");
            Assert.AreEqual(1, disposals);
        }
        finally { scope?.Dispose(); DeleteTempDirectory(root); }
    }

    private static string CreateOperationSourceFixture()
    {
        string root = CreateTempDirectory();
        WriteOperationSourceFixture(root);
        return root;
    }

    private static void WriteOperationSourceFixture(string root)
    {
        WriteBaseContent(root, string.Empty,
            "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
            + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable><sumtoten>10</sumtoten>");
        WritePriorityFixture(root);
    }

    private sealed class OperationCatalog(IContentOverlayCatalogService inner) : IContentOverlayCatalogService
    {
        public bool ThrowNext { get; set; }
        public Action? OnGetCatalog { get; set; }
        public int Calls { get; private set; }
        public ContentOverlayCatalog GetCatalog()
        {
            Calls++;
            OnGetCatalog?.Invoke();
            if (ThrowNext)
            {
                ThrowNext = false;
                throw new IOException("Injected catalog unavailability.");
            }
            return inner.GetCatalog();
        }
        public IReadOnlyList<string> GetDataDirectories() => inner.GetDataDirectories();
        public IReadOnlyList<string> GetLanguageDirectories() => inner.GetLanguageDirectories();
        public string ResolveDataFile(string fileName) => inner.ResolveDataFile(fileName);
    }

    [TestMethod]
    public void Creation_source_context_freezes_cached_bytes_and_new_context_observes_drift()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext firstContext = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(firstContext.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority beforeDrift));
            FileSystemCharacterSourceDataResolver.SourceInputSnapshotDiagnostics firstDiagnostics =
                resolver.LastSourceInputSnapshotDiagnostics!;
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            File.WriteAllText(
                prioritiesPath,
                File.ReadAllText(prioritiesPath).Replace(
                    "<attributes>24</attributes>",
                    "<attributes>23</attributes>",
                    StringComparison.Ordinal));

            Assert.IsTrue(firstContext.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority frozen));
            Assert.IsFalse(frozen.IsAuthoritative);
            CollectionAssert.Contains(
                frozen.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            Assert.AreEqual(
                beforeDrift.EffectivePrioritiesInputsDigest,
                frozen.EffectivePrioritiesInputsDigest);
            Assert.AreEqual(
                firstDiagnostics.PhysicalReadCount,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            CollectionAssert.Contains(
                resolver.LastSourceInputSnapshotDiagnostics!.DriftedPaths.ToList(),
                prioritiesPath);
            Assert.IsTrue(
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadsByPath.Values
                    .All(count => count == 1));

            ICharacterSourceDataContext secondContext = resolver.TryCreateContext(CharacterXml())!;
            Assert.IsTrue(secondContext.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority afterDrift));
            Assert.AreNotEqual(beforeDrift.AuthorityDigest, afterDrift.AuthorityDigest);
            Assert.AreNotEqual(
                beforeDrift.EffectivePrioritiesInputsDigest,
                afterDrift.EffectivePrioritiesInputsDigest);
            Assert.IsTrue(
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadsByPath.Values
                    .All(count => count == 1));
            Assert.IsTrue(
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalXmlParsesByPath.Values
                    .All(count => count == 1));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_rejects_same_length_bytes_with_restored_write_time()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            int readsBeforeDrift = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            DateTime capturedWriteTime = File.GetLastWriteTimeUtc(prioritiesPath);
            string original = File.ReadAllText(prioritiesPath);
            string tampered = original.Replace(
                "<attributes>24</attributes>",
                "<attributes>23</attributes>",
                StringComparison.Ordinal);
            Assert.AreEqual(original.Length, tampered.Length);
            File.WriteAllText(prioritiesPath, tampered);
            File.SetLastWriteTimeUtc(prioritiesPath, capturedWriteTime);

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));

            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            Assert.AreEqual(
                readsBeforeDrift,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            CollectionAssert.Contains(
                resolver.LastSourceInputSnapshotDiagnostics!.DriftedPaths.ToList(),
                prioritiesPath);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_fallback_rehash_rejects_restored_metadata_bytes()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(
                overlays,
                afterSourceBytesRead: null,
                useStrongChangeIdentity: false);
            ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            int captureReads = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            int validationReads = resolver.LastSourceInputSnapshotDiagnostics!.ValidationReadCount;
            DateTime capturedWriteTime = File.GetLastWriteTimeUtc(prioritiesPath);
            string original = File.ReadAllText(prioritiesPath);
            File.WriteAllText(
                prioritiesPath,
                original.Replace(
                    "<attributes>24</attributes>",
                    "<attributes>23</attributes>",
                    StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(prioritiesPath, capturedWriteTime);

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));

            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            Assert.AreEqual(
                captureReads,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.IsTrue(
                resolver.LastSourceInputSnapshotDiagnostics!.ValidationReadCount > validationReads);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.ValidationBytesRead > 0L);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_rejects_atomic_same_metadata_replacement()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            int readsBeforeDrift = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            DateTime capturedWriteTime = File.GetLastWriteTimeUtc(prioritiesPath);
            FileAttributes capturedAttributes = File.GetAttributes(prioritiesPath);
            string original = File.ReadAllText(prioritiesPath);
            string replacementPath = Path.Combine(root, "priorities-replacement.xml");
            File.WriteAllText(
                replacementPath,
                original.Replace(
                    "<attributes>24</attributes>",
                    "<attributes>23</attributes>",
                    StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(replacementPath, capturedWriteTime);
            File.SetAttributes(replacementPath, capturedAttributes);
            File.Move(replacementPath, prioritiesPath, overwrite: true);

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));

            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            Assert.AreEqual(
                readsBeforeDrift,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            CollectionAssert.Contains(
                resolver.LastSourceInputSnapshotDiagnostics!.DriftedPaths.ToList(),
                prioritiesPath);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_rejects_new_overlay_fragment_without_reading_it()
    {
        string root = CreateTempDirectory();
        string amendsRoot = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            WriteOverlay(amendsRoot, "authorized-pack", priority: 10, deviceRating: 4);
            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            int readsBeforeDrift = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            string fragmentPath = Path.Combine(
                amendsRoot,
                "authorized-pack",
                "data",
                "priorities.fragment.xml");
            File.WriteAllText(fragmentPath, "<chummer><priorities /></chummer>");

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));

            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            Assert.AreEqual(
                readsBeforeDrift,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.DirectoryValidationCount > 0);
            Assert.IsFalse(
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadsByPath.ContainsKey(fragmentPath));
        }
        finally
        {
            DeleteTempDirectory(amendsRoot);
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_rejects_new_custom_amendment_without_reading_it()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customSetting =
                "<customdatadirectoryname><directoryname>Authorized Custom</directoryname>"
                + "<order>0</order><enabled>True</enabled></customdatadirectoryname>";
            WriteBaseContent(
                root,
                customSetting,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string customRoot = Path.Combine(root, "customdata", "Authorized Custom");
            Directory.CreateDirectory(customRoot);
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(
                CharacterXml(
                    "<customdatadirectorynames><directoryname>Authorized Custom</directoryname>"
                    + "</customdatadirectorynames>"))!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            int readsBeforeDrift = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            string amendmentPath = Path.Combine(customRoot, "amend_priorities.xml");
            File.WriteAllText(
                amendmentPath,
                "<chummer><priortysumtotenvalues><A>5</A></priortysumtotenvalues></chummer>");

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));

            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            Assert.AreEqual(
                readsBeforeDrift,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.DirectoryValidationCount > 0);
            Assert.IsFalse(
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadsByPath.ContainsKey(amendmentPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_fails_closed_on_mid_capture_same_metadata_mutation()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            bool mutated = false;
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(
                overlays,
                path =>
                {
                    if (mutated
                        || !string.Equals(
                            Path.GetFileName(path),
                            "priorities.xml",
                            StringComparison.Ordinal))
                    {
                        return;
                    }

                    mutated = true;
                    DateTime capturedWriteTime = File.GetLastWriteTimeUtc(path);
                    string original = File.ReadAllText(path);
                    string tampered = original.Replace(
                        "<attributes>24</attributes>",
                        "<attributes>23</attributes>",
                        StringComparison.Ordinal);
                    Assert.AreEqual(original.Length, tampered.Length);
                    File.WriteAllText(path, tampered);
                    File.SetLastWriteTimeUtc(path, capturedWriteTime);
                });

            ICharacterSourceDataContext? context = resolver.TryCreateContext(CharacterXml());

            Assert.IsTrue(mutated);
            if (context is not null)
            {
                bool resolved = context.TryResolveCreationPrerequisiteAuthority(
                    out CharacterCreationPrerequisiteAuthority authority);
                Assert.IsTrue(!resolved || !authority.IsAuthoritative);
            }
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_preserves_byte_bound_quality_digest_but_rejects_unrelated_source_drift()
    {
        string root = CreateTempDirectory();
        try
        {
            CopyCanonicalDataFiles(
                root,
                "settings.xml",
                "priorities.xml",
                "metatypes.xml",
                "skills.xml",
                "qualities.xml");
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(context.TryResolveCreationQualitiesAuthority(
                out CharacterCreationQualitiesAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            DateTime capturedWriteTime = File.GetLastWriteTimeUtc(prioritiesPath);
            string original = File.ReadAllText(prioritiesPath);
            string tampered = original.Replace(
                "<attributes>24</attributes>",
                "<attributes>23</attributes>",
                StringComparison.Ordinal);
            Assert.AreEqual(original.Length, tampered.Length);
            Assert.AreNotEqual(original, tampered);
            File.WriteAllText(prioritiesPath, tampered);
            File.SetLastWriteTimeUtc(prioritiesPath, capturedWriteTime);

            Assert.IsTrue(context.TryResolveCreationQualitiesAuthority(
                out CharacterCreationQualitiesAuthority drifted));

            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationQualitiesBlockers.AuthorityUnavailable);
            Assert.AreEqual(initial.SourceDigest, drifted.SourceDigest);
            Assert.IsTrue(CharacterCreationQualitiesRules.DigestsEqual(
                drifted.SourceDigest,
                initial.SourceDigest));

            var freshResolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext freshContext = freshResolver.TryCreateContext(CharacterXml())!;
            Assert.IsTrue(freshContext.TryResolveCreationQualitiesAuthority(
                out CharacterCreationQualitiesAuthority fresh));
            Assert.IsTrue(fresh.IsAuthoritative, string.Join(",", fresh.Blockers));
            Assert.AreEqual(fresh.SourceDigest, drifted.SourceDigest);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_rejects_skill_source_drift_for_creation_and_direct_skill_lookups()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            WriteSkillsAuthorityFixture(root);
            string skillsPath = Path.Combine(root, "data", "skills.xml");
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(context.TryResolveCreationSkillsAuthority(
                out CharacterCreationSkillsAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            Assert.IsTrue(context.TryResolveActiveSkillSource(
                "30000000-0000-0000-0000-000000000001",
                out _));
            int captureReads = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            int captureParses = resolver.LastSourceInputSnapshotDiagnostics.PhysicalXmlParseCount;
            int validationReads = resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount;
            Assert.IsTrue(context.TryResolveCreationSkillsAuthority(out var unchanged));
            Assert.IsTrue(unchanged.IsAuthoritative, string.Join(",", unchanged.Blockers));
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics.ValidationReadCount > validationReads,
                "Even unchanged native metadata needs byte validation; coarse ctime can repeat between writes.");
            DateTime capturedWriteTime = File.GetLastWriteTimeUtc(skillsPath);
            string original = File.ReadAllText(skillsPath);
            string tampered = original.Replace("<name>Running</name>", "<name>Runnong</name>", StringComparison.Ordinal);
            Assert.AreEqual(original.Length, tampered.Length);
            Assert.AreNotEqual(original, tampered);
            File.WriteAllText(skillsPath, tampered);
            File.SetLastWriteTimeUtc(skillsPath, capturedWriteTime);

            Assert.IsTrue(context.TryResolveCreationSkillsAuthority(
                out CharacterCreationSkillsAuthority drifted));
            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationSkillsBlockers.SkillsSourceDrift);
            Assert.IsFalse(context.TryResolveActiveSkillSource(
                "30000000-0000-0000-0000-000000000001",
                out _));
            Assert.AreEqual(
                captureReads,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.AreEqual(
                captureParses,
                resolver.LastSourceInputSnapshotDiagnostics.PhysicalXmlParseCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_rejects_gear_source_drift_without_reopening_or_reparsing()
    {
        string root = CreateTempDirectory();
        try
        {
            CopyCanonicalDataFiles(
                root,
                "settings.xml",
                "priorities.xml",
                "metatypes.xml",
                "skills.xml",
                "gear.xml");
            string gearPath = Path.Combine(root, "data", "gear.xml");
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(context.TryResolveCreationGearAuthority(
                out CharacterCreationGearAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            int captureReads = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            int captureParses = resolver.LastSourceInputSnapshotDiagnostics.PhysicalXmlParseCount;
            RewriteFirstElementValueSameLength(gearPath, "name");

            Assert.IsTrue(context.TryResolveCreationGearAuthority(
                out CharacterCreationGearAuthority drifted));
            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationGearBlockers.AuthorityUnavailable);
            Assert.AreEqual(initial.SourceDigest, drifted.SourceDigest);
            Assert.AreEqual(
                captureReads,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.AreEqual(
                captureParses,
                resolver.LastSourceInputSnapshotDiagnostics.PhysicalXmlParseCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_freezes_mutable_overlay_catalog_and_new_context_uses_replacement_graph()
    {
        string root = CreateTempDirectory();
        string overlayRoot = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string overlayData = Path.Combine(overlayRoot, "data");
            Directory.CreateDirectory(overlayData);
            string basePriorities = File.ReadAllText(Path.Combine(root, "data", "priorities.xml"));
            string replacementPriorities = basePriorities.Replace(
                "<attributes>24</attributes>",
                "<attributes>23</attributes>",
                StringComparison.Ordinal);
            Assert.AreNotEqual(basePriorities, replacementPriorities);
            File.WriteAllText(Path.Combine(overlayData, "priorities.xml"), replacementPriorities);
            var mutablePacks = new List<ContentOverlayPack>
            {
                new(
                    "replace-priorities",
                    "Replace priorities",
                    overlayRoot,
                    overlayData,
                    Path.Combine(overlayRoot, "lang"),
                    100,
                    true,
                    ContentOverlayModes.ReplaceFile,
                    string.Empty)
            };
            var overlays = new MutableContentOverlayCatalogService(
                Path.Combine(root, "data"),
                Path.Combine(root, "lang"),
                mutablePacks);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext frozenContext = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(frozenContext.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority replacement));
            Assert.IsTrue(replacement.IsAuthoritative, string.Join(",", replacement.Blockers));
            mutablePacks.Clear();
            Assert.IsTrue(frozenContext.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority stillReplacement));
            Assert.IsTrue(stillReplacement.IsAuthoritative, string.Join(",", stillReplacement.Blockers));
            Assert.AreEqual(replacement.AuthorityDigest, stillReplacement.AuthorityDigest);
            Assert.AreEqual(
                replacement.EffectivePrioritiesInputsDigest,
                stillReplacement.EffectivePrioritiesInputsDigest);

            ICharacterSourceDataContext baseContext = resolver.TryCreateContext(CharacterXml())!;
            Assert.IsTrue(baseContext.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority baseAuthority));
            Assert.IsTrue(baseAuthority.IsAuthoritative, string.Join(",", baseAuthority.Blockers));
            Assert.AreNotEqual(
                replacement.EffectivePrioritiesInputsDigest,
                baseAuthority.EffectivePrioritiesInputsDigest);
            Assert.AreNotEqual(replacement.AuthorityDigest, baseAuthority.AuthorityDigest);
        }
        finally
        {
            DeleteTempDirectory(overlayRoot);
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_rejects_new_higher_version_custom_directory_membership()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customId = "4b3a4c48-d2af-4e46-9d27-9f06eab83c0c";
            WriteBaseContent(
                root,
                $"<customdatadirectoryname><directoryname>{customId}&gt;1.0</directoryname>"
                + "<order>0</order><enabled>True</enabled></customdatadirectoryname>",
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string customDataRoot = Path.Combine(root, "customdata");
            string versionTwoRoot = Path.Combine(customDataRoot, "Rules v2");
            Directory.CreateDirectory(versionTwoRoot);
            File.WriteAllText(
                Path.Combine(versionTwoRoot, "manifest.xml"),
                $"<manifest><guid>{customId}</guid><version>2.0.0</version></manifest>");
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(
                CharacterXml(
                    "<customdatadirectorynames><directoryname>Rules v2</directoryname>"
                    + "</customdatadirectorynames>"))!;

            Assert.IsTrue(context.TryResolveCreationSourceProfile(out _));
            int readsBeforeDrift = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;
            string versionThreeRoot = Path.Combine(customDataRoot, "Rules v3");
            Directory.CreateDirectory(versionThreeRoot);
            string manifestPath = Path.Combine(versionThreeRoot, "manifest.xml");
            File.WriteAllText(
                manifestPath,
                $"<manifest><guid>{customId}</guid><version>3.0.0</version></manifest>");

            Assert.IsFalse(context.TryResolveCreationSourceProfile(out _));
            Assert.AreEqual(
                readsBeforeDrift,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics.SourceDriftDetected);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics.DirectoryValidationCount > 0);
            Assert.IsFalse(
                resolver.LastSourceInputSnapshotDiagnostics.PhysicalReadsByPath.ContainsKey(manifestPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_context_rejects_symlink_target_drift_without_reopening_cached_bytes()
    {
        if (OperatingSystem.IsWindows())
            return;

        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            string firstTarget = Path.Combine(root, "data", "priorities-a.xml");
            string secondTarget = Path.Combine(root, "data", "priorities-b.xml");
            File.Move(prioritiesPath, firstTarget);
            File.WriteAllText(
                secondTarget,
                File.ReadAllText(firstTarget).Replace(
                    "<attributes>24</attributes>",
                    "<attributes>23</attributes>",
                    StringComparison.Ordinal));
            File.CreateSymbolicLink(prioritiesPath, Path.GetFileName(firstTarget));
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            ICharacterSourceDataContext context = resolver.TryCreateContext(CharacterXml())!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));
            int readsBeforeDrift = resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount;

            File.Delete(prioritiesPath);
            File.CreateSymbolicLink(prioritiesPath, Path.GetFileName(secondTarget));
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));

            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            Assert.AreEqual(
                readsBeforeDrift,
                resolver.LastSourceInputSnapshotDiagnostics!.PhysicalReadCount);
            Assert.IsTrue(resolver.LastSourceInputSnapshotDiagnostics!.SourceDriftDetected);
            CollectionAssert.Contains(
                resolver.LastSourceInputSnapshotDiagnostics!.DriftedPaths.ToList(),
                prioritiesPath);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Standard_priority_skills_matrix_tampering_is_not_authoritative()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string path = Path.Combine(root, "data", "priorities.xml");
            string original = File.ReadAllText(path);
            string tampered = original.Replace(
                "<skills>46</skills>",
                "<skills>45</skills>",
                StringComparison.Ordinal);
            Assert.AreNotEqual(original, tampered);
            File.WriteAllText(path, tampered);

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority));
            Assert.IsFalse(authority.IsAuthoritative);
            Assert.IsTrue(
                authority.Blockers.Contains(
                    CharacterCreationPrerequisiteBlockers.PriorityRowsInvalid,
                    StringComparer.Ordinal),
                string.Join(",", authority.Blockers));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Priority_authority_projects_special_movement_without_invalidating_heritage()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string metatypesPath = Path.Combine(root, "data", "metatypes.xml");
            string original = File.ReadAllText(metatypesPath);
            string special = original.Replace(
                "<bonus/><source>SR5</source>",
                "<movement>Special</movement><bonus/><source>SR5</source>",
                StringComparison.Ordinal);
            Assert.AreNotEqual(original, special);
            File.WriteAllText(metatypesPath, special);

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority));
            Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
            CharacterCreationPriorityHeritageOptionProjection[] humans = authority.Options
                .Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage)
                .SelectMany(option => option.HeritageOptions)
                .Where(heritage => heritage.MetatypeName == "Human")
                .ToArray();
            Assert.IsGreaterThan(0, humans.Length);
            Assert.IsTrue(humans.All(heritage => heritage.IsEnabled));
            Assert.IsTrue(humans.All(heritage => heritage.Movement.IsSpecial));
            Assert.IsTrue(humans.All(heritage => heritage.Movement ==
                CharacterCreationMetatypeMovementProjection.Special));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_skills_projects_enabled_free_knowledge_points_bound_to_exact_character_xml()
    {
        string characterXml = CharacterXml(
            "<improvements>"
            + "<improvement><val>3</val><condition/><improvementttype>FreeKnowledgeSkills</improvementttype>"
            + "<custom>False</custom><addtorating>False</addtorating><enabled>True</enabled></improvement>"
            + "<improvement><val>99</val><condition/><improvementttype>FreeKnowledgeSkills</improvementttype>"
            + "<custom>False</custom><addtorating>False</addtorating><enabled>False</enabled></improvement>"
            + "</improvements>");
        ICharacterSourceDataContext context = CreateContext(FindCoreRoot(), characterXml)!;

        Assert.IsTrue(context.TryResolveCreationSkillsAuthority(
            out CharacterCreationSkillsAuthority authority));
        Assert.IsTrue(
            authority.IsAuthoritative,
            $"{string.Join(",", authority.Blockers)}; active={authority.ActiveSkills.Count}; "
            + $"knowledge={authority.KnowledgeSkills.Count}; groups={authority.SkillGroups.Count}; "
            + $"contributions={authority.KnowledgePointContributions.Count}");
        Assert.IsTrue(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(authority));
        CharacterCreationKnowledgePointContribution contribution =
            authority.KnowledgePointContributions.Single();
        Assert.AreEqual(3, contribution.Points);
        Assert.AreEqual(
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(characterXml),
            contribution.SourceCharacterXmlDigest);
        Assert.IsTrue(CharacterCreationSkillsDigest.IsCanonical(contribution.SourceDigest));
        CollectionAssert.Contains(authority.SourceAnchorIds.ToList(), "character.xml#improvements");
    }

    [TestMethod]
    public void Creation_qualities_projects_profile_caps_stable_options_and_fail_closed_rows()
    {
        ICharacterSourceDataContext context = CreateContext(FindCoreRoot(), CharacterXml())!;

        Assert.IsTrue(context.TryResolveCreationQualitiesAuthority(
            out CharacterCreationQualitiesAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.IsGreaterThan(0, authority.QualityKarmaLimit);
        Assert.IsGreaterThan(0, authority.Options.Count);
        Assert.IsTrue(authority.Options.Any(static option => option.IsSelectable));
        Assert.IsTrue(authority.Options.Any(static option => !option.IsSelectable));
        Assert.AreEqual(
            authority.Options.Count,
            authority.Options.Select(static option => option.OptionId)
                .Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(authority.Options.All(option =>
            CharacterCreationQualitiesRules.DigestsEqual(
                option.OptionDigest,
                CharacterCreationQualitiesRules.ComputeOptionDigest(option))));
        Assert.IsTrue(authority.Options.All(option =>
            CharacterCreationQualitiesRules.DigestsEqual(
                option.SourceNodeDigest,
                CharacterCreationQualitiesRules.ComputeSourceNodeDigest(
                    option.SourceNodeXml))));
        Assert.IsTrue(CharacterCreationQualitiesRules.DigestsEqual(
            authority.AuthorityDigest,
            CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority)));
    }

    [TestMethod]
    public void Creation_skills_rejects_free_knowledge_points_with_unproven_scope_fields()
    {
        string characterXml = CharacterXml(
            "<improvements><improvement><val>3</val><target>forged-scope</target>"
            + "<condition/><improvementttype>FreeKnowledgeSkills</improvementttype>"
            + "<custom>False</custom><addtorating>False</addtorating><enabled>True</enabled>"
            + "</improvement></improvements>");
        ICharacterSourceDataContext context = CreateContext(FindCoreRoot(), characterXml)!;

        Assert.IsTrue(context.TryResolveCreationSkillsAuthority(
            out CharacterCreationSkillsAuthority authority));
        Assert.IsFalse(authority.IsAuthoritative);
        CollectionAssert.Contains(
            authority.Blockers.ToList(),
            CharacterCreationSkillsBlockers.KnowledgeContributionAuthorityUnsupported);
    }

    [TestMethod]
    public void Creation_skills_rejects_malformed_or_ambiguous_projected_scalars()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string[] malformedActiveRows =
            [
                "<id>30000000-0000-0000-0000-000000000001</id>"
                + "<name>Running</name><name>Forged Running</name><attribute>AGI</attribute>"
                + "<category>Physical Active</category><default>True</default>"
                + "<skillgroup>Athletics</skillgroup><exotic>False</exotic><specs/>"
                + "<source>SR5</source>",
                "<id>30000000-0000-0000-0000-000000000001</id>"
                + "<name>Running</name><attribute>AGI</attribute><category>Physical Active</category>"
                + "<default>True</default><skillgroup>Athletics</skillgroup>"
                + "<exotic>not-a-boolean</exotic><specs/><source>SR5</source>",
                "<id>30000000-0000-0000-0000-000000000001</id>"
                + "<name>Running</name><attribute>AGI</attribute><category>Physical Active</category>"
                + "<default>True</default><skillgroup> </skillgroup><exotic>False</exotic>"
                + "<specs/><source>SR5</source>",
                "<id>30000000-0000-0000-0000-000000000001</id>"
                + "<name>Running</name><attribute code=\"forged\">AGI</attribute>"
                + "<category>Physical Active</category><default>True</default>"
                + "<skillgroup>Athletics</skillgroup><exotic>False</exotic><specs/>"
                + "<source>SR5</source>"
            ];
            foreach (string malformed in malformedActiveRows)
            {
                WriteSkillsAuthorityFixture(root, malformed);
                ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
                Assert.IsNotNull(context);
                Assert.IsTrue(context.TryResolveCreationSkillsAuthority(
                    out CharacterCreationSkillsAuthority authority));
                Assert.IsFalse(authority.IsAuthoritative);
                CollectionAssert.Contains(
                    authority.Blockers.ToList(),
                    CharacterCreationSkillsBlockers.AuthorityUnavailable);
            }
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_skills_rejects_selected_custom_skills_overlay()
    {
        string root = CreateTempDirectory();
        try
        {
            const string directoryName = "Skill Rules";
            WriteBaseContent(
                root,
                $"<customdatadirectoryname><directoryname>{directoryName}</directoryname>"
                + "<order>0</order><enabled>True</enabled></customdatadirectoryname>",
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            WriteSkillsAuthorityFixture(root);
            string customRoot = Path.Combine(root, "customdata", directoryName);
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "custom_skills.xml"),
                "<chummer><skills><skill><id>30000000-0000-0000-0000-000000000099</id>"
                + "<name>Forged Skill</name><attribute>AGI</attribute><category>Physical Active</category>"
                + "<source>SR5</source></skill></skills></chummer>");

            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml($"<customdatadirectorynames><directoryname>{directoryName}</directoryname>"
                             + "</customdatadirectorynames>"))!;
            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCreationSkillsAuthority(
                out CharacterCreationSkillsAuthority authority));
            Assert.IsFalse(authority.IsAuthoritative);
            CollectionAssert.Contains(
                authority.Blockers.ToList(),
                CharacterCreationSkillsBlockers.SkillsSourceDrift);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Canonical_magician_attributes_confirm_and_cold_reopen_with_source_bound_grant()
    {
        string coreRoot = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
        string workspaceRoot = CreateTempDirectory();
        try
        {
            var store = new FileWorkspaceStore(workspaceRoot);
            var id = new CharacterWorkspaceId("canonical-magician-attributes");
            string xml = $"<character><name>Magician</name><alias>Source-bound test</alias><metatype>Human</metatype>"
                + "<buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>"
                + $"<created>false</created><karma>25</karma><nuyen>0</nuyen><settings>{SettingsId}</settings></character>";
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            var prerequisiteService = new CharacterCreationPrerequisiteService(
                store, new XmlCharacterFileQueries(new CharacterFileService()), resolver);
            CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> loaded = prerequisiteService.Load(new(id));
            Assert.IsNotNull(loaded.Value, $"{loaded.Outcome}: {string.Join(",", loaded.Blockers)}");
            CharacterCreationPrerequisiteState state = loaded.Value;
            Assert.HasCount(0, state.Blockers);
            CharacterCreationPriorityHeritageOptionProjection human = state.Authority.Options
                .Single(o => o.CategoryId == CharacterCreationPriorityCategoryIds.Heritage && o.Rank == "E")
                .HeritageOptions.Single(h => h.MetatypeName == "Human" && h.MetavariantName is null);
            CharacterCreationPriorityTalentOptionProjection magician = state.Authority.Options
                .Single(o => o.CategoryId == CharacterCreationPriorityCategoryIds.Talent && o.Rank == "C")
                .TalentOptions.Single(t => t.Value == "Magician");
            Assert.AreEqual(3, magician.Magic);
            IReadOnlyDictionary<string, string> ranks = CharacterCreationPrerequisiteServiceTests.Assign("E", "C", "A", "B", "D");
            CharacterCreationPrerequisitePreview prerequisite = prerequisiteService.Preview(new(state.Binding, ranks)
            {
                HeritageSelectionId = human.SelectionId,
                TalentSelectionId = magician.SelectionId
            }).Value!;
            Assert.IsTrue(prerequisite.CanConfirm, string.Join(",", prerequisite.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                prerequisiteService.Confirm(new(prerequisite.Binding, ranks, prerequisite.PreviewDigest, true)
                {
                    HeritageSelectionId = human.SelectionId,
                    TalentSelectionId = magician.SelectionId
                }).Outcome);

            var service = new CharacterCreationAttributesService(store, resolver);
            CharacterCreationAttributesState attributes = service.Load(new(id)).Value!;
            Assert.IsTrue(attributes.CanEdit, string.Join(",", attributes.Blockers));
            CharacterCreationAttributeAllocation[] allocations = [new("MAG", 1, 0)];
            CharacterCreationAttributesPreview preview = service.Preview(new(attributes.Binding, allocations)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(preview.Binding, allocations, preview.PreviewDigest, true)).Outcome);
            WorkspaceStoredDocument saved = store.Get(id).Value!;

            var coldStore = new FileWorkspaceStore(workspaceRoot);
            var coldResolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));
            CharacterCreationAttributesState cold = new CharacterCreationAttributesService(coldStore, coldResolver)
                .Load(new(id)).Value!;
            Assert.IsTrue(cold.CanEdit, string.Join(",", cold.Blockers));
            Assert.IsNotNull(cold.PendingDraft);
            CharacterCreationAttributeProjection magic = cold.Attributes.Single(a => a.AttributeId == "MAG");
            Assert.AreEqual(3, magic.Minimum);
            Assert.AreEqual(4, magic.Current);
            Assert.AreEqual(6, magic.Maximum);
            Assert.AreEqual(1m, cold.SpecialPointBudget.Used);
            CollectionAssert.IsSubsetOf(magician.SourceAnchorIds.ToArray(), magic.SourceAnchorIds.ToArray());
            Assert.AreEqual(saved.ContentRevision, cold.Binding.ContentRevision);
            Assert.AreEqual(saved.Document.AuxiliaryStateDigest, cold.Binding.AuxiliaryStateDigest);
            Assert.AreEqual(xml, coldStore.Get(id).Value!.Document.Content);
        }
        finally
        {
            DeleteTempDirectory(workspaceRoot);
        }
    }

    [TestMethod]
    public void Canonical_disabled_negative_oni_does_not_block_enabled_human_service_path()
    {
        string coreRoot = FindCoreRoot();
        var overlays = new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null);
        var resolver = new FileSystemCharacterSourceDataResolver(overlays);
        string workspaceRoot = CreateTempDirectory();
        try
        {
            var store = new FileWorkspaceStore(workspaceRoot);
            var workspaceId = new CharacterWorkspaceId("canonical-priority-negative-oni");
            string characterXml = $"""
                                   <character>
                                     <name>Canonical Priority Runner</name>
                                     <alias>Human Path</alias>
                                     <metatype>Human</metatype>
                                     <buildmethod>Priority</buildmethod>
                                     <createdversion>5.225.0</createdversion>
                                     <appversion>5.225.0</appversion>
                                     <karma>25</karma>
                                     <nuyen>0</nuyen>
                                     <created>false</created>
                                     <settings>{SettingsId}</settings>
                                   </character>
                                   """;
            Assert.IsTrue(store.CreateWorkspaceDocument(
                workspaceId,
                new WorkspaceDocument(characterXml, RulesetDefaults.Sr5)).Success);
            var service = new CharacterCreationPrerequisiteService(
                store,
                new XmlCharacterFileQueries(new CharacterFileService()),
                resolver);

            CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> loaded =
                service.Load(new CharacterCreationPrerequisiteLoadRequest(workspaceId));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
            CharacterCreationPrerequisiteState state = loaded.Value!;
            Assert.IsNotNull(state);
            Assert.HasCount(0, state.Blockers);
            Assert.IsTrue(state.Authority.IsAuthoritative, string.Join(",", state.Authority.Blockers));
            CollectionAssert.DoesNotContain(
                state.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);

            CharacterCreationPriorityOptionProjection heritageC = state.Authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && option.Rank == "C");
            CharacterCreationPriorityHeritageOptionProjection oni = heritageC.HeritageOptions.Single(option =>
                option.MetatypeName == "Ork" && option.MetavariantName == "Oni");
            CharacterCreationPriorityHeritageOptionProjection human = heritageC.HeritageOptions.Single(option =>
                option.MetatypeName == "Human" && option.MetavariantName is null);
            CharacterCreationPriorityTalentOptionProjection mundane = state.Authority.Options.Single(option =>
                    option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                    && option.Rank == "E")
                .TalentOptions.Single(option => option.Value == "Mundane");
            Assert.AreEqual(-4, oni.KarmaCost);
            Assert.IsFalse(oni.IsEnabled);
            CollectionAssert.Contains(
                oni.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported);
            Assert.IsTrue(human.IsEnabled);
            Assert.HasCount(0, human.Blockers);
            Assert.IsTrue(mundane.IsEnabled);
            Assert.HasCount(0, mundane.Blockers);

            var assignments = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CharacterCreationPriorityCategoryIds.Heritage] = "C",
                [CharacterCreationPriorityCategoryIds.Talent] = "E",
                [CharacterCreationPriorityCategoryIds.Attributes] = "A",
                [CharacterCreationPriorityCategoryIds.Skills] = "B",
                [CharacterCreationPriorityCategoryIds.Resources] = "D"
            };
            CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> humanResult =
                service.Preview(new CharacterCreationPrerequisitePreviewRequest(
                    state.Binding,
                    assignments)
                {
                    HeritageSelectionId = human.SelectionId,
                    TalentSelectionId = mundane.SelectionId
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, humanResult.Outcome);
            Assert.IsTrue(humanResult.Value!.CanConfirm, string.Join(",", humanResult.Blockers));
            Assert.HasCount(0, humanResult.Value.Blockers);
            Assert.AreEqual(human.KarmaCost, humanResult.Value.CreationKarmaBudget.Used);
            Assert.IsTrue(humanResult.Value.CreationKarmaBudget.Used >= 0);

            CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> oniResult =
                service.Preview(new CharacterCreationPrerequisitePreviewRequest(
                    state.Binding,
                    assignments)
                {
                    HeritageSelectionId = oni.SelectionId,
                    TalentSelectionId = mundane.SelectionId
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, oniResult.Outcome);
            CollectionAssert.Contains(
                oniResult.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported);
            Assert.IsFalse(oniResult.Value!.CanConfirm);
            Assert.AreEqual(0m, oniResult.Value.CreationKarmaBudget.Used);
        }
        finally
        {
            DeleteTempDirectory(workspaceRoot);
        }
    }

    [TestMethod]
    public void Canonical_talent_grants_project_exact_active_skill_and_group_choice_authority()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;

        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            authority.RawSkillsXmlDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            authority.EffectiveSkillsInputsDigest));

        CharacterCreationPriorityTalentOptionProjection magician = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "A")
            .TalentOptions.Single(option => option.Value == "Magician");
        CharacterCreationTalentActiveSkillGrantProjection magicGrant = magician.ActiveSkillGrant!;
        Assert.AreEqual(2, magicGrant.Quantity);
        Assert.AreEqual(5, magicGrant.BaseRating);
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Magic, magicGrant.SkillType);
        Assert.IsTrue(magicGrant.IsSupported, string.Join(",", magicGrant.Blockers));
        Assert.AreNotEqual(0, magicGrant.Options.Count);
        Assert.IsTrue(magicGrant.Options.All(option => option.Category is
            "Magical Active" or "Pseudo-Magical Active"));
        Assert.IsTrue(magicGrant.Options.Any(option => option.Category == "Pseudo-Magical Active"));
        Assert.IsTrue(magicGrant.Options.All(option => Guid.TryParseExact(
            option.SourceId,
            "D",
            out _)));
        Assert.IsTrue(magicGrant.Options.All(option =>
            CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                option.SkillsSourceDigest,
                authority.EffectiveSkillsInputsDigest)));

        CharacterCreationPriorityTalentOptionProjection technomancer = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "A")
            .TalentOptions.Single(option => option.Value == "Technomancer");
        CharacterCreationTalentActiveSkillGrantProjection resonanceGrant =
            technomancer.ActiveSkillGrant!;
        Assert.IsTrue(resonanceGrant.Options.All(option => option.Category == "Resonance Active"
            || option.SkillGroup is "Cracking" or "Electronics"));
        Assert.IsTrue(resonanceGrant.Options.Any(option => option.Category != "Resonance Active"
            && (option.SkillGroup is "Cracking" or "Electronics")));
        Assert.AreEqual(3, resonanceGrant.Quantity);

        CharacterCreationPriorityTalentOptionProjection artificialIntelligence = authority.Options
            .Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                              && option.Rank == "B")
            .TalentOptions.Single(option => option.Value == "A.I.");
        CharacterCreationTalentActiveSkillGrantProjection matrixGrant =
            artificialIntelligence.ActiveSkillGrant!;
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Matrix, matrixGrant.SkillType);
        Assert.IsTrue(matrixGrant.IsSupported, string.Join(",", matrixGrant.Blockers));
        Assert.IsTrue(matrixGrant.Options.Count > 0);
        Assert.IsTrue(matrixGrant.Options.All(option => option.SkillGroup is
            "Cracking" or "Electronics"));

        CharacterCreationPriorityTalentOptionProjection adept = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "B")
            .TalentOptions.Single(option => option.Value == "Adept");
        CharacterCreationTalentActiveSkillChoiceProjection[] exoticOptions = adept.ActiveSkillGrant!
            .Options.Where(option => option.IsExotic).ToArray();
        Assert.IsTrue(adept.IsEnabled, string.Join(",", adept.Blockers));
        Assert.IsTrue(exoticOptions.Length > 0);
        Assert.IsTrue(exoticOptions.All(option => !option.IsEnabled
            && option.Blockers.SequenceEqual(
                [CharacterCreationPrerequisiteBlockers
                    .TalentExoticSkillSpecializationRequired],
                StringComparer.Ordinal)));

        CharacterCreationPriorityTalentOptionProjection explorer = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "C")
            .TalentOptions.Single(option => option.Value == "Explorer");
        CharacterCreationTalentActiveSkillGrantProjection specificGrant =
            explorer.ActiveSkillGrant!;
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Specific, specificGrant.SkillType);
        Assert.IsTrue(specificGrant.IsSupported, string.Join(",", specificGrant.Blockers));
        CollectionAssert.AreEqual(
            new[] { "Arcana", "Assensing", "Astral Combat" },
            specificGrant.SpecificSkillChoiceNames.ToArray());
        CollectionAssert.AreEqual(
            specificGrant.SpecificSkillChoiceNames.ToArray(),
            specificGrant.Options.Select(option => option.CanonicalName).ToArray());

        CharacterCreationPriorityTalentOptionProjection adeptXPath = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "C")
            .TalentOptions.Single(option => option.Value == "Adept");
        CharacterCreationTalentActiveSkillGrantProjection xpathGrant =
            adeptXPath.ActiveSkillGrant!;
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.XPath, xpathGrant.SkillType);
        Assert.AreEqual(
            CharacterCreationTalentSkillGrantTypes.PinnedXPathPredicate,
            xpathGrant.SkillTypeQuery);
        Assert.IsTrue(xpathGrant.IsSupported, string.Join(",", xpathGrant.Blockers));
        Assert.IsTrue(xpathGrant.Options.Count > 0);
        Assert.IsTrue(xpathGrant.Options.All(option => option.Attribute is not ("RES" or "DEP")
            && (option.Category != "Magical Active" || string.IsNullOrEmpty(option.SkillGroup))));

        CharacterCreationPriorityTalentOptionProjection aspected = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "B")
            .TalentOptions.Single(option => option.Value == "Aspected Magician");
        CharacterCreationTalentSkillGroupGrantProjection groupGrant = aspected.SkillGroupGrant!;
        Assert.IsTrue(aspected.IsEnabled, string.Join(",", aspected.Blockers));
        Assert.AreEqual(1, groupGrant.Quantity);
        Assert.AreEqual(4, groupGrant.BaseRating);
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Grouped, groupGrant.SkillGroupType);
        Assert.AreEqual(string.Empty, groupGrant.CompatibilityMarker);
        Assert.IsTrue(groupGrant.IsSupported, string.Join(",", groupGrant.Blockers));
        CollectionAssert.AreEqual(
            new[] { "Conjuring", "Enchanting", "Sorcery" },
            groupGrant.Options.Select(option => option.CanonicalName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Conjuring", "Enchanting", "Sorcery" },
            groupGrant.RequestedGroupNames.ToArray());
        Assert.IsTrue(groupGrant.Options.All(option =>
            option.SelectionId.StartsWith("skill-group:", StringComparison.Ordinal)
            && option.MemberSkillSourceIds.Count > 0
            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(option.GroupDigest)));

        CharacterCreationPriorityTalentOptionProjection aspectedD = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "D")
            .TalentOptions.Single(option => option.Value == "Aspected Magician");
        Assert.AreEqual(0, aspectedD.SkillGroupGrant!.BaseRating);
        Assert.AreEqual(1, aspectedD.SkillGroupGrant.Quantity);
        Assert.IsTrue(aspectedD.SkillGroupGrant.IsSupported, string.Join(",", aspectedD.SkillGroupGrant.Blockers));
        Assert.IsTrue(aspectedD.IsEnabled, string.Join(",", aspectedD.Blockers));
        CollectionAssert.AreEqual(new[] { "Conjuring", "Enchanting", "Sorcery" },
            aspectedD.SkillGroupGrant.Options.Select(option => option.CanonicalName).ToArray());
        Assert.IsFalse(artificialIntelligence.IsEnabled,
            "A selection-only group must not enable the unsupported Depth ledger.");
        Assert.IsFalse(explorer.IsEnabled,
            "A selection-only group must not enable other unsupported Talent families.");
    }

    [TestMethod]
    public void Canonical_talent_corpus_projects_every_child_with_exact_branch_and_option_order()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));

        XDocument priorities = XDocument.Load(
            Path.Combine(coreRoot, "Chummer", "data", "priorities.xml"),
            LoadOptions.PreserveWhitespace);
        XDocument skillsDocument = XDocument.Load(Path.Combine(coreRoot, "Chummer", "data",
            "skills.xml"));
        (string Id, string Name, string Attribute, string Category, string? Group, bool Exotic)[]
            skills = skillsDocument.Root!.Element("skills")!.Elements("skill")
                .Select(skill =>
                {
                    string[] names = skill.Elements("name").Select(node => node.Value)
                        .Distinct(StringComparer.Ordinal).ToArray();
                    Assert.HasCount(1, names);
                    string? group = skill.Element("skillgroup")?.Value;
                    if (string.IsNullOrEmpty(group))
                        group = null;
                    return (
                        Id: skill.Element("id")!.Value,
                        Name: names[0],
                        Attribute: skill.Element("attribute")?.Value ?? string.Empty,
                        Category: skill.Element("category")!.Value,
                        Group: group,
                        Exotic: bool.TryParse(skill.Element("exotic")?.Value, out bool exotic)
                                && exotic);
                })
                .ToArray();
        (string Name, string[] Members)[] groups = skillsDocument.Root!.Element("skillgroups")!
            .Elements("name")
            .Select(node => (
                Name: node.Value,
                Members: skills.Where(skill => string.Equals(
                        skill.Group,
                        node.Value,
                        StringComparison.Ordinal))
                    .Select(skill => skill.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray()))
            .Where(group => group.Members.Length > 0)
            .OrderBy(group => group.Name, StringComparer.Ordinal)
            .ToArray();

        XElement[] rawRows = priorities.Root!.Element("priorities")!.Elements("priority")
            .Where(row => string.Equals(
                row.Element("category")?.Value,
                "Talent",
                StringComparison.Ordinal))
            .ToArray();
        CharacterCreationPriorityOptionProjection[] projectedRows = authority.Options
            .Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent)
            .ToArray();
        Assert.AreEqual(rawRows.Length, projectedRows.Length,
            "Every current-corpus Talent priority row must be represented.");

        foreach (XElement rawRow in rawRows)
        {
            string sourceId = rawRow.Element("id")!.Value;
            CharacterCreationPriorityOptionProjection projectedRow = projectedRows.Single(option =>
                option.SourceId == sourceId);
            XElement[] rawTalents = rawRow.Element("talents")!.Elements("talent").ToArray();
            Assert.AreEqual(rawTalents.Length, projectedRow.TalentOptions.Count);
            for (int index = 0; index < rawTalents.Length; index++)
            {
                XElement rawTalent = rawTalents[index];
                CharacterCreationPriorityTalentOptionProjection projected =
                    projectedRow.TalentOptions[index];
                Assert.AreEqual(rawTalent.Element("name")!.Value, projected.Name);
                Assert.AreEqual(rawTalent.Element("value")!.Value, projected.Value);
                string rawTalentNode = rawTalent.ToString(SaveOptions.DisableFormatting);
                Assert.AreEqual(rawTalentNode, projected.RawTalentNode);
                Assert.AreEqual(
                    CharacterCreationTalentGrantAuthorityDigest.ComputeRawTalentNode(rawTalentNode),
                    projected.PriorityChildNodeDigest);

                XElement? quantityNode = rawTalent.Element("skillqty")
                                         ?? rawTalent.Element("skillgroupqty");
                bool hasPrompt = int.TryParse(quantityNode?.Value, out int sourceQuantity)
                                 && sourceQuantity > 0;
                if (!hasPrompt)
                {
                    Assert.IsNull(projected.ActiveSkillGrant);
                    Assert.IsNull(projected.SkillGroupGrant);
                    continue;
                }
                XElement? typeNode = rawTalent.Element("skilltype")
                                     ?? rawTalent.Element("skillgrouptype");
                string rawSkillType = typeNode?.Value ?? string.Empty;
                string selectorTypeSource = rawTalent.Element("skilltype") is not null
                    ? CharacterCreationTalentGrantSelectorTypeSources.SkillType
                    : rawTalent.Element("skillgrouptype") is not null
                        ? CharacterCreationTalentGrantSelectorTypeSources.SkillGroupType
                        : CharacterCreationTalentGrantSelectorTypeSources.Missing;
                string skillType = CharacterCreationTalentSkillGrantTypes
                    .NormalizeLegacySelectorType(rawSkillType, selectorTypeSource);
                string rawQuery = typeNode?.Attribute("xpath")?.Value ?? string.Empty;
                string query = skillType == CharacterCreationTalentSkillGrantTypes.XPath
                    ? rawQuery
                    : string.Empty;
                bool groupPicker = skillType is
                    CharacterCreationTalentSkillGrantTypes.Grouped
                    or CharacterCreationTalentSkillGrantTypes.Choices;
                bool usesSkillValue = int.TryParse(
                                          rawTalent.Element("skillval")?.Value,
                                          out int effectiveRating)
                                      && effectiveRating >= 0;
                if (!usesSkillValue)
                {
                    Assert.IsTrue(int.TryParse(
                        rawTalent.Element("skillgroupval")?.Value,
                        out effectiveRating));
                    Assert.IsTrue(effectiveRating >= 0);
                }
                string improvementKind = usesSkillValue
                    ? CharacterCreationTalentGrantImprovementKinds.SkillBase
                    : CharacterCreationTalentGrantImprovementKinds.SkillGroupBase;
                if (!groupPicker)
                {

                    CharacterCreationTalentActiveSkillGrantProjection grant =
                        projected.ActiveSkillGrant!;
                    Assert.IsNull(projected.SkillGroupGrant);
                    int quantity = Math.Min(
                        sourceQuantity,
                        CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots);
                    Assert.AreEqual(quantity, grant.Quantity);
                    Assert.AreEqual(effectiveRating, grant.BaseRating);
                    Assert.AreEqual(improvementKind, grant.ImprovementKind);
                    Assert.AreEqual(skillType, grant.SkillType);
                    Assert.AreEqual(rawSkillType, grant.RawSelectorType);
                    Assert.AreEqual(selectorTypeSource, grant.SelectorTypeSource);
                    Assert.AreEqual(rawQuery, grant.RawSelectorTypeQuery);
                    Assert.AreEqual(query, grant.SkillTypeQuery);
                    string[] specificNames = skillType ==
                                             CharacterCreationTalentSkillGrantTypes.Specific
                        ? rawTalent.Element("skillchoices")?.Elements("skill")
                            .Select(node => node.Value).ToArray() ?? []
                        : [];
                    CollectionAssert.AreEqual(
                        specificNames,
                        grant.SpecificSkillChoiceNames.ToArray());

                    IEnumerable<(string Id, string Name, string Attribute, string Category,
                        string? Group, bool Exotic)> candidates = skillType switch
                    {
                        CharacterCreationTalentSkillGrantTypes.Active
                            or CharacterCreationTalentSkillGrantTypes.Default => skills,
                        CharacterCreationTalentSkillGrantTypes.Magic => skills.Where(skill =>
                            skill.Category is "Magical Active" or "Pseudo-Magical Active"),
                        CharacterCreationTalentSkillGrantTypes.Resonance => skills.Where(skill =>
                            skill.Category == "Resonance Active"
                            || skill.Group is "Cracking" or "Electronics"),
                        CharacterCreationTalentSkillGrantTypes.Matrix => skills.Where(skill =>
                            skill.Group is "Cracking" or "Electronics"),
                        CharacterCreationTalentSkillGrantTypes.Specific when specificNames.Length == 0
                            => skills,
                        CharacterCreationTalentSkillGrantTypes.Specific => specificNames.Select(name =>
                            skills.Single(skill => skill.Name == name)),
                        CharacterCreationTalentSkillGrantTypes.XPath when string.Equals(
                            query,
                            CharacterCreationTalentSkillGrantTypes.PinnedXPathPredicate,
                            StringComparison.Ordinal) => skills.Where(skill =>
                            skill.Attribute is not ("RES" or "DEP")
                            && (skill.Category != "Magical Active"
                                || string.IsNullOrEmpty(skill.Group))),
                        _ => []
                    };
                    (string Id, string Name, string Attribute, string Category, string? Group,
                        bool Exotic)[] expected = skillType ==
                                                  CharacterCreationTalentSkillGrantTypes.Specific
                                                  && specificNames.Length > 0
                        ? candidates.ToArray()
                        : candidates.OrderBy(skill => skill.Name, StringComparer.Ordinal)
                            .ThenBy(skill => skill.Id, StringComparer.Ordinal)
                            .ToArray();
                    CollectionAssert.AreEqual(
                        expected.Select(skill => skill.Id).ToArray(),
                        grant.Options.Select(option => option.SelectionId).ToArray(),
                        $"Active option identity/order drift for {projected.Name}.");
                    for (int optionIndex = 0; optionIndex < expected.Length; optionIndex++)
                    {
                        Assert.AreEqual(expected[optionIndex].Name,
                            grant.Options[optionIndex].CanonicalName);
                        Assert.AreEqual(expected[optionIndex].Attribute,
                            grant.Options[optionIndex].Attribute);
                        Assert.AreEqual(expected[optionIndex].Category,
                            grant.Options[optionIndex].Category);
                        Assert.AreEqual(expected[optionIndex].Group,
                            grant.Options[optionIndex].SkillGroup);
                        Assert.AreEqual(expected[optionIndex].Exotic,
                            grant.Options[optionIndex].IsExotic);
                        Assert.AreEqual(!expected[optionIndex].Exotic,
                            grant.Options[optionIndex].IsEnabled);
                    }
                    bool supportedSkillType = skillType switch
                    {
                        CharacterCreationTalentSkillGrantTypes.Active
                            or CharacterCreationTalentSkillGrantTypes.Default
                            or CharacterCreationTalentSkillGrantTypes.Magic
                            or CharacterCreationTalentSkillGrantTypes.Resonance
                            or CharacterCreationTalentSkillGrantTypes.Matrix
                            or CharacterCreationTalentSkillGrantTypes.Specific => true,
                        CharacterCreationTalentSkillGrantTypes.XPath => string.Equals(
                            query,
                            CharacterCreationTalentSkillGrantTypes.PinnedXPathPredicate,
                            StringComparison.Ordinal),
                        _ => false
                    };
                    Assert.AreEqual(
                        supportedSkillType
                        && expected.Count(skill => !skill.Exotic) >= quantity,
                        grant.IsSupported,
                        $"Active support-state drift for {projected.Name}.");
                    continue;
                }

                CharacterCreationTalentSkillGroupGrantProjection groupGrant =
                    projected.SkillGroupGrant!;
                Assert.IsNull(projected.ActiveSkillGrant);
                int groupQuantity = Math.Min(
                    sourceQuantity,
                    CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots);
                Assert.AreEqual(groupQuantity, groupGrant.Quantity);
                Assert.AreEqual(effectiveRating, groupGrant.BaseRating);
                Assert.AreEqual(improvementKind, groupGrant.ImprovementKind);
                Assert.AreEqual(rawSkillType, groupGrant.RawSelectorType);
                Assert.AreEqual(selectorTypeSource, groupGrant.SelectorTypeSource);
                Assert.AreEqual(rawQuery, groupGrant.RawSelectorTypeQuery);
                string groupType = skillType;
                Assert.AreEqual(groupType, groupGrant.SkillGroupType);
                Assert.AreEqual(
                    groupType == CharacterCreationTalentSkillGrantTypes.Choices
                        ? CharacterCreationTalentSkillGrantTypes.GroupChoiceAliasCompatibility
                        : string.Empty,
                    groupGrant.CompatibilityMarker);
                string[] requestedNames = rawTalent.Element("skillgroupchoices")?
                    .Elements("skillgroup").Select(node => node.Value).ToArray() ?? [];
                CollectionAssert.AreEqual(requestedNames, groupGrant.RequestedGroupNames.ToArray());
                bool supportedGroupType = groupType is
                    CharacterCreationTalentSkillGrantTypes.Grouped
                    or CharacterCreationTalentSkillGrantTypes.Choices;
                (string Name, string[] Members)[] expectedGroups = requestedNames.Length == 0
                                                                   && supportedGroupType
                    ? groups
                    : requestedNames.Select(name => groups.Single(group => group.Name == name))
                        .ToArray();
                CollectionAssert.AreEqual(
                    expectedGroups.Select(group => CharacterCreationTalentGrantAuthorityDigest
                            .ComputeSkillGroupSelectionId(
                                CharacterCreationTalentGrantAuthorityDigest.ComputeSkillGroup(
                                    authority.EffectiveSkillsInputsDigest,
                                    group.Name,
                                    group.Members)))
                        .ToArray(),
                    groupGrant.Options.Select(option => option.SelectionId).ToArray(),
                    $"Group option identity/order drift for {projected.Name}.");
                for (int groupIndex = 0; groupIndex < expectedGroups.Length; groupIndex++)
                {
                    Assert.AreEqual(expectedGroups[groupIndex].Name,
                        groupGrant.Options[groupIndex].CanonicalName);
                    CollectionAssert.AreEqual(
                        expectedGroups[groupIndex].Members,
                        groupGrant.Options[groupIndex].MemberSkillSourceIds.ToArray());
                }
                Assert.AreEqual(
                    supportedGroupType && expectedGroups.Length >= groupQuantity,
                    groupGrant.IsSupported,
                    $"Group support-state drift for {projected.Name}.");
            }
        }
    }

    [TestMethod]
    public void Canonical_sum_to_ten_and_improved_profiles_project_exact_weights_and_targets()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext standard = CreateContext(
            coreRoot,
            $"<character><settings>{CanonicalSumToTenSettingsId}</settings></character>")!;
        Assert.IsTrue(standard.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority standardAuthority));
        Assert.IsTrue(standardAuthority.IsAuthoritative,
            string.Join(",", standardAuthority.Blockers));
        Assert.AreEqual(10, standardAuthority.SumToTenTarget);
        Assert.AreEqual(4, standardAuthority.RankWeights.Single(weight => weight.Rank == "A").Value);
        Assert.AreEqual(3, standardAuthority.RankWeights.Single(weight => weight.Rank == "B").Value);

        ICharacterSourceDataContext improved = CreateContext(
            coreRoot,
            $"<character><settings>{CanonicalImprovedSumToTenSettingsId}</settings>"
            + "<customdatadirectorynames><directoryname>Sum-to-Ten Improved</directoryname>"
            + "</customdatadirectorynames></character>")!;
        Assert.IsTrue(improved.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority improvedAuthority));
        Assert.IsTrue(improvedAuthority.IsAuthoritative,
            string.Join(",", improvedAuthority.Blockers));
        Assert.AreEqual(14, improvedAuthority.SumToTenTarget);
        Assert.AreEqual(7, improvedAuthority.RankWeights.Single(weight => weight.Rank == "A").Value);
        Assert.AreEqual(4, improvedAuthority.RankWeights.Single(weight => weight.Rank == "B").Value);
        Assert.AreNotEqual(
            standardAuthority.SelectedPriorityCustomDataInputsDigest,
            improvedAuthority.SelectedPriorityCustomDataInputsDigest);
    }

    [TestMethod]
    public void Priority_authority_detects_source_drift_and_rejects_row_mutating_custom_data()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customSetting =
                "<customdatadirectoryname><directoryname>Unsafe Priority</directoryname>"
                + "<order>0</order><enabled>True</enabled></customdatadirectoryname>";
            WriteBaseContent(
                root,
                customSetting,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string customRoot = Path.Combine(root, "customdata", "Unsafe Priority");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "amend_priorities.xml"),
                "<chummer><priorities amendoperation=\"replace\" /></chummer>");
            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml(
                    "<customdatadirectorynames><directoryname>Unsafe Priority</directoryname>"
                    + "</customdatadirectorynames>"))!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority unsupported));
            Assert.IsFalse(unsupported.IsAuthoritative);
            CollectionAssert.Contains(
                unsupported.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.PriorityCustomDataUnsupported);

            File.Delete(Path.Combine(customRoot, "amend_priorities.xml"));
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));
            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Talent_skill_authority_detects_effective_skills_source_drift()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));

            File.AppendAllText(Path.Combine(root, "data", "skills.xml"), "\n");
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));
            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Talent_skill_authority_projects_case_insensitive_current_and_legacy_choice_rules()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            const string pinnedXPath =
                "not(attribute = 'RES' or attribute = 'DEP') and "
                + "(not(category = 'Magical Active') or skillgroup = '' or not(skillgroup))";
            string talentXml =
                "<talents>"
                + TalentGrant("Mixed", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype>MaGiC</skilltype><skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>grouped</skillgrouptype>")
                + TalentGrant("Hybrid Active Type", "<skilltype>magic</skilltype>"
                    + "<skillgroupqty>1</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>")
                + TalentGrant("Hybrid Active Value", "<skillval>3</skillval>"
                    + "<skillgroupqty>1</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>"
                    + "<skillgroupchoices><skillgroup>Sorcery</skillgroup></skillgroupchoices>")
                + TalentGrant("Hybrid Skill Choices", "<skillchoices><skill>Arcana</skill>"
                    + "</skillchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>grouped</skillgrouptype>"
                    + "<skillgroupchoices><skillgroup>Sorcery</skillgroup></skillgroupchoices>")
                + TalentGrant("Hybrid Active Quantity", "<skillqty>1</skillqty>"
                    + "<skillgroupqty>2</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>"
                    + "<skillgroupchoices><skillgroup>Sorcery</skillgroup></skillgroupchoices>")
                + TalentGrant("Hybrid Invalid Active Value", "<skillqty>1</skillqty>"
                    + "<skillval>not-a-number</skillval><skillgroupval>5</skillgroupval>"
                    + "<skilltype>magic</skilltype>")
                + TalentGrant("Whitespace Negative Numeric", "<skillqty> 1 </skillqty>"
                    + "<skillval> -2 </skillval><skilltype>active</skilltype>")
                + TalentGrant("Resonance", "<skillqty>3</skillqty><skillval>2</skillval>"
                    + "<skilltype>ReSoNaNcE</skilltype>")
                + TalentGrant("Matrix", "<skillqty>2</skillqty><skillval>2</skillval>"
                    + "<skilltype>MaTrIx</skilltype>")
                + TalentGrant("Specific", "<skillchoices><skill>Arcana</skill>"
                    + "<skill>Assensing</skill></skillchoices><skillqty>2</skillqty>"
                    + "<skillval>3</skillval><skilltype>SpEcIfIc</skilltype>")
                + TalentGrant("Empty Specific", "<skillchoices/><skillqty>1</skillqty>"
                    + "<skillval>3</skillval><skilltype>specific</skilltype>")
                + TalentGrant("XPath", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + $"<skilltype xpath=\"{pinnedXPath}\">XpAtH</skilltype>")
                + TalentGrant("Unknown XPath", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\"category = 'Combat Active'\">xpath</skilltype>")
                + TalentGrant("Empty XPath", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\"\">xpath</skilltype>")
                + TalentGrant("Whitespace XPath", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\" \" >xpath</skilltype>")
                + TalentGrant("Outer XPath", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\" category = 'Combat Active' \" >xpath</skilltype>")
                + TalentGrant("Default", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype>DeFaUlT</skilltype>")
                + TalentGrant("Missing Type", "<skillchoices><skill>Arcana</skill></skillchoices>"
                    + "<skillqty>1</skillqty><skillval>2</skillval>")
                + TalentGrant("Clamped Active", "<skillqty>4</skillqty><skillval>2</skillval>"
                    + "<skilltype>active</skilltype>")
                + TalentGrant("Unknown Active", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype>unknown</skilltype>")
                + TalentGrant("Unknown Attr Default", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\"category = 'Combat Active'\">unknown</skilltype>")
                + TalentGrant("Empty Attr Default", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\"\">unknown</skilltype>")
                + TalentGrant("Whitespace Attr Default", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\" \" >unknown</skilltype>")
                + TalentGrant("Outer Attr Default", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\" category = 'Combat Active' \" >unknown</skilltype>")
                + TalentGrant("Primary Choices", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype>choices</skilltype><skillgroupchoices>"
                    + "<skillgroup>Sorcery</skillgroup></skillgroupchoices>")
                + TalentGrant("Empty Type", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype></skilltype>")
                + TalentGrant("Whitespace Type", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype> </skilltype>")
                + string.Concat(new[]
                {
                    (Name: "Zero Active", Quantity: "0"),
                    (Name: "Negative Active", Quantity: "-1"),
                    (Name: "Empty Active", Quantity: string.Empty),
                    (Name: "Unparseable Active", Quantity: "not-a-number")
                }.Select(item => TalentGrant(
                    item.Name,
                    $"<skillqty>{item.Quantity}</skillqty><skillval>2</skillval>"
                    + "<skilltype>active</skilltype><skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>grouped</skillgrouptype>")))
                + TalentGrant("Legacy Choices", "<skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>ChOiCeS</skillgrouptype>")
                + TalentGrant("Grouped", "<skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>GrOuPeD</skillgrouptype>")
                + TalentGrant("Empty Grouped", "<skillgroupchoices/><skillgroupqty>1"
                    + "</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>")
                + TalentGrant("Clamped Grouped", "<skillgroupchoices/><skillgroupqty>4"
                    + "</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>")
                + string.Concat(new[]
                {
                    (Name: "Zero Group", Quantity: "0"),
                    (Name: "Negative Group", Quantity: "-1"),
                    (Name: "Unparseable Group", Quantity: "not-a-number")
                }.Select(item => TalentGrant(
                    item.Name,
                    $"<skillgroupqty>{item.Quantity}</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>grouped</skillgrouptype>")))
                + TalentGrant("Unknown Group", "<skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>unknown</skillgrouptype>")
                + "</talents>";
            WritePriorityFixture(root, talentXml);
            File.WriteAllText(
                Path.Combine(root, "data", "skills.xml"),
                "<chummer><skillgroups><name>Sorcery</name><name>Cracking</name>"
                + "<name>Electronics</name></skillgroups><skills>"
                + Skill("11111111-1111-1111-1111-111111111111", "Spellcasting", "MAG",
                    "Magical Active", "Sorcery", "DISABLED")
                + Skill("22222222-2222-2222-2222-222222222222", "Arcana", "LOG",
                    "Pseudo-Magical Active", null, "DISABLED")
                + Skill("33333333-3333-3333-3333-333333333333", "Assensing", "INT",
                    "Magical Active", null, "SR5")
                + Skill("44444444-4444-4444-4444-444444444444", "Cybercombat", "LOG",
                    "Technical Active", "Cracking", "DISABLED")
                + Skill("55555555-5555-5555-5555-555555555555", "Computer", "LOG",
                    "Technical Active", "Electronics", "DISABLED")
                + Skill("66666666-6666-6666-6666-666666666666", "Compiling", "RES",
                    "Resonance Active", null, "SR5")
                + Skill("77777777-7777-7777-7777-777777777777", "Pistols", "AGI",
                    "Combat Active", null, "SR5")
                + "</skills></chummer>");

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority));
            Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
            CharacterCreationPriorityTalentOptionProjection[] talents = authority.Options
                .First(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent)
                .TalentOptions.ToArray();

            CharacterCreationPriorityTalentOptionProjection mixed = talents.Single(talent =>
                talent.Value == "Mixed");
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Magic,
                mixed.ActiveSkillGrant!.SkillType);
            Assert.IsNull(mixed.SkillGroupGrant, "Active skill fields take branch precedence.");
            Assert.IsTrue(mixed.ActiveSkillGrant.Options.Any(option =>
                option.CanonicalName == "Arcana"), "Talent prompts do not apply BookXPath.");

            CharacterCreationPriorityTalentOptionProjection hybridActiveType = talents.Single(
                talent => talent.Value == "Hybrid Active Type");
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Magic,
                hybridActiveType.ActiveSkillGrant!.SkillType);
            Assert.AreEqual(CharacterCreationTalentGrantImprovementKinds.SkillGroupBase,
                hybridActiveType.ActiveSkillGrant.ImprovementKind,
                "The active selector retains the group-value improvement kind.");
            Assert.IsNull(hybridActiveType.SkillGroupGrant);

            CharacterCreationTalentSkillGroupGrantProjection hybridActiveValue = talents.Single(
                talent => talent.Value == "Hybrid Active Value").SkillGroupGrant!;
            Assert.AreEqual(3, hybridActiveValue.BaseRating,
                "skillval takes precedence over skillgroupval in the one effective lane.");
            Assert.AreEqual(CharacterCreationTalentGrantImprovementKinds.SkillBase,
                hybridActiveValue.ImprovementKind,
                "The group selector retains the active-value improvement kind.");

            CharacterCreationPriorityTalentOptionProjection hybridSkillChoices = talents.Single(
                talent => talent.Value == "Hybrid Skill Choices");
            Assert.IsNull(hybridSkillChoices.ActiveSkillGrant);
            CollectionAssert.AreEqual(
                new[] { "Sorcery" },
                hybridSkillChoices.SkillGroupGrant!.RequestedGroupNames.ToArray());

            CharacterCreationTalentSkillGroupGrantProjection hybridActiveQuantity = talents.Single(
                talent => talent.Value == "Hybrid Active Quantity").SkillGroupGrant!;
            Assert.AreEqual(1, hybridActiveQuantity.Quantity,
                "skillqty takes precedence over skillgroupqty in the one effective lane.");

            CharacterCreationTalentActiveSkillGrantProjection hybridInvalidActiveValue =
                talents.Single(talent => talent.Value == "Hybrid Invalid Active Value")
                    .ActiveSkillGrant!;
            Assert.AreEqual(5, hybridInvalidActiveValue.BaseRating);
            Assert.AreEqual(CharacterCreationTalentGrantImprovementKinds.SkillGroupBase,
                hybridInvalidActiveValue.ImprovementKind,
                "An unparsable skillval falls back to skillgroupval for persisted authority.");

            CharacterCreationTalentActiveSkillGrantProjection whitespaceNegative = talents.Single(
                talent => talent.Value == "Whitespace Negative Numeric").ActiveSkillGrant!;
            Assert.AreEqual(1, whitespaceNegative.Quantity);
            Assert.AreEqual(-2, whitespaceNegative.BaseRating,
                "Legacy int.TryParse accepts numeric whitespace and preserves negative ratings.");

            CharacterCreationTalentActiveSkillGrantProjection resonance = talents.Single(talent =>
                talent.Value == "Resonance").ActiveSkillGrant!;
            Assert.IsTrue(resonance.IsSupported, string.Join(",", resonance.Blockers));
            CollectionAssert.AreEquivalent(
                new[] { "Cybercombat", "Computer", "Compiling" },
                resonance.Options.Select(option => option.CanonicalName).ToArray());

            CharacterCreationTalentActiveSkillGrantProjection matrix = talents.Single(talent =>
                talent.Value == "Matrix").ActiveSkillGrant!;
            Assert.IsTrue(matrix.IsSupported, string.Join(",", matrix.Blockers));
            Assert.IsTrue(matrix.Options.All(option => option.SkillGroup is
                "Cracking" or "Electronics"));

            CharacterCreationTalentActiveSkillGrantProjection specific = talents.Single(talent =>
                talent.Value == "Specific").ActiveSkillGrant!;
            Assert.IsTrue(specific.IsSupported, string.Join(",", specific.Blockers));
            CollectionAssert.AreEqual(
                new[] { "Arcana", "Assensing" },
                specific.Options.Select(option => option.CanonicalName).ToArray());

            CharacterCreationTalentActiveSkillGrantProjection emptySpecific = talents.Single(talent =>
                talent.Value == "Empty Specific").ActiveSkillGrant!;
            Assert.IsTrue(emptySpecific.IsSupported, string.Join(",", emptySpecific.Blockers));
            Assert.IsEmpty(emptySpecific.SpecificSkillChoiceNames);
            CollectionAssert.AreEqual(
                emptySpecific.Options.OrderBy(
                        option => option.CanonicalName,
                        StringComparer.Ordinal)
                    .ThenBy(option => option.SourceId, StringComparer.Ordinal)
                    .Select(option => option.SourceId)
                    .ToArray(),
                emptySpecific.Options.Select(option => option.SourceId).ToArray());
            Assert.AreEqual(7, emptySpecific.Options.Count);

            CharacterCreationTalentActiveSkillGrantProjection xpath = talents.Single(talent =>
                talent.Value == "XPath").ActiveSkillGrant!;
            Assert.IsTrue(xpath.IsSupported, string.Join(",", xpath.Blockers));
            Assert.AreEqual(pinnedXPath, xpath.SkillTypeQuery);
            Assert.IsFalse(xpath.Options.Any(option => option.CanonicalName is
                "Spellcasting" or "Compiling"));

            CharacterCreationTalentActiveSkillGrantProjection unknownXPath = talents.Single(talent =>
                talent.Value == "Unknown XPath").ActiveSkillGrant!;
            Assert.IsFalse(unknownXPath.IsSupported);
            Assert.IsEmpty(unknownXPath.Options);
            CollectionAssert.Contains(
                unknownXPath.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported);
            foreach ((string value, string rawQuery) in new[]
                     {
                         ("Empty XPath", string.Empty),
                         ("Whitespace XPath", " "),
                         ("Outer XPath", " category = 'Combat Active' ")
                     })
            {
                CharacterCreationTalentActiveSkillGrantProjection invalidXPath = talents.Single(
                    talent => talent.Value == value).ActiveSkillGrant!;
                Assert.IsFalse(invalidXPath.IsSupported);
                Assert.IsEmpty(invalidXPath.Options);
                Assert.AreEqual(rawQuery, invalidXPath.RawSelectorTypeQuery);
                Assert.AreEqual(rawQuery, invalidXPath.SkillTypeQuery);
            }

            CharacterCreationTalentActiveSkillGrantProjection defaultGrant = talents.Single(talent =>
                talent.Value == "Default").ActiveSkillGrant!;
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default, defaultGrant.SkillType);
            Assert.IsTrue(defaultGrant.IsSupported, string.Join(",", defaultGrant.Blockers));

            CharacterCreationTalentActiveSkillGrantProjection missingType = talents.Single(talent =>
                talent.Value == "Missing Type").ActiveSkillGrant!;
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default, missingType.SkillType);
            Assert.IsTrue(missingType.IsSupported, string.Join(",", missingType.Blockers));
            Assert.IsEmpty(missingType.SpecificSkillChoiceNames);
            Assert.AreEqual(emptySpecific.Options.Count, missingType.Options.Count);

            CharacterCreationTalentActiveSkillGrantProjection clampedActive = talents.Single(talent =>
                talent.Value == "Clamped Active").ActiveSkillGrant!;
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots,
                clampedActive.Quantity);
            Assert.IsTrue(clampedActive.IsSupported, string.Join(",", clampedActive.Blockers));

            CharacterCreationTalentActiveSkillGrantProjection unknownActive = talents.Single(talent =>
                talent.Value == "Unknown Active").ActiveSkillGrant!;
            Assert.IsTrue(unknownActive.IsSupported, string.Join(",", unknownActive.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                unknownActive.SkillType);
            Assert.AreEqual("unknown", unknownActive.RawSelectorType);
            Assert.AreEqual(CharacterCreationTalentGrantSelectorTypeSources.SkillType,
                unknownActive.SelectorTypeSource);
            Assert.AreEqual(emptySpecific.Options.Count, unknownActive.Options.Count);
            CharacterCreationTalentActiveSkillGrantProjection unknownAttrDefault = talents.Single(
                talent => talent.Value == "Unknown Attr Default").ActiveSkillGrant!;
            Assert.IsTrue(unknownAttrDefault.IsSupported,
                string.Join(",", unknownAttrDefault.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                unknownAttrDefault.SkillType);
            Assert.AreEqual(string.Empty, unknownAttrDefault.SkillTypeQuery,
                "A non-XPATH selector ignores its xpath attribute in legacy dispatch.");
            Assert.AreEqual("category = 'Combat Active'",
                unknownAttrDefault.RawSelectorTypeQuery);
            foreach ((string value, string rawQuery) in new[]
                     {
                         ("Empty Attr Default", string.Empty),
                         ("Whitespace Attr Default", " "),
                         ("Outer Attr Default", " category = 'Combat Active' ")
                     })
            {
                CharacterCreationTalentActiveSkillGrantProjection ignoredQuery = talents.Single(
                    talent => talent.Value == value).ActiveSkillGrant!;
                Assert.IsTrue(ignoredQuery.IsSupported,
                    $"{value}: {string.Join(",", ignoredQuery.Blockers)}");
                Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                    ignoredQuery.SkillType);
                Assert.AreEqual(rawQuery, ignoredQuery.RawSelectorTypeQuery);
                Assert.AreEqual(string.Empty, ignoredQuery.SkillTypeQuery);
            }

            CharacterCreationPriorityTalentOptionProjection primaryChoices = talents.Single(
                talent => talent.Value == "Primary Choices");
            Assert.IsNull(primaryChoices.SkillGroupGrant,
                "The repaired choices alias is limited to skillgrouptype provenance.");
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                primaryChoices.ActiveSkillGrant!.SkillType);
            Assert.IsTrue(primaryChoices.ActiveSkillGrant.IsSupported,
                string.Join(",", primaryChoices.ActiveSkillGrant.Blockers));
            foreach (string value in new[] { "Empty Type", "Whitespace Type" })
            {
                CharacterCreationTalentActiveSkillGrantProjection legacyDefault = talents.Single(
                    talent => talent.Value == value).ActiveSkillGrant!;
                Assert.IsTrue(legacyDefault.IsSupported,
                    $"{value}: {string.Join(",", legacyDefault.Blockers)}");
                Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                    legacyDefault.SkillType);
                Assert.AreEqual(CharacterCreationTalentGrantSelectorTypeSources.SkillType,
                    legacyDefault.SelectorTypeSource);
                Assert.AreEqual(value == "Whitespace Type" ? " " : string.Empty,
                    legacyDefault.RawSelectorType);
            }
            foreach (string value in new[]
                     {
                         "Zero Active", "Negative Active", "Empty Active",
                         "Unparseable Active"
                     })
            {
                CharacterCreationPriorityTalentOptionProjection noPrompt = talents.Single(talent =>
                    talent.Value == value);
                Assert.IsNull(noPrompt.ActiveSkillGrant);
                Assert.IsNull(noPrompt.SkillGroupGrant,
                    "An active branch with no prompts still takes precedence over group fields.");
            }

            CharacterCreationTalentSkillGroupGrantProjection legacy = talents.Single(talent =>
                talent.Value == "Legacy Choices").SkillGroupGrant!;
            Assert.IsTrue(legacy.IsSupported, string.Join(",", legacy.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Choices, legacy.SkillGroupType);
            Assert.AreEqual(
                CharacterCreationTalentSkillGrantTypes.GroupChoiceAliasCompatibility,
                legacy.CompatibilityMarker);
            CollectionAssert.Contains(
                legacy.SourceAnchorIds.ToList(),
                $"compatibility:{legacy.CompatibilityMarker}");

            CharacterCreationTalentSkillGroupGrantProjection grouped = talents.Single(talent =>
                talent.Value == "Grouped").SkillGroupGrant!;
            Assert.IsTrue(grouped.IsSupported, string.Join(",", grouped.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Grouped, grouped.SkillGroupType);
            Assert.AreEqual(string.Empty, grouped.CompatibilityMarker);
            CollectionAssert.Contains(
                grouped.Options.Single().MemberSkillSourceIds.ToList(),
                "11111111-1111-1111-1111-111111111111");

            CharacterCreationTalentSkillGroupGrantProjection emptyGrouped = talents.Single(talent =>
                talent.Value == "Empty Grouped").SkillGroupGrant!;
            Assert.IsTrue(emptyGrouped.IsSupported, string.Join(",", emptyGrouped.Blockers));
            Assert.IsEmpty(emptyGrouped.RequestedGroupNames);
            CollectionAssert.AreEqual(
                new[] { "Cracking", "Electronics", "Sorcery" },
                emptyGrouped.Options.Select(option => option.CanonicalName).ToArray());

            CharacterCreationTalentSkillGroupGrantProjection clampedGrouped = talents.Single(talent =>
                talent.Value == "Clamped Grouped").SkillGroupGrant!;
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots,
                clampedGrouped.Quantity);
            Assert.IsTrue(clampedGrouped.IsSupported, string.Join(",", clampedGrouped.Blockers));
            foreach (string value in new[]
                     {
                         "Zero Group", "Negative Group", "Unparseable Group"
                     })
            {
                Assert.IsNull(talents.Single(talent => talent.Value == value).SkillGroupGrant);
            }
            CharacterCreationPriorityTalentOptionProjection unknownGroup = talents.Single(talent =>
                talent.Value == "Unknown Group");
            Assert.IsNull(unknownGroup.SkillGroupGrant);
            Assert.IsTrue(unknownGroup.ActiveSkillGrant!.IsSupported,
                string.Join(",", unknownGroup.ActiveSkillGrant.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                unknownGroup.ActiveSkillGrant.SkillType);
            Assert.AreEqual(CharacterCreationTalentGrantImprovementKinds.SkillGroupBase,
                unknownGroup.ActiveSkillGrant.ImprovementKind);
            Assert.AreEqual(CharacterCreationTalentGrantSelectorTypeSources.SkillGroupType,
                unknownGroup.ActiveSkillGrant.SelectorTypeSource);
            Assert.AreEqual("unknown", unknownGroup.ActiveSkillGrant.RawSelectorType);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Talent_skill_authority_fails_closed_on_distinct_ordinary_skill_name_collision()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(
                root,
                "<talents>" + TalentGrant(
                    "Active",
                    "<skillqty>1</skillqty><skillval>2</skillval><skilltype>active</skilltype>")
                + "</talents>");
            File.WriteAllText(
                Path.Combine(root, "data", "skills.xml"),
                "<chummer><skillgroups/><skills>"
                + Skill("11111111-1111-1111-1111-111111111111", "Duplicate", "LOG",
                    "Technical Active", null, "SR5")
                + Skill("22222222-2222-2222-2222-222222222222", "Duplicate", "INT",
                    "Technical Active", null, "SR5")
                + "</skills></chummer>");

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority));
            Assert.IsFalse(authority.IsAuthoritative);
            CollectionAssert.Contains(
                authority.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Priority_authority_projects_nonzero_heritage_karma_from_effective_source()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            File.WriteAllText(
                prioritiesPath,
                File.ReadAllText(prioritiesPath).Replace(
                    "<karma>0</karma>",
                    "<karma>7</karma>",
                    StringComparison.Ordinal));

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority));
            Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
            CharacterCreationPriorityHeritageOptionProjection human = authority.Options.Single(option =>
                    option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                    && option.Rank == "A")
                .HeritageOptions.Single(option => option.MetatypeName == "Human");
            Assert.AreEqual(7, human.KarmaCost);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Priority_authority_rejects_metatype_custom_data_and_detects_its_digest_drift()
    {
        string root = CreateTempDirectory();
        try
        {
            const string directoryName = "Unsafe Metatypes";
            const string customSetting =
                "<customdatadirectoryname><directoryname>Unsafe Metatypes</directoryname>"
                + "<order>0</order><enabled>True</enabled></customdatadirectoryname>";
            WriteBaseContent(
                root,
                customSetting,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string customRoot = Path.Combine(root, "customdata", directoryName);
            Directory.CreateDirectory(customRoot);
            string amendmentPath = Path.Combine(customRoot, "amend_metatypes.xml");
            File.WriteAllText(
                amendmentPath,
                "<chummer><metatypes><metatype><name>Human</name>"
                + "<karma amendoperation=\"replace\">1</karma></metatype></metatypes></chummer>");
            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml(
                    "<customdatadirectorynames><directoryname>Unsafe Metatypes</directoryname>"
                    + "</customdatadirectorynames>"))!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority unsupported));
            Assert.IsFalse(unsupported.IsAuthoritative);
            Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                unsupported.SelectedCustomDataInputsDigest));
            CollectionAssert.Contains(
                unsupported.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.MetatypeCustomDataUnsupported);

            File.AppendAllText(amendmentPath, "\n");
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));
            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Priority_projection_fails_closed_on_ambiguous_rows_missing_attributes_and_namespaces()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray></priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            string path = Path.Combine(root, "data", "priorities.xml");

            WritePriorityFixture(root);
            ICharacterSourceDataContext defaultArray = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(defaultArray.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority defaultArrayAuthority));
            Assert.IsTrue(defaultArrayAuthority.IsAuthoritative,
                string.Join(",", defaultArrayAuthority.Blockers));
            CollectionAssert.AreEqual(
                new[] { "A", "B", "C", "D", "E" },
                defaultArrayAuthority.PriorityArray.ToArray());
            string canonical = File.ReadAllText(path);
            File.WriteAllText(
                path,
                canonical.Replace(
                    "</priorities>",
                    "<priority><id>10000000-0000-0000-0000-000000000001</id>"
                    + "<name>duplicate</name><value>A</value><category>Heritage</category>"
                    + "</priority></priorities>",
                    StringComparison.Ordinal));
            AssertPriorityBlocker(root, CharacterCreationPrerequisiteBlockers.PriorityRowsInvalid);

            WritePriorityFixture(root);
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace(
                    "<attributes>24</attributes>",
                    string.Empty,
                    StringComparison.Ordinal));
            AssertPriorityBlocker(root, CharacterCreationPrerequisiteBlockers.PriorityRowsInvalid);

            WritePriorityFixture(root);
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace(
                    "<chummer>",
                    "<chummer xmlns=\"urn:unsupported\">",
                    StringComparison.Ordinal));
            AssertPriorityBlocker(
                root,
                CharacterCreationPrerequisiteBlockers.PriorityCategoriesInvalid);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Canonical_life_module_profile_exposes_exact_750_karma_authority()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{CanonicalLifeModuleSettingsId}</settings></character>")!;

        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority));
        Assert.AreEqual(CharacterCreationBuildMethods.LifeModules, authority.BuildMethod);
        Assert.AreEqual(750, authority.BuildPoints);
        Assert.IsTrue(authority.LifeModuleBudgetIsExact);
        Assert.IsEmpty(authority.BudgetBlockers);
        CollectionAssert.Contains(authority.EnabledSourcebooks.ToList(), "RF");
        CollectionAssert.Contains(authority.EnabledSourcebooks.ToList(), "SR5");
    }

    [TestMethod]
    public void Creation_budget_profile_rejects_missing_duplicate_mismatched_and_negative_fields()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, string.Empty, "");
            CharacterCreationSourceProfileAuthority missing = ResolveCreationProfile(root);
            Assert.IsFalse(missing.LifeModuleBudgetIsExact);
            CollectionAssert.Contains(
                missing.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodInvalid);
            CollectionAssert.Contains(
                missing.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildPointsInvalid);

            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>LifeModule</buildmethod><buildmethod>LifeModule</buildmethod>"
                + "<buildpoints>750</buildpoints><buildpoints>750</buildpoints>");
            CharacterCreationSourceProfileAuthority duplicate = ResolveCreationProfile(root);
            Assert.IsFalse(duplicate.LifeModuleBudgetIsExact);
            CollectionAssert.Contains(
                duplicate.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodInvalid);
            CollectionAssert.Contains(
                duplicate.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildPointsInvalid);

            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>750</buildpoints>");
            CharacterCreationSourceProfileAuthority mismatch = ResolveCreationProfile(root);
            Assert.IsFalse(mismatch.LifeModuleBudgetIsExact);
            CollectionAssert.Contains(
                mismatch.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodMismatch);

            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>LifeModule</buildmethod><buildpoints>-1</buildpoints>");
            CharacterCreationSourceProfileAuthority negative = ResolveCreationProfile(root);
            Assert.IsFalse(negative.LifeModuleBudgetIsExact);
            CollectionAssert.Contains(
                negative.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildPointsInvalid);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_profile_comes_from_saved_settings_and_binds_raw_profile_inputs()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, customDataSetting: string.Empty);
            ICharacterSourceDataContext first = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(first.TryResolveCreationSourceProfile(
                out CharacterCreationSourceProfileAuthority firstAuthority));
            CollectionAssert.AreEqual(
                new[] { "SG", "SR5" },
                firstAuthority.EnabledSourcebooks.ToArray());

            string settingsPath = Path.Combine(root, "data", "settings.xml");
            File.AppendAllText(settingsPath, "\n");
            ICharacterSourceDataContext second = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(second.TryResolveCreationSourceProfile(
                out CharacterCreationSourceProfileAuthority secondAuthority));

            Assert.AreEqual(SettingsId, firstAuthority.SettingsProfileId);
            Assert.AreNotEqual(
                firstAuthority.RawProfileInputsDigest,
                secondAuthority.RawProfileInputsDigest,
                "Changing raw settings.xml bytes must change the profile authority digest.");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_resolves_base_grade_and_vehicle_mod_source_values()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, customDataSetting: string.Empty);
            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryIsBookEnabled("sg", out bool streetGrimoireEnabled));
            Assert.IsTrue(streetGrimoireEnabled);
            Assert.IsTrue(context.TryIsBookEnabled("FA", out bool forbiddenArcanaEnabled));
            Assert.IsFalse(forbiddenArcanaEnabled);
            Assert.IsFalse(context.TryIsBookEnabled(string.Empty, out _));
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out int rating));
            Assert.AreEqual(4, rating);
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Alphaware", "Cyberware", out int fallbackRating));
            Assert.AreEqual(3, fallbackRating);
            Assert.IsTrue(context.TryResolveMaxNuyenDecimals(out int maximumNuyenDecimals));
            Assert.AreEqual(3, maximumNuyenDecimals);
            Assert.IsTrue(context.TryResolveGroupMembershipKarmaCosts(out int joinCost, out int leaveCost));
            Assert.AreEqual(5, joinCost);
            Assert.AreEqual(1, leaveCost);
            Assert.IsTrue(context.TryResolveKarmaNuyenExchangeRates(
                out decimal workingForPeopleRate,
                out decimal workingForManRate));
            Assert.AreEqual(1_500m, workingForPeopleRate);
            Assert.AreEqual(2_000m, workingForManRate);

            Assert.IsTrue(context.TryResolveVehicleModBonuses(
                VehicleModId,
                "Gyro-Stabilization",
                out CharacterVehicleModSourceBonuses bonuses));
            Assert.AreEqual("Rating + 1", bonuses.BodyExpression);
            Assert.AreEqual("2", bonuses.DeviceRatingExpression);
            Assert.AreEqual("3", bonuses.MatrixConditionExpression);
            Assert.AreEqual("1", bonuses.WirelessBodyExpression);
            Assert.AreEqual("4", bonuses.WirelessDeviceRatingExpression);
            Assert.AreEqual("5", bonuses.WirelessMatrixConditionExpression);

            Assert.IsTrue(context.TryResolveVehicleModBonuses(
                Guid.NewGuid().ToString("D"),
                "Removed Source Item",
                out CharacterVehicleModSourceBonuses missing));
            Assert.AreEqual(CharacterVehicleModSourceBonuses.Empty, missing);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_applies_highest_priority_governed_overlay()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, customDataSetting: string.Empty);
            string amendsRoot = Path.Combine(root, "amends");
            WriteOverlay(amendsRoot, "low", priority: 10, deviceRating: 6);
            WriteOverlay(amendsRoot, "high", priority: 20, deviceRating: 8);

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml(), amendsRoot)!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out int rating));
            Assert.AreEqual(8, rating);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_applies_selected_legacy_custom_data_in_profile_order()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customId = "4b3a4c48-d2af-4e46-9d27-9f06eab83c0c";
            WriteBaseContent(
                root,
                $"<customdatadirectoryname><directoryname>{customId}&gt;1.0</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            string customRoot = Path.Combine(root, "customdata", "My Rules");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "manifest.xml"),
                $"<manifest><guid>{customId}</guid><version>2.0.0</version></manifest>");
            File.WriteAllText(
                Path.Combine(customRoot, "amend_cyberware.xml"),
                "<chummer><grades><grade><name>Standard</name><devicerating>7</devicerating></grade></grades></chummer>");
            File.WriteAllText(
                Path.Combine(customRoot, "amend_vehicles.xml"),
                $"<chummer><mods><mod><id>{VehicleModId}</id><bonus><body>Rating + 2</body><devicerating>6</devicerating><matrixcmbonus>7</matrixcmbonus></bonus></mod></mods></chummer>");

            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>My Rules</directoryname></customdatadirectorynames>"))!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out int rating));
            Assert.AreEqual(7, rating);
            Assert.IsTrue(context.TryResolveVehicleModBonuses(
                VehicleModId,
                "Gyro-Stabilization",
                out CharacterVehicleModSourceBonuses bonuses));
            Assert.AreEqual("Rating + 2", bonuses.BodyExpression);
            Assert.AreEqual("6", bonuses.DeviceRatingExpression);
            Assert.AreEqual("7", bonuses.MatrixConditionExpression);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_applies_same_phase_custom_files_in_alphabetical_order()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                "<customdatadirectoryname><directoryname>Ordered Rules</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            string customRoot = Path.Combine(root, "customdata", "Ordered Rules");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "amend_z_cyberware.xml"),
                "<chummer><grades><grade><name>Standard</name><devicerating>9</devicerating></grade></grades></chummer>");
            File.WriteAllText(
                Path.Combine(customRoot, "amend_a_cyberware.xml"),
                "<chummer><grades><grade><name>Standard</name><devicerating>6</devicerating></grade></grades></chummer>");

            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>Ordered Rules</directoryname></customdatadirectorynames>"))!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out int rating));
            Assert.AreEqual(9, rating);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Spirit_catalog_applies_selected_custom_additions_and_amendments_exactly()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customId = "5b3a4c48-d2af-4e46-9d27-9f06eab83c0c";
            const string fireId = "a1111111-1111-1111-1111-111111111111";
            const string airId = "a2222222-2222-2222-2222-222222222222";
            const string waterId = "a3333333-3333-3333-3333-333333333333";
            WriteBaseContent(
                root,
                $"<customdatadirectoryname><directoryname>{customId}&gt;1.0</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            File.WriteAllText(
                Path.Combine(root, "data", "traditions.xml"),
                $"<chummer><spirits><spirit><id>{fireId}</id><name>Spirit of Fire</name></spirit><spirit><id>{airId}</id><name>Spirit of Air</name></spirit></spirits></chummer>");

            string customRoot = Path.Combine(root, "customdata", "Spirit Rules");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "manifest.xml"),
                $"<manifest><guid>{customId}</guid><version>2.0.0</version></manifest>");
            File.WriteAllText(
                Path.Combine(customRoot, "custom_traditions.xml"),
                $"<chummer><spirits><spirit><id>{waterId}</id><name>Spirit of Water</name></spirit></spirits></chummer>");
            File.WriteAllText(
                Path.Combine(customRoot, "amend_traditions.xml"),
                $"<chummer><spirits><spirit><id>{airId}</id><name amendoperation=\"REPLACE\">Spirit of Storm</name></spirit></spirits></chummer>");

            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>Spirit Rules</directoryname></customdatadirectorynames>"))!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveSpiritCatalogNames("Spirit", out IReadOnlyList<string> names));
            CollectionAssert.AreEqual(
                new[] { "Spirit of Fire", "Spirit of Storm", "Spirit of Water" },
                names.ToArray());
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_rejects_saved_custom_directory_mismatch_and_unknown_settings()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, customDataSetting: string.Empty);

            Assert.IsNull(CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>Unexpected Rules</directoryname></customdatadirectorynames>")));
            Assert.IsNull(CreateContext(
                root,
                $"<character><settings>{Guid.NewGuid():D}</settings></character>"));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Targeted_unsupported_amend_operation_fails_closed()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                "<customdatadirectoryname><directoryname>Unsafe Rules</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            string customRoot = Path.Combine(root, "customdata", "Unsafe Rules");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "amend_cyberware.xml"),
                "<chummer><grades><grade amendoperation=\"multiply\"><name>Standard</name><devicerating>9</devicerating></grade></grades></chummer>");
            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>Unsafe Rules</directoryname></customdatadirectorynames>"))!;

            Assert.IsNotNull(context);
            Assert.IsFalse(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out _));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static ICharacterSourceDataContext? CreateContext(
        string root,
        string characterXml,
        string? amendsRoot = null)
    {
        var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
        var resolver = new FileSystemCharacterSourceDataResolver(overlays);
        return resolver.TryCreateContext(characterXml);
    }

    private static void CopyCanonicalDataFiles(string destinationRoot, params string[] fileNames)
    {
        string sourceData = Path.Combine(FindCoreRoot(), "Chummer", "data");
        string destinationData = Path.Combine(destinationRoot, "data");
        Directory.CreateDirectory(destinationData);
        foreach (string fileName in fileNames)
        {
            File.Copy(
                Path.Combine(sourceData, fileName),
                Path.Combine(destinationData, fileName));
        }
    }

    private static void RewriteFirstElementValueSameLength(string path, string elementName)
    {
        DateTime capturedWriteTime = File.GetLastWriteTimeUtc(path);
        string original = File.ReadAllText(path);
        string openingTag = $"<{elementName}>";
        int valueStart = original.IndexOf(openingTag, StringComparison.Ordinal);
        Assert.IsTrue(valueStart >= 0);
        valueStart += openingTag.Length;
        int valueEnd = original.IndexOf($"</{elementName}>", valueStart, StringComparison.Ordinal);
        Assert.IsTrue(valueEnd > valueStart);
        int characterIndex = Enumerable.Range(valueStart, valueEnd - valueStart)
            .First(index => char.IsAsciiLetter(original[index]));
        char replacement = original[characterIndex] == 'X' ? 'Y' : 'X';
        string tampered = original[..characterIndex] + replacement + original[(characterIndex + 1)..];
        Assert.AreEqual(original.Length, tampered.Length);
        File.WriteAllText(path, tampered);
        File.SetLastWriteTimeUtc(path, capturedWriteTime);
    }

    private sealed class MutableContentOverlayCatalogService(
        string baseDataPath,
        string baseLanguagePath,
        IReadOnlyList<ContentOverlayPack> overlays) : IContentOverlayCatalogService
    {
        public ContentOverlayCatalog GetCatalog() => new(baseDataPath, baseLanguagePath, overlays);

        public IReadOnlyList<string> GetDataDirectories() =>
            [baseDataPath, .. overlays.Where(pack => pack.Enabled).Select(pack => pack.DataPath)];

        public IReadOnlyList<string> GetLanguageDirectories() =>
            [baseLanguagePath, .. overlays.Where(pack => pack.Enabled).Select(pack => pack.LanguagePath)];

        public string ResolveDataFile(string fileName)
        {
            foreach (ContentOverlayPack pack in overlays
                         .Where(pack => pack.Enabled
                             && string.Equals(pack.Mode, ContentOverlayModes.ReplaceFile, StringComparison.Ordinal))
                         .OrderByDescending(pack => pack.Priority)
                         .ThenByDescending(pack => pack.Id, StringComparer.Ordinal))
            {
                string candidate = Path.Combine(pack.DataPath, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(baseDataPath, fileName);
        }
    }

    private static string CharacterXml(string extra = "")
        => $"<character><settings>{SettingsId}</settings>{extra}</character>";

    private static CharacterCreationSourceProfileAuthority ResolveCreationProfile(string root)
    {
        ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority));
        return authority;
    }

    private static void WriteBaseContent(
        string root,
        string customDataSetting,
        string? buildAuthorityXml = null)
    {
        buildAuthorityXml ??=
            "<buildmethod>LifeModule</buildmethod><buildpoints>750</buildpoints>";
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(
            Path.Combine(data, "settings.xml"),
            $"<chummer><settings><setting><id>{SettingsId}</id><nuyenformat>#,0.###</nuyenformat><karmajoingroup>5</karmajoingroup><karmaleavegroup>1</karmaleavegroup><nuyenperbpwftp>1500</nuyenperbpwftp><nuyenperbpwftm>2000</nuyenperbpwftm><books><book>SR5</book><book>SG</book></books><customdatadirectorynames>{customDataSetting}</customdatadirectorynames>{buildAuthorityXml}<knowledgepointsexpression>({{INTUnaug}} + {{LOGUnaug}}) * 2</knowledgepointsexpression><usepointsonbrokengroups>False</usepointsonbrokengroups><breakskillgroupsincreatemode>False</breakskillgroupsincreatemode><specializationsbreakskillgroups>True</specializationsbreakskillgroups><alternatemetatypeattributekarma>False</alternatemetatypeattributekarma><reverseattributepriorityorder>False</reverseattributepriorityorder><karmacost><karmaattribute>5</karmaattribute></karmacost></setting></settings></chummer>");
        File.WriteAllText(
            Path.Combine(data, "metatypes.xml"),
            "<chummer><metatypes><metatype><id>a53d885d-a4a4-443d-b6a6-b0a55b0a96c7</id>"
            + "<name>Human</name><category>Metahuman</category><karma>0</karma>"
            + "<bodmin>1</bodmin><bodmax>6</bodmax><bodaug>10</bodaug>"
            + "<agimin>1</agimin><agimax>6</agimax><agiaug>10</agiaug>"
            + "<reamin>1</reamin><reamax>6</reamax><reaaug>10</reaaug>"
            + "<strmin>1</strmin><strmax>6</strmax><straug>10</straug>"
            + "<chamin>1</chamin><chamax>6</chamax><chaaug>10</chaaug>"
            + "<intmin>1</intmin><intmax>6</intmax><intaug>10</intaug>"
            + "<logmin>1</logmin><logmax>6</logmax><logaug>10</logaug>"
            + "<wilmin>1</wilmin><wilmax>6</wilmax><wilaug>10</wilaug>"
            + "<edgmin>2</edgmin><edgmax>7</edgmax><edgaug>7</edgaug>"
            + "<magmin>1</magmin><magmax>6</magmax><magaug>6</magaug>"
            + "<resmin>1</resmin><resmax>6</resmax><resaug>6</resaug>"
            + "<essmin>0</essmin><essmax>6</essmax><essaug>6</essaug>"
            + "<depmin>0</depmin><depmax>0</depmax><depaug>0</depaug>"
            + "<bonus/><source>SR5</source></metatype></metatypes></chummer>");
        File.WriteAllText(
            Path.Combine(data, "skills.xml"),
            "<chummer><skillgroups><name>Sorcery</name></skillgroups><skills><skill>"
            + "<id>40c72109-8924-45ca-a4d7-255b75e6a6b0</id><name>Spellcasting</name>"
            + "<category>Magical Active</category><skillgroup>Sorcery</skillgroup>"
            + "<source>SR5</source></skill></skills></chummer>");
        File.WriteAllText(
            Path.Combine(data, "cyberware.xml"),
            "<chummer><grades><grade><name>Standard</name><devicerating>4</devicerating></grade><grade><name>Alphaware</name></grade></grades></chummer>");
        File.WriteAllText(
            Path.Combine(data, "bioware.xml"),
            "<chummer><grades><grade><name>Standard</name><devicerating>2</devicerating></grade></grades></chummer>");
        File.WriteAllText(
            Path.Combine(data, "vehicles.xml"),
            $"<chummer><mods><mod><id>{VehicleModId}</id><name>Gyro-Stabilization</name><bonus><body>Rating + 1</body><devicerating>2</devicerating><matrixcmbonus>3</matrixcmbonus></bonus><wirelessbonus><body>1</body><devicerating>4</devicerating><matrixcmbonus>5</matrixcmbonus></wirelessbonus></mod></mods></chummer>");
    }

    private static void WriteSkillsAuthorityFixture(string root, string? firstActiveBody = null)
    {
        firstActiveBody ??=
            "<id>30000000-0000-0000-0000-000000000001</id><name>Running</name>"
            + "<attribute>AGI</attribute><category>Physical Active</category>"
            + "<default>True</default><skillgroup>Athletics</skillgroup><exotic>False</exotic>"
            + "<specs/><source>SR5</source>";
        File.WriteAllText(
            Path.Combine(root, "data", "skills.xml"),
            "<chummer><skills><skill>" + firstActiveBody + "</skill><skill>"
            + "<id>30000000-0000-0000-0000-000000000002</id><name>Gymnastics</name>"
            + "<attribute>AGI</attribute><category>Physical Active</category>"
            + "<default>True</default><skillgroup>Athletics</skillgroup><exotic>False</exotic>"
            + "<specs/><source>SR5</source>"
            + "</skill></skills><knowledgeskills><skill>"
            + "<id>40000000-0000-0000-0000-000000000001</id><name>English</name>"
            + "<attribute>INT</attribute><category>Language</category><default>False</default>"
            + "<skillgroup/><specs/><source>SR5</source>"
            + "</skill></knowledgeskills></chummer>");
    }

    private static void WriteOverlay(string amendsRoot, string id, int priority, int deviceRating)
    {
        string packRoot = Path.Combine(amendsRoot, id);
        string data = Path.Combine(packRoot, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(
            Path.Combine(packRoot, "manifest.json"),
            $"{{\"id\":\"{id}\",\"priority\":{priority},\"enabled\":true,\"mode\":\"merge-catalog\"}}");
        File.WriteAllText(
            Path.Combine(data, "cyberware.fragment.xml"),
            $"<chummer><grades><grade><name>Standard</name><devicerating>{deviceRating}</devicerating></grade></grades></chummer>");
    }

    private static string TalentGrant(string name, string grantXml) =>
        $"<talent><name>{name}</name><value>{name}</value>{grantXml}</talent>";

    private static string Skill(
        string id,
        string name,
        string attribute,
        string category,
        string? group,
        string source) =>
        $"<skill><id>{id}</id><name>{name}</name><attribute>{attribute}</attribute>"
        + $"<category>{category}</category>"
        + (group is null ? "<skillgroup/>" : $"<skillgroup>{group}</skillgroup>")
        + $"<source>{source}</source></skill>";

    private static void WritePriorityFixture(string root, string? talentXml = null)
    {
        talentXml ??=
            "<talents><talent><name>Mundane</name><value>Mundane</value>"
            + "<forbidden><oneof><metatype>A.I.</metatype></oneof></forbidden>"
            + "</talent></talents>";
        string[] categories = ["Heritage", "Talent", "Attributes", "Skills", "Resources"];
        string[] ranks = ["A", "B", "C", "D", "E"];
        Dictionary<string, int> attributePoints = new(StringComparer.Ordinal)
        {
            ["A"] = 24,
            ["B"] = 20,
            ["C"] = 16,
            ["D"] = 14,
            ["E"] = 12
        };
        Dictionary<string, (int Active, int Groups)> skillPoints = new(StringComparer.Ordinal)
        {
            ["A"] = (46, 10),
            ["B"] = (36, 5),
            ["C"] = (28, 2),
            ["D"] = (22, 0),
            ["E"] = (18, 0)
        };
        Dictionary<string, int> resourceNuyen = new(StringComparer.Ordinal)
        {
            ["A"] = 450000,
            ["B"] = 275000,
            ["C"] = 140000,
            ["D"] = 50000,
            ["E"] = 6000
        };
        int sequence = 1;
        string rows = string.Concat(categories.SelectMany(category => ranks.Select(rank =>
        {
            string attributes = category == "Attributes"
                ? $"<attributes>{attributePoints[rank]}</attributes>"
                : category == "Heritage"
                    ? "<metatypes><metatype><name>Human</name><value>1</value><karma>0</karma></metatype></metatypes>"
                    : category == "Talent"
                        ? talentXml
                        : category == "Skills"
                            ? $"<skills>{skillPoints[rank].Active}</skills>"
                              + $"<skillgroups>{skillPoints[rank].Groups}</skillgroups>"
                            : category == "Resources"
                                ? $"<resources>{resourceNuyen[rank]}</resources>"
                                : string.Empty;
            string id = $"00000000-0000-0000-0000-{sequence++:000000000000}";
            return $"<priority><id>{id}</id><name>{category}-{rank}</name><value>{rank}</value>"
                   + $"<category>{category}</category>{attributes}</priority>";
        })));
        File.WriteAllText(
            Path.Combine(root, "data", "priorities.xml"),
            "<chummer><categories><category>Heritage</category><category>Talent</category>"
            + "<category>Attributes</category><category>Skills</category><category>Resources</category>"
            + "</categories><priortysumtotenvalues><A>4</A><B>3</B><C>2</C><D>1</D><E>0</E>"
            + $"</priortysumtotenvalues><priorities>{rows}</priorities></chummer>");
    }

    private static void AssertPriorityBlocker(string root, string blocker)
    {
        ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority));
        Assert.IsFalse(authority.IsAuthoritative);
        CollectionAssert.Contains(authority.Blockers.ToList(), blocker);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-source-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate canonical Chummer/data/settings.xml.");
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
