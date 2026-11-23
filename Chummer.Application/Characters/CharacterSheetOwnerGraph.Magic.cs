using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    internal bool ImportedPriority5699 { get; private set; }
    internal IReadOnlyList<CharacterCreationEssenceRules1222.Attribute> SpecialInputs5699 { get; private set; } = [];
    internal IReadOnlyList<CharacterCreationEssenceRules1222.Loss> SpecialResults5699 { get; private set; } = [];

    private void ReadNativeTradition3751()
    {
        var saved = Node(root, "tradition");
        if (saved is null) return;
        const string path = "tradition";
        bool stream = Text(saved, "traditiontype") == "RES";
        Need((stream
                ? Flag(root, "resenabled", false) && Flag(root, "technomancer", false)
                    && !Flag(root, "magenabled", false) && !Flag(root, "adept", false) && !Flag(root, "magician", false)
                : Text(saved, "traditiontype") == "MAG" && Flag(root, "magenabled", false)
                    && Flag(root, "magician", false) && !Flag(root, "resenabled", false) && !Flag(root, "technomancer", false))
            && !Flag(root, "depenabled", false), "tradition-kind-unconsumed", path);
        string kind = stream ? CharacterCreationMagicResonanceKinds.Stream : CharacterCreationMagicResonanceKinds.Tradition;
        string diagnostic = stream ? "stream" : "tradition";
        Guid id = Id(saved, "guid"), sourceId = Id(saved, "sourceid");
        Need(instances.Add(id), "duplicate-owner-instance", path);
        string method = Text(root, "buildmethod", ""), settings = Text(root, "settings");
        string captured;
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> options;
        if (method is CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules)
        {
            // Native methods have their own catalog, never a synthetic Priority
            // authority. Resolve exactly the selected method/profile and reuse the
            // same authenticated source options as retained-current consumption.
            Need(CharacterNativeMagicCatalogAuthority5102.TryResolve(context, method, settings, out var nativeCatalog),
                diagnostic + "-authority-unavailable", path);
            captured = nativeCatalog!.AuthorityDigest;
            options = nativeCatalog.Catalogs.Single(slice => slice.Kind == kind).Options;
            checks.Add(() => CharacterNativeMagicCatalogAuthority5102.TryResolve(context, method, settings, out var final)
                && final!.AuthorityDigest == captured);
        }
        else
        {
            Need(context.TryResolveCreationMagicResonanceAuthority(out var authority)
                && authority.IsAuthoritative && authority.Blockers.Count == 0
                && authority.Schema == CharacterCreationMagicResonanceSchemas.AuthorityV1
                && authority.SettingsProfileId == settings, diagnostic + "-authority-unavailable", path);
            Need(new[] { authority.AuthorityDigest, authority.PrerequisiteAuthorityDigest,
                    authority.SourceInputsDigest, authority.CustomDataInputsDigest, authority.GmPolicyDigest,
                    authority.RuntimeDigest }.All(CharacterCreationMagicResonanceDigest.IsCanonical)
                && authority.AuthorityDigest == CharacterCreationMagicResonanceDigest.Compute(
                    authority with { AuthorityDigest = string.Empty }), diagnostic + "-authority-digest", path);
            captured = authority.AuthorityDigest;
            options = stream ? authority.Streams : authority.Traditions;
            checks.Add(() => context.TryResolveCreationMagicResonanceAuthority(out var final)
                && final.IsAuthoritative && final.Blockers.Count == 0 && final.AuthorityDigest == captured
                && CharacterCreationMagicResonanceDigest.Compute(final with { AuthorityDigest = string.Empty }) == captured);
        }
        // The saved kind selects exactly one source catalog. A matching name or
        // identity from the other catalog cannot authorize this native owner.
        var matches = options.Where(o => Guid.TryParse(o.Identity.SourceId, out var candidate) && candidate == sourceId).ToArray();
        Need(matches is [_] && matches[0].Identity.Kind == kind, diagnostic + "-source-identity", path);
        Need(CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(matches[0], 1, out var projection)
            && projection is not null, diagnostic + "-source-mechanic-unconsumed", path);
        // This strict typed overload refuses disabled options, bonus payloads,
        // prerequisites and unknown mechanics. Reuse the actual native writer's
        // pure projection for every saved scalar, spirit and empty bonus field.
        var native = CharacterCreationAwakenedLegacyProjector.ProjectTradition(projection!,
            captured, out var source);
        // The saved instance is an identity, not evidence of its creation seed.
        // Bind its canonical nonempty unique GUID, never invent a replacement owner.
        native.SetElementValue("guid", id.ToString("D"));
        var normalized = Parse(saved.ToString(SaveOptions.DisableFormatting));
        Need(!saved.HasAttributes && saved.Nodes().All(n => n is XElement
                || n is XText text && string.IsNullOrWhiteSpace(text.Value))
            && saved.Elements().Count() == native.Elements().Count()
            && native.Elements().All(field => NativeTraditionFieldEquals3751(field,
                Node(saved, field.Name.LocalName), Node(normalized, field.Name.LocalName))),
            diagnostic + "-saved-source-mismatch", path);
        sourceEvidence.Add(captured);
        // Keep this owner in exhaustive traversal and the authority digest. It has
        // no admitted effects; normal saved/source effect equality still applies.
        owners.Add(new Owner(stream ? "Stream" : "Tradition", id, sourceId, saved, source, 1, "", true, path));
    }

    private static bool NativeTraditionFieldEquals3751(XElement expected, XElement? saved, XElement? normalized)
    {
        // Only a node-free field from the typed writer can ignore the lexical
        // difference between <field /> and <field></field>. Inspect the original
        // saved field: reparsing the comparison copy can discard whitespace.
        if (expected.Value.Length != 0 || expected.Nodes().Any())
            return XNode.DeepEquals(expected, normalized);
        return saved is not null && saved.Name == expected.Name
            && saved.Value.Length == 0 && !saved.Nodes().Any()
            && expected.Attributes().Select(a => (a.Name, a.Value))
                .SequenceEqual(saved.Attributes().Select(a => (a.Name, a.Value)));
    }

    private void CompileSpecialAttributes(XElement metatype, XElement settings,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants)
    {
        ImportedPriority5699 = priorityGrants?.IsImported5693 == true;
        if (Sprite5751 is not null) { CompileSpriteResonance5751(settings); return; }
        var savedEffects = Node(root, "improvements")?.Elements() ?? [];
        Need(!savedEffects.Any(e => Text(e, "improvementsource", "") == "EssenceLoss"),
            "career-essence-loss-history-unconsumed", "improvements");
        bool resonant = Flag(root, "resenabled", false);
        if (!Flag(root, "magenabled", false) && !resonant) return;
        if (resonant)
            Need(!expected.Any(e => Text(e, "improvementttype") == "Attribute"
                && Text(e, "improvedname").StartsWith("RES", StringComparison.Ordinal)
                && Text(e, "unique", "") != "enableattribute"
                && !(RetainedGrades5719 is { Resonance: true } && Text(e, "improvementsource", "") == "Submersion")),
                "resonance-modifier-unconsumed", "attributes/RES");
        Need(Int(root, "initiategrade", 0) == (RetainedGrades5719 is { Resonance: false } initiation ? initiation.Grade : 0),
            "initiation-attribute-maximum-unconsumed", "initiategrade");
        Need(Int(root, "submersiongrade", 0) == (RetainedGrades5719 is { Resonance: true } submersion ? submersion.Grade : 0),
            "submersion-attribute-maximum-unconsumed", "submersiongrade");
        string prefix = resonant ? "res" : "mag";
        int metatypeMinimum = Int(metatype, prefix + "min"), metatypeMaximum = Int(metatype, prefix + "max"),
            metatypeAugmented = Int(metatype, prefix + "aug");
        int? priorityMinimum = ResolvePrioritySpecialAttributeMinimum3777(resonant, priorityGrants);
        bool split = Flag(settings, "mysadeptsecondmagattribute", false) && Flag(root, "adept", false) && Flag(root, "magician", false);
        var inputs = new List<CharacterCreationEssenceRules1222.Attribute>();
        foreach (string name in resonant ? new[] { "RES" } : split ? new[] { "MAG", "MAGAdept" } : new[] { "MAG" })
        {
            // Match CharacterCreationAttributesService.EvaluateAllocations / the native priority writer:
            // the talent replaces MAG/RES's minimum, and both caps are max(racial cap,
            // grant). Separate MAGAdept retains the metatype range. Saved totals and
            // saved range fields are never inputs to this source derivation.
            bool granted = priorityMinimum.HasValue && name != "MAGAdept";
            int minimum = granted ? priorityMinimum!.Value : metatypeMinimum;
            int maximum = granted ? Math.Max(metatypeMaximum, minimum) : metatypeMaximum;
            int augmented = granted ? maximum : metatypeAugmented;
            var rows = (Node(root, "attributes") ?? throw Error("attributes-missing", "attributes")).Elements("attribute").Where(a => Text(a, "name") == name).ToArray();
            Need(rows is [_], "magic-attribute-identity", "attributes/" + name);
            var row = rows[0];
            Need(Int(row, "metatypemin") == minimum && Int(row, "metatypemax") == maximum
                && Int(row, "metatypeaugmax") == augmented,
                "magic-attribute-source-range", "attributes/" + name);
            int allocated = Int(row, "base"), karma = Int(row, "karma");
            // Native clamps a calculated value. That must not conceal an invalid
            // saved allocation; use a wide sum so forged Int32 values fail this check.
            Need(allocated >= 0 && karma >= 0 && (long)minimum + allocated + karma <=
                (ImportedPriority5699 ? Attribute(name, minimum, maximum, augmented, allocated, karma,
                    Flag(settings, "unclampattributeminimum", false)).Maximum : maximum),
                "magic-attribute-allocation-range", "attributes/" + name);
            var input = new CharacterCreationEssenceRules1222.Attribute(name, true, minimum, maximum, augmented, allocated, karma,
                Flag(settings, "unclampattributeminimum", false),
                FreeBase: Math.Min(Rounded("Attributelevel", name), maximum - minimum));
            if (ImportedPriority5699)
                input = input with
                {
                    MinimumModifier = Rounded("Attribute", name, "min") + Rounded("Attribute", name + "Base", "min"),
                    MaximumModifier = Rounded("Attribute", name, "max") + Rounded("Attribute", name + "Base", "max"),
                    ValueModifier = Rounded("Attribute", name + "Base", "aug"), AugmentedModifier = Channel("Attribute", name, "aug"),
                    AugmentedMaximumModifier = Rounded("Attribute", name, "augmax")
                };
            inputs.Add(input);
        }
        decimal maxEssence = Dec(metatype, "essmax");
        decimal acquisition = Dec(root, "essenceatspecialstart", maxEssence);
        // When legacy's sentinel survives import, source-proven enabling qualities in
        // this supported graph carry no Essence penalties, so their acquisition is max.
        if (acquisition == decimal.MinValue) acquisition = maxEssence;
        Need(acquisition is >= 0 && acquisition <= maxEssence, "magic-acquisition-essence", "essenceatspecialstart");
        var result = CharacterCreationEssenceRules1222.Calculate(maxEssence, maxEssence - Essence(metatype, settings),
            0, 0, 0, 0, acquisition, 1, 1, Int(settings, "essencedecimals", 2),
            Flag(settings, "donotroundessenceinternally", false), Flag(settings, "esslossreducesmaximumonly", false),
            Flag(settings, "specialkarmacostbasedonshownvalue", false), false,
            Flag(root, "adept", false) && Flag(root, "magician", false) && !split, inputs);
        Need(result.Valid, "magic-essence-invalid", "essence");
        Need(result.PowerPointLoss == 0, "mystic-power-point-loss-history-unconsumed", "powers");
        var values = new List<CharacterSheetAttributeValue>();
        foreach (var loss in result.Attributes)
        {
            var input = inputs.Single(a => a.Id == loss.Id);
            values.Add(new(loss.Id, loss.Current) { Unaugmented = loss.Value, AllocatedBase = input.Base,
                AllocatedKarma = input.Karma, AugmentedModifier = loss.Current - loss.Value });
            if (loss.MinimumReduction == 0 && loss.MaximumReduction == 0 && loss.AugmentedReduction == 0) continue;
            var owner = new Owner("EssenceLossChargen", Guid.Empty, Guid.Empty, root, metatype, 1, "", true, "essence");
            var effect = Improvement(owner, "", true, "Attribute", loss.Id, 0, -loss.AugmentedReduction,
                -loss.MinimumReduction, -loss.MaximumReduction, 0, false, "", 1, "", "");
            effect.SetElementValue("sourcename", "");
            expected.Add(effect);
        }
        SpecialAttributes = values;
        SpecialInputs5699 = inputs.AsReadOnly();
        SpecialResults5699 = result.Attributes;
        // Power.TotalMaximumLevels uses effective MAG; no saved totalvalue participates.
        int magic = resonant ? 0 : values.Single(a => a.Name == (split ? "MAGAdept" : "MAG")).Total;
        foreach (var power in owners.Where(o => o.Kind == "Power"))
            Need(power.Rating <= magic, "power-rating-above-effective-magic", power.Path);
    }

    private int? ResolvePrioritySpecialAttributeMinimum3777(bool resonant,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants)
    {
        // Retain the existing metatype-only route without private selection proof.
        // A public priority label/catalog match must not elevate a saved range.
        if (priorityGrants is null) return null;
        if (priorityGrants.IsImported5693)
        {
            Need(priorityGrants.TryResolveImportedMinimum5693(root, context, resonant, out int minimum, out var importedDigest),
                "imported-priority-grant-source-or-selection-mismatch", "prioritytalent");
            sourceEvidence.Add(importedDigest);
            checks.Add(() => priorityGrants.TryResolveImportedMinimum5693(root, context, resonant,
                out _, out var final) && final == importedDigest);
            return minimum;
        }
        string talent = Text(root, "prioritytalent", "");
        Need(talent.Length != 0, "magic-priority-selection-missing", "prioritytalent");
        string build = Text(root, "buildmethod", "");
        Need(build is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen,
            "magic-priority-authority-unavailable", "buildmethod");
        // The private proof reexecutes the confirmed selection, including all ranks,
        // sources and settings. CompilePrioritySkillGrants3563 still reconciles its
        // exact talent owners/effect rows and installs the final proof fence.
        Need(priorityGrants.TryResolve(root, context, out _, out _, out _),
            "priority-grant-source-or-selection-mismatch", "prioritytalent");
        Need(context.TryResolveCreationPrerequisiteAuthority(out var authority)
            && authority.IsAuthoritative && authority.Blockers.Count == 0
            && authority.Schema == CharacterCreationPrerequisiteSchemas.AuthorityV1
            && authority.SettingsProfileId == Text(root, "settings") && authority.BuildMethod == build
            && authority.AuthorityDigest == CharacterCreationPrerequisiteAuthorityDigest.Compute(authority),
            "magic-priority-authority-unavailable", "prioritytalent");
        string rank = Text(root, "priorityspecial", "");
        var options = authority.Options.Where(o => o.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{o.Rank},{o.SumToTenValue}") == rank)
            .SelectMany(o => o.TalentOptions).Where(o => o.Value == talent).ToArray();
        Need(options is [_] && options[0].IsEnabled && options[0].Blockers.Count == 0
            && options[0].Depth is null && (resonant
                ? options[0].Resonance > 0 && options[0].Magic is null
                : options[0].Magic > 0 && options[0].Resonance is null),
            "magic-priority-talent-mismatch", "prioritytalent");
        string digest = authority.AuthorityDigest;
        sourceEvidence.Add(digest);
        checks.Add(() => context.TryResolveCreationPrerequisiteAuthority(out var current)
            && CharacterCreationPrerequisiteAuthorityDigest.Compute(current) == digest);
        return resonant ? options[0].Resonance : options[0].Magic;
    }
}
