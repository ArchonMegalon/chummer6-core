using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Session;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Contracts.Characters.CharacterCreationRoleFindingState;

namespace Chummer.Application.Characters;

/// <summary>One ordinary loaded firearm shot without prior recoil or situational modifiers. Reuses the complete sheet owner proof
/// and local round-custody/fire rules; it never purchases, loads, fires or scores a build.</summary>
internal static class CharacterCreationPhysicalRoutineRules3819
{
    internal const string Semantics = "chummer.sr5-physical-single-shot.v3";

    internal static IReadOnlyList<CharacterCreationRoleFinding> Evaluate(
        CharacterCreationRoleRoutineObjective routine, string characterXml, ICharacterSourceDataContext context,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants = null)
    {
        try
        {
            var c = routine.Conditions;
            var action = routine.Action;
            Need(routine.Kind == CharacterCreationRoleRoutineKind.PhysicalAttack && action?.PhysicalAttack is not null,
                "Declare the exact owned weapon, loaded ammunition and source digests.", true);
            var declared = action!.PhysicalAttack!;
            Guid weaponSourceId = declared.WeaponSourceId, ammoSourceId = declared.AmmunitionSourceId;
            Need(action.AccessMode == "physical-single-shot" && action.Target == "" && action.ForceOrLevel is null
                && action.VehicleSourceId is null && routine.MagicIdentity is null,
                "Only an unopposed physical single-shot readiness scenario is supported; no target outcome is inferred.");
            Need(c.Consumables && !c.UseEdge && !c.ExternalAssistance && !c.SpriteAssistance
                && c.SustainedEffects == 0 && c.Preparation == "" && c.Situation == "ordinary",
                "Declare Consumables for the one loaded round only. Edge, assistance, preparation and other consumable effects are unsupported.");
            Need(declared.WeaponId != Guid.Empty && declared.AmmunitionId != Guid.Empty
                && weaponSourceId != Guid.Empty && ammoSourceId != Guid.Empty,
                "Declare the selected owned firearm and ammunition source identities.", true);
            Need(CharacterCreationFinalizationDigest.IsCanonical(declared.WeaponSourceDigest)
                && CharacterCreationFinalizationDigest.IsCanonical(declared.AmmunitionSourceDigest),
                "Exact canonical source digests are required.", true);
            Need(routine.RequiredEquipmentSourceIds.Count == 2
                && routine.RequiredEquipmentSourceIds.Any(id => SameId(id, weaponSourceId))
                && routine.RequiredEquipmentSourceIds.Any(id => SameId(id, ammoSourceId)),
                "Declare exactly the weapon and ammunition dependencies; other loadout actions need separate routines.", true);

            var root = Xml(characterXml);
            Need(root.Name == "character" && Text(root, "gameedition") == "SR5"
                && !True(root, "ignorerules") && !True(root, "ai") && !True(root, "depenabled"),
                "The ordinary SR5 character context is required.");
            // Weapon.GetDicePool also consumes contextual weapon effects; a general skill
            // pool cannot stand in for those. Preserve such owners and report unsupported.
            string? unsupportedEffect = CharacterPhysicalRoutineEffectBoundary3828.UnsupportedShape5701(root);
            Need(unsupportedEffect is null, unsupportedEffect ?? "Unsupported physical action effect.");
            Need(context is ICharacterSheetOwnerSourceContext, "Current owner-source authority is unavailable.");
            var sources = (ICharacterSheetOwnerSourceContext)context;
            Need(context.TryResolveCareerSettingsProfile(out var settings, out var settingsState)
               , "The exact saved source profile is unavailable.");
            var resolver = new ExactContextResolver(characterXml, context);
            var sheet = CharacterSheetDerivedQuery.ParsePhysicalAmmunition5701(characterXml, resolver,
                new List<SessionPlayDiagnostic>(), out var owners, out var fireStates, out var custody, priorityGrants);
            Need(sheet.UnavailableReason is null && owners is not null
                && CharacterCreationFinalizationDigest.IsCanonical(sheet.AuthorityDigest),
                sheet.UnavailableReason ?? "The complete source/owner graph is unavailable.");

            var weapon = Owned("weapons", "weapon", declared.WeaponId, weaponSourceId, "Weapon");
            var ammo = Owned("gears", "gear", declared.AmmunitionId, ammoSourceId, "Gear");
            // The sheet proves owners and skill effects, not every saved weapon field.
            // Admit only this slice's known inventory/configuration fields; e.g. a saved
            // dicepool/weaponbonus/range override cannot become an inert unknown.
            SourceShape(weapon, "weapon", "guid sourceid name category type conceal accuracy reach damage ap mode rc ammo avail cost source page accessorymounts spec spec2 useskill useskillspec equipped weapontype activeammoslot ammoslots clips allowsingleshot allowshortburst allowlongburst allowfullburst allowsuppressive singleshot shortburst longburst fullburst suppressive cyberware wirelesson parentid rating notes notesColor location stolen discountcost sortorder range alternaterange ammocategory requireammo", CanonicalWeapon3847(context, weaponSourceId));
            SourceShape(ammo, "gear", "guid sourceid name category rating source page avail cost costfor ammoforweapontype qty extra equipped wirelesson isflechetteammo notes notesColor location stolen discountcost sortorder", CanonicalAmmo3847(context, ammoSourceId));
            Need((Text(weapon, "rating") is "" or "0") && !True(ammo, "wirelesson") && !True(ammo, "isflechetteammo"),
                "Rated weapons or nonordinary ammunition configuration need a separate adapter.");
            var weaponEvidence = Source("Weapon", weaponSourceId, declared.WeaponSourceDigest);
            var ammoEvidence = Source("Gear", ammoSourceId, declared.AmmunitionSourceDigest);
            var weaponSource = Xml(weaponEvidence.SourceXml);
            var ammoSource = Xml(ammoEvidence.SourceXml);
            // Absence of weaponbonus does not establish ordinary ammunition:
            // Tracer, trackers and Hi-De also omit it in the current catalog.
            // Resolve the canonical regular-round semantic definition by name,
            // never a pinned GUID. Other ammunition needs an effect-coverage rule.
            Need(sources.TryResolveSheetNamedSource("Gear", "Ammo: Regular Ammo", out var regularRound)
                && regularRound is not null && regularRound == ammoEvidence,
                "Only the current source-defined regular round has complete ordinary-shot semantics here; special or unclassified ammunition effects remain unsupported.");
            Need(context.TryResolveCreationLoadoutAuthority(out var loadout)
                && CharacterCreationLoadoutRules.IsValidAuthority(loadout), "Current loadout source authority is unavailable.");
            var weapons = loadout.Options.Where(o => o.Kind == "weapon" && o.SourceId == weaponSourceId).ToArray();
            Need(weapons.Length == 1 && CharacterCreationLoadoutRules.IsValidOption(weapons[0])
                && XNode.DeepEquals(Xml(weapons[0].SourceNodeXml), weaponSource),
                "Weapon owner and loadout sources disagree.");
            Need(context.TryResolveCareerGearPurchaseSource(ammoSourceId, out var ammoPurchase) && ammoPurchase is not null
                && ammoPurchase.SourceId == ammoSourceId && XNode.DeepEquals(Xml(ammoPurchase.SourceXml), ammoSource),
                "Ammunition owner and cartridge sources disagree.");
            SourceShape(weaponSource, "weapon", "id name category type conceal accuracy reach damage ap mode rc ammo avail cost source page accessorymounts spec spec2 useskill useskillspec range alternaterange ammocategory weapontype requireammo ammoslots allowsingleshot allowshortburst allowlongburst allowfullburst allowsuppressive singleshot shortburst longburst fullburst suppressive");
            SourceShape(ammoSource, "gear", "id name category rating source page avail cost costfor ammoforweapontype");
            var categorySource = Xml(weapons[0].SourceCategoryXml);
            string category = Text(weaponSource, "category");
            string weaponType = Text(weaponSource, "weapontype");
            if (weaponType.Length == 0) weaponType = (string?)categorySource.Attribute("type") ?? category.ToLowerInvariant();
            Need(categorySource.Name == "category" && !categorySource.HasElements && categorySource.Value == category
                && weaponType == "gun" && Text(weaponSource, "type") == "Ranged"
                && Text(ammoSource, "category") == "Ammunition" && Text(ammoSource, "ammoforweapontype") == "gun"
                && Text(ammoSource, "rating") == "0",
                "Only source-described ordinary firearms and effect-free gun ammunition are supported.");
            // RC/reach do not modify this one shot with no prior recoil or melee use.
            // Admit their constant forms without pinning them to this fixture's zero.
            Nonnegative(Text(weaponSource, "rc"), "Recoil compensation");
            Nonnegative(Text(weaponSource, "reach"), "Reach");
            Need(!weapon.Descendants().Any(e => e.Name.LocalName is "accessory" or "weapon" or "gear")
                && !True(weapon, "cyberware") && !True(weapon, "wirelesson")
                && (Text(weapon, "parentid") is "" or "00000000-0000-0000-0000-000000000000"),
                "Attachments, wireless, implanted and parent-mounted weapons need their own action adapter.");
            foreach (string field in new[] { "accuracy", "damage", "ap", "rc", "reach", "useskill", "useskillspec", "spec", "spec2", "range", "alternaterange" })
                Need(Text(weapon, field) == Text(weaponSource, field), "Saved weapon combat fields differ from the selected source.");
            // No alternate damage/AP/ammunition formula: the admitted source is constant,
            // and regular ammunition has no weaponbonus. Native Weapon.GetTotalAccuracy /
            // CalculatedDamage return these base values when these modifiers are absent.
            int accuracy = Positive(Text(weaponSource, "accuracy"));
            string damageText = Text(weaponSource, "damage");
            Need(damageText.EndsWith('P'), "Only constant physical base damage is supported.");
            int damage = Positive(damageText[..^1]);
            int armorPenetration = ArmorPenetration(Text(weaponSource, "ap"));
            Need(category != "Special Weapons" && Text(weaponSource, "useskillspec").Length == 0,
                "Special/exotic weapon skill selection needs its own adapter.");
            string weaponSkill = Text(weaponSource, "useskill");
            if (weaponSkill.Length == 0)
                weaponSkill = CharacterWeaponSkillRules3828.DictionaryKeyForCategory(category, "");
            Need(context.TryResolveActiveSkillSource(routine.SkillSourceId, out var skillSource)
                && SameId(skillSource.SourceSkillId, Guid.Parse(routine.SkillSourceId))
                && skillSource.Name == weaponSkill && !skillSource.IsExotic,
                "The declared learned skill must match the current weapon's source-backed skill mapping.");
            var skillRows = root.Element("newskills")?.Element("skills")?.Elements("skill")
                .Where(e => SameId(Text(e, "suid"), Guid.Parse(routine.SkillSourceId))).ToArray() ?? [];
            Need(skillRows.Length == 1 && Guid.TryParse(Text(skillRows[0], "guid"), out _),
                "A unique instance of the source-selected weapon skill is required.", true);
            Guid skillId = Guid.Parse(Text(skillRows[0], "guid"));
            var skills = sheet.Skills.Where(s => SameId(s.Guid, skillId)).ToArray();
            Need(skills.Length == 1 && skills[0].Name == skillSource.Name && skills[0].Attribute == skillSource.DefaultAttribute
                && skills[0].UnavailableReason is null && skills[0].DicePool is not null && skills[0].EffectiveRating > 0,
                "The actual source-selected learned skill pool is required; defaulting is outside this slice.");
            var skill = skills[0];
            Need(skill.Specializations.Count == 0 && skillRows[0].Element("specs")?.HasElements != true,
                "Weapon specialization selection is outside this initial action slice.");
            Need(sheet.Condition is { WoundModifier: 0 } && !root.Elements().Any(e =>
                (e.Name.LocalName is "physicalcmfilled" or "stuncmfilled") && e.Value != "0"),
                "Wounded attacks need their contextual action adapter.");

            var selected = fireStates.Where(w => w.Identity.WeaponId == declared.WeaponId
                && w.Identity.AmmoGearId == declared.AmmunitionId && w.Identity.AmmoSlot == 1).ToArray();
            Need(custody is not null && selected.Length == 1,
                "The exact selected weapon/ammunition has no current compatible loaded-round authority.");
            Need(selected[0].Modes.Any(mode => mode.Mode == CharacterWeaponFireMode.SingleShot && mode.Rounds == 1),
                "The current source mode/consumption does not provide one ordinary single shot; burst/full-auto or special consumption needs its own action adapter.");
            Need(CharacterWeaponFireRules.TryCreatePlan(selected[0], selected[0].Revision,
                CharacterWeaponFireMode.SingleShot, out var fire) && fire.RoundsConsumed == 1
                && !fire.RequiresPartialConfirmation, "Load a supported owned round before this single-shot routine.", true);

            // Recheck the same profile, owner sources and skill after all dependent reads.
            // A caller can neither rehash stale source bytes nor substitute an owner ID.
            Need(context.TryResolveCareerSettingsProfile(out var finalSettings, out var finalState)
                && finalSettings == settings && finalState == settingsState
                && sources.TryResolveSheetOwnerSource("Weapon", weaponSourceId, out var currentWeapon) && currentWeapon == weaponEvidence
                && sources.TryResolveSheetOwnerSource("Gear", ammoSourceId, out var currentAmmo) && currentAmmo == ammoEvidence
                && sources.TryResolveSheetNamedSource("Gear", "Ammo: Regular Ammo", out var currentRegular) && currentRegular == regularRound
                && context.TryResolveActiveSkillSource(routine.SkillSourceId, out var currentSkill) && currentSkill == skillSource
                && context.TryResolveCreationLoadoutAuthority(out var currentLoadout) && currentLoadout.AuthorityDigest == loadout.AuthorityDigest
                && context.TryResolveCareerGearPurchaseSource(ammoSourceId, out var currentPurchase) && currentPurchase == ammoPurchase,
                "The selected source/profile changed during action evaluation.");
            CharacterCreationRoleContribution[] evidence =
            [
                new("Complete owned sheet", 0, sheet.AuthorityDigest!),
                new("Exact weapon owner/source: " + declared.WeaponId, 0, CharacterCreationFinalizationDigest.Compute(weaponEvidence)),
                new("Exact ammunition owner/source: " + declared.AmmunitionId, 0, CharacterCreationFinalizationDigest.Compute(ammoEvidence)),
                new("Loaded round custody", fire.RoundsConsumed, CharacterCreationFinalizationDigest.Compute(custody)),
                new("Ordinary single-shot semantics", 0, CharacterCreationFinalizationDigest.ComputeUtf8(Semantics))
            ];
            return
            [
                new("physical-attack-action", AssessedPass,
                    "One loaded ordinary single shot is available. No rounds were consumed; no opposed hit, net-hit damage or whole-role effectiveness is claimed.", 1, 1, evidence),
                new("physical-attack-pool", skill.DicePool >= routine.Targets.MinimumSkillDicePool ? AssessedPass : AssessedFailure,
                    $"Source-proven {skillSource.Name}/{skillSource.DefaultAttribute} pool including supported sheet effects; target is the player's readiness target.",
                    skill.DicePool, routine.Targets.MinimumSkillDicePool, evidence),
                new("physical-attack-accuracy", AssessedPass, "Current weapon Accuracy, the attack hit limit; not the character's Physical limit.", accuracy, null, evidence),
                new("physical-attack-base-damage", AssessedPass, "Current constant physical base DV before net hits, armor and resistance; not inflicted damage.", damage, null, evidence),
                new("physical-attack-ap", AssessedPass, "Current constant source AP with effect-free ammunition; not an armor or resistance outcome.", armorPenetration, null, evidence)
            ];

            XElement Owned(string container, string element, Guid id, Guid sourceId, string kind)
            {
                var rows = root.Element(container)?.Elements(element).Where(e => SameId(Text(e, "guid"), id)).ToArray() ?? [];
                Need(rows.Length == 1 && SameId(Text(rows[0], "sourceid"), sourceId), "The selected owned instance/source is missing or ambiguous.", true);
                var selectedOwners = owners.Where(o => o.Entity == id.ToString("D")
                    && o.Metric == CharacterRuleEffectMetric.Selected && o.Qualifier == kind
                    && o.State == CharacterRuleEffectValueState.Known && SameId(o.Text, sourceId)).ToArray();
                Need(selectedOwners.Length == 1 && owners.Any(o => o.Entity == id.ToString("D")
                    && o.Metric == CharacterRuleEffectMetric.Enabled && o.Qualifier == "equipped-owner"
                    && o.State == CharacterRuleEffectValueState.Known && o.Flag == true),
                    "The exact owned weapon/ammunition is inactive or lacks current owner provenance.", true);
                return rows[0];
            }
            CharacterSheetOwnerSource Source(string kind, Guid id, string digest)
            {
                Need(sources.TryResolveSheetOwnerSource(kind, id, out var source) && source is not null
                    && source.Kind == kind && source.SourceId == id
                    && CharacterCreationFinalizationDigest.ComputeUtf8(source.SourceXml) == digest
                    && new[] { source.CatalogDigest, source.ProfileDigest, source.CustomDataDigest, source.PolicyDigest }
                        .All(CharacterCreationFinalizationDigest.IsCanonical),
                    "The declared current source digest/profile is unavailable or changed.");
                return source;
            }
        }
        catch (Unavailable error)
        {
            return [new("physical-attack-action", error.Missing ? AssessedFailure : Unsupported, error.Message, null, null, [])];
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException)
        {
            return [new("physical-attack-action", Unsupported, "The exact physical action source/owner data cannot be consumed safely.", null, null, [])];
        }
    }

    // Source-backed Create/Save projectors emit inert inventory fields absent from
    // the minimal detached evaluator fixtures. Admit an additional field only when
    // its complete node equals the current canonical projector, never by name alone.
    private static XElement? CanonicalWeapon3847(ICharacterSourceDataContext context, Guid sourceId)
    {
        if (!context.TryResolveCreationLoadoutAuthority(out var authority)) return null;
        var option = authority.Options.SingleOrDefault(row => row.Kind == "weapon" && row.SourceId == sourceId);
        if (option is null || !CharacterCreationLoadoutRules.TryProjectBasket(new([new(option.OptionId)], []),
                authority, out var lines, out _, out _)
            || !CharacterCreationLoadoutSourceProjector.TryBuildGraph(lines,
                CharacterCreationFinalizationDigest.ComputeUtf8("physical-saved-shape3847"), out var weapons, out _)) return null;
        return weapons.Single();
    }
    private static XElement? CanonicalAmmo3847(ICharacterSourceDataContext context, Guid sourceId)
    {
        if (!context.TryResolveCreationGearAuthority(out var authority)) return null;
        var option = authority.Options.SingleOrDefault(row => row.SourceId == sourceId);
        if (option is null || !CharacterCreationGearRules.TryProjectBasket([new(option.OptionId, 1)], authority,
                option.PackageCost, out var lines, out _, out _)
            || !CharacterCreationLegacySourceProjector.TryBuildGear(lines.Single(),
                CharacterCreationGearRules.ComputeUtf8("physical-saved-shape3847"), out var gear)) return null;
        return gear;
    }

    private static void SourceShape(XElement source, string kind, string fields, XElement? canonicalSaved3847 = null)
    {
        var allowed = fields.Split(' ').ToHashSet(StringComparer.Ordinal);
        Need(source.Name == kind && !source.HasAttributes && source.Elements().All(e => (allowed.Contains(e.Name.LocalName)
            || canonicalSaved3847?.Element(e.Name) is { } canonical && XNode.DeepEquals(e, canonical))
            && e.Name.NamespaceName.Length == 0) && source.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1),
            "The selected source or owned row contains unsupported or ambiguous mechanics.");
    }
    private static string Text(XElement row, string field)
    {
        var nodes = row.Elements(field).ToArray();
        Need(nodes.Length <= 1 && (nodes.Length == 0 || !nodes[0].HasElements && !nodes[0].HasAttributes), "Ambiguous scalar action data.");
        return nodes.Length == 0 ? "" : nodes[0].Value;
    }
    private static bool True(XElement row, string field)
    {
        string value = Text(row, field);
        Need(value.Length == 0 || bool.TryParse(value, out _), "Invalid action flag.");
        return bool.TryParse(value, out bool result) && result;
    }
    private static int Positive(string value)
    {
        Need(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result)
            && result > 0 && result.ToString(CultureInfo.InvariantCulture) == value,
            "A constant positive combat value is required; expressions are unsupported.");
        return result;
    }
    private static int Nonnegative(string value, string mechanic)
    {
        Need(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result)
            && result >= 0 && result.ToString(CultureInfo.InvariantCulture) == value,
            mechanic + " must be a source constant; expressions are unsupported.");
        return result;
    }

    private static int ArmorPenetration(string value)
    {
        if (value == "-") return 0; // Native CalculatedAP's ordinary no-penetration token.
        Need(int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int result)
            && result.ToString(CultureInfo.InvariantCulture) == value.TrimStart('+'),
            "Armor penetration must be a source constant; expressions and special armor rules are unsupported.");
        return result;
    }

    private static bool SameId(string? text, Guid expected) => Guid.TryParse(text, out var id) && id == expected;
    private static XElement Xml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XElement.Load(reader);
    }
    private static void Need([DoesNotReturnIf(false)] bool condition, string detail, bool missing = false)
    { if (!condition) throw new Unavailable(detail, missing); }
    private sealed class Unavailable(string detail, bool missing) : Exception(detail)
    { internal bool Missing { get; } = missing; }
    private sealed class ExactContextResolver(string xml, ICharacterSourceDataContext context) : ICharacterSourceDataResolver
    { public ICharacterSourceDataContext? TryCreateContext(string characterXml) => characterXml == xml ? context : null; }
}
