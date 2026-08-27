using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

/// <summary>
/// Atomic XML authority over a caller-projected, digest-bound SR5 workshop catalog.
/// It never evaluates source expressions: only rows explicitly marked Exact can mutate.
/// </summary>
public sealed class CharacterVehicleWorkshopAuthority : ICharacterVehicleWorkshopAuthority
{
    public CharacterVehicleWorkshopPreparation Prepare(
        string characterXml,
        long contentRevision,
        CharacterVehicleWorkshopCatalog catalog)
    {
        string characterDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(characterXml);
        var blockers = new List<string>();
        decimal nuyen = 0m;
        string settingsProfile = string.Empty;
        XDocument? character = TryParseCharacter(characterXml, blockers);
        XElement? root = character?.Root;
        if (contentRevision < 0)
            blockers.Add("Character content revision must be non-negative.");
        if (root is not null)
        {
            if (!TryReadBoolean(root, "created", out bool created) || !created)
                blockers.Add(CharacterVehicleWorkshopBlockers.NotCareer);
            if (!TryReadScalar(root, "settings", out settingsProfile) || string.IsNullOrWhiteSpace(settingsProfile))
                blockers.Add("The saved character has no unique settings profile identity.");
            if (!TryReadDecimal(root, "nuyen", out nuyen) || nuyen < 0m)
                blockers.Add("The saved character has no unique non-negative Nuyen balance.");
            XElement[] improvements = root.Elements("improvements").Take(2).ToArray();
            if (improvements.Length > 1 || improvements.Any(container => container.Elements().Any()))
            {
                blockers.Add("Saved improvements are unsupported until their vehicle cost, availability, slot, and capacity effects are projected exactly.");
            }
            if (root.Elements("vehicles").Take(2).Count() > 1
                || root.Elements("expenses").Take(2).Count() > 1)
            {
                blockers.Add("The saved vehicle or expense container is ambiguous.");
            }
        }

        CharacterVehicleWorkshopSourceBinding binding = catalog?.Binding
            ?? new CharacterVehicleWorkshopSourceBinding(string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                false, 0m, false, 0m, false);
        IReadOnlyList<CharacterVehicleWorkshopChassisEntry> chassis = catalog?.Chassis ?? [];
        IReadOnlyList<CharacterVehicleWorkshopModificationEntry> modifications = catalog?.Modifications ?? [];
        IReadOnlyList<CharacterVehicleWeaponMountComponentEntry> components = catalog?.WeaponMountComponents ?? [];
        string catalogDigest = catalog is null ? string.Empty : CharacterVehicleWorkshopRules.ComputeCatalogDigest(catalog);
        if (catalog is not null
            && (catalog.Binding is null || catalog.Chassis is null || catalog.Modifications is null
                || catalog.WeaponMountComponents is null
                || catalog.Chassis.Any(item => item?.FactoryGears is null)))
        {
            blockers.Add("The typed workshop catalog contains null authority collections.");
        }
        ValidateBinding(binding, settingsProfile, blockers);
        if (catalog is null
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(catalog.DeclaredCatalogDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(catalog.DeclaredCatalogDigest, catalogDigest))
        {
            blockers.Add(CharacterVehicleWorkshopBlockers.CatalogAltered);
        }
        ValidateCatalog(chassis, modifications, components, blockers);

        CharacterVehicleWorkshopUnsupportedRow[] unsupported = chassis
            .Where(item => item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Unsupported)
                .Select(item => new CharacterVehicleWorkshopUnsupportedRow(
                    "chassis", item.SourceId.Value, item.Name, item.UnsupportedReason))
            .Concat(chassis.SelectMany(item => item.FactoryGears ?? [])
                .Where(item => item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Unsupported)
                .Select(item => new CharacterVehicleWorkshopUnsupportedRow(
                    "factory-gear", item.SourceId.Value, item.Name, item.UnsupportedReason)))
            .Concat(modifications
                .Where(item => item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Unsupported)
                .Select(item => new CharacterVehicleWorkshopUnsupportedRow(
                    "modification", item.SourceId.Value, item.Name, item.UnsupportedReason)))
            .Concat(components
                .Where(item => item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Unsupported)
                .Select(item => new CharacterVehicleWorkshopUnsupportedRow(
                    "weapon-mount-component", item.SourceId.Value, item.Name, item.UnsupportedReason)))
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.SourceId)
            .ToArray();
        string[] normalized = CharacterVehicleWorkshopRules.Normalize(blockers);
        return new CharacterVehicleWorkshopPreparation(
            normalized.Length == 0,
            normalized,
            contentRevision,
            characterDigest,
            nuyen,
            binding,
            catalogDigest,
            chassis.ToArray(),
            modifications.ToArray(),
            components.ToArray(),
            unsupported);
    }

    public CharacterVehicleWorkshopQuote Quote(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopSelection selection)
        => CharacterVehicleWorkshopRules.Quote(preparation, selection);

    public CharacterVehicleWorkshopCommitResult Commit(
        string characterXml,
        long currentContentRevision,
        CharacterVehicleWorkshopCatalog catalog,
        CharacterVehicleWorkshopCommitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TryValidateIdempotencyKey(command.IdempotencyKey))
            return Blocked(characterXml, currentContentRevision, command.Selection?.NewVehicleInstanceId ?? default,
                command.NewExpenseId, CharacterVehicleWorkshopBlockers.IdempotencyKeyInvalid);
        if (command.Selection is null)
            return Blocked(characterXml, currentContentRevision, default, command.NewExpenseId,
                CharacterVehicleWorkshopBlockers.IdentityInvalid);
        if (catalog is null)
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);

        string keyDigest = CharacterVehicleWorkshopRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey);
        string commandDigest = CharacterVehicleWorkshopRules.ComputeCommandDigest(command);
        TransactionLookup transaction = FindTransaction(characterXml, keyDigest);
        if (transaction.Ambiguous)
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, CharacterVehicleWorkshopBlockers.IdempotencyConflict);
        if (transaction.Vehicle is not null)
        {
            if (!string.Equals(transaction.CommandDigest, commandDigest, StringComparison.Ordinal))
                return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                    command.NewExpenseId, CharacterVehicleWorkshopBlockers.IdempotencyConflict);
            return RecoverCore(characterXml, currentContentRevision, catalog, command, keyDigest, commandDigest, transaction);
        }

        CharacterVehicleWorkshopPreparation preparation = Prepare(characterXml, currentContentRevision, catalog);
        CharacterVehicleWorkshopQuote quote = Quote(preparation, command.Selection);
        string? blocker = ValidateCommit(preparation, quote, command, characterXml);
        if (blocker is not null)
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, blocker);

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            CharacterVehicleWorkshopChassisEntry chassis = preparation.Chassis.Single(item =>
                item.SourceId == command.Selection.ChassisSourceId);
            Guid[] factoryGearInstanceIds = FactoryGearInstanceIdentities(
                chassis,
                command.Selection.NewVehicleInstanceId).ToArray();
            if (CommandInstanceIdentities(command).Concat(factoryGearInstanceIds).Append(command.NewExpenseId)
                .Any(identity => ContainsGuid(root, identity)))
            {
                return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                    command.NewExpenseId, CharacterVehicleWorkshopBlockers.IdentityInvalid);
            }

            XElement vehicle = CreateVehicle(preparation, chassis, quote, command, keyDigest, commandDigest);
            XElement expense = CreateExpense(chassis, quote, command);
            XElement vehicles = GetOrCreateUniqueContainer(root, "vehicles");
            XElement expenses = GetOrCreateUniqueContainer(root, "expenses");
            root.Elements("nuyen").Single().Value = checked(preparation.AvailableNuyen + quote.NuyenDelta)
                .ToString(CultureInfo.InvariantCulture);
            vehicles.Add(vehicle);
            expenses.Add(expense);

            string output = document.ToString(SaveOptions.DisableFormatting);
            string outputDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(output);
            string vehicleDigest = ElementDigest(vehicle);
            string expenseDigest = ElementDigest(expense);
            var unsigned = new CharacterVehicleWorkshopCommitReceipt(
                checked(currentContentRevision + 1), outputDigest,
                currentContentRevision, preparation.CharacterDigest, preparation.AvailableNuyen,
                preparation.CatalogDigest, quote.QuoteDigest, keyDigest, commandDigest,
                command.Selection.NewVehicleInstanceId, command.NewExpenseId, quote.NuyenDelta,
                vehicleDigest, expenseDigest, UndoReady: true, ReceiptDigest: string.Empty);
            CharacterVehicleWorkshopCommitReceipt receipt = unsigned with
            {
                ReceiptDigest = CharacterVehicleWorkshopRules.ComputeReceiptDigest(unsigned)
            };
            return new CharacterVehicleWorkshopCommitResult(
                CharacterVehicleWorkshopCommitStatus.Committed, string.Empty,
                currentContentRevision, checked(currentContentRevision + 1),
                preparation.CharacterDigest, outputDigest, output,
                command.Selection.NewVehicleInstanceId, command.NewExpenseId, quote.NuyenDelta, receipt);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException or System.Xml.XmlException)
        {
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, "The workshop composition could not be applied atomically to the exact saved XML shape.");
        }
    }

    public CharacterVehicleWorkshopCommitResult Recover(
        string characterXml,
        long currentContentRevision,
        CharacterVehicleWorkshopCatalog catalog,
        CharacterVehicleWorkshopCommitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Selection is null || !TryValidateIdempotencyKey(command.IdempotencyKey))
            return Blocked(characterXml, currentContentRevision, command.Selection?.NewVehicleInstanceId ?? default,
                command.NewExpenseId, CharacterVehicleWorkshopBlockers.IdempotencyKeyInvalid);
        if (catalog is null)
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        string keyDigest = CharacterVehicleWorkshopRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey);
        string commandDigest = CharacterVehicleWorkshopRules.ComputeCommandDigest(command);
        TransactionLookup transaction = FindTransaction(characterXml, keyDigest);
        if (transaction.Ambiguous || transaction.Vehicle is null
            || !string.Equals(transaction.CommandDigest, commandDigest, StringComparison.Ordinal))
        {
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, transaction.Vehicle is null
                    ? "No committed workshop transaction matches the recovery key."
                    : CharacterVehicleWorkshopBlockers.IdempotencyConflict);
        }
        return RecoverCore(characterXml, currentContentRevision, catalog, command, keyDigest, commandDigest, transaction);
    }

    public CharacterVehicleWorkshopCommitResult Undo(
        string characterXml,
        long currentContentRevision,
        CharacterVehicleWorkshopCatalog catalog,
        CharacterVehicleWorkshopUndoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CharacterVehicleWorkshopCommitReceipt? receipt = command.Receipt;
        CharacterVehicleInstanceId vehicleId = receipt?.VehicleInstanceId ?? default;
        Guid expenseId = receipt?.ExpenseId ?? Guid.Empty;
        string characterDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(characterXml);
        if (catalog is null)
            return Blocked(characterXml, currentContentRevision, vehicleId, expenseId,
                CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        CharacterVehicleWorkshopPreparation currentPreparation = Prepare(
            characterXml, currentContentRevision, catalog);
        if (!currentPreparation.Exact)
            return Blocked(characterXml, currentContentRevision, vehicleId, expenseId,
                currentPreparation.Blockers.FirstOrDefault()
                ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        if (receipt is null || !receipt.UndoReady
            || receipt.ContentRevision != currentContentRevision
            || !CharacterVehicleWorkshopRules.FixedEquals(receipt.CharacterDigest, characterDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(receipt.ReceiptDigest,
                CharacterVehicleWorkshopRules.ComputeReceiptDigest(receipt))
            || receipt.VehicleInstanceId.Value == Guid.Empty || receipt.ExpenseId == Guid.Empty
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(receipt.VehicleXmlDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(receipt.ExpenseXmlDigest))
        {
            return Blocked(characterXml, currentContentRevision, vehicleId, expenseId,
                CharacterVehicleWorkshopBlockers.StaleReceipt);
        }

        string catalogDigest = CharacterVehicleWorkshopRules.ComputeCatalogDigest(catalog);
        if (!CharacterVehicleWorkshopRules.FixedEquals(receipt.CatalogDigest, catalogDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(catalog.DeclaredCatalogDigest, catalogDigest))
        {
            return Blocked(characterXml, currentContentRevision, vehicleId, expenseId,
                CharacterVehicleWorkshopBlockers.StaleCatalog);
        }

        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement root = document.Root!;
            XElement[] vehicleMatches = FindByGuid(root, "vehicles", "vehicle", vehicleId.Value);
            XElement[] expenseMatches = FindByGuid(root, "expenses", "expense", expenseId);
            if (vehicleMatches.Length != 1 || expenseMatches.Length != 1
                || !CharacterVehicleWorkshopRules.FixedEquals(ElementDigest(vehicleMatches[0]), receipt.VehicleXmlDigest)
                || !CharacterVehicleWorkshopRules.FixedEquals(ElementDigest(expenseMatches[0]), receipt.ExpenseXmlDigest)
                || !TryReadDecimal(expenseMatches[0], "amount", out decimal amount)
                || amount != receipt.NuyenDelta
                || !TryReadDecimal(root, "nuyen", out decimal nuyen)
                || !TryReadTransaction(vehicleMatches[0], out TransactionMetadata metadata)
                || !MetadataMatchesReceipt(metadata, receipt))
            {
                return Blocked(characterXml, currentContentRevision, vehicleId, expenseId,
                    CharacterVehicleWorkshopBlockers.StaleReceipt);
            }

            vehicleMatches[0].Remove();
            expenseMatches[0].Remove();
            root.Elements("nuyen").Single().Value = checked(nuyen - amount).ToString(CultureInfo.InvariantCulture);
            string output = document.ToString(SaveOptions.DisableFormatting);
            return new CharacterVehicleWorkshopCommitResult(
                CharacterVehicleWorkshopCommitStatus.Undone, string.Empty,
                currentContentRevision, checked(currentContentRevision + 1), characterDigest,
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(output), output,
                vehicleId, expenseId, -amount, Receipt: null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException or System.Xml.XmlException)
        {
            return Blocked(characterXml, currentContentRevision, vehicleId, expenseId,
                "The workshop undo could not be applied atomically to the exact saved XML shape.");
        }
    }

    private CharacterVehicleWorkshopCommitResult RecoverCore(
        string characterXml,
        long currentContentRevision,
        CharacterVehicleWorkshopCatalog catalog,
        CharacterVehicleWorkshopCommitCommand command,
        string keyDigest,
        string commandDigest,
        TransactionLookup transaction)
    {
        string characterDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(characterXml);
        CharacterVehicleWorkshopPreparation currentPreparation = Prepare(
            characterXml, currentContentRevision, catalog);
        if (!currentPreparation.Exact)
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, currentPreparation.Blockers.FirstOrDefault()
                ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        string catalogDigest = currentPreparation.CatalogDigest;
        if (!CharacterVehicleWorkshopRules.FixedEquals(catalog.DeclaredCatalogDigest, catalogDigest)
            || !TryReadTransaction(transaction.Vehicle!, out TransactionMetadata metadata)
            || !string.Equals(metadata.IdempotencyKeyDigest, keyDigest, StringComparison.Ordinal)
            || !string.Equals(metadata.CommandDigest, commandDigest, StringComparison.Ordinal)
            || !string.Equals(metadata.CatalogDigest, catalogDigest, StringComparison.Ordinal)
            || metadata.VehicleId != command.Selection.NewVehicleInstanceId.Value
            || metadata.ExpenseId != command.NewExpenseId)
        {
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, CharacterVehicleWorkshopBlockers.StaleCatalog);
        }
        CharacterVehicleWorkshopPreparation historicalPreparation = currentPreparation with
        {
            ContentRevision = metadata.PreviousRevision,
            CharacterDigest = metadata.PreviousCharacterDigest,
            AvailableNuyen = metadata.PreviousNuyen
        };
        CharacterVehicleWorkshopQuote historicalQuote = Quote(historicalPreparation, command.Selection);
        CharacterVehicleWorkshopChassisEntry historicalChassis = historicalPreparation.Chassis.Single(item =>
            item.SourceId == command.Selection.ChassisSourceId);
        XElement expectedVehicle = CreateVehicle(
            historicalPreparation,
            historicalChassis,
            historicalQuote,
            command,
            keyDigest,
            commandDigest);
        if (!historicalQuote.Exact
            || !CharacterVehicleWorkshopRules.FixedEquals(historicalQuote.QuoteDigest, metadata.QuoteDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(
                ElementDigest(expectedVehicle),
                ElementDigest(transaction.Vehicle!)))
        {
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, CharacterVehicleWorkshopBlockers.StaleReceipt);
        }
        XElement root = transaction.Vehicle!.Document?.Root!;
        XElement[] expenses = FindByGuid(root, "expenses", "expense", metadata.ExpenseId);
        if (expenses.Length != 1 || !TryReadDecimal(expenses[0], "amount", out decimal amount)
            || amount != metadata.NuyenDelta)
        {
            return Blocked(characterXml, currentContentRevision, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, CharacterVehicleWorkshopBlockers.StaleReceipt);
        }

        bool undoReady = currentContentRevision == metadata.CommitRevision;
        string vehicleDigest = ElementDigest(transaction.Vehicle);
        string expenseDigest = ElementDigest(expenses[0]);
        var unsigned = new CharacterVehicleWorkshopCommitReceipt(
            currentContentRevision, characterDigest, metadata.PreviousRevision, metadata.PreviousCharacterDigest,
            metadata.PreviousNuyen, metadata.CatalogDigest, metadata.QuoteDigest,
            keyDigest, commandDigest, command.Selection.NewVehicleInstanceId, command.NewExpenseId,
            metadata.NuyenDelta, vehicleDigest, expenseDigest, undoReady, ReceiptDigest: string.Empty);
        CharacterVehicleWorkshopCommitReceipt receipt = unsigned with
        {
            ReceiptDigest = CharacterVehicleWorkshopRules.ComputeReceiptDigest(unsigned)
        };
        return new CharacterVehicleWorkshopCommitResult(
            CharacterVehicleWorkshopCommitStatus.Recovered, string.Empty,
            metadata.PreviousRevision, currentContentRevision, metadata.PreviousCharacterDigest,
            characterDigest, characterXml, command.Selection.NewVehicleInstanceId, command.NewExpenseId,
            metadata.NuyenDelta, receipt);
    }

    private static XElement CreateVehicle(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopChassisEntry chassis,
        CharacterVehicleWorkshopQuote quote,
        CharacterVehicleWorkshopCommitCommand command,
        string keyDigest,
        string commandDigest)
    {
        XElement mods = new("mods");
        foreach (CharacterVehicleWorkshopModificationSelection selected in command.Selection.Modifications)
        {
            CharacterVehicleWorkshopModificationEntry entry = preparation.Modifications.Single(item =>
                item.SourceId == selected.SourceId);
            mods.Add(CreateModification(entry, selected));
        }
        XElement mounts = new("weaponmounts");
        foreach (CharacterVehicleWeaponMountSelection selected in command.Selection.WeaponMounts)
            mounts.Add(CreateWeaponMount(preparation, selected));
        XElement gears = new("gears");
        foreach (CharacterVehicleWorkshopFactoryGearEntry factoryGear in chassis.FactoryGears
                     .OrderBy(item => item.Ordinal))
        {
            gears.Add(CreateFactoryGear(factoryGear, command.Selection.NewVehicleInstanceId));
        }

        return new XElement("vehicle",
            Scalar("sourceid", chassis.SourceId.Value.ToString("D")),
            Scalar("guid", command.Selection.NewVehicleInstanceId.Value.ToString("D")),
            Scalar("name", chassis.Name),
            Scalar("category", chassis.Category),
            Scalar("handling", chassis.Handling), Scalar("offroadhandling", chassis.OffRoadHandling),
            Scalar("accel", chassis.Acceleration), Scalar("offroadaccel", chassis.OffRoadAcceleration),
            Scalar("speed", chassis.Speed), Scalar("offroadspeed", chassis.OffRoadSpeed),
            Scalar("pilot", chassis.Pilot), Scalar("body", chassis.Body), Scalar("seats", chassis.Seats),
            Scalar("armor", chassis.Armor), Scalar("sensor", chassis.Sensor),
            Scalar("avail", FormatAvailability(chassis.Availability)),
            Scalar("cost", chassis.Cost), Scalar("addslots", 0), Scalar("modslots", chassis.ModificationSlots),
            Scalar("powertrainmodslots", 0), Scalar("protectionmodslots", 0), Scalar("weaponmodslots", 0),
            Scalar("bodymodslots", 0), Scalar("electromagneticmodslots", 0), Scalar("cosmeticmodslots", 0),
            Scalar("source", chassis.SourceBook), Scalar("page", chassis.Page), Scalar("parentid", string.Empty),
            Scalar("sortorder", 0), Scalar("stolen", false), Scalar("physicalcmfilled", 0),
            Scalar("matrixcmfilled", 0), Scalar("vehiclename", command.Selection.CustomName),
            mods, mounts, gears, new XElement("weapons"), Scalar("location", string.Empty),
            Scalar("notes", string.Empty), Scalar("notesColor", "Chocolate"), Scalar("discountedcost", false),
            Scalar("dealerconnection", false), Scalar("active", false), Scalar("homenode", false),
            Scalar("devicerating", chassis.Pilot), Scalar("programlimit", string.Empty), Scalar("overclocked", "None"),
            Scalar("attack", string.Empty), Scalar("sleaze", string.Empty), Scalar("dataprocessing", string.Empty),
            Scalar("firewall", string.Empty), Scalar("attributearray", string.Empty), Scalar("modattack", string.Empty),
            Scalar("modsleaze", string.Empty), Scalar("moddataprocessing", string.Empty),
            Scalar("modfirewall", string.Empty), Scalar("modattributearray", string.Empty),
            Scalar("canswapattributes", false),
            new XElement("workshoptransaction",
                Scalar("version", CharacterVehicleWorkshopRules.SemanticsVersion),
                Scalar("idempotencydigest", keyDigest), Scalar("commanddigest", commandDigest),
                Scalar("catalogdigest", preparation.CatalogDigest), Scalar("quotedigest", quote.QuoteDigest),
                Scalar("previousrevision", preparation.ContentRevision),
                Scalar("previouscharacterdigest", preparation.CharacterDigest),
                Scalar("previousnuyen", preparation.AvailableNuyen),
                Scalar("commitrevision", checked(preparation.ContentRevision + 1)),
                Scalar("vehicleid", command.Selection.NewVehicleInstanceId.Value.ToString("D")),
                Scalar("expenseid", command.NewExpenseId.ToString("D")), Scalar("nuyendelta", quote.NuyenDelta)));
    }

    private static XElement CreateFactoryGear(
        CharacterVehicleWorkshopFactoryGearEntry entry,
        CharacterVehicleInstanceId vehicleInstanceId)
    {
        CharacterVehicleFactoryGearInstanceId instanceId =
            CharacterVehicleWorkshopRules.DeriveFactoryGearInstanceId(vehicleInstanceId, entry.ProjectionId);
        return new XElement("gear",
            Scalar("sourceid", entry.SourceId.Value.ToString("D")),
            Scalar("guid", instanceId.Value.ToString("D")),
            Scalar("name", entry.Name),
            Scalar("category", entry.Category),
            Scalar("capacity", entry.Capacity),
            Scalar("armorcapacity", entry.ArmorCapacity),
            Scalar("minrating", entry.MinimumRating),
            Scalar("maxrating", entry.MaximumRating),
            Scalar("rating", entry.Rating),
            Scalar("qty", entry.Quantity),
            Scalar("avail", FormatAvailability(entry.Availability)),
            Scalar("cost", 0),
            Scalar("weight", entry.Weight),
            Scalar("extra", string.Empty),
            Scalar("bonded", false),
            Scalar("equipped", true),
            Scalar("wirelesson", false),
            Scalar("stolen", false),
            Scalar("bonus", string.Empty),
            Scalar("wirelessbonus", string.Empty),
            Scalar("weaponbonus", string.Empty),
            Scalar("flechetteweaponbonus", string.Empty),
            Scalar("source", entry.SourceBook),
            Scalar("page", entry.Page),
            Scalar("isflechetteammo", false),
            Scalar("ammoforweapontype", string.Empty),
            Scalar("canformpersona", string.Empty),
            Scalar("devicerating", string.Empty),
            Scalar("gearname", string.Empty),
            Scalar("forcedvalue", string.Empty),
            Scalar("matrixcmfilled", 0),
            Scalar("matrixcmbonus", 0),
            Scalar("parentid", vehicleInstanceId.Value.ToString("D")),
            Scalar("allowrename", false),
            new XElement("children"),
            Scalar("location", string.Empty),
            Scalar("notes", string.Empty),
            Scalar("notesColor", "Chocolate"),
            Scalar("discountedcost", false),
            Scalar("programlimit", string.Empty),
            Scalar("overclocked", "None"),
            Scalar("attack", string.Empty),
            Scalar("sleaze", string.Empty),
            Scalar("dataprocessing", string.Empty),
            Scalar("firewall", string.Empty),
            Scalar("attributearray", string.Empty),
            Scalar("modattack", string.Empty),
            Scalar("modsleaze", string.Empty),
            Scalar("moddataprocessing", string.Empty),
            Scalar("modfirewall", string.Empty),
            Scalar("modattributearray", string.Empty),
            Scalar("canswapattributes", false),
            Scalar("active", false),
            Scalar("homenode", false),
            Scalar("sortorder", 0));
    }

    private static XElement CreateModification(
        CharacterVehicleWorkshopModificationEntry entry,
        CharacterVehicleWorkshopModificationSelection selection)
        => new("mod",
            Scalar("sourceid", entry.SourceId.Value.ToString("D")),
            Scalar("guid", selection.InstanceId.Value.ToString("D")), Scalar("name", entry.Name),
            Scalar("category", entry.Category), Scalar("limit", string.Empty),
            Scalar("slots", checked(entry.BaseSlots + entry.SlotsPerRating * selection.Rating)),
            Scalar("capacity", checked(entry.BaseCapacity + entry.CapacityPerRating * selection.Rating)),
            Scalar("rating", selection.Rating), Scalar("maxrating", entry.MaximumRating),
            Scalar("ratinglabel", "String_Rating"), Scalar("conditionmonitor", 0),
            Scalar("avail", FormatAvailability(entry.Availability)),
            Scalar("cost", checked(entry.BaseCost + entry.CostPerRating * selection.Rating)), Scalar("extra", string.Empty),
            Scalar("source", entry.SourceBook), Scalar("page", entry.Page), Scalar("included", false),
            Scalar("equipped", true), Scalar("wirelesson", false), Scalar("subsystems", string.Empty),
            Scalar("weaponmountcategories", string.Empty), Scalar("ammobonus", 0),
            Scalar("ammobonuspercent", 0), Scalar("ammoreplace", string.Empty), new XElement("weapons"),
            Scalar("notes", string.Empty), Scalar("notesColor", "Chocolate"), Scalar("discountedcost", false),
            Scalar("useownattributesforweapon", false), Scalar("sortorder", 0), Scalar("stolen", false));

    private static XElement CreateWeaponMount(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWeaponMountSelection selection)
    {
        CharacterVehicleWeaponMountComponentEntry[] entries = selection.Components.Select(component =>
            preparation.WeaponMountComponents.Single(item => item.SourceId == component.SourceId)).ToArray();
        CharacterVehicleWeaponMountComponentEntry size = entries.Single(item =>
            item.Kind == CharacterVehicleWeaponMountComponentKind.Size);
        CharacterVehicleWeaponMountComponentSelection sizeSelection = selection.Components.Single(component =>
            component.SourceId == size.SourceId);
        var options = new XElement("weaponmountoptions");
        foreach (CharacterVehicleWeaponMountComponentSelection componentSelection in selection.Components.Where(component =>
                     component.SourceId != size.SourceId))
        {
            CharacterVehicleWeaponMountComponentEntry entry = entries.Single(item => item.SourceId == componentSelection.SourceId);
            options.Add(new XElement("weaponmountoption",
                Scalar("sourceid", entry.SourceId.Value.ToString("D")),
                Scalar("guid", componentSelection.InstanceId.Value.ToString("D")), Scalar("name", entry.Name),
                Scalar("category", entry.Kind.ToString()), Scalar("slots", entry.Slots),
                Scalar("avail", FormatAvailability(entry.Availability)), Scalar("cost", entry.Cost),
                Scalar("includedinparent", false)));
        }
        return new XElement("weaponmount",
            Scalar("sourceid", size.SourceId.Value.ToString("D")),
            Scalar("guid", selection.InstanceId.Value.ToString("D")), Scalar("name", size.Name),
            Scalar("category", size.Kind.ToString()), Scalar("limit", string.Empty), Scalar("slots", size.Slots),
            Scalar("avail", FormatAvailability(size.Availability)), Scalar("cost", size.Cost), Scalar("freecost", false),
            Scalar("extra", string.Empty), Scalar("source", size.SourceBook), Scalar("page", size.Page),
            Scalar("included", false), Scalar("equipped", true), Scalar("weaponmountcategories", string.Empty),
            Scalar("weaponfilter", string.Empty), Scalar("weaponcapacity", 0), new XElement("weapons"), options,
            new XElement("mods"), Scalar("notes", string.Empty), Scalar("notesColor", "Chocolate"),
            Scalar("discountedcost", false), Scalar("sortorder", 0), Scalar("stolen", false),
            Scalar("sizeoptioninstance", sizeSelection.InstanceId.Value.ToString("D")));
    }

    private static XElement CreateExpense(
        CharacterVehicleWorkshopChassisEntry chassis,
        CharacterVehicleWorkshopQuote quote,
        CharacterVehicleWorkshopCommitCommand command)
        => new("expense",
            Scalar("guid", command.NewExpenseId.ToString("D")),
            Scalar("date", command.ExpenseDate.UtcDateTime.ToString("s", CultureInfo.InvariantCulture)),
            Scalar("amount", quote.NuyenDelta), Scalar("reason", $"Purchased Vehicle {chassis.Name}"),
            Scalar("type", "Nuyen"), Scalar("refund", false), Scalar("forcecareervisible", false),
            new XElement("undo", Scalar("karmatype", "ImproveAttribute"), Scalar("nuyentype", "AddVehicle"),
                Scalar("objectid", command.Selection.NewVehicleInstanceId.Value.ToString("D")),
                Scalar("qty", 0), Scalar("extra", string.Empty)));

    private static string? ValidateCommit(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopQuote quote,
        CharacterVehicleWorkshopCommitCommand command,
        string characterXml)
    {
        if (!preparation.Exact)
            return preparation.Blockers.FirstOrDefault() ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable;
        if (command.ExpectedContentRevision != preparation.ContentRevision)
            return CharacterVehicleWorkshopBlockers.StaleRevision;
        if (!CharacterVehicleWorkshopRules.FixedEquals(command.ExpectedCharacterDigest, preparation.CharacterDigest)
            || !CharacterVehicleWorkshopRules.FixedEquals(preparation.CharacterDigest,
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(characterXml)))
            return CharacterVehicleWorkshopBlockers.StaleCharacter;
        if (!CharacterVehicleWorkshopRules.FixedEquals(command.ExpectedCatalogDigest, preparation.CatalogDigest))
            return CharacterVehicleWorkshopBlockers.StaleCatalog;
        if (!quote.Exact)
            return quote.Blockers.FirstOrDefault() ?? CharacterVehicleWorkshopBlockers.UnsupportedSelection;
        if (!CharacterVehicleWorkshopRules.FixedEquals(command.ExpectedQuoteDigest, quote.QuoteDigest))
            return CharacterVehicleWorkshopBlockers.StaleQuote;
        CharacterVehicleWorkshopChassisEntry? chassis = preparation.Chassis.SingleOrDefault(item =>
            item.SourceId == command.Selection.ChassisSourceId);
        IReadOnlyList<Guid> commandIdentities = CommandInstanceIdentities(command);
        Guid[] factoryGearIdentities = chassis is null
            ? []
            : FactoryGearInstanceIdentities(chassis, command.Selection.NewVehicleInstanceId).ToArray();
        Guid[] allIdentities = commandIdentities.Concat(factoryGearIdentities).Append(command.NewExpenseId).ToArray();
        if (command.NewExpenseId == Guid.Empty || command.ExpenseDate.Offset != TimeSpan.Zero
            || chassis is null
            || allIdentities.Any(identity => identity == Guid.Empty)
            || allIdentities.Distinct().Count() != allIdentities.Length
            || chassis.FactoryGears.Select(item => item.SourceId.Value).Intersect(allIdentities).Any())
            return CharacterVehicleWorkshopBlockers.IdentityInvalid;
        return null;
    }

    private static void ValidateBinding(
        CharacterVehicleWorkshopSourceBinding binding,
        string settingsProfile,
        List<string> blockers)
    {
        if (!binding.Exact
            || !string.Equals(binding.RulesetId, CharacterVehicleWorkshopRules.RulesetId, StringComparison.Ordinal)
            || !string.Equals(binding.SemanticsVersion, CharacterVehicleWorkshopRules.SemanticsVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(binding.ProfileId)
            || !string.Equals(binding.ProfileId, binding.ProfileId.Trim(), StringComparison.Ordinal)
            || !string.Equals(binding.ProfileId, settingsProfile, StringComparison.Ordinal)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(binding.ProfileDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(binding.VehiclesDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(binding.WeaponsDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(binding.GearDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(binding.OverlayDigest)
            || binding.RestrictedCostMultiplier < 0m || binding.ForbiddenCostMultiplier < 0m
            || binding.MultiplyRestrictedCost && binding.RestrictedCostMultiplier <= 0m
            || binding.MultiplyForbiddenCost && binding.ForbiddenCostMultiplier <= 0m)
        {
            blockers.Add(CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable);
        }
    }

    private static void ValidateCatalog(
        IReadOnlyList<CharacterVehicleWorkshopChassisEntry> chassis,
        IReadOnlyList<CharacterVehicleWorkshopModificationEntry> modifications,
        IReadOnlyList<CharacterVehicleWeaponMountComponentEntry> components,
        List<string> blockers)
    {
        if (chassis.Any(item => item is null) || modifications.Any(item => item is null) || components.Any(item => item is null)
            || chassis.Any(item => item is not null && (item.FactoryGears is null
                || item.FactoryGears.Any(gear => gear is null)))
            || modifications.Any(item => item is not null && item.AllowedChassis is null)
            || components.Any(item => item is not null
                && (item.AllowedChassis is null || item.RequiredComponents is null || item.ForbiddenComponents is null))
            || HasDuplicates(chassis.Select(item => item.SourceId.Value))
            || HasDuplicates(modifications.Select(item => item.SourceId.Value))
            || HasDuplicates(components.Select(item => item.SourceId.Value)))
        {
            blockers.Add("The typed workshop catalog contains null or duplicate source identities.");
            return;
        }
        var chassisIds = chassis.Select(item => item.SourceId).ToHashSet();
        var componentIds = components.Select(item => item.SourceId).ToHashSet();
        if (HasDuplicates(chassis.SelectMany(item => item.FactoryGears).Select(item => item.ProjectionId.Value)))
            blockers.Add("Factory gear projection identities must be globally unique in the workshop catalog.");
        foreach (CharacterVehicleWorkshopChassisEntry item in chassis)
        {
            if (!ValidProjection(item.ProjectionStatus, item.UnsupportedReason)
                || item.SourceId.Value == Guid.Empty || string.IsNullOrWhiteSpace(item.Name)
                || !Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Posture)
                || !ValidAvailability(item.Availability)
                || item.FactoryGears.Select(gear => gear.Ordinal).Distinct().Count() != item.FactoryGears.Count
                || item.FactoryGears.OrderBy(gear => gear.Ordinal).Select(gear => gear.Ordinal)
                    .Where((ordinal, index) => ordinal != index).Any())
            {
                blockers.Add("A chassis catalog row is structurally invalid.");
            }
            if (item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                && (item.Cost < 0m || item.ModificationSlots < 0 || item.ModificationCapacity < 0
                    || item.Body < 0 || item.Armor < 0 || item.Sensor < 0 || item.Pilot < 0
                    || item.Availability.AddToParent
                    || item.FactoryGears.Any(gear =>
                        gear.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact)
                    || item.Posture == CharacterVehicleChassisPosture.Stock && item.GmAuthorityDigest.Length != 0
                    || item.Posture == CharacterVehicleChassisPosture.GmApprovedCustom
                       && !CharacterVehicleWorkshopRules.IsCanonicalDigest(item.GmAuthorityDigest)))
            {
                blockers.Add("An exact chassis row has invalid cost, capacity, statistics, or GM posture.");
            }
            foreach (CharacterVehicleWorkshopFactoryGearEntry gear in item.FactoryGears)
            {
                CharacterVehicleFactoryGearProjectionId expectedProjectionId =
                    CharacterVehicleWorkshopRules.DeriveFactoryGearProjectionId(
                        item.SourceId,
                        gear.SourceId,
                        gear.Ordinal,
                        gear.InstructionNodeDigest);
                if (!ValidProjection(gear.ProjectionStatus, gear.UnsupportedReason)
                    || gear.ProjectionId.Value == Guid.Empty
                    || gear.ChassisSourceId != item.SourceId
                    || gear.Ordinal < 0
                    || gear.ProjectionId != expectedProjectionId
                    || !ValidAvailability(gear.Availability)
                    || !CharacterVehicleWorkshopRules.IsCanonicalDigest(gear.InstructionNodeDigest))
                {
                    blockers.Add("A factory gear catalog row is structurally invalid.");
                }
                if (gear.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                    && (gear.SourceId.Value == Guid.Empty
                        || string.IsNullOrWhiteSpace(gear.Name)
                        || string.IsNullOrWhiteSpace(gear.Category)
                        || string.IsNullOrWhiteSpace(gear.SourceBook)
                        || string.IsNullOrWhiteSpace(gear.Page)
                        || gear.Quantity <= 0m
                        || gear.Rating < 0
                        || !ValidRatingText(gear.MinimumRating)
                        || !ValidRatingText(gear.MaximumRating)
                        || !ValidFixedCapacity(gear.Capacity)
                        || !ValidFixedCapacity(gear.ArmorCapacity)
                        || !ValidOptionalFixedDecimal(gear.Weight)
                        || !CharacterVehicleWorkshopRules.IsCanonicalDigest(gear.SourceNodeDigest)))
                {
                    blockers.Add("An exact factory gear row has invalid saved fields or source bindings.");
                }
            }
        }
        foreach (CharacterVehicleWorkshopModificationEntry item in modifications)
        {
            if (!ValidProjection(item.ProjectionStatus, item.UnsupportedReason)
                || item.SourceId.Value == Guid.Empty || string.IsNullOrWhiteSpace(item.Name)
                || !ValidAvailability(item.Availability)
                || HasDuplicates(item.AllowedChassis.Select(id => id.Value))
                || item.AllowedChassis.Any(id => !chassisIds.Contains(id)))
            {
                blockers.Add("A modification catalog row is structurally invalid.");
            }
            if (item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                && (item.MinimumRating < 0 || item.MaximumRating < item.MinimumRating
                    || item.BaseCost < 0m || item.CostPerRating < 0m
                    || item.BaseSlots < 0 || item.SlotsPerRating < 0
                    || item.BaseCapacity < 0 || item.CapacityPerRating < 0))
            {
                blockers.Add("An exact modification row has an invalid rating, cost, slot, or capacity projection.");
            }
        }
        foreach (CharacterVehicleWeaponMountComponentEntry item in components)
        {
            if (!ValidProjection(item.ProjectionStatus, item.UnsupportedReason)
                || item.SourceId.Value == Guid.Empty || string.IsNullOrWhiteSpace(item.Name)
                || !Enum.IsDefined(item.Kind) || !ValidAvailability(item.Availability)
                || HasDuplicates(item.AllowedChassis.Select(id => id.Value))
                || HasDuplicates(item.RequiredComponents.Select(id => id.Value))
                || HasDuplicates(item.ForbiddenComponents.Select(id => id.Value))
                || item.AllowedChassis.Any(id => !chassisIds.Contains(id))
                || item.RequiredComponents.Any(id => !componentIds.Contains(id))
                || item.ForbiddenComponents.Any(id => !componentIds.Contains(id))
                || item.RequiredComponents.Intersect(item.ForbiddenComponents).Any())
            {
                blockers.Add("A weapon-mount component catalog row is structurally invalid.");
            }
            if (item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                && (item.Cost < 0m || item.Slots < 0 || item.Capacity < 0))
            {
                blockers.Add("An exact weapon-mount component has an invalid cost, slot, or capacity projection.");
            }
        }
    }

    private static TransactionLookup FindTransaction(string characterXml, string keyDigest)
    {
        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement[] matches = document.Root?.Elements("vehicles").SelectMany(container => container.Elements("vehicle"))
                .Where(vehicle => TryReadTransaction(vehicle, out TransactionMetadata metadata)
                                  && string.Equals(metadata.IdempotencyKeyDigest, keyDigest, StringComparison.Ordinal))
                .Take(2).ToArray() ?? [];
            if (matches.Length != 1)
                return new TransactionLookup(null, string.Empty, matches.Length > 1);
            return TryReadTransaction(matches[0], out TransactionMetadata found)
                ? new TransactionLookup(matches[0], found.CommandDigest, false)
                : new TransactionLookup(null, string.Empty, true);
        }
        catch (System.Xml.XmlException)
        {
            return new TransactionLookup(null, string.Empty, false);
        }
    }

    private static bool TryReadTransaction(XElement vehicle, out TransactionMetadata metadata)
    {
        metadata = default;
        XElement[] transactions = vehicle.Elements("workshoptransaction").Take(2).ToArray();
        if (transactions.Length != 1)
            return false;
        XElement node = transactions[0];
        if (!TryReadScalar(node, "version", out string version)
            || version != CharacterVehicleWorkshopRules.SemanticsVersion
            || !TryReadScalar(node, "idempotencydigest", out string key)
            || !TryReadScalar(node, "commanddigest", out string command)
            || !TryReadScalar(node, "catalogdigest", out string catalog)
            || !TryReadScalar(node, "quotedigest", out string quote)
            || !TryReadLong(node, "previousrevision", out long previousRevision)
            || !TryReadScalar(node, "previouscharacterdigest", out string previousDigest)
            || !TryReadDecimal(node, "previousnuyen", out decimal previousNuyen)
            || !TryReadLong(node, "commitrevision", out long commitRevision)
            || !TryReadGuid(node, "vehicleid", out Guid vehicleId)
            || !TryReadGuid(node, "expenseid", out Guid expenseId)
            || !TryReadDecimal(node, "nuyendelta", out decimal delta)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(key)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(command)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(catalog)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(quote)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(previousDigest)
            || previousRevision < 0 || commitRevision != previousRevision + 1
            || vehicleId == Guid.Empty || expenseId == Guid.Empty || previousNuyen < 0m || delta > 0m)
        {
            return false;
        }
        metadata = new TransactionMetadata(key, command, catalog, quote, previousRevision,
            previousDigest, previousNuyen, commitRevision, vehicleId, expenseId, delta);
        return true;
    }

    private static bool MetadataMatchesReceipt(
        TransactionMetadata metadata,
        CharacterVehicleWorkshopCommitReceipt receipt)
        => metadata.IdempotencyKeyDigest == receipt.IdempotencyKeyDigest
           && metadata.CommandDigest == receipt.CommandDigest
           && metadata.CatalogDigest == receipt.CatalogDigest
           && metadata.QuoteDigest == receipt.QuoteDigest
           && metadata.PreviousRevision == receipt.PreviousContentRevision
           && metadata.PreviousCharacterDigest == receipt.PreviousCharacterDigest
           && metadata.PreviousNuyen == receipt.PreviousAvailableNuyen
           && metadata.CommitRevision == receipt.ContentRevision
           && metadata.VehicleId == receipt.VehicleInstanceId.Value
           && metadata.ExpenseId == receipt.ExpenseId
           && metadata.NuyenDelta == receipt.NuyenDelta;

    private static XDocument? TryParseCharacter(string characterXml, List<string> blockers)
    {
        try
        {
            XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
            XElement? root = document.Root;
            if (root is null || root.Name.NamespaceName.Length != 0 || root.Name.LocalName != "character" || root.HasAttributes)
            {
                blockers.Add("The saved character XML root is unsupported.");
                return null;
            }
            return document;
        }
        catch (System.Xml.XmlException)
        {
            blockers.Add("The saved character XML is malformed.");
            return null;
        }
    }

    private static CharacterVehicleWorkshopCommitResult Blocked(
        string characterXml,
        long currentContentRevision,
        CharacterVehicleInstanceId vehicleId,
        Guid expenseId,
        string reason)
    {
        string digest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(characterXml);
        return new CharacterVehicleWorkshopCommitResult(
            CharacterVehicleWorkshopCommitStatus.Blocked, reason, currentContentRevision, currentContentRevision,
            digest, digest, characterXml, vehicleId, expenseId, 0m, Receipt: null);
    }

    private static XElement GetOrCreateUniqueContainer(XElement root, string name)
    {
        XElement[] matches = root.Elements(name).Take(2).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException($"Ambiguous {name} container.");
        if (matches.Length == 1)
            return matches[0];
        var container = new XElement(name);
        root.Add(container);
        return container;
    }

    private static XElement[] FindByGuid(XElement root, string containerName, string itemName, Guid id)
        => root.Elements(containerName).Take(2).SelectMany(container => container.Elements(itemName))
            .Where(node => TryReadGuid(node, "guid", out Guid candidate) && candidate == id).Take(2).ToArray();

    private static IReadOnlyList<Guid> CommandInstanceIdentities(CharacterVehicleWorkshopCommitCommand command)
    {
        var result = new List<Guid> { command.Selection.NewVehicleInstanceId.Value };
        foreach (CharacterVehicleWorkshopModificationSelection modification in command.Selection.Modifications)
        {
            result.Add(modification.InstanceId.Value);
        }
        foreach (CharacterVehicleWeaponMountSelection mount in command.Selection.WeaponMounts)
        {
            result.Add(mount.InstanceId.Value);
            foreach (CharacterVehicleWeaponMountComponentSelection component in mount.Components)
                result.Add(component.InstanceId.Value);
        }
        return result;
    }

    private static IEnumerable<Guid> FactoryGearInstanceIdentities(
        CharacterVehicleWorkshopChassisEntry chassis,
        CharacterVehicleInstanceId vehicleInstanceId)
        => chassis.FactoryGears.Select(item => CharacterVehicleWorkshopRules
            .DeriveFactoryGearInstanceId(vehicleInstanceId, item.ProjectionId).Value);

    private static bool ContainsGuid(XElement root, Guid value)
        => root.Descendants().Any(node => node.Name.NamespaceName.Length == 0
            && node.Name.LocalName is "guid" or "id" or "sizeoptioninstance"
            && Guid.TryParse(node.Value, out Guid candidate) && candidate == value);

    private static bool TryValidateIdempotencyKey(string? value)
        => value is { Length: > 0 and <= CharacterVehicleWorkshopRules.MaximumIdempotencyKeyLength }
           && !string.IsNullOrWhiteSpace(value)
           && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool ValidProjection(CharacterVehicleWorkshopProjectionStatus status, string reason)
        => Enum.IsDefined(status)
           && (status == CharacterVehicleWorkshopProjectionStatus.Exact
               ? reason.Length == 0
               : !string.IsNullOrWhiteSpace(reason));

    private static bool ValidAvailability(CharacterVehicleWorkshopAvailability availability)
        => availability is not null && availability.Value >= 0 && Enum.IsDefined(availability.Legality);

    private static bool ValidRatingText(string value)
        => value is not null
           && (value.Length == 0
               || int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
               && parsed >= 0);

    private static bool ValidFixedCapacity(string value)
    {
        if (value is null)
            return false;
        if (value.Length == 0)
            return true;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int scalar) && scalar >= 0)
            return true;
        if (ValidBracketedNonNegativeInt(value))
            return true;
        int slash = value.IndexOf("/[", StringComparison.Ordinal);
        return slash > 0
            && int.TryParse(value[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)
            && prefix >= 0
            && ValidBracketedNonNegativeInt(value[(slash + 1)..]);
    }

    private static bool ValidBracketedNonNegativeInt(string value)
        => value.Length >= 3
           && value[0] == '['
           && value[^1] == ']'
           && int.TryParse(value[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
           && parsed >= 0;

    private static bool ValidOptionalFixedDecimal(string value)
        => value is not null
           && (value.Length == 0
               || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
               && parsed >= 0m);

    private static bool HasDuplicates(IEnumerable<Guid> values)
    {
        var seen = new HashSet<Guid>();
        return values.Any(value => !seen.Add(value));
    }

    private static string FormatAvailability(CharacterVehicleWorkshopAvailability value)
        => (value.AddToParent ? "+" : string.Empty)
           + value.Value.ToString(CultureInfo.InvariantCulture)
           + (value.Legality switch
           {
               CharacterVehicleWorkshopLegality.Restricted => "R",
               CharacterVehicleWorkshopLegality.Forbidden => "F",
               _ => string.Empty
           });

    private static XElement Scalar(string name, object value)
        => new(name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);

    private static bool TryReadScalar(XElement? parent, string name, out string value)
    {
        value = string.Empty;
        if (parent is null)
            return false;
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].HasAttributes || matches[0].HasElements)
            return false;
        value = matches[0].Value;
        return true;
    }

    private static bool TryReadBoolean(XElement parent, string name, out bool value)
    {
        value = false;
        return TryReadScalar(parent, name, out string text) && bool.TryParse(text, out value);
    }

    private static bool TryReadDecimal(XElement parent, string name, out decimal value)
    {
        value = 0m;
        return TryReadScalar(parent, name, out string text)
               && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadLong(XElement parent, string name, out long value)
    {
        value = 0;
        return TryReadScalar(parent, name, out string text)
               && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadGuid(XElement parent, string name, out Guid value)
    {
        value = Guid.Empty;
        return TryReadScalar(parent, name, out string text) && Guid.TryParse(text, out value);
    }

    private static string ElementDigest(XElement element)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            element.ToString(SaveOptions.DisableFormatting)))).ToLowerInvariant();

    private readonly record struct TransactionLookup(XElement? Vehicle, string CommandDigest, bool Ambiguous);

    private readonly record struct TransactionMetadata(
        string IdempotencyKeyDigest,
        string CommandDigest,
        string CatalogDigest,
        string QuoteDigest,
        long PreviousRevision,
        string PreviousCharacterDigest,
        decimal PreviousNuyen,
        long CommitRevision,
        Guid VehicleId,
        Guid ExpenseId,
        decimal NuyenDelta);
}
