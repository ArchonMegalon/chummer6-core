using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Rulesets.Sr6;

/// <summary>SR6 foundation rules; these choices do not apply character effects.</summary>
public static class Sr6CreationFoundationRules
{
    public const string CoreSourceAnchor = "sr6_core_de_2024:p65-67";
    public const string CoreSourceSha256 = "104dd5cc0f167232c3bc0f6453b389d9114dd7df483345e5b1211fda667bf023";

    public static IReadOnlyList<Sr6CreationFoundationOption> Metatypes() =>
    [
        new("human", ["C", "D", "E"]), new("elf", ["B", "C", "D", "E"]),
        new("dwarf", ["A", "B", "C", "D", "E"]), new("ork", ["A", "B", "C", "D", "E"]),
        new("troll", ["A", "B", "C", "D", "E"])
    ];

    public static IReadOnlyList<Sr6CreationFoundationOption> Talents() =>
    [
        new("mundane", ["E"]), new("magician", ["A", "B", "C", "D"]),
        new("aspected-magician", ["A", "B", "C", "D"]), new("adept", ["A", "B", "C", "D"]),
        new("mystic-adept", ["A", "B", "C", "D"]), new("technomancer", ["A", "B", "C", "D"])
    ];

    private static string AuthorityDigest(CharacterCreationBootstrapBinding bootstrap)
        => bootstrap.BuildMethod == Sr6CharacterCreationBuildMethods.PointBuy
            ? Sr6CreationPointBuyRules.AuthorityDigest(bootstrap) : Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.priority-foundation-authority.v1",
            bootstrap.BindingDigest, CoreSourceAnchor, CoreSourceSha256,
            Metatypes = Metatypes(), Talents = Talents(),
            Rows = "ABCDE".Select(new Sr6CharacterCreationProvider().GetPriorityRow).ToArray(),
            BaseMagicAndResonanceByRank = new[] { 4, 3, 2, 1, 0 }, AspectedBonus = 1
        });

    public static CharacterCreationFoundationResult<Sr6CreationFoundationState> Load(WorkspaceStoredDocument saved)
    {
        var state = saved.Document.AuxiliaryState;
        var bootstrap = state.CharacterCreationBootstrapBinding;
        if (saved.Document.RulesetId != RulesetDefaults.Sr6 || bootstrap is null
            || bootstrap.BuildMethod is not (Sr6CharacterCreationBuildMethods.Priority or Sr6CharacterCreationBuildMethods.SumToTen or Sr6CharacterCreationBuildMethods.PointBuy)
            || !Equals(state with { Sr6CreationFoundationDecisions = null },
                new WorkspaceDocumentAuxiliaryState(CharacterCreationBootstrapBinding: bootstrap)))
            return Blocked<Sr6CreationFoundationState>(Sr6CreationFoundationBlockers.PendingDraftRequired);

        // Only this SR6 draft ledger may accompany the unchanged pending XML.
        var bootstrapDocument = saved.Document with { State = saved.Document.State with
            { AuxiliaryState = state with { Sr6CreationFoundationDecisions = null } } };
        if (!new Sr6CharacterCreationBootstrapProvider().IsCurrent(saved.Id, bootstrapDocument))
            return Blocked<Sr6CreationFoundationState>(Sr6CreationFoundationBlockers.StaleBinding);
        if (!Sr6CreationFoundationIntegrity.IsValidLedger(saved.Id, saved.ContentRevision, state))
            return Blocked<Sr6CreationFoundationState>(Sr6CreationFoundationBlockers.HistoryInvalid);
        foreach (var decision in state.Sr6CreationFoundationDecisions ?? [])
        {
            var expected = Preview(bootstrap, decision.Command.Binding, decision.Command.Selection).Value;
            if (expected is null || expected.PreviewDigest != decision.Preview.PreviewDigest)
                return Blocked<Sr6CreationFoundationState>(Sr6CreationFoundationBlockers.HistoryInvalid);
        }
        var binding = new Sr6CreationFoundationBinding(saved.Id, saved.ContentRevision, saved.SavedRevision,
            saved.Document.AuxiliaryStateDigest, bootstrap.BindingDigest, AuthorityDigest(bootstrap));
        var selected = state.Sr6CreationFoundationDecisions?.LastOrDefault()?.Preview;
        bool pointBuy = bootstrap.BuildMethod == Sr6CharacterCreationBuildMethods.PointBuy;
        return Success(new Sr6CreationFoundationState(binding, bootstrap.BuildMethod,
            pointBuy ? Metatypes().Select(row => row with { AllowedRanks = [] }).ToArray() : Metatypes(),
            pointBuy ? Talents().Select(row => row with { AllowedRanks = [] }).ToArray() : Talents(), selected)
        {
            AttributeOptions = selected is null ? null : Sr6CreationAttributeRules.Options(selected),
            SkillOptions = selected is null ? null : Sr6CreationSkillRules.Options(selected, selected.Selection.Skills?.AspectedSkillId),
            KnowledgePointBudget = selected?.Attributes is null ? null : Sr6CreationKarmaRules.AttributeRating(selected, "Logic"),
            PointBuyLimits = pointBuy ? Sr6CreationPointBuyRules.Limits() : null,
            TalentOptions = selected is null ? null : Sr6CreationTalentRules.Options(selected),
            ComplexFormOptions = selected is { Selection.TalentId: "technomancer", TalentAllocation: not null }
                ? Sr6CreationComplexFormRules.Catalog() : null,
            SpellOptions = selected is null ? null : Sr6CreationSpellRules.Options(selected),
            AdeptPowerOptions = selected is null ? null : Sr6CreationAdeptPowerRules.Options(selected),
            KarmaOptions = selected is null ? null : Sr6CreationKarmaRules.Options(selected),
            KarmaSpecializationOptions = selected is null ? null : Sr6CreationKarmaRules.SpecializationOptions(selected),
            KarmaKnowledgeOptions = selected is null ? null : Sr6CreationKarmaKnowledgeRules.Options(selected)
        });
    }

    public static CharacterCreationFoundationResult<Sr6CreationFoundationPreview> Preview(
        CharacterCreationBootstrapBinding bootstrap, Sr6CreationFoundationBinding binding,
        Sr6CreationFoundationSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.ValidBinding(binding)
            || !CharacterCreationBootstrapBindingDigest.IsValid(bootstrap)
            || bootstrap.RulesetId != RulesetDefaults.Sr6 || binding.WorkspaceId != bootstrap.WorkspaceId
            || binding.BootstrapBindingDigest != bootstrap.BindingDigest || binding.AuthorityDigest != AuthorityDigest(bootstrap))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationFoundationBlockers.StaleBinding);
        if (selection?.Skills is { } requestedSkills
            && !Sr6CreationFoundationIntegrity.TryFreezeSkills(requestedSkills, out _))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationSkillBlockers.InvalidAllocation);
        if (selection?.Knowledge is { } requestedKnowledge
            && !Sr6CreationFoundationIntegrity.TryFreezeKnowledge(requestedKnowledge, out _))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationKnowledgeBlockers.InvalidSelection);
        if (selection?.TalentAllocation is { SelectedPowerPoints: < 0 or > 6 })
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationTalentBlockers.InvalidSelection);
        if (selection?.ComplexForms is { } requestedForms
            && !Sr6CreationFoundationIntegrity.TryFreezeComplexForms(requestedForms, out _))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationComplexFormBlockers.InvalidSelection);
        if (selection?.Spells is { } requestedSpells
            && !Sr6CreationFoundationIntegrity.TryFreezeSpells(requestedSpells, out _))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationSpellBlockers.InvalidSelection);
        if (selection?.AdeptPowers is { } requestedPowers
            && !Sr6CreationFoundationIntegrity.TryFreezeAdeptPowers(requestedPowers, out _))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationAdeptPowerBlockers.InvalidSelection);
        if (selection?.Karma is { } requestedKarma
            && !Sr6CreationFoundationIntegrity.TryFreezeKarma(requestedKarma, out _))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationKarmaBlockers.InvalidSelection);
        bool pointBuy = bootstrap.BuildMethod == Sr6CharacterCreationBuildMethods.PointBuy;
        if (pointBuy != (selection?.PointBuy is not null))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationPointBuyBlockers.MethodMismatch);
        if (pointBuy && (!Sr6CreationFoundationIntegrity.ValidPointBuyShape(selection!.PointBuy) || selection.Assignments is not { Count: 0 }))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationPointBuyBlockers.InvalidSelection);
        if (!Sr6CreationFoundationIntegrity.TryFreezeSelection(selection, out selection))
            return Blocked<Sr6CreationFoundationPreview>(pointBuy ? Sr6CreationPointBuyBlockers.InvalidSelection : Sr6CreationPriorityBlockers.CategoriesInvalid);

        Sr6CreationFoundationPreview preview;
        if (pointBuy)
        {
            var pools = Sr6CreationPointBuyRules.Evaluate(bootstrap, binding, selection);
            if (pools.Value is null) return pools;
            preview = pools.Value;
        }
        else
        {
            // Availability comes from the exact persisted profile, never a client flag.
            bool companion = bootstrap.SettingsProfileId == Sr6CharacterCreationBootstrapProfiles.SumToTen
                && bootstrap.SourceAnchorIds.Contains("sr6_schattenkompendium_2022:p28", StringComparer.Ordinal);
            var priorities = new Sr6CharacterCreationProvider().EvaluatePriorities(
                new(RulesetDefaults.Sr6, bootstrap.BuildMethod, selection.Assignments), companion);
            if (!priorities.IsValid || priorities.Budget is null)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, priorities.Blockers);
            string heritage = selection.Assignments.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage).Rank;
            string talent = priorities.Budget.MagicResonanceRank!;
            if (!Metatypes().Single(item => item.Id == selection.MetatypeId).AllowedRanks.Contains(heritage, StringComparer.Ordinal))
                return Blocked<Sr6CreationFoundationPreview>(Sr6CreationFoundationBlockers.MetatypeUnavailable);
            if (!Talents().Single(item => item.Id == selection.TalentId).AllowedRanks.Contains(talent, StringComparer.Ordinal))
                return Blocked<Sr6CreationFoundationPreview>(Sr6CreationFoundationBlockers.TalentUnavailable);
            int baseRating = 'E' - talent[0];
            int magic = selection.TalentId is "mundane" or "technomancer" ? 0
                : baseRating + (selection.TalentId == "aspected-magician" ? 1 : 0);
            int resonance = selection.TalentId == "technomancer" ? baseRating : 0;
            string[] anchors = companion ? [CoreSourceAnchor, "sr6_schattenkompendium_2022:p28"] : [CoreSourceAnchor];
            preview = new Sr6CreationFoundationPreview(binding, selection, priorities.Budget, magic, resonance, anchors, string.Empty);
        }
        if (selection.Attributes is { } allocation)
        {
            var attributes = Sr6CreationAttributeRules.Evaluate(preview, allocation);
            if (attributes.Value is null)
                return new(attributes.Outcome, null, attributes.Blockers);
            preview = preview with { Attributes = attributes.Value,
                SourceAnchorIds = preview.SourceAnchorIds.Concat(attributes.Value.SourceAnchorIds).Distinct(StringComparer.Ordinal).ToArray() };
        }
        if (selection.Skills is { } skillSelection)
        {
            var skills = Sr6CreationSkillRules.Evaluate(preview, skillSelection);
            if (skills.Value is null) return new(skills.Outcome, null, skills.Blockers);
            preview = preview with { Skills = skills.Value,
                SourceAnchorIds = [.. preview.SourceAnchorIds, Sr6CreationSkillRules.SourceAnchor] };
        }
        if (selection.Karma is { } karmaSelection)
        {
            var karma = Sr6CreationKarmaRules.Evaluate(preview, karmaSelection);
            if (karma.Value is null) return new(karma.Outcome, null, karma.Blockers);
            preview = preview with { Karma = karma.Value,
                SourceAnchorIds = preview.SourceAnchorIds.Concat(karma.Value.SourceAnchorIds).Distinct(StringComparer.Ordinal).ToArray() };
        }
        if (selection.TalentAllocation is { } talentSelection)
        {
            var talent = Sr6CreationTalentRules.Evaluate(preview, talentSelection);
            if (talent.Value is null) return new(talent.Outcome, null, talent.Blockers);
            preview = preview with { TalentAllocation = talent.Value,
                SourceAnchorIds = preview.SourceAnchorIds.Concat(talent.Value.SourceAnchorIds).Distinct(StringComparer.Ordinal).ToArray() };
            if (preview.PointBuy is { } points)
            {
                int spent = points.PointsSpent + talent.Value.PowerPointCharacterPointCost;
                preview = preview with { PointBuy = points with { PointsSpent = spent,
                    PointsRemaining = points.CharacterPoints - spent, AllCharacterPointsSpent = spent == points.CharacterPoints,
                    PowerPointCost = talent.Value.PowerPointCharacterPointCost } };
            }
        }
        if (selection.ComplexForms is { } formSelection)
        {
            var forms = Sr6CreationComplexFormRules.Evaluate(preview, formSelection);
            if (forms.Value is null) return new(forms.Outcome, null, forms.Blockers);
            preview = preview with { ComplexForms = forms.Value,
                SourceAnchorIds = [.. preview.SourceAnchorIds, Sr6CreationComplexFormRules.SourceAnchor] };
            if (preview.PointBuy is { } points)
            {
                int spent = points.PointsSpent + forms.Value.CharacterPointCost;
                preview = preview with { PointBuy = points with { PointsSpent = spent,
                    PointsRemaining = points.CharacterPoints - spent, AllCharacterPointsSpent = spent == points.CharacterPoints,
                    ComplexFormCost = forms.Value.CharacterPointCost } };
            }
        }
        if (selection.Spells is { } spellSelection)
        {
            var spells = Sr6CreationSpellRules.Evaluate(preview, spellSelection);
            if (spells.Value is null) return new(spells.Outcome, null, spells.Blockers);
            preview = preview with { Spells = spells.Value,
                SourceAnchorIds = [.. preview.SourceAnchorIds, Sr6CreationSpellRules.SourceAnchor] };
            if (preview.PointBuy is { } points)
            {
                int spent = points.PointsSpent + spells.Value.CharacterPointCost;
                preview = preview with { PointBuy = points with { PointsSpent = spent,
                    PointsRemaining = points.CharacterPoints - spent, AllCharacterPointsSpent = spent == points.CharacterPoints,
                    SpellCost = spells.Value.CharacterPointCost } };
            }
        }
        if (selection.AdeptPowers is { } powerSelection)
        {
            var powers = Sr6CreationAdeptPowerRules.Evaluate(preview, powerSelection);
            if (powers.Value is null) return new(powers.Outcome, null, powers.Blockers);
            preview = preview with { AdeptPowers = powers.Value,
                SourceAnchorIds = [.. preview.SourceAnchorIds, .. powers.Value.SourceAnchorIds] };
        }
        if (selection.Knowledge is { } knowledgeSelection)
        {
            var knowledge = Sr6CreationKnowledgeRules.Evaluate(preview, knowledgeSelection);
            if (knowledge.Value is null) return new(knowledge.Outcome, null, knowledge.Blockers);
            preview = preview with { Knowledge = knowledge.Value,
                SourceAnchorIds = preview.SourceAnchorIds.Append(Sr6CreationKnowledgeRules.SourceAnchor).Distinct(StringComparer.Ordinal).ToArray() };
        }
        return Success(preview with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(preview) });
    }

    public static CharacterCreationFoundationResult<Sr6CreationFoundationCommit>? Lookup(
        WorkspaceStoredDocument saved, Sr6CreationFoundationConfirmRequest request)
    {
        if (Load(saved).Value is null)
            return Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.HistoryInvalid);
        var prior = saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions?
            .SingleOrDefault(item => item.Command.OperationId == request.OperationId);
        if (prior is null) return null;
        return saved.CanReplayReceipt(prior.CommittedContentRevision)
               && Sr6CreationFoundationIntegrity.Digest(prior.Command) == Sr6CreationFoundationIntegrity.Digest(request)
            ? Success(new Sr6CreationFoundationCommit(prior, true))
            : Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.OperationConflict);
    }

    public static bool TryBuild(WorkspaceStoredDocument saved, Sr6CreationFoundationConfirmRequest request,
        out WorkspaceDocument document, out Sr6CreationFoundationDecision decision)
    {
        document = null!;
        decision = null!;
        if (!Sr6CreationFoundationIntegrity.TryFreezeRequest(request, out request)
            || Load(saved).Value is not { } current || current.Binding != request.Binding
            || saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions?.Count >= Sr6CreationFoundationIntegrity.MaximumDecisions)
            return false;
        var preview = Preview(saved.Document.AuxiliaryState.CharacterCreationBootstrapBinding!, current.Binding, request.Selection).Value;
        if (preview is null || preview.PreviewDigest != request.PreviewDigest) return false;
        var unsigned = new Sr6CreationFoundationDecision(Sr6CreationFoundationDecision.SchemaV1, request, preview,
            saved.ContentRevision + 1, string.Empty);
        decision = unsigned with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(unsigned) };
        document = saved.Document with { State = saved.Document.State with { AuxiliaryState = saved.Document.AuxiliaryState with
            { Sr6CreationFoundationDecisions = [.. saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions ?? [], decision] } } };
        return Sr6CreationFoundationIntegrity.IsValidLedger(saved.Id, decision.CommittedContentRevision, document.AuxiliaryState);
    }

    public static CharacterCreationFoundationResult<T> Blocked<T>(string blocker) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
    private static CharacterCreationFoundationResult<T> Success<T>(T value) where T : class
        => new(CharacterCreationFoundationOutcomes.Success, value, []);
}
