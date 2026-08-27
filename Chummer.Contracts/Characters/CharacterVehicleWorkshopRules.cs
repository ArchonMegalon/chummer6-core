using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public static class CharacterVehicleWorkshopBlockers
{
    public const string NotCareer = "The Vehicle & Drone Workshop is available only for a saved Career character with created=true.";
    public const string SourceAuthorityUnavailable = "The exact SR5 vehicle source/profile authority is unavailable.";
    public const string CatalogAltered = "The typed Vehicle & Drone Workshop catalog digest is absent or altered.";
    public const string StaleRevision = "The character content revision changed after the workshop quote was prepared.";
    public const string StaleCharacter = "The character bytes changed after the workshop quote was prepared.";
    public const string StaleCatalog = "The exact source/profile catalog changed after the workshop quote was prepared.";
    public const string StaleQuote = "The workshop quote changed after confirmation.";
    public const string IdempotencyKeyInvalid = "The workshop idempotency key is invalid.";
    public const string IdempotencyConflict = "The workshop idempotency key was already used for a different command.";
    public const string IdentityInvalid = "Workshop source, instance, component, and expense identities must be valid and distinct.";
    public const string UnsupportedSelection = "At least one selected source row has semantics that were not projected exactly.";
    public const string CapacityExceeded = "The selected composition exceeds the chassis capacity.";
    public const string SlotsExceeded = "The selected composition exceeds the chassis modification slots.";
    public const string InsufficientNuyen = "The exact workshop price exceeds available Nuyen.";
    public const string GmAuthorityRequired = "A custom chassis requires the exact GM authorization digest projected by the catalog.";
    public const string StaleReceipt = "The commit-issued Vehicle & Drone Workshop receipt is stale or altered.";
}

public static class CharacterVehicleWorkshopRules
{
    public const string RulesetId = "SR5";
    public const string SemanticsVersion = "chummer-sr5-vehicle-workshop-v2";
    public const int MaximumIdempotencyKeyLength = 200;
    public const int MaximumCustomNameLength = 200;

    public static CharacterVehicleWorkshopQuote Quote(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopSelection selection)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(selection);

        var blockers = new List<string>();
        var lines = new List<CharacterVehicleWorkshopQuoteLine>();
        string customName = selection.CustomName ?? string.Empty;
        if (!preparation.Exact)
            blockers.AddRange(preparation.Blockers);
        if (selection.NewVehicleInstanceId.Value == Guid.Empty)
            blockers.Add(CharacterVehicleWorkshopBlockers.IdentityInvalid);

        CharacterVehicleWorkshopChassisEntry[] chassisMatches = preparation.Chassis
            .Where(candidate => candidate.SourceId == selection.ChassisSourceId)
            .Take(2)
            .ToArray();
        CharacterVehicleWorkshopChassisEntry? chassis = chassisMatches.Length == 1 ? chassisMatches[0] : null;
        if (chassis is null)
            blockers.Add("The selected chassis source identity is absent or ambiguous.");
        else if (chassis.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact)
            blockers.Add(UnsupportedReason(chassis.UnsupportedReason));

        if (chassis is not null
            && chassis.Posture == CharacterVehicleChassisPosture.GmApprovedCustom
            && (!IsCanonicalDigest(selection.GmAuthorityDigest)
                || !FixedEquals(selection.GmAuthorityDigest, chassis.GmAuthorityDigest)))
        {
            blockers.Add(CharacterVehicleWorkshopBlockers.GmAuthorityRequired);
        }
        if (chassis is not null
            && chassis.Posture == CharacterVehicleChassisPosture.Stock
            && selection.GmAuthorityDigest.Length != 0)
        {
            blockers.Add("A stock chassis must not carry a custom GM authorization digest.");
        }
        if (selection.CustomName is null
            || customName.Length > MaximumCustomNameLength
            || !string.Equals(customName, customName.Trim(), StringComparison.Ordinal))
        {
            blockers.Add("The optional vehicle name must be trimmed and no longer than 200 characters.");
        }

        decimal totalCost = chassis?.Cost ?? 0m;
        int slotsUsed = 0;
        int capacityUsed = 0;
        CharacterVehicleWorkshopAvailability availability = chassis?.Availability
            ?? new CharacterVehicleWorkshopAvailability(0, CharacterVehicleWorkshopLegality.Legal, false);

        try
        {
            foreach (CharacterVehicleWorkshopModificationSelection selected in selection.Modifications ?? [])
            {
                CharacterVehicleWorkshopModificationEntry[] matches = preparation.Modifications
                    .Where(candidate => candidate.SourceId == selected.SourceId)
                    .Take(2)
                    .ToArray();
                CharacterVehicleWorkshopModificationEntry? entry = matches.Length == 1 ? matches[0] : null;
                string reason = entry is null
                    ? "The selected modification source identity is absent or ambiguous."
                    : entry.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact
                        ? UnsupportedReason(entry.UnsupportedReason)
                        : chassis is not null && entry.AllowedChassis.Count != 0
                          && !entry.AllowedChassis.Contains(chassis.SourceId)
                            ? "The selected modification is not legal for this chassis."
                            : selected.Rating < entry.MinimumRating || selected.Rating > entry.MaximumRating
                                ? "The selected modification rating is outside the exact source range."
                                : string.Empty;
                decimal cost = entry is null ? 0m : checked(entry.BaseCost + entry.CostPerRating * selected.Rating);
                int slots = entry is null ? 0 : checked(entry.BaseSlots + entry.SlotsPerRating * selected.Rating);
                int capacity = entry is null ? 0 : checked(entry.BaseCapacity + entry.CapacityPerRating * selected.Rating);
                lines.Add(new CharacterVehicleWorkshopQuoteLine(
                    "modification", selected.SourceId.Value, selected.InstanceId.Value,
                    entry?.Name ?? string.Empty, selected.Rating, cost, slots, capacity,
                    entry?.Availability ?? new CharacterVehicleWorkshopAvailability(0, CharacterVehicleWorkshopLegality.Legal, false),
                    reason.Length == 0, reason));
                if (reason.Length != 0)
                    blockers.Add(reason);
                totalCost = checked(totalCost + cost);
                slotsUsed = checked(slotsUsed + slots);
                capacityUsed = checked(capacityUsed + capacity);
                if (entry is not null)
                    availability = Combine(availability, entry.Availability);
            }

            foreach (CharacterVehicleWeaponMountSelection mount in selection.WeaponMounts ?? [])
            {
                CharacterVehicleWeaponMountComponentSelection[] components = mount.Components?.ToArray() ?? [];
                var selectedEntries = new List<CharacterVehicleWeaponMountComponentEntry>();
                var selectedIds = components.Select(component => component.SourceId).ToHashSet();
                bool exactComposition = components.Length == 4
                    && components.Select(component => component.SourceId).Distinct().Count() == 4
                    && components.Select(component => component.InstanceId).Distinct().Count() == 4;
                foreach (CharacterVehicleWeaponMountComponentSelection component in components)
                {
                    CharacterVehicleWeaponMountComponentEntry[] matches = preparation.WeaponMountComponents
                        .Where(candidate => candidate.SourceId == component.SourceId)
                        .Take(2)
                        .ToArray();
                    CharacterVehicleWeaponMountComponentEntry? entry = matches.Length == 1 ? matches[0] : null;
                    string reason = entry is null
                        ? "The selected weapon-mount component identity is absent or ambiguous."
                        : entry.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact
                            ? UnsupportedReason(entry.UnsupportedReason)
                            : chassis is not null && entry.AllowedChassis.Count != 0
                              && !entry.AllowedChassis.Contains(chassis.SourceId)
                                ? "The selected weapon-mount component is not legal for this chassis."
                                : entry.RequiredComponents.Any(required => !selectedIds.Contains(required))
                                    ? "The weapon-mount composition omits a required component."
                                    : entry.ForbiddenComponents.Any(selectedIds.Contains)
                                        ? "The weapon-mount composition contains a forbidden component."
                                        : string.Empty;
                    if (entry is not null)
                        selectedEntries.Add(entry);
                    lines.Add(new CharacterVehicleWorkshopQuoteLine(
                        "weapon-mount-component", component.SourceId.Value, component.InstanceId.Value,
                        entry?.Name ?? string.Empty, 1, entry?.Cost ?? 0m, entry?.Slots ?? 0,
                        entry?.Capacity ?? 0,
                        entry?.Availability ?? new CharacterVehicleWorkshopAvailability(0, CharacterVehicleWorkshopLegality.Legal, false),
                        reason.Length == 0, reason));
                    if (reason.Length != 0)
                        blockers.Add(reason);
                }
                if (!exactComposition
                    || selectedEntries.Select(entry => entry.Kind).Distinct().Count() != 4
                    || selectedEntries.Any(entry => !Enum.IsDefined(entry.Kind)))
                {
                    blockers.Add("A weapon mount requires one exact Size, Visibility, Flexibility, and Control component.");
                }
                foreach (CharacterVehicleWeaponMountComponentEntry entry in selectedEntries)
                {
                    totalCost = checked(totalCost + entry.Cost);
                    slotsUsed = checked(slotsUsed + entry.Slots);
                    capacityUsed = checked(capacityUsed + entry.Capacity);
                    availability = Combine(availability, entry.Availability);
                }
            }
        }
        catch (OverflowException)
        {
            blockers.Add("Workshop cost, slot, or capacity arithmetic exceeded the exact saved-data range.");
        }

        try
        {
            if (availability.Legality == CharacterVehicleWorkshopLegality.Restricted
                && preparation.Binding.MultiplyRestrictedCost)
            {
                totalCost = checked(totalCost * preparation.Binding.RestrictedCostMultiplier);
            }
            else if (availability.Legality == CharacterVehicleWorkshopLegality.Forbidden
                     && preparation.Binding.MultiplyForbiddenCost)
            {
                totalCost = checked(totalCost * preparation.Binding.ForbiddenCostMultiplier);
            }
        }
        catch (OverflowException)
        {
            blockers.Add("Workshop profile cost multiplication exceeded the exact saved-data range.");
        }

        if (chassis is not null && slotsUsed > chassis.ModificationSlots)
            blockers.Add(CharacterVehicleWorkshopBlockers.SlotsExceeded);
        if (chassis is not null && capacityUsed > chassis.ModificationCapacity)
            blockers.Add(CharacterVehicleWorkshopBlockers.CapacityExceeded);
        if (totalCost < 0m || totalCost > preparation.AvailableNuyen)
            blockers.Add(CharacterVehicleWorkshopBlockers.InsufficientNuyen);
        if (!HasDistinctIdentities(selection))
            blockers.Add(CharacterVehicleWorkshopBlockers.IdentityInvalid);

        string[] normalized = Normalize(blockers);
        var unsigned = new CharacterVehicleWorkshopQuote(
            Exact: normalized.Length == 0,
            Blockers: normalized,
            selection.ChassisSourceId,
            selection.NewVehicleInstanceId,
            DisplayName: customName.Length == 0 ? chassis?.Name ?? string.Empty : customName,
            Kind: chassis?.Kind ?? CharacterVehicleChassisKind.Vehicle,
            Posture: chassis?.Posture ?? CharacterVehicleChassisPosture.Stock,
            TotalCost: totalCost,
            NuyenDelta: normalized.Length == 0 ? -totalCost : 0m,
            SlotsUsed: slotsUsed,
            SlotsRemaining: chassis is null ? 0 : chassis.ModificationSlots - slotsUsed,
            CapacityUsed: capacityUsed,
            CapacityRemaining: chassis is null ? 0 : chassis.ModificationCapacity - capacityUsed,
            availability,
            Lines: lines.ToArray(),
            QuoteDigest: string.Empty);
        return normalized.Length == 0
            ? unsigned with { QuoteDigest = ComputeQuoteDigest(preparation, selection, unsigned) }
            : unsigned;
    }

    public static string ComputeCatalogDigest(CharacterVehicleWorkshopCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        CharacterVehicleWorkshopSourceBinding binding = catalog.Binding
            ?? new CharacterVehicleWorkshopSourceBinding(string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                false, 0m, false, 0m, false);
        var lines = new List<string>
        {
            "sr5-vehicle-workshop-catalog-v2",
            binding.RulesetId,
            binding.ProfileId,
            binding.SemanticsVersion,
            binding.ProfileDigest,
            binding.VehiclesDigest,
            binding.WeaponsDigest,
            binding.GearDigest,
            binding.OverlayDigest,
            binding.MultiplyRestrictedCost.ToString(CultureInfo.InvariantCulture),
            binding.RestrictedCostMultiplier.ToString(CultureInfo.InvariantCulture),
            binding.MultiplyForbiddenCost.ToString(CultureInfo.InvariantCulture),
            binding.ForbiddenCostMultiplier.ToString(CultureInfo.InvariantCulture),
            binding.Exact.ToString(CultureInfo.InvariantCulture)
        };
        lines.AddRange((catalog.Chassis ?? []).OrderBy(item => item?.SourceId.Value ?? Guid.Empty)
            .Select(item => item is null ? "null-chassis" : CanonicalChassis(item)));
        lines.AddRange((catalog.Modifications ?? []).OrderBy(item => item?.SourceId.Value ?? Guid.Empty)
            .Select(item => item is null ? "null-modification" : CanonicalModification(item)));
        lines.AddRange((catalog.WeaponMountComponents ?? []).OrderBy(item => item?.SourceId.Value ?? Guid.Empty)
            .Select(item => item is null ? "null-mount-component" : CanonicalComponent(item)));
        return Digest(string.Join("\n", lines));
    }

    public static string ComputeCharacterDigest(string characterXml) => Digest(characterXml ?? string.Empty);

    public static CharacterVehicleFactoryGearProjectionId DeriveFactoryGearProjectionId(
        CharacterVehicleChassisSourceId chassisSourceId,
        CharacterVehicleFactoryGearSourceId gearSourceId,
        int ordinal,
        string instructionNodeDigest)
        => new(DeriveGuid(string.Join("\n",
            "sr5-vehicle-workshop-factory-gear-projection-v1",
            chassisSourceId.Value.ToString("D"),
            gearSourceId.Value.ToString("D"),
            ordinal.ToString(CultureInfo.InvariantCulture),
            instructionNodeDigest ?? string.Empty)));

    public static CharacterVehicleFactoryGearInstanceId DeriveFactoryGearInstanceId(
        CharacterVehicleInstanceId vehicleInstanceId,
        CharacterVehicleFactoryGearProjectionId projectionId)
        => new(DeriveGuid(string.Join("\n",
            "sr5-vehicle-workshop-factory-gear-instance-v1",
            vehicleInstanceId.Value.ToString("D"),
            projectionId.Value.ToString("D"))));

    public static string ComputeIdempotencyKeyDigest(string value) => Digest(value ?? string.Empty);

    public static string ComputeCommandDigest(CharacterVehicleWorkshopCommitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CharacterVehicleWorkshopSelection selection = command.Selection;
        var lines = new List<string>
        {
            "sr5-vehicle-workshop-command-v2",
            command.ExpectedContentRevision.ToString(CultureInfo.InvariantCulture),
            command.ExpectedCharacterDigest,
            command.ExpectedCatalogDigest,
            command.ExpectedQuoteDigest,
            ComputeIdempotencyKeyDigest(command.IdempotencyKey),
            command.NewExpenseId.ToString("D"),
            command.ExpenseDate.ToString("O", CultureInfo.InvariantCulture),
            selection.ChassisSourceId.Value.ToString("D"),
            selection.NewVehicleInstanceId.Value.ToString("D"),
            selection.CustomName,
            selection.GmAuthorityDigest
        };
        lines.AddRange((selection.Modifications ?? []).Select(item => string.Join("|",
            "mod", item.SourceId.Value.ToString("D"), item.InstanceId.Value.ToString("D"),
            item.Rating.ToString(CultureInfo.InvariantCulture))));
        foreach (CharacterVehicleWeaponMountSelection mount in selection.WeaponMounts ?? [])
        {
            lines.Add($"mount|{mount.InstanceId.Value:D}");
            lines.AddRange((mount.Components ?? []).Select(item => string.Join("|",
                "component", item.SourceId.Value.ToString("D"), item.InstanceId.Value.ToString("D"))));
        }
        return Digest(string.Join("\n", lines));
    }

    public static string ComputeReceiptDigest(CharacterVehicleWorkshopCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return Digest(string.Join("\n",
            "sr5-vehicle-workshop-receipt-v2",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture),
            receipt.CharacterDigest,
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture),
            receipt.PreviousCharacterDigest,
            receipt.PreviousAvailableNuyen.ToString(CultureInfo.InvariantCulture),
            receipt.CatalogDigest,
            receipt.QuoteDigest,
            receipt.IdempotencyKeyDigest,
            receipt.CommandDigest,
            receipt.VehicleInstanceId.Value.ToString("D"),
            receipt.ExpenseId.ToString("D"),
            receipt.NuyenDelta.ToString(CultureInfo.InvariantCulture),
            receipt.VehicleXmlDigest,
            receipt.ExpenseXmlDigest,
            receipt.UndoReady.ToString(CultureInfo.InvariantCulture)));
    }

    public static bool IsCanonicalDigest(string? digest)
        => digest is { Length: 64 } && digest.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool FixedEquals(string? left, string? right)
    {
        if (!IsCanonicalDigest(left) || !IsCanonicalDigest(right))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right!));
    }

    public static string[] Normalize(IEnumerable<string> blockers)
        => blockers.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static bool HasDistinctIdentities(CharacterVehicleWorkshopSelection selection)
    {
        var sourceIdentities = new List<Guid> { selection.ChassisSourceId.Value };
        var instanceIdentities = new List<Guid> { selection.NewVehicleInstanceId.Value };
        foreach (CharacterVehicleWorkshopModificationSelection modification in selection.Modifications ?? [])
        {
            sourceIdentities.Add(modification.SourceId.Value);
            instanceIdentities.Add(modification.InstanceId.Value);
        }
        foreach (CharacterVehicleWeaponMountSelection mount in selection.WeaponMounts ?? [])
        {
            instanceIdentities.Add(mount.InstanceId.Value);
            foreach (CharacterVehicleWeaponMountComponentSelection component in mount.Components ?? [])
            {
                sourceIdentities.Add(component.SourceId.Value);
                instanceIdentities.Add(component.InstanceId.Value);
            }
        }
        return sourceIdentities.All(value => value != Guid.Empty)
               && instanceIdentities.All(value => value != Guid.Empty)
               && instanceIdentities.Distinct().Count() == instanceIdentities.Count
               && !sourceIdentities.Intersect(instanceIdentities).Any();
    }

    private static CharacterVehicleWorkshopAvailability Combine(
        CharacterVehicleWorkshopAvailability left,
        CharacterVehicleWorkshopAvailability right)
        => new(checked(left.Value + (right.AddToParent ? right.Value : 0)),
            (CharacterVehicleWorkshopLegality)Math.Max((int)left.Legality, (int)right.Legality),
            AddToParent: false);

    private static string UnsupportedReason(string reason)
        => string.IsNullOrWhiteSpace(reason) ? CharacterVehicleWorkshopBlockers.UnsupportedSelection : reason;

    private static string ComputeQuoteDigest(
        CharacterVehicleWorkshopPreparation preparation,
        CharacterVehicleWorkshopSelection selection,
        CharacterVehicleWorkshopQuote quote)
    {
        var lines = new List<string>
        {
            "sr5-vehicle-workshop-quote-v2",
            preparation.ContentRevision.ToString(CultureInfo.InvariantCulture),
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            selection.ChassisSourceId.Value.ToString("D"),
            selection.NewVehicleInstanceId.Value.ToString("D"),
            selection.CustomName,
            selection.GmAuthorityDigest,
            quote.TotalCost.ToString(CultureInfo.InvariantCulture),
            quote.NuyenDelta.ToString(CultureInfo.InvariantCulture),
            quote.SlotsUsed.ToString(CultureInfo.InvariantCulture),
            quote.CapacityUsed.ToString(CultureInfo.InvariantCulture),
            quote.Availability.Value.ToString(CultureInfo.InvariantCulture),
            quote.Availability.Legality.ToString(),
            quote.Availability.AddToParent.ToString(CultureInfo.InvariantCulture)
        };
        lines.AddRange(quote.Lines.Select(line => string.Join("|",
            line.Kind, line.SourceId.ToString("D"), line.InstanceId.ToString("D"), line.Name,
            line.Rating.ToString(CultureInfo.InvariantCulture), line.Cost.ToString(CultureInfo.InvariantCulture),
            line.Slots.ToString(CultureInfo.InvariantCulture), line.Capacity.ToString(CultureInfo.InvariantCulture),
            line.Availability.Value.ToString(CultureInfo.InvariantCulture), line.Availability.Legality.ToString(),
            line.Availability.AddToParent.ToString(CultureInfo.InvariantCulture))));
        return Digest(string.Join("\n", lines));
    }

    private static string CanonicalChassis(CharacterVehicleWorkshopChassisEntry item)
    {
        CharacterVehicleWorkshopAvailability availability = item.Availability
            ?? new CharacterVehicleWorkshopAvailability(-1, (CharacterVehicleWorkshopLegality)(-1), false);
        string chassis = string.Join("|", "chassis", item.SourceId.Value.ToString("D"), item.Kind, item.Posture,
            item.Name, item.Category, item.Handling, item.OffRoadHandling, item.Acceleration,
            item.OffRoadAcceleration, item.Speed, item.OffRoadSpeed, item.Pilot, item.Body,
            item.Seats, item.Armor, item.Sensor, item.ModificationSlots, item.ModificationCapacity,
            item.Cost.ToString(CultureInfo.InvariantCulture), availability.Value,
            availability.Legality, availability.AddToParent, item.SourceBook, item.Page, item.GmAuthorityDigest,
            item.ProjectionStatus, item.UnsupportedReason);
        return string.Join("\n", new[] { chassis }.Concat((item.FactoryGears ?? [])
            .OrderBy(gear => gear?.Ordinal ?? int.MinValue)
            .Select(gear => gear is null ? "null-factory-gear" : CanonicalFactoryGear(gear))));
    }

    private static string CanonicalFactoryGear(CharacterVehicleWorkshopFactoryGearEntry item)
    {
        CharacterVehicleWorkshopAvailability availability = item.Availability
            ?? new CharacterVehicleWorkshopAvailability(-1, (CharacterVehicleWorkshopLegality)(-1), false);
        return string.Join("|", "factory-gear", item.ProjectionId.Value.ToString("D"),
            item.ChassisSourceId.Value.ToString("D"), item.Ordinal, item.SourceId.Value.ToString("D"),
            item.Name, item.Category, item.Capacity, item.ArmorCapacity, item.MinimumRating,
            item.MaximumRating, item.Rating, item.Quantity.ToString(CultureInfo.InvariantCulture),
            availability.Value, availability.Legality, availability.AddToParent, item.Weight,
            item.SourceBook, item.Page, item.ConsumeCapacity, item.SourceNodeDigest,
            item.InstructionNodeDigest, item.ProjectionStatus, item.UnsupportedReason);
    }

    private static string CanonicalModification(CharacterVehicleWorkshopModificationEntry item)
    {
        CharacterVehicleWorkshopAvailability availability = item.Availability
            ?? new CharacterVehicleWorkshopAvailability(-1, (CharacterVehicleWorkshopLegality)(-1), false);
        return string.Join("|", "mod", item.SourceId.Value.ToString("D"), item.Name, item.Category,
            item.MinimumRating, item.MaximumRating, item.BaseCost.ToString(CultureInfo.InvariantCulture),
            item.CostPerRating.ToString(CultureInfo.InvariantCulture), item.BaseSlots, item.SlotsPerRating,
            item.BaseCapacity, item.CapacityPerRating, availability.Value,
            availability.Legality, availability.AddToParent, item.SourceBook, item.Page,
            string.Join(",", (item.AllowedChassis ?? []).OrderBy(id => id.Value).Select(id => id.Value.ToString("D"))),
            item.ProjectionStatus, item.UnsupportedReason);
    }

    private static string CanonicalComponent(CharacterVehicleWeaponMountComponentEntry item)
    {
        CharacterVehicleWorkshopAvailability availability = item.Availability
            ?? new CharacterVehicleWorkshopAvailability(-1, (CharacterVehicleWorkshopLegality)(-1), false);
        return string.Join("|", "mount", item.SourceId.Value.ToString("D"), item.Kind, item.Name,
            item.Cost.ToString(CultureInfo.InvariantCulture), item.Slots, item.Capacity,
            availability.Value, availability.Legality, availability.AddToParent, item.SourceBook, item.Page,
            string.Join(",", (item.AllowedChassis ?? []).OrderBy(id => id.Value).Select(id => id.Value.ToString("D"))),
            string.Join(",", (item.RequiredComponents ?? []).OrderBy(id => id.Value).Select(id => id.Value.ToString("D"))),
            string.Join(",", (item.ForbiddenComponents ?? []).OrderBy(id => id.Value).Select(id => id.Value.ToString("D"))),
            item.ProjectionStatus, item.UnsupportedReason);
    }

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Guid DeriveGuid(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes.AsSpan(0, 16));
    }
}
