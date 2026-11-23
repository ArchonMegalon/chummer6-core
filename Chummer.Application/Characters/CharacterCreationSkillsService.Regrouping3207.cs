using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    internal sealed partial class CompleteSkillTrace
    {
        internal sealed record GroupInventory3207(string GroupId, int Rating, int GrantedRating,
            IReadOnlyList<string> Members);
        internal sealed record RegroupedGroup3207(string GroupId, bool? IsBroken, bool? AllowRegrouping);
        internal sealed record RegroupingExecution3207(string ProfileId, string ProfileInputsDigest,
            string PolicyDigest, string PurchasesDigest, string ResultDigest, bool Enabled,
            bool ProjectGroups, IReadOnlyList<GroupInventory3207> Inventory,
            IReadOnlyList<RegroupedGroup3207> Results);

        internal IReadOnlyList<GroupInventory3207>? EnumeratedGroups3207 { get; private set; }
        internal RegroupingExecution3207? Regrouping3207 { get; private set; }

        internal void BeginRegroupingPurchases3207() => Regrouping3207 = null;

        internal void ResetRegrouping3207()
        {
            EnumeratedGroups3207 = null;
            Regrouping3207 = null;
        }

        // Called by Finish only after current selections, automatic group grants,
        // available members and legality/budget checks have actually completed.
        internal void FinishGroupEnumeration3207(IReadOnlyList<CharacterCreationSkillGroupProjection> groups)
            => EnumeratedGroups3207 = Inventory3207(groups);

        private static IReadOnlyList<GroupInventory3207> Inventory3207(
            IReadOnlyList<CharacterCreationSkillGroupProjection> groups)
            => Array.AsReadOnly(groups.OrderBy(row => row.GroupId, StringComparer.Ordinal)
                .Select(row => new GroupInventory3207(row.GroupId, row.Rating, row.GrantedRating,
                    Array.AsReadOnly(row.MemberSkillSourceIds.ToArray()))).ToArray());

        // Both call sites are successful PreviewPurchases exits, after the real
        // group-projection branch (including its completed empty enumeration).
        internal void FinishRegrouping3207(CharacterCreationKarmaSkillsPolicy policy,
            IReadOnlyList<CharacterCreationSkillKarmaPurchase> purchases,
            CharacterCreationAttributeSkillsPreview result, bool projectGroups)
        {
            Regrouping3207 = null;
            if (!Completed || EnumeratedGroups3207 is not { } inventory
                || policy.AllowSkillRegrouping is not bool enabled || result.Blockers.Count != 0
                || PurchasesDigest is null || PurchasesDigest != CharacterCreationFinalizationDigest.Compute(new
                    { Policy = policy, Purchases = purchases, Result = result })
                || CharacterCreationFinalizationDigest.Compute(inventory)
                    != CharacterCreationFinalizationDigest.Compute(Inventory3207(result.SkillGroups))
                || inventory.Count != 0 && (!projectGroups
                    || result.SkillGroups.Any(row => row.AllowSkillRegrouping != enabled || row.IsBroken is null))) return;
            Regrouping3207 = new(policy.SettingsProfileId, policy.RawProfileInputsDigest,
                policy.AuthorityDigest, PurchasesDigest, CharacterCreationFinalizationDigest.Compute(result),
                enabled, projectGroups, inventory, Array.AsReadOnly(result.SkillGroups
                    .OrderBy(row => row.GroupId, StringComparer.Ordinal)
                    .Select(row => new RegroupedGroup3207(row.GroupId, row.IsBroken, row.AllowSkillRegrouping)).ToArray()));
        }
    }
}
