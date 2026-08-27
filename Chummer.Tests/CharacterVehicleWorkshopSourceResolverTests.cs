using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterVehicleWorkshopSourceResolverTests
{
    private const string StandardProfileId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string FullHouseProfileId = "67e25032-2a4e-42ca-97fa-69f7f608236c";

    [TestMethod]
    public void Effective_profile_projects_enabled_vehicle_drone_mod_and_mount_rows_with_exact_binding()
    {
        ICharacterSourceDataContext context = CreateContext(FullHouseCharacterXml());

        Assert.IsTrue(context.TryResolveVehicleWorkshopCatalog(out CharacterVehicleWorkshopCatalog catalog));
        Assert.IsTrue(catalog.Binding.Exact);
        Assert.AreEqual(CharacterVehicleWorkshopRules.RulesetId, catalog.Binding.RulesetId);
        Assert.AreEqual(FullHouseProfileId, catalog.Binding.ProfileId);
        Assert.AreEqual(CharacterVehicleWorkshopRules.SemanticsVersion, catalog.Binding.SemanticsVersion);
        Assert.IsTrue(CharacterVehicleWorkshopRules.IsCanonicalDigest(catalog.Binding.ProfileDigest));
        Assert.IsTrue(CharacterVehicleWorkshopRules.IsCanonicalDigest(catalog.Binding.VehiclesDigest));
        Assert.IsTrue(CharacterVehicleWorkshopRules.IsCanonicalDigest(catalog.Binding.WeaponsDigest));
        Assert.IsTrue(CharacterVehicleWorkshopRules.IsCanonicalDigest(catalog.Binding.OverlayDigest));
        Assert.AreEqual(
            CharacterVehicleWorkshopRules.ComputeCatalogDigest(catalog),
            catalog.DeclaredCatalogDigest);
        Assert.IsGreaterThan(100, catalog.Chassis.Count);
        Assert.IsGreaterThan(100, catalog.Modifications.Count);
        Assert.IsGreaterThan(10, catalog.WeaponMountComponents.Count);
        Assert.IsTrue(catalog.Chassis.Any(item => item.Kind == CharacterVehicleChassisKind.Vehicle));
        Assert.IsTrue(catalog.Chassis.Any(item => item.Kind == CharacterVehicleChassisKind.Drone));
        Assert.IsTrue(catalog.Chassis.Any(item =>
            item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact));
        Assert.IsTrue(catalog.Modifications.Any(item =>
            item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact));
        Assert.IsTrue(catalog.WeaponMountComponents.Any(item =>
            item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact));
    }

    [TestMethod]
    public void Factory_children_dynamic_semantics_and_mount_constraints_remain_visible_but_fail_closed()
    {
        ICharacterSourceDataContext context = CreateContext(FullHouseCharacterXml());
        Assert.IsTrue(context.TryResolveVehicleWorkshopCatalog(out CharacterVehicleWorkshopCatalog catalog));

        CharacterVehicleWorkshopChassisEntry scoot = catalog.Chassis.Single(item =>
            item.Name == "Dodge Scoot (Scooter)");
        Assert.AreEqual(CharacterVehicleWorkshopProjectionStatus.Unsupported, scoot.ProjectionStatus);
        StringAssert.Contains(scoot.UnsupportedReason, "gears");
        StringAssert.Contains(scoot.UnsupportedReason, "mods");

        CharacterVehicleWorkshopModificationEntry smartTires = catalog.Modifications.Single(item =>
            item.Name == "Smart Tires");
        Assert.AreEqual(CharacterVehicleWorkshopProjectionStatus.Unsupported, smartTires.ProjectionStatus);
        StringAssert.Contains(smartTires.UnsupportedReason, "rating");
        StringAssert.Contains(smartTires.UnsupportedReason, "required");

        CharacterVehicleWeaponMountComponentEntry lightMount = catalog.WeaponMountComponents.Single(item =>
            item.Name == "Light" && item.Kind == CharacterVehicleWeaponMountComponentKind.Size);
        Assert.AreEqual(CharacterVehicleWorkshopProjectionStatus.Unsupported, lightMount.ProjectionStatus);
        StringAssert.Contains(lightMount.UnsupportedReason, "forbidden");
        StringAssert.Contains(lightMount.UnsupportedReason, "weaponcategories");
    }

    [TestMethod]
    public void Projected_exact_stock_chassis_can_be_prepared_and_quoted_without_caller_catalog_invention()
    {
        string characterXml = FullHouseCharacterXml(created: true, nuyen: 100_000_000m);
        ICharacterSourceDataContext context = CreateContext(characterXml);
        Assert.IsTrue(context.TryResolveVehicleWorkshopCatalog(out CharacterVehicleWorkshopCatalog catalog));
        CharacterVehicleWorkshopChassisEntry chassis = catalog.Chassis.First(item =>
            item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact);
        var authority = new CharacterVehicleWorkshopAuthority();

        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(characterXml, 41, catalog);
        var selection = new CharacterVehicleWorkshopSelection(
            chassis.SourceId,
            new CharacterVehicleInstanceId(Guid.Parse("30000000-0000-4000-8000-000000000041")),
            string.Empty,
            string.Empty,
            [],
            []);
        CharacterVehicleWorkshopQuote quote = authority.Quote(preparation, selection);

        Assert.IsTrue(preparation.Exact, string.Join("; ", preparation.Blockers));
        Assert.IsTrue(quote.Exact, string.Join("; ", quote.Blockers));
        Assert.AreEqual(chassis.Cost, quote.TotalCost);
        Assert.AreEqual(-chassis.Cost, quote.NuyenDelta);
        Assert.AreEqual(chassis.ModificationSlots, quote.SlotsRemaining);
        Assert.IsNotEmpty(preparation.UnsupportedRows);
    }

    [TestMethod]
    public void Disabled_sourcebooks_are_removed_instead_of_becoming_selectable_fallbacks()
    {
        ICharacterSourceDataContext context = CreateContext(CharacterXml(StandardProfileId));

        Assert.IsTrue(context.TryResolveVehicleWorkshopCatalog(out CharacterVehicleWorkshopCatalog catalog));
        Assert.IsFalse(catalog.Chassis.Any(item => item.SourceBook == "R5"));
        Assert.IsFalse(catalog.Modifications.Any(item => item.SourceBook == "R5"));
        Assert.IsFalse(catalog.WeaponMountComponents.Any(item => item.SourceBook == "R5"));
        Assert.IsTrue(catalog.Chassis.Any(item => item.SourceBook == "SR5"));
    }

    private static ICharacterSourceDataContext CreateContext(string characterXml)
    {
        string root = FindCoreRoot();
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(root, root, null));
        return resolver.TryCreateContext(characterXml)
            ?? throw new InvalidOperationException("The exact saved source profile did not resolve.");
    }

    private static string CharacterXml(string profileId)
        => $"<character><settings>{profileId}</settings></character>";

    private static string FullHouseCharacterXml(bool created = false, decimal nuyen = 0m)
        => $"""
           <character>
             <settings>{FullHouseProfileId}</settings>
             <created>{created.ToString().ToLowerInvariant()}</created>
             <nuyen>{nuyen.ToString(System.Globalization.CultureInfo.InvariantCulture)}</nuyen>
             <customdatadirectorynames>
               <directoryname>Chrome Flesh Stealth Errata</directoryname>
               <directoryname>Dark Terrors Stealth Errata</directoryname>
               <directoryname>Forbidden Arcana Stealth Errata</directoryname>
               <directoryname>No Future Stealth Errata</directoryname>
             </customdatadirectorynames>
             <improvements />
             <vehicles />
             <expenses />
           </character>
           """;

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
