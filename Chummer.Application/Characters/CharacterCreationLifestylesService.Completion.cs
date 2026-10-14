using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationLifestylesService
{
    /// <summary>
    /// Read the actual source and saved history before or after ordinary purchase
    /// drafts exist. The ordinary Lifestyle editor retains its saved-graph gate.
    /// </summary>
    public CharacterCreationLifestyleResult<CharacterCreationLifestylesCompletionInputs> LoadCompletionInputs(
        CharacterCreationLifestylesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_completionQueries is null)
            return Blocked<CharacterCreationLifestylesCompletionInputs>(CharacterCreationLifestyleOutcomes.Blocked,
                CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        var read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationLifestylesCompletionInputs>(read);
        using ICharacterSourceDataResolverOperationScope? scope =
            (_sourceData as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var resolver = scope ?? _sourceData;
        var source = resolver.TryCreateContext(workspace.Document.Content);
        if (source is null || !source.TryResolveCreationLifestylesAuthority(out var authority)
            || !CharacterCreationLifestylesRules.IsValidAuthority(authority))
            return Blocked<CharacterCreationLifestylesCompletionInputs>(CharacterCreationLifestyleOutcomes.Blocked,
                CharacterCreationLifestylesBlockers.AuthorityUnavailable);

        var gearResult = new CharacterCreationGearService(_workspaceStore, resolver)
            .LoadCompletionInputsFromObservation(workspace);
        if (gearResult.Value is not { } gear)
            return new(CharacterCreationLifestyleOutcomes.Blocked, null,
                Normalize(gearResult.Blockers.Append(CharacterCreationLifestylesCompletionSchemas.UpstreamBudgetUnavailable)));
        var qualitiesResult = new CharacterCreationQualitiesService(_workspaceStore, resolver,
            new CharacterCreationPrerequisiteService(_workspaceStore, _completionQueries, resolver),
            new CharacterCreationAttributesService(_workspaceStore, resolver)).LoadCompletionInputs(new(workspace.Id));
        if (qualitiesResult.Value is not { } qualities)
            return new(CharacterCreationLifestyleOutcomes.Blocked, null,
                Normalize(qualitiesResult.Blockers.Append(CharacterCreationLifestylesCompletionSchemas.UpstreamBudgetUnavailable)));

        IReadOnlyList<CharacterCreationTalentQualitySource> talentSources = [];
        if (qualities.PrerequisiteDraft.TalentSelection is not { GrantedQualities: not null } selectedTalent
            || selectedTalent.GrantedQualities.Count != 0
                && (!CharacterCreationTalentQualityGrants.TryResolveTalent(qualities.PrerequisiteDraft, source,
                        out _, out var talent)
                    || (talentSources = talent.GrantedQualitySources!) is null)
            || !AreCompletionGrantEffectsSupported(qualities.PrerequisiteDraft, talentSources))
            return Blocked<CharacterCreationLifestylesCompletionInputs>(CharacterCreationLifestyleOutcomes.Blocked,
                CharacterCreationLifestylesBlockers.UnsupportedSemantics);

        var binding = new CharacterCreationLifestyleBinding(workspace.Id, workspace.ContentRevision,
            workspace.ContentRevision, workspace.SavedRevision,
            CharacterCreationLifestyleReceiptLedgerIntegrity.ComputeContentDigest(workspace.Document.Content),
            workspace.Document.AuxiliaryStateDigest, authority.SourceDigest, CompletionRulesDigest(authority),
            authority.RuntimeDigest);
        var inputs = new CharacterCreationLifestylesCompletionInputs(CharacterCreationLifestylesCompletionSchemas.InputsV1,
            binding, authority, gear, qualities, gear.Resources.LifestylesDraft?.Lifestyles ?? [], string.Empty);
        inputs = inputs with { SnapshotDigest = CharacterCreationLifestylesCompletionRules.ComputeInputsDigest(inputs) };
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CharacterCreationLifestylesCompletionInputs>(CharacterCreationLifestyleOutcomes.Conflict,
                CharacterCreationLifestylesCompletionSchemas.SnapshotConflict);
        // Source admission is repeated after the dependent readers. This remains
        // a single synchronous observation, never an enduring source capability.
        if (!source.TryResolveCreationLifestylesAuthority(out var after)
            || !SameCompletionDigest(authority.AuthorityDigest, after.AuthorityDigest))
            return Blocked<CharacterCreationLifestylesCompletionInputs>(CharacterCreationLifestyleOutcomes.Conflict,
                CharacterCreationLifestylesBlockers.StaleSourceDigest);
        return new(CharacterCreationLifestyleOutcomes.Available, DetachCompletion(inputs), []);
    }

    /// <summary>
    /// Recompute every upstream purchase and the complete Lifestyle list. Costs
    /// are paid from creation funding, never from the later starting-cash roll.
    /// </summary>
    public static CharacterCreationLifestyleResult<CharacterCreationLifestylesCompletionProjection> PreviewCompletion(
        CharacterCreationLifestylesCompletionInputs inputs,
        CharacterCreationMagicResonanceCompletionInputs magicInputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationMagicResonanceSelections magicSelections,
        IReadOnlyList<string> qualityOptionIds,
        int totalKarmaInvestment,
        IReadOnlyList<CharacterCreationGearSelection> basket,
        IReadOnlyList<CharacterCreationLifestyleConfiguration> lifestyles,
        Guid? startingLifestyleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(magicInputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(magicSelections);
        ArgumentNullException.ThrowIfNull(qualityOptionIds);
        ArgumentNullException.ThrowIfNull(basket);
        ArgumentNullException.ThrowIfNull(lifestyles);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCreationKarmaLifestylesRules.TryFreeze(lifestyles, out var frozen))
            return Blocked<CharacterCreationLifestylesCompletionProjection>(CharacterCreationLifestyleOutcomes.Invalid,
                CharacterCreationLifestylesBlockers.InvalidMutation);
        inputs = DetachCompletion(inputs);
        magicInputs = DetachCompletion(magicInputs);
        attributeAllocations = attributeAllocations.ToArray();
        magicSelections = DetachCompletion(magicSelections);
        qualityOptionIds = qualityOptionIds.ToArray();
        basket = basket.ToArray();
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CharacterCreationLifestylesCompletionProjection>(CharacterCreationLifestyleOutcomes.Blocked,
                CharacterCreationLifestylesBlockers.AuthorityUnavailable);

        var gearResult = CharacterCreationGearService.PreviewCompletion(inputs.Gear, magicInputs, inputs.Qualities,
            attributeAllocations, magicSelections, qualityOptionIds, totalKarmaInvestment, basket, cancellationToken);
        if (gearResult.Value is not { } gear)
            return new(gearResult.Outcome == CharacterCreationGearOutcomes.Conflict
                    ? CharacterCreationLifestyleOutcomes.Conflict : CharacterCreationLifestyleOutcomes.Blocked,
                null, Normalize(gearResult.Blockers.Append(CharacterCreationLifestylesCompletionSchemas.UpstreamBudgetUnavailable)));
        // Insufficient funds is still an exactly source-priced full basket.
        // Any other basket failure must not be repriced as a valid partial list.
        if (gear.Budget.Blockers.Any(blocker => blocker != CharacterCreationGearBlockers.InsufficientFunds))
            return new(CharacterCreationLifestyleOutcomes.Blocked, null, gear.Blockers);
        var qualityResult = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs.Qualities,
            attributeAllocations, qualityOptionIds, cancellationToken);
        if (qualityResult.Value is not { } qualities)
            return new(CharacterCreationLifestyleOutcomes.Blocked, null, qualityResult.Blockers);
        if (!AreCompletionGrantEffectsSupported(inputs.Qualities.PrerequisiteDraft,
                magicInputs.SelectedTalent.GrantedQualitySources ?? [])
            || !TryCompletionAuthority(inputs.Authority, qualities, out var authority))
            return Blocked<CharacterCreationLifestylesCompletionProjection>(CharacterCreationLifestyleOutcomes.Blocked,
                CharacterCreationLifestylesBlockers.UnsupportedSemantics);

        var rows = new List<CharacterCreationLifestyleProjection>(frozen.Length);
        var findings = new List<string>();
        decimal lifestyleCost = 0m;
        foreach (var configuration in frozen)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CharacterCreationLifestylesRules.TryProject(configuration, authority, out var row, out var blockers))
                return new(CharacterCreationLifestyleOutcomes.Blocked, null, Normalize(gear.Blockers.Concat(blockers)));
            try { lifestyleCost = checked(lifestyleCost + row.Economics.TotalCost); }
            catch (OverflowException)
            {
                return Blocked<CharacterCreationLifestylesCompletionProjection>(CharacterCreationLifestyleOutcomes.Blocked,
                    CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            }
            rows.Add(row);
        }
        if (frozen.Length == 0 ? startingLifestyleId is not null
            : startingLifestyleId is null || frozen.All(row => row.LifestyleId != startingLifestyleId))
            findings.Add(CharacterCreationLifestylesCompletionSchemas.StartingLifestyleRequired);

        CharacterCreationResourcesCompletionProjection funding;
        try
        {
            funding = CharacterCreationResourcesService.RepriceCompletionPurchases(inputs.Gear.Resources,
                gear.Resources, gear.Budget.BasketCost, lifestyleCost);
        }
        catch (OverflowException)
        {
            return Blocked<CharacterCreationLifestylesCompletionProjection>(CharacterCreationLifestyleOutcomes.Blocked,
                CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        }
        // Rebuild rather than keep the superseded old-Lifestyle funding shortage.
        // All upstream rules and exact Gear basket failures are retained.
        string[] gearBlockers = Normalize(funding.Blockers.Concat(gear.Budget.Blockers));
        gear = gear with { Resources = funding, Blockers = gearBlockers, ProposalDigest = string.Empty };
        gear = gear with { ProposalDigest = CharacterCreationGearCompletionRules.ComputeProjectionDigest(gear) };
        findings.AddRange(gearBlockers);
        decimal total = gear.Budget.RemainingNuyen;
        decimal overspend = lifestyleCost > total ? lifestyleCost - total : 0m;
        if (overspend > 0m) findings.Add(CharacterCreationLifestylesBlockers.InsufficientFunds);
        string[] anchors = authority.SourceAnchorIds.Concat(rows.SelectMany(row => row.SourceAnchorIds))
            .Concat(gear.SourceAnchorIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // As in the ordinary Lifestyle budget, exact pricing is distinct from
        // affordability and overall readiness; Overspend/Blockers expose both.
        var budget = new CharacterCreationLifestyleBudget(total, lifestyleCost,
            Math.Max(0m, total - lifestyleCost), overspend, true,
            overspend > 0m ? [CharacterCreationLifestylesBlockers.InsufficientFunds] : [], anchors);
        string[] normalized = Normalize(findings);
        var projection = new CharacterCreationLifestylesCompletionProjection(
            CharacterCreationLifestylesCompletionSchemas.ProjectionV1, inputs.Binding, inputs.SnapshotDigest,
            gear, qualities, authority, rows.ToArray(), startingLifestyleId, budget, anchors, normalized, string.Empty);
        projection = projection with
        {
            ProposalDigest = CharacterCreationLifestylesCompletionRules.ComputeProjectionDigest(projection)
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new(normalized.Length == 0 ? CharacterCreationLifestyleOutcomes.Available : CharacterCreationLifestyleOutcomes.Blocked,
            DetachCompletion(projection), normalized);
    }

    private static bool IsValidCompletionInputs(CharacterCreationLifestylesCompletionInputs inputs)
    {
        if (inputs.Schema != CharacterCreationLifestylesCompletionSchemas.InputsV1
            || inputs.Binding is not { } binding || inputs.Authority is not { } authority
            || inputs.Gear is not { State.Binding: { } gear,
                Resources: { Authority: not null, PrerequisiteDraft: not null } resources }
            || inputs.Qualities is not { Binding: { } qualities, PrerequisiteDraft: { } prerequisite }
            || authority.Blockers is null || authority.SourceAnchorIds is null
            || authority.LifestyleOptions is null || authority.QualityOptions is null
            || authority.LifestyleOptions.Any(option => option is null || option.Blockers is null
                || option.SourceAnchorIds is null || option.BuiltInQualities is null)
            || authority.QualityOptions.Any(option => option is null || option.Blockers is null
                || option.SourceAnchorIds is null || option.AllowedFreeLifestyleNames is null)
            || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
            || binding.WorkspaceRevision != binding.ContentRevision || binding.ContentRevision <= 0
            || binding.ContentRevision != binding.SavedRevision
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(binding.ContentDigest)
            || binding.AuxiliaryStateDigest is not { Length: 64 }
            || binding.AuxiliaryStateDigest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || !SameCompletionDigest(binding.SourceDigest, authority.SourceDigest)
            || !SameCompletionDigest(binding.RulesDigest, CompletionRulesDigest(authority))
            || !SameCompletionDigest(binding.RuntimeDigest, authority.RuntimeDigest)
            || !SameCompletionDigest(inputs.SnapshotDigest, CharacterCreationLifestylesCompletionRules.ComputeInputsDigest(inputs))
            || binding.WorkspaceId != gear.WorkspaceId || binding.WorkspaceId != qualities.WorkspaceId
            || binding.ContentRevision != gear.ContentRevision || binding.ContentRevision != qualities.ContentRevision
            || binding.SavedRevision != gear.SavedRevision || binding.SavedRevision != qualities.SavedRevision
            || !SameCompletionDigest(binding.ContentDigest, gear.RawCharacterXmlDigest)
            || !SameCompletionDigest(binding.ContentDigest, qualities.RawCharacterXmlDigest)
            || !SameCompletionDigest(binding.AuxiliaryStateDigest, gear.AuxiliaryStateDigest)
            || !SameCompletionDigest(binding.AuxiliaryStateDigest, qualities.AuxiliaryStateDigest)
            || authority.SettingsProfileId != prerequisite.SettingsProfileId
            || authority.SettingsProfileId != resources.Authority.SettingsProfileId
            || !SameCompletionDigest(authority.ProfileDigest, resources.Authority.ProfileDigest)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(prerequisite, resources.PrerequisiteDraft)
            || !CharacterCreationKarmaLifestylesRules.TryFreeze(inputs.Lifestyles, out _)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs.Lifestyles,
                resources.LifestylesDraft?.Lifestyles ?? [])
            || !CharacterCreationResourcesService.TryGetRetainedCompletionLifestyleCost(resources, out _))
            return false;
        return true;
    }

    private static bool TryCompletionAuthority(CharacterCreationLifestylesAuthority original,
        CharacterCreationAttributeQualitiesPreview qualities, out CharacterCreationLifestylesAuthority effective)
    {
        effective = CharacterCreationLifestylesAuthority.Unavailable;
        if (!CharacterCreationLegacySourceProjector.TryProjectCompletionQualityEffects(qualities.Qualities.Selections,
                out var effects)) return false;
        int level = original.TrustFundLevel;
        foreach (var effect in effects)
        {
            if (effect.ImprovementType is "LifestyleCost" or "BasicLifestyleCost") return false;
            if (effect.ImprovementType != "TrustFund") continue;
            if (effect.ImprovedName.Length != 0 || effect.Value is < 1m or > 4m
                || effect.Value != decimal.Truncate(effect.Value) || level != 0) return false;
            level = (int)effect.Value;
        }
        var candidate = original with
        {
            TrustFundLevel = level,
            GmPolicyDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                original.GmPolicyDigest, OriginalAuthority = original.AuthorityDigest,
                Qualities = qualities.ProposalDigest, TrustFundLevel = level
            }),
            AuthorityDigest = string.Empty
        };
        effective = candidate with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(candidate) };
        return CharacterCreationLifestylesRules.IsValidAuthority(effective);
    }

    private static bool AreCompletionGrantEffectsSupported(CharacterCreationPrerequisiteDraft prerequisite,
        IReadOnlyList<CharacterCreationTalentQualitySource> talentSources)
        // Grant effects are not purchased selections. Admit only the exact
        // shared supported source grammar, never silently ignore a future
        // Lifestyle-changing (or unknown) racial/talent grant.
        => CharacterCreationLegacySourceProjector.AreCompletionGrantEffectsIndependent(prerequisite, talentSources);

    private static string CompletionRulesDigest(CharacterCreationLifestylesAuthority authority)
        => CharacterCreationLifestylesDigestForApplication(new
        {
            CharacterCreationLifestylesSchemas.RulesV1, authority.ProfileDigest, authority.GmPolicyDigest,
            CharacterCreationLifestyleSourceAnchors.All
        });

    private static bool SameCompletionDigest(string? left, string? right)
        => CharacterCreationGearRules.DigestsEqual(left, right);

    private static T DetachCompletion<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
