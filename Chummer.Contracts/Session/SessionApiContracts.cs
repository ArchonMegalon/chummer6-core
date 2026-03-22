using Chummer.Contracts.Characters;
using System.Diagnostics;

namespace Chummer.Contracts.Session;

public static class SessionApiOperations
{
    public const string ListCharacters = "list-characters";
    public const string GetCharacterProjection = "get-character-projection";
    public const string ApplyCharacterPatches = "apply-character-patches";
    public const string SyncCharacterLedger = "sync-character-ledger";
    public const string ListProfiles = "list-profiles";
    public const string GetRuntimeState = "get-runtime-state";
    public const string GetRuntimeBundle = "get-runtime-bundle";
    public const string RefreshRuntimeBundle = "refresh-runtime-bundle";
    public const string SelectProfile = "select-profile";
    public const string ListRulePacks = "list-rulepacks";
    public const string UpdatePins = "update-pins";
}

public static class SessionProfileSelectionOutcomes
{
    public const string Selected = "selected";
    public const string Deferred = "deferred";
    public const string Blocked = "blocked";
}

public sealed record SessionCharacterListItem(
    string CharacterId,
    string DisplayName,
    string RulesetId,
    string RuntimeFingerprint);

public sealed record SessionCharacterCatalog(
    IReadOnlyList<SessionCharacterListItem> Characters);

public sealed record SessionProfileListItem(
    string ProfileId,
    string Title,
    string RulesetId,
    string RuntimeFingerprint,
    string UpdateChannel,
    bool SessionReady = true,
    string? Audience = null);

public sealed record SessionProfileCatalog(
    IReadOnlyList<SessionProfileListItem> Profiles,
    string? ActiveProfileId = null);

public sealed record SessionPatchRequest(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    IReadOnlyList<SessionEventEnvelope> Events);

public sealed record SessionPinUpdateRequest(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    IReadOnlyList<SessionQuickActionPin> Pins);

public sealed record SessionProfileSelectionRequest(
    string ProfileId);

public sealed record SessionProfileSelectionReceipt(
    string CharacterId,
    string ProfileId,
    string RuntimeFingerprint,
    string Outcome,
    bool RequiresBundleRefresh = false,
    string? DeferredReason = null);

public sealed record SessionNotImplementedReceipt(
    string Error,
    string Operation,
    string Message,
    string? CharacterId = null,
    string? OwnerId = null,
    SessionOperationObservability? Observability = null);

public sealed record SessionOperationObservability(
    string Operation,
    string CorrelationId,
    string TraceId,
    string MetricName,
    DateTimeOffset ObservedAtUtc,
    string? OwnerId = null,
    string? CharacterId = null,
    IReadOnlyDictionary<string, string>? Tags = null);

public static class SessionObservabilityMetrics
{
    public const string OperationDurationMilliseconds = "chummer.session.operation.duration.ms";
}

public static class SessionApiObservability
{
    public static SessionOperationObservability Create(
        string operation,
        string? ownerId = null,
        string? characterId = null,
        string? correlationId = null,
        string? traceId = null,
        IReadOnlyDictionary<string, string>? tags = null,
        DateTimeOffset? observedAtUtc = null)
    {
        string normalizedOperation = NormalizeRequired(operation, nameof(operation));
        string resolvedTraceId = NormalizeTraceId(traceId);
        string resolvedCorrelationId = NormalizeCorrelationId(correlationId, resolvedTraceId);

        return new SessionOperationObservability(
            Operation: normalizedOperation,
            CorrelationId: resolvedCorrelationId,
            TraceId: resolvedTraceId,
            MetricName: SessionObservabilityMetrics.OperationDurationMilliseconds,
            ObservedAtUtc: observedAtUtc ?? DateTimeOffset.UtcNow,
            OwnerId: NormalizeOptional(ownerId),
            CharacterId: NormalizeOptional(characterId),
            Tags: tags);
    }

    private static string NormalizeCorrelationId(string? correlationId, string traceId)
    {
        string? normalized = NormalizeOptional(correlationId);
        if (normalized is not null)
        {
            return normalized;
        }

        return traceId;
    }

    private static string NormalizeTraceId(string? traceId)
    {
        string? normalized = NormalizeOptional(traceId);
        if (normalized is not null)
        {
            return normalized;
        }

        ActivityTraceId? activityTraceId = Activity.Current?.TraceId;
        if (activityTraceId.HasValue)
        {
            return activityTraceId.Value.ToString();
        }

        return ActivityTraceId.CreateRandom().ToString();
    }

    private static string NormalizeRequired(string value, string argumentName)
    {
        string? normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            throw new ArgumentException("Value cannot be null or whitespace.", argumentName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SessionApiResult<T>(
    T? Payload = default,
    SessionNotImplementedReceipt? NotImplemented = null,
    SessionOperationObservability? Observability = null)
{
    public bool IsImplemented => NotImplemented is null;

    public static SessionApiResult<T> Implemented(T payload, SessionOperationObservability? observability = null)
        => new(payload, null, observability);

    public static SessionApiResult<T> FromNotImplemented(SessionNotImplementedReceipt receipt)
        => new(default, receipt, receipt.Observability);
}
