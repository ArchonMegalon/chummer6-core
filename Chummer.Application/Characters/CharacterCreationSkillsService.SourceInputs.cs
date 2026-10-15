using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    // Real source admission, shared by ordinary editing and read-only completion.
    // This is deliberately not a pure DTO operation: resolver/context access must
    // remain inside the caller's owner/source operation scope.
    private SkillSourceInputs ReadSourceInputs(WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        ICharacterSourceDataContext? context = _resolver.TryCreateContext(workspace.Document.Content);
        CharacterCreationPrerequisiteAuthority prerequisiteAuthority = CharacterCreationPrerequisiteAuthority.Unavailable;
        CharacterCreationSkillsAuthority authority = CharacterCreationSkillsAuthority.Unavailable;
        bool authorityReady = context is not null
            && context.TryResolveCreationPrerequisiteAuthority(out prerequisiteAuthority)
            && context.TryResolveCreationSkillsAuthority(out authority)
            && CharacterCreationSkillsDraftIntegrity.IsValidAuthority(authority);
        if (!authorityReady)
            blockers.Add(CharacterCreationSkillsBlockers.AuthorityUnavailable);
        else if (!string.Equals(authority.SettingsProfileId, prerequisiteAuthority.SettingsProfileId, StringComparison.Ordinal)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(authority.RawProfileInputsDigest, prerequisiteAuthority.RawProfileInputsDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(authority.EffectiveSkillsInputsDigest, prerequisiteAuthority.EffectiveSkillsInputsDigest))
            blockers.Add(CharacterCreationSkillsBlockers.SkillsSourceDrift);
        else if (authority.KnowledgePointContributions.Any(contribution =>
                     !CharacterCreationSkillsDigest.EqualsFixedTime(contribution.SourceCharacterXmlDigest, rawDigest)))
            blockers.Add(CharacterCreationSkillsBlockers.KnowledgeContributionAuthorityUnsupported);

        CharacterCreationPrerequisiteDraft? prerequisite = workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft;
        if (prerequisite is null || !CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(
                prerequisite, workspace.Id, workspace.ContentRevision, rawDigest, prerequisiteAuthority))
        {
            blockers.Add(CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
            prerequisite = null;
        }
        // Re-review needs the original catalog as well as the contextual one.
        CharacterCreationSkillsAuthority catalogAuthority = authority;
        if (prerequisite is not null && authorityReady
            && !CharacterCreationSkillsAccessRules.TryBind(prerequisite, context!, authority, out authority))
            blockers.Add(CharacterCreationSkillsBlockers.TalentAccessRequired);

        int activeTotal = 0;
        int groupTotal = 0;
        if (prerequisite is not null)
        {
            CharacterCreationPriorityAssignment? assignment = prerequisite.Assignments.SingleOrDefault(item =>
                item.CategoryId == CharacterCreationPriorityCategoryIds.Skills);
            CharacterCreationPriorityOptionProjection? option = assignment is null ? null
                : prerequisiteAuthority.Options.SingleOrDefault(item =>
                    item.CategoryId == CharacterCreationPriorityCategoryIds.Skills
                    && item.SourceId == assignment.SourceId && item.Rank == assignment.Rank
                    && CharacterCreationSkillsDigest.EqualsFixedTime(item.SourceNodeDigest, assignment.SourceNodeDigest));
            if (option?.BaseActiveSkillPoints is not int active || option.BaseSkillGroupPoints is not int groups
                || prerequisite.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                || !string.Equals(prerequisite.PriorityTable, "Standard", StringComparison.Ordinal)
                || !CharacterCreationStandardPrioritySkillsRules.HasExactBudgetTable(prerequisiteAuthority.Options)
                || !CharacterCreationStandardPrioritySkillsRules.TryGetBudget(assignment?.Rank, out int expectedActive, out int expectedGroups)
                || active != expectedActive || groups != expectedGroups)
                blockers.Add(CharacterCreationSkillsBlockers.SkillsPriorityAuthorityInvalid);
            else
            {
                activeTotal = active;
                groupTotal = groups;
            }
            if (!CharacterCreationTalentSkillGrants.TryResolve(prerequisite, authority, out _))
                blockers.Add(CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
        }

        CharacterCreationKnowledgePointContribution[] contributions = authority.KnowledgePointContributions
            .OrderBy(item => item.ContributionId, StringComparer.Ordinal).ToArray();
        return new(rawDigest, catalogAuthority, authority, prerequisite, activeTotal, groupTotal,
            ResolveMovementCapability(prerequisite?.HeritageSelection?.Movement), contributions,
            CharacterCreationSkillsDigest.Compute(contributions), Normalize(blockers.Concat(authority.Blockers)));
    }

    private sealed record SkillSourceInputs(
        string RawDigest,
        CharacterCreationSkillsAuthority CatalogAuthority,
        CharacterCreationSkillsAuthority Authority,
        CharacterCreationPrerequisiteDraft? Prerequisite,
        int ActiveTotal,
        int GroupTotal,
        CharacterCreationMovementCapability Movement,
        IReadOnlyList<CharacterCreationKnowledgePointContribution> Contributions,
        string ContributionDigest,
        IReadOnlyList<string> Blockers);
}
