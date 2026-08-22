using Chummer.Contracts.Characters;

namespace Chummer.Contracts.BuildGhost;

public static class BuildGhostWizardContractVersions
{
    public const string ContextV1 = "chummer.build_ghost_wizard_context.v1";
    public const string TurnRequestV1 = "chummer.build_ghost_wizard_turn_request.v1";
    public const string TurnResponseV1 = "chummer.build_ghost_wizard_turn_response.v1";
}

public static class BuildGhostConversationRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string DeterministicFallback = "deterministic-fallback";
}

public static class BuildGhostConversationStatuses
{
    public const string Ready = "ready";
    public const string ProviderUnavailable = "provider-unavailable";
    public const string DeterministicFallback = "deterministic-fallback";
    public const string Stale = "stale";
}

public sealed record BuildGhostWizardContextPacket(
    string Schema,
    string OwnerId,
    string ThreadId,
    string WorkspaceId,
    long WorkspaceRevision,
    string ActiveStepId,
    string Locale,
    CharacterCreationWizardSnapshot Wizard,
    IReadOnlyList<BuildGhostSourceAnchor> SourceAnchors,
    IReadOnlyList<BuildGhostFact> Facts,
    IReadOnlyList<string> AllowedQuestionScopes,
    IReadOnlyList<BuildGhostAllowedAction> AllowedSuggestedActions,
    string DeterministicFallbackText,
    string InputDigest,
    string PacketDigest);

public sealed record BuildGhostConversationMessage(
    string MessageId,
    string Role,
    string Text,
    DateTimeOffset CreatedAt,
    long BoundWorkspaceRevision,
    string BoundWizardSnapshotDigest,
    string BoundPacketDigest,
    IReadOnlyList<string> ReferencedFactIds,
    IReadOnlyList<string> ReferencedOptionIds,
    IReadOnlyList<string> ReferencedSourceAnchorIds,
    bool IsStale);

public sealed record BuildGhostWizardConversationState(
    string ThreadId,
    string WorkspaceId,
    string ActiveStepId,
    long LastBoundWorkspaceRevision,
    string LastBoundWizardSnapshotDigest,
    string LastBoundPacketDigest,
    string Status,
    IReadOnlyList<BuildGhostConversationMessage> Messages);

public sealed record BuildGhostWizardTurnRequest(
    string Schema,
    string RequestId,
    string ThreadId,
    string OwnerId,
    string WorkspaceId,
    long WorkspaceRevision,
    string ActiveStepId,
    string Locale,
    string UserText,
    BuildGhostWizardContextPacket Context,
    string RequestDigest);

public sealed record BuildGhostWizardSuggestion(
    string ActionId,
    string ActionType,
    string Label,
    bool PreviewOnly,
    bool RequiresExplicitReview,
    long ExpectedWorkspaceRevision,
    string ExpectedWizardSnapshotDigest,
    string ExpectedPacketDigest,
    IReadOnlyList<CharacterCreationChoiceConsequence> Consequences,
    IReadOnlyList<CharacterCreationChoiceCost> Costs,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record BuildGhostWizardTurnResponse(
    string Schema,
    string RequestId,
    string ThreadId,
    string PacketDigest,
    string Locale,
    string Text,
    string Status,
    IReadOnlyList<string> ReferencedFactIds,
    IReadOnlyList<string> ReferencedOptionIds,
    IReadOnlyList<string> ReferencedSourceAnchorIds,
    IReadOnlyList<BuildGhostWizardSuggestion> Suggestions,
    string ResponseDigest);

public sealed record BuildGhostWizardTurnValidationResult(
    bool Accepted,
    string OutcomeStatus,
    string SafeText,
    IReadOnlyList<BuildGhostWizardSuggestion> SafeSuggestions,
    IReadOnlyList<string> RejectionReasons);

