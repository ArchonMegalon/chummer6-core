using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Returns detached diagnostics only when this exact invocation retained the
    // canonical funding execution. Public budgets and digests cannot register it.
    public bool TryGetExecutedProfileFunding994(CharacterCreationResourcesAuthority authority,
        string profileXmlDigest, string field, string leafXmlDigest, out string? result, out string? executionDigest)
        => CharacterCreationResourcesService.CompleteFundingExecution.TryReadSource994(this, authority,
            profileXmlDigest, field, leafXmlDigest, out result, out executionDigest);

    // Detached diagnostics from this exact privately registered invocation.
    // Public/copied budgets cannot register execution; no receipt fields are added.
    public bool TryGetExecutedContactPoints962(CharacterCreationKarmaContactsPolicy policy,
        string profileXmlDigest, string leafXmlDigest, out CharacterCreationContactBudget? budget,
        out string? executionDigest)
        => CharacterCreationCompleteContactsExecution.TryReadSource962(this, policy, profileXmlDigest,
            leafXmlDigest, out budget, out executionDigest);

    // Existing canonical input/result types; internal setters keep public DTOs
    // and serialized receipts from supplying an invocation.
    [JsonIgnore] public CharacterCreationPrerequisiteDraft? CreationDraft718 { get; internal init; }
    [JsonIgnore] public CharacterCreationAttributesPreview? Attributes718 { get; internal init; }
    [JsonIgnore] public CharacterCreationAttributePolicy? AttributePolicy718 { get; internal init; }
    [JsonIgnore] public CharacterCreationAttributeSkillsPreview? Skills718 { get; internal init; }
    [JsonIgnore] public string? ResourcesExecution718 { get; internal init; }
    [JsonIgnore] public CharacterCreationLifestylesAuthority? LifestyleAuthority718 { get; internal init; }
    [JsonIgnore] public IReadOnlyList<CharacterCreationLifestyleProjection>? Lifestyles718 { get; internal init; }
    [JsonIgnore] public Guid? StartingLifestyleId718 { get; internal init; }
    [JsonIgnore] public CharacterWorkspaceId? WorkspaceId718 { get; internal init; }
}
