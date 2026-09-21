using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Sr6;

public sealed partial class Sr6CharacterCreationProvider
{
    /// <summary>
    /// Calculates the five-category allocation using this provider's SR6 table.
    /// Companion availability is supplied by the Core source context, never
    /// inferred from the requested method. This pure projection neither saves a
    /// character nor establishes source, metatype, talent or finalization authority.
    /// </summary>
    public Sr6CreationPriorityResult EvaluatePriorities(
        Sr6CreationPriorityRequest request,
        bool companionEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.RulesetId, RulesetDefaults.Sr6, StringComparison.Ordinal))
            return Reject(Sr6CreationPriorityBlockers.RulesetMismatch);

        bool sumToTen = request.BuildMethod == Sr6CharacterCreationBuildMethods.SumToTen;
        if (!sumToTen && request.BuildMethod != Sr6CharacterCreationBuildMethods.Priority)
            return Reject(Sr6CreationPriorityBlockers.MethodUnsupported);
        if (sumToTen && !companionEnabled)
            return Reject(Sr6CreationPriorityBlockers.CompanionRequired);

        // Snapshot before validation: no later dictionary lookup uses caller-owned
        // list storage, and duplicate/missing categories cannot project partial budgets.
        IReadOnlyList<string> categories = CharacterCreationPriorityCategoryIds.Ordered;
        if (request.Assignments is null || request.Assignments.Count != categories.Count)
            return Reject(Sr6CreationPriorityBlockers.CategoriesInvalid);
        Sr6CreationPriorityChoice[] assignments = request.Assignments.ToArray();
        if (assignments.Length != categories.Count
            || assignments.Any(assignment => assignment is null
                || !categories.Contains(assignment.CategoryId, StringComparer.Ordinal))
            || assignments.Select(assignment => assignment.CategoryId)
                .Distinct(StringComparer.Ordinal).Count() != categories.Count)
        {
            return Reject(Sr6CreationPriorityBlockers.CategoriesInvalid);
        }

        if (assignments.Any(assignment => assignment.Rank is not ("A" or "B" or "C" or "D" or "E")))
            return Reject(Sr6CreationPriorityBlockers.RankInvalid);

        int total = assignments.Sum(assignment => 'E' - assignment.Rank[0]);
        if (sumToTen && total != 10)
            return new(false, total, null, [Sr6CreationPriorityBlockers.SumToTenMismatch]);
        if (!sumToTen && assignments.Select(assignment => assignment.Rank)
                .Distinct(StringComparer.Ordinal).Count() != categories.Count)
        {
            return Reject(Sr6CreationPriorityBlockers.UniqueRanksRequired);
        }

        var byCategory = assignments.ToDictionary(assignment => assignment.CategoryId,
            assignment => assignment.Rank, StringComparer.Ordinal);
        var budget = new Sr6CreationPriorityBudget(
            GetPriorityRow(byCategory[CharacterCreationPriorityCategoryIds.Attributes][0]).AttributePoints,
            GetPriorityRow(byCategory[CharacterCreationPriorityCategoryIds.Skills][0]).SkillPoints,
            GetPriorityRow(byCategory[CharacterCreationPriorityCategoryIds.Resources][0]).ResourcesNuyen,
            GetPriorityRow(byCategory[CharacterCreationPriorityCategoryIds.Heritage][0]).MetatypeAdjustmentPoints,
            byCategory[CharacterCreationPriorityCategoryIds.Talent]);
        return new(true, sumToTen ? total : null, budget, []);
    }

    private static Sr6CreationPriorityResult Reject(string blocker)
        => new(false, null, null, [blocker]);
}
