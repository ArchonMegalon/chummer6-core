using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal enum CharacterCreationCompositeEditStage { None, Magic, Qualities, Resources, Gear, Loadout, Contacts, Lifestyles }

/// <summary>
/// Protects the player's original selections while reviewing additions. This is
/// not source admission, budget validation or a capability to mutate a runner.
/// The review service evaluates the same detached observations independently.
/// </summary>
public static class CharacterCreationCompletionPreservationRules
{
    public static IReadOnlyList<string> Check(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionSelections proposed)
        => Check(inputs, proposed, CancellationToken.None);

    public static IReadOnlyList<string> Check(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionSelections proposed,
        CancellationToken cancellationToken)
        => CheckForCompositeEdit(inputs, proposed, CharacterCreationCompositeEditStage.None, cancellationToken);

    internal static IReadOnlyList<string> CheckForCompositeEdit(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections proposed,
        CharacterCreationCompositeEditStage stage, CancellationToken cancellationToken, bool attributesReplacement = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(proposed);
        if (inputs.Skills?.Attributes?.Attributes is null || inputs.Skills.Authority is null
            || inputs.Skills.Allocations is null || inputs.Skills.GroupAllocations is null
            || inputs.Magic?.Selections is null || inputs.Lifestyles?.Qualities?.SelectedOptionIds is null
            || inputs.Lifestyles.Gear?.State is null || inputs.Lifestyles.Gear.Resources is null
            || inputs.Contacts?.Contacts is null || inputs.Lifestyles.Lifestyles is null
            || proposed.AttributeAllocations is null || proposed.SkillAllocations is null
            || proposed.GroupAllocations is null || proposed.MagicSelections is null
            || proposed.QualityOptionIds is null || proposed.Gear is null
            || proposed.Contacts is null || proposed.Lifestyles is null
            || proposed.AttributeAllocations.Any(value => value is null)
            || proposed.GroupAllocations.Any(value => value is null))
            return [CharacterCreationCompletionReviewSchemas.RequestInvalid];
        var original = SnapshotOriginal(inputs);
        return Check(inputs.Skills, original, proposed, cancellationToken, stage, attributesReplacement);
    }

    // Reuse the canonical preservation origin for assessment; replay reads its
    // own equivalent origin from the real archive.
    internal static CharacterCreationCompletionOriginalSelections SnapshotOriginal(
        CharacterCreationCompletionReviewInputs inputs)
        => new CharacterCreationCompletionOriginalSelections(inputs.Magic.Selections,
            inputs.Lifestyles.Qualities.SelectedOptionIds,
            inputs.Lifestyles.Gear.State.PendingDraft?.Lines.Select(value =>
                new CharacterCreationGearSelection(value.OptionId, value.Quantity)).ToArray() ?? [],
            inputs.Lifestyles.Gear.Resources.PendingDraft?.KarmaInvestment ?? 0,
            inputs.Contacts.Contacts, inputs.Lifestyles.Lifestyles) { Loadout = inputs.Loadout?.OriginalSelections };

    internal static IReadOnlyList<string> Check(CharacterCreationSkillsCompletionInputs skills,
        CharacterCreationCompletionOriginalSelections originalSelections,
        CharacterCreationCompletionSelections proposed, CancellationToken cancellationToken = default,
        CharacterCreationCompositeEditStage stage = CharacterCreationCompositeEditStage.None, bool? attributesReplacement = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (skills?.Attributes?.Attributes is null || skills.Authority is null
            || skills.Allocations is null || skills.GroupAllocations is null
            || originalSelections is null || originalSelections.Magic is null
            || proposed.AttributeAllocations is null || proposed.SkillAllocations is null
            || proposed.GroupAllocations is null || proposed.MagicSelections is null
            || proposed.QualityOptionIds is null || proposed.Gear is null
            || proposed.Contacts is null || proposed.Lifestyles is null
            || proposed.AttributeAllocations.Any(value => value is null)
            || proposed.GroupAllocations.Any(value => value is null))
            return [CharacterCreationCompletionReviewSchemas.RequestInvalid];
        if (!CharacterCreationSkillCompletionPermissionRules.TrySnapshotAuthorityCatalogs(skills.Authority,
                out var authority, cancellationToken))
            return [CharacterCreationCompletionReviewSchemas.InvalidChangePermissions];
        skills = skills with { Authority = authority };
        var attributes = skills.Attributes.Attributes;
        if (proposed.ChangeableAttributeIds is null || proposed.ChangeableSkillTargets is null
            || proposed.ChangeableAttributeIds.Count > 13
            || proposed.ChangeableAttributeIds.Any(id => string.IsNullOrWhiteSpace(id)
                || !attributes.Any(value => value.AttributeId == id && value.IsEnabled))
            || proposed.ChangeableAttributeIds.Distinct(StringComparer.Ordinal).Count()
                != proposed.ChangeableAttributeIds.Count
            || !CharacterCreationSkillCompletionPermissionRules.TrySnapshot(skills.Authority,
                proposed.ChangeableSkillTargets, out var skillPermissions, cancellationToken))
            return [CharacterCreationCompletionReviewSchemas.InvalidChangePermissions];

        var mutableAttributes = proposed.ChangeableAttributeIds.ToHashSet(StringComparer.Ordinal);
        var mutableSkills = skillPermissions.ToHashSet();
        var originalAttributes = attributes.Select(value => new CharacterCreationAttributeAllocation(
            value.AttributeId, value.PriorityPointsSpent, value.KarmaLevels)).ToArray();
        // Missing zero rows are semantically zero, not a way to unlock them.
        var proposedAttributes = proposed.AttributeAllocations.ToList();
        foreach (var original in originalAttributes)
            if (!proposedAttributes.Any(value => value.AttributeId == original.AttributeId))
                proposedAttributes.Add(new(original.AttributeId, 0, 0));

        if (skills.CompositeBaseline is { } composite)
        {
            var selected = composite.Selections;
            originalSelections = new(selected.MagicSelections, selected.QualityOptionIds, selected.Gear,
                selected.TotalKarmaInvestment, selected.Contacts, selected.Lifestyles)
                { Loadout = selected.Loadout ?? originalSelections.Loadout };
        }
        // Only the synchronous owner-bound workflow or transaction-admitted replay
        // can reach this exception. Public Check and ordinary Finish retain old rules.
        bool replacing = attributesReplacement != false
            && CharacterCreationAttributesReplacementExecution.AuthorizesPreservation(skills, proposed);
        if (attributesReplacement == true && !replacing)
            return [CharacterCreationAttributesReplacementIntent.Invalid];
        bool preserved = replacing || Retains(originalAttributes, proposedAttributes, value => value.AttributeId,
            (before, after) => mutableAttributes.Contains(before.AttributeId)
                ? after.PriorityPoints >= before.PriorityPoints && after.KarmaLevels >= before.KarmaLevels
                : after == before);

        if (!replacing)
            preserved &= CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                skills.CompositeBaseline?.Selections.PlayerChosenAttributeIds, proposed.PlayerChosenAttributeIds);

        preserved &= Retains(skills.Allocations, proposed.SkillAllocations,
            value => new CharacterCreationSkillCompletionTarget(value.Kind, value.SourceSkillId),
            (before, after) => mutableSkills.Contains(new(before.Kind, before.SourceSkillId))
                ? before.IsNativeLanguage == after.IsNativeLanguage
                    && before.SpecializationOptionId == after.SpecializationOptionId
                    && (before.Rating is int rating ? after.Rating >= rating : after.Rating is null)
                : before == after);
        if (skills.CompositeBaseline?.Selections.SkillKarmaPurchases is { } paid)
            preserved &= proposed.SkillKarmaPurchases is { } purchases
                && Retains(paid, purchases, value => (value.Kind, value.SourceSkillId),
                    (before, after) => before.SourceNodeDigest == after.SourceNodeDigest
                        && after.Rating >= before.Rating
                        && (before.SpecializationOptionId is null || before.SpecializationOptionId == after.SpecializationOptionId));
        preserved &= Retains(skills.GroupAllocations, proposed.GroupAllocations, value => value.GroupId,
            (before, after) => mutableSkills.Contains(new(CharacterCreationSkillCompletionKinds.Group, before.GroupId))
                ? after.Rating >= before.Rating : after == before);

        // Raising a group also raises its members even if the explicit skill
        // allocations do not change. Do not use that to evade a member lock.
        foreach (var group in proposed.GroupAllocations)
        {
            var baseline = skills.GroupAllocations.FirstOrDefault(value => value.GroupId == group.GroupId);
            if (group.Rating <= (baseline?.Rating ?? 0)) continue;
            var definition = skills.Authority.SkillGroups.FirstOrDefault(value => value.GroupId == group.GroupId);
            if (!mutableSkills.Contains(new(CharacterCreationSkillCompletionKinds.Group, group.GroupId))
                || definition is null || definition.MemberSkillSourceIds.Any(id =>
                    !mutableSkills.Contains(new(CharacterCreationSkillKinds.Active, id))))
                preserved = false;
        }

        // Only the internal ordinary route may replace its one owned stage.
        // This permission is never serialized or accepted by public Finish APIs.
        var magic = stage == CharacterCreationCompositeEditStage.Magic
            ? proposed.MagicSelections : originalSelections.Magic;
        var nextMagic = proposed.MagicSelections;
        preserved &= (magic.Tradition is null || magic.Tradition == nextMagic.Tradition)
            && (magic.Stream is null || magic.Stream == nextMagic.Stream)
            && nextMagic.MysticAdeptPowerPoints >= magic.MysticAdeptPowerPoints
            && Retains(magic.AdeptPowers, nextMagic.AdeptPowers, value => value.Identity, (before, after) => before == after)
            && Retains(magic.Spells, nextMagic.Spells, value => value, (before, after) => before == after)
            && Retains(magic.ComplexForms, nextMagic.ComplexForms, value => value, (before, after) => before == after);

        preserved &= Retains(stage == CharacterCreationCompositeEditStage.Qualities
                ? proposed.QualityOptionIds : originalSelections.QualityOptionIds, proposed.QualityOptionIds,
            value => value, (before, after) => before == after);
        preserved &= Retains(stage == CharacterCreationCompositeEditStage.Gear ? proposed.Gear : originalSelections.Gear, proposed.Gear, value => value.OptionId, (before, after) => before == after);
        preserved &= stage == CharacterCreationCompositeEditStage.Loadout || (originalSelections.Loadout is null ? proposed.Loadout is null
            : proposed.Loadout is not null
                && (originalSelections.Loadout.Weapons.Count == 0
                    || originalSelections.Loadout.Weapons.Count == proposed.Loadout.Weapons.Count)
                && (originalSelections.Loadout.Armor.Count == 0
                    || originalSelections.Loadout.Armor.Count == proposed.Loadout.Armor.Count)
                && Retains(originalSelections.Loadout.Weapons, proposed.Loadout.Weapons, value => value.OptionId,
                    (before, after) => before == after)
                && Retains(originalSelections.Loadout.Armor, proposed.Loadout.Armor, value => value.OptionId,
                    (before, after) => before == after));
        preserved &= stage == CharacterCreationCompositeEditStage.Resources || proposed.TotalKarmaInvestment >= originalSelections.KarmaInvestment;
        preserved &= Retains(stage == CharacterCreationCompositeEditStage.Contacts ? proposed.Contacts : originalSelections.Contacts, proposed.Contacts, value => value.ContactId,
            (before, after) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(before, after));
        preserved &= Retains(stage == CharacterCreationCompositeEditStage.Lifestyles ? proposed.Lifestyles : originalSelections.Lifestyles, proposed.Lifestyles, value => value.LifestyleId,
            (before, after) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(before, after));

        return preserved ? [] : [CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict];
    }

    private static bool Retains<T, TKey>(IReadOnlyList<T> original, IReadOnlyList<T> proposed,
        Func<T, TKey> key, Func<T, T, bool> preserved) where TKey : notnull
    {
        if (original is null || proposed is null || original.Any(value => value is null)
            || proposed.Any(value => value is null)) return false;
        var originalKeys = original.Select(key).ToArray();
        var proposedKeys = proposed.Select(key).ToArray();
        if (originalKeys.Any(value => value is null) || proposedKeys.Any(value => value is null)
            || originalKeys.Distinct().Count() != original.Count
            || proposedKeys.Distinct().Count() != proposed.Count) return false;
        var index = proposed.ToDictionary(key);
        return original.All(before => index.TryGetValue(key(before), out var after) && preserved(before, after));
    }
}
