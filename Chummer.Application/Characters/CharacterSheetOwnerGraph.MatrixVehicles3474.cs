using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    internal List<CharacterSheetVehicleValue> Vehicles3474 { get; } = [];
    internal List<CharacterSheetComplexFormValue> ComplexForms3474 { get; } = [];
    private readonly HashSet<Guid> grantedGear3474 = [];
    private CharacterVehicleWorkshopCatalog? vehicleCatalog3474;
    private string vehicleCatalogDigest3474 = "";

    private void ValidateComplexForm3474(Owner owner)
    {
        Need(owner.Saved.Parent?.Parent == root && owner.Saved.Parent!.Name == "complexforms",
            "complex-form-owner-path", owner.Path);
        foreach (string field in new[] { "gear", "gears", "children" })
            Need(Node(owner.Saved, field)?.HasElements != true, "complex-form-child-unconsumed", owner.Path + "/" + field);
        Need(Flag(root, "resenabled", false) && !Flag(root, "magenabled", false),
            "complex-form-without-resonance", owner.Path);
        Need(owners.Count(o => o.Kind == "ComplexForm" && o.SourceId == owner.SourceId) == 1,
            "duplicate-complex-form-source", owner.Path);
        Need(CharacterCareerComplexFormLearningTransaction.TryValidateCurrentSource(owner.SourceId, owner.Source, out _),
            "complex-form-source-mechanic-unconsumed", owner.Path);
        Need(context is ICharacterCareerComplexFormLearningSourceContext, "complex-form-source-capability", owner.Path);
        var reader = (ICharacterCareerComplexFormLearningSourceContext)context;
        Need(reader.TryResolveCareerComplexFormLearningSource(owner.SourceId, out var receipt) && receipt is not null,
            "complex-form-current-source-unavailable", owner.Path);
        var bound = receipt!;
        Need(XNode.DeepEquals(Parse(bound.ComplexFormXml), owner.Source), "complex-form-source-mismatch", owner.Path);
        checks.Add(() => reader.TryResolveCareerComplexFormLearningSource(owner.SourceId, out var final) && final == bound);
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(bound));
        foreach (string field in new[] { "useskill", "target", "duration", "fv" })
            Need(Text(owner.Saved, field, "") == Text(owner.Source, field, ""), "complex-form-saved-source-mismatch", owner.Path + "/" + field);
        Need(Int(owner.Saved, "grade", 0) == 0 && owner.Rating == 0 && owner.Extra.Length == 0,
            "complex-form-submersion-or-choice-unconsumed", owner.Path);
        ComplexForms3474.Add(new(owner.Id, owner.SourceId, Text(owner.Source, "name"), Text(owner.Source, "target"),
            Text(owner.Source, "duration"), Text(owner.Source, "fv"), CharacterCreationFinalizationDigest.Compute(bound)));
    }

    private void ValidateVehicle3474(Owner owner)
    {
        Need(owner.Saved.Parent?.Parent == root && owner.Saved.Parent!.Name == "vehicles",
            "vehicle-owner-path", owner.Path);
        if (vehicleCatalog3474 is null)
        {
            Need(context.TryResolveVehicleWorkshopCatalog(out var catalog) && catalog.Binding.Exact
                && string.Equals(catalog.Binding.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
                && catalog.Binding.ProfileId == Text(root, "settings"), "vehicle-current-catalog-unavailable", owner.Path);
            vehicleCatalog3474 = catalog;
            vehicleCatalogDigest3474 = CharacterCreationFinalizationDigest.Compute(catalog);
            string captured = vehicleCatalogDigest3474;
            sourceEvidence.Add(captured);
            checks.Add(() => context.TryResolveVehicleWorkshopCatalog(out var final)
                && CharacterCreationFinalizationDigest.Compute(final) == captured);
        }
        var matches = vehicleCatalog3474.Chassis.Where(c => c.SourceId.Value == owner.SourceId).ToArray();
        Need(matches is [_], "vehicle-source-identity", owner.Path);
        var chassis = matches[0];
        Need(chassis.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
            && chassis.Posture == CharacterVehicleChassisPosture.Stock && chassis.FactoryModifications.Count == 0,
            "vehicle-modified-or-custom-chassis-unconsumed", owner.Path);
        Need(Text(owner.Source, "name") == chassis.Name && Text(owner.Source, "source") == chassis.SourceBook
            && Text(owner.Saved, "category") == chassis.Category && Text(owner.Source, "category") == chassis.Category,
            "vehicle-source-identity", owner.Path);
        foreach (string field in new[] { "mods", "weaponmounts", "weapons", "children" })
            Need(Node(owner.Saved, field)?.HasElements != true, "vehicle-equipment-unconsumed", owner.Path + "/" + field);
        Need(!Flag(owner.Saved, "active", false), "vehicle-jumped-persona-unconsumed", owner.Path);
        Need(owner.Rating == 0 && owner.Extra.Length == 0 && Text(owner.Saved, "parentid", "").Length == 0,
            "vehicle-owner-state-unconsumed", owner.Path);
        foreach (string field in new[] { "bonus", "wirelessbonus", "pairbonus", "wirelesspairbonus" })
        {
            Need(Empty3474(owner.Source, field) && Empty3474(owner.Saved, field), "vehicle-effect-unconsumed", owner.Path + "/" + field);
        }
        Pair("handling", "offroadhandling", chassis.Handling, chassis.OffRoadHandling);
        Pair("accel", "offroadaccel", chassis.Acceleration, chassis.OffRoadAcceleration);
        Pair("speed", "offroadspeed", chassis.Speed, chassis.OffRoadSpeed);
        foreach (var (field, value) in new[] { ("pilot", chassis.Pilot), ("body", chassis.Body), ("seats", chassis.Seats),
                     ("armor", chassis.Armor), ("sensor", chassis.Sensor) })
            Need(Int(owner.Saved, field) == value, "vehicle-saved-source-mismatch", owner.Path + "/" + field);
        Need(Int(owner.Saved, "physicalcmfilled", 0) == 0 && Int(owner.Saved, "matrixcmfilled", 0) == 0,
            "vehicle-damage-modifier-unconsumed", owner.Path);
        // These are no longer saved display values: the ordinary chassis source
        // and workshop projection supply them. Any Matrix customization stays out.
        Need(Text(owner.Saved, "devicerating", chassis.Pilot.ToString(CultureInfo.InvariantCulture))
                == chassis.Pilot.ToString(CultureInfo.InvariantCulture)
            && Text(owner.Saved, "overclocked", "None") is "" or "None", "vehicle-matrix-source-mismatch", owner.Path);
        foreach (string field in new[] { "attack", "sleaze", "dataprocessing", "firewall", "attributearray", "programlimit",
                     "modattack", "modsleaze", "moddataprocessing", "modfirewall", "moddevicerating", "modprogramlimit", "modattributearray" })
            Need(Text(owner.Saved, field, "").Length == 0, "vehicle-matrix-modifier-unconsumed", owner.Path + "/" + field);
        Need(CharacterVehicleConditionMonitorCalculator.TryCalculatePhysicalMaximum(chassis.Category, chassis.Body, [], out int maximum),
            "vehicle-condition-unconsumed", owner.Path);
        ValidateVehicleFactory3474(owner, chassis);
        Vehicles3474.Add(new(owner.Id, owner.SourceId, chassis.Name, chassis.Kind, chassis.Handling, chassis.OffRoadHandling,
            chassis.Body, chassis.Armor, maximum, vehicleCatalogDigest3474));

        void Pair(string field, string offRoad, int on, int off)
        {
            // Legacy imported saves may use slash notation; native workshop saves
            // persist both scalar fields. Reconcile both, including any duplicate.
            string value = Text(owner.Saved, field);
            bool slash = value.Contains('/');
            Need(slash ? value == on.ToString(CultureInfo.InvariantCulture) + "/" + off.ToString(CultureInfo.InvariantCulture)
                : Int(owner.Saved, field) == on, "vehicle-saved-source-mismatch", owner.Path + "/" + field);
            Need(Node(owner.Saved, offRoad) is not null ? Int(owner.Saved, offRoad) == off : slash || on == off,
                "vehicle-off-road-source-mismatch", owner.Path + "/" + offRoad);
        }
    }

    private void ValidateVehicleFactory3474(Owner owner, CharacterVehicleWorkshopChassisEntry chassis)
    {
        var children = Node(owner.Saved, "gears")?.Elements().ToArray() ?? [];
        Need(children.Length == chassis.FactoryGears.Count, "vehicle-factory-gear-closure", owner.Path);
        foreach (var factory in chassis.FactoryGears)
        {
            Need(factory.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact,
                "vehicle-factory-gear-unconsumed", owner.Path);
            var rows = children.Where(g => g.Name == "gear" && Id(g, "sourceid") == factory.SourceId.Value).ToArray();
            Need(rows is [_], "vehicle-factory-gear-identity", owner.Path);
            var saved = rows[0];
            Need(Text(saved, "name") == factory.Name && Text(saved, "category") == factory.Category
                && Text(saved, "source") == factory.SourceBook && Int(saved, "rating") == factory.Rating
                && Dec(saved, "qty", 1) == factory.Quantity && Text(saved, "capacity") == factory.Capacity
                && Text(saved, "armorcapacity") == factory.ArmorCapacity
                && Text(saved, "maxrating", "") == factory.MaximumRating
                && Text(saved, "parentid", "") is var parent && (parent.Length == 0 || parent == owner.Id.ToString("D")),
                "vehicle-factory-gear-source-mismatch", owner.Path);
            Need(Node(saved, "children")?.HasElements != true && !Flag(saved, "active", false),
                "vehicle-factory-gear-children-unconsumed", owner.Path);
            foreach (string field in new[] { "bonus", "wirelessbonus", "weaponbonus", "flechetteweaponbonus" })
                Need(Empty3474(saved, field), "vehicle-factory-gear-effect-unconsumed", owner.Path);
        }
    }

    private static bool Empty3474(XElement node, string field) => Node(node, field) is not { } value
        || !value.HasAttributes && !value.HasElements && string.IsNullOrWhiteSpace(value.Value);

    private void ValidateGearChildren3474(Owner owner)
    {
        var source = Node(owner.Source, "gears");
        if (source is null) return;
        Need(source.Attributes().All(a => a.Name == "startcollapsed" && bool.TryParse(a.Value, out _)),
            "gear-generated-container-unconsumed", owner.Path);
        var consumed = new HashSet<Guid>();
        foreach (var instruction in source.Elements())
        {
            Need(instruction.Name == "usegear" && !instruction.HasAttributes
                && instruction.Elements().All(e => e.Name.LocalName is "name" or "category" or "rating" or "capacity" or "maxrating"
                    && !e.HasElements && !e.HasAttributes), "gear-generated-instruction-unconsumed", owner.Path);
            string name = Text(instruction, "name");
            Need(sources!.TryResolveSheetNamedSource("Gear", name, out var receipt) && receipt is not null,
                "gear-generated-source-unavailable", owner.Path);
            var bound = receipt!;
            checks.Add(() => sources!.TryResolveSheetNamedSource("Gear", name, out var final) && final == bound);
            sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(bound));
            var children = Node(owner.Saved, "children")?.Elements().Where(c => c.Name == "gear" && Id(c, "sourceid") == bound.SourceId).ToArray() ?? [];
            Need(children is [_], "gear-generated-child-missing-or-duplicate", owner.Path);
            var child = children[0];
            Need(consumed.Add(Id(child, "guid")) && Text(child, "category") == Text(instruction, "category")
                && Int(child, "rating") == Int(instruction, "rating", 0), "gear-generated-child-source-mismatch", owner.Path);
            foreach (string field in new[] { "capacity", "maxrating" })
                if (Node(instruction, field) is not null)
                    Need(Text(child, field) == Text(instruction, field), "gear-generated-child-source-mismatch", owner.Path + "/" + field);
        }
    }

    private void BindMatrixOwners3474()
    {
        var active = owners.Where(o => o.Kind is "Gear" or "Vehicle" or "Armor" or "Weapon" or "Cyberware"
            && Flag(o.Saved, "active", false)).ToArray();
        Need(active.Length <= 1, "active-matrix-owner-conflict", "owners");
        if (active is [var persona])
            Need(persona.Kind == "Gear" && persona.Saved.Parent?.Parent == root && persona.Enabled
                && Text(persona.Source, "canformpersona", "").Contains("Self", StringComparison.Ordinal),
                "active-matrix-owner-path-unconsumed", persona.Path);
        foreach (var owner in owners.Where(o => o.Kind == "Gear" && o.Saved.Parent?.Name == "children"))
        {
            Need((Empty3474(owner.Source, "required") || SimModulePrerequisite5082(owner)) && Empty3474(owner.Source, "forbidden"),
                "gear-child-prerequisite-unconsumed", owner.Path);
            string parent = Text(owner.Saved, "parentid", "");
            Need(owner.Saved.Parent?.Parent is { } savedParent && owners.Any(o => ReferenceEquals(o.Saved, savedParent)
                && o.Kind == "Gear" && (parent.Length == 0 || parent == o.Id.ToString("D"))), "gear-parent-owner-mismatch", owner.Path);
        }
    }

    private Guid BindGrantedGear3474(Owner quality, XElement instruction)
    {
        string name = Text(instruction, "name"), category = Text(instruction, "category");
        Need(sources!.TryResolveSheetNamedSource("Gear", name, out var receipt) && receipt is not null,
            "granted-gear-source-unavailable", quality.Path);
        var bound = receipt!;
        checks.Add(() => sources!.TryResolveSheetNamedSource("Gear", name, out var final) && final == bound);
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(bound));
        var matches = owners.Where(o => o.Kind == "Gear" && o.SourceId == bound.SourceId
            && Text(o.Saved, "parentid", "") == quality.Id.ToString("D") && o.Saved.Parent?.Parent == root).ToArray();
        Need(matches is [_], "granted-gear-owner-missing-or-ambiguous", quality.Path);
        var gear = matches[0];
        Need(grantedGear3474.Add(gear.Id) && Text(gear.Source, "category") == category
            && Text(gear.Saved, "category") == category && gear.Rating == 0 && Dec(gear.Saved, "qty") == 1
            && XNode.DeepEquals(gear.Source, Parse(bound.SourceXml)), "granted-gear-owner-source-mismatch", quality.Path);
        return gear.Id;
    }

    private void ValidateGrantedGear3474()
    {
        foreach (var gear in owners.Where(o => o.Kind == "Gear" && o.Saved.Parent?.Parent == root))
        {
            if (Text(gear.Saved, "parentid", "").Length == 0) continue;
            Need(grantedGear3474.Contains(gear.Id), "unbound-granted-gear-owner", gear.Path);
        }
    }

    internal IReadOnlyList<CharacterSheetMatrixDeviceValue> MatrixDevices3474(IReadOnlyDictionary<string, int> attributes)
    {
        var result = new List<CharacterSheetMatrixDeviceValue>();
        var personas = owners.Where(o => o.Kind == "Gear" && o.Saved.Parent?.Parent == root
            && Text(o.Source, "canformpersona", "").Contains("Self", StringComparison.Ordinal)).ToArray();
        if (personas.Length > 0)
            Need(!owners.Any(o => o.Kind == "Gear" && o.Enabled && Text(o.Source, "category", "").Contains("Programs", StringComparison.Ordinal)),
                "matrix-running-program-effects-unconsumed", "gears");
        foreach (var owner in personas)
        {
            foreach (var saved in owner.Saved.DescendantsAndSelf("gear"))
            {
                var bound = owners.Single(o => ReferenceEquals(o.Saved, saved));
                ValidateMatrixGear3474(bound);
            }
            // The complete sheet graph has already admitted every owner. The
            // existing Matrix consumer receives this exact owned device subtree,
            // never a character with unsupported inventory silently removed.
            Need(Mechanics695Execution.TryMatrix711(new XElement("character", new XElement("gears", new XElement(owner.Saved))),
                attributes, false, out var devices), "matrix-device-mechanic-unconsumed", owner.Path);
            var device = devices.Single(d => d.Path.Count == 1);
            int Value(string name) => device.Attributes.Single(a => a.Attribute == name).Total;
            result.Add(new(owner.Id, owner.SourceId, Text(owner.Source, "name"), Flag(owner.Saved, "active", false),
                Value("Attack"), Value("Sleaze"), Value("Data Processing"), Value("Firewall"), Value("Device Rating"),
                Value("Program Limit"), AuthorityDigest));
        }
        return result;
    }

    private void ValidateMatrixGear3474(Owner owner)
    {
        Need(Node(owner.Saved, "gears")?.HasElements != true && Node(owner.Saved, "gear")?.HasElements != true,
            "matrix-gear-child-layout-unconsumed", owner.Path);
        Need((Empty3474(owner.Source, "required") || SimModulePrerequisite5082(owner)) && Empty3474(owner.Source, "forbidden"),
            "matrix-gear-prerequisite-unconsumed", owner.Path);
        Need(Text(owner.Saved, "category") == Text(owner.Source, "category"), "matrix-gear-source-mismatch", owner.Path);
        Need(!owner.Enabled || !Text(owner.Source, "category").Contains("Programs", StringComparison.Ordinal),
            "matrix-running-program-effects-unconsumed", owner.Path);
        foreach (string field in new[] { "bonus", "wirelessbonus", "weaponbonus", "flechetteweaponbonus" })
            Need(Empty3474(owner.Source, field) && Empty3474(owner.Saved, field), "matrix-gear-effects-unconsumed", owner.Path);
        foreach (string field in new[] { "devicerating", "canformpersona", "modattack", "modsleaze", "moddataprocessing", "modfirewall", "moddevicerating", "modprogramlimit" })
            Need(Text(owner.Saved, field, "") == Text(owner.Source, field, ""), "matrix-gear-source-mismatch", owner.Path + "/" + field);
        Need(Text(owner.Saved, "programlimit", "") == Text(owner.Source, "programs", Text(owner.Source, "programlimit", ""))
            && Text(owner.Saved, "attributearray", "") == Text(owner.Source, "attributearray", "")
            && Text(owner.Saved, "overclocked", "None") is "" or "None" && Text(owner.Saved, "modattributearray", "").Length == 0,
            "matrix-gear-configuration-unconsumed", owner.Path);
        string[] fields = ["attack", "sleaze", "dataprocessing", "firewall"];
        if (Text(owner.Source, "attributearray", "") is { Length: > 0 } array)
            Need(fields.Select(f => Text(owner.Saved, f)).Order(StringComparer.Ordinal)
                .SequenceEqual(array.Split(',').Order(StringComparer.Ordinal)), "matrix-gear-array-source-mismatch", owner.Path);
        else foreach (string field in fields)
            Need(Text(owner.Saved, field, "") == Text(owner.Source, field, ""), "matrix-gear-source-mismatch", owner.Path + "/" + field);
    }
}
