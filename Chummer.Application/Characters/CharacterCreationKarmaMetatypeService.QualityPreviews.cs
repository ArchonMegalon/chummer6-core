using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationKarmaMetatypeService
{
    public CharacterCreationFoundationResult<CharacterCreationKarmaQualityPreviews> PreviewQualitySelections(
        CharacterCreationKarmaQualityPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryFreezeQualityPreviewRequest(request, out request))
            return Blocked<CharacterCreationKarmaQualityPreviews>(CharacterCreationQualitiesBlockers.InvalidSelection);

        // This snapshot is admitted here, never supplied by the caller, and lives
        // only for this synchronous read. The owner-bound adapter holds one exact
        // owner lease; no context or quote is retained for a later operation.
        var binding = request.Binding;
        var loaded = Load(binding.WorkspaceId,
            includeSkills: request.SkillsSelection is not null || binding.SkillsCatalogDigest is not null || binding.SkillsPolicyDigest is not null,
            includeQualities: true,
            includeGear: request.GearSelections is not null || binding.GearAuthorityDigest is not null,
            includeLifestyles: request.LifestyleSelections is not null || binding.LifestylesAuthorityDigest is not null,
            includeMagic: request.MagicSelections is not null || binding.MagicAuthorityDigest is not null,
            out var admittedQualities, out var context);
        cancellationToken.ThrowIfCancellationRequested();
        if (loaded.Value is not { } state)
            return new(loaded.Outcome, null, loaded.Blockers);
        if (state.Binding != binding)
            return Blocked<CharacterCreationKarmaQualityPreviews>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);

        var results = new List<CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote>>();
        foreach (var ids in request.QualitySelections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Use the ordinary complete preview calculation: a quality can alter
            // skill eligibility, contacts, lifestyles, magic and the shared budget.
            var result = PreviewLoaded(state, admittedQualities, request.MetatypeOptionId,
                request.TalentOptionId, request.AttributeAllocations, request.SkillsSelection, request.ResourceKarmaInvestment,
                ids, request.GearSelections, request.ContactSelections, request.LifestyleSelections,
                request.StartingLifestyleId, request.MagicSelections, context!);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Outcome == CharacterCreationFoundationOutcomes.Conflict
                || result.Blockers.Contains(CharacterCreationKarmaMetatypeBlockers.StaleBinding))
                return new(result.Outcome, null, result.Blockers);
            results.Add(result);
        }
        return new(CharacterCreationFoundationOutcomes.Success, new(results.AsReadOnly()), []);
    }

    private static bool TryFreezeQualityPreviewRequest(CharacterCreationKarmaQualityPreviewRequest request,
        out CharacterCreationKarmaQualityPreviewRequest frozen)
    {
        frozen = request;
        try
        {
            if (request.QualitySelections is not { Count: > 0 and <= CharacterCreationKarmaQualityPreviewRequest.MaximumCandidates })
                return false;
            var candidates = request.QualitySelections.Take(CharacterCreationKarmaQualityPreviewRequest.MaximumCandidates + 1).ToArray();
            if (candidates.Length != request.QualitySelections.Count) return false;
            var selections = new List<IReadOnlyList<string>>();
            foreach (var ids in candidates)
            {
                if (!CharacterCreationKarmaQualitiesRules.TryFreeze(ids, out var selected)) return false;
                selections.Add(Array.AsReadOnly(selected));
            }
            CharacterCreationKarmaAttributeAllocation[]? attributes = null;
            if (request.AttributeAllocations is not null)
            {
                if (request.AttributeAllocations.Count > 13) return false;
                attributes = request.AttributeAllocations.Take(14).ToArray();
                if (!CharacterCreationKarmaAttributesRules.IsAllocationShape(attributes)) return false;
            }
            CharacterCreationKarmaSkillsSelection? skills = null;
            if (request.SkillsSelection is not null && !CharacterCreationKarmaSkillsRules.TryFreeze(request.SkillsSelection, out skills)) return false;
            CharacterCreationGearSelection[]? gear = null;
            if (request.GearSelections is not null && !CharacterCreationKarmaGearRules.TryFreeze(request.GearSelections, out gear)) return false;
            CharacterCreationKarmaContactSelection[]? contacts = null;
            if (request.ContactSelections is not null && !CharacterCreationKarmaContactsRules.TryFreeze(request.ContactSelections, out contacts)) return false;
            CharacterCreationLifestyleConfiguration[]? lifestyles = null;
            if (request.LifestyleSelections is not null && !CharacterCreationKarmaLifestylesRules.TryFreeze(request.LifestyleSelections, out lifestyles)) return false;
            CharacterCreationMagicResonanceSelections? magic = null;
            if (request.MagicSelections is not null && !CharacterCreationKarmaMagicSelectionRules.TryFreeze(request.MagicSelections, out magic)) return false;
            frozen = request with
            {
                QualitySelections = selections.AsReadOnly(), AttributeAllocations = attributes,
                SkillsSelection = skills, GearSelections = gear, ContactSelections = contacts,
                LifestyleSelections = lifestyles, MagicSelections = magic
            };
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
