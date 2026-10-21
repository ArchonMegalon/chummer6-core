using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationPrerequisiteService
{
    // This is a canonical consumer entry point, not an evidence factory. The
    // retained resolver must still bind its inputs to the private Complete run.
    public static bool TryExecuteSourceSelection718(CharacterCreationPrerequisiteAuthority authority,
        CharacterCreationPrerequisiteDraft expected, out CharacterCreationPrerequisiteDraft? executed)
    {
        executed = null;
        if (!IsValidAuthority(authority) || expected.HeritageSelection is null
            || expected.TalentSelection is null || authority.CreationKarmaTotal is not int karma
            || expected.AuthorityDigest != authority.AuthorityDigest
            || expected.SettingsProfileId != authority.SettingsProfileId
            || expected.BuildMethod != authority.BuildMethod
            || expected.PriorityTable != authority.PriorityTable
            || !expected.PriorityArray.SequenceEqual(authority.PriorityArray)
            || expected.SumToTenTarget != authority.SumToTenTarget
            || expected.Assignments.Select(row => row.CategoryId).Distinct().Count() != expected.Assignments.Count)
            return false;

        var blockers = new List<string>();
        var assignments = ResolveAssignments(authority,
            expected.Assignments.ToDictionary(row => row.CategoryId, row => row.Rank, StringComparer.Ordinal), blockers);
        int used;
        try { used = checked(assignments.Sum(row => row.SumToTenValue)); }
        catch (OverflowException) { return false; }
        ValidateSelectedRanks(authority, assignments, used, blockers);
        var heritage = ResolveHeritageSelection(authority, assignments, expected.HeritageSelection.SelectionId, blockers);
        var talent = ResolveTalentSelection(authority, assignments, expected.TalentSelection.SelectionId,
            expected.TalentSelection.GrantPlan?.ActiveSkills.Select(row => row.SelectionId).ToArray(),
            expected.TalentSelection.GrantPlan?.SkillGroups.Select(row => row.SelectionId).ToArray(), blockers);
        int normal = assignments.SingleOrDefault(row =>
            row.CategoryId == CharacterCreationPriorityCategoryIds.Attributes)?.BaseNormalAttributePoints ?? -1;
        if (normal < 0 || heritage is null || talent is null || blockers.Count != 0) return false;
        ResolveAttributePointPools718(heritage, talent, normal, blockers, out int effective, out int special);
        if (blockers.Count != 0) return false;
        var result = expected with
        {
            Assignments = assignments,
            HeritageSelection = heritage,
            TalentSelection = talent,
            CreationKarmaTotal = karma,
            CreationKarmaUsed = heritage.KarmaCost,
            EffectiveNormalAttributePoints = effective,
            TotalSpecialAttributePoints = special
        };
        // Compare the entire canonical selection, including nested grant plans,
        // source identities and house-rule point pools, before accounting it.
        if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(result, expected)
            || BuildBudget(authority, result, false).Blockers.Count != 0) return false;
        executed = result;
        return true;
    }

    // Extracted without changing arithmetic from EvaluatePreview. Both the
    // interactive creation route and retained Complete execution call this.
    private static void ResolveAttributePointPools718(CharacterCreationPriorityHeritageSelection heritage,
        CharacterCreationPriorityTalentSelection talent, int normal, ICollection<string> blockers,
        out int effective, out int special)
    {
        effective = heritage.HalvesNormalAttributePoints ? normal / 2 : normal;
        special = 0;
        try { special = checked(heritage.SpecialAttributePoints + talent.SpecialAttributePoints); }
        catch (OverflowException) { blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionInvalid); }
    }
}
