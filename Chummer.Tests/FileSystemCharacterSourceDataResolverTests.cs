#nullable enable annotations

using System;
using System.IO;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
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
        CharacterCreationPriorityOptionProjection heritageE = authority.Options.Single(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && option.Rank == "E");
        CharacterCreationPriorityHeritageOptionProjection human = heritageE.HeritageOptions.Single(option =>
            option.MetatypeName == "Human" && option.MetavariantName is null);
        Assert.IsTrue(human.IsEnabled, string.Join(",", human.Blockers));
        Assert.AreEqual(1, human.SpecialAttributePoints);
        Assert.IsFalse(human.HalvesNormalAttributePoints);
        Assert.HasCount(13, human.Attributes);
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
                CharacterCreationPrerequisiteBlockers.PrioritiesSourceDrift);
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
                CharacterCreationPrerequisiteBlockers.CustomDataDrift);
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
            $"<chummer><settings><setting><id>{SettingsId}</id><nuyenformat>#,0.###</nuyenformat><karmajoingroup>5</karmajoingroup><karmaleavegroup>1</karmaleavegroup><nuyenperbpwftp>1500</nuyenperbpwftp><nuyenperbpwftm>2000</nuyenperbpwftm><books><book>SR5</book><book>SG</book></books><customdatadirectorynames>{customDataSetting}</customdatadirectorynames>{buildAuthorityXml}<alternatemetatypeattributekarma>False</alternatemetatypeattributekarma><reverseattributepriorityorder>False</reverseattributepriorityorder><karmacost><karmaattribute>5</karmaattribute></karmacost></setting></settings></chummer>");
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
            Path.Combine(data, "cyberware.xml"),
            "<chummer><grades><grade><name>Standard</name><devicerating>4</devicerating></grade><grade><name>Alphaware</name></grade></grades></chummer>");
        File.WriteAllText(
            Path.Combine(data, "bioware.xml"),
            "<chummer><grades><grade><name>Standard</name><devicerating>2</devicerating></grade></grades></chummer>");
        File.WriteAllText(
            Path.Combine(data, "vehicles.xml"),
            $"<chummer><mods><mod><id>{VehicleModId}</id><name>Gyro-Stabilization</name><bonus><body>Rating + 1</body><devicerating>2</devicerating><matrixcmbonus>3</matrixcmbonus></bonus><wirelessbonus><body>1</body><devicerating>4</devicerating><matrixcmbonus>5</matrixcmbonus></wirelessbonus></mod></mods></chummer>");
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

    private static void WritePriorityFixture(string root)
    {
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
        int sequence = 1;
        string rows = string.Concat(categories.SelectMany(category => ranks.Select(rank =>
        {
            string attributes = category == "Attributes"
                ? $"<attributes>{attributePoints[rank]}</attributes>"
                : category == "Heritage"
                    ? "<metatypes><metatype><name>Human</name><value>1</value><karma>0</karma></metatype></metatypes>"
                    : category == "Talent"
                        ? "<talents><talent><name>Mundane</name><value>Mundane</value><forbidden><oneof><metatype>A.I.</metatype></oneof></forbidden></talent></talents>"
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
