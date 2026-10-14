using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerSkillRatingRulesTests
{
    [TestMethod]
    public void Module_grants_accumulate_separately_from_saved_allocations()
    {
        XElement rows = new("improvements", Grant("SkillLevel", "Pistols", "1"),
            Grant("SkillLevel", "Pistols", "2"), Grant("SkillBase", "Pistols", "1"));
        string original = rows.ToString();
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryReadFreePoints(rows, "Pistols", false, out var free));
        Assert.AreEqual(new CharacterCareerSkillFreePoints(1, 3), free);
        var points = new CharacterCareerSkillRatingPoints(0, 0, free.Base, free.Levels);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectSkill(points, null, false, 12, out var rating));
        Assert.AreEqual(4, rating.TotalBaseRating);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectSkill(points with { KarmaPoints = 1 },
            null, false, 12, out var advanced));
        Assert.AreEqual(5, advanced.TotalBaseRating);
        Assert.AreEqual(0, points.KarmaPoints);
        Assert.AreEqual(original, rows.ToString());
    }

    [TestMethod]
    public void Exact_targets_do_not_admit_blank_other_or_case_folded_grants()
    {
        XElement rows = new("improvements", Grant("SkillLevel", "", "20"),
            Grant("SkillLevel", "pistols", "20"), Grant("SkillLevel", "Blades", "20"),
            Grant("SkillGroupLevel", "Pistols", "20"), Grant("SkillLevel", "Pistols", "2"));
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryReadFreePoints(rows, "Pistols", false, out var free));
        Assert.AreEqual(new CharacterCareerSkillFreePoints(0, 2), free);
    }

    [TestMethod]
    public void Disabled_conditional_and_rating_pool_grants_do_not_enter_free_levels()
    {
        XElement disabled = Grant("SkillLevel", "Pistols", "8");
        disabled.SetElementValue("enabled", "0");
        XElement conditional = Grant("SkillLevel", "Pistols", "8");
        conditional.SetElementValue("condition", "create");
        XElement rating = Grant("SkillLevel", "Pistols", "8");
        rating.SetElementValue("addtorating", "true");
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryReadFreePoints(
            new("improvements", disabled, conditional, rating), "Pistols", false, out var free));
        Assert.AreEqual(new CharacterCareerSkillFreePoints(0, 0), free);
    }

    [TestMethod]
    [DataRow("", 2)]
    [DataRow("career", 2)]
    [DataRow("create", 0)]
    [DataRow("Career", 0)]
    [DataRow(" ", 0)]
    public void Free_grant_conditions_match_exact_legacy_career_admission(string condition, int expected)
    {
        XElement row = Grant("SkillLevel", "Pistols", "2");
        row.SetElementValue("condition", condition);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryReadFreePoints(new("improvements", row),
            "Pistols", false, out var free));
        Assert.AreEqual(expected, free.Levels);
    }

    [TestMethod]
    [DataRow("unique", "precedence0")]
    [DataRow("uniquename", "same-source")]
    [DataRow("exclude", "Pistols")]
    [DataRow("target", "other")]
    [DataRow("min", "1")]
    [DataRow("max", "6")]
    [DataRow("val", "-1")]
    [DataRow("val", "0.5")]
    [DataRow("val", "1001")]
    [DataRow("enabled", "maybe")]
    [DataRow("addtorating", "maybe")]
    public void Unsupported_or_malformed_grant_does_not_become_zero(string field, string value)
    {
        XElement row = Grant("SkillLevel", "Pistols", "2");
        row.SetElementValue(field, value);
        Assert.IsFalse(CharacterCareerSkillRatingRules.TryReadFreePoints(new("improvements", row),
            "Pistols", false, out _));
    }

    [TestMethod]
    public void Duplicate_target_or_type_cannot_hide_a_relevant_grant()
    {
        XElement target = Grant("SkillLevel", "Blades", "1");
        target.Add(new XElement("improvedname", "Pistols"));
        XElement type = Grant("Other", "Pistols", "1");
        type.Add(new XElement("improvementttype", "SkillLevel"));
        foreach (var row in new[] { target, type })
            Assert.IsFalse(CharacterCareerSkillRatingRules.TryReadFreePoints(new("improvements", row),
                "Pistols", false, out _));
    }

    [TestMethod]
    public void Repeated_grants_are_bounded_before_integer_overflow()
    {
        Assert.IsFalse(CharacterCareerSkillRatingRules.TryReadFreePoints(new("improvements",
            Grant("SkillLevel", "Pistols", "1000"), Grant("SkillLevel", "Pistols", "1")),
            "Pistols", false, out _));
    }

    [TestMethod]
    public void Group_free_levels_apply_to_members_without_becoming_paid_group_karma()
    {
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryReadFreePoints(new("improvements",
            Grant("SkillGroupLevel", "Stealth", "2")), "Stealth", true, out var free));
        var groupPoints = new CharacterCareerSkillRatingPoints(0, 0, free.Base, free.Levels);
        var member = new CharacterCareerSkillRatingPoints(0, 0, 0, 1);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectGroup(groupPoints, [member, member], false, 12,
            out var group));
        Assert.AreEqual(new CharacterCareerSkillGroupRating(0, 2, true), group);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectSkill(member, group, false, 12, out var rating));
        Assert.AreEqual(3, rating.TotalBaseRating);
        Assert.AreEqual(0, groupPoints.KarmaPoints);
    }

    [TestMethod]
    public void Unequal_member_bases_suppress_paid_group_karma_but_not_free_group_levels()
    {
        var groupPoints = new CharacterCareerSkillRatingPoints(0, 4, 0, 2);
        var high = new CharacterCareerSkillRatingPoints(2, 0, 1, 0);
        var low = new CharacterCareerSkillRatingPoints(0, 0, 0, 1);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectGroup(groupPoints, [high, low], false, 12,
            out var broken));
        Assert.AreEqual(new CharacterCareerSkillGroupRating(0, 2, false), broken);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectGroup(groupPoints,
            [high, low with { KarmaPoints = 2 }], false, 12, out var intact));
        Assert.AreEqual(new CharacterCareerSkillGroupRating(0, 6, true), intact);
    }

    [TestMethod]
    public void Disabled_group_contributes_no_free_or_paid_points_to_a_member()
    {
        var member = new CharacterCareerSkillRatingPoints(1, 1, 1, 1);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectGroup(new(2, 2, 2, 2),
            [member], true, 12, out var group));
        Assert.AreEqual(new CharacterCareerSkillGroupRating(0, 0, false), group);
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectSkill(member, group, false, 12, out var rating));
        Assert.AreEqual(4, rating.TotalBaseRating);
    }

    [TestMethod]
    [DataRow(false, 4)]
    [DataRow(true, 6)]
    public void Group_base_policy_keeps_member_free_base_in_both_modes(bool usePoints, int expectedBase)
    {
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectSkill(new(2, 1, 1, 2), new(3, 2, true),
            usePoints, 12, out var rating));
        Assert.AreEqual(expectedBase, rating.Base);
        Assert.AreEqual(5, rating.Karma);
    }

    [TestMethod]
    public void Caps_match_separate_legacy_base_and_karma_caps_and_reject_invalid_input()
    {
        Assert.IsTrue(CharacterCareerSkillRatingRules.TryProjectSkill(new(10, 10, 10, 10), null,
            false, 12, out var rating));
        Assert.AreEqual(new CharacterCareerSkillRating(12, 12), rating);
        Assert.IsFalse(CharacterCareerSkillRatingRules.TryProjectSkill(new(-1, 0, 0, 0), null, false, 12, out _));
        Assert.IsFalse(CharacterCareerSkillRatingRules.TryProjectGroup(new(0, 0, 0, 0),
            new CharacterCareerSkillRatingPoints[] { null! }, false, 12, out _));
        Assert.IsFalse(CharacterCareerSkillRatingRules.TryProjectSkill(new(0, 0, 0, 0), new(1001, 0, true),
            false, 12, out _));
    }

    private static XElement Grant(string type, string name, string value) => new("improvement",
        new XElement("improvementttype", type), new XElement("improvedname", name),
        new XElement("enabled", "1"), new XElement("addtorating", "0"), new XElement("val", value));
}
