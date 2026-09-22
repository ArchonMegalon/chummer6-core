using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Pool skills with admitted quality caps, before Karma. Free-text specialties require GM review.</summary>
public static class Sr6CreationSkillRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p66-67,94-99";

    public static IReadOnlyList<Sr6CreationSkillOption> Options(Sr6CreationFoundationPreview foundation, string? aspect = null)
        => Sr6CreationSkillIds.Ordered.Select(id =>
        {
            string talent = foundation.Selection.TalentId;
            string? reason = id switch
            {
                "Tasking" when talent != "technomancer" => Sr6CreationSkillBlockers.SkillUnavailable,
                "Astral" when talent is "adept" or "mystic-adept" && !Sr6CreationAdeptPowerRules.HasAstralPerception(foundation.Selection) => Sr6CreationSkillBlockers.AstralPowerRequired,
                "Astral" when talent is not ("magician" or "aspected-magician" or "adept" or "mystic-adept") => Sr6CreationSkillBlockers.SkillUnavailable,
                "Sorcery" or "Conjuring" or "Enchanting" when talent == "aspected-magician" && aspect is null => Sr6CreationSkillBlockers.AspectRequired,
                "Sorcery" or "Conjuring" or "Enchanting" when talent == "aspected-magician" && aspect != id => Sr6CreationSkillBlockers.SkillUnavailable,
                "Sorcery" or "Conjuring" or "Enchanting" when talent is not ("magician" or "aspected-magician" or "mystic-adept") => Sr6CreationSkillBlockers.SkillUnavailable,
                _ => null
            };
            return new Sr6CreationSkillOption(id,
                Sr6SkillProvider.StartingMaximum + Sr6CreationQualityRules.SkillMaximumBonus(foundation, id), reason is null, reason);
        }).ToArray();

    public static CharacterCreationFoundationResult<Sr6CreationSkillPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationSkillSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeSkills(selection, out var frozen))
            return Fail(Sr6CreationSkillBlockers.InvalidAllocation);
        if (frozen!.AspectedSkillId is not null && foundation.Selection.TalentId != "aspected-magician")
            return Fail(Sr6CreationSkillBlockers.InvalidAllocation);
        var options = Options(foundation, frozen.AspectedSkillId);
        var values = new List<Sr6CreationSkillValue>();
        int spent = 0, maximumCount = 0;
        foreach (var row in frozen.Allocations)
        {
            var option = options.Single(option => option.SkillId == row.SkillId);
            if (row.Rating > option.Maximum) return Fail(Sr6CreationSkillBlockers.InvalidAllocation);
            if (row.Rating > 0 && !option.Available) return Fail(option.UnavailableReason!);
            if (row.Rating == option.Maximum) maximumCount++;
            bool exotic = row.SkillId == "ExoticWeapons";
            if ((row.Rating == 0 && row.Specializations.Count != 0)
                || (!exotic && row.Specializations.Count > 1)
                || (exotic && row.Rating > 0 && row.Specializations.Count == 0))
                return Fail(Sr6CreationSkillBlockers.SpecializationInvalid);
            int specializationCost = exotic ? Math.Max(0, row.Specializations.Count - 1) : row.Specializations.Count;
            spent += row.Rating + specializationCost;
            values.Add(new(row.SkillId, row.Rating, row.Specializations, specializationCost, row.Rating + specializationCost));
        }
        if (maximumCount > 1) return Fail(Sr6CreationSkillBlockers.MaximumCountExceeded);
        if (spent > foundation.Budget.SkillPoints) return Fail(Sr6CreationSkillBlockers.BudgetExceeded);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-skills.v1", SourceAnchor, Sr6CreationFoundationRules.CoreSourceSha256,
            foundation.Binding.AuthorityDigest, foundation.Selection.TalentId, frozen.AspectedSkillId,
            foundation.Budget.SkillPoints, Options = options, MaximumSkillsAtCap = 1,
            SpecializationPointCost = 1, FirstExoticSpecializationFree = true
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(values, spent, foundation.Budget.SkillPoints - spent, spent == foundation.Budget.SkillPoints,
                values.Any(row => row.Specializations.Count > 0), authority, [SourceAnchor]), []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationSkillPreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
