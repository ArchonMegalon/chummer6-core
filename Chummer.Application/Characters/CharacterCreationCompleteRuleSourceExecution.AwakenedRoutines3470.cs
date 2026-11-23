using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Contracts.Characters.CharacterCreationRoleFindingState;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private const string AwakenedRoutineEvaluator3470 = "chummer.sr5-awakened-routines.v1";

    // Private planner dispatch only: never accept a transported sheet as authority.
    // Reuses the admitted brief. Target encodes declared opposition, not a second
    // character: living:WIL=N / living:BOD=N, AccessMode=physical-los. No counterspelling,
    // cover, technological vision, astral targets, reckless casting or multiple casts.
    private IEnumerable<CharacterCreationRoleFinding> AssessOrdinaryCasting3470(
        CharacterCreationRoleRoutineObjective routine, CharacterCreationCompletionBaseline baseline,
        XElement root, CharacterSheetDerivedSection sheet, CharacterCreationRoleFinding resistance)
    {
        var selected = baseline.Plan.Selections.MagicSelections;
        var options = Inputs.Magic.Authority.Spells.Where(o => o.Identity == routine.MagicIdentity).ToArray();
        if (routine.MagicIdentity is null || !selected.Spells.Contains(routine.MagicIdentity))
        {
            yield return Failure("casting-action", "Learn the exact intended spell within the shared budget before using this routine.");
            yield break;
        }
        if (options.Length != 1 || !CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(options[0], 1, out _))
        {
            yield return Unknown("casting-action", "The exact enabled selected spell source is unavailable or no longer canonical.");
            yield break;
        }
        var option = options[0];
        var source = RoleXml(option.CanonicalSourceXml);
        var savedSpells = root.Element("spells")?.Elements("spell")
            .Where(e => (string?)e.Element("sourceid") == option.Identity.SourceId).ToArray() ?? [];
        if (savedSpells.Length != 1 || new[] { "limited", "extended", "customextended", "alchemical", "barehandedadept" }
            .Any(flag => (string?)savedSpells[0].Element(flag) is not (null or "False" or "false"))
            || (string?)savedSpells[0].Element("extra") is not (null or ""))
        {
            yield return Unknown("casting-action", "A unique ordinary learned spell is required; limited/alchemical/extended/choice variants need their own action consumer.");
            yield break;
        }
        using var sourceScope5311 = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var castingContext5311 = (sourceScope5311 ?? resolution.Resolver).TryCreateContext(baseline.Finalization.CharacterXml);
        if (castingContext5311 is null || !CharacterDirectSpellRules5296.TryResolveSkill(source, castingContext5311,
            out var effectiveSkill5311, out var skillEvidence5311))
        {
            yield return Unknown("casting-action", "The spell needs an explicit supported useskill override or one matching category with useskill=Spellcasting. Restore the source metadata; other skills need a Core consumer.");
            yield break;
        }
        var action = routine.Action;
        var skill = SelectedRoutineSkill3470(root, sheet, routine.SkillSourceId);
        var skillSources = Inputs.Skills.Authority.ActiveSkills.Where(s => s.SourceSkillId == routine.SkillSourceId).ToArray();
        var magic = sheet.Attributes.Where(a => a.Name == "MAG").ToArray();
        if (sheet.UnavailableReason is not null || skill is null || magic.Length != 1
            || skillSources.Length != 1 || skillSources[0].Name != effectiveSkill5311 || skill.Attribute != "MAG"
            || skill.EffectiveRating is null || skill.DicePool is null || skill.UnavailableReason is not null)
        {
            yield return Unknown("casting-action", sheet.UnavailableReason ?? "A unique source-resolved Spellcasting/MAG projection is required; another declared skill cannot cast this spell.");
            yield break;
        }
        if (skill.EffectiveRating <= 0 || skill.DicePool <= 0 || magic[0].Total <= 0
            || (string?)root.Element("magician") != "True")
        {
            yield return Failure("casting-action", "This routine needs an enabled casting talent, positive Magic and learned Spellcasting; Spellcasting cannot default.");
            yield break;
        }
        if (!CharacterDirectSpellRules5296.TryReadSource(source, out int drainAdjustment, out string defenseAttribute)
            || action is null || action.AccessMode != "physical-los" || action.VehicleSourceId is not null
            || !TryOpposition3470(action.Target, defenseAttribute, out int opposition)
            || action.ForceOrLevel is not int force)
        {
            yield return Unknown("casting-action", "Supported slice: ordinary single-target, instantaneous Direct Combat spell, physical line of sight to a living target. Declare Force, AccessMode physical-los and Target living:WIL=N (mana) or living:BOD=N (physical), with the target attribute pool 1..100. Other targeting/effects remain unassessed.");
            if (routine.Targets.MaximumDamageProbability is { } risk)
                yield return Unknown("damage-risk", "The spell/scenario is outside this drain/action slice.", risk);
            yield break;
        }
        if (force < 1 || force > 2 * magic[0].Total)
        {
            yield return Failure("casting-action", "Force must be at least one and no more than twice current Core Magic. Keep the requested Force; offer an explicitly permitted allocation or a separate lower-Force objective.");
            yield break;
        }
        // Spell.DicePool/CalculatedDv have action-specific improvements which the
        // sheet pool does not apply. Refuse instead of silently dropping these.
        if (root.Element("improvements")?.Elements("improvement").Any(e =>
            ((string?)e.Element("improvementttype")) is "SpellCategory" or "SpellDicePool"
                or "DrainValue" or "SpellCategoryDrain" or "SpellDescriptorDrain"
                or "SpellCategoryDamage" or "SpellDescriptorDamage" or "SpellDamage") == true)
        {
            yield return Unknown("casting-action", "Spell-specific pool/drain/damage improvements require their contextual consumer; a base skill pool cannot substitute.");
            yield break;
        }
        if (resistance.Actual is not decimal drainPoolValue || drainPoolValue < 0 || drainPoolValue > 200
            || drainPoolValue != decimal.Truncate(drainPoolValue))
        {
            yield return Unknown("casting-action", "The selected tradition's current drain projection is unavailable or outside the bounded distribution.");
            yield break;
        }
        var specs = skill.Specializations.Where(s => s.Name == option.Category).ToArray();
        if (specs.Length > 1)
        {
            yield return Unknown("casting-action", "Ambiguous applicable casting specialization.");
            yield break;
        }
        int pool = specs.Length == 1 ? specs[0].DicePool : skill.DicePool.Value;
        if (pool is < 1 or > 200 || force > 200)
        {
            yield return Unknown("casting-action", "The casting pool or Force exceeds this bounded routine evaluator (200).");
            yield break;
        }
        var contributions = new List<CharacterCreationRoleContribution>
        {
            new("Core current MAG", magic[0].Total, sheet.AuthorityDigest!),
            new("Core effective Spellcasting rating", skill.EffectiveRating.Value, sheet.AuthorityDigest!),
            new("Remaining Core skill effects/wounds", skill.DicePool.Value - magic[0].Total - skill.EffectiveRating.Value, sheet.AuthorityDigest!)
        };
        if (specs.Length == 1)
            contributions.Add(new("Selected spell category specialization: " + option.Category, pool - skill.DicePool.Value, sheet.AuthorityDigest!));
        yield return Minimum("skill-pool", pool, routine.Targets.MinimumSkillDicePool, null, contributions);
        int drain = CharacterDirectSpellRules5296.DrainValue(force, drainAdjustment); // Spell.GetDvTooltip: floor after the selected formula.
        var outcome = DirectSpellDistribution3470(pool, force, magic[0].Total, opposition, (int)drainPoolValue, drain);
        string spellAuthority = CharacterCreationFinalizationDigest.Compute(new { option.CanonicalSourceXmlDigest, skillEvidence5311 });
        yield return Observed3470("casting-force-limit", force, "Force is the casting hit limit; no Edge/reagents substitute for it.", spellAuthority);
        yield return Observed3470("casting-actions", 1, "One ordinary Complex Action; instantaneous effect, no sustaining cost. This is one cast, not a repeat-casting simulation.", spellAuthority);
        yield return Observed3470("spell-opposition-pool", opposition, "Declared living target " + defenseAttribute + "; no counterspelling or other target assistance is included.", CharacterCreationFinalizationDigest.Compute(routine.Action!));
        yield return Observed3470("spell-net-hit-probability", outcome.Success, "Chance of at least one net hit against the declared opposition. Direct-spell damage is net hits, without a second armor soak; discretionary glitch consequences are separate.", spellAuthority);
        yield return Observed3470("spell-expected-damage:" + (string?)source.Element("damage"), outcome.ExpectedNetHits, "Expected direct-spell damage boxes to this target, including misses; no Force bonus is added to net hits.", spellAuthority);
        yield return Observed3470("drain-value", drain, "Selected spell formula " + (string?)source.Element("dv") + ", with SR5 minimum 2. Resistance is the selected tradition's separate test.", spellAuthority);
        yield return Observed3470("drain-expected-boxes", outcome.ExpectedDrain, "Expected baseline unresisted drain boxes for this single cast, including rolls that resist all drain.", spellAuthority);
        yield return Observed3470("drain-physical-damage-probability", outcome.PhysicalDamage, "Joint probability of unresisted drain and casting hits AFTER the Force limit exceeding MAG; Force above MAG alone does not make all drain Physical.", spellAuthority);
        yield return Observed3470("drain-stun-damage-probability", outcome.StunDamage, "Joint probability of unresisted baseline drain with capped casting hits no greater than MAG.", spellAuthority);
        yield return Observed3470("drain-expected-physical-boxes", outcome.ExpectedPhysicalDrain, "Expected Physical portion of baseline drain; no Stun-to-Physical overflow modeled.", spellAuthority);
        yield return Observed3470("drain-expected-stun-boxes", outcome.ExpectedStunDrain, "Expected Stun portion of baseline drain; current monitor overflow/recovery is separate.", spellAuthority);
        if (routine.Targets.MaximumDamageProbability is decimal maximum)
        {
            bool exceeds = outcome.AnyDamage > maximum;
            string tradeoff = "Retain player locks; offer a permitted increase to the selected tradition's drain attributes, or explicitly trade Force/frequency for lower risk in a separate objective. No universal WIL floor is imposed.";
            yield return new("drain-baseline-risk", exceeds ? AssessedFailure : AssessedPass,
                "Single-cast baseline drain probability only, excluding GM-decided glitch consequences and monitor overflow. "
                + (exceeds ? "Already exceeds the retained maximum. " + tradeoff : "Meets the numeric bound for this component; this does not pass the full requested damage-risk target."),
                outcome.AnyDamage, maximum, [new("Selected tradition's Core resistance", drainPoolValue, resistance.Contributions.Single().Authority)]);
            yield return exceeds
                ? new("damage-risk", AssessedFailure, "Baseline drain alone already exceeds the unchanged requested damage-risk maximum. " + tradeoff,
                    outcome.AnyDamage, maximum, [])
                : Unknown("damage-risk", "Baseline drain is within the bound, but total damage risk is unassessed because glitch consequences/overflow are not specified. The requested target is not silently narrowed to baseline drain.", maximum);
        }
        yield return Unknown("casting-glitch-and-sequence", "Discretionary glitch consequences, repeated-cast wounds, recovery and monitor overflow are not included in these baseline probabilities.");
        yield return new("casting-action", AssessedPass, "The selected spell is castable for this declared ordinary action; source, effective skill, Force, opposition and baseline drain are evaluated. Read strength/risk failures separately.", 1, 1, [new(option.Name, 1, spellAuthority)]);
    }

    private IEnumerable<CharacterCreationRoleFinding> AssessOrdinaryAdept3470(
        CharacterCreationRoleRoutineObjective routine, CharacterCreationCompletionBaseline baseline,
        XElement root, CharacterSheetDerivedSection sheet)
    {
        var action = routine.Action;
        if (action is null || action.Target is not ("physical" or "social" or "entry")
            || action.AccessMode is not ("skill" or "defense" or "armor")
            || action.ForceOrLevel is not null || action.VehicleSourceId is not null)
        {
            yield return Unknown("adept-power-synergy", "Declare Target physical/social/entry and AccessMode skill/defense/armor. This slice attributes an ordinary skill pool, defense pool or armor contribution, not an entire encounter.");
            yield break;
        }
        var allocations = baseline.Plan.Selections.MagicSelections.AdeptPowers.Where(p => p.Identity == routine.MagicIdentity).ToArray();
        if (allocations.Length != 1 || allocations[0].Levels <= 0)
        {
            yield return Failure("adept-power-synergy", "Select and pay for the intended power at a usable rating; purchased empty Power Points or unrelated powers cannot fulfill this routine.");
            yield break;
        }
        var options = Inputs.Magic.Authority.AdeptPowers.Where(o => o.Identity == allocations[0].Identity).ToArray();
        var projection = baseline.Plan.Review.UpstreamBudget?.Magic;
        if (sheet.UnavailableReason is not null || projection is null || projection.Blockers.Count != 0
            || options.Length != 1 || !CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(options[0], allocations[0].Levels, out _))
        {
            yield return Unknown("adept-power-synergy", sheet.UnavailableReason ?? "The selected power, finalized budget or source/effect projection is unavailable.");
            yield break;
        }
        var option = options[0];
        var source = RoleXml(option.CanonicalSourceXml);
        if (!CharacterCreationAdeptPowerSourceRules.TryCompileUndiscountedEffects(source, allocations[0].Levels,
                out var effects, option.AdeptSkillSourceXml)
            || (string?)source.Element("action") is not (null or "" or "Auto"))
        {
            yield return Unknown("adept-power-synergy", "The complete selected power payload or its activation is unsupported; no partial effects are credited.");
            yield break;
        }
        var skill = SelectedRoutineSkill3470(root, sheet, routine.SkillSourceId);
        var skills = Inputs.Skills.Authority.ActiveSkills.Where(s => s.SourceSkillId == routine.SkillSourceId).ToArray();
        if (skills.Length != 1 || !(action.Target switch
        {
            "physical" => skills[0].Category is "Physical Active" or "Combat Active",
            "social" => skills[0].Category == "Social Active" || skills[0].Name == "Perception",
            "entry" => skills[0].Category is "Physical Active" or "Technical Active",
            _ => false
        }))
        {
            yield return Unknown("adept-power-synergy", "This declared skill is outside the bounded physical/social/entry action families; a power does not establish a different action.");
            yield break;
        }
        if (skill is null || skill.DicePool is null || skill.UnavailableReason is not null || skill.IsNativeLanguage)
        {
            yield return Unknown("adept-power-synergy", "The declared routine lacks a usable source-resolved skill pool; a power cannot grant an unlearned restricted magical skill.");
            yield break;
        }
        if (skill.DicePool <= 0)
        {
            yield return Failure("adept-power-synergy", "The declared action has no dice; a power contribution alone cannot make this routine usable.");
            yield break;
        }
        var budget = projection.AdeptPowerPointBudget;
        if (budget.Blockers.Count != 0 || budget.Remaining < 0 || budget.Used > budget.Total)
        {
            yield return Failure("adept-power-affordability", "The selected powers exceed the independently reviewed shared PP budget.");
            yield break;
        }
        string authority = option.CanonicalSourceXmlDigest;
        yield return new("adept-power-affordability", AssessedPass, "All selected powers share this Core-reviewed PP wallet. Purchase Karma is already charged once by the baseline.", budget.Used, budget.Total,
            [new("Total PP", budget.Total, projection.ProposalDigest), new("Used PP", budget.Used, projection.ProposalDigest),
             new("Unallocated PP", budget.Remaining, projection.ProposalDigest),
             new("This selected power's PP cost", option.PointCost * allocations[0].Levels, authority),
             new("Purchased PP Karma", projection.MysticAdeptPowerPoints?.KarmaCost ?? 0, projection.ProposalDigest)]);
        var relevant = effects.Where(e => action.AccessMode switch
        {
            "skill" => e.Type == "Skill" && e.Target == skill.Name,
            "defense" => e.Type == "Dodge" && routine.Targets.MinimumDefensePool is not null && sheet.DefensePool is > 0,
            "armor" => e.Type == "Armor" && sheet.OtherPoolsAndModifiers.ContainsKey("ArmorModifier"),
            _ => false
        }).ToArray();
        if (relevant.Length == 0)
        {
            yield return Failure("adept-power-synergy", "This selected power contributes no supported effect to the declared use. Choose a relevant power/skill combination within permissions, or explicitly change the routine; power count is not usefulness.");
            yield break;
        }
        int benefit = relevant.Sum(e => e.Value);
        yield return new("adept-power-synergy", AssessedPass,
            action.AccessMode == "skill" ? "The selected source adds dice to this exact skill; the Core pool already includes them. No ranks are granted."
                : action.AccessMode == "defense" ? "The selected source adds to ordinary Core defense for this routine; it does not increase the declared attack/social/entry skill pool or prove surprise handling."
                : "The selected source contributes armor for this routine; this is not an attack bonus, full armor/soak total, or damage-reduction probability.",
            benefit, null, relevant.Select(e => new CharacterCreationRoleContribution(option.Name + " / " + e.Type + " " + e.Target, e.Value, authority)).ToArray());
        if (action.AccessMode == "armor")
            yield return Unknown("armor-damage-outcome", "Selected armor contribution is known; incoming AP, stacking armor, attack damage and overflow are not declared/evaluated here.");
    }

    private static CharacterSheetSkillValue? SelectedRoutineSkill3470(XElement root, CharacterSheetDerivedSection sheet, string sourceId)
    {
        if (!Guid.TryParse(sourceId, out var source)) return null;
        var saved = root.Element("newskills")?.Elements().Where(e => e.Name == "skills" || e.Name == "knoskills")
            .Elements("skill").Where(e => Guid.TryParse((string?)e.Element("suid"), out var id) && id == source).ToArray() ?? [];
        if (saved.Length > 1) return null;
        string? instance = saved.Length == 1 ? (string?)saved[0].Element("guid") : sourceId;
        var matches = sheet.Skills.Where(s => Guid.TryParse(s.Guid, out var id)
            && Guid.TryParse(instance, out var expected) && id == expected).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private CharacterCreationRoleFinding AssessOrdinaryFallback3470(CharacterCreationRoleRoutineObjective routine,
        XElement root, CharacterSheetDerivedSection sheet)
    {
        // A useful no-casting fallback with no assumed weapon/ammunition/tool.
        // This bounds readiness for a real Perception test; it does not claim a
        // detected target, initiative, attack or equipment-dependent fallback.
        var sources = Inputs.Skills.Authority.ActiveSkills.Where(s => s.SourceSkillId == routine.SkillSourceId).ToArray();
        var skill = SelectedRoutineSkill3470(root, sheet, routine.SkillSourceId);
        if (routine.Action is not { Target: "Perception", ForceOrLevel: null, VehicleSourceId: null }
            || routine.MagicIdentity is not null || routine.RequiredEquipmentSourceIds.Count != 0
            || sources.Length != 1 || sources[0].Name != "Perception" || sources[0].DefaultAttribute != "INT"
            || routine.Targets.Limit != CharacterCreationRoleLimit.Mental || routine.Targets.MinimumLimit is null
            || sheet.UnavailableReason is not null || skill is null || skill.UnavailableReason is not null
            || skill.Attribute != "INT" || skill.DicePool is null || sheet.MentalLimit is null)
            return Unknown("fallback-action", "This self-contained fallback supports a declared ordinary Perception test with its Mental limit. Other fallback actions or loadouts need their own consumer.");
        if (skill.EffectiveRating is not > 0 || skill.DicePool <= 0 || sheet.MentalLimit <= 0)
            return Failure("fallback-action", "Learn a usable Perception skill and retain a positive Mental limit; a declared fallback with no usable pool/limit is not ready.");
        return new("fallback-action", AssessedPass, "An ordinary Perception test is usable without casting, setup or borrowed equipment; Core INT/skill effects and Mental limit are assessed separately. Environmental detection/opposition and combat fallback are not inferred.",
            skill.DicePool, routine.Targets.MinimumSkillDicePool, [new("Core Mental limit", sheet.MentalLimit.Value, sheet.AuthorityDigest!)]);
    }

    // No spell name/ID constants: consume the exact admitted selected source. Refuse
    // extra descriptors, source bonuses and custom actions, even on a familiar name.
    internal static bool TryDirectSpell3470(XElement source, out int adjustment, out string defense)
        => CharacterDirectSpellRules5296.TryReadSource(source, out adjustment, out defense);

    private static bool TryOpposition3470(string target, string attribute, out int dice)
    {
        dice = 0;
        string prefix = "living:" + attribute + "=";
        return target.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(target.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out dice)
            && dice is >= 1 and <= 100;
    }

    private static CharacterCreationRoleFinding Observed3470(string code, decimal value, string detail, string authority)
        => new(code, AssessedPass, detail, value, null, [new("Source-bound " + code, value, authority)]);

    internal sealed record DirectSpellResult3470(decimal Success, decimal ExpectedNetHits, decimal AnyDamage,
        decimal PhysicalDamage, decimal StunDamage, decimal ExpectedDrain, decimal ExpectedPhysicalDrain, decimal ExpectedStunDrain);

    // SR5 d6 hit = 5 or 6. Narrow, bounded single direct spell distribution, not a
    // combat engine. Glitch consequences depend on GM choice and are not invented.
    internal static DirectSpellResult3470 DirectSpellDistribution3470(int casting, int force, int magic,
        int opposition, int resistance, int drain)
    {
        if (casting is < 1 or > 200 || force is < 1 or > 200 || magic is < 1 or > 100
            || opposition is < 1 or > 100 || resistance is < 0 or > 200 || drain is < 2 or > 300)
            throw new ArgumentOutOfRangeException(nameof(casting));
        var cast = Hits(casting); var resist = Hits(resistance); var defend = Hits(opposition);
        double success = 0, damage = 0, physical = 0, drainRisk = 0, drainMean = 0;
        for (int h = 0; h < cast.Length; h++)
        {
            int limited = CharacterDirectSpellRules5296.LimitedHits(h, force);
            if (CharacterDirectSpellRules5296.PhysicalDrain(limited, magic)) physical += cast[h];
            for (int d = 0; d < defend.Length; d++)
                if (limited > d) { double probability = cast[h] * defend[d]; success += probability; damage += probability * CharacterDirectSpellRules5296.NetHits(limited, d); }
        }
        for (int h = 0; h < resist.Length; h++)
            if (h < drain) { drainRisk += resist[h]; drainMean += resist[h] * CharacterDirectSpellRules5296.UnresistedDrain(drain, h); }
        // Decimal finding values are display evidence, not mutable authority.
        decimal risk = Probability(drainRisk), p = Probability(physical), mean = (decimal)drainMean;
        return new(Probability(success), (decimal)damage, risk, risk * p, risk * (1 - p), mean, mean * p, mean * (1 - p));

        static decimal Probability(double value) => (decimal)Math.Clamp(value, 0, 1);
        static double[] Hits(int count)
        {
            var values = new double[count + 1]; values[0] = 1;
            for (int die = 0; die < count; die++)
                for (int hit = die + 1; hit >= 0; hit--)
                    values[hit] = values[hit] * (2.0 / 3.0) + (hit == 0 ? 0 : values[hit - 1] / 3.0);
            return values;
        }
    }
}
