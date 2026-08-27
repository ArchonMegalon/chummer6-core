using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterVehicleWorkshopAuthorityTests
{
    private const string ProfileId = "sr5-standard";
    private static readonly CharacterVehicleChassisSourceId s_StockChassis = new(Guid.Parse("10000000-0000-4000-8000-000000000001"));
    private static readonly CharacterVehicleChassisSourceId s_CustomChassis = new(Guid.Parse("10000000-0000-4000-8000-000000000002"));
    private static readonly CharacterVehicleModificationSourceId s_ArmorMod = new(Guid.Parse("20000000-0000-4000-8000-000000000001"));
    private static readonly CharacterVehicleModificationSourceId s_UnsupportedMod = new(Guid.Parse("20000000-0000-4000-8000-000000000002"));
    private static readonly CharacterVehicleModificationSourceId s_FixedMod = new(Guid.Parse("20000000-0000-4000-8000-000000000003"));
    private static readonly CharacterVehicleFactoryGearSourceId s_SensorArraySource = new(Guid.Parse("2ca81a10-d0f7-4b39-ac93-a84f2f69f9d9"));
    private static readonly CharacterVehicleInstanceId s_VehicleInstance = new(Guid.Parse("30000000-0000-4000-8000-000000000001"));
    private static readonly CharacterVehicleModificationInstanceId s_ModInstance = new(Guid.Parse("30000000-0000-4000-8000-000000000002"));
    private static readonly CharacterVehicleWeaponMountInstanceId s_MountInstance = new(Guid.Parse("30000000-0000-4000-8000-000000000003"));
    private static readonly Guid s_ExpenseId = Guid.Parse("30000000-0000-4000-8000-000000000004");
    private static readonly string s_GmDigest = Digest("gm-authorization");

    [TestMethod]
    public void Typed_catalog_quotes_stock_vehicle_modification_and_mount_composition_exactly()
    {
        CharacterVehicleWorkshopAuthority authority = new();
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(CharacterXml(), 5, catalog);

        Assert.IsTrue(preparation.Exact, string.Join("; ", preparation.Blockers));
        Assert.AreEqual(catalog.DeclaredCatalogDigest, preparation.CatalogDigest);
        Assert.IsTrue(preparation.Chassis.Any(item => item.Kind == CharacterVehicleChassisKind.Vehicle));
        Assert.IsTrue(preparation.Chassis.Any(item => item.Kind == CharacterVehicleChassisKind.Drone));
        CharacterVehicleWorkshopUnsupportedRow unsupported = preparation.UnsupportedRows.Single();
        Assert.AreEqual(s_UnsupportedMod.Value, unsupported.SourceId);
        StringAssert.Contains(unsupported.Reason, "expression");

        CharacterVehicleWorkshopQuote quote = authority.Quote(preparation, StockSelection());

        Assert.IsTrue(quote.Exact, string.Join("; ", quote.Blockers));
        Assert.AreEqual(7_900m, quote.TotalCost);
        Assert.AreEqual(-7_900m, quote.NuyenDelta);
        Assert.AreEqual(6, quote.SlotsUsed);
        Assert.AreEqual(2, quote.SlotsRemaining);
        Assert.AreEqual(4, quote.CapacityUsed);
        Assert.AreEqual(2, quote.CapacityRemaining);
        Assert.AreEqual(8, quote.Availability.Value);
        Assert.AreEqual(CharacterVehicleWorkshopLegality.Forbidden, quote.Availability.Legality);
        Assert.HasCount(5, quote.Lines);
        Assert.AreEqual(64, quote.QuoteDigest.Length);
    }

    [TestMethod]
    public void Gm_custom_chassis_requires_the_exact_catalog_authorization_digest()
    {
        CharacterVehicleWorkshopAuthority authority = new();
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(CharacterXml(), 5, Catalog());
        CharacterVehicleWorkshopSelection missingAuthority = new(
            s_CustomChassis,
            new CharacterVehicleInstanceId(Guid.Parse("30000000-0000-4000-8000-000000000099")),
            "GM Prototype",
            string.Empty,
            [],
            []);

        CharacterVehicleWorkshopQuote blocked = authority.Quote(preparation, missingAuthority);
        Assert.IsFalse(blocked.Exact);
        CollectionAssert.Contains(blocked.Blockers.ToArray(), CharacterVehicleWorkshopBlockers.GmAuthorityRequired);

        CharacterVehicleWorkshopQuote exact = authority.Quote(
            preparation,
            missingAuthority with { GmAuthorityDigest = s_GmDigest });
        Assert.IsTrue(exact.Exact, string.Join("; ", exact.Blockers));
        Assert.AreEqual(CharacterVehicleChassisPosture.GmApprovedCustom, exact.Posture);
        Assert.AreEqual(CharacterVehicleChassisKind.Drone, exact.Kind);
    }

    [TestMethod]
    public void Commit_is_atomic_idempotent_recoverable_and_receipt_bound_for_undo()
    {
        CharacterVehicleWorkshopAuthority authority = new();
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        string before = CharacterXml();
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(before, 5, catalog);
        CharacterVehicleWorkshopQuote quote = authority.Quote(preparation, StockSelection());
        CharacterVehicleWorkshopCommitCommand command = Command(preparation, quote, "vehicle-order-1");

        CharacterVehicleWorkshopCommitResult committed = authority.Commit(before, 5, catalog, command);

        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Committed, committed.Status, committed.BlockReason);
        Assert.AreEqual(6L, committed.NewContentRevision);
        Assert.AreEqual(2_100m, ReadDecimal(committed.CharacterXml, "nuyen"));
        Assert.AreEqual(1, XDocument.Parse(committed.CharacterXml).Descendants("vehicle").Count());
        Assert.AreEqual(1, XDocument.Parse(committed.CharacterXml).Descendants("expense").Count());
        Assert.AreEqual("AddVehicle", XDocument.Parse(committed.CharacterXml).Descendants("nuyentype").Single().Value);
        Assert.AreEqual("+4R", XDocument.Parse(committed.CharacterXml).Descendants("mod").Single().Element("avail")!.Value);
        XElement factoryGear = XDocument.Parse(committed.CharacterXml).Root!
            .Element("vehicles")!.Element("vehicle")!.Element("gears")!.Elements("gear").Single();
        CharacterVehicleWorkshopFactoryGearEntry projectedGear = Catalog().Chassis
            .Single(item => item.SourceId == s_StockChassis).FactoryGears.Single();
        CharacterVehicleFactoryGearInstanceId expectedGearId = CharacterVehicleWorkshopRules
            .DeriveFactoryGearInstanceId(s_VehicleInstance, projectedGear.ProjectionId);
        Assert.AreEqual(s_SensorArraySource.Value.ToString("D"), factoryGear.Element("sourceid")!.Value);
        Assert.AreEqual(expectedGearId.Value.ToString("D"), factoryGear.Element("guid")!.Value);
        Assert.AreEqual(s_VehicleInstance.Value.ToString("D"), factoryGear.Element("parentid")!.Value);
        Assert.AreEqual("2", factoryGear.Element("rating")!.Value);
        Assert.AreEqual("8/[0]", factoryGear.Element("capacity")!.Value);
        Assert.AreEqual("[0]", factoryGear.Element("armorcapacity")!.Value);
        Assert.AreEqual("0", factoryGear.Element("cost")!.Value);
        Assert.IsNotNull(committed.Receipt);
        Assert.AreEqual(64, committed.Receipt.ReceiptDigest.Length);

        CharacterVehicleWorkshopCommitResult replay = authority.Commit(
            committed.CharacterXml, committed.NewContentRevision, catalog, command);
        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Recovered, replay.Status, replay.BlockReason);
        Assert.AreEqual(committed.CharacterXml, replay.CharacterXml);
        Assert.IsTrue(replay.Receipt!.UndoReady);
        Assert.AreEqual(committed.Receipt.ReceiptDigest, replay.Receipt.ReceiptDigest);

        CharacterVehicleWorkshopCommitResult directRecovery = authority.Recover(
            committed.CharacterXml, committed.NewContentRevision, catalog, command);
        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Recovered, directRecovery.Status);
        Assert.AreEqual(committed.Receipt.ReceiptDigest, directRecovery.Receipt!.ReceiptDigest);

        XDocument tamperedDocument = XDocument.Parse(committed.CharacterXml);
        tamperedDocument.Descendants("gear").Single().Element("rating")!.Value = "3";
        string tamperedXml = tamperedDocument.ToString(SaveOptions.DisableFormatting);
        CharacterVehicleWorkshopCommitResult tamperedRecovery = authority.Recover(
            tamperedXml, committed.NewContentRevision, catalog, command);
        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Blocked, tamperedRecovery.Status);
        Assert.AreEqual(CharacterVehicleWorkshopBlockers.StaleReceipt, tamperedRecovery.BlockReason);
        Assert.AreEqual(tamperedXml, tamperedRecovery.CharacterXml);

        CharacterVehicleWorkshopCommitResult conflict = authority.Commit(
            committed.CharacterXml,
            committed.NewContentRevision,
            catalog,
            command with { Selection = command.Selection with { CustomName = "Different command" } });
        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Blocked, conflict.Status);
        Assert.AreEqual(CharacterVehicleWorkshopBlockers.IdempotencyConflict, conflict.BlockReason);
        Assert.AreEqual(committed.CharacterXml, conflict.CharacterXml);

        CharacterVehicleWorkshopCommitResult undone = authority.Undo(
            committed.CharacterXml,
            committed.NewContentRevision,
            catalog,
            new CharacterVehicleWorkshopUndoCommand(committed.Receipt));
        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Undone, undone.Status, undone.BlockReason);
        Assert.AreEqual(7L, undone.NewContentRevision);
        Assert.AreEqual(10_000m, ReadDecimal(undone.CharacterXml, "nuyen"));
        Assert.AreEqual(0, XDocument.Parse(undone.CharacterXml).Descendants("vehicle").Count());
        Assert.AreEqual(0, XDocument.Parse(undone.CharacterXml).Descendants("expense").Count());
    }

    [TestMethod]
    public void Repeated_mount_sources_are_allowed_but_aggregate_slots_still_fail_closed()
    {
        CharacterVehicleWorkshopAuthority authority = new();
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(CharacterXml(), 5, Catalog());
        CharacterVehicleWorkshopSelection selection = StockSelection();
        CharacterVehicleWeaponMountSelection repeatedMount = new(
            new CharacterVehicleWeaponMountInstanceId(Guid.Parse("30000000-0000-4000-8000-000000000077")),
            selection.WeaponMounts[0].Components.Select((component, index) => component with
            {
                InstanceId = new CharacterVehicleWeaponMountComponentInstanceId(Guid.Parse(
                    $"50000000-0000-4000-8000-{(index + 101).ToString("000000000000", System.Globalization.CultureInfo.InvariantCulture)}"))
            }).ToArray());

        CharacterVehicleWorkshopQuote quote = authority.Quote(
            preparation,
            selection with { WeaponMounts = [selection.WeaponMounts[0], repeatedMount] });

        Assert.IsFalse(quote.Exact);
        CollectionAssert.Contains(quote.Blockers.ToArray(), CharacterVehicleWorkshopBlockers.SlotsExceeded);
        CollectionAssert.DoesNotContain(quote.Blockers.ToArray(), CharacterVehicleWorkshopBlockers.IdentityInvalid);
        Assert.AreEqual(10, quote.SlotsUsed);
        Assert.AreEqual(6, quote.CapacityUsed);
    }

    [TestMethod]
    public void Fixed_non_rated_source_modifications_do_not_invent_a_rating_multiplier()
    {
        CharacterVehicleWorkshopAuthority authority = new();
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(CharacterXml(), 5, Catalog());
        CharacterVehicleWorkshopSelection selection = StockSelection() with
        {
            Modifications =
            [
                new CharacterVehicleWorkshopModificationSelection(
                    s_FixedMod,
                    new CharacterVehicleModificationInstanceId(Guid.Parse("30000000-0000-4000-8000-000000000076")),
                    0)
            ],
            WeaponMounts = []
        };

        CharacterVehicleWorkshopQuote quote = authority.Quote(preparation, selection);

        Assert.IsTrue(quote.Exact, string.Join("; ", quote.Blockers));
        Assert.AreEqual(5_750m, quote.TotalCost);
        Assert.AreEqual(1, quote.SlotsUsed);
        Assert.AreEqual(0, quote.CapacityUsed);
    }

    [TestMethod]
    public void Profile_legality_cost_multipliers_are_catalog_and_quote_bound()
    {
        CharacterVehicleWorkshopAuthority authority = new();
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        catalog = catalog with
        {
            Binding = catalog.Binding with
            {
                MultiplyForbiddenCost = true,
                ForbiddenCostMultiplier = 1.25m
            }
        };
        catalog = catalog with
        {
            DeclaredCatalogDigest = CharacterVehicleWorkshopRules.ComputeCatalogDigest(catalog)
        };
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(CharacterXml(), 5, catalog);

        CharacterVehicleWorkshopQuote quote = authority.Quote(preparation, StockSelection());

        Assert.IsTrue(quote.Exact, string.Join("; ", quote.Blockers));
        Assert.AreEqual(9_875m, quote.TotalCost);
        Assert.AreEqual(-9_875m, quote.NuyenDelta);
    }

    [TestMethod]
    public void Stale_cas_tampered_receipt_and_unsupported_rows_fail_closed_without_xml_changes()
    {
        CharacterVehicleWorkshopAuthority authority = new();
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        string before = CharacterXml();
        CharacterVehicleWorkshopPreparation preparation = authority.Prepare(before, 5, catalog);
        CharacterVehicleWorkshopQuote quote = authority.Quote(preparation, StockSelection());
        CharacterVehicleWorkshopCommitCommand command = Command(preparation, quote, "vehicle-order-2");

        CharacterVehicleWorkshopCommitResult stale = authority.Commit(
            before, 6, catalog, command);
        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Blocked, stale.Status);
        Assert.AreEqual(CharacterVehicleWorkshopBlockers.StaleRevision, stale.BlockReason);
        Assert.AreEqual(before, stale.CharacterXml);

        CharacterVehicleWorkshopCatalog changedGearSource = catalog with
        {
            Binding = catalog.Binding with { GearDigest = Digest("changed-effective-gear.xml") }
        };
        changedGearSource = changedGearSource with
        {
            DeclaredCatalogDigest = CharacterVehicleWorkshopRules.ComputeCatalogDigest(changedGearSource)
        };
        CharacterVehicleWorkshopCommitResult staleGearCatalog = authority.Commit(
            before, 5, changedGearSource, command);
        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Blocked, staleGearCatalog.Status);
        Assert.AreEqual(CharacterVehicleWorkshopBlockers.StaleCatalog, staleGearCatalog.BlockReason);
        Assert.AreEqual(before, staleGearCatalog.CharacterXml);

        CharacterVehicleWorkshopCommitResult committed = authority.Commit(before, 5, catalog, command);
        CharacterVehicleWorkshopCommitReceipt tampered = committed.Receipt! with
        {
            NuyenDelta = committed.Receipt!.NuyenDelta - 1m
        };
        CharacterVehicleWorkshopCommitResult undo = authority.Undo(
            committed.CharacterXml,
            committed.NewContentRevision,
            catalog,
            new CharacterVehicleWorkshopUndoCommand(tampered));
        Assert.AreEqual(CharacterVehicleWorkshopCommitStatus.Blocked, undo.Status);
        Assert.AreEqual(CharacterVehicleWorkshopBlockers.StaleReceipt, undo.BlockReason);
        Assert.AreEqual(committed.CharacterXml, undo.CharacterXml);

        CharacterVehicleWorkshopSelection unsupported = StockSelection() with
        {
            Modifications =
            [
                new CharacterVehicleWorkshopModificationSelection(
                    s_UnsupportedMod,
                    new CharacterVehicleModificationInstanceId(Guid.Parse("30000000-0000-4000-8000-000000000098")),
                    1)
            ],
            WeaponMounts = []
        };
        CharacterVehicleWorkshopQuote unsupportedQuote = authority.Quote(preparation, unsupported);
        Assert.IsFalse(unsupportedQuote.Exact);
        Assert.IsTrue(unsupportedQuote.Lines.Single().BlockReason.Contains("expression", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, unsupportedQuote.QuoteDigest);
    }

    [TestMethod]
    public void Altered_source_binding_and_catalog_digest_are_rejected_before_quote()
    {
        CharacterVehicleWorkshopAuthority authority = new();
        CharacterVehicleWorkshopCatalog catalog = Catalog();

        CharacterVehicleWorkshopPreparation alteredCatalog = authority.Prepare(
            CharacterXml(),
            5,
            catalog with { DeclaredCatalogDigest = Digest("not-the-catalog") });
        Assert.IsFalse(alteredCatalog.Exact);
        CollectionAssert.Contains(alteredCatalog.Blockers.ToArray(), CharacterVehicleWorkshopBlockers.CatalogAltered);

        CharacterVehicleWorkshopCatalog wrongProfile = catalog with
        {
            Binding = catalog.Binding with { ProfileId = "different-profile" }
        };
        wrongProfile = wrongProfile with
        {
            DeclaredCatalogDigest = CharacterVehicleWorkshopRules.ComputeCatalogDigest(wrongProfile)
        };
        CharacterVehicleWorkshopPreparation alteredProfile = authority.Prepare(CharacterXml(), 5, wrongProfile);
        Assert.IsFalse(alteredProfile.Exact);
        CollectionAssert.Contains(alteredProfile.Blockers.ToArray(), CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
    }

    private static CharacterVehicleWorkshopCatalog Catalog()
    {
        CharacterVehicleWorkshopSourceBinding binding = new(
            CharacterVehicleWorkshopRules.RulesetId,
            ProfileId,
            CharacterVehicleWorkshopRules.SemanticsVersion,
            Digest("profile"),
            Digest("vehicles.xml"),
            Digest("weapons.xml"),
            Digest("gear.xml"),
            Digest("ordered-overlay-set"),
            MultiplyRestrictedCost: false,
            RestrictedCostMultiplier: 1m,
            MultiplyForbiddenCost: false,
            ForbiddenCostMultiplier: 1m,
            Exact: true);
        CharacterVehicleWorkshopChassisEntry[] chassis =
        [
            new(s_StockChassis, CharacterVehicleChassisKind.Vehicle, CharacterVehicleChassisPosture.Stock,
                "GMC Test Roadmaster", "Trucks", 3, 2, 2, 1, 4, 3, 3, 12, 6, 10, 3,
                8, 6, 5_000m, new CharacterVehicleWorkshopAvailability(4, CharacterVehicleWorkshopLegality.Legal, false),
                "SR5", "462", string.Empty, CharacterVehicleWorkshopProjectionStatus.Exact, string.Empty,
                [FactoryGear(s_StockChassis)]),
            new(s_CustomChassis, CharacterVehicleChassisKind.Drone, CharacterVehicleChassisPosture.GmApprovedCustom,
                "GM Custom Drone", "Drones: Small", 4, 4, 3, 3, 3, 3, 3, 3, 0, 2, 3,
                4, 4, 1_000m, new CharacterVehicleWorkshopAvailability(0, CharacterVehicleWorkshopLegality.Legal, false),
                "CUSTOM", "GM", s_GmDigest, CharacterVehicleWorkshopProjectionStatus.Exact, string.Empty, [])
        ];
        CharacterVehicleWorkshopModificationEntry[] modifications =
        [
            new(s_ArmorMod, "Armor Package", "Protection", 1, 4, 0m, 500m, 0, 1, 0, 1,
                new CharacterVehicleWorkshopAvailability(4, CharacterVehicleWorkshopLegality.Restricted, true),
                "R5", "160", [s_StockChassis], CharacterVehicleWorkshopProjectionStatus.Exact, string.Empty),
            new(s_UnsupportedMod, "Dynamic Formula Mod", "Powertrain", 1, 6, 0m, 0m, 0, 0, 0, 0,
                new CharacterVehicleWorkshopAvailability(0, CharacterVehicleWorkshopLegality.Legal, false),
                "R5", "161", [s_StockChassis], CharacterVehicleWorkshopProjectionStatus.Unsupported,
                "The source cost expression references unprojected vehicle attributes."),
            new(s_FixedMod, "Rigger Interface", "Electromagnetic", 0, 0, 750m, 0m, 1, 0, 0, 0,
                new CharacterVehicleWorkshopAvailability(4, CharacterVehicleWorkshopLegality.Legal, false),
                "SR5", "461", [s_StockChassis], CharacterVehicleWorkshopProjectionStatus.Exact, string.Empty)
        ];
        CharacterVehicleWeaponMountComponentEntry[] components =
        [
            Component("40000000-0000-4000-8000-000000000001", CharacterVehicleWeaponMountComponentKind.Size,
                "Standard", 1_000m, 2, 1, 6, CharacterVehicleWorkshopLegality.Forbidden),
            Component("40000000-0000-4000-8000-000000000002", CharacterVehicleWeaponMountComponentKind.Visibility,
                "Internal", 200m, 1, 0, 2, CharacterVehicleWorkshopLegality.Legal),
            Component("40000000-0000-4000-8000-000000000003", CharacterVehicleWeaponMountComponentKind.Flexibility,
                "Flexible", 300m, 0, 1, 4, CharacterVehicleWorkshopLegality.Legal),
            Component("40000000-0000-4000-8000-000000000004", CharacterVehicleWeaponMountComponentKind.Control,
                "Remote", 400m, 1, 0, 8, CharacterVehicleWorkshopLegality.Restricted)
        ];
        var unbound = new CharacterVehicleWorkshopCatalog(binding, chassis, modifications, components, string.Empty);
        return unbound with { DeclaredCatalogDigest = CharacterVehicleWorkshopRules.ComputeCatalogDigest(unbound) };
    }

    private static CharacterVehicleWorkshopFactoryGearEntry FactoryGear(
        CharacterVehicleChassisSourceId chassisSourceId)
    {
        string instructionDigest = Digest("<gear><name>Sensor Array</name><rating>1</rating></gear>");
        CharacterVehicleFactoryGearProjectionId projectionId = CharacterVehicleWorkshopRules
            .DeriveFactoryGearProjectionId(chassisSourceId, s_SensorArraySource, 0, instructionDigest);
        return new CharacterVehicleWorkshopFactoryGearEntry(
            projectionId,
            chassisSourceId,
            0,
            s_SensorArraySource,
            "Sensor Array",
            "Sensors",
            "8/[0]",
            "[0]",
            "2",
            "8",
            2,
            1m,
            new CharacterVehicleWorkshopAvailability(7, CharacterVehicleWorkshopLegality.Legal, false),
            string.Empty,
            "SR5",
            "445",
            ConsumeCapacity: false,
            SourceNodeDigest: Digest("sensor-array-source"),
            InstructionNodeDigest: instructionDigest,
            CharacterVehicleWorkshopProjectionStatus.Exact,
            UnsupportedReason: string.Empty);
    }

    private static CharacterVehicleWeaponMountComponentEntry Component(
        string id,
        CharacterVehicleWeaponMountComponentKind kind,
        string name,
        decimal cost,
        int slots,
        int capacity,
        int availability,
        CharacterVehicleWorkshopLegality legality)
        => new(new CharacterVehicleWeaponMountComponentSourceId(Guid.Parse(id)), kind, name, cost, slots, capacity,
            new CharacterVehicleWorkshopAvailability(availability, legality, false), "R5", "163", [s_StockChassis],
            [], [], CharacterVehicleWorkshopProjectionStatus.Exact, string.Empty);

    private static CharacterVehicleWorkshopSelection StockSelection()
    {
        string[] ids =
        [
            "50000000-0000-4000-8000-000000000001",
            "50000000-0000-4000-8000-000000000002",
            "50000000-0000-4000-8000-000000000003",
            "50000000-0000-4000-8000-000000000004"
        ];
        CharacterVehicleWeaponMountComponentSourceId[] sourceIds = Catalog().WeaponMountComponents
            .Select(item => item.SourceId).ToArray();
        return new CharacterVehicleWorkshopSelection(
            s_StockChassis,
            s_VehicleInstance,
            string.Empty,
            string.Empty,
            [new CharacterVehicleWorkshopModificationSelection(s_ArmorMod, s_ModInstance, 2)],
            [
                new CharacterVehicleWeaponMountSelection(
                    s_MountInstance,
                    sourceIds.Select((sourceId, index) => new CharacterVehicleWeaponMountComponentSelection(
                        sourceId,
                        new CharacterVehicleWeaponMountComponentInstanceId(Guid.Parse(ids[index])))).ToArray())
            ]);
    }

    private static CharacterVehicleWorkshopCommitCommand Command(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopQuote quote,
        string key)
        => new(preparation.ContentRevision, preparation.CharacterDigest, preparation.CatalogDigest,
            quote.QuoteDigest, key, s_ExpenseId,
            new DateTimeOffset(2080, 1, 2, 3, 4, 5, TimeSpan.Zero), StockSelection());

    private static string CharacterXml()
        => $"<character><created>True</created><settings>{ProfileId}</settings><nuyen>10000</nuyen><improvements /></character>";

    private static string Digest(string value) => CharacterVehicleWorkshopRules.ComputeCharacterDigest(value);

    private static decimal ReadDecimal(string xml, string name)
        => decimal.Parse(XDocument.Parse(xml).Root!.Elements(name).Single().Value, System.Globalization.CultureInfo.InvariantCulture);
}
