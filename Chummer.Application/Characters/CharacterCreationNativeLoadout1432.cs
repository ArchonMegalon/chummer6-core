using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;

namespace Chummer.Application.Characters;

internal static class CharacterCreationNativeLoadout1432
{
    internal static CharacterCreationNativeLoadoutProjection1432? Evaluate(XElement graph, string? schema,
        AwakenedGraphInputs inputs, CharacterCreationPrerequisiteDraft draft,
        CharacterCreationLoadoutCompletionProjection? loadout3478 = null,
        CharacterCreationOriginalInventory3475? originalLoadout3478 = null, string? instanceSeed3497 = null)
        => schema == CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            ? Evaluate(graph, CharacterCreationAstralInitiative1249.Source(inputs), draft, loadout3478, originalLoadout3478, instanceSeed3497) : null;

    internal static CharacterCreationNativeLoadoutProjection1432? Evaluate(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft,
        CharacterCreationLoadoutCompletionProjection? loadout3478 = null,
        CharacterCreationOriginalInventory3475? originalLoadout3478 = null, string? instanceSeed3497 = null)
    {
        if (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            || source!.SettingsProfileId != draft.SettingsProfileId || graph.Name != "character"
            || Leaf(graph, "gameedition")?.Value != "SR5"
            || Leaf(graph, "created") is not { } saved || !bool.TryParse(saved.Value, out bool created)
            || draft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen))
            return null;
        var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        if (!TryReadPolicy(profile, out var policy)) return null;
        return EvaluateGraph(graph, policy!, created, source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), loadout3478, originalLoadout3478, instanceSeed3497);
    }

    internal static bool TryReadPolicy(XElement profile, out CharacterCreationNativeLoadoutPolicy1432? policy)
    {
        policy = null;
        if (profile.Name != "setting" || profile.HasAttributes) return false;
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var definition in CharacterCreationNativeLoadoutDefinitions1432.All)
        {
            if (!profile.Elements().Any(node => string.Equals(node.Name.LocalName, definition.Element, StringComparison.OrdinalIgnoreCase)))
                continue; // An absent leaf acquires neither a default nor coverage.
            if (Leaf(profile, definition.Element) is not { } leaf) return false;
            if (definition.Kind == CharacterCreationSettingValueKind.Boolean && bool.TryParse(leaf.Value, out bool boolean))
                values.Add(definition.Element, boolean);
            else if (definition.Kind == CharacterCreationSettingValueKind.Integer
                && int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                values.Add(definition.Element, integer);
            else return false;
        }
        if (values.Count == 0) return false;
        bool? B(string field) => values.TryGetValue(field, out var value) ? (bool)value : null;
        int? I(string field) => values.TryGetValue(field, out var value) ? (int)value : null;
        policy = new(B("unarmedimprovementsapplytoweapons"), B("dronearmormultiplierenabled"), I("dronearmorflatnumber"),
            B("noarmorencumbrance"), I("limbcount"), B("enforcecapacity"), B("dronemods"), B("dronemodsmaximumpilot"));
        return true;
    }

    // Native early returns, plus a private source-proven original stock loadout.
    // Optional DTO results cannot confer the private proof needed for this path.
    // It cannot mint invocation authority; that requires the private selected consumer.
    internal static CharacterCreationNativeLoadoutProjection1432 EvaluateGraph(XElement graph,
        CharacterCreationNativeLoadoutPolicy1432 policy, bool created, string profileId,
        string profileInputsDigest, string profileXmlDigest,
        CharacterCreationLoadoutCompletionProjection? loadout3478 = null,
        CharacterCreationOriginalInventory3475? originalLoadout3478 = null, string? instanceSeed3497 = null,
        CharacterSheetOwnerGraph? importedOwners5696 = null, CharacterSheetOwnerGraph? sourceOwners5925 = null)
    {
        CharacterCreationStockLoadoutProjection3478? stock = null;
        if (originalLoadout3478 is not null
            && !CharacterCreationOriginalLoadout3478.TryEvaluate(graph, loadout3478, originalLoadout3478, out stock, instanceSeed3497))
            throw new InvalidDataException(CharacterCreationOriginalLoadout3478.Blocker);
        if (importedOwners5696 is not null && (originalLoadout3478 is not null
            || !CharacterCreationOriginalLoadout3478.TryEvaluateImported5696(graph, importedOwners5696, out stock)))
            throw new InvalidDataException("imported-stock-loadout-unresolved");
        bool noWeapons = NoObjects(graph, "weapons", "weapon");
        bool noVehicles = NoObjects(graph, "vehicles", "vehicle");
        bool noWare = NoObjects(graph, "cyberwares", "cyberware");
        // CharacterCreate validation visits gear nested in armor, mods, accessories,
        // cyberware and vehicles too. Search the whole actual graph, not selections.
        bool noOtherCapacityTargets6511 = NoObjects(graph, "armors", "armor", sourceOwners5925)
            && noWare && noVehicles
            && NoObjects(graph, "accessories", "accessory")
            && NoObjects(graph, "armormods", "armormod")
            && NoObjects(graph, "weaponmounts", "weaponmount");
        bool noCapacityTargets = noOtherCapacityTargets6511 && NoObjects(graph, "gears", "gear");
        bool retainedFocusCapacity6511 = noOtherCapacityTargets6511 && !noCapacityTargets
            && sourceOwners5925?.HasOnlyCapacityFreeRetainedFoci6511(graph) == true;
        int? limbs = policy.LimbCount is int count && NoAddLimbImprovements(graph) ? count : null;
        var capacity = policy.EnforceCapacity switch
        {
            false => CharacterCreationCapacityBranch1432.DisabledBySetting,
            true when noCapacityTargets => CharacterCreationCapacityBranch1432.NoCapacityTargets,
            true when retainedFocusCapacity6511 => CharacterCreationCapacityBranch1432.SourceProvenRetainedFocusCapacity,
            true when stock is { CapacityComplete: true } => CharacterCreationCapacityBranch1432.SourceProvenStockCapacity,
            _ => CharacterCreationCapacityBranch1432.Unsupported
        };
        // No vehicles means Vehicle's optional modification/MaxArmor/MaxPilot
        // consumers have no target. Never run an armor multiplier ahead of the
        // native IgnoreRules and IsDrone && DroneMods early returns (Vehicle.cs).
        return new(policy, created, noWeapons, noVehicles, noWare, limbs,
            policy.NoArmorEncumbrance == false && stock is { ArmorOwners: > 0 }
                ? CharacterCreationArmorEncumbranceBranch1432.SourceProvenStockArmor
                : ArmorBranch(graph, policy.NoArmorEncumbrance, sourceOwners5925), capacity,
            profileId, profileInputsDigest, profileXmlDigest,
            CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting)))
            { StockLoadout3478 = stock };
    }

    private static CharacterCreationArmorEncumbranceBranch1432 ArmorBranch(XElement graph, bool? disabled,
        CharacterSheetOwnerGraph? sourceOwners5925)
    {
        // Character.ArmorEncumbrance checks this setting before reading inventory.
        if (disabled == true) return CharacterCreationArmorEncumbranceBranch1432.DisabledBySetting;
        if (disabled is null) return CharacterCreationArmorEncumbranceBranch1432.Unsupported;
        if (!TryCollection(graph, "armors", "armor", out var armor, sourceOwners5925)) return CharacterCreationArmorEncumbranceBranch1432.Unsupported;
        var equipped = new List<XElement>();
        foreach (var item in armor)
        {
            if (Leaf(item, "equipped") is not { } leaf || !bool.TryParse(leaf.Value, out bool worn))
                return CharacterCreationArmorEncumbranceBranch1432.Unsupported;
            if (worn) equipped.Add(item);
        }
        if (equipped.Count == 0) return CharacterCreationArmorEncumbranceBranch1432.NoEquippedArmor;
        return equipped.All(item => Leaf(item, "encumbrance") is { } leaf && bool.TryParse(leaf.Value, out bool value) && !value)
            ? CharacterCreationArmorEncumbranceBranch1432.AllEquippedArmorExempt
            : CharacterCreationArmorEncumbranceBranch1432.Unsupported;
    }

    private static bool NoAddLimbImprovements(XElement graph)
        => TryCollection(graph, "improvements", "improvement", out var rows)
            && rows.All(row => Leaf(row, "improvementttype") is { } type && !string.IsNullOrWhiteSpace(type.Value)
                && !string.Equals(type.Value, "AddLimb", StringComparison.OrdinalIgnoreCase));

    private static bool TryCollection(XElement graph, string container, string entry, out XElement[] items,
        CharacterSheetOwnerGraph? sourceOwners5925 = null)
    {
        items = [];
        var containers = graph.Descendants().Where(node => SameName(node, container)).ToArray();
        if (containers.Length > 1 || containers.Any(node => node.Parent != graph || node.Name != container
            || node.HasAttributes || !PlainContainer(node))) return false;
        items = containers.SingleOrDefault()?.Elements().ToArray() ?? [];
        return items.All(node => node.Name == entry && !node.HasAttributes && PlainContainer(node))
            && graph.Descendants().Where(node => SameName(node, entry)
                && sourceOwners5925?.IsCurrentCritterArmorBonus5925(graph, node) != true).SequenceEqual(items);
    }

    private static bool NoObjects(XElement graph, string container, string entry,
        CharacterSheetOwnerGraph? sourceOwners5925 = null)
    {
        var roots = graph.Descendants().Where(node => SameName(node, container)).ToArray();
        return graph.Elements().Count(node => SameName(node, container)) <= 1
            && roots.All(node => node.Name == container && !node.HasAttributes && !node.HasElements && PlainContainer(node))
            && !graph.Descendants().Any(node => SameName(node, entry)
                && sourceOwners5925?.IsCurrentCritterArmorBonus5925(graph, node) != true
                // Weapon serialization contains a scalar cyberware=False flag.
                && !(entry == "cyberware" && node.Parent?.Name == "weapon" && node.Name == "cyberware"
                    && !node.HasAttributes && node.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText)
                    && bool.TryParse(node.Value, out bool installed) && !installed));
    }

    private static bool PlainContainer(XElement node) => node.Nodes().All(child => child is XElement
        || child.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(((XText)child).Value));

    private static bool SameName(XElement node, string name)
        => string.Equals(node.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    private static XElement? Leaf(XElement parent, string field)
        => parent.Elements().Where(node => SameName(node, field)).ToArray() is [var leaf]
            && leaf.Name == field && !leaf.HasAttributes && leaf.Nodes().ToArray() is [XText text]
            && text.GetType() == typeof(XText) ? leaf : null;
}

internal sealed partial class CharacterCreationSelectedEffectsConsumer
{
    private CharacterCreationNativeLoadoutProjection1432? nativeLoadout1432;
    internal CharacterCreationNativeLoadoutProjection1432? NativeLoadout1432(CharacterCreationCompleteAssessment authority)
        => DigestFor(authority) is not null && nativeLoadout1432 is { } result
            && result.GraphDigest == CharacterCreationFinalizationDigest.Compute(
                compiledGraph.ToString(SaveOptions.DisableFormatting)) ? result : null;
}
