using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Consumes explicit cartridge ownership wishes against the already admitted
/// Gear authority. Gear remains the canonical inventory, pricing and replay lane.
/// Explicit caliber assignments use selected typed weapon instances. This consumer
/// never infers a caliber, decrements owned quantity, or loads a magazine.</summary>
internal static class CharacterCreationAmmunitionCompletionConsumer
{
    internal static bool TrySelect(CharacterCreationGearAuthority authority,
        CharacterCreationAmmunitionPurchase purchase, out CharacterCreationGearSelection? selection, out string[] blockers)
        => TrySelect(authority, purchase, null, null, out selection, out blockers);

    internal static bool TrySelect(CharacterCreationGearAuthority authority,
        CharacterCreationAmmunitionPurchase purchase, CharacterCreationLoadoutCompletionInputs? loadoutInputs,
        CharacterCreationLoadoutSelections? loadout, out CharacterCreationGearSelection? selection,
        out string[] blockers)
    {
        selection = null;
        blockers = [];
        var option = authority.Options.SingleOrDefault(row => row.OptionId == purchase.OptionId);
        if (option is null || !CharacterCreationGearRules.IsValidOption(option))
            blockers = [CharacterCreationGearBlockers.InvalidOption];
        else if (!CharacterCreationGearRules.DigestsEqual(purchase.SourceNodeDigest, option.SourceNodeDigest))
            blockers = [CharacterCreationAmmunitionCompletionBlockers.SourceChanged];
        else if (!option.IsSelectable)
            blockers = option.Blockers.Count == 0 ? [CharacterCreationGearBlockers.UnsupportedSemantics] : option.Blockers.ToArray();
        else if (option.Availability > authority.MaximumAvailability)
            blockers = [CharacterCreationGearBlockers.AvailabilityExceeded];
        else if (purchase.Cartridges < 1 || purchase.Cartridges > authority.MaximumQuantityPerLine)
            blockers = [CharacterCreationGearBlockers.InvalidQuantity];
        else if (!TryReadCartridgeSource(option, out string? blocker))
            blockers = [blocker!];
        else
            selection = new(option.OptionId, purchase.Cartridges);
        if (selection is not null && purchase.Weapon is { } weapon)
        {
            if (loadoutInputs is null || loadout is null)
                blockers = [CharacterCreationAmmunitionCompletionBlockers.WeaponNotSelected];
            else if (!CharacterCreationLoadoutRules.TryProjectBasket(loadout, loadoutInputs.Authority,
                         out var lines, out _, out var loadoutBlockers))
                blockers = loadoutBlockers.ToArray();
            else if (!TryValidateWeapon(weapon, lines, out string? weaponBlocker))
                blockers = [weaponBlocker!];
            if (blockers.Length != 0) selection = null;
        }
        return selection is not null;
    }

    // Values come from the accepted full-basket evaluation, including its exact
    // per-cartridge price. No parallel arithmetic or new budget is introduced.
    internal static CharacterCreationAmmunitionAllocation Describe(CharacterCreationGearLine line,
        CharacterCreationAmmunitionWeaponTarget? weapon = null)
        => new(line.OptionId, line.SourceId, line.SourceNodeDigest, line.Quantity,
            line.PackageQuantity, line.PackageCost, line.TotalCost,
            weapon is null
                ? [CharacterCreationAmmunitionCompletionGaps.WeaponCompatibility,
                   CharacterCreationAmmunitionCompletionGaps.LoadedState,
                   CharacterCreationAmmunitionCompletionGaps.CombatEffects]
                : [CharacterCreationAmmunitionCompletionGaps.LoadedState,
                   CharacterCreationAmmunitionCompletionGaps.CombatEffects]) { Weapon = weapon };

    // Shared by full review, selected replay and final graph projection. Rehashing
    // a caller payload cannot substitute for source-backed compatibility checks.
    internal static bool TryValidateAssignments(
        IReadOnlyList<CharacterCreationAmmunitionAssignment>? assignments,
        IReadOnlyList<CharacterCreationGearLine> gear,
        IReadOnlyList<CharacterCreationLoadoutLine> loadout, out string[] blockers)
    {
        blockers = [];
        if (assignments is null) return true;
        if (assignments.Count > CharacterCreationPurchaseCompletionPlanner.MaximumTargets
            || assignments.Any(row => row is null || string.IsNullOrWhiteSpace(row.GearOptionId)
                || !CharacterCreationGearRules.IsCanonicalDigest(row.GearSourceNodeDigest))
            || assignments.Select(row => row.GearOptionId).Distinct(StringComparer.Ordinal).Count() != assignments.Count)
        {
            blockers = [CharacterCreationAmmunitionCompletionBlockers.AssignmentInvalid];
            return false;
        }
        foreach (var assignment in assignments)
        {
            var matches = gear.Where(row => row.OptionId == assignment.GearOptionId).ToArray();
            if (matches.Length != 1
                || !CharacterCreationGearRules.DigestsEqual(matches[0].SourceNodeDigest, assignment.GearSourceNodeDigest))
            {
                blockers = [CharacterCreationAmmunitionCompletionBlockers.SourceChanged];
                return false;
            }
            var line = matches[0];
            // Canonical Gear projection verifies its source digest, identity,
            // supported grammar, price, package quantity and total owned quantity.
            if (line.OptionId != $"gear:{line.SourceId:D}"
                || !CharacterCreationLegacySourceProjector.TryBuildGear(line, line.SourceNodeDigest, out var saved)
                || saved.Element("ammoforweapontype")?.Value != "gun"
                || !bool.TryParse(saved.Element("isflechetteammo")?.Value, out bool flechette) || flechette)
            {
                blockers = [CharacterCreationAmmunitionCompletionBlockers.UnsupportedSemantics];
                return false;
            }
            if (!TryValidateWeapon(assignment.Weapon, loadout, out string? blocker))
            {
                blockers = [blocker!];
                return false;
            }
        }
        return true;
    }

    private static bool TryValidateWeapon(CharacterCreationAmmunitionWeaponTarget? target,
        IReadOnlyList<CharacterCreationLoadoutLine> lines, out string? blocker)
    {
        blocker = CharacterCreationAmmunitionCompletionBlockers.AssignmentInvalid;
        if (target is null || string.IsNullOrWhiteSpace(target.OptionId)
            || !CharacterCreationGearRules.IsCanonicalDigest(target.SourceNodeDigest)
            || target.InstanceOrdinal < 0 || string.IsNullOrWhiteSpace(target.AmmoCategory)
            || target.AmmoCategory.Length > 256) return false;
        blocker = CharacterCreationAmmunitionCompletionBlockers.WeaponNotSelected;
        var matches = lines.Where(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
            && row.OptionId == target.OptionId).ToArray();
        if (matches.Length != 1 || target.InstanceOrdinal >= matches[0].Quantity) return false;
        var line = matches[0];
        blocker = CharacterCreationAmmunitionCompletionBlockers.WeaponSourceChanged;
        if (!CharacterCreationGearRules.DigestsEqual(line.SourceNodeDigest, target.SourceNodeDigest)) return false;
        blocker = CharacterCreationAmmunitionCompletionBlockers.UnsupportedSemantics;
        if (!CharacterCreationLoadoutRules.IsValidLine(line)) return false;
        // IsValidLine enforces bounded DTD-free source, gun family, RequireAmmo,
        // plain damage and fixed supported capacity; unknown/effect fields refuse.
        var source = XElement.Parse(line.SourceNodeXml);
        string caliber = source.Element("ammocategory")?.Value ?? string.Empty;
        if (caliber.Length == 0) caliber = source.Element("category")!.Value;
        // These legacy predicates have additional gear/name/skill semantics.
        if (caliber == "Gear" || source.Element("useskill")?.Value == "Throwing Weapons"
            || source.Element("category")?.Value == "Throwing Weapons") return false;
        blocker = CharacterCreationAmmunitionCompletionBlockers.CaliberMismatch;
        if (!string.Equals(caliber, target.AmmoCategory, StringComparison.Ordinal)) return false;
        blocker = null;
        return true;
    }

    internal static bool TryProjectAssignments(XElement root,
        IReadOnlyList<CharacterCreationAmmunitionAssignment>? assignments,
        IReadOnlyList<CharacterCreationGearLine> gear, IReadOnlyList<CharacterCreationLoadoutLine> loadout,
        string reviewDigest)
    {
        if (!CharacterCreationFinalizationDigest.IsCanonical(reviewDigest)
            || !TryValidateAssignments(assignments, gear, loadout, out _)) return false;
        foreach (var assignment in assignments ?? [])
        {
            var gearLine = gear.Single(row => row.OptionId == assignment.GearOptionId);
            var weaponLine = loadout.Single(row => row.OptionId == assignment.Weapon.OptionId);
            // Read the graph already produced by the canonical projectors, including
            // retained IDs. Never recompute an instance ID using an invented seed.
            var gearNodes = root.Element("gears")!.Elements("gear")
                .Where(row => row.Element("sourceid")?.Value == gearLine.SourceId.ToString("D")).ToArray();
            var weaponNodes = root.Element("weapons")!.Elements("weapon")
                .Where(row => row.Element("sourceid")?.Value == weaponLine.SourceId.ToString("D")).ToArray();
            if (gearNodes.Length != 1 || weaponNodes.Length != weaponLine.Quantity) return false;
            var ammunition = gearNodes[0];
            var weapon = weaponNodes[assignment.Weapon.InstanceOrdinal];
            if (!Guid.TryParse(weapon.Element("guid")?.Value, out Guid weaponId) || weaponId == Guid.Empty
                || !Guid.TryParse(ammunition.Element("guid")?.Value, out Guid gearId) || gearId == Guid.Empty
                || ammunition.Element("extra")?.Value != string.Empty
                || ammunition.Element("ammunitionassignment") is not null) return false;
            // Extra is the legacy caliber discriminator. The separate Core association
            // preserves the exact reviewed instance; neither field represents loading.
            ammunition.Element("extra")!.Value = assignment.Weapon.AmmoCategory;
            ammunition.Add(new XElement("ammunitionassignment",
                new XElement("gearinstanceid", gearId.ToString("D")),
                new XElement("gearoptionid", assignment.GearOptionId),
                new XElement("gearsourcedigest", assignment.GearSourceNodeDigest),
                new XElement("weaponinstanceid", weaponId.ToString("D")),
                new XElement("weaponoptionid", assignment.Weapon.OptionId),
                new XElement("weaponsourcedigest", assignment.Weapon.SourceNodeDigest),
                new XElement("weaponinstanceordinal", assignment.Weapon.InstanceOrdinal.ToString(CultureInfo.InvariantCulture)),
                new XElement("ammocategory", assignment.Weapon.AmmoCategory),
                new XElement("reviewdigest", reviewDigest)));
        }
        return true;
    }

    private static bool TryReadCartridgeSource(CharacterCreationGearCatalogOption option, out string? blocker)
    {
        blocker = CharacterCreationAmmunitionCompletionBlockers.UnsupportedSemantics;
        try
        {
            using var reader = XmlReader.Create(new StringReader(option.SourceNodeXml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = CharacterCreationGearRules.MaximumSourceNodeLength
            });
            var source = XElement.Load(reader);
            // Admit bounded, DTD-free XML before invoking the existing parser.
            // Its prompt-free Gear grammar rejects bonuses, child/add/select
            // requirements, ratings, duplicate fields and unknown XML.
            if (!CharacterCreationLegacySourceProjector.IsGearSourceProjectable(option.SourceNodeXml)) return false;
            var family = source.Element("ammoforweapontype");
            // This is the canonical weapon-family token, not a display name or
            // category heuristic. Other families need their own evaluated rules.
            if (family is null || family.HasAttributes || family.HasElements || family.Value != "gun"
                || source.Element("isflechetteammo") is { } flechette
                    && (!bool.TryParse(flechette.Value, out bool value) || value)) return false;

            blocker = CharacterCreationAmmunitionCompletionBlockers.SourceMismatch;
            if (!Guid.TryParse(source.Element("id")?.Value, out Guid id) || id != option.SourceId
                || option.OptionId != $"gear:{id:D}"
                || source.Element("name")?.Value != option.Name
                || source.Element("category")?.Value != option.Category
                || source.Element("source")?.Value != option.SourceBook
                || source.Element("page")?.Value != option.Page
                || !decimal.TryParse(source.Element("cost")?.Value, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal packageCost) || packageCost != option.PackageCost
                || !int.TryParse(source.Element("costfor")?.Value ?? "1", NumberStyles.None,
                    CultureInfo.InvariantCulture, out int packageQuantity) || packageQuantity != option.PackageQuantity
                || !CharacterCreationLoadoutRules.TryReadAvailability(source.Element("avail")?.Value ?? string.Empty,
                    out int availability, out string legality)
                || availability != option.Availability || legality != option.Legality) return false;
            blocker = null;
            return true;
        }
        catch (XmlException) { return false; }
    }
}
