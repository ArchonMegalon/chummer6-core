using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;

namespace Chummer.Application.Characters;

internal static class CharacterCreationNativeCompatibility1430
{
    // Compatibility identity of the four protected native policies and their
    // executed applicability. Originally derived from the 1430 whole-file map;
    // retained after the 2612 review because those semantics are unchanged.
    // This is NOT the current whole-file fingerprint. Keeping it stable preserves
    // persisted projection/receipt digests; private current-source execution is
    // still mandatory. A policy/applicability change needs a new identity and an
    // explicit historical replay decision, never an old-version wildcard.
    internal const string SourceVersion = "60e9c7c5c3ce57c4b45cf03c4995b0f206195f721d810be36b81ae913c09c324";

    // SHA256 of the sorted compact UTF-8 JSON path-to-SHA256 map for all 13 pins.
    // Keys are core-relative forward-slash paths; lowercase hashes; no final newline.
    // Evidence identity, not execution authority; SourceVersion remains independent.
    internal const string ReviewedSourceFingerprint = "d680b8be53d01f0614fe7c98133c05b20929545e4cbaf130b83a9f226bf69097";

    internal static CharacterCreationNativeCompatibilityPolicy1430? ReadPolicy(XElement profile)
    {
        if (profile.Name != "setting" || profile.HasAttributes) return null;
        var values = new Dictionary<CharacterCreationNativeCompatibilitySetting1430, bool?>();
        foreach (var definition in CharacterCreationNativeCompatibilityDefinitions1430.All)
        {
            var occurrences = profile.Elements().Where(node => node.Name.LocalName == definition.Element).ToArray();
            if (occurrences.Length == 0) { values.Add(definition.Key, null); continue; }
            if (PlainLeaf(profile, definition.Element) is not { } leaf
                || !bool.TryParse(leaf.Value, out bool value)) return null;
            values.Add(definition.Key, value);
        }
        if (values.Values.All(value => value is null)) return null;
        return new(values[CharacterCreationNativeCompatibilitySetting1430.NoSingleArmorEncumbrance],
            values[CharacterCreationNativeCompatibilitySetting1430.ExtendAnyDetectionSpell],
            values[CharacterCreationNativeCompatibilitySetting1430.ProfileAllowSkillDiceRolling],
            values[CharacterCreationNativeCompatibilitySetting1430.AllowTechnomancerSchooling]);
    }

    internal static bool? Value(CharacterCreationNativeCompatibilityPolicy1430 policy, string field) => field switch
    {
        "nosinglearmorencumbrance" => policy.NoSingleArmorEncumbrance,
        "extendanydetectionspell" => policy.ExtendAnyDetectionSpell,
        "allowskilldicerolling" => policy.ProfileAllowSkillDiceRolling,
        "allowtechnomancerschooling" => policy.AllowTechnomancerSchooling,
        _ => null
    };

    internal static CharacterCreationNativeCompatibilityProjection1430? Evaluate(XElement graph, string? schema,
        AwakenedGraphInputs inputs, CharacterCreationPrerequisiteDraft draft)
        => schema == CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            ? Evaluate(graph, CharacterCreationAstralInitiative1249.Source(inputs), draft) : null;

    internal static CharacterCreationNativeCompatibilityProjection1430? Evaluate(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft)
    {
        if (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            || source!.SettingsProfileId != draft.SettingsProfileId
            || draft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || graph.Name != "character" || graph.HasAttributes
            || PlainLeaf(graph, "gameedition")?.Value != "SR5"
            || !Boolean(graph, "created", out bool created)
            || !Boolean(graph, "resenabled", out bool resonance)) return null;
        var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        if (ReadPolicy(profile) is not { } policy) return null;
        var spells = new List<CharacterCreationSpellExtensionControl1430>();
        if (policy.ExtendAnyDetectionSpell is bool allowExtended)
        {
            if (!Container(graph, "spells", out var collection)) return null;
            foreach (var spell in collection?.Elements() ?? [])
            {
                if (spell.Name != "spell" || spell.HasAttributes
                    || PlainLeaf(spell, "sourceid") is not { } id || !Guid.TryParse(id.Value, out _)
                    || PlainLeaf(spell, "name") is not { } name
                    || PlainLeaf(spell, "category") is not { } category
                    || PlainLeaf(spell, "descriptors", allowEmpty: true) is not { } descriptors
                    || !Boolean(spell, "extended", out bool extended)
                    || !Boolean(spell, "customextended", out bool custom)) return null;
                // ApplyOptions emits both false. Finish currently has no custom
                // extension command, so refuse that demand instead of granting a
                // spell-drain or extended-range execution that never occurred.
                if (extended || custom) return null;
                bool inherent = descriptors.Value.Split(',').Any(value =>
                    string.Equals(value.Trim(), "Extended Area", StringComparison.OrdinalIgnoreCase));
                // SelectSpell.BuildSpellList hides named Extended variants when
                // custom extension is enabled. This Finish lane cannot replace
                // such a selection with a custom variant; keep that demand open.
                bool listed = !allowExtended || !name.Value.Contains(", Extended", StringComparison.Ordinal);
                if (!listed) return null;
                spells.Add(new(id.Value, CharacterCreationFinalizationDigest.Compute(
                    spell.ToString(SaveOptions.DisableFormatting)), category.Value, inherent,
                    listed, !inherent && category.Value == "Detection" && allowExtended, extended, custom));
            }
        }
        if (policy.AllowTechnomancerSchooling is not null
            && (!Container(graph, "initiationgrades", out var grades) || grades?.HasElements == true
                || !Uninitiated(graph, "initiategrade") || !Uninitiated(graph, "submersiongrade"))) return null;
        // CharacterCreate and CharacterCareer both enter this control branch
        // on RESEnabled. Do not substitute Created, talent name or false policy.
        bool? schoolingControl = resonance ? policy.AllowTechnomancerSchooling : null;
        return new(SourceVersion, policy, Array.AsReadOnly(spells.ToArray()), resonance, schoolingControl, created,
            source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
            CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting)));
    }

    private static bool Uninitiated(XElement graph, string field)
    {
        // Character's private grade counters initialize to zero; Load/LoadAsync
        // only replace them by explicit named reads. Missing is not a policy default.
        if (!graph.Elements().Any(node => node.Name.LocalName == field)) return true;
        return PlainLeaf(graph, field) is { } leaf
            && int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade) && grade == 0;
    }

    private static bool Container(XElement graph, string name, out XElement? container)
    {
        container = null;
        var matches = graph.Elements().Where(node => node.Name.LocalName == name).ToArray();
        // Native Load enumerates named children; an absent collection is empty.
        if (matches.Length == 0) return true;
        if (matches is not [var found] || found.Name != name || found.HasAttributes
            || found.Nodes().Any(node => node is not XElement
                && (node.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(((XText)node).Value)))) return false;
        container = found;
        return true;
    }

    private static bool Boolean(XElement parent, string field, out bool value)
    {
        value = false;
        return PlainLeaf(parent, field) is { } leaf && bool.TryParse(leaf.Value, out value);
    }

    private static XElement? PlainLeaf(XElement parent, string name, bool allowEmpty = false)
    {
        var matches = parent.Elements().Where(node => node.Name.LocalName == name).ToArray();
        return matches is [var leaf] && leaf.Name == name && !leaf.HasAttributes
            && (leaf.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText)
                || allowEmpty && !leaf.Nodes().Any()) ? leaf : null;
    }
}
