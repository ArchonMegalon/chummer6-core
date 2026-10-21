using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Observes owner-private retained continuation bytes without importing, restoring
/// or opening a mutable runner. The host supplies its real owner lease authority
/// and source services. Results are detached observations, never save authority;
/// consumers must still evaluate effective policy and review proposed completion.
/// </summary>
public sealed class WorkspaceContinuationCompletionReader(
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver,
    ICharacterFileQueries characterQueries,
    ILifeModulesCatalogService lifeModules)
{
    public WorkspaceContinuationCompletionResult LoadCompletionInputs(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation,
        int maximumBytes, CancellationToken cancellationToken = default)
        => LoadCompletionInputsCore(expectedOwner, retainedContinuation, maximumBytes, false, cancellationToken);

    /// <summary>Capture canonical consumption inputs through the same retained
    /// owner/source boundary. This observation grants no Complete or save authority.</summary>
    public WorkspaceContinuationCompletionResult LoadCompletionInputsWithPolicyConsumption(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation,
        int maximumBytes, CancellationToken cancellationToken = default)
        => LoadCompletionInputsCore(expectedOwner, retainedContinuation, maximumBytes, true, cancellationToken);

    /// <summary>Captures real execution and its canonical retained identity together.
    /// The view is the existing read-only continuation evaluator, never a runner store.</summary>
    public WorkspaceContinuationCompletionResult LoadCompletionInputsWithRuleSourceExecution(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation, int maximumBytes,
        out CharacterCreationRuleSourceExecutionContext? execution, CancellationToken cancellationToken = default)
        => LoadExecution(expectedOwner, retainedContinuation, maximumBytes, out execution,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1, cancellationToken);

    /// <summary>Explicit purchase-source V2 capture. The historical V1 entry above
    /// remains unchanged; neither detached result confers later mutation authority.</summary>
    public WorkspaceContinuationCompletionResult LoadCompletionInputsWithPurchaseSourceExecution(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation, int maximumBytes,
        out CharacterCreationRuleSourceExecutionContext? execution, CancellationToken cancellationToken = default)
        => LoadExecution(expectedOwner, retainedContinuation, maximumBytes, out execution,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, cancellationToken);

    /// <summary>
    /// Freshly validates retained bytes and evaluates on this synchronous call stack.
    /// A null execution schema selects the historical policy-consumption reader;
    /// V1 and V2 retain their exact source/capture identities. The callback receives
    /// detached observations, not a history-validation permission. Account, selection
    /// and campaign admission remain the caller's separate responsibility.
    /// </summary>
    public TResult EvaluateCompletionInputs<TResult>(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation, int maximumBytes,
        string? executionSchema,
        Func<WorkspaceContinuationCompletionResult, CharacterCreationRuleSourceExecutionContext?, TResult> evaluate,
        CancellationToken cancellationToken = default, bool sourcebookSelection = false)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        if (sourcebookSelection && executionSchema is not (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4))
            throw new ArgumentException("Sourcebook execution requires the V2, V3 or V4 source consumer.", nameof(executionSchema));
        if (executionSchema is not (null or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4))
            throw new ArgumentException("Unsupported completion execution schema.", nameof(executionSchema));
        // Do not accept an asynchronous result as a synchronous evaluation. The
        // internal memo is thread-local, never AsyncLocal, and is always disposed.
        if (typeof(Task).IsAssignableFrom(typeof(TResult)) || typeof(TResult) == typeof(ValueTask)
            || typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(ValueTask<>))
            throw new ArgumentException("Completion evaluation must be synchronous.", nameof(evaluate));
        cancellationToken.ThrowIfCancellationRequested();
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return evaluate(new(null, null, ["owner-authority-unavailable"], []), null);
        using var ownerLease = lease;
        using var history = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
        CharacterCreationRuleSourceExecutionContext? execution = null;
        // The owner lease is non-reentrant. Internal readers must share this
        // admission rather than acquiring a second lease inside the callback scope.
        var loaded = executionSchema is null
            ? LoadCompletionInputsCore(expectedOwner, retainedContinuation, maximumBytes, true, cancellationToken, admittedOwner: lease)
            : LoadExecution(expectedOwner, retainedContinuation, maximumBytes, out execution, executionSchema, cancellationToken,
                admittedOwner: lease, sourcebookSelection: sourcebookSelection);
        cancellationToken.ThrowIfCancellationRequested();
        // The callback must handle a refused observation; no failed load is upgraded.
        var result = evaluate(loaded, execution);
        cancellationToken.ThrowIfCancellationRequested();
        if (lease.Stamp != expectedOwner)
            throw new InvalidOperationException("Completion owner authority changed.");
        return result;
    }

    internal WorkspaceContinuationCompletionResult LoadExecutionUnderLease(
        OwnerContextStamp expectedOwner, IOwnerContextLease lease, ReadOnlyMemory<byte> retainedContinuation,
        int maximumBytes, out CharacterCreationRuleSourceExecutionContext? execution,
        string executionSchema, CancellationToken cancellationToken,
        ICharacterCreationRuleSourceRecovery? recovery = null, bool sourcebookSelection = false)
        => LoadExecution(expectedOwner, retainedContinuation, maximumBytes, out execution,
            executionSchema, cancellationToken, lease, recovery, sourcebookSelection);

    public WorkspaceContinuationCompletionResult LoadCompletionInputsWithSourcebookSelectionExecution(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation, int maximumBytes,
        out CharacterCreationRuleSourceExecutionContext? execution, CancellationToken cancellationToken = default)
        => LoadExecution(expectedOwner, retainedContinuation, maximumBytes, out execution,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, cancellationToken, sourcebookSelection: true);

    private WorkspaceContinuationCompletionResult LoadExecution(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation, int maximumBytes,
        out CharacterCreationRuleSourceExecutionContext? execution, string executionSchema,
        CancellationToken cancellationToken, IOwnerContextLease? admittedOwner = null,
        ICharacterCreationRuleSourceRecovery? recovery = null, bool sourcebookSelection = false)
    {
        execution = null;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (executionSchema is not (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)) return Unavailable();
        // Use one detached byte sequence for both historical boundary validation and execution.
        if (retainedContinuation.Length > maximumBytes)
            return new(null, null, ["continuation-wire-invalid"], []);
        byte[] bytes = retainedContinuation.ToArray();
        try
        {
            // V4-only source grammar cannot pass through a preliminary historical
            // catalogue read. Older consumers retain their original input identity.
            string? currentSchema = executionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                ? executionSchema : null;
            var historical = LoadCompletionInputsCore(expectedOwner, bytes, maximumBytes, true, cancellationToken, admittedOwner, recovery,
                currentSchema, sourcebookSelection: currentSchema is not null && sourcebookSelection);
            if (!historical.Success || historical.Identity is not { } identity || historical.Inputs is null)
                return historical;
            IOwnerContextLease? acquired = null;
            if (!WorkspaceContinuationCodec.TryDecodeCandidate(bytes, maximumBytes, out var candidate)
                || candidate.Snapshot.OwnerId != expectedOwner.Owner.NormalizedValue
                || candidate.SnapshotDigest != identity.ContinuationDigest
                || admittedOwner is null && !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out acquired))
                return Unavailable();
            using (acquired)
            {
                IOwnerContextLease lease = admittedOwner ?? acquired!;
                if (lease.Stamp != expectedOwner) return Unavailable();
                var snapshot = candidate.Snapshot.Workspace;
                var original = new WorkspaceStoredDocument(snapshot.Id, snapshot.Document,
                    snapshot.ContentRevision, snapshot.SavedRevision, snapshot.LastUpdatedUtc);
                var view = new WorkspaceContinuationReadView(expectedOwner.Owner, original);
                CharacterCreationRuleSourceCapture? capture = null;
                using var scope = (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                var loaded = OwnerBoundCharacterCreationCompletionReader.LoadFromObservation(
                    view, scope ?? sourceResolver, characterQueries,
                    new(identity.WorkspaceId) { IncludeKarmaPurchasePolicy = true }, cancellationToken,
                    includeLoadout: true, includeEffectivePolicy: true, includeConsumerPolicy: true,
                    knowledgeModifiers: true, policyConsumption: true, includeOriginalCharacter: true,
                    includeRuleSourceExecution: true, captured: value => capture = value,
                    ruleSourceExecutionSchema: executionSchema, includeSourcebooks: sourcebookSelection);
                if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } inputs
                    || capture is null || !capture.MatchesOriginalCharacter(candidate.Snapshot.Workspace.Document.Content)
                    || inputs.Skills.Binding.RawCharacterXmlDigest != identity.RawCharacterXmlDigest
                    || inputs.Skills.Binding.AuxiliaryStateDigest != identity.AuxiliaryStateDigest)
                    return Unavailable();
                // Reconstruct the preliminary observation from exactly this capture and
                // grammar. No second lease or source transition can change its identity.
                var replay = OwnerBoundCharacterCreationCompletionReader.LoadFromObservation(
                    view, capture.CreateReplayResolver(), characterQueries,
                    new(identity.WorkspaceId) { IncludeKarmaPurchasePolicy = true }, cancellationToken,
                    includeLoadout: true, includeEffectivePolicy: true, includeConsumerPolicy: true,
                    knowledgeModifiers: true, policyConsumption: true,
                    includeOriginalCharacter: currentSchema is not null,
                    includeRuleSourceExecution: currentSchema is not null,
                    retainedCapture: currentSchema is not null ? capture : null,
                    ruleSourceExecutionSchema: currentSchema ?? CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
                    includeSourcebooks: currentSchema is not null && sourcebookSelection);
                if (replay.Outcome != CharacterCreationFoundationOutcomes.Success
                    || replay.Value?.SnapshotDigest != historical.Inputs.SnapshotDigest
                    || replay.Value.EffectivePolicy?.PolicyDigest != historical.Inputs.EffectivePolicy?.PolicyDigest
                    || lease.Stamp != expectedOwner) return Unavailable();
                identity = identity with
                {
                    SourceDigest = executionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
                        or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                        ? inputs.RuleSourceCaptureDigest! : identity.SourceDigest,
                    InputsDigest = inputs.SnapshotDigest,
                    PolicyDigest = inputs.EffectivePolicy!.PolicyDigest,
                    ObservationDigest = string.Empty
                };
                identity = identity with
                {
                    ObservationDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(identity)
                };
                cancellationToken.ThrowIfCancellationRequested();
                execution = new(capture, characterQueries);
                return historical with { Inputs = inputs, Identity = identity };
            }
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }

        static WorkspaceContinuationCompletionResult Unavailable()
            => new(null, null, [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable], []);
    }

    private WorkspaceContinuationCompletionResult LoadCompletionInputsCore(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation, int maximumBytes,
        bool policyConsumption, CancellationToken cancellationToken, IOwnerContextLease? admittedOwner = null,
        ICharacterCreationRuleSourceRecovery? recovery = null,
        string? ruleSourceExecutionSchema = null, bool sourcebookSelection = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (retainedContinuation.Length > maximumBytes)
            return new(null, null, ["continuation-wire-invalid"], []);
        // Caller memory can change even when exposed as ReadOnlyMemory.
        byte[] bytes = retainedContinuation.ToArray();
        var evaluator = new WorkspaceContinuationCandidateEvaluator(
            ownerContext, sourceResolver, characterQueries, lifeModules, ruleSourceExecutionSchema, sourcebookSelection);
        var evaluated = admittedOwner is not null
            ? evaluator.EvaluateCompletionInputsUnderLease(expectedOwner, admittedOwner, bytes,
                maximumBytes, policyConsumption, cancellationToken, recovery)
            : policyConsumption
            ? evaluator.EvaluateCompletionInputsWithPolicyConsumption(expectedOwner, bytes, maximumBytes, cancellationToken)
            : evaluator.Evaluate(expectedOwner, bytes, maximumBytes, includeCompletionInputs: true,
                cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string[] blockers = evaluated.BoundaryBlockers
            .Concat(evaluated.DomainChecks.SelectMany(check => check.Blockers))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] readiness = evaluated.CompletionReadinessBlockers
            .Concat(evaluated.DomainChecks.SelectMany(check => check.ReadinessBlockers))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!evaluated.CurrentDraftChecksPassed || evaluated.CompletionInputs is not { } inputs
            || evaluated.Candidate is not { } candidate || evaluated.SourceDigest is not { } sourceDigest)
            return new(null, null, blockers, readiness);
        var snapshot = candidate.Snapshot.Workspace;
        var identity = new WorkspaceContinuationCompletionIdentity(candidate.Snapshot.OwnerId, snapshot.Id,
            snapshot.ContentRevision, snapshot.SavedRevision, candidate.SnapshotDigest, sourceDigest,
            inputs.Skills.Binding.RawCharacterXmlDigest, inputs.Skills.Binding.AuxiliaryStateDigest,
            inputs.SnapshotDigest, inputs.EffectivePolicy!.PolicyDigest, string.Empty);
        identity = identity with
        {
            ObservationDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(identity)
        };
        return new(inputs, identity, blockers, readiness);
    }
}
