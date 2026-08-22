using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Characters;

namespace Chummer.Application.BuildGhost;

public static class BuildGhostWizardConversationValidator
{
    public static IReadOnlyList<string> ValidateContext(BuildGhostWizardContextPacket? context)
    {
        List<string> reasons = [];
        if (context is null)
        {
            return ["wizard-context-missing"];
        }

        RequireExact(reasons, "context-schema", context.Schema, BuildGhostWizardContractVersions.ContextV1);
        Require(reasons, "owner", context.OwnerId);
        Require(reasons, "thread", context.ThreadId);
        Require(reasons, "workspace", context.WorkspaceId);
        Require(reasons, "active-step", context.ActiveStepId);
        Require(reasons, "locale", context.Locale);
        Require(reasons, "fallback", context.DeterministicFallbackText);
        if (context.WorkspaceRevision < 0)
        {
            reasons.Add("workspace-revision-invalid");
        }

        CharacterCreationWizardSnapshot wizard = context.Wizard;
        RequireExact(reasons, "wizard-schema", wizard.Schema, CharacterCreationWizardSchemas.SnapshotV1);
        RequireExact(reasons, "wizard-workspace", wizard.WorkspaceId, context.WorkspaceId);
        RequireExact(reasons, "wizard-step", wizard.ActiveStepId, context.ActiveStepId);
        if (wizard.WorkspaceRevision != context.WorkspaceRevision)
        {
            reasons.Add("wizard-revision-mismatch");
        }

        RequireSha256(reasons, "wizard-content", wizard.ContentDigest);
        RequireSha256(reasons, "wizard-source", wizard.SourceDigest);
        RequireSha256(reasons, "wizard-snapshot", wizard.SnapshotDigest);
        RequireSha256(reasons, "context-input", context.InputDigest);
        RequireSha256(reasons, "context-packet", context.PacketDigest);
        if (BuildGhostCanonicalDigest.IsSha256(wizard.SnapshotDigest))
        {
            string expected = BuildGhostCanonicalDigest.Compute(wizard with { SnapshotDigest = string.Empty });
            if (!string.Equals(expected, wizard.SnapshotDigest, StringComparison.Ordinal))
            {
                reasons.Add("wizard-snapshot-digest-mismatch");
            }
        }

        if (BuildGhostCanonicalDigest.IsSha256(context.PacketDigest))
        {
            string expected = BuildGhostCanonicalDigest.Compute(context with { PacketDigest = string.Empty });
            if (!string.Equals(expected, context.PacketDigest, StringComparison.Ordinal))
            {
                reasons.Add("wizard-context-packet-digest-mismatch");
            }
        }

        if (context.AllowedSuggestedActions.Any(action =>
                !action.RequiresExplicitReview
                || action.WorkspaceRevision != context.WorkspaceRevision
                || !string.Equals(action.SourceDigest, wizard.SourceDigest, StringComparison.Ordinal)))
        {
            reasons.Add("wizard-context-action-binding-mismatch");
        }

        return reasons.Distinct(StringComparer.Ordinal).OrderBy(static reason => reason, StringComparer.Ordinal).ToArray();
    }

    public static BuildGhostWizardTurnValidationResult ValidateResponse(
        BuildGhostWizardTurnRequest request,
        BuildGhostWizardTurnResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        List<string> reasons = ValidateContext(request.Context).ToList();
        RequireExact(reasons, "request-schema", request.Schema, BuildGhostWizardContractVersions.TurnRequestV1);
        RequireExact(reasons, "request-thread-context", request.ThreadId, request.Context.ThreadId);
        RequireExact(reasons, "request-owner-context", request.OwnerId, request.Context.OwnerId);
        RequireExact(reasons, "request-workspace-context", request.WorkspaceId, request.Context.WorkspaceId);
        RequireExact(reasons, "request-step-context", request.ActiveStepId, request.Context.ActiveStepId);
        RequireExact(reasons, "request-locale-context", request.Locale, request.Context.Locale, ignoreCase: true);
        Require(reasons, "request-id", request.RequestId);
        Require(reasons, "request-user-text", request.UserText);
        RequireSha256(reasons, "request-digest", request.RequestDigest);
        if (request.WorkspaceRevision != request.Context.WorkspaceRevision)
        {
            reasons.Add("request-revision-context-mismatch");
        }
        if (BuildGhostCanonicalDigest.IsSha256(request.RequestDigest))
        {
            string expected = BuildGhostCanonicalDigest.Compute(request with { RequestDigest = string.Empty });
            if (!string.Equals(expected, request.RequestDigest, StringComparison.Ordinal))
            {
                reasons.Add("request-digest-mismatch");
            }
        }

        RequireExact(reasons, "response-schema", response.Schema, BuildGhostWizardContractVersions.TurnResponseV1);
        RequireExact(reasons, "response-request", response.RequestId, request.RequestId);
        RequireExact(reasons, "response-thread", response.ThreadId, request.ThreadId);
        RequireExact(reasons, "response-packet", response.PacketDigest, request.Context.PacketDigest);
        RequireExact(reasons, "response-locale", response.Locale, request.Locale, ignoreCase: true);
        Require(reasons, "response-text", response.Text);
        RequireSha256(reasons, "response-digest", response.ResponseDigest);
        if (BuildGhostCanonicalDigest.IsSha256(response.ResponseDigest))
        {
            string expected = BuildGhostCanonicalDigest.Compute(response with { ResponseDigest = string.Empty });
            if (!string.Equals(expected, response.ResponseDigest, StringComparison.Ordinal))
            {
                reasons.Add("response-digest-mismatch");
            }
        }

        AddUnknownReferences(
            reasons,
            "fact",
            response.ReferencedFactIds,
            request.Context.Facts.Select(static fact => fact.FactId));
        AddUnknownReferences(
            reasons,
            "option",
            response.ReferencedOptionIds,
            request.Context.Wizard.LegalOptionsByStep.Values
                .SelectMany(static options => options)
                .Select(static option => option.OptionId));
        AddUnknownReferences(
            reasons,
            "source-anchor",
            response.ReferencedSourceAnchorIds,
            request.Context.SourceAnchors.Select(static anchor => anchor.AnchorId));

        Dictionary<string, BuildGhostAllowedAction> allowedActions = request.Context.AllowedSuggestedActions
            .ToDictionary(static action => action.ActionId, StringComparer.Ordinal);
        foreach (BuildGhostWizardSuggestion suggestion in response.Suggestions)
        {
            if (!allowedActions.TryGetValue(suggestion.ActionId, out BuildGhostAllowedAction? allowed))
            {
                reasons.Add($"unknown-suggestion:{suggestion.ActionId}");
                continue;
            }

            if (!suggestion.PreviewOnly
                || !suggestion.RequiresExplicitReview
                || !allowed.RequiresExplicitReview
                || suggestion.ExpectedWorkspaceRevision != request.WorkspaceRevision
                || suggestion.ExpectedWorkspaceRevision != allowed.WorkspaceRevision
                || !string.Equals(suggestion.ExpectedWizardSnapshotDigest, request.Context.Wizard.SnapshotDigest, StringComparison.Ordinal)
                || !string.Equals(suggestion.ExpectedPacketDigest, request.Context.PacketDigest, StringComparison.Ordinal)
                || !string.Equals(suggestion.ActionType, allowed.ActionType, StringComparison.Ordinal))
            {
                reasons.Add($"suggestion-binding-mismatch:{suggestion.ActionId}");
            }

            AddUnknownReferences(
                reasons,
                $"suggestion-source-anchor:{suggestion.ActionId}",
                suggestion.SourceAnchorIds,
                request.Context.SourceAnchors.Select(static anchor => anchor.AnchorId));
        }

        string[] orderedReasons = reasons
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static reason => reason, StringComparer.Ordinal)
            .ToArray();
        return orderedReasons.Length == 0
            ? new BuildGhostWizardTurnValidationResult(
                Accepted: true,
                OutcomeStatus: "validated-provider-answer",
                SafeText: response.Text,
                SafeSuggestions: response.Suggestions,
                RejectionReasons: [])
            : new BuildGhostWizardTurnValidationResult(
                Accepted: false,
                OutcomeStatus: BuildGhostConversationStatuses.DeterministicFallback,
                SafeText: request.Context.DeterministicFallbackText,
                SafeSuggestions: [],
                RejectionReasons: orderedReasons);
    }

    private static void AddUnknownReferences(
        ICollection<string> reasons,
        string kind,
        IEnumerable<string> references,
        IEnumerable<string> known)
    {
        HashSet<string> knownSet = known.ToHashSet(StringComparer.Ordinal);
        foreach (string reference in references.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!knownSet.Contains(reference))
            {
                reasons.Add($"unknown-{kind}:{reference}");
            }
        }
    }

    private static void Require(ICollection<string> reasons, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reasons.Add($"{field}-missing");
        }
    }

    private static void RequireExact(
        ICollection<string> reasons,
        string field,
        string? actual,
        string? expected,
        bool ignoreCase = false)
    {
        if (!string.Equals(
                actual,
                expected,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            reasons.Add($"{field}-mismatch");
        }
    }

    private static void RequireSha256(ICollection<string> reasons, string field, string? value)
    {
        if (!BuildGhostCanonicalDigest.IsSha256(value))
        {
            reasons.Add($"{field}-invalid");
        }
    }
}

