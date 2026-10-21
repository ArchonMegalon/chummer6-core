using System.Collections.ObjectModel;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    // Called only immediately after the canonical interval consumer succeeds.
    // These predicates select the exact TryCalculate branch; no prices are recomputed.
    private static void ObserveSkillInterval972(Dictionary<string, int>? used, string kind,
        int lower, int upper, CharacterCreationKarmaSkillsPolicy policy)
    {
        if (used is null || lower == upper) return;
        if (kind == CharacterCreationSkillKinds.Active)
        {
            used["karmaimproveactiveskill"] = policy.KarmaImproveActiveSkill;
            if (lower == 0) used["karmanewactiveskill"] = policy.KarmaNewActiveSkill;
        }
        else
        {
            used["karmaimproveknowledgeskill"] = policy.KarmaImproveKnowledgeSkill;
            // The canonical knowledge price omits the new-skill adjustment at zero improve cost.
            if (lower == 0 && policy.KarmaImproveKnowledgeSkill > 0)
                used["karmanewknowledgeskill"] = policy.KarmaNewKnowledgeSkill;
        }
    }

    private static void ObserveSkillSpecialization972(Dictionary<string, int>? used, string kind,
        CharacterCreationKarmaSkillsPolicy policy)
    {
        if (used is null) return;
        if (kind == CharacterCreationSkillKinds.Active) used["karmaspecialization"] = policy.KarmaSpecialization;
        else used["karmaknospecialization"] = policy.KarmaKnowledgeSpecialization;
    }

    // The completed TryPriceActive call consumed these terms in TryActiveInterval.
    // Group compensation is not a direct TryGroup purchase and must keep its own branches.
    private static void ObserveSkillCompensation972(Dictionary<string, int>? used, int lower, int upper,
        bool compensate, int? otherMinimum, CharacterCreationKarmaSkillsPolicy policy)
    {
        if (used is null || lower >= upper || !compensate || otherMinimum is null || otherMinimum <= lower) return;
        used["karmaimproveskillgroup"] = policy.KarmaImproveSkillGroup;
        if (lower == 0) used["karmanewskillgroup"] = policy.KarmaNewSkillGroup;
    }

    internal sealed partial class CompleteSkillTrace
    {
        internal sealed record SkillCostExecution972(string ProfileId, string ProfileInputsDigest,
            string PolicyDigest, string PurchasesDigest, string ResultDigest, IReadOnlyDictionary<string, int> Leaves);
        internal SkillCostExecution972? SkillCosts972 { get; private set; }

        internal void FinishSkillCosts972(CharacterCreationKarmaSkillsPolicy policy,
            IReadOnlyList<CharacterCreationSkillKarmaPurchase> purchases, CharacterCreationAttributeSkillsPreview result,
            IReadOnlyDictionary<string, int> used)
        {
            // FinishPurchases already requires a completed canonical allocation and
            // successful interval, specialization, modifier and compensation checks.
            if (!Completed || PurchasesDigest is null || used.Count == 0
                || PurchasesDigest != CharacterCreationFinalizationDigest.Compute(new
                { Policy = policy, Purchases = purchases, Result = result })) return;
            SkillCosts972 = new(policy.SettingsProfileId, policy.RawProfileInputsDigest,
                policy.AuthorityDigest, PurchasesDigest, CharacterCreationFinalizationDigest.Compute(result),
                new ReadOnlyDictionary<string, int>(used.ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.Ordinal)));
        }
    }
}
