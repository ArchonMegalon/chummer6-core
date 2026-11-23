using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Contracts.Characters.CharacterCreationRoleFindingState;
using static Chummer.Contracts.Characters.CharacterCreationRoleRoutineKind;

namespace Chummer.Application.Characters;

/// <summary>SR5 ordinary action rules at the existing finalized-creation rules boundary.
/// Internal scalar inputs come from the independently reviewed candidate, never a caller sheet.
/// No opposed roll, success probability, purchase, mutation or inferred equipment.</summary>
internal static class CharacterCreationMatrixRoutineRules3470
{
    internal const string Semantics = "chummer.core.sr5-matrix-vehicle-ordinary/v3";
    internal static IReadOnlyList<CharacterCreationRoleFinding> Evaluate(
        CharacterCreationRoleRoutineObjective routine, XElement graph,
        IReadOnlyDictionary<string, int> attributes, IReadOnlyList<CharacterCreationSkillProjection> skills,
        ICharacterSourceDataContext context, string projectionDigest,
        CharacterCreationMagicResonanceCatalogOption? form = null,
        CharacterCreationMagicResonanceCatalogOption? stream = null,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants = null)
    {
        var result = new List<CharacterCreationRoleFinding>();
        var checks = new List<Func<bool>>();
        var evidence = new List<CharacterCreationRoleContribution>();
        CharacterSheetOwnerGraph? nativeOwners = null;
        CharacterSheetRuleModifier? matrixPerception = null;
        evidence.Add(new("Rule semantics " + Semantics, 0, CharacterCreationFinalizationDigest.ComputeUtf8(Semantics)));
        try
        {
            Need(graph.Name == "character" && Text(graph, "gameedition", "SR5") == "SR5",
                "edition", "This action boundary supports SR5 only.");
            var conditions = routine.Conditions;
            Need(!conditions.UseEdge && !conditions.ExternalAssistance && !conditions.SpriteAssistance
                && !conditions.Consumables && conditions.SustainedEffects == 0
                && conditions.Preparation.Length == 0 && conditions.Situation == "ordinary",
                "conditions", "Edge, assistance, sustaining, preparation and complex conditions require separate mechanics.");
            Need(routine.Action is not null, "scenario", "Select a concrete action and access mode.", failure: true);
            Need(context is ICharacterSheetOwnerSourceContext, "source", "The selected resolver has no owner-source boundary.");
            var sources = (ICharacterSheetOwnerSourceContext)context;
            Need(context.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState),
                "settings", "The current settings cannot be resolved.");
            checks.Add(() => context.TryResolveCareerSettingsProfile(out var current, out var state)
                && current == settingsXml && state == settingsState);
            OrdinaryGraph();
            foreach (string required in routine.RequiredEquipmentSourceIds)
            {
                var matches = graph.Descendants().Where(n => n.Name.LocalName is "gear" or "cyberware" or "vehicle"
                    && SameId(Text(n, "sourceid"), required)).ToArray();
                Need(matches.Length > 0, "equipment", "A required equipment source has no actual current owner.", true);
                Need(matches.Length == 1, "equipment-ambiguous", "A required source has multiple current instances.");
                if (matches[0].Name == "gear")
                {
                    Need(True(matches[0], "equipped"), "equipment-ready", "A required gear instance is not equipped.", true);
                    Gear(matches[0], Source("Gear", Guid.Parse(required)), 0);
                }
                else if (matches[0].Name == "cyberware") Bind(matches[0], Source("Cyberware", Guid.Parse(required)));
                else Need(routine.Kind is RiggerManual or RiggerRemote or RiggerJumpedIn
                    && SameId(required, routine.Action!.VehicleSourceId ?? ""), "equipment-vehicle",
                    "A required vehicle must be the selected chassis evaluated by this routine; other vehicle dependencies remain unsupported.");
            }
            var action = routine.Action!;
            switch (routine.Kind)
            {
                case DeckerMatrix: Matrix(); break;
                case TechnomancerForm: Form(); break;
                case RiggerManual: case RiggerRemote: case RiggerJumpedIn: Vehicle(); break;
                default: throw new RoutineUnavailable("kind", "This routine is outside the Matrix/vehicle boundary.", false);
            }
            Need(checks.All(check => check()), "source-drift", "A selected source or settings changed during evaluation.");
            return result.ToArray();

            void OrdinaryGraph()
            {
                Need(!True(graph, "ignorerules") && !True(graph, "ai") && !True(graph, "depenabled"),
                    "settings", "Ignore-rules, AI and Depth personas are outside this SR5 slice.");
                Need(graph.Descendants().Where(n => n.Element("guid") is not null)
                    .Select(n => Text(n, "guid")).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == graph.Descendants().Count(n => n.Element("guid") is not null),
                    "owner-identity", "Duplicate owner instance identities cannot bind a routine.");
                // Review values are natural attributes/ranks. Never silently lose a live
                // modifier, wound, specialization, sustained effect or program bonus.
                var effectContainers = graph.Elements("improvements").ToArray();
                Need(effectContainers.Length <= 1, "action-effects", "The improvement container is ambiguous.");
                if (True(graph, "resenabled") || effectContainers.Any(n => n.HasElements))
                    NativeEffects();
                foreach (string field in new[] { "physicalcmfilled", "stuncmfilled", "physicalfilled", "stunfilled" })
                    Need(Number(graph, field, 0) == 0, "wounds", "Wounded action pools require the current wound projection.");
                foreach (string container in new[] { "sustainedspells", "sustainedobjects", "drugs" })
                    Need(graph.Element(container)?.HasElements != true, "action-effects", "Active effects require a contextual action projection.");
                // A finalized review owns these numbers; raw saved totals never supply them.
                foreach (var (name, value) in attributes)
                {
                    var rows = graph.Element("attributes")?.Elements("attribute")
                        .Where(n => Text(n, "name") == name).ToArray() ?? [];
                    Need(rows.Length == 1, "attributes", "A reviewed attribute has no unique current instance.");
                    var row = rows[0];
                    int natural = Math.Min(checked(Number(row, "base") + Number(row, "karma")
                        + Number(row, "metatypemin")), Number(row, "metatypemax"));
                    Need(value == natural && (row.Element("totalvalue") is null || Number(row, "totalvalue") == value),
                        "attributes", "The current attribute differs from the unmodified finalized review.");
                }
            }

            void NativeEffects()
            {
                Need(CharacterSheetDerivedQuery.TryMetatype(graph, context, out var metatypeXml,
                    out var metatypeState, out var metatypeId), "action-owner-source",
                    "The complete native owner graph needs its current metatype source.");
                checks.Add(() => CharacterSheetDerivedQuery.TryMetatype(graph, context, out var xml,
                    out var state, out var id) && xml == metatypeXml && state == metatypeState && id == metatypeId);
                // Resolve the original graph, including every owner, factory child and
                // disabled effect. The compiler compares saved rows to source-compiled
                // rows before any grant or conditional modifier can be consumed here.
                nativeOwners = CharacterSheetOwnerGraph.Resolve(graph, context, checks,
                    Xml(metatypeXml), Xml(settingsXml), metatypeId, out _, priorityGrants);
                evidence.Add(new("Complete native owner/effect graph", 0, nativeOwners.AuthorityDigest));
                foreach (var special in nativeOwners.SpecialAttributes)
                    Need(attributes.TryGetValue(special.Name, out int value) && value == special.Total,
                        "attributes", "The finalized special attribute differs from the current native owner projection.");

                Guid talentOwner = Guid.Empty;
                if (True(graph, "resenabled"))
                {
                    Need(sources.TryResolveSheetNamedSource("Quality", "Technomancer", out var talent) && talent is not null,
                        "talent-source", "The current Technomancer grant source cannot be resolved.");
                    var bound = Source("Quality", talent!.SourceId);
                    Need(bound == talent, "talent-source", "The named and exact Technomancer source disagree.");
                    checks.Add(() => sources.TryResolveSheetNamedSource("Quality", "Technomancer", out var current) && current == bound);
                    var owners = graph.Element("qualities")?.Elements("quality")
                        .Where(q => SameId(Text(q, "sourceid"), bound.SourceId.ToString("D"))).ToArray() ?? [];
                    Need(owners.Length == 1, "talent-owner", "The living persona needs its unique native Technomancer quality owner.");
                    talentOwner = Guid.Parse(Text(owners[0], "guid"));
                }

                foreach (var modifier in nativeOwners.RuleModifiers() ?? [])
                {
                    Need(talentOwner != Guid.Empty && modifier.OwnerId == talentOwner
                        && modifier.Kind == CharacterSheetRuleModifierKind.ConditionalSkill
                        && modifier.Target == "Computer" && modifier.Condition == "Matrix Perception"
                        && modifier.Exclude.Length == 0 && !modifier.AppliesToRating
                        && modifier.ActionUnavailableReason is null && matrixPerception is null,
                        "action-effects", "This routine cannot consume the current source-bound contextual modifier.");
                    matrixPerception = modifier;
                }
                foreach (var effect in nativeOwners.Effects)
                {
                    string type = Text(effect, "improvementttype"), name = Text(effect, "improvedname");
                    bool talentEffect = talentOwner != Guid.Empty && Text(effect, "improvementsource") == "Quality"
                        && SameId(Text(effect, "sourcename"), talentOwner.ToString("D"));
                    // Reviewed ranks already include these free base allocations. Recognize
                    // only the compiler's source-proven rows; never add their values again.
                    bool supported = nativeOwners.IsPrioritySkillGrant3563(effect) || type == "Attribute" && Text(effect, "unique") == "enableattribute"
                        && Text(effect, "condition").Length == 0
                        || talentEffect && (type == "Gear"
                            || type == "SpecialTab" && name == "Technomancer"
                            || type == "SpecialSkills" && name == "Technomancer"
                            || type == "Skill" && name == "Computer" && Text(effect, "condition") == "Matrix Perception"
                                && Text(effect, "exclude").Length == 0 && !CharacterSheetOwnerGraph.Flag(effect, "addtorating"));
                    Need(supported, "action-effects", "This unmodified routine cannot consume the current source-bound effect graph.");
                }
            }

            int Attribute(string name)
            {
                Need(attributes.TryGetValue(name, out int value) && value > 0,
                    "attribute:" + name, "The action requires an enabled positive " + name + " attribute.", failure: true);
                evidence.Add(new("Reviewed natural " + name, value, projectionDigest));
                return value;
            }

            int Skill(string name)
            {
                var rows = skills.Where(s => SameId(s.SourceSkillId, routine.SkillSourceId)).ToArray();
                Need(rows.Length == 1 && rows[0].Name == name && rows[0].IsEnabled
                    && rows[0].Blockers.Count == 0 && rows[0].EffectiveRating is > 0,
                    "skill", "The selected action needs a reviewed trained " + name + " skill, not an unrelated skill or free ranks.", true);
                var source = Source("Skill", Guid.Parse(routine.SkillSourceId));
                Need(source.Name == name, "skill-source", "The selected skill source does not match the action.");
                evidence.Add(new("Reviewed " + name + " rating", rows[0].EffectiveRating!.Value, projectionDigest));
                return rows[0].EffectiveRating!.Value;
            }

            CharacterSheetOwnerSource Source(string kind, Guid id)
            {
                Need(sources.TryResolveSheetOwnerSource(kind, id, out var receipt) && receipt is not null,
                    "source:" + kind, "The selected " + kind + " source is missing, disabled or changed.");
                var bound = receipt!;
                Need(bound.Kind == kind && bound.SourceId == id && new[] { bound.CatalogDigest,
                    bound.CustomDataDigest, bound.ProfileDigest, bound.PolicyDigest }.All(CharacterCreationFinalizationDigest.IsCanonical),
                    "source-binding", "The owner source lacks exact current catalog/profile bindings.");
                var xml = Xml(bound.SourceXml);
                Need(SameId(Text(xml, "id"), id.ToString("D")) && Text(xml, "name") == bound.Name
                    && Text(xml, "source") == bound.SourceBook, "source-identity", "The source receipt and canonical row disagree.");
                checks.Add(() => sources.TryResolveSheetOwnerSource(kind, id, out var current) && current == bound);
                return bound;
            }

            void Bind(XElement saved, CharacterSheetOwnerSource source)
            {
                Need(Guid.TryParse(Text(saved, "guid"), out var instance) && instance != Guid.Empty
                    && SameId(Text(saved, "sourceid"), source.SourceId.ToString("D"))
                    && Text(saved, "name") == source.Name && Text(saved, "source") == source.SourceBook,
                    "owner-source", "The selected instance no longer matches the current source identity.");
                evidence.Add(new(source.Kind + " instance " + instance.ToString("D") + "; source " + source.SourceId.ToString("D"),
                    0, CharacterCreationFinalizationDigest.Compute(source)));
            }

            XElement RequiredOwner(string kind, string sourceId)
            {
                Need(Guid.TryParse(sourceId, out _), "equipment", "An explicit valid equipment source identity is required.", true);
                var rows = graph.Descendants(kind).Where(n => SameId(Text(n, "sourceid"), sourceId)).ToArray();
                Need(rows.Length > 0, "equipment", "The requested source has no current owned " + kind + " instance.", true);
                Need(rows.Length == 1, "equipment-ambiguous", "The source identifies multiple owners; this contract cannot choose an instance.");
                Need(Guid.TryParse(Text(rows[0], "guid"), out var instance) && instance != Guid.Empty,
                    "owner-identity", "The selected owner has no valid current instance identity.");
                return rows[0];
            }

            IReadOnlyDictionary<string, int> Device(bool requireDeck)
            {
                var active = ActiveMatrixOwners();
                Need(active.Length == 1 && active[0].Name == "gear", "active-persona",
                    "Select exactly one current active owned gear persona; active vehicle, armor or weapon owners are different access paths.", true);
                var saved = active[0];
                Need(saved.Parent?.Parent == graph && saved.Parent.Name.LocalName is "gear" or "gears",
                    "persona-owner-path", "Nested persona devices require the corresponding parent projection.");
                string sourceId = Text(saved, "sourceid");
                Need(routine.RequiredEquipmentSourceIds.Any(id => SameId(id, sourceId)),
                    "selected-controller", "The active controller/deck must be explicitly required by this routine.", true);
                Need(ReferenceEquals(RequiredOwner("gear", sourceId), saved), "selected-controller", "The active owner is ambiguous.");
                var source = Source("Gear", Guid.Parse(sourceId));
                var canonical = Xml(source.SourceXml);
                Need(Text(canonical, "canformpersona").Contains("Self", StringComparison.Ordinal),
                    "persona", "The selected source cannot form a persona.", true);
                Need(!requireDeck || Text(canonical, "category") == "Cyberdecks", "deck", "A selected usable cyberdeck is required.", true);
                Need(True(saved, "equipped") && True(saved, "wirelesson"),
                    "device-ready", "The selected active device must be equipped and wireless for this declared AR route.", true);
                Gear(saved, source, 0);
                // Project this exact reconciled owner subtree through the existing Matrix
                // calculator. This is an owner projection, not a filtered character sheet.
                Need(Mechanics695Execution.TryMatrix711(new XElement("character", new XElement("gears", new XElement(saved))),
                    attributes, false, out var devices), "matrix-attributes", "The current device graph uses unsupported Matrix expressions or damage.");
                var device = devices.Single(d => d.Path.Count == 1);
                var values = device.Attributes.ToDictionary(a => a.Attribute, a => a.Total, StringComparer.Ordinal);
                int programs = saved.Descendants("gear").Count(n => True(n, "equipped")
                    && Text(n, "category").Contains("Programs", StringComparison.Ordinal));
                Need(programs <= values["Program Limit"], "program-capacity", "Running programs exceed the current device's program capacity.", true);
                Need(programs == 0, "running-program-effects", "The selected catalog records program identities without their action modifiers. Running programs require their own source-backed action consumers; they cannot silently be ignored.");
                result.Add(Pass("program-loadout", "The current source-bound device has no running optional programs and fits its program capacity. These ordinary actions require no optional program.", programs));
                return values;
            }

            void Gear(XElement saved, CharacterSheetOwnerSource source, int depth)
            {
                Need(depth < 16, "gear-depth", "The selected equipment nesting is unsupported.");
                Bind(saved, source);
                var canonical = Xml(source.SourceXml);
                Need(Text(saved, "category") == Text(canonical, "category"), "gear-source", "The saved gear category differs from its source.");
                foreach (string field in new[] { "bonus", "wirelessbonus", "weaponbonus", "flechetteweaponbonus" })
                    Need(canonical.Element(field)?.HasElements != true && saved.Element(field)?.HasElements != true,
                        "gear-effects", "The selected program/equipment effect needs its action-specific consumer.");
                foreach (string field in new[] { "devicerating", "canformpersona", "modattack", "modsleaze", "moddataprocessing", "modfirewall", "moddevicerating", "modprogramlimit" })
                    Need(Text(saved, field) == Text(canonical, field), "gear-source", "A saved Matrix field differs from its current amended source: " + field + ".");
                Need(Text(saved, "programlimit") == Text(canonical, "programs")
                    && Text(saved, "attributearray") == Text(canonical, "attributearray"),
                    "gear-source", "Saved program capacity or attribute array differs from the source.");
                string[] fields = ["attack", "sleaze", "dataprocessing", "firewall"];
                if (Text(canonical, "attributearray") is { Length: > 0 } array)
                    Need(fields.Select(f => Text(saved, f)).OrderBy(x => x, StringComparer.Ordinal)
                        .SequenceEqual(array.Split(',').OrderBy(x => x, StringComparer.Ordinal)),
                        "deck-array", "The current deck configuration is not a permutation of the selected source array.");
                else foreach (string field in fields)
                    Need(Text(saved, field) == Text(canonical, field), "gear-source", "Saved " + field + " differs from the source.");
                Need(Text(saved, "overclocked") is "" or "None" && Text(saved, "modattributearray").Length == 0,
                    "device-settings", "Overclocking or modified arrays require their selected-effect authority.");
                var children = saved.Element("children")?.Elements("gear").ToArray() ?? [];
                Need(!saved.Elements().Any(n => n.Name.LocalName is "gears" or "gear" && n.HasElements),
                    "gear-children", "This device uses an unsupported nested gear layout.");
                foreach (var child in children) Gear(child, Source("Gear", Guid.Parse(Text(child, "sourceid"))), depth + 1);
                foreach (var factory in canonical.Element("gears")?.Elements("usegear") ?? [])
                    Need(children.Any(c => Text(c, "name") == Text(factory, "name")
                        && Text(c, "category") == Text(factory, "category")),
                        "factory-equipment", "A required source factory attachment is missing from the selected device.", true);
            }

            void Matrix()
            {
                bool living = routine.Role == CharacterCreationRole.Technomancer;
                Need(action.AccessMode == (living ? "living-persona;wireless;noise=0;target=visible;same-grid" : "ar;wireless;noise=0;target=visible;same-grid"),
                    "matrix-access", "Declare ordinary AR, wireless, zero noise, visible target and same-grid access; other modes/penalties remain unsupported.");
                var device = living ? LivingPersona() : Device(requireDeck: true);
                string attribute, skill, limit;
                switch (action.Target)
                {
                    case "hack-on-the-fly:one-mark": attribute = "LOG"; skill = "Hacking"; limit = "Sleaze"; break;
                    case "brute-force:one-mark": attribute = "LOG"; skill = "Cybercombat"; limit = "Attack"; break;
                    case "matrix-perception": attribute = "INT"; skill = "Computer"; limit = "Data Processing"; break;
                    case "defend:willpower-firewall": Defense("WIL", device["Firewall"]); return;
                    case "defend:intuition-firewall": Defense("INT", device["Firewall"]); return;
                    default: throw new RoutineUnavailable("matrix-action", "Select a supported one-mark hack, Matrix perception or Firewall defense action.", false);
                }
                Need(device[limit] > 0, "matrix-capability", "The selected device has no usable " + limit + " rating.", true);
                int personaBonus = living && action.Target == "matrix-perception" && matrixPerception is not null
                    ? CharacterCreationEssenceRules1222.Round(matrixPerception.Value) : 0;
                if (personaBonus != 0) evidence.Add(new("Source-bound Matrix Perception; quality owner " + matrixPerception!.OwnerId,
                    personaBonus, nativeOwners!.AuthorityDigest));
                Pool(checked(Attribute(attribute) + Skill(skill) + personaBonus), device[limit],
                    attribute + " + " + skill + " [" + limit + "] under the declared access scenario (SR5 238–241). No opposition or success probability is calculated.");
            }

            void Defense(string attribute, int firewall)
            {
                int pool = checked(Attribute(attribute) + firewall);
                result.Add(Minimum("matrix-defense-pool", pool, routine.Targets.MinimumSkillDicePool,
                    attribute + " + current persona Firewall (SR5 238); no optional Full Matrix Defense or opposed probability."));
            }

            void Form()
            {
                Need(action.AccessMode == "living-persona;wireless;noise=0;target=visible-device"
                    && action.Target == "resonance-spike", "form-action", "This slice threads Resonance Spike at a visible device using the living persona; other form actions remain unsupported.");
                _ = LivingPersona();
                int resonance = Attribute("RES");
                Need(form is not null && form.IsEnabled && form.Identity == routine.MagicIdentity,
                    "form-selection", "Select the required form from the current enabled, finalized source graph.", true);
                Need(stream is not null && stream.IsEnabled, "stream", "Select a current source-bound stream.", true);
                Need(context.TryResolveCreationMagicResonanceAuthority(out var currentMagic),
                    "form-source", "The current form/stream catalog cannot be re-resolved.");
                Need(currentMagic.ComplexForms.Count(o => o.Identity == form.Identity && o.IsEnabled
                        && o.CanonicalSourceXmlDigest == form.CanonicalSourceXmlDigest && o.SourceNodeDigest == form.SourceNodeDigest) == 1
                    && currentMagic.Streams.Count(o => o.Identity == stream.Identity && o.IsEnabled
                        && o.CanonicalSourceXmlDigest == stream.CanonicalSourceXmlDigest && o.SourceNodeDigest == stream.SourceNodeDigest) == 1,
                    "form-source", "The finalized form/stream identities differ from the current amended source catalog.");
                string boundMagic = CharacterCreationFinalizationDigest.Compute(currentMagic);
                checks.Add(() => context.TryResolveCreationMagicResonanceAuthority(out var current)
                    && CharacterCreationFinalizationDigest.Compute(current) == boundMagic);
                var canonical = Xml(form!.CanonicalSourceXml);
                Need(form.CanonicalSourceXmlDigest == CharacterCreationMagicResonanceDigest.ComputeUtf8(form.CanonicalSourceXml)
                    && SameId(Text(canonical, "id"), form.Identity.SourceId)
                    && Text(canonical, "name") == "Resonance Spike" && Text(canonical, "source") == "SR5"
                    && Text(canonical, "target") == "Device" && Text(canonical, "duration") == "I",
                    "form-source", "The chosen form source does not match the bounded instant-device action semantics.");
                var saved = RequiredOwner("complexform", form.Identity.SourceId);
                Need(Text(saved, "name") == form.Name && Text(saved, "source") == form.SourceBook
                    && Text(saved, "fv") == Text(canonical, "fv"), "form-source", "Saved form identity or fading differs from its selected amended source.");
                Need(action.ForceOrLevel is > 0 && action.ForceOrLevel <= checked(3 * resonance),
                    "threading-level", "Choose a positive threading level no greater than three times current Resonance (SR5 251).", true);
                int level = action.ForceOrLevel!.Value;
                Need(TryFadingValue(Text(canonical, "fv"), level, out int fading), "fading-expression", "The chosen source fading expression is outside the existing bounded arithmetic grammar.");
                Need(routine.Targets.MinimumResistancePool is not null, "fading-resistance-target", "Declare the required fading resistance target.", true);
                Need(!SameId(stream.Identity.SourceId, CharacterTraditionNameRules.CustomMagicalTraditionSourceId.ToString("D")),
                    "stream-selector", "A custom selected resistance attribute needs the current selector authority, not the catalog default.");
                Need(stream!.CanonicalSourceXmlDigest == CharacterCreationMagicResonanceDigest.ComputeUtf8(stream.CanonicalSourceXml)
                    && CharacterCreationNativeMagic1430.TryEvaluateCurrentDrain(graph, Xml(stream.CanonicalSourceXml), true, out _),
                    "fading-resistance", "The current stream resistance expression/effects are unsupported by the existing drain kernel.");
                CharacterCreationNativeMagic1430.TryEvaluateCurrentDrain(graph, Xml(stream.CanonicalSourceXml), true, out int resistance);
                evidence.Add(new("Selected form instance " + Text(saved, "guid") + "; source " + form.Identity.SourceId, 0, form.CanonicalSourceXmlDigest));
                evidence.Add(new("Selected stream " + stream.Identity.SourceId, resistance, stream.CanonicalSourceXmlDigest));
                Pool(checked(Skill("Software") + resonance), level,
                    "Software + RES [Level], opposed by target WIL + Firewall; Resonance Spike deals Matrix damage per net hit (SR5 252–253). No opposed roll is simulated.");
                result.Add(Pass("fading-value", "Source FV with native minimum 2; this is incoming Fading, not expected damage after resistance.", fading));
                result.Add(Minimum("routine-fading-resistance", resistance, routine.Targets.MinimumResistancePool!.Value,
                    "Current stream-specific expression evaluated by the existing native drain kernel."));
                result.Add(Pass("fading-damage-type", level <= resonance
                    ? "The ordinary Level limit cannot allow more hits than RES; Fading is Stun."
                    : "Fading is Physical only if actual threading hits exceed RES; otherwise Stun. A level above RES alone does not make Fading Physical.", null));
            }

            IReadOnlyDictionary<string, int> LivingPersona()
            {
                Need(True(graph, "resenabled") && !True(graph, "magenabled"), "living-persona", "An enabled Technomancer Resonance persona is required.", true);
                _ = Attribute("RES");
                Need(sources.TryResolveSheetNamedSource("Gear", "Living Persona", out var persona) && persona is not null,
                    "persona-source", "The selected catalog has no unique living-persona rules source.");
                var bound = Source("Gear", persona!.SourceId);
                Need(nativeOwners is not null, "talent-owner", "The living persona needs its complete source-verified native grant graph.");
                var projected = nativeOwners.MatrixDevices3474(attributes).Where(d => d.SourceId == bound.SourceId).ToArray();
                Need(projected.Length == 1, "persona-owner", "The living persona must be the actual granted native gear owner.");
                var source = Xml(bound.SourceXml);
                Need(Text(source, "canformpersona") == "Self", "persona-source", "The selected source does not form an innate persona.");
                Need(source.Element("bonus")?.HasElements != true && source.Element("wirelessbonus")?.HasElements != true
                    && !source.Elements().Any(n => n.Name.LocalName.StartsWith("mod", StringComparison.Ordinal) && n.Value.Length != 0),
                    "persona-effects", "The current innate persona source requires unimplemented modifier semantics.");
                var active = ActiveMatrixOwners();
                Need(active.Length <= 1 && active.All(n => n.Name == "gear" && SameId(Text(n, "sourceid"), bound.SourceId.ToString("D"))),
                    "persona-mode", "The active hardware persona conflicts with the selected living-persona routine.", true);
                evidence.Add(new("Innate living persona owner " + projected[0].OwnerId + "; source " + bound.SourceId,
                    0, projected[0].SourceDigest));
                var device = projected[0];
                var values = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var (label, field, value) in new[] { ("Attack", "attack", device.Attack),
                    ("Sleaze", "sleaze", device.Sleaze), ("Data Processing", "dataprocessing", device.DataProcessing),
                    ("Firewall", "firewall", device.Firewall), ("Device Rating", "devicerating", device.DeviceRating) })
                {
                    Need(value > 0, "persona-attributes", "A required living-persona attribute is not usable.", true);
                    values.Add(label, value);
                    result.Add(Pass("living-persona:" + field, "Innate persona attribute from the current source; no device was fabricated.", value));
                }
                return values;
            }

            void Vehicle()
            {
                Need(action.VehicleSourceId is not null, "vehicle", "Select the actual owned vehicle/drone source.", true);
                var saved = RequiredOwner("vehicle", action.VehicleSourceId!);
                Need(saved.Parent?.Name == "vehicles" && saved.Parent.Parent == graph,
                    "vehicle-owner-path", "The selected chassis must be a current top-level vehicle owner.");
                Need(context.TryResolveVehicleWorkshopCatalog(out var catalog) && catalog.Binding.Exact
                    && string.Equals(catalog.Binding.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase),
                    "vehicle-source", "The current vehicle workshop source projection is unavailable or inexact.");
                var boundCatalog = CharacterCreationFinalizationDigest.Compute(catalog);
                checks.Add(() => context.TryResolveVehicleWorkshopCatalog(out var current)
                    && CharacterCreationFinalizationDigest.Compute(current) == boundCatalog);
                var rows = catalog.Chassis.Where(c => SameId(c.SourceId.Value.ToString("D"), action.VehicleSourceId!)).ToArray();
                Need(rows.Length == 1 && rows[0].ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact,
                    "vehicle-source", "The selected chassis source is missing, disabled, custom or unprojected.");
                var chassis = rows[0];
                Need(Guid.TryParse(Text(saved, "guid"), out var instance) && instance != Guid.Empty
                    && Text(saved, "name") == chassis.Name && Text(saved, "source") == chassis.SourceBook
                    && Text(saved, "category") == chassis.Category, "vehicle-owner", "The selected vehicle owner does not match the current chassis source.");
                evidence.Add(new("Vehicle instance " + instance.ToString("D") + "; source " + chassis.SourceId.Value, 0, boundCatalog));
                Need(Number(saved, "physicalcmfilled", 0) == 0 && Number(saved, "matrixcmfilled", 0) == 0,
                    "vehicle-damage", "Damaged vehicles need the current damage/control modifier projection.");
                Need(action.Target is "vehicle-handling:on-road" or "vehicle-handling:off-road",
                    "vehicle-action", "Select an ordinary handling test with an explicit road environment; speed, sensor, gunnery and autonomous Pilot are separate routines.");
                int rigRating = routine.Kind == RiggerJumpedIn ? JumpPrerequisites(saved, chassis, catalog) : 0;
                Need(rigRating > 0 || saved.Element("mods")?.HasElements != true,
                    "vehicle-modifications", "Modified handling requires its source-bound modification projection.");
                Need(chassis.FactoryModifications.Count == 0, "vehicle-factory-modifications", "This factory-modified chassis requires the modification action projection.");
                string handling = Text(saved, "handling");
                bool slash = handling.Contains('/');
                // Imported saves may use on/off notation; the native workshop writer
                // saves two scalars. Reconcile both against the same typed source,
                // including a redundant off-road scalar when an import supplies one.
                Need(slash ? handling == chassis.Handling.ToString(CultureInfo.InvariantCulture) + "/"
                        + chassis.OffRoadHandling.ToString(CultureInfo.InvariantCulture)
                    : Number(saved, "handling") == chassis.Handling,
                    "vehicle-source", "Saved handling differs from the current source/amendment.");
                Need(saved.Element("offroadhandling") is not null ? Number(saved, "offroadhandling") == chassis.OffRoadHandling
                    : slash || chassis.Handling == chassis.OffRoadHandling,
                    "vehicle-source", "Saved off-road handling is missing or differs from the current source/amendment.");
                Need(chassis.Category is "Bikes" or "Cars" or "Trucks" or "Municipal/Construction" or "Corpsec/Police/Military",
                    "vehicle-skill", "This bounded handling slice resolves ground vehicles only; drone movement mode and other pilot skills need source-backed mappings.");
                int limit = action.Target.EndsWith(":off-road", StringComparison.Ordinal) ? chassis.OffRoadHandling : chassis.Handling;
                int pool = checked(Skill("Pilot Ground Craft") + Attribute("REA"));
                if (routine.Kind == RiggerJumpedIn)
                {
                    Pool(checked(pool + rigRating), checked(limit + rigRating + 2),
                        "Direct cold-sim jumped-in Pilot Ground Craft + REA + installed control rig [terrain Handling + rig + 2 VR]. No hot-sim, remote Data Processing clamp or wireless Noise (SR5 201, 266, 452).");
                    result.Add(Pass("vehicle-threshold-reduction", "The rig reduces a supplied Vehicle Test threshold, to a minimum of 1. No threshold was supplied and no success probability is computed.", rigRating));
                    result.Add(new("vehicle-feedback-risk", Unsupported,
                        "Cold-sim feedback is Stun; incoming vehicle damage and feedback resistance are separate. This handling calculation does not assess a damage probability.", null, null, []));
                }
                else if (routine.Kind == RiggerManual)
                {
                    Need(action.AccessMode == "manual;owner;onboard;uncontested", "manual-access", "Manual control requires declared onboard, owner, uncontested access.");
                    Need(chassis.Kind == CharacterVehicleChassisKind.Vehicle && chassis.Seats > 0,
                        "manual-controls", "The selected chassis lacks an occupied vehicle control position.", true);
                    Pool(pool, limit, "Manual Pilot Ground Craft + REA [terrain Handling]; no controller, VR or control-rig bonus (SR5 199–203).");
                }
                else
                {
                    Need(action.AccessMode == "ar;owner;wireless;noise=0;uncontested", "remote-access", "Remote AR requires declared owner control, wireless, zero noise and uncontested control.");
                    var device = Device(false);
                    Need(device["Data Processing"] > 0, "remote-controller", "The selected controller has no usable Data Processing.", true);
                    Pool(pool, Math.Min(checked(limit + 1), device["Data Processing"]),
                        "Remote AR Control Device: Pilot Ground Craft + REA [minimum of terrain Handling + 1 AR and controller Data Processing]. Owner access supplies required marks; no control-rig or VR bonus (SR5 201, 238, 265).");
                }
            }

            int JumpPrerequisites(XElement saved, CharacterVehicleWorkshopChassisEntry chassis, CharacterVehicleWorkshopCatalog catalog)
            {
                Need(action.AccessMode == "cold-sim;owner;direct;uncontested", "jumped-access", "This prerequisite check requires declared owner permission, direct connection, cold-sim and no other jumped-in controller.");
                var rigs = graph.Descendants("cyberware").Where(n => routine.RequiredEquipmentSourceIds.Any(id => SameId(id, Text(n, "sourceid"))))
                    .Select(n => (Saved: n, Source: Source("Cyberware", Guid.Parse(Text(n, "sourceid")))))
                    .Where(p => p.Source.Name == "Control Rig").ToArray();
                Need(rigs.Length == 1, "control-rig", "Select one actual owned Control Rig source; remote/manual control does not supply a rig.", true);
                var rig = rigs[0].Saved;
                var rigSource = rigs[0].Source;
                Bind(rig, rigSource);
                var canonical = Xml(rigSource.SourceXml);
                int rating = Number(rig, "rating");
                Need(rig.Parent?.Name == "cyberwares" && rig.Parent.Parent == graph
                    && Text(rig, "plugsintomodularmount").Length == 0 && !True(rig, "addtoparent"),
                    "control-rig-installation", "A directly installed rig is required; modular or nested ware needs its installation projection.");
                Need(rigSource.SourceBook == "SR5" && Number(canonical, "minrating") == 1
                    && Number(canonical, "rating") == 3 && rating is >= 1 and <= 3
                    && canonical.Element("bonus")?.HasElements != true,
                    "control-rig-rating", "The selected source is outside the bounded SR5 rating-1-to-3 control rig semantics.");
                bool adapted = chassis.Kind == CharacterVehicleChassisKind.Drone;
                foreach (var mod in saved.Element("mods")?.Elements("mod") ?? [])
                {
                    var matches = catalog.Modifications.Where(m => SameId(Text(mod, "sourceid"), m.SourceId.Value.ToString("D"))).ToArray();
                    Need(matches.Length == 1 && matches[0].Name == "Rigger Interface" && matches[0].SourceBook == "SR5"
                        && matches[0].ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                        && Text(mod, "name") == matches[0].Name && Text(mod, "source") == matches[0].SourceBook
                        && True(mod, "equipped") && Number(mod, "rating") == 0
                        && Guid.TryParse(Text(mod, "guid"), out var modId) && modId != Guid.Empty,
                        "vehicle-modifications", "Only an installed source-bound ordinary Rigger Interface is supported in this jumped-in handling slice.");
                    adapted = true;
                }
                Need(adapted, "rigger-adaptation", "The actual selected vehicle needs an installed Rigger Interface; drone interfaces are intrinsic (SR5 241, 266).", true);
                // Preserve actual native factory children, rather than adding a free
                // sim module/datajack because the routine's role says 'Rigger'.
                var sim = rig.Element("gears")?.Elements("gear").Where(n => Text(n, "name") == "Sim Module, Hot").ToArray() ?? [];
                var jack = rig.Element("children")?.Elements("cyberware").Where(n => Text(n, "name") == "Datajack").ToArray() ?? [];
                Need(sim.Length == 1 && jack.Length == 1 && True(sim[0], "equipped"),
                    "rig-interface-path", "The installed control rig must retain its actual factory sim module and datajack in the current owner graph.", true);
                Need(canonical.Element("gears")?.Elements("usegear").Any(n => Text(n, "name") == "Sim Module, Hot") == true
                    && canonical.Element("subsystems")?.Elements("cyberware").Any(n => Text(n, "name") == "Datajack") == true,
                    "rig-source-path", "The current amended control rig no longer declares the supported factory interface path.");
                var simSource = Source("Gear", Guid.Parse(Text(sim[0], "sourceid")));
                Need(simSource.Name == "Sim Module, Hot", "rig-interface-path", "The installed sim identity differs from the factory path.");
                Gear(sim[0], simSource, 0);
                var jackSource = Source("Cyberware", Guid.Parse(Text(jack[0], "sourceid")));
                Need(jackSource.Name == "Datajack" && Text(jack[0], "plugsintomodularmount").Length == 0,
                    "rig-interface-path", "The installed datajack identity/path is unsupported.");
                Bind(jack[0], jackSource);
                var active = ActiveMatrixOwners();
                Need(active.Length == 1 && ReferenceEquals(active[0], saved),
                    "jumped-persona", "Select only the directly connected vehicle as current persona device; another active Matrix owner is a different access path.", true);
                evidence.Add(new("Installed control rig rating; SR5 452", rating, CharacterCreationFinalizationDigest.Compute(rigSource)));
                return rating;
            }

            XElement[] ActiveMatrixOwners() => graph.Descendants().Where(n =>
                n.Name.LocalName is "gear" or "vehicle" or "armor" or "weapon" or "cyberware"
                && True(n, "active")).ToArray();

            void Pool(int pool, int limit, string detail)
            {
                Need(limit > 0, "action-limit", "The actual action limit is not usable.", true);
                result.Add(Minimum("routine-action-pool", pool, routine.Targets.MinimumSkillDicePool, detail));
                result.Add(Pass("routine-action-limit", "Actual action limit. The brief's physical/mental/social minimum remains a separate base-limit target.", limit));
            }

            CharacterCreationRoleFinding Minimum(string code, int actual, int target, string detail)
                => new(code, actual >= target ? AssessedPass : AssessedFailure, detail, actual, target, evidence.ToArray());
            CharacterCreationRoleFinding Pass(string code, string detail, int? value)
                => new(code, AssessedPass, detail, value, null, evidence.ToArray());
        }
        catch (RoutineUnavailable unavailable)
        {
            // No partial numeric success survives a missing dependency or source drift.
            return [new("routine-" + unavailable.Code, unavailable.Failure ? AssessedFailure : Unsupported,
                unavailable.Message, null, null, [])];
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or XmlException)
        {
            return [new("routine-malformed-source", Unsupported,
                "The current owner graph or source expression is malformed or unsupported; no action value was inferred.", null, null, [])];
        }
    }

    // Same native ComplexForm minimum and existing rating-expression arithmetic.
    // Only the level token changes; arbitrary XPath/functions are not introduced.
    internal static bool TryFadingValue(string expression, int level, out int value)
    {
        value = 0;
        if (level <= 0 || expression.Length == 0 || expression.Any(c => !char.IsDigit(c) && c is not ('L' or '+' or '-' or ' '))) return false;
        if (!CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            expression.Replace("L", level.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), 0, out int raw)) return false;
        value = Math.Max(2, raw);
        return true;
    }

    private sealed class RoutineUnavailable(string code, string message, bool failure) : Exception(message)
    { internal string Code { get; } = code; internal bool Failure { get; } = failure; }
    private static void Need([DoesNotReturnIf(false)] bool valid, string code, string detail, bool failure = false)
    { if (!valid) throw new RoutineUnavailable(code, detail, failure); }
    private static string Text(XElement row, string field, string fallback = "")
    {
        var nodes = row.Elements(field).ToArray();
        if (nodes.Length > 1 || nodes.Any(n => n.HasElements || n.HasAttributes)) throw new InvalidDataException();
        return nodes.Length == 0 ? fallback : nodes[0].Value;
    }
    private static int Number(XElement row, string field, int? fallback = null)
        => int.Parse(Text(row, field, fallback?.ToString(CultureInfo.InvariantCulture) ?? ""), CultureInfo.InvariantCulture);
    private static bool True(XElement row, string field) => bool.TryParse(Text(row, field), out bool value) && value;
    private static bool SameId(string a, string b) => Guid.TryParse(a, out var x) && Guid.TryParse(b, out var y) && x != Guid.Empty && x == y;
    private static XElement Xml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        return XElement.Load(reader);
    }
}
