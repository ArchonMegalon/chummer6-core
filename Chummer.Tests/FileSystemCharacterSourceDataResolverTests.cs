#nullable enable annotations

using System;
using System.IO;
using Chummer.Application.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class FileSystemCharacterSourceDataResolverTests
{
    private const string SettingsId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string VehicleModId = "f89a112e-600a-4278-8731-9b14cf3737c9";

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

    private static void WriteBaseContent(string root, string customDataSetting)
    {
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(
            Path.Combine(data, "settings.xml"),
            $"<chummer><settings><setting><id>{SettingsId}</id><nuyenformat>#,0.###</nuyenformat><karmajoingroup>5</karmajoingroup><karmaleavegroup>1</karmaleavegroup><books><book>SR5</book><book>SG</book></books><customdatadirectorynames>{customDataSetting}</customdatadirectorynames></setting></settings></chummer>");
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

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-source-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
