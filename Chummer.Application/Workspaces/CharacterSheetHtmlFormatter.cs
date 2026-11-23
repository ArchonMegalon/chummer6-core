using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Canonical printable sheet. Every section is projected from the caller's captured
/// envelope; this formatter never reads a store, follows linked files, or computes rules.
/// </summary>
internal sealed class CharacterSheetHtmlFormatter
{
    private const string Unavailable = "Unavailable in this character's current projection.";
    private readonly IRulesetWorkspaceCodec _codec;
    private readonly WorkspacePayloadEnvelope _envelope;
    private readonly StringBuilder _html = new();
    private CharacterSheetDerivedSection? _derived;
    private bool IsSr5 => string.Equals(_envelope.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase);

    private CharacterSheetHtmlFormatter(IRulesetWorkspaceCodec codec, WorkspacePayloadEnvelope envelope)
    {
        _codec = codec;
        _envelope = envelope;
    }

    public static string Format(
        IRulesetWorkspaceCodec codec,
        WorkspacePayloadEnvelope envelope,
        DataExportBundle bundle,
        string title)
        => new CharacterSheetHtmlFormatter(codec, envelope).Build(bundle, title);

    private string Build(DataExportBundle bundle, string title)
    {
        // Prefer the full queries: the portable export's optimized projection can be narrower.
        CharacterProfileSection? profile = Section<CharacterProfileSection>("profile") ?? bundle.Profile;
        CharacterProgressSection? progress = Section<CharacterProgressSection>("progress") ?? bundle.Progress;
        CharacterInventorySection? inventory = Section<CharacterInventorySection>("inventory") ?? bundle.Inventory;
        CharacterGearSection? gear = Section<CharacterGearSection>("gear");

        _html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />")
            .Append("<title>").Append(Text(title)).Append("</title>")
            .Append("""
                <style>
                @page { margin: 14mm; }
                * { box-sizing: border-box; }
                body { font-family: 'Segoe UI', 'Noto Sans', sans-serif; font-size: 10pt; line-height: 1.4; color: #111; margin: 1rem; }
                h1 { font-size: 22pt; } h2 { font-size: 15pt; border-bottom: 1px solid #777; padding-bottom: .2em; }
                h1, h2, h3, caption { break-after: avoid; page-break-after: avoid; }
                section { margin-bottom: 1.2em; }
                table { width: 100%; border-collapse: collapse; table-layout: fixed; margin: .5em 0 1em; }
                thead { display: table-header-group; } tfoot { display: table-footer-group; }
                th, td { text-align: left; vertical-align: top; border: 1px solid #aaa; padding: .4em; }
                th { background: #eee; } tr { break-inside: avoid; page-break-inside: avoid; }
                .items th:first-child { width: 23%; } .items th:last-child { width: 27%; }
                dl { margin: 0; } dt { font-weight: bold; } dd { margin: 0 0 .35em; }
                p, li, dd, td, th, h1, h2, h3 { overflow-wrap: anywhere; word-wrap: break-word; }
                .text { white-space: pre-wrap; } p { orphans: 3; widows: 3; }
                .availability { color: #444; font-style: italic; }
                @media print { body { margin: 0; } a { color: inherit; } }
                </style></head><body>
                """)
            .Append("<h1>").Append(Text(title)).Append("</h1>");

        Start("Identity and edition");
        _html.Append(Fields(("Edition", Edition())));
        if (profile is null)
        {
            _html.Append(Fields(("Name", bundle.Summary.Name), ("Alias", bundle.Summary.Alias),
                ("Metatype", bundle.Summary.Metatype)));
            Availability(Unavailable);
        }
        else
        {
            _html.Append(Fields(("Name", profile.Name), ("Alias", profile.Alias), ("Player", profile.PlayerName),
                ("Metatype", profile.Metatype), ("Metavariant", profile.Metavariant), ("Concept", profile.Concept),
                ("Mode", profile.Created ? "Career" : "Creation"), ("Build method", profile.BuildMethod),
                ("Gameplay option", profile.GameplayOption), ("Sex", profile.Sex), ("Age", profile.Age),
                ("Height", profile.Height), ("Weight", profile.Weight), ("Hair", profile.Hair),
                ("Eyes", profile.Eyes), ("Skin", profile.Skin), ("Primary arm", profile.PrimaryArm),
                ("Ambidextrous", profile.Ambidextrous)));
        }
        End();

        Start("Progress and resources");
        if (progress is null) Availability(Unavailable);
        else _html.Append(Fields(("Karma", progress.Karma), ("Nuyen", progress.Nuyen),
            ("Starting nuyen", progress.StartingNuyen), ("Street cred", progress.StreetCred),
            ("Notoriety", progress.Notoriety), ("Public awareness", progress.PublicAwareness),
            ("Burnt street cred", progress.BurntStreetCred), ("Astral reputation", progress.AstralReputation),
            ("Wild reputation", progress.WildReputation), ("Initiate grade", progress.InitiateGrade),
            ("Submersion grade", progress.SubmersionGrade)));
        End();

        _derived = IsSr5 ? Section<CharacterSheetDerivedSection>("sheetderived") : null;
        CharacterAttributesSection? attributes = Section<CharacterAttributesSection>("attributes") ?? bundle.Attributes;
        Table("Attributes", ["Attribute", "Base", "Total"], attributes?.Attributes.Select(
            a => new object?[] { a.Name, a.BaseValue, IsSr5
                ? Number(_derived?.Attributes.FirstOrDefault(value => value.Name == a.Name)?.Total)
                : a.TotalValue }));
        CharacterSkillsSection? skills = Section<CharacterSkillsSection>("skills") ?? bundle.Skills;
        var skillEntries = skills?.Skills.Select(SkillEntry).ToList();
        if (IsSr5 && _derived is { UnavailableReason: null })
        {
            // Core has already resolved these unsaved group members against the same
            // captured payload. Keep explicit rows (and their notes/specs) exactly once.
            var savedIds = skills?.Skills.Select(s => s.Guid).ToHashSet(StringComparer.Ordinal) ?? [];
            foreach (var member in _derived.Skills.Where(s => s.Guid.StartsWith("group:", StringComparison.Ordinal)
                         && !savedIds.Contains(s.Guid)))
                (skillEntries ??= []).Add(new Entry(member.Name,
                    Fields(("Kind", "Active"), ("Effective rating", Number(member.EffectiveRating)),
                        ("Dice pool", Number(member.DicePool)), ("Linked attribute", member.Attribute))
                        + SkillAlternatives(member)));
        }
        Items("Skills and ratings", skillEntries, entry => entry);
        Derived(progress);

        Items("Gear", gear?.Gear, g => new Entry(Name(g.Name, g.CustomName),
            Fields(("Category", g.Category), ("Rating", g.Rating), ("Quantity", g.Quantity),
                ("Equipped", g.Equipped), ("Location", g.Location), ("Contained in", g.ParentName),
                ("Cost", g.Cost), ("Source", g.Source)), g.Notes), inventory?.GearNames);
        Items("Weapons", Section<CharacterWeaponsSection>("weapons")?.Weapons, w => new Entry(Name(w.Name, w.CustomName),
            Fields(("Category", w.Category), ("Type", w.Type), ("Damage", w.Damage), ("AP", w.AP),
                ("Accuracy", w.Accuracy), ("Mode", w.Mode), ("Ammo", w.Ammo),
                ("Equipped", w.Equipped), ("Cost", w.Cost), ("Source", w.Source)), w.Notes), inventory?.WeaponNames);
        Items("Armor", Section<CharacterArmorsSection>("armors")?.Armors, a => new Entry(Name(a.Name, a.CustomName),
            Fields(("Category", a.Category), ("Armor", a.ArmorValue), ("Rating", a.Rating),
                ("Equipped", a.Equipped), ("Cost", a.Cost), ("Source", a.Source)), a.Notes), inventory?.ArmorNames);
        Items("Augmentations", Section<CharacterCyberwaresSection>("cyberwares")?.Cyberwares, a => new Entry(Name(a.Name, a.CustomName),
            Fields(("Category", a.Category), ("Rating", a.Rating), ("Grade", a.Grade), ("Essence", a.Essence),
                ("Capacity", a.Capacity), ("Location", a.Location), ("Installed in", a.ParentName),
                ("Equipped", a.Equipped), ("Cost", a.Cost), ("Source", a.Source)), a.Notes), inventory?.CyberwareNames);
        Items("Vehicles and drones", Section<CharacterVehiclesSection>("vehicles")?.Vehicles, v => new Entry(Name(v.Name, v.CustomName),
            Fields(("Category", v.Category), ("Handling", v.Handling), ("Speed", v.Speed), ("Body", v.Body),
                ("Armor", v.Armor), ("Sensor", v.Sensor), ("Seats", v.Seats),
                ("Physical damage", v.PhysicalDamage),
                ("Physical track", IsSr5 && v.PhysicalConditionMaximumExact ? (object)v.PhysicalConditionMaximum : Unavailable),
                ("Matrix damage", v.MatrixDamage),
                ("Matrix track", IsSr5 && v.MatrixConditionMaximumExact ? (object)v.MatrixConditionMaximum : Unavailable),
                ("Cost", v.Cost), ("Source", v.Source)),
            v.Notes), inventory?.VehicleNames);
        Items("Drugs", Section<CharacterDrugsSection>("drugs")?.Drugs, d => new Entry(Name(d.Name, d.CustomName),
            Fields(("Category", d.Category), ("Rating", d.Rating), ("Quantity", d.Quantity), ("Source", d.Source)), d.Notes));
        EquipmentModifications();

        CharacterQualitiesSection? qualities = Section<CharacterQualitiesSection>("qualities") ?? bundle.Qualities;
        Items("Qualities", qualities?.Qualities, q => new Entry(Name(q.Name, q.CustomName),
            Fields(("Source", q.Source), ("BP", q.BP), ("Level", q.LevelSemantics?.Level)), q.Notes));
        Magic(gear);
        CharacterContactsSection? contacts = Section<CharacterContactsSection>("contacts") ?? bundle.Contacts;
        Items("Contacts", contacts?.Contacts, c => new Entry(Name(c.Name, c.CustomName),
            Fields(("Role", c.Role), ("Location", c.Location), ("Connection", c.Connection), ("Loyalty", c.Loyalty),
                ("Metatype", c.Metatype), ("Type", c.ContactType), ("Group", c.GroupName),
                ("Preferred payment", c.PreferredPayment), ("Hobbies / vice", c.HobbiesVice),
                ("Personal life", c.PersonalLife)), c.Notes));
        CharacterLifestylesSection? lifestyles = Section<CharacterLifestylesSection>("lifestyles") ?? bundle.Lifestyles;
        Items("Lifestyles", lifestyles?.Lifestyles, l => new Entry(Name(l.Name, l.CustomName),
            Fields(("Base lifestyle", l.BaseLifestyle), ("Cost", l.Cost), ("Months", l.Months), ("Source", l.Source)), l.Notes));
        Start("Notes and background");
        if (profile is null) Availability(Unavailable);
        else
        {
            Note("Description", profile.Description);
            Note("Background", profile.Background);
            Note("Character notes", profile.CharacterNotes);
            Note("Game notes", profile.GameNotes);
            Note("Group notes", profile.GroupNotes);
        }
        End();
        _html.Append("</body></html>");
        return _html.ToString();

        string Edition() => _envelope.RulesetId.ToLowerInvariant() switch
        {
            "sr4" => "Shadowrun Fourth Edition (SR4)",
            "sr5" => "Shadowrun Fifth Edition (SR5)",
            "sr6" => "Shadowrun Sixth Edition (SR6)",
            _ => _envelope.RulesetId
        };
    }

    private void Derived(CharacterProgressSection? progress)
    {
        Start("Derived statistics");
        if (IsSr5) _html.Append(Fields(("Essence", _derived?.Essence is { } essence ? (object)essence : Unavailable)));
        else if (progress is null) Availability("Essence: " + Unavailable);
        else _html.Append(Fields(("Essence", progress.TotalEssence)));
        if (IsSr5)
        {
            MovementMode("Ground", _derived?.Movement?.Ground);
            MovementMode("Swim", _derived?.Movement?.Swim);
            MovementMode("Fly", _derived?.Movement?.Fly);
        }
        else
        {
            CharacterMovementSection? movement = Section<CharacterMovementSection>("movement");
            if (movement is null) Availability("Movement: " + Unavailable);
            else _html.Append(Fields(("Walk", movement.Walk), ("Run", movement.Run), ("Sprint", movement.Sprint),
                ("Alternate walk", movement.WalkAlt), ("Alternate run", movement.RunAlt), ("Alternate sprint", movement.SprintAlt)));
        }
        CharacterConditionMonitorSection? monitor = Section<CharacterConditionMonitorSection>("conditionmonitor");
        if (monitor is null) Availability("Condition monitors: " + Unavailable);
        else if (IsSr5)
        {
            var condition = !monitor.PhysicalActsAsCore && !monitor.StunActsAsMatrix ? _derived?.Condition : null;
            _html.Append(Fields(("Physical track", Number(condition?.PhysicalMaximum)), ("Physical damage", monitor.PhysicalFilled),
                ("Physical overflow", monitor.PhysicalOverflow), ("Overflow modifier (boxes)", Number(condition?.OverflowModifier)),
                ("Physical threshold offset", Number(condition?.PhysicalThresholdOffset)),
                ("Physical recovery", Number(condition?.PhysicalRecoveryPool)), ("Stun track", Number(condition?.StunMaximum)),
                ("Stun damage", monitor.StunFilled), ("Stun threshold offset", Number(condition?.StunThresholdOffset)),
                ("Stun recovery", Number(condition?.StunRecoveryPool)), ("Wound threshold", Number(condition?.WoundThreshold)),
                ("Wound modifier", Number(condition?.WoundModifier))));
        }
        else _html.Append(Fields(("Physical track", monitor.PhysicalTrack), ("Physical damage", monitor.PhysicalFilled),
            ("Physical overflow", monitor.PhysicalOverflow), ("Physical threshold offset", monitor.PhysicalThresholdOffset),
            ("Physical recovery", monitor.PhysicalNaturalRecovery), ("Stun track", monitor.StunTrack),
            ("Stun damage", monitor.StunFilled), ("Stun threshold offset", monitor.StunThresholdOffset),
            ("Stun recovery", monitor.StunNaturalRecovery), ("Physical acts as core", monitor.PhysicalActsAsCore),
            ("Stun acts as matrix", monitor.StunActsAsMatrix)));
        _html.Append(Fields(("Physical initiative", _derived?.PhysicalInitiative is { } initiative
                ? $"{initiative.Value.ToString(CultureInfo.InvariantCulture)} + {initiative.Dice.ToString(CultureInfo.InvariantCulture)}D6" : Unavailable),
            ("Defense pool", Number(_derived?.DefensePool)),
            ("Physical limit", Number(_derived?.PhysicalLimit)),
            ("Mental limit", Number(_derived?.MentalLimit)),
            ("Social limit", Number(_derived?.SocialLimit))));
        if (_derived?.DefenseUnavailableReason is not null)
            Availability("Defense: A modifier for this character cannot be resolved for a defense test.");
        if (_derived?.UnavailableReason is not null) Availability(Unavailable);
        End();
        if (IsSr5) DerivedEffects();
        else Items("Limit modifiers", Section<CharacterLimitModifiersSection>("limitmodifiers")?.LimitModifiers,
            m => new Entry(m.Name, Fields(("Limit", m.Limit), ("Bonus", m.Bonus), ("Condition", m.Condition))));
        Items("Spell defense", Section<CharacterSpellDefenseSection>("spelldefense")?.Metrics,
            m => new Entry(m.Label, Fields(("Base", m.BaseValue), ("Counterspelling", m.CounterspellingDice),
                ("Total", m.TotalValue), ("Formula", m.Formula))));
    }

    private void MovementMode(string label, CharacterSheetMovementMode? mode)
    {
        if (mode?.IsAvailable is not true)
        {
            _html.Append(Fields((label + " movement", mode?.IsAvailable is false ? "Not available" : Unavailable)));
            if (mode?.UnavailableReason is not null) Availability(label + " movement: " + Unavailable);
            return;
        }
        _html.Append(Fields((label + " walk (m/turn)", (object?)mode.WalkMetersPerTurn ?? Unavailable),
            (label + " run (m/turn)", (object?)mode.RunMetersPerTurn ?? Unavailable),
            (label + " sprint (m/hit)", (object?)mode.SprintMetersPerHit ?? Unavailable)));
    }

    private Entry SkillEntry(CharacterSkillSummary skill)
    {
        var matches = _derived?.Skills.Where(value => value.Guid == skill.Guid).ToArray();
        CharacterSheetSkillValue? value = matches is { Length: 1 } ? matches[0] : null;
        return new Entry(Name(value?.Name ?? skill.Name, skill.CustomName, "Unnamed skill"),
            Fields(("Category", skill.Category), ("Kind", skill.IsKnowledge ? "Knowledge" : "Active"),
                ("Base rating", skill.BaseValue), ("Karma rating", skill.KarmaValue),
                ("Effective rating", value?.IsNativeLanguage == true ? "Native language" : Number(value?.EffectiveRating)),
                ("Dice pool", value?.IsNativeLanguage == true ? "Not a dice test" : Number(value?.DicePool)),
                ("Linked attribute", value?.Attribute),
                ("Specializations", string.Join(", ", skill.Specializations))) + SkillAlternatives(value), skill.Notes);
    }

    private static string SkillAlternatives(CharacterSheetSkillValue? skill)
    {
        if (skill?.UnavailableReason is { } reason)
            return Fields(("Test availability", reason switch
            {
                "A source-owned conditional skill modifier requires action context." => "Requires the situation for this test.",
                "The source-owned quality blocks defaulting in this skill category." => "Defaulting is not allowed in this skill category.",
                "Movement-dependent skill availability has not been resolved." => "Movement for this skill has not been resolved.",
                _ => Unavailable
            }));
        if (skill is null || skill.IsNativeLanguage || skill.DicePool is null || skill.Specializations.Count == 0) return "";
        return Fields(("Specialization use", "Alternative pools for matching tests; replace the ordinary dice pool."))
            + string.Concat(skill.Specializations.Select(spec => Fields(("Specialization", spec.Name),
                ("Alternative dice pool (dice)", spec.DicePool), ("Included specialization bonus (dice)", Signed(spec.Bonus)))));
    }

    private void DerivedEffects()
    {
        bool exact = _derived is { UnavailableReason: null };
        Items("Conditional limit modifiers", exact ? _derived!.OwnedConditionalLimits : null,
            limit => new Entry(LimitName(limit.Limit), Fields(("Modifier", Signed(limit.Modifier)),
                ("Condition", SheetCondition(limit.Condition)), ("Status", limit.Enabled ? "Enabled" : "Disabled"),
                ("Application", "Separate from the base limit; applies only when enabled and the condition holds."))));
        // SR5 uses the owner-checked list once. Saved limit rows have no separate authority.
        var pools = exact ? _derived!.OtherPoolsAndModifiers : null;
        Items("Other dice pools", pools?.Where(pair => IsOtherPool(pair.Key)).ToArray(),
            pair => new Entry(OtherLabel(pair.Key), Fields(("Dice pool (dice)", pair.Value))));
        Items("Other modifiers", pools?.Where(pair => !IsOtherPool(pair.Key)).ToArray(),
            pair => new Entry(OtherLabel(pair.Key), Fields(("Modifier", OtherModifier(pair.Key, pair.Value)))));
        Items("Granted traits", _derived is null || !exact && _derived.GrantedTraits.Count == 0 ? null : _derived.GrantedTraits,
            trait => new Entry(trait.Name, Fields(("Kind", trait.Kind switch
                { "Quality" => "Quality", "CritterPower" => "Critter power", _ => "Trait" }),
                ("Selection", trait.Selection), ("Rating", trait.Rating), ("Source", trait.SourceBook))
                + (trait.MechanicsUnavailableReason is null ? "" : Fields(("Effects", Unavailable)))
                + (trait.ActionUnavailableReason is null ? "" : Fields(("Actions", TraitAction(trait.ActionUnavailableReason))))));
        Items("Unlocked skills", exact ? _derived!.UnlockedSkills.DistinctBy(skill => skill.Name, StringComparer.Ordinal).ToArray() : null,
            skill => new Entry(skill.Name, Fields(("Availability", "Available to learn; this grant adds no rating or dice."))));
        // Movement values already include these replacements/additions. Do not list them again as modifiers to apply.
        Items("Rules for specific tests and advancement", exact ? _derived!.RuleModifiers?.Where(rule => rule.Kind is not
            (CharacterSheetRuleModifierKind.WalkMultiplierReplacement or CharacterSheetRuleModifierKind.RunMultiplierReplacement
            or CharacterSheetRuleModifierKind.SprintMetersPerHitReplacement or CharacterSheetRuleModifierKind.SprintMetersPerHitAddition))
            .ToArray() ?? [] : null, RuleEntry);
    }

    private static Entry RuleEntry(CharacterSheetRuleModifier rule) => rule.Kind switch
    {
        CharacterSheetRuleModifierKind.AdditionalLimb => new Entry("Additional limbs",
            Fields(("Limb", rule.Target switch { "arm" => "Arms", "leg" => "Legs", _ => Unavailable }), ("Count", rule.Value))),
        CharacterSheetRuleModifierKind.SkillCategoryDefaultBlocked => new Entry("Skill defaulting",
            Fields(("Category", SheetTarget(rule.Target)), ("Defaulting", "Not allowed"))),
        CharacterSheetRuleModifierKind.SkillCategoryKarmaCostPercent => CostRule("Skill karma cost", rule),
        CharacterSheetRuleModifierKind.SkillCategorySpecializationKarmaCostPercent => CostRule("Specialization karma cost", rule),
        CharacterSheetRuleModifierKind.SkillGroupCategoryKarmaCostPercent => CostRule("Skill group karma cost", rule),
        CharacterSheetRuleModifierKind.ConditionalSkill => ConditionalRule("Skill", rule),
        CharacterSheetRuleModifierKind.ConditionalSkillGroup => ConditionalRule("Skill group", rule),
        CharacterSheetRuleModifierKind.ConditionalSkillCategory => ConditionalRule("Skill category", rule),
        CharacterSheetRuleModifierKind.ConditionalSkillAttribute => ConditionalRule("Linked attribute", rule),
        CharacterSheetRuleModifierKind.UnresolvedDefenseTest => new Entry("Unresolved defense modifier",
            Fields(("Declared modifier (dice)", Signed(rule.Value)), ("Application", "Not applied; the defense test remains unavailable."))),
        _ => new Entry("Rule effect", Fields(("Effect", Unavailable)))
    };

    private static Entry CostRule(string label, CharacterSheetRuleModifier rule) => new(label,
        Fields(("Category", SheetTarget(rule.Target)), ("Cost (% of normal)", rule.Value),
            ("Application", "Applies to this category only; adds no dice.")));

    private static Entry ConditionalRule(string targetLabel, CharacterSheetRuleModifier rule) => new("Conditional skill modifier",
        Fields((targetLabel, SheetTarget(rule.Target)), (rule.AppliesToRating ? "Rating modifier" : "Dice modifier (dice)", Signed(rule.Value)),
            ("Condition", SheetCondition(rule.Condition)), ("Except", string.IsNullOrWhiteSpace(rule.Exclude) ? "None" : SheetTarget(rule.Exclude)),
            ("Application", "Requires a specific test; not included in ordinary pools.")));

    private static string TraitAction(string reason) => reason switch
    {
        "Critter-power actions require a separate action authority." => "Power actions have not been resolved for this sheet.",
        "Granted weapon attacks require a separate attack authority." => "Granted weapon attacks have not been resolved.",
        "This source has no typed action mechanics; situational trait effects require separate action authority." => "Situational effects require a specific test.",
        _ => Unavailable
    };

    private static bool IsOtherPool(string key) => key is "Composure" or "Memory" or "JudgeIntentionsDefense";

    private static string OtherLabel(string key) => key switch
    {
        "Composure" => "Composure", "Memory" => "Memory", "JudgeIntentionsDefense" => "Judge Intentions defense",
        "DamageResistanceModifier" => "Damage resistance dice modifier", "ArmorModifier" => "Armor modifier",
        "Reach" => "Reach modifier", "LifestyleCost" => "Lifestyle cost modifier (%)", "SpellResistance" => "Spell resistance dice modifier",
        "PathogenContactResist" => "Contact pathogen resistance dice modifier", "PathogenIngestionResist" => "Ingested pathogen resistance dice modifier",
        "PathogenInhalationResist" => "Inhaled pathogen resistance dice modifier", "PathogenInjectionResist" => "Injected pathogen resistance dice modifier",
        "ToxinContactResist" => "Contact toxin resistance dice modifier", "ToxinIngestionResist" => "Ingested toxin resistance dice modifier",
        "ToxinInhalationResist" => "Inhaled toxin resistance dice modifier", "ToxinInjectionResist" => "Injected toxin resistance dice modifier",
        "NotorietyModifier" => "Notoriety modifier",
        _ when key.StartsWith("SpellDicePool:", StringComparison.Ordinal) => "Spell dice modifier: " + SheetTarget(key[14..]),
        _ => "Other modifier"
    };

    private static string OtherModifier(string key, int value) => OtherLabel(key) == "Other modifier" ? Unavailable : Signed(value);
    private static string Signed(decimal value) => value.ToString("+0.############################;-0.############################;0", CultureInfo.InvariantCulture);
    private static string LimitName(string limit) => limit switch
        { "Physical" => "Physical limit", "Mental" => "Mental limit", "Social" => "Social limit", _ => "Limit" };
    private static string SheetTarget(string target) => target switch
    {
        "BOD" => "Body", "AGI" => "Agility", "REA" => "Reaction", "STR" => "Strength", "CHA" => "Charisma",
        "INT" => "Intuition", "LOG" => "Logic", "WIL" => "Willpower", "MAG" => "Magic", "MAGAdept" => "Adept Magic",
        "RES" => "Resonance", "EDG" => "Edge",
        _ when Guid.TryParse(target, out _) || target.Length == 64 && target.All(Uri.IsHexDigit) => Unavailable,
        _ => target
    };

    // Reuse the established English condition labels; this sheet currently has no locale input.
    // Unrecognized localization keys or expression conditions must not masquerade as readable conditions.
    private static string SheetCondition(string condition) => condition switch
    {
        "LimitCondition_AttribSkillINT" => "Only with Intuition-linked skills",
        "LimitCondition_AttribSkillLOG" => "Only with Logic-linked skills",
        "LimitCondition_BunkerGearVisible" => "Only during emergency situations, Must be visible",
        "LimitCondition_ClimbingTests" => "For climbing only",
        "LimitCondition_CorporationVisible" => "Only with appropriate corp, Must be visible",
        "LimitCondition_CyberwareBalanceTail" => "Only for Gymnastics (Climbing), Gymnastics (Jumping), Running, and knockdown resist",
        "LimitCondition_CyberwareCyberfins" => "Only for making repairs, typing, dancing etc., Must be engaged",
        "LimitCondition_CyberwareHydraulicJacks" => "Only for Gymnastics (Jumping) and Running (Sprinting)",
        "LimitCondition_CyberwareMonkeyFoot" => "Only when used as hand",
        "LimitCondition_CyberwareRaptorFoot" => "Only when attacking with the legs",
        "LimitCondition_CyberwareSnakeFingers" => "Only with manual dexterity",
        "LimitCondition_ExcludeFansGangers" => "Except for Urban Brawl Fans and Gangers",
        "LimitCondition_ExcludeIntimidation" => "Does not include Intimidation",
        "LimitCondition_ExcludeIntimidationVisible" => "For all tests except Intimidation, Must be visible.",
        "LimitCondition_GangVisible" => "Gang members only, Must be visible.",
        "LimitCondition_GearAutopicker" => "Only for picking mechanical locks",
        "LimitCondition_InUse" => "Only when in use",
        "LimitCondition_Intimidation" => "Only for Intimidation",
        "LimitCondition_IntimidationVisible" => "Only for Intimidation, Must be visible.",
        "LimitCondition_KnockdownResistanceFootAnchor" => "Only for knockdown resist, Must be engaged.",
        "LimitCondition_Megacorp" => "Limited to Single Megacorp",
        "LimitCondition_NationalLanguageRanks" => "Must have 4+ Ranks in National Language",
        "LimitCondition_PublicVisible" => "General public only, Must be visible.",
        "LimitCondition_QualityChatty" => "Only when communicating via AR or VR.",
        "LimitCondition_QualityTrustworthy" => "Only for situations involving trust.",
        "LimitCondition_ShieldPhysicalPenalty" => "For actions where Shield might get in the way.",
        "LimitCondition_SkillGroupStealth" => "Only for Stealth group",
        "LimitCondition_SkillGroupStealthNaked" => "Only for Stealth group, Must be completely naked",
        "LimitCondition_SkillsActiveContortion" => "Only for Contortion",
        "LimitCondition_SkillsActiveDisguiseImpersonation" => "Only for Disguise and Impersonation",
        "LimitCondition_SkillsActiveEscapeArtist" => "Only for Escape Artist",
        "LimitCondition_SkillsActiveEscapeArtistGrappleLoose" => "Only for Escape Artist and Grappling, Must be loosened",
        "LimitCondition_SkillsActiveFirstAidMedicine" => "Only for First Aid and Medicine",
        "LimitCondition_SkillsActiveGymnasticsClimbing" => "Only for Gymnastics (Climbing)",
        "LimitCondition_SkillsActiveLeadership" => "Only for Leadership",
        "LimitCondition_SkillsActiveNavigation" => "Only for Navigation",
        "LimitCondition_SkillsActivePerception" => "Only for Perception",
        "LimitCondition_SkillsActivePerceptionHearing" => "Only for Perception (Hearing)",
        "LimitCondition_SkillsActivePerceptionSpatialRecognizer" => "Only for Perception to find the source of a sound",
        "LimitCondition_SkillsActivePerceptionVisual" => "Only for Perception (Visual)",
        "LimitCondition_SkillsActivePerformance" => "Only for Performance",
        "LimitCondition_SkillsActivePerformanceSinging" => "Only for Performance (Singing)",
        "LimitCondition_SkillsActivePerformanceSynthtrument" => "Only for Performance, Must use linked synthtrument",
        "LimitCondition_SkillsActiveSneaking" => "Only for Sneaking against Thermographic Vision or thermal sensors.",
        "LimitCondition_SkillsActiveSneakingNaked" => "Only for Sneaking, Must be completely naked",
        "LimitCondition_SkillsActiveSneakingVisible" => "Only for Sneaking, Must be visible",
        "LimitCondition_SkillsActiveSwimming" => "Only for Swimming",
        "LimitCondition_SkillsKnowledgeAcademic" => "Only for Academic Knowledge Skills",
        "LimitCondition_SkillsKnowledgeScientificTechnical" => "Only for Scientific or Technical Knowledge Skills",
        "LimitCondition_Skillwires" => "Only for Skillwires",
        "LimitCondition_SoldierParagon" => "Only for attack actions",
        "LimitCondition_SportsFans" => "Fans of Sports Team",
        "LimitCondition_SportsRivals" => "Rivals of Sports Team",
        "LimitCondition_Sprawl" => "Limited to Single Sprawl",
        "LimitCondition_TestAttribAGI" => "Only for Agility-linked Tests",
        "LimitCondition_TestAttribINT" => "Only with Intuition Tests",
        "LimitCondition_TestAttribLOG" => "Only with Logic Tests",
        "LimitCondition_TestSneakingThermal" => "Only for Sneaking against Thermographic Vision or thermal sensors.",
        "LimitCondition_TestSpeech" => "Only for test involving speaking",
        "LimitCondition_TestsEndurance" => "Only for tests involving endurance, Must be tightened",
        "LimitCondition_Visible" => "Must be visible",
        "" => "Always",
        _ when condition.StartsWith("LimitCondition_", StringComparison.Ordinal) || condition.StartsWith("/", StringComparison.Ordinal)
            || condition.StartsWith("not(", StringComparison.Ordinal) => "Condition unavailable; do not apply without resolving it.",
        _ => condition
    };

    private static object Number(int? value) => value is { } number ? number : Unavailable;

    private void EquipmentModifications()
    {
        Items("Weapon accessories", Section<CharacterWeaponAccessoriesSection>("weaponaccessories")?.Accessories,
            a => new Entry(Name(a.Name, a.CustomName), Fields(("Weapon", a.WeaponName), ("Mount", a.Mount),
                ("Extra mount", a.ExtraMount), ("Rating", a.Rating), ("Equipped", a.Equipped), ("Source", a.Source)), a.Notes));
        Items("Armor modifications", Section<CharacterArmorModsSection>("armormods")?.ArmorMods,
            a => new Entry(Name(a.Name, a.CustomName), Fields(("Armor", a.ArmorName), ("Category", a.Category),
                ("Rating", a.Rating), ("Equipped", a.Equipped), ("Source", a.Source)), a.Notes));
        Items("Vehicle modifications", Section<CharacterVehicleModsSection>("vehiclemods")?.VehicleMods,
            v => new Entry(Name(v.Name, v.CustomName), Fields(("Vehicle", v.VehicleName), ("Category", v.Category),
                ("Slots", v.Slots), ("Rating", v.Rating), ("Equipped", v.Equipped), ("Source", v.Source)), v.Notes));
    }

    private void Magic(CharacterGearSection? gear)
    {
        Start("Magic and resonance");
        CharacterAwakeningSection? awakening = Section<CharacterAwakeningSection>("awakening");
        if (awakening is null) Availability(Unavailable);
        else _html.Append(Fields(("Magic enabled", awakening.MagEnabled), ("Resonance enabled", awakening.ResEnabled),
            ("Depth enabled", awakening.DepEnabled), ("Adept", awakening.Adept), ("Magician", awakening.Magician),
            ("Technomancer", awakening.Technomancer), ("AI", awakening.AI), ("Initiate grade", awakening.InitiateGrade),
            ("Submersion grade", awakening.SubmersionGrade), ("Tradition", awakening.TraditionName),
            ("Tradition drain", awakening.TraditionDrain), ("Stream", awakening.Stream), ("Stream drain", awakening.StreamDrain),
            ("Combat spirit", awakening.SpiritCombat), ("Detection spirit", awakening.SpiritDetection),
            ("Health spirit", awakening.SpiritHealth), ("Illusion spirit", awakening.SpiritIllusion),
            ("Manipulation spirit", awakening.SpiritManipulation),
            ("Counterspelling dice", awakening.CurrentCounterspellingDice), ("Spell limit", awakening.SpellLimit),
            ("Complex form limit", awakening.CfpLimit), ("AI normal program limit", awakening.AiNormalProgramLimit),
            ("AI advanced program limit", awakening.AiAdvancedProgramLimit)));
        End();
        Items("Spells", Section<CharacterSpellsSection>("spells")?.Spells, s => new Entry(Name(s.Name, s.CustomName),
            Fields(("Category", s.Category), ("Type", s.Type), ("Range", s.Range), ("Duration", s.Duration),
                ("Drain", s.DrainValue), ("Source", s.Source)), s.Notes));
        Items("Adept powers", Section<CharacterPowersSection>("powers")?.Powers, p => new Entry(Name(p.Name, p.CustomName),
            Fields(("Rating", p.Rating), ("Points per level", p.PointsPerLevel), ("Source", p.Source)), p.Notes));
        Items("Complex forms", Section<CharacterComplexFormsSection>("complexforms")?.ComplexForms, f => new Entry(Name(f.Name, f.CustomName),
            Fields(("Target", f.Target), ("Duration", f.Duration), ("Fading", f.FadingValue), ("Source", f.Source)), f.Notes));
        // The shared codec's spirits section already contains both spirits and sprites.
        Items("Spirits and sprites", Section<CharacterSpiritsSection>("spirits")?.Spirits, s => new Entry(Name(s.Name, s.CustomName),
            Fields(("Type", s.EntityType), ("Force / level", s.Force), ("Services", s.Services),
                ("Bound", s.Bound), ("Critter name", s.CritterName)), s.Notes));
        Items("Foci", Section<CharacterFociSection>("foci")?.Foci, f =>
        {
            CharacterGearSummary[] matches = string.IsNullOrWhiteSpace(f.GearId) ? [] : gear?.Gear
                .Where(g => string.Equals(g.Guid, f.GearId, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray() ?? [];
            CharacterGearSummary? item = matches.Length == 1 ? matches[0] : null;
            return item is null
                ? new Entry("Focus", Fields(("Details", "Linked gear details unavailable in this projection.")))
                : new Entry(Name(item.Name, item.CustomName), Fields(("Rating", item.Rating), ("Category", item.Category)), item.Notes);
        });
        Items("Metamagics and echoes", Section<CharacterMetamagicsSection>("metamagics")?.Metamagics,
            m => new Entry(m.Name, Fields(("Grade", m.Grade), ("Paid with karma", m.PaidWithKarma), ("Source", m.Source))));
        Items("Magical arts", Section<CharacterArtsSection>("arts")?.Arts,
            a => new Entry(a.Name, Fields(("Grade", a.Grade), ("Source", a.Source))));
        Items("Initiation and submersion", Section<CharacterInitiationGradesSection>("initiationgrades")?.InitiationGrades,
            g => new Entry(g.Res ? "Submersion" : "Initiation", Fields(("Grade", g.Grade), ("Group", g.Group),
                ("Ordeal", g.Ordeal), ("Schooling", g.Schooling), ("Reward", g.Reward)), g.Notes));
        Items("Mentors and paragons", Section<CharacterMentorSpiritsSection>("mentorspirits")?.MentorSpirits,
            m => new Entry(m.Name, Fields(("Type", m.MentorType), ("Advantage", m.Advantage),
                ("Disadvantage", m.Disadvantage), ("Source", m.Source))));
        Items("Critter powers", Section<CharacterCritterPowersSection>("critterpowers")?.CritterPowers,
            p => new Entry(Name(p.Name, p.CustomName), Fields(("Category", p.Category), ("Rating", p.Rating),
                ("Type", p.Type), ("Action", p.Action), ("Range", p.Range), ("Duration", p.Duration), ("Source", p.Source)), p.Notes));
        Items("AI programs", Section<CharacterAiProgramsSection>("aiprograms")?.AiPrograms,
            p => new Entry(Name(p.Name, p.CustomName), Fields(("Rating", p.Rating), ("Source", p.Source)), p.Notes));
        Items("Martial arts", Section<CharacterMartialArtsSection>("martialarts")?.MartialArts,
            a => new Entry(a.Name, Fields(("Rating", a.Rating), ("Techniques", string.Join(", ", a.Techniques)), ("Source", a.Source))));
    }

    private T? Section<T>(string sectionId) where T : class
    {
        try { return _codec.ParseSection(sectionId, _envelope) as T; }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException
            or FormatException or ArgumentException or XmlException)
        {
            // Do not print exception text: it can contain source paths or raw payload data.
            return null;
        }
    }

    private void Start(string title) => _html.Append("<section><h2>").Append(Text(title)).Append("</h2>");
    private void End() => _html.Append("</section>");
    private void Availability(string message) => _html.Append("<p class=\"availability\">").Append(Text(message)).Append("</p>");
    private void Note(string label, string? value) => _html.Append("<h3>").Append(Text(label))
        .Append("</h3><p class=\"text\">").Append(Text(value)).Append("</p>");

    private void Table(string title, string[] headings, IEnumerable<object?[]>? rows)
    {
        Start(title);
        if (rows is null) Availability(Unavailable);
        else
        {
            object?[][] values = rows.ToArray();
            if (values.Length == 0) Availability("None recorded.");
            else
            {
                _html.Append("<table><thead><tr>");
                foreach (string heading in headings) _html.Append("<th scope=\"col\">").Append(Text(heading)).Append("</th>");
                _html.Append("</tr></thead><tbody>");
                foreach (object?[] row in values)
                {
                    _html.Append("<tr>");
                    foreach (object? cell in row) _html.Append("<td class=\"text\">").Append(Text(cell)).Append("</td>");
                    _html.Append("</tr>");
                }
                _html.Append("</tbody></table>");
            }
        }
        End();
    }

    private void Items<T>(string title, IReadOnlyList<T>? items, Func<T, Entry> project, IReadOnlyList<string>? names = null)
    {
        Start(title);
        if (items is null)
        {
            Availability(Unavailable);
            if (names?.Count > 0)
            {
                _html.Append("<p>Recorded inventory names:</p><ul>");
                foreach (string name in names) _html.Append("<li class=\"text\">").Append(Text(name)).Append("</li>");
                _html.Append("</ul>");
            }
        }
        else if (items.Count == 0) Availability("None recorded.");
        else
        {
            _html.Append("<table class=\"items\"><thead><tr><th scope=\"col\">Name</th>")
                .Append("<th scope=\"col\">Details</th><th scope=\"col\">Notes</th></tr></thead><tbody>");
            foreach (T item in items)
            {
                Entry entry = project(item);
                _html.Append("<tr><th scope=\"row\" class=\"text\">").Append(Text(entry.Name))
                    .Append("</th><td>").Append(entry.Details).Append("</td><td class=\"text\">")
                    .Append(Text(entry.Notes)).Append("</td></tr>");
            }
            _html.Append("</tbody></table>");
        }
        End();
    }

    private static string Fields(params (string Label, object? Value)[] fields)
    {
        StringBuilder html = new("<dl>");
        foreach ((string label, object? value) in fields)
            html.Append("<dt>").Append(Text(label)).Append("</dt><dd class=\"text\">").Append(Text(value)).Append("</dd>");
        return html.Append("</dl>").ToString();
    }

    private static string Name(string name, string customName, string fallback = "Unnamed item")
        => string.IsNullOrWhiteSpace(customName)
            ? string.IsNullOrWhiteSpace(name) ? fallback : name
            : string.IsNullOrWhiteSpace(name) ? customName : $"{name} ({customName})";

    private static string Text(object? value)
    {
        string? text = value switch
        {
            bool flag => flag ? "Yes" : "No",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString()
        };
        return WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(text) ? "Not recorded" : text);
    }

    private sealed record Entry(string Name, string Details, string? Notes = null);
}
