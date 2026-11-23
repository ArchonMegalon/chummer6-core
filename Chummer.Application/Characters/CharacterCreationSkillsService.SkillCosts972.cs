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
            string PolicyDigest, string PurchasesDigest, string ResultDigest, IReadOnlyDictionary<string, int> Leaves)
        {
            [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
            public bool EmptyPurchaseScope { get; init; }
        }
        internal SkillCostExecution972? SkillCosts972 { get; private set; }

        internal void FinishSkillCosts972(CharacterCreationKarmaSkillsPolicy policy,
            IReadOnlyList<CharacterCreationSkillKarmaPurchase> purchases, CharacterCreationAttributeSkillsPreview result,
            IReadOnlyDictionary<string, int> used)
        {
            // FinishPurchases already requires a completed canonical allocation and
            // successful interval, specialization, modifier and compensation checks.
            if (!Completed || PurchasesDigest is null
                || PurchasesDigest != CharacterCreationFinalizationDigest.Compute(new
                { Policy = policy, Purchases = purchases, Result = result })) return;
            // This is the end of the real purchase consumer, after automatic
            // members/grants, baseline charges and final pricing. A missing
            // purchase request or a point-only preview never reaches this seal.
            // Only this fully empty executed scope proves all eight price
            // branches inactive; unused terms in a nonempty scope stay unproven.
            bool empty = purchases.Count == 0 && used.Count == 0
                && result.Skills.All(row => row.KarmaLevels == 0 && row.KarmaCost == 0 && !row.KarmaSpecialization);
            if (used.Count == 0 && !empty) return;
            IReadOnlyDictionary<string, int> leaves = empty ? new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["karmanewactiveskill"] = policy.KarmaNewActiveSkill,
                ["karmaimproveactiveskill"] = policy.KarmaImproveActiveSkill,
                ["karmanewknowledgeskill"] = policy.KarmaNewKnowledgeSkill,
                ["karmaimproveknowledgeskill"] = policy.KarmaImproveKnowledgeSkill,
                ["karmanewskillgroup"] = policy.KarmaNewSkillGroup,
                ["karmaimproveskillgroup"] = policy.KarmaImproveSkillGroup,
                ["karmaspecialization"] = policy.KarmaSpecialization,
                ["karmaknospecialization"] = policy.KarmaKnowledgeSpecialization
            } : used;
            SkillCosts972 = new(policy.SettingsProfileId, policy.RawProfileInputsDigest,
                policy.AuthorityDigest, PurchasesDigest, CharacterCreationFinalizationDigest.Compute(result),
                new ReadOnlyDictionary<string, int>(leaves.ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.Ordinal))) { EmptyPurchaseScope = empty };
        }
    }
}
