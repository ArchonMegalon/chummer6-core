using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Bounded, source-priced additions to the exact original purchases.
/// No store, generated IDs, refunds, implicit investment or confirmation path.</summary>
public static class CharacterCreationPurchaseCompletionPlanner
{
    public const int MaximumTargets = 64;
    public const int MaximumEvaluations = 64;

    public static CharacterCreationFoundationResult<CharacterCreationPurchaseCompletionPlan> Plan(
        CharacterCreationLifestylesCompletionInputs inputs,
        CharacterCreationMagicResonanceCompletionInputs magicInputs,
        CharacterCreationPurchaseCompletionRequest request,
        CancellationToken cancellationToken = default)
        => Plan(inputs, magicInputs, null, request, cancellationToken);

    public static CharacterCreationFoundationResult<CharacterCreationPurchaseCompletionPlan> Plan(
        CharacterCreationLifestylesCompletionInputs inputs,
        CharacterCreationMagicResonanceCompletionInputs magicInputs,
        CharacterCreationLoadoutCompletionInputs? loadoutInputs,
        CharacterCreationPurchaseCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(magicInputs);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = Detach(inputs);
        magicInputs = Detach(magicInputs);
        if (loadoutInputs is not null) loadoutInputs = Detach(loadoutInputs);
        request = Detach(request);
        if (!ValidRequest(request))
            return Reject(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationPurchaseCompletionBlockers.RequestInvalid);
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(inputs.SnapshotDigest, request.LifestylesSnapshotDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(magicInputs.SnapshotDigest, request.MagicSnapshotDigest)
            || (loadoutInputs is null) != (request.LoadoutSnapshotDigest is null)
            || loadoutInputs is not null && !CharacterCreationSkillsDigest.EqualsFixedTime(
                loadoutInputs.SnapshotDigest, request.LoadoutSnapshotDigest))
            return Reject(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationPurchaseCompletionBlockers.SnapshotConflict);
        if (loadoutInputs is null && request.Targets.Any(target => target.Weapon is not null || target.Armor is not null)
            || loadoutInputs is not null && !CharacterCreationLoadoutCompletionService.IsValidInputs(loadoutInputs))
            return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable);
        if (inputs.Gear is not { State: not null, Resources: not null } || inputs.Lifestyles is null
            || inputs.Lifestyles.Any(row => row is null)
            || inputs.Gear.State.PendingDraft is { } draft && (draft.Lines is null || draft.Lines.Any(row => row is null)))
            return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        if (loadoutInputs is not null && (loadoutInputs.Binding.WorkspaceId != inputs.Binding.WorkspaceId
                || loadoutInputs.Binding.ContentRevision != inputs.Binding.ContentRevision
                || loadoutInputs.Binding.SavedRevision != inputs.Binding.SavedRevision
                || !CharacterCreationSkillsDigest.EqualsFixedTime(loadoutInputs.Binding.RawCharacterXmlDigest, inputs.Binding.ContentDigest)
                || loadoutInputs.Binding.AuxiliaryStateDigest != inputs.Binding.AuxiliaryStateDigest
                || loadoutInputs.Authority.SettingsProfileId != inputs.Authority.SettingsProfileId
                || loadoutInputs.Authority.ProfileDigest != inputs.Authority.ProfileDigest))
            return Reject(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
        if (request.TotalKarmaInvestment < (inputs.Gear.Resources.PendingDraft?.KarmaInvestment ?? 0))
            return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationPurchaseCompletionBlockers.InvestmentDecrease);

        var gear = inputs.Gear.State.PendingDraft?.Lines.Select(line =>
            new CharacterCreationGearSelection(line.OptionId, line.Quantity)).ToList() ?? [];
        var lifestyles = inputs.Lifestyles.ToList();
        var loadout = loadoutInputs?.OriginalSelections;
        var original = Evaluate(gear, lifestyles, loadout);
        int evaluations = 1;
        if (original.Value is not { } current || !Admissible(current))
            return new(original.Outcome == CharacterCreationLifestyleOutcomes.Conflict
                    ? CharacterCreationFoundationOutcomes.Conflict : CharacterCreationFoundationOutcomes.Blocked,
                null, original.Blockers);
        decimal originalCost = current.Gear.Resources.Budget.KnownPurchaseCost;
        if (current.Gear.Resources.Budget.RemainingNuyen < request.NuyenToReserve)
            return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationPurchaseCompletionBlockers.ReserveRequired);

        var results = new List<CharacterCreationPurchaseCompletionTargetResult>();
        for (int index = 0; index < request.Targets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = request.Targets[index];
            var existingGear = target.Gear is { } item
                ? gear.FirstOrDefault(row => row.OptionId == item.OptionId) : null;
            var existingLifestyle = target.Lifestyle is { } home
                ? lifestyles.FirstOrDefault(row => row.LifestyleId == home.LifestyleId) : null;
            var existingWeapon = target.Weapon is { } weapon
                ? loadout?.Weapons.FirstOrDefault(row => row.OptionId == weapon.OptionId) : null;
            var existingArmor = target.Armor is { } armor
                ? loadout?.Armor.FirstOrDefault(row => row.OptionId == armor.OptionId) : null;
            if (existingGear is not null || existingLifestyle is not null || existingWeapon is not null || existingArmor is not null)
            {
                bool same = existingGear is not null ? existingGear == target.Gear
                    : existingWeapon is not null ? existingWeapon == target.Weapon
                    : existingArmor is not null ? existingArmor == target.Armor
                    : CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(existingLifestyle, target.Lifestyle);
                results.Add(new(index, same ? CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent
                    : CharacterCreationPurchaseCompletionTargetOutcomes.ExistingSelectionLocked,
                    same ? [] : [CharacterCreationPurchaseCompletionBlockers.ExistingSelectionLocked]));
                continue;
            }
            if (evaluations >= request.MaximumEvaluations)
            {
                results.Add(new(index, CharacterCreationPurchaseCompletionTargetOutcomes.WorkLimit,
                    [CharacterCreationPurchaseCompletionBlockers.WorkLimit]));
                continue;
            }

            List<CharacterCreationGearSelection> proposedGear = target.Gear is null ? gear : [.. gear, target.Gear];
            List<CharacterCreationLifestyleConfiguration> proposedLifestyles = target.Lifestyle is null
                ? lifestyles : [.. lifestyles, target.Lifestyle];
            var proposedLoadout = loadout is null ? null : new CharacterCreationLoadoutSelections(
                target.Weapon is null ? loadout.Weapons : [.. loadout.Weapons, target.Weapon],
                target.Armor is null ? loadout.Armor : [.. loadout.Armor, target.Armor]);
            var candidate = Evaluate(proposedGear, proposedLifestyles, proposedLoadout);
            evaluations++;
            if (candidate.Outcome == CharacterCreationLifestyleOutcomes.Conflict)
                return new(CharacterCreationFoundationOutcomes.Conflict, null, candidate.Blockers);
            if (candidate.Value is not { } preview || !Admissible(preview))
            {
                bool unaffordable = candidate.Blockers.Any(IsFundsBlocker)
                    && candidate.Blockers.All(blocker => IsFundsBlocker(blocker)
                        || blocker == CharacterCreationLifestylesCompletionSchemas.StartingLifestyleRequired);
                results.Add(new(index, unaffordable ? CharacterCreationPurchaseCompletionTargetOutcomes.Unaffordable
                    : CharacterCreationPurchaseCompletionTargetOutcomes.Unavailable, candidate.Blockers.ToArray()));
                continue;
            }
            decimal additional = preview.Gear.Resources.Budget.KnownPurchaseCost - originalCost;
            if (additional < 0m)
                return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            if (preview.Gear.Resources.Budget.RemainingNuyen < request.NuyenToReserve)
            {
                results.Add(new(index, CharacterCreationPurchaseCompletionTargetOutcomes.ReserveRequired,
                    [CharacterCreationPurchaseCompletionBlockers.ReserveRequired]));
                continue;
            }
            if (additional > request.MaximumAdditionalSpend)
            {
                results.Add(new(index, CharacterCreationPurchaseCompletionTargetOutcomes.SpendLimit,
                    [CharacterCreationPurchaseCompletionBlockers.SpendLimit]));
                continue;
            }
            gear = proposedGear;
            lifestyles = proposedLifestyles;
            loadout = proposedLoadout;
            current = preview;
            results.Add(new(index, CharacterCreationPurchaseCompletionTargetOutcomes.Added, []));
        }

        var blockers = current.Blockers.ToList();
        if (results.Any(row => row.Outcome is not (CharacterCreationPurchaseCompletionTargetOutcomes.Added
                or CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent)))
            blockers.Add(CharacterCreationPurchaseCompletionBlockers.TargetsUnfulfilled);
        if (request.StartingLifestyleId is not null && current.StartingLifestyleId != request.StartingLifestyleId)
            blockers.Add(CharacterCreationLifestylesCompletionSchemas.StartingLifestyleRequired);
        string[] normalized = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var plan = new CharacterCreationPurchaseCompletionPlan(request, gear.ToArray(), lifestyles.ToArray(),
            current.StartingLifestyleId, current, results.ToArray(), current.Gear.Resources.Budget.KnownPurchaseCost - originalCost,
            results.Any(row => row.Outcome == CharacterCreationPurchaseCompletionTargetOutcomes.Added)
                || request.TotalKarmaInvestment != (inputs.Gear.Resources.PendingDraft?.KarmaInvestment ?? 0),
            normalized.Length == 0, evaluations, normalized, string.Empty) { Loadout = loadout };
        plan = plan with { PlanDigest = CharacterCreationPurchaseCompletionRules.ComputePlanDigest(plan) };
        cancellationToken.ThrowIfCancellationRequested();
        return new(plan.IsPurchasePlanComplete ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, Detach(plan), normalized);

        CharacterCreationLifestyleResult<CharacterCreationLifestylesCompletionProjection> Evaluate(
            IReadOnlyList<CharacterCreationGearSelection> basket, IReadOnlyList<CharacterCreationLifestyleConfiguration> homes,
            CharacterCreationLoadoutSelections? equipment)
        {
            CharacterCreationLoadoutCompletionProjection? projection = null;
            if (loadoutInputs is not null)
            {
                var priced = CharacterCreationLoadoutCompletionService.Preview(loadoutInputs, equipment!);
                if (priced.Value is null) return new(CharacterCreationLifestyleOutcomes.Blocked, null, priced.Blockers);
                projection = priced.Value;
            }
            return CharacterCreationLifestylesService.PreviewCompletion(inputs, magicInputs, request.AttributeAllocations,
                request.MagicSelections, request.QualityOptionIds, request.TotalKarmaInvestment, basket, homes,
                homes.Any(row => row.LifestyleId == request.StartingLifestyleId) ? request.StartingLifestyleId : null,
                projection, cancellationToken);
        }
    }

    private static bool Admissible(CharacterCreationLifestylesCompletionProjection projection)
        => projection.Budget.IsExact && projection.Budget.Overspend == 0m
            && projection.Gear.Budget.IsExact && projection.Gear.Budget.Overspend == 0m
            && projection.Gear.Resources.Budget.IsExact && projection.Gear.Resources.Budget.Overspend == 0m
            && projection.Blockers.All(blocker => blocker == CharacterCreationLifestylesCompletionSchemas.StartingLifestyleRequired);

    private static bool IsFundsBlocker(string blocker)
        => blocker is CharacterCreationLifestylesBlockers.InsufficientFunds
            or CharacterCreationGearBlockers.InsufficientFunds or CharacterCreationResourcesBlockers.InsufficientNuyen;

    private static bool ValidRequest(CharacterCreationPurchaseCompletionRequest request)
        => request.AttributeAllocations is not null && request.AttributeAllocations.Count <= 32
            && request.AttributeAllocations.All(row => row is not null)
            && request.MagicSelections is { AdeptPowers: not null, Spells: not null, ComplexForms: not null }
            && request.MagicSelections.AdeptPowers.All(row => row is not null)
            && request.MagicSelections.Spells.All(row => row is not null)
            && request.MagicSelections.ComplexForms.All(row => row is not null)
            && request.QualityOptionIds is not null && request.QualityOptionIds.Count <= 65_536
            && request.QualityOptionIds.All(row => !string.IsNullOrWhiteSpace(row))
            && request.Targets is not null && request.Targets.Count <= MaximumTargets
            && request.Targets.All(target => target is not null
                && (target.Gear is null ? 0 : 1) + (target.Lifestyle is null ? 0 : 1)
                    + (target.Weapon is null ? 0 : 1) + (target.Armor is null ? 0 : 1) == 1
                && (target.Gear is null || !string.IsNullOrWhiteSpace(target.Gear.OptionId) && target.Gear.Quantity > 0)
                && (target.Lifestyle is null || target.Lifestyle.LifestyleId != Guid.Empty)
                && (target.Weapon is null || !string.IsNullOrWhiteSpace(target.Weapon.OptionId) && target.Weapon.Quantity is >= 1 and <= 20)
                && (target.Armor is null || !string.IsNullOrWhiteSpace(target.Armor.OptionId) && target.Armor.Quantity is >= 1 and <= 20))
            && request.NuyenToReserve >= 0m && request.MaximumAdditionalSpend >= 0m
            && request.TotalKarmaInvestment >= 0 && request.MaximumEvaluations is >= 1 and <= MaximumEvaluations
            && request.StartingLifestyleId != Guid.Empty;

    private static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;

    private static CharacterCreationFoundationResult<CharacterCreationPurchaseCompletionPlan> Reject(string outcome, string blocker)
        => new(outcome, null, [blocker]);
}
