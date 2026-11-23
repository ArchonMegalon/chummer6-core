using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    // Reuse the canonical enabled/lifecycle/unique/precedence selector. It expects
    // scalar values; attribute channels project their inspected native fields here.
    internal decimal Channel(string type, string? target = null, string field = "val")
    {
        var rows = Effects.Where(e => Text(e, "improvementttype") == type
            && (target is null || Text(e, "improvedname") == target)).Select(e =>
        {
            var copy = new XElement(e);
            // Native grade caps use max * rating. Only the retained, validated
            // history path can contribute these rows to the reconciled graph.
            bool gradeCap = RetainedGrades5719 is not null && type == "Attribute" && field == "max"
                && Text(e, "improvementsource", "") is "Initiation" or "Submersion";
            copy.SetElementValue("val", Dec(e, field) * (field == "aug" || gradeCap ? Int(e, "rating") : 1));
            return copy;
        }).ToArray();
        return CharacterCareerReputationProjector.ResolvePhysicalChannel886(
            new XElement("character", new XElement("improvements", rows)), type, Flag(root, "created"));
    }
    internal int Rounded(string type, string? target = null, string field = "val") =>
        CharacterCreationEssenceRules1222.Round(Channel(type, target, field));

    internal CharacterCreationEssenceRules1222.NativeValue Attribute(string name, int min, int max, int aug,
        int allocated, int karma, bool unclamp)
    {
        // Attribute.Core Value consumes the Base target; TotalValue consumes the
        // ordinary target. Minimum/maximum deltas use their own canonical channels.
        return CharacterCreationEssenceRules1222.Native(new(name, true, min, max, aug, allocated, karma, unclamp,
            FreeBase: Math.Min(Rounded("Attributelevel", name), max - min),
            MinimumModifier: Rounded("Attribute", name, "min") + Rounded("Attribute", name + "Base", "min"),
            MaximumModifier: Rounded("Attribute", name, "max") + Rounded("Attribute", name + "Base", "max"),
            ValueModifier: Rounded("Attribute", name + "Base", "aug"), AugmentedModifier: Channel("Attribute", name, "aug"),
            AugmentedMaximumModifier: Rounded("Attribute", name, "augmax")));
    }

    internal CharacterSheetConditionValues Condition(IReadOnlyDictionary<string, int> attributes, decimal essence)
    {
        bool Has(string type) => Effects.Any(e => Text(e, "improvementttype") == type && Active(e));
        int body = attributes["BOD"], will = attributes["WIL"];
        int physical = checked(8 + (body + 1) / 2 + Rounded("PhysicalCM"));
        int stun = checked(8 + (will + 1) / 2 + Rounded("StunCM"));
        int threshold = checked(3 + Rounded("CMThreshold"));
        Need(physical >= 0 && stun >= 0 && threshold > 0, "condition-monitor-range", "condition");
        int filledPhysical = Int(root, "physicalcmfilled", 0), filledStun = Int(root, "stuncmfilled", 0);
        Need(filledPhysical >= 0 && filledStun >= 0, "negative-wounds", "condition");
        decimal offset = Channel("CMThresholdOffset"), shared = Channel("CMSharedThresholdOffset");
        bool ignoreP = Has("IgnoreCMPenaltyPhysical"), ignoreS = Has("IgnoreCMPenaltyStun");
        int Offset(int otherFilled, bool ignoreOther) => CharacterCreationEssenceRules1222.Round(ignoreOther
            ? offset + shared : Math.Max(offset, offset + shared - Math.Max(otherFilled - threshold - offset, 0)));
        int po = ignoreP ? int.MaxValue : Offset(filledStun, ignoreS);
        int so = ignoreS ? int.MaxValue : Offset(filledPhysical, ignoreP);
        int wound = checked((ignoreP ? 0 : Math.Min(0, po - Math.Min(filledPhysical, physical)) / threshold)
            + (ignoreS ? 0 : Math.Min(0, so - Math.Min(filledStun, stun)) / threshold));
        // Character.PhysicalCMNaturalRecovery/StunCMNaturalRecovery: no wound penalty.
        int pRecovery = checked(2 * body + Rounded("PhysicalCMRecovery") + (Has("AddESStoPhysicalCMRecovery") ? (int)essence : 0));
        int sRecovery = checked(body + will + Rounded("StunCMRecovery") + (Has("AddESStoStunCMRecovery") ? (int)essence : 0));
        return new(physical, stun, threshold, po, so, wound, pRecovery, sRecovery, Rounded("CMOverflow"))
        { ThresholdOffset = offset, SharedThresholdOffset = shared, IgnorePhysical = ignoreP, IgnoreStun = ignoreS };
    }

    internal CharacterPhysicalInitiativeProjection Initiative(CharacterCreationPhysicalInitiativeAuthority authority,
        IReadOnlyDictionary<string, int> attributes, CharacterSheetConditionValues condition)
    {
        Need(Int(root, "initiativedice", authority.BaseDice) == authority.BaseDice, "initiative-base-source-mismatch", "initiativedice");
        int initiative = Rounded("Initiative"), dice = Rounded("InitiativeDice"), additive = Rounded("InitiativeDiceAdd");
        Need(authority.BaseDice + dice + additive >= 0, "initiative-dice-range", "initiative");
        return new(Math.Max(0, checked(attributes["REA"] + attributes["INT"] + condition.WoundModifier + initiative)),
            Math.Min(authority.MaximumDice, checked(authority.BaseDice + dice + additive)), authority.BaseDice,
            attributes["REA"], attributes["INT"], condition.WoundModifier, initiative, dice, additive, Flag(root, "created"),
            CharacterCreationFinalizationDigest.Compute(new { Semantics, Source = authority, Graph = AuthorityDigest }), AuthorityDigest);
    }

    internal int SkillModifier(string name, string category, string group, string attribute, bool addRating)
    {
        // Skill.Bonus sums the relevant active rows; it does not use ValueOf's unique selector.
        return CharacterCreationEssenceRules1222.Round(Effects.Where(e => Active(e) && Text(e, "condition", "").Length == 0 && Flag(e, "addtorating") == addRating
            && ((Text(e, "improvementttype"), Text(e, "improvedname")) switch {
                ("Skill", var t) => t == name,
                ("SkillGroup", var t) => t == group,
                ("SkillCategory", var t) => t == category,
                ("SkillAttribute", var t) => t == attribute,
                _ => false })).Where(e => !Text(e, "exclude").Contains(name, StringComparison.Ordinal))
            .Sum(e => Dec(e, "val")));
    }

    internal static bool IsDefaultingChannel5701(string type) => type == "BlockSkillCategoryDefault";
    internal bool BlocksDefaulting(string category) => Effects.Any(e => Active(e)
        && IsDefaultingChannel5701(Text(e, "improvementttype")) && Text(e, "improvedname") == category);

    // SkillGroup.IsDisabled tests the presence of active non-rating effects, not
    // their numeric value (the native disable instructions have value zero).
    internal bool IsSkillGroupDisabled(string name, IEnumerable<string> categories) => Effects.Any(e => Active(e)
        && !Flag(e, "addtorating") && ((Text(e, "improvementttype"), Text(e, "improvedname")) switch {
            ("SkillGroupDisable", var target) => target == name,
            ("SkillGroupCategoryDisable", var target) => categories.Contains(target, StringComparer.Ordinal),
            _ => false }));

    internal bool HasConditionalSkill(string name, string category, string group, string attribute) => Effects.Any(e =>
        Flag(e, "enabled") && Text(e, "condition", "").Length > 0
        && ((Text(e, "improvementttype"), Text(e, "improvedname")) switch {
            ("Skill", var t) => t == name, ("SkillGroup", var t) => t == group,
            ("SkillCategory", var t) => t == category, ("SkillAttribute", var t) => t == attribute, _ => false })
        && !Text(e, "exclude").Contains(name, StringComparison.Ordinal));

    internal CharacterSheetMovementValues Movement(XElement metatype, IReadOnlyDictionary<string, int> attributes)
    {
        // Character.WalkingRate/RunningRate/SprintingRate and CalculatedMovement:
        // greatest active replacement, then SprintBonus in hundredths of m/hit.
        // The existing compiler rejects multiplier/percent effects, cyberlimbs,
        // dual-form owners and other unconsumed mechanics before reaching here.
        // Never bypass that closure or use the saved walk/run/sprint/totalvalue.
        try
        {
            Need(Text(metatype, "movement", "") != "Special", "movement-special-unconsumed", "metatype/movement");
            var walk = Rates("walk");
            var run = Rates("run");
            var sprint = Rates("sprint");
            return new(Mode("Ground", 0), Mode("Swim", 1), Mode("Fly", 2));

            decimal[] Rates(string field)
            {
                var node = Node(metatype, field);
                Need(node is not null, "movement-source-missing", "metatype/" + field);
                // Alternate-form rates need the independent dual-form attribute owner.
                Need(!node!.HasAttributes && !node.HasElements, "movement-source-shape-unconsumed", "metatype/" + field);
                var parts = node.Value.Split('/');
                Need(parts.Length == 3, "movement-source-shape", "metatype/" + field);
                var rates = new decimal[3];
                for (int i = 0; i < rates.Length; i++)
                {
                    Need(decimal.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rates[i])
                        && rates[i] >= 0, "movement-source-rate", "metatype/" + field);
                    var digits = parts[i].Split('.');
                    string whole = digits[0].TrimStart('0');
                    string fraction = digits.Length == 2 ? digits[1].TrimEnd('0') : "";
                    string normalized = (whole.Length == 0 ? "0" : whole) + (fraction.Length == 0 ? "" : "." + fraction);
                    Need(normalized == rates[i].ToString("0.############################", CultureInfo.InvariantCulture),
                        "movement-source-precision", "metatype/" + field);
                }
                return rates;
            }

            // Decimal checked arithmetic rejects overflow but can still round.
            // Compare integer coefficients at one fixed scale; do not publish a
            // rounded value as exact authority, even for an otherwise valid source.
            static BigInteger Scaled(decimal value)
            {
                int[] bits = decimal.GetBits(value);
                BigInteger coefficient = (uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
                if (bits[3] < 0) coefficient = -coefficient;
                return coefficient * BigInteger.Pow(10, 28 - ((bits[3] >> 16) & 255));
            }

            decimal MultiplyExactly(decimal value, decimal factor)
            {
                decimal result = checked(value * factor);
                Need(Scaled(result) * BigInteger.Pow(10, 28) == Scaled(value) * Scaled(factor),
                    "movement-result-precision", "movement/scaling");
                return result;
            }

            decimal DivideExactly(decimal value, decimal divisor)
            {
                decimal result = value / divisor;
                Need(Scaled(result) * Scaled(divisor) == Scaled(value) * BigInteger.Pow(10, 28),
                    "movement-result-precision", "movement/sprint");
                return result;
            }

            decimal Replacement(string type, string mode, decimal basis, decimal divisor = 1m)
            {
                var values = Effects.Where(e => Active(e) && Text(e, "improvementttype") == type
                    && Text(e, "improvedname") == mode).Select(e => DivideExactly(Dec(e, "val"), divisor)).ToArray();
                // A replacement overrides even a greater base; it is not an addition.
                return values.Length == 0 ? basis : values.Max();
            }

            CharacterSheetMovementMode Mode(string mode, int index)
            {
                decimal w = Replacement("WalkSpeed", mode, walk[index]);
                decimal r = Replacement("RunSpeed", mode, run[index]);
                decimal bonus = Channel("SprintBonus", mode);
                // Preserve the existing selector, while rejecting decimal rounding in
                // its sum. The compiler admits these channels only without unique or
                // precedence semantics, so their active values must sum exactly.
                Need(Scaled(bonus) == Effects.Where(e => Active(e) && Text(e, "improvementttype") == "SprintBonus"
                    && Text(e, "improvedname") == mode).Aggregate(BigInteger.Zero, (sum, e) => sum + Scaled(Dec(e, "val"))),
                    "movement-result-precision", "movement/" + mode);
                decimal sprintRate = Replacement("SprintSpeed", mode, sprint[index], 100m);
                decimal sprintBonus = DivideExactly(bonus, 100m);
                decimal s = checked(sprintRate + sprintBonus);
                Need(Scaled(s) == Scaled(sprintRate) + Scaled(sprintBonus), "movement-result-precision", "movement/" + mode);
                decimal factor = mode == "Swim" ? ((decimal)attributes["AGI"] + attributes["STR"]) / 2m : attributes["AGI"];
                w = MultiplyExactly(w, factor); r = MultiplyExactly(r, factor);
                Need(w >= 0 && r >= 0 && s >= 0, "movement-result-range", "movement/" + mode);
                return new(w != 0 || r != 0 || s != 0, w, r, s, null);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or OverflowException)
        {
            // Preserve independent sheet values, but never publish partial movement
            // numbers from a malformed vector or an overflowing computation.
            string reason = (exception is InvalidDataException ? exception.Message.Split(':').ElementAtOrDefault(1) : null) switch
            {
                "movement-special-unconsumed" => "The selected metatype uses special movement that this sheet cannot resolve.",
                "movement-source-missing" => "The selected metatype's movement rates are incomplete.",
                "movement-source-shape-unconsumed" => "Movement with alternate forms or structured source fields is not yet supported.",
                "movement-source-precision" or "movement-result-precision" => "The selected movement rate cannot be represented exactly.",
                "movement-source-rate" or "movement-source-shape" or "duplicate-or-namespaced-field" or "non-scalar-field"
                    => "The selected metatype's movement rates are malformed.",
                _ => "The movement calculation is outside the supported numeric range."
            };
            return CharacterSheetMovementValues.Unavailable(reason);
        }
    }

    internal IReadOnlyList<CharacterSheetRuleModifier>? RuleModifiers()
    {
        var result = new List<CharacterSheetRuleModifier>();
        _ = CharacterCareerReputationProjector.ResolvePhysicalChannel886(
            new XElement("character", new XElement("improvements", Effects)), "AddLimb", Flag(root, "created"), out var limbs);
        for (int index = 0; index < Effects.Length; index++)
        {
            var row = Effects[index];
            if (!Flag(row, "enabled") || Text(row, "improvementttype") == "AddLimb" && !limbs.Contains(index)) continue;
            string type = Text(row, "improvementttype"), condition = Text(row, "condition", "");
            CharacterSheetRuleModifierKind? kind = type switch {
                "AddLimb" => CharacterSheetRuleModifierKind.AdditionalLimb,
                "WalkSpeed" => CharacterSheetRuleModifierKind.WalkMultiplierReplacement,
                "RunSpeed" => CharacterSheetRuleModifierKind.RunMultiplierReplacement,
                "SprintSpeed" => CharacterSheetRuleModifierKind.SprintMetersPerHitReplacement,
                "SprintBonus" => CharacterSheetRuleModifierKind.SprintMetersPerHitAddition,
                "BlockSkillCategoryDefault" => CharacterSheetRuleModifierKind.SkillCategoryDefaultBlocked,
                "SkillCategoryKarmaCostMultiplier" => CharacterSheetRuleModifierKind.SkillCategoryKarmaCostPercent,
                "SkillCategorySpecializationKarmaCostMultiplier" => CharacterSheetRuleModifierKind.SkillCategorySpecializationKarmaCostPercent,
                "SkillGroupCategoryKarmaCostMultiplier" => CharacterSheetRuleModifierKind.SkillGroupCategoryKarmaCostPercent,
                "Skill" when condition.Length > 0 => CharacterSheetRuleModifierKind.ConditionalSkill,
                "SkillGroup" when condition.Length > 0 => CharacterSheetRuleModifierKind.ConditionalSkillGroup,
                "SkillCategory" when condition.Length > 0 => CharacterSheetRuleModifierKind.ConditionalSkillCategory,
                "SkillAttribute" when condition.Length > 0 => CharacterSheetRuleModifierKind.ConditionalSkillAttribute,
                _ => null };
            if (kind is null) continue;
            // Limb rows have already passed the shared unique/precedence selector.
            // Other scoped effects have no wireless suffix or unique selection.
            // Retain the selected contributor's exact owner identity and units.
            Need(Guid.TryParse(Text(row, "sourcename"), out Guid id), "scoped-effect-owner", type);
            decimal value = Dec(row, "val") / (type is "SprintBonus" or "SprintSpeed" ? 100m : 1m);
            result.Add(new(id, kind.Value, Text(row, "improvedname"), value, condition, Text(row, "exclude"), Flag(row, "addtorating")));
        }
        foreach (var (owner, value) in ActiveSourceDefense8587())
            result.Add(new(owner.Id, CharacterSheetRuleModifierKind.SourceDefenseTestPenalty, "Defense", value)
            {
                SourceId = owner.SourceId,
                SourceDigest = CharacterCreationFinalizationDigest.Compute(owner.Source.ToString(SaveOptions.DisableFormatting))
            });
        foreach (var (owner, value) in unresolvedDefense.Where(e => e.Owner.Enabled && !suppressedOwners.Contains(e.Owner.Id)))
            result.Add(new(owner.Id, CharacterSheetRuleModifierKind.UnresolvedDefenseTest, "Defense", value,
                ActionUnavailableReason: DefenseUnavailableReason));
        return result.Count == 0 ? null : result;
    }

    // These source channels are consumed by Other, not a ranged attack or round
    // transfer. Share the consumer mapping with the complete-sheet action boundary;
    // adding an unconsumed effect type to an action-only ignore list is not proof.
    internal static readonly IReadOnlyDictionary<string, string> OtherScalarChannels5701 =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DamageResistance"] = "DamageResistanceModifier", ["Armor"] = "ArmorModifier",
            ["Reach"] = "Reach", ["LifestyleCost"] = "LifestyleCost", ["SpellResistance"] = "SpellResistance",
            ["PathogenContactResist"] = "PathogenContactResist", ["PathogenIngestionResist"] = "PathogenIngestionResist",
            ["PathogenInhalationResist"] = "PathogenInhalationResist", ["PathogenInjectionResist"] = "PathogenInjectionResist",
            ["ToxinContactResist"] = "ToxinContactResist", ["ToxinIngestionResist"] = "ToxinIngestionResist",
            ["ToxinInhalationResist"] = "ToxinInhalationResist", ["ToxinInjectionResist"] = "ToxinInjectionResist"
        };

    internal IReadOnlyDictionary<string, int> Other(IReadOnlyDictionary<string, int> a)
    {
        var values = new Dictionary<string, int>
        {
            [OtherScalarChannels5701["DamageResistance"]] = Rounded("DamageResistance"),
            [OtherScalarChannels5701["Armor"]] = Rounded("Armor"),
            ["Composure"] = checked(a["CHA"] + a["WIL"] + SustainingPenalty5064 + Rounded("Composure")),
            ["Memory"] = checked(a["LOG"] + a["WIL"] + SustainingPenalty5064 + Rounded("Memory")),
            ["JudgeIntentionsDefense"] = checked(a["CHA"] + a["WIL"] + Rounded("JudgeIntentionsDefense"))
        };
        foreach (var channel in OtherScalarChannels5701.Where(channel => channel.Key is not ("DamageResistance" or "Armor")))
            values.Add(channel.Value, Rounded(channel.Key));
        foreach (string spell in Effects.Where(e => Text(e, "improvementttype") == "SpellDicePool")
            .Select(e => Text(e, "improvedname")).Distinct(StringComparer.Ordinal))
            values.Add("SpellDicePool:" + spell, Rounded("SpellDicePool", spell));
        if (Effects.Any(e => Text(e, "improvementttype") == "Notoriety")) values.Add("NotorietyModifier", Rounded("Notoriety"));
        AddSpriteValues5751(values, a);
        return values;
    }
}
