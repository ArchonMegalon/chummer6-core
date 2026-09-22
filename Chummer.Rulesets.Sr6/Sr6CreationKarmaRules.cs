using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Customization after pool allocation. Keeps pool costs and Karma costs separate.
/// No qualities, Karma spell/form purchases, expertise, or equipment effects are inferred.</summary>
public static class Sr6CreationKarmaRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p69,71-72,158";
    public const string SpecializationSourceAnchor = "sr6_core_de_2024:p66,72,94,97";

    public static Sr6CreationKarmaSpecializationOptions? SpecializationOptions(Sr6CreationFoundationPreview foundation)
    {
        if (foundation.Attributes is null || foundation.Skills is null) return null;
        var skills = Sr6CreationSkillRules.Options(foundation, foundation.Selection.Skills?.AspectedSkillId).Select(row =>
        {
            var saved = foundation.Skills.Values.SingleOrDefault(value => value.SkillId == row.SkillId);
            string[] existing = saved?.Specializations.ToArray() ?? [];
            bool exotic = row.SkillId == "ExoticWeapons";
            string? reason = row.UnavailableReason ?? (!exotic && existing.Length > 0 ? Sr6CreationKarmaBlockers.SpecializationLimit : null);
            return new Sr6CreationKarmaSpecializationOption(row.SkillId, saved?.Rating ?? 0,
                existing, exotic ? 0 : 2, reason is null, reason);
        }).ToArray();
        return new(5, 1, false, skills, [SpecializationSourceAnchor]);
    }

    public static Sr6CreationKarmaOptions? Options(Sr6CreationFoundationPreview foundation)
    {
        if (foundation.Attributes is null || foundation.Skills is null) return null;
        var attributes = foundation.Attributes.Values.Select(row => new Sr6CreationKarmaOption(
            row.AttributeId, row.Value, row.Maximum - row.Value, row.Value > 0,
            row.Value > 0 ? null : Sr6CreationKarmaBlockers.RatingUnavailable)).ToArray();
        var skills = Sr6CreationSkillRules.Options(foundation, foundation.Selection.Skills?.AspectedSkillId)
            .Select(row =>
            {
                int rating = foundation.Skills.Values.SingleOrDefault(value => value.SkillId == row.SkillId)?.Rating ?? 0;
                return new Sr6CreationKarmaOption(row.SkillId, rating, row.Maximum - rating, row.Available, row.UnavailableReason);
            }).ToArray();
        return new(50, 50, 2000, 5, attributes, skills);
    }

    public static CharacterCreationFoundationResult<Sr6CreationKarmaPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationKarmaSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeKarma(selection, out var frozen))
            return Fail(Sr6CreationKarmaBlockers.InvalidSelection);
        if (Options(foundation) is not { } options) return Fail(Sr6CreationKarmaBlockers.AllocationsRequired);
        var attributes = new List<Sr6CreationKarmaValue>();
        var skills = new List<Sr6CreationKarmaValue>();
        foreach (var purchase in frozen!.Attributes)
        {
            var option = options.Attributes.Single(row => row.Id == purchase.Id);
            if (!option.Available || purchase.Increase > option.MaximumIncrease)
                return Fail(Sr6CreationKarmaBlockers.RatingUnavailable);
            attributes.Add(Cost(option, purchase));
        }
        foreach (var purchase in frozen.Skills)
        {
            var option = options.Skills.Single(row => row.Id == purchase.Id);
            if (!option.Available) return Fail(option.UnavailableReason!);
            if (purchase.Increase > option.MaximumIncrease) return Fail(Sr6CreationKarmaBlockers.RatingUnavailable);
            bool needsWeapon = option.Id == "ExoticWeapons" && option.BaseRating == 0;
            if (needsWeapon != (purchase.FirstExoticSpecialization is not null))
                return Fail(Sr6CreationSkillBlockers.SpecializationInvalid);
            skills.Add(Cost(option, purchase));
        }
        // Creation caps still apply after Karma, including untouched pool allocations.
        if (foundation.Attributes!.Values.Count(row => row.AttributeId is not ("Edge" or "Magic" or "Resonance")
            && (attributes.SingleOrDefault(value => value.Id == row.AttributeId)?.Rating ?? row.Value) == row.Maximum) > 1)
            return Fail(Sr6CreationAttributeBlockers.MaximumCountExceeded);
        if (options.Skills.Count(row => (skills.SingleOrDefault(value => value.Id == row.Id)?.Rating ?? row.BaseRating)
            == Sr6SkillProvider.StartingMaximum) > 1) return Fail(Sr6CreationSkillBlockers.MaximumCountExceeded);
        var specialties = new List<Sr6CreationKarmaSpecializationValue>();
        var specialtyOptions = SpecializationOptions(foundation)!;
        foreach (var purchase in frozen.Specializations ?? [])
        {
            var option = specialtyOptions.Skills.Single(row => row.SkillId == purchase.SkillId);
            if (!option.Available) return Fail(option.UnavailableReason!);
            var ratingPurchase = skills.SingleOrDefault(row => row.Id == purchase.SkillId);
            if ((ratingPurchase?.Rating ?? option.BaseRating) < specialtyOptions.MinimumRating)
                return Fail(Sr6CreationKarmaBlockers.SpecializationRatingRequired);
            var existing = option.PoolSpecializations.Concat(specialties.Where(row => row.SkillId == purchase.SkillId).Select(row => row.Subject));
            if (ratingPurchase?.FirstExoticSpecialization is { } firstWeapon) existing = existing.Append(firstWeapon);
            if (existing.Contains(purchase.Subject, StringComparer.OrdinalIgnoreCase)
                || purchase.SkillId != "ExoticWeapons" && existing.Any())
                return Fail(Sr6CreationKarmaBlockers.SpecializationLimit);
            specialties.Add(new(purchase.SkillId, purchase.Subject, specialtyOptions.KarmaCost, option.DicePoolBonus, true));
        }
        Sr6CreationKarmaKnowledgePreview? knowledge = null;
        if (frozen.Knowledge is { } knowledgeSelection)
        {
            var result = Sr6CreationKarmaKnowledgeRules.Evaluate(foundation, knowledgeSelection);
            if (result.Value is null) return new(result.Outcome, null, result.Blockers);
            knowledge = result.Value;
        }
        int spent = attributes.Sum(row => row.KarmaCost) + skills.Sum(row => row.KarmaCost)
            + frozen.KarmaForNuyen + specialties.Sum(row => row.KarmaCost) + (knowledge?.KarmaCost ?? 0);
        if (frozen.KarmaForNuyen > options.MaximumKarmaForNuyen || spent > options.KarmaBudget)
            return Fail(Sr6CreationKarmaBlockers.BudgetExceeded);
        int remaining = options.KarmaBudget - spent;
        int nuyen = frozen.KarmaForNuyen * options.NuyenPerKarma;
        int powerPoints = foundation.Selection.TalentId is "adept" or "mystic-adept"
            ? frozen.Attributes.SingleOrDefault(row => row.Id == "Magic")?.Increase ?? 0 : 0;
        string[] anchors = foundation.PointBuy is null ? [SourceAnchor] : [SourceAnchor, Sr6CreationPointBuyRules.SourceAnchor];
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-karma.v1", SourceAnchor, Sr6CreationFoundationRules.CoreSourceSha256,
            foundation.Binding.AuthorityDigest, foundation.Selection.MetatypeId, foundation.Selection.TalentId,
            AttributeAuthority = foundation.Attributes.AuthorityDigest, SkillAuthority = foundation.Skills!.AuthorityDigest,
            options, selection = frozen, RatingCostMultiplier = 5, MaximumNormalAttributesAtCap = 1,
            MaximumSkillsAtCap = 1, foundation.Budget.ResourcesNuyen, powerPoints
        });
        // Preserve v1 authority and canonical bytes for already saved drafts without purchases.
        if (specialties.Count > 0)
        {
            authority = Sr6CreationFoundationIntegrity.Digest(new
            {
                Schema = "chummer.sr6.creation-karma-specializations.v1", BaseAuthority = authority,
                SpecializationSourceAnchor, Sr6CreationFoundationRules.CoreSourceSha256,
                Options = specialtyOptions, Values = specialties, MaximumNormalSpecializations = 1
            });
            anchors = [.. anchors, SpecializationSourceAnchor];
        }
        if (knowledge is not null)
        {
            authority = Sr6CreationFoundationIntegrity.Digest(new
            {
                Schema = "chummer.sr6.creation-karma-with-knowledge.v1", BaseAuthority = authority, Knowledge = knowledge
            });
            anchors = anchors.Concat(knowledge.SourceAnchorIds).Distinct(StringComparer.Ordinal).ToArray();
        }
        return new(CharacterCreationFoundationOutcomes.Success, new(attributes.ToArray(), skills.ToArray(),
            options.KarmaBudget, spent, remaining, frozen.KarmaForNuyen, nuyen, foundation.Budget.ResourcesNuyen + nuyen,
            options.MaximumCarryOver, Math.Max(0, remaining - options.MaximumCarryOver), powerPoints, authority, anchors)
            { Specializations = specialties.Count > 0 ? specialties.ToArray() : null, Knowledge = knowledge }, []);
    }

    // These consume only an already evaluated preview. Never read unverified client increases here.
    public static int AttributeRating(Sr6CreationFoundationPreview foundation, string id)
        => foundation.Karma?.Attributes.SingleOrDefault(row => row.Id == id)?.Rating
            ?? foundation.Attributes?.Values.Single(row => row.AttributeId == id).Value ?? 0;

    public static int SkillRating(Sr6CreationFoundationPreview foundation, string id)
        => foundation.Karma?.Skills.SingleOrDefault(row => row.Id == id)?.Rating
            ?? foundation.Skills?.Values.SingleOrDefault(row => row.SkillId == id)?.Rating ?? 0;

    private static Sr6CreationKarmaValue Cost(Sr6CreationKarmaOption option, Sr6CreationKarmaIncrease purchase)
    {
        var steps = Enumerable.Range(option.BaseRating + 1, purchase.Increase)
            .Select(rating => new Sr6CreationKarmaStep(rating, rating * 5)).ToArray();
        return new(option.Id, option.BaseRating, option.BaseRating + purchase.Increase,
            steps.Sum(row => row.KarmaCost), steps, purchase.FirstExoticSpecialization);
    }

    private static CharacterCreationFoundationResult<Sr6CreationKarmaPreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
