#nullable enable annotations

using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationMetatypeCatalogAuthorityTests
{
    private const string CanonicalLifeModuleSettingsId = "8a31af6d-7137-4284-872b-7d8087e156c6";
    private const string HumanId = "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7";
    private const string ElfId = "b3259991-b315-4dbe-ae3c-51f71a1116e2";
    private const string CanonicalMetatypesDigest = "sha256:ccee5dfabb8d0e193aa980e9905822a0f94fb9bb8093c162f5b694a974946425";

    [TestMethod]
    public void Canonical_life_module_profile_projects_digest_bound_human_and_elf()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(coreRoot, CharacterXml())!;

        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative);
        Assert.IsEmpty(authority.Blockers);
        Assert.AreEqual(CanonicalLifeModuleSettingsId, authority.SourceContext.SettingsProfileId);
        Assert.AreEqual(CanonicalMetatypesDigest, authority.SourceContext.RawMetatypesXmlDigest);
        Assert.AreEqual(1, authority.SourceContext.MetatypeKarmaMultiplier);
        Assert.AreEqual(1, authority.SourceContext.MinimumInitiativeDiceFallback);
        Assert.IsTrue(authority.SourceContext.DroneMods.HasValue);
        Assert.IsFalse(authority.SourceContext.DroneMods.GetValueOrDefault());
        StringAssert.StartsWith(authority.SourceContext.EffectiveMetatypesInputsDigest, "sha256:");
        StringAssert.StartsWith(authority.SourceContext.AuthorityDigest, "sha256:");
        Assert.HasCount(2, authority.Options);

        CharacterCreationMetatypeOptionProjection human = authority.Options.Single(option => option.OptionId == HumanId);
        Assert.IsTrue(human.IsEnabled);
        Assert.AreEqual("Human", human.Label);
        Assert.AreEqual("SR5", human.SourceBook);
        Assert.AreEqual(50, human.SourcePage);
        Assert.AreEqual(0, human.BaseKarma);
        Assert.AreEqual(0, human.KarmaCost);
        AssertAttribute(human, "BOD", 1, 6, 10);
        AssertAttribute(human, "AGI", 1, 6, 10);
        AssertAttribute(human, "CHA", 1, 6, 10);
        AssertAttribute(human, "EDG", 2, 7, 7);
        AssertAttribute(human, "DEP", 0, 0, 0);
        Assert.AreEqual(new CharacterCreationMetatypeInitiativeProjection(2, 12, 20, 1), human.Initiative);
        Assert.AreEqual(new CharacterCreationMetatypeMovementRate(2m, 1m, 0m), human.Movement.Walk);
        Assert.AreEqual(new CharacterCreationMetatypeMovementRate(4m, 0m, 0m), human.Movement.Run);
        Assert.AreEqual(new CharacterCreationMetatypeMovementRate(2m, 1m, 0m), human.Movement.Sprint);
        Assert.IsEmpty(human.GrantedQualities);
        CollectionAssert.AreEqual(new[] { "Nartaki" }, human.ExcludedMetavariants.Select(item => item.Label).ToArray());
        Assert.IsTrue(human.ExcludedMetavariants.All(item =>
            item.Blockers.Contains(CharacterCreationMetatypeCatalogBlockers.MetavariantUnsupported)));

        CharacterCreationMetatypeOptionProjection elf = authority.Options.Single(option => option.OptionId == ElfId);
        Assert.IsTrue(elf.IsEnabled);
        Assert.AreEqual("Elf", elf.Label);
        Assert.AreEqual("SR5", elf.SourceBook);
        Assert.AreEqual(50, elf.SourcePage);
        Assert.AreEqual(40, elf.BaseKarma);
        Assert.AreEqual(40, elf.KarmaCost);
        AssertAttribute(elf, "BOD", 1, 6, 10);
        AssertAttribute(elf, "AGI", 2, 7, 11);
        AssertAttribute(elf, "CHA", 3, 8, 12);
        AssertAttribute(elf, "EDG", 1, 6, 6);
        AssertAttribute(elf, "ESS", 0, 6, 6);
        CollectionAssert.AreEqual(
            new[] { "Low-Light Vision" },
            elf.GrantedQualities.Select(quality => quality.Name).ToArray());
        Assert.AreEqual(CharacterCreationMetatypeQualityPolarities.Positive, elf.GrantedQualities[0].Polarity);
        CollectionAssert.AreEqual(
            new[] { "Dryad", "Nocturna", "Xapiri Thëpë", "Wakyambi" },
            elf.ExcludedMetavariants.Select(item => item.Label).ToArray());
    }

    [TestMethod]
    public void Profile_source_and_duplicate_drift_fail_closed_without_enabled_options()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteCanonicalSubset(root, books: "<book>RF</book>", profileFields:
                "<metatypecostskarma>True</metatypecostskarma>"
                + "<metatypecostskarmamultiplier>1</metatypecostskarmamultiplier>"
                + "<metatypecostskarmamultiplier>1</metatypecostskarmamultiplier>"
                + "<mininitiativedice>1</mininitiativedice>");
            CharacterCreationMetatypeCatalogAuthority profileBlocked = Resolve(root);
            Assert.IsFalse(profileBlocked.IsAuthoritative);
            Assert.IsEmpty(profileBlocked.Options);
            CollectionAssert.Contains(
                profileBlocked.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.ProfileKarmaMultiplierInvalid);
            CollectionAssert.Contains(
                profileBlocked.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.ProfileDroneModsInvalid);

            WriteCanonicalSubset(root, books: "<book>RF</book>");
            CharacterCreationMetatypeCatalogAuthority sourceBlocked = Resolve(root);
            Assert.IsFalse(sourceBlocked.IsAuthoritative);
            Assert.IsTrue(sourceBlocked.Options.All(option => !option.IsEnabled));
            CollectionAssert.Contains(
                sourceBlocked.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.SourceDisabled);

            XDocument metatypes = XDocument.Load(Path.Combine(root, "data", "metatypes.xml"));
            XElement human = metatypes.Root!.Element("metatypes")!.Elements("metatype")
                .Single(item => string.Equals(item.Element("id")?.Value, HumanId, StringComparison.OrdinalIgnoreCase));
            human.AddAfterSelf(new XElement(human));
            metatypes.Save(Path.Combine(root, "data", "metatypes.xml"));
            CharacterCreationMetatypeCatalogAuthority duplicateBlocked = Resolve(root);
            Assert.IsFalse(duplicateBlocked.IsAuthoritative);
            Assert.IsTrue(duplicateBlocked.Options.All(option => !option.IsEnabled));
            CollectionAssert.Contains(
                duplicateBlocked.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.BaseEntryDuplicate);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Overlay_and_selected_custom_data_inputs_are_digest_bound_and_blocked()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteCanonicalSubset(root);
            string amendsRoot = Path.Combine(root, "amends");
            string overlayRoot = Path.Combine(amendsRoot, "metatype-test");
            Directory.CreateDirectory(Path.Combine(overlayRoot, "data"));
            File.WriteAllText(
                Path.Combine(overlayRoot, "manifest.json"),
                "{\"id\":\"metatype-test\",\"priority\":10,\"enabled\":true,\"mode\":\"merge-catalog\"}");
            File.WriteAllText(
                Path.Combine(overlayRoot, "data", "metatypes.fragment.xml"),
                "<chummer><metatypes /></chummer>");

            CharacterCreationMetatypeCatalogAuthority overlayBlocked = Resolve(root, amendsRoot: amendsRoot);
            Assert.IsFalse(overlayBlocked.IsAuthoritative);
            Assert.IsEmpty(overlayBlocked.Options);
            CollectionAssert.Contains(
                overlayBlocked.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.OverlayUnsupported);
            Assert.AreNotEqual(
                overlayBlocked.SourceContext.RawMetatypesXmlDigest,
                overlayBlocked.SourceContext.EffectiveMetatypesInputsDigest);

            const string customDirectoryName = "Metatype Test Rules";
            WriteCanonicalSubset(
                root,
                customDataSetting:
                    $"<customdatadirectoryname><directoryname>{customDirectoryName}</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            string customRoot = Path.Combine(root, "customdata", customDirectoryName);
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "custom_metatypes.xml"),
                "<chummer><metatypes /></chummer>");
            CharacterCreationMetatypeCatalogAuthority customBlocked = Resolve(
                root,
                $"<customdatadirectorynames><directoryname>{customDirectoryName}</directoryname></customdatadirectorynames>");
            Assert.IsFalse(customBlocked.IsAuthoritative);
            Assert.IsEmpty(customBlocked.Options);
            CollectionAssert.Contains(
                customBlocked.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.CustomDataUnsupported);
            StringAssert.StartsWith(customBlocked.SourceContext.SelectedCustomDataInputsDigest, "sha256:");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Selector_and_unknown_base_semantics_are_explicit_and_fail_closed()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteCanonicalSubset(root);
            string metatypesPath = Path.Combine(root, "data", "metatypes.xml");
            XDocument metatypes = XDocument.Load(metatypesPath);
            XElement elf = metatypes.Root!.Element("metatypes")!.Elements("metatype")
                .Single(item => string.Equals(item.Element("id")?.Value, ElfId, StringComparison.OrdinalIgnoreCase));
            elf.Element("qualities")!.Element("positive")!.Element("quality")!.SetAttributeValue("select", "Vision");
            metatypes.Save(metatypesPath);

            CharacterCreationMetatypeCatalogAuthority selectorBlocked = Resolve(root);
            Assert.IsFalse(selectorBlocked.IsAuthoritative);
            Assert.IsTrue(selectorBlocked.Options.All(option => !option.IsEnabled));
            CollectionAssert.Contains(
                selectorBlocked.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.SelectorSemanticsUnsupported);

            elf.Add(new XElement("special", "opaque"));
            metatypes.Save(metatypesPath);
            CharacterCreationMetatypeCatalogAuthority unknownBlocked = Resolve(root);
            Assert.IsFalse(unknownBlocked.IsAuthoritative);
            Assert.IsTrue(unknownBlocked.Options.All(option => !option.IsEnabled));
            CollectionAssert.Contains(
                unknownBlocked.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.UnknownSemantics);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Missing_inputs_and_post_context_source_or_profile_drift_fail_closed()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteCanonicalSubset(root);
            ICharacterSourceDataContext sourceDriftContext = CreateContext(root, CharacterXml())!;
            File.AppendAllText(Path.Combine(root, "data", "metatypes.xml"), "\n");
            Assert.IsTrue(sourceDriftContext.TryResolveCreationMetatypeCatalog(
                out CharacterCreationMetatypeCatalogAuthority sourceDrift));
            Assert.IsFalse(sourceDrift.IsAuthoritative);
            Assert.IsEmpty(sourceDrift.Options);
            CollectionAssert.Contains(
                sourceDrift.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.MetatypesSourceDrift);

            WriteCanonicalSubset(root);
            ICharacterSourceDataContext profileDriftContext = CreateContext(root, CharacterXml())!;
            File.AppendAllText(Path.Combine(root, "data", "settings.xml"), "\n");
            Assert.IsTrue(profileDriftContext.TryResolveCreationMetatypeCatalog(
                out CharacterCreationMetatypeCatalogAuthority profileDrift));
            Assert.IsFalse(profileDrift.IsAuthoritative);
            Assert.IsEmpty(profileDrift.Options);
            CollectionAssert.Contains(
                profileDrift.Blockers.ToList(),
                CharacterCreationMetatypeCatalogBlockers.ProfileSettingsDrift);

            WriteCanonicalSubset(root);
            File.Delete(Path.Combine(root, "data", "metatypes.xml"));
            ICharacterSourceDataContext missingContext = CreateContext(root, CharacterXml())!;
            Assert.IsNotNull(missingContext);
            Assert.IsFalse(missingContext.TryResolveCreationMetatypeCatalog(out _));
            Assert.IsNull(new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(root, root, null)).TryCreateContext(null!));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static void AssertAttribute(
        CharacterCreationMetatypeOptionProjection option,
        string attributeId,
        int minimum,
        int maximum,
        int augmentedMaximum)
    {
        CharacterCreationMetatypeAttributeProjection attribute = option.Attributes
            .Single(item => item.AttributeId == attributeId);
        Assert.AreEqual(
            new CharacterCreationMetatypeAttributeProjection(attributeId, minimum, maximum, augmentedMaximum),
            attribute);
    }

    private static CharacterCreationMetatypeCatalogAuthority Resolve(
        string root,
        string characterExtra = "",
        string? amendsRoot = null)
    {
        ICharacterSourceDataContext context = CreateContext(root, CharacterXml(characterExtra), amendsRoot)!;
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority authority));
        return authority;
    }

    private static ICharacterSourceDataContext? CreateContext(
        string root,
        string characterXml,
        string? amendsRoot = null)
    {
        var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
        return new FileSystemCharacterSourceDataResolver(overlays).TryCreateContext(characterXml);
    }

    private static string CharacterXml(string extra = "")
        => $"<character><settings>{CanonicalLifeModuleSettingsId}</settings>{extra}</character>";

    private static void WriteCanonicalSubset(
        string root,
        string books = "<book>SR5</book><book>RF</book>",
        string customDataSetting = "",
        string? profileFields = null)
    {
        profileFields ??=
            "<metatypecostskarma>True</metatypecostskarma>"
            + "<metatypecostskarmamultiplier>1</metatypecostskarmamultiplier>"
            + "<mininitiativedice>1</mininitiativedice>"
            + "<dronemods>False</dronemods>";
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        File.Copy(
            Path.Combine(FindCoreRoot(), "Chummer", "data", "metatypes.xml"),
            Path.Combine(data, "metatypes.xml"),
            overwrite: true);
        File.WriteAllText(
            Path.Combine(data, "settings.xml"),
            $"<chummer><settings><setting><id>{CanonicalLifeModuleSettingsId}</id>"
            + "<buildmethod>LifeModule</buildmethod><buildpoints>750</buildpoints>"
            + $"{profileFields}<books>{books}</books>"
            + $"<customdatadirectorynames>{customDataSetting}</customdatadirectorynames>"
            + "</setting></settings></chummer>");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-metatype-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "metatypes.xml")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate canonical Chummer/data/metatypes.xml.");
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
