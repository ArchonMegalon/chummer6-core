using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    // Stable catalog identities, not translated names or saved talent/display flags.
    private static readonly Guid Magician5082 = new("0e741331-d776-4be8-abc5-4101228abdef");
    private static readonly Guid Explorer5082 = new("2f20c108-d724-4684-a642-d40ffe7c51e1");
    private static readonly Guid AstralForm5082 = new("0ea84be4-a293-44bf-bc60-52314495cc9a");
    private static readonly Guid LivingPersona5082 = new("73b55822-dfb8-48f5-8ff8-37ef498ab9ef");
    private static readonly Guid Trodes5082 = new("418d5ba1-dd19-4179-add8-074be445a7b2");
    private static readonly Guid Datajack5082 = new("47c48542-48c3-417e-91f0-b5a456183f05");
    private static readonly Guid DatajackPlus5082 = new("8496afd6-ad34-4e73-b3f4-191ef11e121c");
    private static readonly Guid ColdModule5082 = new("d589142e-a71f-4cd9-b916-967168721eea");
    private static readonly Guid HotModule5082 = new("b7da0596-da6e-4122-adc3-21d7f3f9e3f1");

    internal IReadOnlyList<CharacterInitiativeModeOption> InitiativeModes5082(XElement settings,
        IReadOnlyDictionary<string, int> attributes, CharacterPhysicalInitiativeProjection physical,
        IReadOnlyList<CharacterSheetMatrixDeviceValue> devices)
    {
        var result = new List<CharacterInitiativeModeOption>
        {
            new(CharacterInitiativeMode.Physical, physical.Value, physical.Dice, null, [])
        };
        // The full owner graph and enabled/source effects have already been recompiled.
        // Aspected/mystic adepts and mere astral perception are not projection authority.
        var projection = owners.FirstOrDefault(o => o.Enabled && !suppressedOwners.Contains(o.Id)
            && (o.Kind == "Quality" && (o.SourceId == Magician5082 || o.SourceId == Explorer5082)
                || o.Kind == "CritterPower" && o.SourceId == AstralForm5082));
        bool astral = projection is not null && (projection.SourceId == AstralForm5082
            || attributes.TryGetValue("MAG", out int magic) && magic > 0);
        bool astralSettings = CharacterCreationAstralInitiative1249.TryBounds(settings, out int low, out int high);
        Add(CharacterInitiativeMode.Astral, checked(attributes["INT"] * 2 + physical.WoundModifier),
            astralSettings ? Math.Min(low, high) : null,
            new("astral-projection-or-form", astral, "Source-owned astral projection or astral form is required.", projection?.Id),
            new("astral-settings", astralSettings, "The selected profile must resolve astral initiative dice."));

        var device = devices.SingleOrDefault(d => d.Active);
        var persona = device is null ? null : owners.Single(o => o.Id == device.OwnerId);
        bool active = persona is { Enabled: true } && Dec(persona.Saved, "qty", 1) >= 1;
        bool living = active && persona!.SourceId == LivingPersona5082
            && Flag(root, "technomancer", false) && attributes.TryGetValue("RES", out int resonance) && resonance > 0
            && grantedGear3474.Contains(persona.Id);
        bool Module(Guid id) => active && owners.Any(o => o.Kind == "Gear" && o.SourceId == id && o.Enabled && Dec(o.Saved, "qty", 1) >= 1
            && o.Saved.Ancestors().Any(a => ReferenceEquals(a, persona!.Saved)));
        var neuralInterface = owners.FirstOrDefault(o => o.Enabled && !suppressedOwners.Contains(o.Id)
            && (o.Kind == "Gear" && o.SourceId == Trodes5082 && Dec(o.Saved, "qty", 1) >= 1
                || o.Kind == "Cyberware" && (o.SourceId == Datajack5082 || o.SourceId == DatajackPlus5082)));
        var neuralRequirement = new CharacterInitiativePrerequisite("direct-neural-interface", living || neuralInterface is not null,
            "VR requires equipped source-owned trodes, a datajack, or a living persona.", neuralInterface?.Id);
        bool cold = !living && (Module(ColdModule5082) || Module(HotModule5082));
        bool hot = living || Module(HotModule5082);
        var activeRequirement = new CharacterInitiativePrerequisite("active-persona", active,
            "An equipped, active, source-bound persona is required.", device?.OwnerId);
        Add(CharacterInitiativeMode.MatrixAr, physical.Value, physical.Dice, activeRequirement);
        // Career resolves the effective profile as <settings>; the creation
        // policy reader consumes the catalog's <setting> wrapper. Retain all
        // attributes and children so malformed or amended policy stays visible.
        var matrixSettings = new XElement(settings);
        if (matrixSettings.Name == "settings") matrixSettings.Name = "setting";
        bool policy = CharacterCreationMatrixInitiative.TryPolicy(matrixSettings, out var matrixPolicy);
        // Character.MatrixInitiativeColdValue/HotValue and their Dice getters; use
        // the same source-compiled native unique/precedence/lifecycle channel selector.
        int matrixValue = checked(attributes["INT"] + (device?.DataProcessing ?? 0)
            + physical.WoundModifier + MatrixChannel("MatrixInitiative"));
        int matrixDice = MatrixChannel("MatrixInitiativeDice");
        int MatrixChannel(string type)
        {
            // ImprovementManager.ValueOf excludes Gear improvements for an active
            // living persona; use its source identity rather than a saved label.
            var effects = Effects.Where(e => Text(e, "improvementttype") == type
                && (!living || Text(e, "improvementsource") != "Gear"));
            return CharacterCreationEssenceRules1222.Round(CharacterCareerReputationProjector.ResolvePhysicalChannel886(
                new XElement("character", new XElement("improvements", effects.Select(e => new XElement(e)))), type, Flag(root, "created")));
        }
        var policyRequirement = new CharacterInitiativePrerequisite("matrix-settings", policy,
            "The selected profile must resolve Matrix initiative dice.");
        Add(CharacterInitiativeMode.MatrixColdSim, matrixValue,
            policy ? Math.Min(checked(matrixPolicy!.MinimumColdDice + matrixDice), matrixPolicy.MaximumColdDice) : null,
            activeRequirement, neuralRequirement, new("cold-sim-interface", cold,
                living ? "A living persona uses hot-sim VR; cold-sim is unavailable."
                    : "An equipped sim module attached to the active persona is required."), policyRequirement);
        Add(CharacterInitiativeMode.MatrixHotSim, matrixValue,
            policy ? Math.Min(checked(matrixPolicy!.MinimumHotDice + matrixDice), matrixPolicy.MaximumHotDice) : null,
            activeRequirement, neuralRequirement, new("hot-sim-interface", hot,
                "An equipped hot-sim module or a source-owned living persona is required."), policyRequirement);
        return result;

        void Add(CharacterInitiativeMode mode, int value, int? dice, params CharacterInitiativePrerequisite[] prerequisites)
        {
            string? unavailable = prerequisites.FirstOrDefault(p => !p.Satisfied)?.Code
                ?? (dice is not (>= 0 and <= 64) ? "initiative-dice-outside-supported-range" : null);
            result.Add(new(mode, unavailable is null ? value : null, unavailable is null ? dice : null, unavailable, prerequisites));
        }
    }

    // Native factory cyberdeck children include sim modules despite their purchase
    // prerequisite. Admit only the exact source predicate and a bound persona parent;
    // do not discard required/forbidden rules for arbitrary child gear.
    private bool SimModulePrerequisite5082(Owner owner)
    {
        if (owner.Kind != "Gear" || owner.SourceId != ColdModule5082 && owner.SourceId != HotModule5082
            || !Empty3474(owner.Source, "forbidden") || owner.Saved.Parent?.Name != "children") return false;
        var required = Node(owner.Source, "required");
        var expected = XElement.Parse("<required><geardetails><OR><category>Commlinks</category><category>Custom</category></OR></geardetails></required>");
        if (required is null || !XNode.DeepEquals(XElement.Parse(required.ToString(SaveOptions.DisableFormatting)), expected)) return false;
        var parent = owners.SingleOrDefault(o => ReferenceEquals(o.Saved, owner.Saved.Parent.Parent));
        if (parent is null || parent.Kind != "Gear" || !Text(parent.Source, "canformpersona", "").Contains("Self", StringComparison.Ordinal)) return false;
        if (Text(parent.Source, "category", "") is "Commlinks" or "Custom") return true;
        // The existing factory-child validator binds exact source/name/category/count.
        return Node(parent.Source, "gears")?.Elements("usegear").Any(g => Text(g, "name", "") == Text(owner.Source, "name")
            && Text(g, "category", "") == Text(owner.Source, "category")) == true;
    }
}
