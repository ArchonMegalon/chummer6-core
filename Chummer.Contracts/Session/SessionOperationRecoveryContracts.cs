namespace Chummer.Contracts.Session;

public static class SessionOperationFailureClasses
{
    public const string RuleExecutionFailure = "rule_execution_failure";
    public const string FallbackTransition = "fallback_transition";
    public const string ThroughputBudgetExceeded = "throughput_budget_exceeded";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
    public const string Interrupted = "interrupted";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ValidationBlocked = "validation_blocked";
}

public static class SessionOperationRetryClasses
{
    public const string SafeImmediateRetry = "safe_immediate_retry";
    public const string SafeRetryAfterBackoff = "safe_retry_after_backoff";
    public const string UseFallback = "use_fallback";
    public const string ContinueWithCurrentState = "continue_with_current_state";
    public const string Resume = "resume";
    public const string DoNotRetry = "do_not_retry";
}

public static class SessionOperationSafeActionIds
{
    public const string Retry = "retry";
    public const string RetryAfterBackoff = "retry_after_backoff";
    public const string UseFallback = "use_fallback";
    public const string Continue = "continue";
    public const string Cancel = "cancel";
    public const string Resume = "resume";
    public const string OpenSupport = "open_support";
}

public sealed record SessionOperationRecoveryAction(
    string ActionId,
    string TitleKey,
    bool UserInitiated,
    bool Destructive = false,
    string? ExplanationKey = null);

public sealed record SessionOperationRecoveryContract(
    string Operation,
    string FailureClass,
    string RetryClass,
    bool Retriable,
    bool FallbackAllowed,
    bool CanContinue,
    string UserMessageKey,
    IReadOnlyList<SessionOperationRecoveryAction> SafeActions,
    SessionOperationObservability? Observability = null)
{
    public SessionOperationRecoveryAction? PrimaryAction => SafeActions.Count > 0 ? SafeActions[0] : null;
}

public sealed record SessionOperationThroughputGuardrail(
    string Operation,
    string BudgetClass,
    int MaxBatchSize,
    TimeSpan TargetP95Latency,
    TimeSpan HardTimeout,
    long MaxAllocatedBytes,
    string MetricName);

public static class SessionOperationThroughputMetrics
{
    public const string CampaignEngineBatchDurationMilliseconds = "chummer.session.campaign_engine.batch.duration.ms";
    public const string CampaignEngineBatchAllocatedBytes = "chummer.session.campaign_engine.batch.allocated.bytes";
}

public static class SessionLongRunningOperationStates
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Recoverable = "recoverable";
    public const string Resumable = "resumable";
    public const string RolledBack = "rolled_back";
}

public sealed record SessionLongRunningOperationState(
    string OperationId,
    string Operation,
    string State,
    bool Recoverable,
    bool Retryable,
    bool Cancellable,
    bool Resumable,
    bool RollbackAvailable,
    string? ResumeToken = null,
    string? StateMessageKey = null,
    SessionOperationRecoveryContract? Recovery = null);

public static class SessionOperationRecoveryContracts
{
    public static SessionOperationRecoveryContract RuleExecutionFailure(
        string operation,
        SessionOperationObservability? observability = null)
        => new(
            Operation: NormalizeRequired(operation, nameof(operation)),
            FailureClass: SessionOperationFailureClasses.RuleExecutionFailure,
            RetryClass: SessionOperationRetryClasses.UseFallback,
            Retriable: true,
            FallbackAllowed: true,
            CanContinue: false,
            UserMessageKey: "session.recovery.rule_execution_failure",
            SafeActions:
            [
                new SessionOperationRecoveryAction(
                    SessionOperationSafeActionIds.UseFallback,
                    "session.recovery.action.use_fallback",
                    UserInitiated: true,
                    ExplanationKey: "session.recovery.action.use_fallback.explain"),
                new SessionOperationRecoveryAction(
                    SessionOperationSafeActionIds.Retry,
                    "session.recovery.action.retry",
                    UserInitiated: true)
            ],
            Observability: observability);

    public static SessionOperationRecoveryContract ThroughputBudgetExceeded(
        string operation,
        SessionOperationObservability? observability = null)
        => new(
            Operation: NormalizeRequired(operation, nameof(operation)),
            FailureClass: SessionOperationFailureClasses.ThroughputBudgetExceeded,
            RetryClass: SessionOperationRetryClasses.SafeRetryAfterBackoff,
            Retriable: true,
            FallbackAllowed: false,
            CanContinue: true,
            UserMessageKey: "session.recovery.throughput_budget_exceeded",
            SafeActions:
            [
                new SessionOperationRecoveryAction(
                    SessionOperationSafeActionIds.RetryAfterBackoff,
                    "session.recovery.action.retry_after_backoff",
                    UserInitiated: true),
                new SessionOperationRecoveryAction(
                    SessionOperationSafeActionIds.Continue,
                    "session.recovery.action.continue_current_state",
                    UserInitiated: true)
            ],
            Observability: observability);

    public static SessionOperationRecoveryContract InterruptedOrCancelled(
        string operation,
        SessionOperationObservability? observability = null)
        => new(
            Operation: NormalizeRequired(operation, nameof(operation)),
            FailureClass: SessionOperationFailureClasses.Interrupted,
            RetryClass: SessionOperationRetryClasses.Resume,
            Retriable: true,
            FallbackAllowed: false,
            CanContinue: true,
            UserMessageKey: "session.recovery.interrupted_or_cancelled",
            SafeActions:
            [
                new SessionOperationRecoveryAction(
                    SessionOperationSafeActionIds.Resume,
                    "session.recovery.action.resume",
                    UserInitiated: true),
                new SessionOperationRecoveryAction(
                    SessionOperationSafeActionIds.Cancel,
                    "session.recovery.action.cancel",
                    UserInitiated: true)
            ],
            Observability: observability);

    public static SessionOperationThroughputGuardrail CampaignEngineBatchGuardrail(
        string operation,
        int maxBatchSize,
        TimeSpan targetP95Latency,
        TimeSpan hardTimeout,
        long maxAllocatedBytes)
        => new(
            Operation: NormalizeRequired(operation, nameof(operation)),
            BudgetClass: "campaign_engine_batch",
            MaxBatchSize: maxBatchSize > 0 ? maxBatchSize : throw new ArgumentOutOfRangeException(nameof(maxBatchSize)),
            TargetP95Latency: targetP95Latency > TimeSpan.Zero ? targetP95Latency : throw new ArgumentOutOfRangeException(nameof(targetP95Latency)),
            HardTimeout: hardTimeout >= targetP95Latency ? hardTimeout : throw new ArgumentOutOfRangeException(nameof(hardTimeout)),
            MaxAllocatedBytes: maxAllocatedBytes > 0 ? maxAllocatedBytes : throw new ArgumentOutOfRangeException(nameof(maxAllocatedBytes)),
            MetricName: SessionOperationThroughputMetrics.CampaignEngineBatchDurationMilliseconds);

    public static SessionLongRunningOperationState RecoverableCancellation(
        string operationId,
        string operation,
        string resumeToken,
        SessionOperationRecoveryContract? recovery = null)
        => new(
            OperationId: NormalizeRequired(operationId, nameof(operationId)),
            Operation: NormalizeRequired(operation, nameof(operation)),
            State: SessionLongRunningOperationStates.Cancelled,
            Recoverable: true,
            Retryable: true,
            Cancellable: false,
            Resumable: true,
            RollbackAvailable: true,
            ResumeToken: NormalizeRequired(resumeToken, nameof(resumeToken)),
            StateMessageKey: "session.operation.cancelled_recoverable",
            Recovery: recovery ?? InterruptedOrCancelled(operation));

    public static string ResolveUserMessageKey(SessionOperationRecoveryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return string.IsNullOrWhiteSpace(contract.UserMessageKey)
            ? $"session.recovery.{contract.FailureClass}"
            : contract.UserMessageKey;
    }

    public static IReadOnlyList<SessionOperationRecoveryAction> ResolveSafeActions(
        SessionOperationRecoveryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return contract.SafeActions ?? [];
    }

    private static string NormalizeRequired(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", argumentName);
        }

        return value.Trim();
    }
}
