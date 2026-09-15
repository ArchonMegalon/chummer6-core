using Chummer.Application.Explain;
using Chummer.Application.Owners;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Explain;

/// <summary>
/// Provider-answer admission for the local workspace-question capability. The
/// caller's answer is only a reference-checked presentation of a newly resolved
/// Core result; it is never a source of rule facts, identity, or authority.
/// </summary>
public sealed class WorkspaceRuleProviderAnswerService(
    IOwnerContextAccessor owners,
    IWorkspaceRuleQuestionService questions) : IWorkspaceRuleProviderAnswerService
{
    private const string FallbackText =
        "This rule could not be resolved from the current Chummer context.";
    private const string ValidatedStatus = "validated-provider-answer";
    private const string FallbackStatus = "deterministic-fallback";

    public BuildGhostProviderValidationResult Validate(
        OwnerContextStamp expectedOwner,
        WorkspaceRuleQuestionRequest request,
        string expectedRequestId,
        BuildGhostProviderAnswer? answer)
    {
        List<string> reasons = [];
        WorkspaceRuleQuestionResult? current = null;
        if (request is null)
        {
            reasons.Add("request-missing");
        }
        else
        {
            try
            {
                current = questions.Resolve(expectedOwner, request);
            }
            catch
            {
                reasons.Add("core-resolution-failed");
            }
        }

        IOwnerContextLease? lease = null;
        try
        {
            if (owners is not IOwnerContextLeaseAccessor authority
                || !authority.TryAcquire(expectedOwner, out lease)
                || lease is null
                || lease.Stamp != expectedOwner)
            {
                reasons.Add("owner-context-invalid");
                return Rejected(reasons, FallbackText);
            }

            bool currentIsValid = request is not null
                && current is not null
                && IsResolvedResult(expectedOwner, request, current);
            string safeText = SafeText(current, currentIsValid);
            if (!currentIsValid)
            {
                reasons.Add(current is { Resolved: false }
                    ? "core-result-unresolved"
                    : "core-result-invalid");
            }

            if (!BoundedIdentifier(expectedRequestId))
                reasons.Add("request-id-invalid");

            if (answer is null)
            {
                reasons.Add("provider-answer-missing");
            }
            else if (currentIsValid)
            {
                try
                {
                    ValidateAnswer(answer, expectedRequestId, request!, current!, reasons);
                }
                catch
                {
                    reasons.Add("provider-answer-invalid");
                }
            }

            return RejectedOrAccepted(reasons, safeText);
        }
        catch
        {
            reasons.Add("provider-validation-failed");
            return Rejected(reasons, FallbackText);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private static BuildGhostProviderValidationResult RejectedOrAccepted(
        ICollection<string> reasons, string safeText)
    {
        string[] distinctReasons = reasons
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static reason => reason, StringComparer.Ordinal)
            .ToArray();
        return new BuildGhostProviderValidationResult(
            Accepted: distinctReasons.Length == 0,
            OutcomeStatus: distinctReasons.Length == 0 ? ValidatedStatus : FallbackStatus,
            SafeText: safeText,
            RejectionReasons: distinctReasons);
    }

    private static BuildGhostProviderValidationResult Rejected(
        ICollection<string> reasons, string safeText)
        => RejectedOrAccepted(reasons, safeText);

    private static void ValidateAnswer(
        BuildGhostProviderAnswer answer,
        string expectedRequestId,
        WorkspaceRuleQuestionRequest request,
        WorkspaceRuleQuestionResult current,
        ICollection<string> reasons)
    {
        if (!string.Equals(answer.Schema, WorkspaceRuleQuestionSchemas.ProviderAnswerV1,
                StringComparison.Ordinal))
            reasons.Add("provider-answer-schema");
        if (!string.Equals(answer.RequestId, expectedRequestId, StringComparison.Ordinal))
            reasons.Add("provider-answer-request-id");
        if (!string.Equals(answer.PacketDigest, current.ResultDigest, StringComparison.Ordinal))
            reasons.Add("provider-answer-result-digest");
        if (!string.Equals(answer.Locale, request.Locale, StringComparison.Ordinal)
            || current.Binding is null
            || !string.Equals(answer.Locale, current.Binding.Locale, StringComparison.Ordinal))
            reasons.Add("provider-answer-locale");
        if (!string.Equals(answer.Text, current.Explanation.Explanation, StringComparison.Ordinal))
            reasons.Add("provider-answer-text");

        WorkspaceRuleSourceAnchor anchor = current.SourceAnchors[0];
        if (answer.ReferencedRuleExplanationIds is null
            || answer.ReferencedRuleExplanationIds.Count != 1
            || !string.Equals(answer.ReferencedRuleExplanationIds[0],
                current.Explanation.ExplanationId, StringComparison.Ordinal)
            || answer.ReferencedSourceAnchorIds is null
            || answer.ReferencedSourceAnchorIds.Count != 1
            || !string.Equals(answer.ReferencedSourceAnchorIds[0], anchor.AnchorId,
                StringComparison.Ordinal))
            reasons.Add("provider-answer-references");

        if (answer.ReferencedFactIds is null || answer.ReferencedFactIds.Count != 0
            || answer.ReferencedStrategyIds is null || answer.ReferencedStrategyIds.Count != 0
            || answer.ReferencedVariantIds is null || answer.ReferencedVariantIds.Count != 0
            || answer.ReferencedMemberRefs is null || answer.ReferencedMemberRefs.Count != 0
            || answer.SuggestedActionIds is null || answer.SuggestedActionIds.Count != 0
            || answer.Links is null || answer.Links.Count != 0)
            reasons.Add("provider-answer-extra-references");
    }

    private static bool IsResolvedResult(
        OwnerContextStamp expectedOwner,
        WorkspaceRuleQuestionRequest request,
        WorkspaceRuleQuestionResult result)
    {
        try
        {
            if (!result.Resolved
                || !string.Equals(result.Schema, WorkspaceRuleQuestionSchemas.ResultV1,
                    StringComparison.Ordinal)
                || !string.Equals(result.Status, WorkspaceRuleQuestionStatuses.Resolved,
                    StringComparison.Ordinal)
                || result.Explanation is null
                || !string.Equals(result.Explanation.Schema,
                    BuildGhostContractVersions.RuleExplanationV1, StringComparison.Ordinal)
                || !BoundedIdentifier(result.Explanation.ExplanationId)
                || string.IsNullOrWhiteSpace(result.Explanation.Question)
                || string.IsNullOrWhiteSpace(result.Explanation.Explanation)
                || !string.Equals(result.Explanation.Status,
                    WorkspaceRuleQuestionStatuses.Resolved, StringComparison.Ordinal)
                || result.Explanation.UncertaintyReason is not null
                || result.Explanation.SourceLookupRoute is not null
                || !string.Equals(result.Explanation.RuleId,
                    WorkspaceRuleQuestionIntents.QualityLevelRuleId, StringComparison.Ordinal)
                || result.Explanation.SourceAnchorIds is null
                || result.Explanation.SourceAnchorIds.Count != 1
                || result.SourceAnchors is null
                || result.SourceAnchors.Count != 1
                || result.Binding is null
                || result.Level is not > 0
                || result.MaximumLevel is not > 0
                || result.Level > result.MaximumLevel
                || result.FailureReason is not null
                || !CharacterCreationQualitiesRules.IsCanonicalDigest(result.ResultDigest)
                || !string.Equals(result.ResultDigest,
                    WorkspaceRuleQuestionIntegrity.ComputeResultDigest(result), StringComparison.Ordinal))
                return false;

            WorkspaceRuleQuestionBinding binding = result.Binding;
            WorkspaceRuleSourceAnchor anchor = result.SourceAnchors[0];
            if (!string.Equals(binding.Schema, WorkspaceRuleQuestionSchemas.BindingV1,
                    StringComparison.Ordinal)
                || !string.Equals(binding.OwnerId, expectedOwner.Owner.Value, StringComparison.Ordinal)
                || binding.TrustedLocalOwner != expectedOwner.Owner.IsLocalSingleUser
                || !string.Equals(binding.OwnerAuthorityInstanceId,
                    expectedOwner.AuthorityInstanceId, StringComparison.Ordinal)
                || binding.OwnerTransitionRevision != expectedOwner.TransitionRevision
                || binding.WorkspaceId != request.WorkspaceId
                || !string.Equals(binding.RulesetId, request.RulesetId, StringComparison.Ordinal)
                || binding.ContentRevision != request.ExpectedContentRevision
                || binding.SavedRevision < 0
                || binding.SavedRevision > binding.ContentRevision
                || !string.Equals(binding.Intent, request.Intent, StringComparison.Ordinal)
                || !string.Equals(binding.SubjectId, request.SubjectId, StringComparison.Ordinal)
                || !string.Equals(binding.Locale, request.Locale, StringComparison.Ordinal)
                || !CharacterCreationQualitiesRules.IsCanonicalDigest(binding.WorkspaceDocumentDigest)
                || !CharacterCreationQualitiesRules.IsCanonicalDigest(binding.SourceProfileDigest)
                || !CharacterCreationQualitiesRules.IsCanonicalDigest(binding.SourceNodeDigest)
                || !string.Equals(binding.EngineIdentityKind,
                    WorkspaceRuleQuestionSchemas.ExecutingModulesV1, StringComparison.Ordinal)
                || binding.ExecutingModules is null
                || binding.ExecutingModules.Count == 0
                || !CharacterCreationQualitiesRules.IsCanonicalDigest(binding.EngineFingerprint)
                || !string.Equals(binding.EngineFingerprint,
                    WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(binding.ExecutingModules),
                    StringComparison.Ordinal))
                return false;

            HashSet<string> moduleRoles = new(StringComparer.Ordinal);
            foreach (WorkspaceRuleExecutingModule module in binding.ExecutingModules)
            {
                if (module is null
                    || string.IsNullOrWhiteSpace(module.Role)
                    || string.IsNullOrWhiteSpace(module.ImplementationType)
                    || string.IsNullOrWhiteSpace(module.AssemblyName)
                    || module.ModuleVersionId == Guid.Empty
                    || !moduleRoles.Add(module.Role))
                    return false;
            }

            if (!string.Equals(result.Explanation.SourceAnchorIds[0], anchor.AnchorId,
                    StringComparison.Ordinal)
                || !string.Equals(anchor.RulesetId, binding.RulesetId, StringComparison.Ordinal)
                || anchor.Page <= 0
                || string.IsNullOrWhiteSpace(anchor.SourceBook)
                || !Guid.TryParseExact(anchor.QualitySourceId, "D", out Guid sourceId)
                || sourceId == Guid.Empty
                || !CharacterCreationQualitiesRules.IsCanonicalDigest(anchor.SourceNodeDigest)
                || !CharacterCreationQualitiesRules.IsCanonicalDigest(anchor.SourceProfileDigest)
                || !string.Equals(anchor.SourceNodeDigest, binding.SourceNodeDigest,
                    StringComparison.Ordinal)
                || !string.Equals(anchor.SourceProfileDigest, binding.SourceProfileDigest,
                    StringComparison.Ordinal)
                || !string.Equals(anchor.SettingsProfileId, binding.SettingsProfileId,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(anchor.SettingsProfileId)
                || anchor.CalculationTrace is null
                || anchor.CalculationTrace.Count == 0)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeText(WorkspaceRuleQuestionResult? result, bool resolvedIsValid)
    {
        if (resolvedIsValid
            && result?.Explanation is BuildGhostRuleExplanation resolved
            && !string.IsNullOrWhiteSpace(resolved.Explanation))
            return resolved.Explanation;
        if (result is null || !string.Equals(result.Schema, WorkspaceRuleQuestionSchemas.ResultV1,
                StringComparison.Ordinal)
            || result.Resolved || result.Binding is not null
            || result.Level is not null || result.MaximumLevel is not null
            || result.SourceAnchors is null || result.SourceAnchors.Count != 0
            || result.Explanation is not BuildGhostRuleExplanation unresolved
            || !string.Equals(unresolved.Schema, BuildGhostContractVersions.RuleExplanationV1,
                StringComparison.Ordinal)
            || !BoundedIdentifier(unresolved.ExplanationId)
            || !string.Equals(unresolved.RuleId, WorkspaceRuleQuestionIntents.QualityLevelRuleId,
                StringComparison.Ordinal)
            || unresolved.SourceAnchorIds is null || unresolved.SourceAnchorIds.Count != 0
            || !string.Equals(result.Status, WorkspaceRuleQuestionStatuses.Unresolved,
                StringComparison.Ordinal)
            || !string.Equals(unresolved.Status, "bounded-uncertainty", StringComparison.Ordinal)
            || !CharacterCreationQualitiesRules.IsCanonicalDigest(result.ResultDigest)
            || !string.Equals(result.ResultDigest,
                WorkspaceRuleQuestionIntegrity.ComputeResultDigest(result), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(unresolved.Explanation))
            return FallbackText;
        return unresolved.Explanation;
    }

    private static bool BoundedIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && value.All(static character => !char.IsControl(character));
}
