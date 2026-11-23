using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationRuleSourceCaptureTests
{
    private const string Pistols9965 = "30000000-0000-0000-0000-000000000002";
    private const string Blades9965 = "30000000-0000-0000-0000-000000000003";
    private const string Weapon9965 = "50000000-0000-0000-0000-000000000001";
    private const BindingFlags Private9965 = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [TestMethod]
    public void Weapons9965_Two_combat_skills_and_repeat_preserve_every_option_and_raw_source()
    {
        using var fixture = new Fixture();
        WriteWeaponsFixture9965(fixture);
        var current = Current9965(fixture, Capture9965(fixture));
        var resolution = Resolution9965(current);
        using var evaluation = BeginEvaluation9965(resolution, current, out var source);
        // Ordinary independent queries are the oracle; they run before the scratch exists.
        var pistols = ReadSpecialization9965(source, Pistols9965);
        var blades = ReadSpecialization9965(source, Blades9965);
        Assert.IsTrue(pistols.Options.Any(row => row.Kind == CharacterCareerSkillSpecializationOptionKind.CombatWeapon));
        Assert.IsTrue(blades.Options.Any(row => row.Kind == CharacterCareerSkillSpecializationOptionKind.CombatWeapon));
        using (var scratch = BeginWeapons9965(source, current))
        {
            Assert.IsNull(Rows9965(scratch));
            Assert.IsTrue(source.TryResolveCareerSkillSpecializationSource(
                "30000000-0000-0000-0000-000000000001", CharacterCareerSkillKind.Active, out _));
            Assert.IsTrue(source.TryResolveCareerSkillSpecializationSource(
                "40000000-0000-0000-0000-000000000001", CharacterCareerSkillKind.Knowledge, out _));
            Assert.IsNull(Rows9965(scratch));
            AssertEquivalent9965(pistols, ReadSpecialization9965(source, Pistols9965));
            var rows = Rows9965(scratch)!;
            Assert.IsNotNull(rows);
            string[] bytes = rows.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray();
            AssertEquivalent9965(blades, ReadSpecialization9965(source, Blades9965));
            AssertEquivalent9965(pistols, ReadSpecialization9965(source, Pistols9965));
            Assert.AreSame(rows, Rows9965(scratch));
            CollectionAssert.AreEqual(bytes, rows.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray());
        }
        Assert.IsNull(ActiveWeapons9965(source));
    }

    [TestMethod]
    public void Weapons9965_Fresh_operation_and_source_cannot_inherit_a_previous_array()
    {
        using var fixture = new Fixture();
        WriteWeaponsFixture9965(fixture);
        var capture = Capture9965(fixture);
        var current = Current9965(fixture, capture);
        var resolution = Resolution9965(current);
        XElement[] previous;
        ICharacterSourceDataContext oldSource;
        using (BeginEvaluation9965(resolution, current, out oldSource))
        {
            using var scratch = BeginWeapons9965(oldSource, current);
            ReadSpecialization9965(oldSource, Pistols9965);
            previous = Rows9965(scratch)!;
            Assert.IsNotNull(previous);
        }
        Assert.IsNull(ActiveWeapons9965(oldSource));
        // Repeat the actual issuer lifetime, then use an independently captured source.
        foreach (var next in new[] { current, Current9965(fixture, Capture9965(fixture)) })
        {
            var nextResolution = ReferenceEquals(next, current) ? resolution : Resolution9965(next);
            using var evaluation = BeginEvaluation9965(nextResolution, next, out var source);
            Assert.IsNull(ActiveWeapons9965(source));
            var expected = ReadSpecialization9965(source, Pistols9965);
            using var scratch = BeginWeapons9965(source, next);
            Assert.IsNull(Rows9965(scratch));
            AssertEquivalent9965(expected, ReadSpecialization9965(source, Pistols9965));
            Assert.IsNotNull(Rows9965(scratch));
            Assert.AreNotSame(previous, Rows9965(scratch));
        }
    }

    [DataTestMethod]
    [DataRow("books")]
    [DataRow("amend")]
    public void Weapons9965_Changed_books_and_ordered_amends_are_recomputed_by_a_fresh_capture(string change)
    {
        using var fixture = new Fixture(selected: true);
        WriteWeaponsFixture9965(fixture);
        File.WriteAllText(fixture.Custom("First", "amend_weapons.xml"), WeaponNameAmend9965("First name"));
        File.WriteAllText(fixture.Custom("Second", "amend_weapons.xml"), WeaponNameAmend9965("Second name"));
        var original = Capture9965(fixture);
        var before = ReadCachedCapture9965(fixture, original);
        Assert.IsTrue(before.Options.Any(row => row.Name == "Second name"));
        Assert.IsFalse(before.Options.Any(row => row.Name == "Supplement pistol"));

        if (change == "amend")
            File.WriteAllText(fixture.Custom("Second", "amend_weapons.xml"), WeaponNameAmend9965("Changed name"));
        else
        {
            var settings = XDocument.Load(fixture.Data("settings.xml"));
            settings.Descendants("books").Single().Add(new XElement("book", "ALT"));
            settings.Save(fixture.Data("settings.xml"));
        }
        var after = ReadCachedCapture9965(fixture, Capture9965(fixture));
        Assert.AreEqual(change == "amend", after.Options.Any(row => row.Name == "Changed name"));
        Assert.AreEqual(change == "books", after.Options.Any(row => row.Name == "Supplement pistol"));
        Assert.AreEqual(change == "books", after.Options.Any(row => row.Name == "Second name"));
        Assert.AreNotEqual(before.RawSourceState, after.RawSourceState);
        // Captured bytes keep their own meaning after the live files change.
        AssertEquivalent9965(before, ReadCachedCapture9965(fixture, original));
    }

    [TestMethod]
    public void Weapons9965_Missing_enumeration_is_not_cached_and_unrelated_skills_do_not_read_weapons()
    {
        using var fixture = new Fixture();
        WriteWeaponsFixture9965(fixture);
        File.Delete(fixture.Data("weapons.xml"));
        var current = Current9965(fixture, Capture9965(fixture));
        using var evaluation = BeginEvaluation9965(Resolution9965(current), current, out var source);
        Assert.IsFalse(source.TryResolveCareerSkillSpecializationSource(Pistols9965, CharacterCareerSkillKind.Active, out _));
        using var scratch = BeginWeapons9965(source, current);
        Assert.IsTrue(source.TryResolveCareerSkillSpecializationSource(
            "30000000-0000-0000-0000-000000000001", CharacterCareerSkillKind.Active, out _));
        Assert.IsTrue(source.TryResolveCareerSkillSpecializationSource(
            "40000000-0000-0000-0000-000000000001", CharacterCareerSkillKind.Knowledge, out _));
        Assert.IsNull(Rows9965(scratch));
        for (int query = 0; query < 2; query++)
        {
            Assert.IsFalse(source.TryResolveCareerSkillSpecializationSource(Pistols9965, CharacterCareerSkillKind.Active, out _));
            Assert.IsNull(Rows9965(scratch));
        }
    }

    [TestMethod]
    public void Weapons9965_Malformed_XML_cannot_acquire_retained_authority_or_inherit_a_warm_capture()
    {
        using var fixture = new Fixture();
        WriteWeaponsFixture9965(fixture);
        var original = Capture9965(fixture);
        var expected = ReadCachedCapture9965(fixture, original);
        File.WriteAllText(fixture.Data("weapons.xml"), "<chummer><weapons>");
        Assert.IsFalse(fixture.Resolver().TryCaptureChummer5aRuleSources(fixture.Character,
            Chummer5aLegacyImportCompatibility.ProfileV2, out var rejected, out _));
        Assert.IsNull(rejected);
        AssertEquivalent9965(expected, ReadCachedCapture9965(fixture, original));
    }

    [TestMethod]
    public void Weapons9965_More_than_4096_rows_return_complete_uncached_results_each_time()
    {
        using var fixture = new Fixture();
        WriteWeaponsFixture9965(fixture);
        new XDocument(new XElement("chummer", new XElement("weapons",
            Enumerable.Range(1, 4097).Select(index => WeaponRow9965(
                $"60000000-0000-0000-0000-{index:000000000000}", $"Pistol {index:0000}", "Pistols", "SR5")))))
            .Save(fixture.Data("weapons.xml"));
        var current = Current9965(fixture, Capture9965(fixture));
        using var evaluation = BeginEvaluation9965(Resolution9965(current), current, out var source);
        var expected = ReadSpecialization9965(source, Pistols9965);
        Assert.AreEqual(4097, expected.Options.Count(row => row.Kind == CharacterCareerSkillSpecializationOptionKind.CombatWeapon));
        using var scratch = BeginWeapons9965(source, current);
        for (int query = 0; query < 2; query++)
        {
            AssertEquivalent9965(expected, ReadSpecialization9965(source, Pistols9965));
            Assert.IsNull(Rows9965(scratch));
        }
    }

    [TestMethod]
    public void Weapons9965_Observed_mutation_then_reversion_invalidates_and_clears_the_array()
    {
        using var fixture = new Fixture();
        WriteWeaponsFixture9965(fixture);
        var mutableSegments = new List<int>();
        var current = Current9965(fixture, Capture9965(fixture), mutableSegments);
        var evaluation = BeginEvaluation9965(Resolution9965(current), current, out var source);
        try
        {
            using var scratch = BeginWeapons9965(source, current);
            ReadSpecialization9965(source, Pistols9965);
            Assert.IsNotNull(Rows9965(scratch));
            mutableSegments.Add(7);
            Assert.IsFalse(source.TryResolveCareerSkillSpecializationSource(Pistols9965, CharacterCareerSkillKind.Active, out _));
            Assert.IsNull(Rows9965(scratch));
            mutableSegments.Clear();
            Assert.IsTrue(current.IsUnchanged);
            Assert.IsFalse(source.TryResolveCareerSkillSpecializationSource(Blades9965, CharacterCareerSkillKind.Active, out _));
            Assert.IsNull(Rows9965(scratch));
            Assert.ThrowsExactly<InvalidOperationException>(() => evaluation.Dispose());
        }
        finally { evaluation.Dispose(); }
    }

    [TestMethod]
    public void Weapons9965_Retired_evaluation_refuses_and_releases_warm_rows()
    {
        using var fixture = new Fixture();
        WriteWeaponsFixture9965(fixture);
        var current = Current9965(fixture, Capture9965(fixture));
        using var evaluation = BeginEvaluation9965(Resolution9965(current), current, out var source);
        using var scratch = BeginWeapons9965(source, current);
        ReadSpecialization9965(source, Pistols9965);
        Assert.IsNotNull(Rows9965(scratch));
        evaluation.Dispose();
        Assert.IsNull(scratch.GetType().GetMethod("Read", Private9965)!.Invoke(scratch, null));
        Assert.IsNull(Rows9965(scratch));
        scratch.Dispose();
        Assert.IsNull(ActiveWeapons9965(source));
        Assert.IsNull(source.GetType().GetMethod("BeginSkillSpecializationWeapons9962", Private9965)!
            .Invoke(source, [current]));
    }

    [TestMethod]
    public void Weapons9965_Foreign_current_source_and_historical_reads_cannot_open_scratch()
    {
        using var fixture = new Fixture();
        WriteWeaponsFixture9965(fixture);
        var capture = Capture9965(fixture);
        var current = Current9965(fixture, capture);
        using var evaluation = BeginEvaluation9965(Resolution9965(current), current, out var source);
        var foreignCurrent = Current9965(fixture, capture);
        Assert.IsNull(source.GetType().GetMethod("BeginSkillSpecializationWeapons9962", Private9965)!
            .Invoke(source, [foreignCurrent]));
        var otherSource = Resolution9965(current).Resolver.TryCreateContext(current.CharacterXml)!;
        var historical = capture.CreateReplayResolver().TryCreateContext(current.CharacterXml)!;
        var live = fixture.Resolver().TryCreateContext(current.CharacterXml)!;
        foreach (var candidate in new[] { otherSource, historical, live })
        {
            Assert.IsNotNull(candidate);
            Assert.IsNull(candidate.GetType().GetMethod("BeginSkillSpecializationWeapons9962", Private9965)!
                .Invoke(candidate, [current]));
            Assert.IsNull(ActiveWeapons9965(candidate));
        }
        using var scratch = BeginWeapons9965(source, current);
        ReadSpecialization9965(source, Pistols9965);
        Assert.IsNotNull(Rows9965(scratch));
    }

    private static void WriteWeaponsFixture9965(Fixture fixture)
    {
        var skills = XDocument.Load(fixture.Data("skills.xml"));
        skills.Root!.Element("skills")!.Add(new[] { (Pistols9965, "Pistols"), (Blades9965, "Blades") }
            .Select(row => new XElement("skill", new XElement("id", row.Item1), new XElement("name", row.Item2),
                new XElement("attribute", "AGI"), new XElement("category", "Combat Active"),
                new XElement("default", "True"), new XElement("skillgroup", ""), new XElement("exotic", "False"),
                new XElement("specs", new XElement("spec", "Canonical " + row.Item2)), new XElement("source", "SR5"))));
        skills.Save(fixture.Data("skills.xml"));
        new XDocument(new XElement("chummer", new XElement("categories"), new XElement("weapons",
            WeaponRow9965(Weapon9965, "Base pistol", "Pistols", "SR5"),
            WeaponRow9965("50000000-0000-0000-0000-000000000002", "Base blade", "Blades", "SR5"),
            WeaponRow9965("50000000-0000-0000-0000-000000000003", "Supplement pistol", "Pistols", "ALT"))))
            .Save(fixture.Data("weapons.xml"));
    }

    private static XElement WeaponRow9965(string id, string name, string skill, string book)
        => new("weapon", new XElement("id", id), new XElement("name", name), new XElement("category", skill),
            new XElement("useskill", skill), new XElement("source", book), new XElement("page", "1"));

    private static string WeaponNameAmend9965(string name)
        => new XElement("chummer", new XElement("weapons", new XElement("weapon", new XElement("id", Weapon9965),
            new XElement("name", new XAttribute("amendoperation", "replace"), name)))).ToString();

    private static CharacterCreationRuleSourceCapture Capture9965(Fixture fixture)
    {
        Assert.IsTrue(fixture.Resolver().TryCaptureChummer5aRuleSources(fixture.Character,
            Chummer5aLegacyImportCompatibility.ProfileV2, out var capture, out _));
        Assert.IsNotNull(capture);
        return capture;
    }

    private static CharacterCurrentRuleEnvironmentContext Current9965(Fixture fixture,
        CharacterCreationRuleSourceCapture capture, List<int>? mutableSegments = null)
    {
        var stored = new WorkspaceStoredDocument(new CharacterWorkspaceId("weapons9965"),
            new WorkspaceDocument(fixture.Character, "sr5"), 1, 1, DateTimeOffset.UnixEpoch)
        { DelegatedGmHistorySegmentStarts = mutableSegments ?? new List<int>() };
        var current = CharacterCurrentRuleEnvironmentAdmission.CreateCurrentEnvironmentContext(stored, capture,
            new XmlCharacterFileQueries(new CharacterFileService()));
        Assert.IsNotNull(current);
        return current;
    }

    private static CharacterCreationRuleSourceResolution Resolution9965(CharacterCurrentRuleEnvironmentContext current)
    {
        var resolution = current.Capture.CreateCurrentEnvironmentResolution(current.SemanticsProfile, current.SemanticsFingerprint);
        Assert.IsNotNull(resolution);
        return resolution;
    }

    private static IDisposable BeginEvaluation9965(CharacterCreationRuleSourceResolution resolution,
        CharacterCurrentRuleEnvironmentContext current, out ICharacterSourceDataContext source)
    {
        // Invoke the real issuer with its real capture, resolver and immutable queries.
        // Do not construct RetainedEvaluation2764 or install an authority flag/delegate.
        var issuer = resolution.GetType().GetInterfaces().Single(type => type.Name == "IRetainedCurrentRuleEvaluationIssuer2764");
        var evaluation = issuer.GetMethod("BeginRetainedEvaluation2764")!.Invoke(resolution, [current]) as IDisposable;
        Assert.IsNotNull(evaluation);
        try
        {
            using var operation = ((ICharacterSourceDataResolverOperationScopeFactory)resolution.Resolver).CreateOperationScope();
            var admitted = operation.TryCreateContext(current.CharacterXml);
            Assert.IsNotNull(admitted);
            source = admitted;
            return evaluation;
        }
        catch { evaluation.Dispose(); throw; }
    }

    private static IDisposable BeginWeapons9965(ICharacterSourceDataContext source, CharacterCurrentRuleEnvironmentContext current)
    {
        var scratch = source.GetType().GetMethod("BeginSkillSpecializationWeapons9962", Private9965)!
            .Invoke(source, [current]) as IDisposable;
        Assert.IsNotNull(scratch);
        return scratch;
    }

    private static object? ActiveWeapons9965(ICharacterSourceDataContext source)
        => source.GetType().GetField("_skillSpecializationWeapons9962", Private9965)!.GetValue(source);

    private static XElement[]? Rows9965(IDisposable scratch)
        => (XElement[]?)scratch.GetType().GetField("weapons", Private9965)!.GetValue(scratch);

    private static CharacterCareerSkillSpecializationSource ReadSpecialization9965(ICharacterSourceDataContext source, string id)
    {
        Assert.IsTrue(source.TryResolveCareerSkillSpecializationSource(id, CharacterCareerSkillKind.Active, out var result));
        return result;
    }

    private static void AssertEquivalent9965(CharacterCareerSkillSpecializationSource expected,
        CharacterCareerSkillSpecializationSource actual)
    {
        Assert.AreEqual(expected.RawSourceState, actual.RawSourceState);
        CollectionAssert.AreEqual(expected.Options.ToArray(), actual.Options.ToArray());
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    private static CharacterCareerSkillSpecializationSource ReadCachedCapture9965(Fixture fixture, CharacterCreationRuleSourceCapture capture)
    {
        var current = Current9965(fixture, capture);
        using var evaluation = BeginEvaluation9965(Resolution9965(current), current, out var source);
        var expected = ReadSpecialization9965(source, Pistols9965);
        using var scratch = BeginWeapons9965(source, current);
        var actual = ReadSpecialization9965(source, Pistols9965);
        AssertEquivalent9965(expected, actual);
        AssertEquivalent9965(expected, ReadSpecialization9965(source, Pistols9965));
        return actual;
    }
}
