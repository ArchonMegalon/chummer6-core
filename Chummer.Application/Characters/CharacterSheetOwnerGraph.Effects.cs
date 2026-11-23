using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    private CharacterCreationPriorityGrantAuthority3563? skillUnlockChoiceAuthority3778;

    // The caller supplies the same in-process proof used by CompilePrioritySkillGrants3563.
    // Saved SpecialSkills rows are outputs to reconcile, never a prompt authority.
    private void BindSkillUnlockChoices3778(CharacterCreationPriorityGrantAuthority3563? authority)
        => skillUnlockChoiceAuthority3778 = authority;

    private string CompileSkillUnlock3778(Owner owner, XElement node, string path)
    {
        string[] options = node.Value.Split(',', StringSplitOptions.TrimEntries);
        Need(!node.HasAttributes && !node.HasElements && options.All(option => option.Length > 0),
            "skill-unlock-shape", path);
        // Validate every option against the native compiler's filter vocabulary.
        // Supplying the first option here validates grammar only; it grants nothing.
        Need(CharacterCreationSkillsAccessRules.TryChooseUnlock(node, [options[0]], "", out var choice),
            "skill-unlock-filter-unconsumed", path);
        if (options.Length == 1) return choice;

        var authority = skillUnlockChoiceAuthority3778;
        Need(authority is not null, "skill-unlock-choice-authority-unavailable", path);
        var captured = authority!;
        Need(captured.TryResolveSelection3785(root, context, out _, out var selectedGroups, out var qualities, out var digest),
            "skill-unlock-choice-source-or-selection-mismatch", path);
        var matches = qualities.Where(quality => owner.Kind == "Quality"
            && quality.SourceId == owner.SourceId.ToString("D")
            && Text(owner.Saved, "qualitysource", "") == "Heritage"
            && XNode.DeepEquals(owner.Source, Parse(quality.CanonicalSourceXml))).ToArray();
        Need(matches.Length == 1, "skill-unlock-choice-owner-mismatch", path);
        Need(CharacterCreationSkillsAccessRules.TryChooseUnlock(node,
                selectedGroups,
                matches[0].ForcedSelection, out choice), "skill-unlock-choice-unresolved", path);
        sourceEvidence.Add(digest);
        checks.Add(() => captured.TryResolveSelection3785(root, context, out _, out _, out _, out var final) && final == digest);
        return choice;
    }

    // Source vocabulary, not item-name admission. Unknown fields cannot become inert.
    private static readonly HashSet<string> Metadata = new(("id name translate category source page altpage nameonpage " +
        "notes altnotes notesColor stolen cost avail rating minrating ratinglabel required forbidden hide priority " +
        "bonus wirelessbonus firstlevelbonus pairbonus wirelesspairbonus wirelesspairinclude pairinclude").Split(' '), StringComparer.Ordinal);
    private void ValidateSourceShape(Owner owner)
    {
        string kind = owner.Kind, path = owner.Path;
        XElement source = owner.Source;
        string fields = kind switch
        {
            "LifeModule" => "implemented",
            "Quality" => "karma limit chargenlimit careerlimit chargenonly careeronly contributetolimit contributetobp onlyprioritygiven chargenquality onlyprioritygiven metagenic mutuallyexclusive doublecareer removable print hidden implemented firstlevelbonus naturalweapons addqualities addweapon",
            "CritterPower" => "type action range duration toxic",
            "Power" => "points levels maxlevel maxlevels limit adeptway adeptwayrequires doublecost extrapointcost action enhancements",
            "Cyberware" or "Bioware" => "ess capacity bannedgrades forcegrade geneware isgeneware addtoparentess addtoparentcapacity limbslot limbslotcount modularmount plugsintomodularmount subsystems gears addweapon addvehicle weight allowedgears",
            "Gear" => "capacity armorcapacity weight device devicerating attack sleaze dataprocessing firewall attributearray canformpersona programlimit programs gears weaponbonus flechetteweaponbonus ammobonus addoncategory iscommlink ispersona ratinglabel minrating allowedgears matrixcm matrixcmbonus costfor requireparent",
            "Weapon" => "type conceal accuracy reach damage ap mode rc ammo accessories accessorymounts spec spec2 useskill useskillspec range alternaterange damagetype slots weight size allowaccessory mount extramount underbarrels hide ammoformat clips ammocategory weapontype sizecategory requireammo",
            "WeaponAccessory" => "mount extramount recoil conceal accuracy reach damage ap rc ammo weaponbonus weight specialmodification allowedweaponcategories allowedweapons",
            "Armor" => "armor armorcapacity armoroverride encumbrance weight gears mods addweapon armorname",
            "Spell" => "category type range damage duration dv descriptor useskill drain alchemical force free custom requiresquality",
            "ComplexForm" => "useskill target duration fv",
            "Vehicle" => "handling accel speed pilot body seats armor sensor addslots modslots gears mods powertrainmodslots protectionmodslots weaponmodslots bodymodslots electromagneticmodslots cosmeticmodslots",
            _ => throw Error("source-kind-unconsumed", path)
        };
        var allowed = fields.Split(' ').ToHashSet(StringComparer.Ordinal);
        if (kind == "Gear" && Node(source, "ammoforweapontype") is not null)
        {
            BindAmmunitionMetadata3483(owner);
            allowed.UnionWith(["costfor", "ammoforweapontype"]);
        }
        if (kind == "WeaponAccessory" && Node(source, "ammobonus") is not null)
        {
            BindAmmoAccessory3483(owner);
            allowed.Add("ammobonus");
        }
        Need(!source.HasAttributes && source.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1
                || kind == "Gear" && g.Key == "addoncategory" && g.All(e => !e.HasAttributes && !e.HasElements))
            && source.DescendantsAndSelf().All(e => e.Name.Namespace == XNamespace.None
                && !e.Nodes().OfType<XProcessingInstruction>().Any()), "source-shape", path);
        foreach (var node in source.Elements())
            Need(Metadata.Contains(node.Name.LocalName) || allowed.Contains(node.Name.LocalName),
                "source-field-unconsumed", path + "/" + node.Name.LocalName);
        // The casting authority resolves this explicit override against the skill
        // source. It is a bounded selector here, never an unlock or a default skill.
        if (kind == "Spell" && Node(source, "useskill") is { } spellSkill)
            Need(!spellSkill.HasAttributes && !spellSkill.HasElements && spellSkill.Value.Length is > 0 and <= 256
                && spellSkill.Value == spellSkill.Value.Trim() && !spellSkill.Value.Any(char.IsControl),
                "spell-skill-selector-shape", path + "/useskill");
        // SelectCritterPower uses this flag only for catalog filtering. Granted
        // powers retain their exact source identity; it grants no action authority.
        if (kind == "CritterPower" && Node(source, "toxic") is not null)
            _ = Flag(source, "toxic");
        foreach (string field in new[] { "naturalweapons", "addqualities", "enhancements", "weaponbonus", "flechetteweaponbonus", "ammobonus", "mods", "underbarrels" })
            Need(Node(source, field)?.HasElements != true, "source-generated-mechanic-unconsumed", path + "/" + field);
    }

    private void CompileContainer(Owner owner, XElement? container, bool enabled, string suffix)
    {
        if (container is null) return;
        Need(!container.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)), "bonus-mixed-content", owner.Path);
        Need(container.Attributes().All(a => a.Name == "unique" || a.Name == "useselected"),
            "bonus-container-attribute", owner.Path);
        if (container.Attribute("useselected") is { } selected)
            Need(bool.TryParse(selected.Value, out _), "bonus-use-selected-shape", owner.Path);
        string unique = container.Attribute("unique")?.Value ?? "";
        bool usedChoice = false;
        foreach (var node in container.Elements())
        {
            string path = owner.Path + "/" + container.Name.LocalName + "/" + node.Name.LocalName;
            bool precedenceSupported = node.Name.LocalName is "specificattribute" or "specificskill" or "skillattribute" or "initiativedice" or "initiativepass" or "armor" or "addlimb";
            Need(node.Name.Namespace == XNamespace.None && node.Attributes().All(a => a.Name == "precedence" && precedenceSupported
                || node.Name == "selectskill" && (a.Name == "minimumrating"
                    || owner.Kind == "MentorSpirit" && a.Name == "skillcategory") || node.Name == "armor" && a.Name == "group"
                || node.Name == "unlockskills" && a.Name == "name"
                || node.Name == "optionalpowers" && a.Name == "count" && owner.Kind == "Metatype" && Sprite5751 is not null),
                "effect-attribute-unconsumed", path);
            string u = unique;
            if (node.Attribute("precedence") is { } precedence)
            {
                Need(precedence.Value is "0" or "1" or "-1", "effect-precedence", path);
                u = "precedence" + precedence.Value;
            }
            if (node.Name == "armor" && node.Attribute("precedence") is null && node.Attribute("group") is { } group)
            {
                Need(int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _), "armor-group-shape", path);
                u = "group" + group.Value;
            }
            decimal Value(string name, decimal fallback = 0) => Node(node, name) is null ? fallback : Expression(Text(node, name), owner.Rating);
            void Shape(params string[] fields) => Need(node.Elements().All(e => fields.Contains(e.Name.LocalName)), "effect-shape-unconsumed", path);
            void Add(string type, string target = "", decimal value = 0, decimal aug = 0, decimal min = 0,
                decimal max = 0, decimal augmax = 0, bool addRating = false, string condition = "", int effectRating = 1,
                string? effectUnique = null, string exclude = "", bool testCondition = false)
            {
                Need(testCondition || condition is "" or "career" or "create", "effect-condition-unconsumed", path);
                expected.Add(Improvement(owner, suffix, enabled, type, target, value, aug, min, max, augmax,
                    addRating, condition, effectRating, effectUnique ?? u, exclude));
            }
            switch (node.Name.LocalName)
            {
                case "allowspritefettering":
                    // AddImprovementCollection.allowspritefettering: a source-owned flag.
                    Shape();
                    Need(string.IsNullOrWhiteSpace(node.Value), "sprite-fettering-flag-shape", path);
                    Add("AllowSpriteFettering");
                    break;
                case "addgear":
                    Shape("name", "category");
                    Need(owner.Kind == "Quality" && suffix.Length == 0 && u.Length == 0,
                        "gear-grant-context-unconsumed", path);
                    Add("Gear", BindGrantedGear3474(owner, node).ToString("D"));
                    break;
                case "specificattribute":
                case "selectattribute":
                {
                    bool select = node.Name == "selectattribute";
                    Shape(select ? "attribute" : "name", "min", "max", "val", "aug", "affectbase");
                    string target = select ? owner.Extra : Text(node, "name");
                    Need(new[] { "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG" }.Contains(target),
                        "attribute-target-unconsumed", path);
                    if (select)
                    {
                        Need(!usedChoice && node.Elements("attribute").Any(e => !e.HasElements && !e.HasAttributes && e.Value == target), "attribute-choice", path);
                        usedChoice = true;
                        boundChoices.Add(owner.Id);
                    }
                    Need(u.Length == 0 || Value("min") == 0 && Value("max") == 0 && Value("aug") == 0,
                        "attribute-range-precedence-unconsumed", path);
                    if (Node(node, "affectbase") is { } affectBase)
                    {
                        Need(!affectBase.HasAttributes && !affectBase.HasElements && string.IsNullOrWhiteSpace(affectBase.Value), "attribute-base-shape", path);
                        target += "Base";
                    }
                    Add("Attribute", target, aug: Value("val"), min: Value("min"), max: Value("max"), augmax: Value("aug"));
                    break;
                }
                case "specificskill":
                case "skillgroup":
                case "skillcategory":
                case "skillattribute":
                {
                    Shape("name", "bonus", "applytorating", "condition", "exclude");
                    Need(node.Name != "specificskill" || Node(node, "exclude") is null, "specific-skill-exclude-unconsumed", path);
                    Need(Node(node, "bonus") is not null, "skill-bonus-missing", path);
                    string type = node.Name.LocalName switch { "specificskill" => "Skill", "skillgroup" => "SkillGroup",
                        "skillcategory" => "SkillCategory", _ => "SkillAttribute" };
                    Need(Text(node, "condition", "").Length == 0 || u.Length == 0 && suffix.Length == 0,
                        "conditional-skill-selection-unconsumed", path);
                    if (owner.Kind == "Power" && node.Name == "specificskill")
                    {
                        string name = Text(node, "name");
                        CharacterSheetOwnerSource? receipt = null;
                        Need(sources is not null && sources.TryResolveSheetNamedSource("Skill", name, out receipt)
                            && receipt is not null && receipt.Name == name, "power-skill-source-unavailable", path);
                        var captured = receipt!;
                        checks.Add(() => sources!.TryResolveSheetNamedSource("Skill", name, out var final) && final == captured);
                        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(captured));
                    }
                    Add(type, Text(node, "name"), Value("bonus"), addRating: Flag(node, "applytorating", false),
                        condition: Text(node, "condition", ""), exclude: Text(node, "exclude", ""), testCondition: true);
                    break;
                }
                case "addqualities":
                    Need(suffix.Length == 0 && qualityGrants.TryGetValue(node, out _), "quality-grant-link-missing", path);
                    foreach (Guid child in qualityGrants[node]) Add("SpecificQuality", child.ToString("D"));
                    break;
                case "addlimb":
                    Shape("limbslot", "val");
                    Need(suffix.Length == 0 && Text(node, "limbslot") is "arm" or "leg" && Value("val") > 0,
                        "limb-modifier-shape", path);
                    Add("AddLimb", Text(node, "limbslot"), ExactInt(Value("val")));
                    break;
                case "movementreplace":
                case "sprintbonus":
                {
                    bool replace = node.Name == "movementreplace";
                    Shape(replace ? "speed" : "category", "category", "val");
                    string category = Text(node, "category");
                    Need(u.Length == 0 && suffix.Length == 0 && category is "Ground" or "Swim" or "Fly" && Node(node, "val") is not null,
                        "movement-modifier-shape", path);
                    string speed = replace ? Text(node, "speed") : "sprint";
                    Need(speed is "walk" or "run" or "sprint" && Value("val") >= 0, "movement-speed-shape", path);
                    if (replace || Value("val") != 0)
                        Add(replace ? speed switch { "walk" => "WalkSpeed", "run" => "RunSpeed", _ => "SprintSpeed" }
                            : "SprintBonus", category, Value("val"));
                    break;
                }
                case "blockskillcategorydefaulting":
                    Need(!node.HasElements && !string.IsNullOrWhiteSpace(node.Value) && node.Value == node.Value.Trim()
                        && u.Length == 0 && suffix.Length == 0, "defaulting-category-shape", path);
                    Add("BlockSkillCategoryDefault", node.Value);
                    break;
                case "skillcategorykarmacostmultiplier":
                case "skillcategoryspecializationkarmacostmultiplier":
                case "skillgroupcategorykarmacostmultiplier":
                    // The canonical Uneducated rows have no rating bounds or conditions.
                    // Do not erase more complex cost semantics from effective custom data.
                    Shape("name", "val");
                    Need(u.Length == 0 && suffix.Length == 0 && !string.IsNullOrWhiteSpace(Text(node, "name"))
                        && Text(node, "name") == Text(node, "name").Trim() && Node(node, "val") is not null && Value("val") > 0,
                        "skill-cost-modifier-shape", path);
                    Add(node.Name.LocalName switch {
                        "skillcategorykarmacostmultiplier" => "SkillCategoryKarmaCostMultiplier",
                        "skillcategoryspecializationkarmacostmultiplier" => "SkillCategorySpecializationKarmaCostMultiplier",
                        _ => "SkillGroupCategoryKarmaCostMultiplier" }, Text(node, "name"), Value("val"));
                    break;
                case "defensetest":
                    CompileSourceDefense8587(owner, container, node, enabled, suffix);
                    // This source-only contribution must never enter expected/Effects:
                    // legacy has neither a DEFENSETEST handler nor ImprovementType.
                    break;
                case "selectskill":
                {
                    Shape("val", "applytorating", "skillcategories");
                    Need(Node(node, "val") is not null, "skill-bonus-missing", path);
                    Need(!usedChoice && owner.Extra.Length > 0, "skill-choice", path);
                    var rows = Node(Node(root, "newskills") ?? new XElement("newskills"), "skills")?.Elements("skill") ?? [];
                    var matches = new List<(XElement Saved, CharacterActiveSkillSource Source)>();
                    foreach (var row in rows)
                    {
                        if (!context.TryResolveActiveSkillSource(Text(row, "suid"), out var skill) || skill.Name != owner.Extra) continue;
                        Need(!skill.IsExotic, "exotic-skill-choice-unconsumed", path);
                        matches.Add((row, skill));
                    }
                    Need(matches.Count == 1, "skill-choice-source-unavailable", path);
                    var match = matches[0]; var source = match.Source;
                    checks.Add(() => context.TryResolveActiveSkillSource(source.SourceSkillId, out var final) && final == source);
                    // Stock mentor prompts use the native single-category attribute.
                    // Keep this bounded to mentor owners and an exact source skill join.
                    if (node.Attribute("skillcategory") is { } category)
                        Need(category.Value.Length > 0 && category.Value == source.SkillCategory
                            && category.Value == Text(Parse(source.RawSourceXml), "category")
                            && Node(node, "skillcategories") is null,
                            "skill-choice-category", path);
                    var categories = Node(node, "skillcategories");
                    Need(categories is null || !categories.HasAttributes && categories.Elements().All(e => e.Name == "category"
                        && !e.HasElements && !e.HasAttributes) && categories.Elements().Any(e => e.Value == Text(Parse(source.RawSourceXml), "category")),
                        "skill-choice-category", path);
                    int minimum = ExactInt(Expression(node.Attribute("minimumrating")?.Value ?? "0", owner.Rating));
                    selectedSkillMinimums.Add((source.Name, minimum));
                    Add("Skill", source.Name, Value("val"), addRating: Flag(node, "applytorating", false));
                    usedChoice = true;
                    boundChoices.Add(owner.Id);
                    break;
                }
                case "spellcategory":
                case "spellcategorydicepool":
                {
                    // Gear categories need native power-focus compensation, outside this consumer.
                    Need(owner.Kind is "Quality" or "MentorSpirit" && suffix.Length == 0,
                        "spell-category-origin-unconsumed", path);
                    Shape("name", "val", "condition");
                    string category = Text(node, "name");
                    Need(category.Length is > 0 and <= 256 && category == category.Trim()
                        && !category.Any(char.IsControl) && Node(node, "val") is not null
                        && !node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)),
                        "spell-category-shape", path);
                    Need(Text(node, "condition", "").Length == 0, "spell-category-condition-unconsumed", path);
                    Add("SpellCategory", category, Value("val"));
                    break;
                }
                case "spelldicepool":
                    Shape("name", "val");
                    Need(Node(node, "val") is not null && !string.IsNullOrWhiteSpace(Text(node, "name"))
                        && !node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)),
                        "spell-dice-pool-shape", path);
                    Add("SpellDicePool", Text(node, "name"), Value("val")); break;
                case "limitmodifier":
                {
                    Shape("limit", "value", "condition");
                    string limit = Text(node, "limit"), condition = Text(node, "condition", "");
                    Need(limit is "Physical" or "Mental" or "Social", "limit-target-unconsumed", path);
                    decimal value = Value("value");
                    var links = (Node(root, "improvements")?.Elements() ?? []).Where(e =>
                        Text(e, "improvementttype", "") == "LimitModifier"
                        && Text(e, "sourcename", "") == owner.Id.ToString("D") + suffix
                        && Text(e, "condition", "") == condition && Dec(e, "val") == value).ToArray();
                    Need(links.Length == 1 && Guid.TryParse(Text(links[0], "improvedname"), out _), "limit-owner-link", path);
                    Guid id = Guid.Parse(Text(links[0], "improvedname"));
                    Need(limitIds.Add(id), "duplicate-limit-owner", path);
                    var limits = (Node(root, "limitmodifiers")?.Elements() ?? []).Where(e => Id(e, "guid") == id).ToArray();
                    Need(limits is [_], "limit-owned-row-missing", path);
                    var saved = limits[0];
                    int modifier = CharacterCreationEssenceRules1222.Round(value);
                    Need(saved.Name == "limitmodifier" && Text(saved, "limit") == limit && Int(saved, "bonus") == modifier
                        && Text(saved, "condition", "") == condition && !Flag(saved, "candelete", true), "limit-owned-row-mismatch", path);
                    expected.Add(Improvement(owner, suffix, enabled, "LimitModifier", id.ToString("D"), value,
                        0, 0, 0, 0, false, condition, 0, unique, ""));
                    ownedLimitPositions.Add((id, ConditionalLimits.Count));
                    ConditionalLimits.Add(new(limit, modifier, condition, enabled));
                    break;
                }
                case "conditionmonitor":
                {
                    Shape("physical", "stun", "threshold", "thresholdoffset", "sharedthresholdoffset", "overflow");
                    foreach (var child in node.Elements())
                    {
                        Need(child.Attributes().All(a => a.Name == "precedence") && !child.HasElements, "condition-monitor-shape", path);
                        string type = child.Name.LocalName switch { "physical" => "PhysicalCM", "stun" => "StunCM",
                            "threshold" => "CMThreshold", "thresholdoffset" => "CMThresholdOffset",
                            "sharedthresholdoffset" => "CMSharedThresholdOffset", _ => "CMOverflow" };
                        string cu = child.Attribute("precedence") is { } cp ? "precedence" + cp.Value : unique;
                        Need(child.Attribute("precedence") is null || child.Attribute("precedence")!.Value is "0" or "1" or "-1", "effect-precedence", path);
                        Add(type, value: Expression(child.Value, owner.Rating), effectUnique: cu);
                    }
                    break;
                }
                case "optionalpowers":
                    Need(owner.Kind == "Metatype" && Sprite5751 is not null && spriteOptionalContainers5751.Contains(node),
                        "optional-power-selection-unconsumed", path); break;
                case "freespiritpowerpoints":
                    Need(Sprite5751 is not null && !node.HasElements, "sprite-power-points-shape", path);
                    Add("FreeSpiritPowerPoints", value: Expression(node.Value, owner.Rating)); break;
                case "enableattribute":
                    Shape("name"); Need(Text(node, "name") is "MAG" or "RES", "special-attribute-unconsumed", path);
                    Add("Attribute", Text(node, "name"), effectRating: 0, effectUnique: "enableattribute"); break;
                case "enabletab":
                    Shape("name");
                    foreach (var name in node.Elements("name"))
                    {
                        Need(!name.HasElements && !name.HasAttributes && name.Value is "adept" or "magician" or "critter" or "technomancer", "talent-tab-unconsumed", path);
                        Add("SpecialTab", name.Value switch { "adept" => "Adept", "magician" => "Magician", "technomancer" => "Technomancer", _ => "Critter" }, effectRating: 0, effectUnique: "enabletab");
                    }
                    break;
                case "selecttext":
                    Need(!usedChoice && !node.HasElements && string.IsNullOrWhiteSpace(node.Value)
                        && !string.IsNullOrWhiteSpace(owner.Extra), "text-choice-unresolved", path);
                    // ImprovementManager creates Text before dispatching the empty
                    // selecttext method. No attack or numeric expression is implied.
                    Add("Text", owner.Extra);
                    usedChoice = true;
                    boundChoices.Add(owner.Id);
                    break;
                case "unlockskills" when node.Attribute("name") is { } named:
                {
                    Need(!node.HasElements && node.Value == "Name" && named.Value.Length > 0,
                        "named-skill-unlock-shape", path);
                    CharacterSheetOwnerSource? receipt = null;
                    Need(sources is not null && sources.TryResolveSheetNamedSource("Skill", named.Value, out receipt)
                        && receipt is not null, "named-skill-unlock-source-unavailable", path);
                    var captured = receipt!;
                    Need(context.TryResolveActiveSkillSource(captured.SourceId.ToString("D"), out var skill)
                        && skill.Name == named.Value && !skill.IsExotic
                        && XNode.DeepEquals(Parse(skill.RawSourceXml), Parse(captured.SourceXml)),
                        "named-skill-unlock-source-mismatch", path);
                    checks.Add(() => sources!.TryResolveSheetNamedSource("Skill", named.Value, out var final) && final == captured);
                    checks.Add(() => context.TryResolveActiveSkillSource(skill.SourceSkillId, out var final) && final == skill);
                    sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(captured));
                    Add("SpecialSkills", "Name");
                    expected[^1].SetElementValue("target", named.Value);
                    if (enabled)
                    {
                        NamedSkillSources[skill.SourceSkillId] = skill;
                        UnlockedSkills.Add(new(owner.Id, captured.SourceId, skill.Name));
                    }
                    break;
                }
                case "unlockskills":
                    Add("SpecialSkills", CompileSkillUnlock3778(owner, node, path));
                    break;
                case "disablequality":
                case "blockspelldescriptor":
                case "limitspellcategory":
                    Need(!node.HasElements && node.Value.Length > 0 && !node.Value.Contains(','), "effect-choice-unconsumed", path);
                    Add(node.Name.LocalName switch { "disablequality" => "DisableQuality",
                        "blockspelldescriptor" => "BlockSpellDescriptor", _ => "LimitSpellCategory" }, node.Value); break;
                // Erased is a native presence flag consumed by the Reputation
                // projector. Preserve and reconcile its exact owned row here too.
                case "erased": case "ambidextrous": case "ignorecmpenaltystun": case "ignorecmpenaltyphysical":
                case "addesstostuncmrecovery": case "addesstophysicalcmrecovery":
                    Need(!node.HasElements && string.IsNullOrWhiteSpace(node.Value), "flag-effect-shape", path);
                    Add(node.Name.LocalName switch { "erased" => "Erased", "ambidextrous" => "Ambidextrous", "ignorecmpenaltystun" => "IgnoreCMPenaltyStun",
                        "ignorecmpenaltyphysical" => "IgnoreCMPenaltyPhysical", "addesstostuncmrecovery" => "AddESStoStunCMRecovery",
                        _ => "AddESStoPhysicalCMRecovery" }); break;
                default:
                {
                    string? type = node.Name.LocalName switch {
                        "matrixinitiative" => "MatrixInitiative",
                        "matrixinitiativepass" or "matrixinitiativedice" or "matrixinitiativepassadd" or "matrixinitiativediceadd" => "MatrixInitiativeDice",
                        "initiative" => "Initiative", "initiativepass" or "initiativedice" => "InitiativeDice",
                        "initiativepassadd" or "initiativediceadd" => "InitiativeDiceAdd", "dodge" => "Dodge",
                        "physicallimit" => "PhysicalLimit", "mentallimit" => "MentalLimit", "sociallimit" => "SocialLimit",
                        "physicalcmrecovery" => "PhysicalCMRecovery", "stuncmrecovery" => "StunCMRecovery",
                        "damageresistance" => "DamageResistance", "armor" => "Armor", "composure" => "Composure",
                        "notoriety" => "Notoriety", "memory" => "Memory", "judgeintentionsdefense" => "JudgeIntentionsDefense",
                        "reach" => "Reach", "lifestylecost" => "LifestyleCost", "spellresistance" => "SpellResistance",
                        "pathogencontactresist" => "PathogenContactResist", "pathogeningestionresist" => "PathogenIngestionResist",
                        "pathogeninhalationresist" => "PathogenInhalationResist", "pathogeninjectionresist" => "PathogenInjectionResist",
                        "toxincontactresist" => "ToxinContactResist", "toxiningestionresist" => "ToxinIngestionResist",
                        "toxininhalationresist" => "ToxinInhalationResist", "toxininjectionresist" => "ToxinInjectionResist", _ => null };
                    if (type is null && CharacterCreationLegacySourceProjector.TryCompileCurrentSkillEffects(
                            new XElement("quality", new XElement("bonus", new XElement(node))), false, out var costs)
                        && costs is [var cost] && CharacterCreationSkillKarmaModifierRules.IsSupportedType(cost.Type))
                    {
                        // Share the existing source grammar. This bridge covers ordinary literal
                        // costs only; bounds, conditions and precedence need their own owner contract.
                        Need(u.Length == 0 && suffix.Length == 0 && cost.Minimum == 0 && cost.Maximum == 0
                            && cost.Condition.Length == 0 && string.IsNullOrEmpty(cost.Unique),
                            "skill-cost-modifier-shape", path);
                        Add(cost.Type, cost.Target, cost.Value);
                        break;
                    }
                    Need(type is not null && !node.HasElements, "effect-unconsumed", path);
                    string target = type switch { "PhysicalLimit" => "Physical", "MentalLimit" => "Mental", "SocialLimit" => "Social", _ => "" };
                    Add(type!, target, Expression(node.Value, owner.Rating), effectUnique:
                        type == "MatrixInitiativeDice" ? node.Name.LocalName is "matrixinitiativepass" or "matrixinitiativedice" ? "matrixinitiativepass" : unique
                        : type == "MatrixInitiative" ? unique
                        : type == "InitiativeDice" ? node.Attribute("precedence") is null ? node.Name.LocalName : u
                        : type is "Initiative" or "InitiativeDiceAdd" ? unique : u);
                    break;
                }
            }
        }
    }

    private static XElement Improvement(Owner o, string suffix, bool enabled, string type, string target,
        decimal value, decimal aug, decimal min, decimal max, decimal augmax, bool addRating, string condition,
        int rating, string unique, string exclude) => new("improvement",
        new XElement("target", ""), new XElement("improvedname", target), new XElement("sourcename", o.Id.ToString("D") + suffix),
        new XElement("min", min), new XElement("max", max), new XElement("aug", aug), new XElement("augmax", augmax),
        new XElement("val", value), new XElement("rating", rating), new XElement("exclude", exclude), new XElement("condition", condition),
        new XElement("improvementttype", type), new XElement("improvementsource", o.Kind), new XElement("custom", false),
        new XElement("customname", ""), new XElement("customid", ""), new XElement("customgroup", ""),
        new XElement("addtorating", addRating ? 1 : 0), new XElement("enabled", enabled ? 1 : 0), new XElement("order", 0),
        new XElement("unique", unique));

    private static string Key(XElement row)
    {
        string[] fields = ["target", "improvedname", "sourcename", "min", "max", "aug", "augmax", "val", "rating",
            "exclude", "condition", "improvementttype", "improvementsource", "custom", "customname", "customid", "customgroup",
            "addtorating", "enabled", "unique"];
        Need(!row.HasAttributes && row.Elements().All(e => fields.Contains(e.Name.LocalName)
            || e.Name == "notes" || e.Name == "notesColor" || e.Name == "order"), "unknown-improvement-field", "improvements");
        return string.Join('\0', fields.Select(f => f switch {
            "min" or "max" or "aug" or "augmax" or "val" or "rating" => Dec(row, f).ToString(CultureInfo.InvariantCulture),
            "enabled" or "custom" or "addtorating" => Flag(row, f) ? "1" : "0",
            "unique" => Text(row, f, ""), _ => Text(row, f) }));
    }
}
