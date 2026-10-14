using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReviewFixture = Chummer.Tests.CharacterCreationCompletionReviewServiceTests;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationCompletionPreservationTests
{
    [TestMethod]
    public void Completion_preservation_keeps_each_actual_saved_choice_and_does_not_mutate_the_baseline()
    {
        using var context = ReviewFixture.CreateSavedContext();
        var inputs = ReviewFixture.LoadInputs(context);
        var baseline = ReviewFixture.BaselineSelections(inputs);
        string originalInputs = CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs);
        string originalSelections = CharacterCreationFinalizationDigest.Compute(baseline);
        Accept(inputs, baseline, "unchanged actual saved selections");
        Accept(inputs, baseline with
        {
            AttributeAllocations = baseline.AttributeAllocations.Reverse().ToArray(),
            SkillAllocations = baseline.SkillAllocations.Reverse().ToArray(),
            GroupAllocations = baseline.GroupAllocations.Reverse().ToArray(),
            QualityOptionIds = baseline.QualityOptionIds.Reverse().ToArray(),
            Gear = baseline.Gear.Reverse().ToArray(),
            Contacts = baseline.Contacts.Reverse().ToArray(),
            Lifestyles = baseline.Lifestyles.Reverse().ToArray()
        }, "selection order is not a respec");

        var attribute = OriginalAttribute(inputs, "BOD");
        var skill = baseline.SkillAllocations.First(row => row.Kind == CharacterCreationSkillKinds.Active && row.Rating > 0);
        var group = baseline.GroupAllocations.First();
        var gear = baseline.Gear.First();
        var contact = baseline.Contacts.First();
        var lifestyle = baseline.Lifestyles.First();
        Assert.IsTrue(baseline.QualityOptionIds.Count > 0);
        Assert.IsNotNull(baseline.MagicSelections.Tradition);
        Assert.IsTrue(baseline.MagicSelections.Spells.Count > 0);
        Assert.IsTrue(baseline.MagicSelections.AdeptPowers.Count > 0, "The actual saved fixture must exercise power preservation.");
        Assert.IsTrue(baseline.MagicSelections.MysticAdeptPowerPoints > 0);
        Assert.IsTrue(baseline.TotalKarmaInvestment > 0);

        (string Label, CharacterCreationCompletionSelections Proposal)[] changes =
        [
            ("attribute rating", WithAttribute(baseline, attribute with { PriorityPoints = attribute.PriorityPoints + 1 })),
            ("skill rating", WithSkill(baseline, skill with { Rating = skill.Rating + 1 })),
            ("removed skill", baseline with { SkillAllocations = baseline.SkillAllocations.Where(row => row.SourceSkillId != skill.SourceSkillId || row.Kind != skill.Kind).ToArray() }),
            ("group rating", WithGroup(baseline, group with { Rating = group.Rating + 1 })),
            ("removed group", baseline with { GroupAllocations = baseline.GroupAllocations.Where(row => row.GroupId != group.GroupId).ToArray() }),
            ("gear quantity", baseline with { Gear = baseline.Gear.Select(row => row.OptionId == gear.OptionId ? row with { Quantity = row.Quantity + 1 } : row).ToArray() }),
            ("removed gear", baseline with { Gear = baseline.Gear.Where(row => row.OptionId != gear.OptionId).ToArray() }),
            ("removed quality", baseline with { QualityOptionIds = baseline.QualityOptionIds.Skip(1).ToArray() }),
            ("contact loyalty", baseline with { Contacts = baseline.Contacts.Select(row => row.ContactId == contact.ContactId ? row with { Loyalty = row.Loyalty + 1 } : row).ToArray() }),
            ("contact identity", baseline with { Contacts = baseline.Contacts.Select(row => row.ContactId == contact.ContactId ? row with { Identity = row.Identity with { Name = "Not the chosen contact" } } : row).ToArray() }),
            ("removed contact", baseline with { Contacts = baseline.Contacts.Where(row => row.ContactId != contact.ContactId).ToArray() }),
            ("lifestyle increments", baseline with { Lifestyles = baseline.Lifestyles.Select(row => row.LifestyleId == lifestyle.LifestyleId ? row with { Increments = row.Increments + 1 } : row).ToArray() }),
            ("lifestyle identity", baseline with { Lifestyles = baseline.Lifestyles.Select(row => row.LifestyleId == lifestyle.LifestyleId ? row with { Name = "Not the chosen home" } : row).ToArray() }),
            ("removed lifestyle", baseline with { Lifestyles = baseline.Lifestyles.Where(row => row.LifestyleId != lifestyle.LifestyleId).ToArray() }),
            ("tradition", baseline with { MagicSelections = baseline.MagicSelections with { Tradition = null } }),
            ("spell", baseline with { MagicSelections = baseline.MagicSelections with { Spells = baseline.MagicSelections.Spells.Skip(1).ToArray() } }),
            ("power point refund", baseline with { MagicSelections = baseline.MagicSelections with { MysticAdeptPowerPoints = baseline.MagicSelections.MysticAdeptPowerPoints - 1 } }),
            ("resource refund", baseline with { TotalKarmaInvestment = baseline.TotalKarmaInvestment - 1 })
        ];
        foreach (var (label, proposal) in changes) Reject(inputs, proposal, label);
        foreach (var power in baseline.MagicSelections.AdeptPowers)
        {
            Reject(inputs, baseline with { MagicSelections = baseline.MagicSelections with
            {
                AdeptPowers = baseline.MagicSelections.AdeptPowers.Select(row => row.Identity == power.Identity
                    ? row with { Levels = row.Levels + 1 } : row).ToArray()
            } }, "existing power level is exact");
            Reject(inputs, baseline with { MagicSelections = baseline.MagicSelections with
            {
                AdeptPowers = baseline.MagicSelections.AdeptPowers.Where(row => row.Identity != power.Identity).ToArray()
            } }, "existing power cannot disappear");
        }

        Assert.AreEqual(originalInputs, CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs));
        Assert.AreEqual(originalSelections, CharacterCreationFinalizationDigest.Compute(baseline));
        Assert.AreEqual(inputs.Skills.Binding.ContentRevision, context.Store.Get(context.WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Completion_preservation_permissions_allow_only_monotonic_ratings_and_require_every_group_member()
    {
        using var context = ReviewFixture.CreateSavedContext();
        var inputs = ReviewFixture.LoadInputs(context);
        var baseline = ReviewFixture.BaselineSelections(inputs);
        var attribute = OriginalAttribute(inputs, "BOD");
        var allowed = baseline with { ChangeableAttributeIds = [attribute.AttributeId] };
        Accept(inputs, WithAttribute(allowed, attribute with { PriorityPoints = attribute.PriorityPoints + 1 }), "explicit attribute increase");
        Accept(inputs, WithAttribute(allowed, attribute with { KarmaLevels = attribute.KarmaLevels + 1 }), "explicit attribute Karma increase");
        Reject(inputs, WithAttribute(allowed, attribute with { PriorityPoints = attribute.PriorityPoints - 1, KarmaLevels = attribute.KarmaLevels + 1 }), "priority decrease is not an increase");
        Reject(inputs, WithAttribute(allowed, attribute with { KarmaLevels = attribute.KarmaLevels - 1 }), "Karma decrease is not an increase");

        var zero = inputs.Skills.Attributes.Attributes.First(row => row.IsEnabled && row.PriorityPointsSpent == 0 && row.KarmaLevels == 0);
        var omittedZero = baseline with { AttributeAllocations = baseline.AttributeAllocations.Where(row => row.AttributeId != zero.AttributeId).ToArray() };
        Accept(inputs, omittedZero, "omitted zero remains zero");
        Reject(inputs, WithAttribute(omittedZero, new(zero.AttributeId, 1, 0)), "omission is not an unlock");
        Accept(inputs, WithAttribute(omittedZero with { ChangeableAttributeIds = [zero.AttributeId] }, new(zero.AttributeId, 1, 0)), "explicit zero unlock");

        var skill = baseline.SkillAllocations.First(row => row.Kind == CharacterCreationSkillKinds.Active && row.Rating > 0);
        var skillAllowed = baseline with { ChangeableSkillTargets = [new(skill.Kind, skill.SourceSkillId)] };
        Accept(inputs, WithSkill(skillAllowed, skill with { Rating = skill.Rating + 1 }), "explicit skill increase");
        Reject(inputs, WithSkill(skillAllowed, skill with { Rating = skill.Rating - 1 }), "skill refund");
        Reject(inputs, WithSkill(skillAllowed, skill with { SpecializationOptionId = "replacement-specialization" }), "permission is not specialization replacement");
        Reject(inputs, WithSkill(skillAllowed, skill with { IsNativeLanguage = !skill.IsNativeLanguage }), "permission is not native-language replacement");
        var native = baseline.SkillAllocations.First(row => row.IsNativeLanguage);
        var nativeAllowed = baseline with { ChangeableSkillTargets = [new(native.Kind, native.SourceSkillId)] };
        Reject(inputs, WithSkill(nativeAllowed, native with { Rating = 1 }), "native null rating stays null");
        Reject(inputs, WithSkill(nativeAllowed, native with { IsNativeLanguage = false }), "chosen native language stays chosen");

        var group = baseline.GroupAllocations.First();
        var definition = inputs.Skills.Authority.SkillGroups.Single(row => row.GroupId == group.GroupId);
        Assert.IsTrue(definition.MemberSkillSourceIds.Count > 0);
        var groupTarget = new CharacterCreationSkillCompletionTarget(CharacterCreationSkillCompletionKinds.Group, group.GroupId);
        var groupOnly = baseline with { ChangeableSkillTargets = [groupTarget] };
        Reject(inputs, WithGroup(groupOnly, group with { Rating = group.Rating + 1 }), "group cannot bypass member locks");
        var allMembers = groupOnly with { ChangeableSkillTargets = [groupTarget, .. definition.MemberSkillSourceIds.Select(id =>
            new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, id))] };
        Accept(inputs, WithGroup(allMembers, group with { Rating = group.Rating + 1 }), "explicit group and member increase");
        Reject(inputs, WithGroup(allMembers, group with { Rating = group.Rating - 1 }), "group refund");
    }

    [TestMethod]
    public void Completion_preservation_rejects_unknown_duplicate_permissions_but_does_not_claim_source_admission_for_additions()
    {
        using var context = ReviewFixture.CreateSavedContext();
        var inputs = ReviewFixture.LoadInputs(context);
        var baseline = ReviewFixture.BaselineSelections(inputs);
        string id = inputs.Skills.Attributes.Attributes.First(row => row.IsEnabled).AttributeId;
        var skill = baseline.SkillAllocations.First();
        var target = new CharacterCreationSkillCompletionTarget(skill.Kind, skill.SourceSkillId);
        CharacterCreationCompletionSelections[] malformed =
        [
            baseline with { ChangeableAttributeIds = [id, id] },
            baseline with { ChangeableAttributeIds = ["unknown-attribute"] },
            baseline with { ChangeableAttributeIds = [" "] },
            baseline with { ChangeableSkillTargets = [target, target] },
            baseline with { ChangeableSkillTargets = [new(target.Kind, "unknown-skill")] },
            baseline with { ChangeableSkillTargets = [new("unknown-kind", target.SourceId)] }
        ];
        foreach (var proposal in malformed)
            CollectionAssert.Contains(CharacterCreationCompletionPreservationRules.Check(inputs, proposal).ToArray(),
                CharacterCreationCompletionReviewSchemas.InvalidChangePermissions);
        var disabled = inputs.Skills.Attributes.Attributes.First(row => !row.IsEnabled);
        CollectionAssert.Contains(CharacterCreationCompletionPreservationRules.Check(inputs,
            baseline with { ChangeableAttributeIds = [disabled.AttributeId] }).ToArray(),
            CharacterCreationCompletionReviewSchemas.InvalidChangePermissions);

        var extraGear = inputs.Lifestyles.Gear.State.Authority.Options.First(option => option.IsSelectable
            && baseline.Gear.All(row => row.OptionId != option.OptionId));
        var extraQuality = inputs.Lifestyles.Qualities.Authority.Options.First(option => option.IsSelectable
            && !baseline.QualityOptionIds.Contains(option.OptionId));
        var contact = baseline.Contacts.First();
        var home = baseline.Lifestyles.First();
        var additions = baseline with
        {
            Gear = [.. baseline.Gear, new(extraGear.OptionId, 1)],
            QualityOptionIds = [.. baseline.QualityOptionIds, extraQuality.OptionId],
            Contacts = [.. baseline.Contacts, contact with { ContactId = Guid.Parse("e3a82894-f219-4c1f-b72c-b424336e9977") }],
            Lifestyles = [.. baseline.Lifestyles, home with { LifestyleId = Guid.Parse("ef018beb-8742-4892-bc7b-1e14c232388e") }],
            TotalKarmaInvestment = baseline.TotalKarmaInvestment + 1,
            MagicSelections = baseline.MagicSelections with { MysticAdeptPowerPoints = baseline.MagicSelections.MysticAdeptPowerPoints + 1 }
        };
        // This helper checks preservation only. The whole Review separately
        // validates source identity, eligibility, all shared budgets and caps.
        Accept(inputs, additions, "new choices leave the original exact subset intact");
        Reject(inputs, baseline with { Gear = [.. baseline.Gear, baseline.Gear.First()] }, "duplicate baseline key is not an addition");
    }

    [TestMethod]
    public void Completion_preservation_cannot_refund_actual_saved_attribute_purchases_even_with_permission()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        MagicResonanceCompletionTests.SaveAttributes(context, [new("BOD", 1, 1)]);
        var inputs = ReviewFixture.LoadInputs(context);
        var baseline = ReviewFixture.BaselineSelections(inputs);
        var purchased = baseline.AttributeAllocations.Single(row => row.AttributeId == "BOD");
        Assert.AreEqual(1, purchased.PriorityPoints);
        Assert.AreEqual(1, purchased.KarmaLevels);
        string original = CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs);
        Accept(inputs, baseline, "actual initial attribute purchase");
        Reject(inputs, baseline with { AttributeAllocations = [] }, "deleting a purchased row is not default zero");
        var allowed = baseline with { ChangeableAttributeIds = [purchased.AttributeId] };
        Reject(inputs, WithAttribute(allowed, purchased with { PriorityPoints = 0 }), "chosen priority point cannot be refunded");
        Reject(inputs, WithAttribute(allowed, purchased with { KarmaLevels = 0 }), "chosen Karma level cannot be refunded");
        Reject(inputs, WithAttribute(allowed, purchased with { PriorityPoints = 0, KarmaLevels = 2 }), "priority-to-Karma respec");
        Reject(inputs, WithAttribute(allowed, purchased with { PriorityPoints = 2, KarmaLevels = 0 }), "Karma-to-priority respec");
        Accept(inputs, WithAttribute(allowed, purchased with { PriorityPoints = 2 }), "permitted increase preserves existing purchase");
        Assert.AreEqual(original, CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs));
    }

    private static CharacterCreationAttributeAllocation OriginalAttribute(CharacterCreationCompletionReviewInputs inputs, string id)
    {
        var original = inputs.Skills.Attributes.Attributes.Single(row => row.AttributeId == id);
        return new(original.AttributeId, original.PriorityPointsSpent, original.KarmaLevels);
    }

    private static CharacterCreationCompletionSelections WithAttribute(CharacterCreationCompletionSelections selections,
        CharacterCreationAttributeAllocation replacement)
        => selections with { AttributeAllocations = [.. selections.AttributeAllocations.Where(row => row.AttributeId != replacement.AttributeId), replacement] };

    private static CharacterCreationCompletionSelections WithSkill(CharacterCreationCompletionSelections selections,
        CharacterCreationSkillAllocation replacement)
        => selections with { SkillAllocations = [.. selections.SkillAllocations.Where(row => row.SourceSkillId != replacement.SourceSkillId || row.Kind != replacement.Kind), replacement] };

    private static CharacterCreationCompletionSelections WithGroup(CharacterCreationCompletionSelections selections,
        CharacterCreationSkillGroupAllocation replacement)
        => selections with { GroupAllocations = [.. selections.GroupAllocations.Where(row => row.GroupId != replacement.GroupId), replacement] };

    private static void Accept(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections selections, string label)
    {
        var blockers = CharacterCreationCompletionPreservationRules.Check(inputs, selections);
        Assert.AreEqual(0, blockers.Count, label + ": " + string.Join(",", blockers));
    }

    private static void Reject(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections selections, string label)
        => CollectionAssert.Contains(CharacterCreationCompletionPreservationRules.Check(inputs, selections).ToArray(),
            CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict, label);
}
