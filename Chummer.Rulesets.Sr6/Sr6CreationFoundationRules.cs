using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Rulesets.Sr6;

/// <summary>Priority/Sum-to-Ten foundation rules; these choices do not apply character effects.</summary>
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
        => Sr6CreationFoundationIntegrity.Digest(new
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
            || bootstrap.BuildMethod is not (Sr6CharacterCreationBuildMethods.Priority or Sr6CharacterCreationBuildMethods.SumToTen)
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
        return Success(new Sr6CreationFoundationState(binding, bootstrap.BuildMethod, Metatypes(), Talents(), selected)
        {
            AttributeOptions = selected is null ? null : Sr6CreationAttributeRules.Options(selected),
            SkillOptions = selected is null ? null : Sr6CreationSkillRules.Options(selected, selected.Selection.Skills?.AspectedSkillId),
            KnowledgePointBudget = selected?.Attributes?.Values.Single(row => row.AttributeId == "Logic").Value
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
        if (!Sr6CreationFoundationIntegrity.TryFreezeSelection(selection, out selection))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationPriorityBlockers.CategoriesInvalid);

        // Availability comes from the exact persisted profile, never a client flag.
        bool companion = bootstrap.SettingsProfileId == Sr6CharacterCreationBootstrapProfiles.SumToTen
            && bootstrap.SourceAnchorIds.Contains("sr6_schattenkompendium_2022:p28", StringComparer.Ordinal);
        var priorities = new Sr6CharacterCreationProvider().EvaluatePriorities(
            new(RulesetDefaults.Sr6, bootstrap.BuildMethod, selection.Assignments), companion);
        if (!priorities.IsValid || priorities.Budget is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, priorities.Blockers);
        string heritage = selection.Assignments.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage).Rank;
        string talent = priorities.Budget.MagicResonanceRank;
        if (!Metatypes().Single(item => item.Id == selection.MetatypeId).AllowedRanks.Contains(heritage, StringComparer.Ordinal))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationFoundationBlockers.MetatypeUnavailable);
        if (!Talents().Single(item => item.Id == selection.TalentId).AllowedRanks.Contains(talent, StringComparer.Ordinal))
            return Blocked<Sr6CreationFoundationPreview>(Sr6CreationFoundationBlockers.TalentUnavailable);
        int baseRating = 'E' - talent[0];
        int magic = selection.TalentId is "mundane" or "technomancer" ? 0
            : baseRating + (selection.TalentId == "aspected-magician" ? 1 : 0);
        int resonance = selection.TalentId == "technomancer" ? baseRating : 0;
        string[] anchors = companion ? [CoreSourceAnchor, "sr6_schattenkompendium_2022:p28"] : [CoreSourceAnchor];
        var preview = new Sr6CreationFoundationPreview(binding, selection, priorities.Budget, magic, resonance, anchors, string.Empty);
        if (selection.Attributes is { } allocation)
        {
            var attributes = Sr6CreationAttributeRules.Evaluate(preview, allocation);
            if (attributes.Value is null)
                return new(attributes.Outcome, null, attributes.Blockers);
            preview = preview with { Attributes = attributes.Value };
        }
        if (selection.Skills is { } skillSelection)
        {
            var skills = Sr6CreationSkillRules.Evaluate(preview, skillSelection);
            if (skills.Value is null) return new(skills.Outcome, null, skills.Blockers);
            preview = preview with { Skills = skills.Value,
                SourceAnchorIds = [.. preview.SourceAnchorIds, Sr6CreationSkillRules.SourceAnchor] };
        }
        if (selection.Knowledge is { } knowledgeSelection)
        {
            var knowledge = Sr6CreationKnowledgeRules.Evaluate(preview, knowledgeSelection);
            if (knowledge.Value is null) return new(knowledge.Outcome, null, knowledge.Blockers);
            preview = preview with { Knowledge = knowledge.Value,
                SourceAnchorIds = [.. preview.SourceAnchorIds, Sr6CreationKnowledgeRules.SourceAnchor] };
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
