using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationSourceCaptureTests
{
    private const string CharacterXml = "<character><settings>capture.xml</settings></character>";

    [TestMethod]
    public void Every_captured_domain_and_complete_catalog_remain_available_without_live_reads()
    {
        var source = new Source();
        var catalog = new Catalog();
        WorkspaceContinuationSourceCapture capture = Capture(source, catalog);
        source.ForbidReads = true;
        catalog.ForbidReads = true;
        ICharacterSourceDataContext frozen = Context(capture);

        Assert.IsTrue(frozen.TryResolveCreationSourceProfile(out var profile)); Same(source.Profile, profile);
        Assert.IsTrue(frozen.TryResolveCreationMetatypeCatalog(out var metatypes)); Same(source.Metatypes, metatypes);
        Assert.IsTrue(frozen.TryResolveCreationPrerequisiteAuthority(out var prerequisite)); Same(source.Prerequisite, prerequisite);
        Assert.IsTrue(frozen.TryResolveCreationSkillsAuthority(out var skills)); Same(source.Skills, skills);
        Assert.IsTrue(frozen.TryResolveCreationQualitiesAuthority(out var qualities)); Same(source.Qualities, qualities);
        Assert.IsTrue(frozen.TryResolveCreationMagicResonanceAuthority(out var magic)); Same(source.Magic, magic);
        Assert.IsTrue(frozen.TryResolveCreationResourcesAuthority(out var resources)); Same(source.Resources, resources);
        Assert.IsTrue(frozen.TryResolveCreationGearAuthority(out var gear)); Same(source.Gear, gear);
        Assert.IsTrue(frozen.TryResolveCreationLifestylesAuthority(out var lifestyles)); Same(source.Lifestyles, lifestyles);
        Assert.IsTrue(frozen.TryResolveCareerReputationSettings(out var reputation, out string rawRules));
        Same(source.Reputation, reputation);
        Assert.AreEqual(source.ReputationRawRules, rawRules);
        Assert.IsTrue(frozen.TryIsBookEnabled("sr5", out bool enabled));
        Assert.IsTrue(enabled);
        Assert.IsTrue(frozen.TryIsBookEnabled("absent-book", out enabled));
        Assert.IsFalse(enabled);
        Assert.IsFalse(frozen.TryIsBookEnabled(" ", out enabled));
        Assert.IsFalse(enabled);

        Assert.IsTrue(capture.LifeModulesResolved);
        Same(catalog.Authority, capture.LifeModules.GetAuthority());
        Same(catalog.Stages, capture.LifeModules.GetStages());
        Same(catalog.Modules, capture.LifeModules.GetModules());
        Same(catalog.Options, capture.LifeModules.GetOptionProjections(null, source.Books));
        foreach (LifeModuleStageDto stage in catalog.Stages)
        {
            Same(catalog.Modules.Where(item => item.Stage == stage.Name).ToArray(),
                capture.LifeModules.GetModules(stage.Name));
            Same(catalog.Options.Where(item => item.StageId == stage.Name).ToArray(),
                capture.LifeModules.GetOptionProjections(stage.Name, source.Books));
        }
        Assert.AreEqual(10, source.ReadCount);
    }

    [TestMethod]
    public void Original_and_returned_nested_collections_cannot_mutate_an_existing_capture()
    {
        var source = new Source();
        var catalog = new Catalog();
        WorkspaceContinuationSourceCapture capture = Capture(source, catalog);
        string digest = capture.Digest;
        source.Books.Add("forged-book");
        source.SkillAnchors[0] = "changed-source";
        catalog.EffectParameters["rating"] = "99";
        catalog.Stages.Add(new(99, "Injected"));

        ICharacterSourceDataContext frozen = Context(capture);
        Assert.IsTrue(frozen.TryResolveCreationSourceProfile(out var profile));
        CollectionAssert.AreEqual(new[] { "SR5" }, profile.EnabledSourcebooks.ToArray());
        ((IList<string>)profile.EnabledSourcebooks).Add("returned-mutation");
        Assert.IsTrue(frozen.TryResolveCreationSkillsAuthority(out var skills));
        Assert.AreEqual("skills.xml", skills.SourceAnchorIds[0]);
        ((IList<string>)skills.SourceAnchorIds)[0] = "returned-mutation";
        var option = capture.LifeModules.GetOptionProjections("Nationality", ["SR5"])[0];
        Assert.AreEqual("1", option.Effects[0].Parameters["rating"]);
        ((IDictionary<string, string>)option.Effects[0].Parameters)["rating"] = "returned-mutation";
        ((IList<LifeModuleStageDto>)capture.LifeModules.GetStages()).Add(new(100, "Returned"));

        ICharacterSourceDataContext second = Context(capture);
        Assert.IsTrue(second.TryResolveCreationSourceProfile(out var secondProfile));
        CollectionAssert.AreEqual(new[] { "SR5" }, secondProfile.EnabledSourcebooks.ToArray());
        Assert.IsTrue(second.TryResolveCreationSkillsAuthority(out var secondSkills));
        Assert.AreEqual("skills.xml", secondSkills.SourceAnchorIds[0]);
        Assert.AreEqual("1", capture.LifeModules.GetOptionProjections("Nationality", ["SR5"])[0].Effects[0].Parameters["rating"]);
        Assert.HasCount(2, capture.LifeModules.GetStages());
        Assert.AreEqual(digest, capture.Digest);
        Assert.AreNotEqual(digest, Capture(source, catalog).Digest);
    }

    [TestMethod]
    [DataRow("profile")]
    [DataRow("metatypes")]
    [DataRow("prerequisite")]
    [DataRow("skills")]
    [DataRow("qualities")]
    [DataRow("magic")]
    [DataRow("resources")]
    [DataRow("gear")]
    [DataRow("lifestyles")]
    [DataRow("reputation-settings")]
    [DataRow("reputation-rules")]
    [DataRow("catalog-authority")]
    [DataRow("catalog-stages")]
    [DataRow("catalog-modules")]
    [DataRow("catalog-later-stage-options")]
    public void Every_source_domain_changes_the_capture_digest_even_without_changing_its_claimed_digest(string domain)
    {
        var source = new Source();
        var catalog = new Catalog();
        string before = Capture(source, catalog).Digest;
        Assert.AreEqual(before, Capture(source, catalog).Digest);
        switch (domain)
        {
            case "profile": source.Profile = source.Profile with { BuildPoints = 99 }; break;
            case "metatypes": source.Metatypes = source.Metatypes with { Blockers = ["changed"] }; break;
            case "prerequisite": source.Prerequisite = source.Prerequisite with { KarmaAttribute = 99 }; break;
            case "skills": source.Skills = source.Skills with { MaxActiveSkillRatingCreate = 99 }; break;
            case "qualities": source.Qualities = source.Qualities with { Blockers = ["changed"] }; break;
            case "magic": source.Magic = source.Magic with { Blockers = ["changed"] }; break;
            case "resources": source.Resources = source.Resources with { KarmaToNuyenRate = 99 }; break;
            case "gear": source.Gear = source.Gear with { MaximumQuantityPerLine = 99 }; break;
            case "lifestyles": source.Lifestyles = source.Lifestyles with { TrustFundLevel = 99 }; break;
            case "reputation-settings": source.Reputation = new(false); break;
            case "reputation-rules": source.ReputationRawRules = "changed"; break;
            case "catalog-authority": catalog.Authority = catalog.Authority with { SourceAnchorIds = ["changed"] }; break;
            case "catalog-stages": catalog.Stages[1] = catalog.Stages[1] with { Order = 99 }; break;
            case "catalog-modules": catalog.Modules[1] = catalog.Modules[1] with { Story = "changed" }; break;
            case "catalog-later-stage-options": catalog.Options[1] = catalog.Options[1] with { KarmaCost = 99 }; break;
            default: Assert.Fail("Unknown test domain."); break;
        }
        Assert.AreNotEqual(before, Capture(source, catalog).Digest, domain);
    }

    [TestMethod]
    public void Unavailable_flags_and_optional_source_failures_are_preserved_without_live_fallback()
    {
        var source = new Source();
        var catalog = new Catalog();
        string availableDigest = Capture(source, catalog).Digest;
        source.SkillsResolved = false;
        WorkspaceContinuationSourceCapture unavailable = Capture(source, catalog);
        Assert.AreNotEqual(availableDigest, unavailable.Digest);
        Assert.IsFalse(Context(unavailable).TryResolveCreationSkillsAuthority(out var unresolvedSkills));
        Same(source.Skills, unresolvedSkills);
        Assert.IsTrue(Context(unavailable).TryResolveCreationGearAuthority(out _));

        source.ThrowSkills = true;
        WorkspaceContinuationSourceCapture failed = Capture(source, catalog);
        Assert.IsFalse(Context(failed).TryResolveCreationSkillsAuthority(out var failedSkills));
        Same(CharacterCreationSkillsAuthority.Unavailable, failedSkills);
        Assert.IsTrue(Context(failed).TryResolveCreationGearAuthority(out _));

        source.ProfileResolved = false;
        WorkspaceContinuationSourceCapture noProfile = Capture(source, catalog);
        Assert.IsFalse(Context(noProfile).TryIsBookEnabled("SR5", out bool enabled));
        Assert.IsFalse(enabled);
        Assert.IsFalse(noProfile.LifeModulesResolved);
        Assert.ThrowsExactly<InvalidOperationException>(() => noProfile.LifeModules.GetAuthority());
        source.ProfileResolved = true;
        catalog.ThrowStage = "Further Education";
        WorkspaceContinuationSourceCapture partialCatalog = Capture(source, catalog);
        Assert.IsFalse(partialCatalog.LifeModulesResolved);
        Assert.ThrowsExactly<InvalidOperationException>(() => partialCatalog.LifeModules.GetModules());
        Assert.IsTrue(Context(partialCatalog).TryResolveCreationGearAuthority(out _));
    }

    [TestMethod]
    public void Wrong_xml_undiscovered_catalog_scopes_and_uncaptured_context_methods_fail_closed()
    {
        var source = new Source();
        var catalog = new Catalog();
        WorkspaceContinuationSourceCapture capture = Capture(source, catalog);
        source.ForbidReads = true;
        catalog.ForbidReads = true;
        Assert.IsNull(capture.CreateResolver().TryCreateContext(CharacterXml + " "));
        Assert.IsNull(capture.CreateResolver().TryCreateContext(CharacterXml.ToUpperInvariant()));
        Assert.ThrowsExactly<InvalidOperationException>(() => capture.LifeModules.GetModules("unknown"));
        Assert.ThrowsExactly<InvalidOperationException>(() => capture.LifeModules.GetOptionProjections("unknown", ["SR5"]));
        Assert.ThrowsExactly<InvalidOperationException>(() => capture.LifeModules.GetOptionProjections(" Nationality", ["SR5"]));
        Assert.ThrowsExactly<InvalidOperationException>(() => capture.LifeModules.GetOptionProjections("Nationality"));
        Assert.ThrowsExactly<InvalidOperationException>(() => capture.LifeModules.GetOptionProjections("Nationality", []));
        Assert.ThrowsExactly<InvalidOperationException>(() => capture.LifeModules.GetOptionProjections("Nationality", ["SR5", "unknown"]));
        Assert.HasCount(1, capture.LifeModules.GetOptionProjections("Nationality", ["sr5"]));
        ICharacterSourceDataContext frozen = Context(capture);
        Assert.IsFalse(frozen.TryResolveCyberwareGradeDeviceRating("standard", "cyberware", out _));
        Assert.IsFalse(frozen.TryResolveVehicleModBonuses("id", "vehicle", out _));
        Assert.IsFalse(frozen.TryResolveMaxNuyenDecimals(out _));
        Assert.IsFalse(frozen.TryResolveActiveSkillSource("id", out _));
        Assert.IsFalse(frozen.TryResolveTraditionDrainExpressions(out _));
        Assert.IsFalse(frozen.TryResolveGroupMembershipKarmaCosts(out _, out _));
        Assert.AreEqual(10, source.ReadCount);
        Assert.AreNotEqual(capture.Digest, Capture(new Source(), new Catalog(), CharacterXml + " ").Digest);
    }

    private static WorkspaceContinuationSourceCapture Capture(Source source, Catalog catalog, string xml = CharacterXml)
    {
        Assert.IsTrue(WorkspaceContinuationSourceCapture.TryCapture(source, xml, catalog, out var capture));
        Assert.IsNotNull(capture);
        return capture;
    }

    private static ICharacterSourceDataContext Context(WorkspaceContinuationSourceCapture capture)
    {
        var context = capture.CreateResolver().TryCreateContext(CharacterXml);
        Assert.IsNotNull(context);
        return context;
    }

    private static void Same<T>(T expected, T actual) => Assert.IsTrue(
        CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, actual));

    // These are source-capture fixtures, not claimed mechanically admitted
    // characters. Actual domain validators independently inspect each authority.
    private sealed class Source : ICharacterSourceDataContext
    {
        public List<string> Books { get; } = ["SR5"];
        public string[] SkillAnchors { get; } = ["skills.xml"];
        public CharacterCreationSourceProfileAuthority Profile { get; set; }
        public CharacterCreationMetatypeCatalogAuthority Metatypes { get; set; } = CharacterCreationMetatypeCatalogAuthority.Unavailable;
        public CharacterCreationPrerequisiteAuthority Prerequisite { get; set; } = CharacterCreationPrerequisiteAuthority.Unavailable;
        public CharacterCreationSkillsAuthority Skills { get; set; }
        public CharacterCreationQualitiesAuthority Qualities { get; set; } = CharacterCreationQualitiesAuthority.Unavailable;
        public CharacterCreationMagicResonanceAuthority Magic { get; set; } = CharacterCreationMagicResonanceAuthority.Unavailable;
        public CharacterCreationResourcesAuthority Resources { get; set; } = CharacterCreationResourcesAuthority.Unavailable;
        public CharacterCreationGearAuthority Gear { get; set; } = CharacterCreationGearAuthority.Unavailable;
        public CharacterCreationLifestylesAuthority Lifestyles { get; set; } = CharacterCreationLifestylesAuthority.Unavailable;
        public CharacterCareerReputationSettings Reputation { get; set; } = new(true);
        public string ReputationRawRules { get; set; } = "<settings><publicawareness>true</publicawareness></settings>";
        public bool ForbidReads { get; set; }
        public bool ProfileResolved { get; set; } = true;
        public bool SkillsResolved { get; set; } = true;
        public bool ThrowSkills { get; set; }
        public int ReadCount { get; private set; }

        public Source()
        {
            Profile = CharacterCreationSourceProfileAuthority.Unavailable with { EnabledSourcebooks = Books };
            Skills = CharacterCreationSkillsAuthority.Unavailable with { SourceAnchorIds = SkillAnchors };
        }

        private bool Read<T>(T value, out T result)
        {
            if (ForbidReads) throw new AssertFailedException("Frozen query reached the live source.");
            ReadCount++;
            result = value;
            return true;
        }

        public bool TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority value) => Read(Profile, out value) && ProfileResolved;
        public bool TryResolveCreationMetatypeCatalog(out CharacterCreationMetatypeCatalogAuthority value) => Read(Metatypes, out value);
        public bool TryResolveCreationPrerequisiteAuthority(out CharacterCreationPrerequisiteAuthority value) => Read(Prerequisite, out value);
        public bool TryResolveCreationSkillsAuthority(out CharacterCreationSkillsAuthority value)
        {
            Read(Skills, out value);
            if (ThrowSkills) throw new IOException("Missing optional skills source.");
            return SkillsResolved;
        }
        public bool TryResolveCreationQualitiesAuthority(out CharacterCreationQualitiesAuthority value) => Read(Qualities, out value);
        public bool TryResolveCreationMagicResonanceAuthority(out CharacterCreationMagicResonanceAuthority value) => Read(Magic, out value);
        public bool TryResolveCreationResourcesAuthority(out CharacterCreationResourcesAuthority value) => Read(Resources, out value);
        public bool TryResolveCreationGearAuthority(out CharacterCreationGearAuthority value) => Read(Gear, out value);
        public bool TryResolveCreationLifestylesAuthority(out CharacterCreationLifestylesAuthority value) => Read(Lifestyles, out value);
        public bool TryResolveCareerReputationSettings(out CharacterCareerReputationSettings value, out string rawRules)
        {
            rawRules = ReputationRawRules;
            return Read(Reputation, out value);
        }
        public bool TryResolveCyberwareGradeDeviceRating(string gradeName, string improvementSource, out int rating) => Read(99, out rating);
        public bool TryResolveVehicleModBonuses(string sourceId, string name, out CharacterVehicleModSourceBonuses bonuses) => Read(CharacterVehicleModSourceBonuses.Empty, out bonuses);
        public bool TryResolveMaxNuyenDecimals(out int places) => Read(99, out places);
    }

    private sealed class Catalog : ILifeModulesCatalogService
    {
        public LifeModuleCatalogAuthorityDto Authority { get; set; } = new(LifeModuleJourneySchemas.CatalogAuthorityV1,
            "sha256:" + new string('a', 64), ["lifemodules.xml"]);
        public List<LifeModuleStageDto> Stages { get; } = [new(1, "Nationality"), new(4, "Further Education")];
        public List<LifeModuleSummaryDto> Modules { get; } =
            [new("origin", "Nationality", "Origin", "10", "SR5", "1", "story"),
                new("school", "Further Education", "School", "20", "SR5", "2", "story")];
        public Dictionary<string, string> EffectParameters { get; } = new(StringComparer.Ordinal) { ["rating"] = "1" };
        public List<LifeModuleLegalOptionDto> Options { get; }
        public bool ForbidReads { get; set; }
        public string? ThrowStage { get; set; }

        public Catalog()
        {
            LifeModuleEffectProjectionDto effect = new("effect", "attribute", "BOD", "0", "1", null,
                0, ["effect-anchor"], EffectParameters, "<effect/>", true, null);
            Options = [Option("origin", "Nationality", 1) with { Effects = [effect] },
                Option("school", "Further Education", 4)];
        }

        private void Check(string? stage = null)
        {
            if (ForbidReads) throw new AssertFailedException("Frozen query reached the live catalog.");
            if (ThrowStage is not null && stage == ThrowStage) throw new IOException("Missing catalog stage.");
        }
        public LifeModuleCatalogAuthorityDto GetAuthority() { Check(); return Authority; }
        public IReadOnlyList<LifeModuleStageDto> GetStages() { Check(); return Stages; }
        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
        {
            Check(stage);
            return stage is null ? Modules : Modules.Where(item => item.Stage == stage).ToArray();
        }
        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(string? stage = null, IReadOnlyCollection<string>? enabledSources = null)
        {
            Check(stage);
            return Options.Where(item => (stage is null || item.StageId == stage)
                && enabledSources is not null && enabledSources.Contains(item.Source, StringComparer.OrdinalIgnoreCase)).ToArray();
        }
        private static LifeModuleLegalOptionDto Option(string id, string stage, int order) => new(
            id, order, id, 10, "SR5", 1, "story", true, [], [], [], [], ["module-anchor"], stage,
            false, "10", true, "1", []);
    }
}
