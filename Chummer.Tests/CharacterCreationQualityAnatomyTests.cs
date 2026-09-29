using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationQualityAnatomyTests
{
    private static readonly Guid AmbidextrousId = Guid.Parse("68cfe94a-fa7e-4129-a9b9-b5d73e3ced99");
    private static readonly string DraftDigest = CharacterCreationFinalizationDigest.ComputeUtf8("anatomy-quality-draft");

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Contextual_catalog_keeps_original_source_and_rejects_unproven_anatomy(string method)
    {
        using var context = CharacterCreationFinalizationServiceTests.ReadyContext.Create(true, buildMethod: method);
        var document = context.Store.Get(context.WorkspaceId).Value!.Document;
        var prerequisite = document.AuxiliaryState.CharacterCreationPrerequisiteDraft!;
        var source = context.Resolver.TryCreateContext(document.Content)!;
        Assert.IsTrue(source.TryResolveCreationQualitiesAuthority(out var legacy));
        Assert.IsFalse(legacy.Options.Single(item => item.SourceId == AmbidextrousId).IsSelectable);
        Assert.IsTrue(source.TryResolveCreationQualitiesAuthority(prerequisite, out var current));
        var option = current.Options.Single(item => item.SourceId == AmbidextrousId);
        Assert.IsTrue(option.IsSelectable, option.DisableReasonKey);
        Assert.AreEqual(2, option.ResolvedArmCount);
        Assert.AreEqual(legacy.Options.Single(item => item.SourceId == AmbidextrousId).SourceNodeXml, option.SourceNodeXml);
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(option with { OptionDigest = string.Empty }),
            CharacterCreationQualitiesRules.ComputeOptionDigest(option));
        Assert.AreNotEqual(option.OptionDigest, CharacterCreationQualitiesRules.ComputeOptionDigest(option with { ResolvedArmCount = 4 }));
        Assert.IsFalse(JsonSerializer.Serialize(legacy.Options.Single(item => item.SourceId == AmbidextrousId)).Contains("ResolvedArmCount"));

        foreach (var unsupported in new[]
        {
            prerequisite with { AuthorityDigest = DraftDigest },
            prerequisite with { HeritageSelection = prerequisite.HeritageSelection! with { MetatypeSourceId = Guid.NewGuid().ToString("D") } },
            prerequisite with { HeritageSelection = prerequisite.HeritageSelection! with { MetavariantSourceId = "abfd41e2-3db6-4c02-b404-11f92e426279" } },
            prerequisite with { HeritageSelection = prerequisite.HeritageSelection! with { MetatypeSourceNodeDigest = DraftDigest } },
            prerequisite with { TalentSelection = prerequisite.TalentSelection! with { GrantedQualities = ["Shiva Arms (Pair)"] } }
        })
        {
            Assert.IsTrue(source.TryResolveCreationQualitiesAuthority(unsupported, out var rejected));
            var unavailable = rejected.Options.Single(item => item.SourceId == AmbidextrousId);
            Assert.IsFalse(unavailable.IsSelectable);
            Assert.IsNull(unavailable.ResolvedArmCount);
        }
    }

    [TestMethod]
    [DataRow(2, 1)]
    [DataRow(4, 3)]
    [DataRow(6, 5)]
    [DataRow(101, 100)]
    public void Original_ambidextrous_limit_uses_explicit_anatomy_without_rewriting_source(int arms, int maximum)
    {
        string source = OriginalAmbidextrous();
        Assert.AreEqual("{arm} - 1", XElement.Parse(source).Element("limit")!.Value);
        var selection = Selection(source, maximum);
        Assert.IsTrue(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(source, arms));
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            selection, DraftDigest, out var qualities, out var improvements, arms));
        Assert.HasCount(maximum, qualities);
        Assert.HasCount(maximum, improvements);
        Assert.HasCount(maximum, qualities.Select(item => item.Element("guid")!.Value).Distinct().ToArray());
        for (int index = 0; index < maximum; index++)
        {
            Assert.AreEqual(AmbidextrousId.ToString("D"), qualities[index].Element("sourceid")!.Value);
            Assert.AreEqual("4", qualities[index].Element("bp")!.Value);
            Assert.AreEqual("Selected", qualities[index].Element("qualitysource")!.Value);
            Assert.IsNotNull(qualities[index].Element("bonus")!.Element("ambidextrous"));
            Assert.AreEqual("Ambidextrous", improvements[index].Element("improvementttype")!.Value);
            Assert.AreEqual("Quality", improvements[index].Element("improvementsource")!.Value);
            Assert.AreEqual(qualities[index].Element("guid")!.Value, improvements[index].Element("sourcename")!.Value);
            Assert.AreEqual("1", improvements[index].Element("enabled")!.Value);
        }
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            selection, DraftDigest, out var reopenedQualities, out var reopenedImprovements, arms));
        CollectionAssert.AreEqual(qualities.Select(item => item.ToString()).ToArray(),
            reopenedQualities.Select(item => item.ToString()).ToArray());
        CollectionAssert.AreEqual(improvements.Select(item => item.ToString()).ToArray(),
            reopenedImprovements.Select(item => item.ToString()).ToArray());
        Assert.AreEqual(source, selection.SourceNodeXml);
        Assert.AreEqual(CharacterCreationQualitiesRules.ComputeSourceNodeDigest(source), selection.SourceNodeDigest);
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            Selection(source, maximum + 1), DraftDigest, out var rejectedQualities, out var rejectedImprovements, arms));
        Assert.HasCount(0, rejectedQualities);
        Assert.HasCount(0, rejectedImprovements);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(102)]
    [DataRow(int.MaxValue)]
    [DataRow(int.MinValue)]
    public void Missing_or_unbounded_anatomy_does_not_admit_an_arm_limited_quality(int? arms)
    {
        string source = OriginalAmbidextrous();
        Assert.IsFalse(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(source, arms));
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            Selection(source, 1), DraftDigest, out var qualities, out var improvements, arms));
        Assert.HasCount(0, qualities);
        Assert.HasCount(0, improvements);
        // Existing callers have no implicit two-arm fallback.
        Assert.IsFalse(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(source));
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            Selection(source, 1), DraftDigest, out _, out _));
    }

    [TestMethod]
    [DataRow("{arm} + 1")]
    [DataRow("{leg} - 1")]
    [DataRow("{arm} - 1 + 100")]
    [DataRow("{arm} - 1 or true()")]
    public void Anatomy_support_does_not_admit_unimplemented_expressions(string expression)
    {
        XElement node = XElement.Parse(OriginalAmbidextrous());
        node.Element("limit")!.Value = expression;
        string source = node.ToString(SaveOptions.DisableFormatting);
        Assert.IsFalse(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(source, 2));
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
            Selection(source, 1), DraftDigest, out _, out _, 2));
    }

    [TestMethod]
    public void Anatomy_does_not_bypass_source_digest_cost_or_rating_checks()
    {
        var selection = Selection(OriginalAmbidextrous(), 1);
        foreach (var invalid in new[]
        {
            selection with { SourceNodeDigest = DraftDigest },
            selection with { KarmaCost = 0 },
            selection with { Rating = 0, KarmaCost = 0 },
            selection with { Rating = 2, KarmaCost = 8 },
            selection with { IsFreeOrGranted = true }
        })
            Assert.IsFalse(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(
                invalid, DraftDigest, out _, out _, 2));
    }

    private static CharacterCreationQualitySelection Selection(string source, int rating) => new(
        $"quality:{AmbidextrousId:D}:rating:{rating.ToString(CultureInfo.InvariantCulture)}",
        AmbidextrousId, AmbidextrousId.ToString("D"), "Ambidextrous", CharacterCreationQualityType.Positive,
        rating, checked(4 * rating), false, true, true, false, null, null,
        [$"qualities.xml#quality:{AmbidextrousId:D}"], source,
        CharacterCreationQualitiesRules.ComputeSourceNodeDigest(source), DraftDigest);

    private static string OriginalAmbidextrous()
    {
        DirectoryInfo? directory = new(AppDomain.CurrentDomain.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "Chummer", "data", "qualities.xml");
            if (File.Exists(path))
                return XDocument.Load(path).Root!.Element("qualities")!.Elements("quality")
                    .Single(item => item.Element("id")?.Value == AmbidextrousId.ToString("D"))
                    .ToString(SaveOptions.DisableFormatting);
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Canonical qualities.xml was not found.");
    }
}
