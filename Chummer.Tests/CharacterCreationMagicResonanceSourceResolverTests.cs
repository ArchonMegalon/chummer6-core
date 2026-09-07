#nullable enable annotations

using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationMagicResonanceSourceResolverTests
{
    private const string StandardPrioritySettingsId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";

    [TestMethod]
    public void Talent_quality_sources_reject_missing_ambiguous_inactive_and_malformed_rows()
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        var context = resolver.TryCreateContext($"<character><settings>{StandardPrioritySettingsId}</settings></character>")!;
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var prerequisite));
        XElement[] metatypes = XDocument.Load(Path.Combine(root, "Chummer", "data", "metatypes.xml"))
            .Root!.Element("metatypes")!.Elements("metatype").ToArray();
        XElement[] qualities = XDocument.Load(Path.Combine(root, "Chummer", "data", "qualities.xml"))
            .Root!.Element("qualities")!.Elements("quality").ToArray();
        XElement original = qualities.Single(item => item.Element("name")?.Value == "Magician");
        string digest = CharacterCreationMagicResonanceDigest.ComputeUtf8("unchanged fixture inputs");
        CharacterCreationMagicResonanceAuthority Project(XElement[] rows, string[] books)
        {
            string qualityDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(
                new XElement("qualities", rows).ToString(SaveOptions.DisableFormatting));
            return CharacterCreationMagicResonanceAuthorityProjector.Project(metatypes, [], [], [], [], [], rows,
                new(prerequisite.SettingsProfileId, prerequisite, digest, digest, digest, digest, digest, digest,
                    digest, qualityDigest, digest, books, ["qualities.xml"], []));
        }
        var control = Project(qualities, ["SR5"]);
        var controlTalent = control.Talents.First(item => item.Kind == "magician");
        Assert.IsTrue(controlTalent.IsEnabled);
        XElement malformed = new(original);
        malformed.Add(new XElement("id", original.Element("id")!.Value));
        foreach (var failure in new[]
        {
            Project(qualities.Where(item => item != original).ToArray(), ["SR5"]),
            Project([.. qualities, new XElement(original)], ["SR5"]),
            Project(qualities.Select(item => item == original ? malformed : item).ToArray(), ["SR5"]),
            Project(qualities, [])
        })
        {
            var talent = failure.Talents.First(item => item.Kind == "magician");
            Assert.IsFalse(talent.IsEnabled);
            Assert.IsNotEmpty(talent.Blockers);
            Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.TryCreate(digest, 1, digest, 1, digest,
                failure, talent, new(null, null, [], [], []), out _, out _));
        }
        XElement amended = new(original);
        amended.Element("bonus")!.Element("enableattribute")!.Element("name")!.Value = "DEP";
        var changed = Project(qualities.Select(item => item == original ? amended : item).ToArray(), ["SR5"]);
        var changedSource = changed.Talents.First(item => item.Kind == "magician").GrantedQualitySources!.Single();
        Assert.AreNotEqual(control.SourceInputsDigest, changed.SourceInputsDigest);
        Assert.AreNotEqual(control.AuthorityDigest, changed.AuthorityDigest);
        Assert.AreNotEqual(controlTalent.GrantedQualitySources!.Single().SourceNodeDigest, changedSource.SourceNodeDigest);
        Assert.AreEqual("DEP", XElement.Parse(changedSource.CanonicalSourceXml).Element("bonus")!.Element("enableattribute")!.Element("name")!.Value,
            "Effective custom semantics must be retained as evidence, never replaced with a hardcoded Magician bonus.");
    }

    [TestMethod]
    public void Talent_quality_reference_validation_preserves_forced_choice_and_rejects_forgery()
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        var context = resolver.TryCreateContext($"<character><settings>{StandardPrioritySettingsId}</settings></character>")!;
        Assert.IsTrue(context.TryResolveCreationMagicResonanceAuthority(out var authority));
        var talent = authority.Talents.First(item => item.Kind == "magician");
        var source = talent.GrantedQualitySources!.Single();
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.IsValidSource(source));
        foreach (var invalid in new[]
        {
            source with { CanonicalSourceXml = null! },
            source with { SourceAnchorIds = null! },
            source with { SourceAnchorIds = ["qualities.xml#quality:invented"] },
            source with { SourceId = Guid.NewGuid().ToString("D") },
            source with { Name = "Adept" },
            source with { SourceBook = "SG" },
            source with { Page = "999" },
            source with { Reference = "Adept" },
            source with { EffectiveSourceDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("foreign-source") },
            source with { CanonicalSourceXml = source.CanonicalSourceXml.Replace("<page>69</page>", "<page>99</page>", StringComparison.Ordinal) }
        })
            Assert.IsFalse(CharacterCreationTalentQualitySourceRules.IsValidSource(invalid));
        Assert.IsFalse(CharacterCreationTalentQualitySourceRules.MatchesTalent(talent.CanonicalSourceXml, null));
        Assert.IsFalse(CharacterCreationTalentQualitySourceRules.MatchesTalent(talent.CanonicalSourceXml, [null!]));
        Assert.IsFalse(CharacterCreationTalentQualitySourceRules.MatchesTalent(talent.CanonicalSourceXml, []));
        Assert.IsFalse(CharacterCreationTalentQualitySourceRules.MatchesTalent(talent.CanonicalSourceXml, [source, source]));
        string selected = $"<talent><qualities><quality select=\"Sorcery\">{source.SourceId}</quality></qualities></talent>";
        var byId = source with { Reference = source.SourceId, ForcedSelection = "Sorcery" };
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.MatchesTalent(selected, [byId]));
        Assert.IsFalse(CharacterCreationTalentQualitySourceRules.MatchesTalent(selected, [byId with { ForcedSelection = "Conjuring" }]));
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.MatchesTalent("<talent />", []));
        foreach (string malformed in new[]
        {
            "<talent><qualities><quality select=\"\">Magician</quality></qualities></talent>",
            "<talent><qualities><quality rating=\"2\">Magician</quality></qualities></talent>",
            "<talent><qualities><quality>Magician</quality><quality>Magician</quality></qualities></talent>",
            "<talent><qualities /><qualities /></talent>",
            "<talent><qualities>Magician</qualities></talent>",
            "<talent xmlns=\"foreign\"><qualities /></talent>",
            "<!DOCTYPE talent [<!ENTITY q 'Magician'>]><talent><qualities><quality>&q;</quality></qualities></talent>"
        })
            Assert.IsFalse(CharacterCreationTalentQualitySourceRules.TryReadReferences(malformed, out _), malformed);
    }

    [TestMethod]
    public void Existing_magic_context_rejects_quality_byte_drift_and_fresh_context_rebinds()
    {
        string root = Path.Combine(Path.GetTempPath(), $"chummer-magic-quality-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data"));
        try
        {
            foreach (string file in new[] { "settings.xml", "priorities.xml", "metatypes.xml", "skills.xml", "qualities.xml",
                "traditions.xml", "streams.xml", "powers.xml", "spells.xml", "complexforms.xml" })
                File.Copy(Path.Combine(FindCoreRoot(), "Chummer", "data", file), Path.Combine(root, "data", file));
            var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            string character = $"<character><settings>{StandardPrioritySettingsId}</settings></character>";
            var context = resolver.TryCreateContext(character)!;
            Assert.IsTrue(context.TryResolveCreationMagicResonanceAuthority(out var before));
            Assert.IsTrue(before.IsAuthoritative, string.Join(",", before.Blockers));
            string path = Path.Combine(root, "data", "qualities.xml");
            DateTime modifiedAt = File.GetLastWriteTimeUtc(path);
            string bytes = File.ReadAllText(path);
            string changed = bytes.Replace("<name>MAG</name>", "<name>DEP</name>", StringComparison.Ordinal);
            Assert.AreNotEqual(bytes, changed);
            Assert.AreEqual(bytes.Length, changed.Length);
            File.WriteAllText(path, changed);
            File.SetLastWriteTimeUtc(path, modifiedAt);
            bool resolved = context.TryResolveCreationMagicResonanceAuthority(out var stale);
            Assert.IsFalse(resolved && stale.IsAuthoritative, "Same-length, restored-mtime source changes cannot reuse old authority.");
            var fresh = new FileSystemCharacterSourceDataResolver(overlays).TryCreateContext(character)!;
            Assert.IsTrue(fresh.TryResolveCreationMagicResonanceAuthority(out var after));
            Assert.IsTrue(after.IsAuthoritative, string.Join(",", after.Blockers));
            Assert.AreNotEqual(before.SourceInputsDigest, after.SourceInputsDigest);
            Assert.AreNotEqual(before.AuthorityDigest, after.AuthorityDigest);
            Assert.AreNotEqual(before.Talents.First(item => item.Kind == "magician").GrantedQualitySources!.Single().SourceNodeDigest,
                after.Talents.First(item => item.Kind == "magician").GrantedQualitySources!.Single().SourceNodeDigest);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Awakened_talents_capture_actual_quality_definitions_not_just_names()
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        var context = resolver.TryCreateContext($"<character><settings>{StandardPrioritySettingsId}</settings></character>")!;
        Assert.IsTrue(context.TryResolveCreationMagicResonanceAuthority(out var authority));
        XDocument qualities = XDocument.Load(Path.Combine(root, "Chummer", "data", "qualities.xml"));
        foreach (string kind in new[] { "magician", "adept", "mystic-adept", "aspected-magician", "technomancer" })
        {
            var talent = authority.Talents.First(item => item.Kind == kind);
            Assert.IsTrue(talent.IsEnabled, string.Join(",", talent.Blockers));
            Assert.IsNotNull(talent.GrantedQualitySources, kind);
            Assert.HasCount(1, talent.GrantedQualitySources);
            var source = talent.GrantedQualitySources.Single();
            Assert.IsTrue(CharacterCreationTalentQualitySourceRules.MatchesTalent(talent.CanonicalSourceXml, talent.GrantedQualitySources), kind);
            XElement expected = qualities.Root!.Element("qualities")!.Elements("quality")
                .Single(item => item.Element("id")?.Value == source.SourceId);
            Assert.AreEqual(expected.ToString(SaveOptions.DisableFormatting), source.CanonicalSourceXml);
            Assert.IsNotNull(XElement.Parse(source.CanonicalSourceXml).Element("bonus"));
            CollectionAssert.IsSubsetOf(source.SourceAnchorIds.ToArray(), talent.SourceAnchorIds.ToArray());
            CollectionAssert.IsSubsetOf(source.SourceAnchorIds.ToArray(), authority.SourceAnchorIds.ToArray());
        }
    }

    [TestMethod]
    public void Canonical_standard_priority_profile_projects_digest_bound_magic_resonance_authority()
    {
        string root = FindCoreRoot();
        var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
        var resolver = new FileSystemCharacterSourceDataResolver(overlays);
        ICharacterSourceDataContext context = resolver.TryCreateContext(
            $"<character><settings>{StandardPrioritySettingsId}</settings></character>")!;

        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.IsTrue(CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(authority), string.Join(";",
            authority.Talents.Where(item => !CharacterCreationMagicResonanceFinalizationRules.HasValidTalentPayload(item)
                || (item.IsEnabled && !CharacterCreationTalentQualitySourceRules.MatchesTalent(item.CanonicalSourceXml, item.GrantedQualitySources)))
                .Select(item => $"talent:{item.Name}")
                .Concat(authority.Traditions.Concat(authority.Streams).Concat(authority.AdeptPowers).Concat(authority.Spells).Concat(authority.ComplexForms)
                    .Where(item => item.MaximumLevels <= 0 || item.PointCost <= 0
                        || !CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(item))
                    .Select(item => $"{item.Identity.Kind}:{item.Name}:enabled={item.IsEnabled}:levels={item.MaximumLevels}:cost={item.PointCost}"))));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.AuthorityDigest));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.SourceInputsDigest));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.CustomDataInputsDigest));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.GmPolicyDigest));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.RuntimeDigest));

        CharacterCreationMagicResonanceTalentOption magician = authority.Talents.Single(item =>
            item.Rank == "C" && item.Kind == CharacterCreationMagicResonanceKinds.Magician);
        Assert.AreEqual(3, magician.Magic);
        Assert.AreEqual(5, magician.SpellBudget);
        Assert.IsTrue(magician.RequiresTradition);
        Assert.IsTrue(magician.AllowsSpells);
        Assert.IsTrue(magician.IsEnabled, string.Join(",", magician.Blockers));
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.HasValidTalentPayload(magician));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            magician.CanonicalSourceXmlDigest,
            CharacterCreationMagicResonanceDigest.ComputeUtf8(magician.CanonicalSourceXml)));

        CharacterCreationMagicResonanceTalentOption adept = authority.Talents.Single(item =>
            item.Rank == "D" && item.Kind == CharacterCreationMagicResonanceKinds.Adept);
        Assert.AreEqual(2m, adept.AdeptPowerPointBudget);
        Assert.IsTrue(adept.AllowsAdeptPowers);
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.HasValidTalentPayload(adept));

        CharacterCreationMagicResonanceCatalogOption acidStream = authority.Spells.Single(item =>
            item.Name == "Acid Stream");
        Assert.IsTrue(acidStream.IsEnabled, string.Join(",", acidStream.Blockers));
        Assert.AreEqual("Combat", acidStream.Category);
        Assert.AreEqual("SR5", acidStream.SourceBook);
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(acidStream));

        CharacterCreationMagicResonanceCatalogOption selectablePower = authority.AdeptPowers.Single(item =>
            item.Name == "Adrenaline Boost");
        Assert.IsTrue(selectablePower.IsEnabled, string.Join(",", selectablePower.Blockers));
        Assert.AreEqual(0.25m, selectablePower.PointCost);
        Assert.AreEqual(1, selectablePower.MaximumLevels);
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(selectablePower));

        CharacterCreationMagicResonanceCatalogOption unsupportedPower = authority.AdeptPowers.Single(item =>
            item.Name == "Astral Perception");
        Assert.IsFalse(unsupportedPower.IsEnabled);
        CollectionAssert.Contains(
            unsupportedPower.Blockers.ToList(),
            CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);

        var zeroCost = authority.AdeptPowers.Single(item => item.Name == "Berserker Temper");
        Assert.IsFalse(zeroCost.IsEnabled);
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(zeroCost));
        Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(
            zeroCost with { IsEnabled = true, Blockers = [] }));
        var toxic = authority.Traditions.Single(item => item.Name == "Toxic");
        Assert.AreEqual(Guid.Parse(XElement.Parse(toxic.CanonicalSourceXml).Element("id")!.Value).ToString("D"), toxic.Identity.SourceId);
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(toxic));
    }

    [TestMethod]
    public void Receipt_ledger_rejects_command_tampering_and_binds_every_authority_digest()
    {
        var workspaceId = new Chummer.Contracts.Workspaces.CharacterWorkspaceId(Guid.NewGuid().ToString("D"));
        string digest = CharacterCreationMagicResonanceDigest.ComputeUtf8("bound");
        var receipt = new CharacterCreationMagicResonanceReceipt(
            CharacterCreationMagicResonanceSchemas.ReceiptV1,
            workspaceId,
            PreviousContentRevision: 7,
            ContentRevision: 8,
            SavedRevision: 8,
            DraftRevision: 1,
            DraftDigest: digest,
            PreviewDigest: digest,
            IdempotencyKeyDigest: CharacterCreationMagicResonanceDigest.ComputeUtf8("key"),
            CommandDigest: CharacterCreationMagicResonanceDigest.ComputeUtf8("command"),
            PreviousReceiptDigest: CharacterCreationMagicResonanceDigest.ReceiptLedgerRootDigest,
            AuthorityDigest: digest,
            SourceInputsDigest: digest,
            CustomDataInputsDigest: digest,
            GmPolicyDigest: digest,
            RuntimeDigest: digest,
            TalentKind: CharacterCreationMagicResonanceKinds.Magician,
            AdeptPowerPointsRemaining: 0m,
            SpellsRemaining: 5,
            ComplexFormsRemaining: 0,
            CharacterDocumentChanged: false,
            ReceiptDigest: string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = CharacterCreationMagicResonanceDigest.ComputeReceipt(receipt)
        };

        Assert.IsTrue(CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
            [receipt], workspaceId, persistedContentRevision: 8));
        Assert.IsFalse(CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
            [receipt with { CommandDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("tampered") }],
            workspaceId,
            persistedContentRevision: 8));
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
}
