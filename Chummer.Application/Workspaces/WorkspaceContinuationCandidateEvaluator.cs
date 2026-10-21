using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

internal sealed record WorkspaceContinuationDomainCheck(
    string Domain, bool Evaluated, IReadOnlyList<string> Blockers,
    IReadOnlyList<string> ReadinessBlockers);

/// <summary>
/// A bounded read-only observation, never a durable restore/replay authorization.
/// Even valid historical receipt hashes do not authenticate their origin.
/// </summary>
internal sealed record WorkspaceContinuationCandidateEvaluation(
    WorkspaceContinuationExport? Candidate, string? SourceDigest, bool HistoryConsistent,
    IReadOnlyList<WorkspaceContinuationDomainCheck> DomainChecks,
    IReadOnlyList<string> BoundaryBlockers)
{
    public CharacterCreationCompletionReviewInputs? CompletionInputs { get; init; }
    public IReadOnlyList<string> CompletionReadinessBlockers { get; init; } = [];
    public bool CurrentDraftChecksPassed => Candidate is not null && SourceDigest is not null
        && HistoryConsistent && BoundaryBlockers.Count == 0
        && DomainChecks.All(check => check.Evaluated && check.Blockers.Count == 0);
    public bool RestoreAuthorized => false;
    public bool HistoricalProvenanceVerified => false;
}

/// <summary>
/// Recomputes every present Creation draft from Core services against one private
/// candidate and captured source view. No target store is supplied: reads cannot
/// fall back to another owner, and no store mutation or replay can be executed.
/// Atomic restore must separately reacquire owner, sources and destination CAS.
/// </summary>
internal sealed class WorkspaceContinuationCandidateEvaluator(
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver,
    ICharacterFileQueries characterQueries,
    ILifeModulesCatalogService lifeModules,
    string? ruleSourceExecutionSchema = null,
    bool sourcebookSelection = false)
{
    public WorkspaceContinuationCandidateEvaluation Evaluate(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> bytes, int maximumBytes,
        bool includeCompletionInputs = false, CancellationToken cancellationToken = default)
        => EvaluateCore(expectedOwner, bytes, maximumBytes,
            includeCompletionInputs ? CompletionObservation.EffectivePolicy : CompletionObservation.None, cancellationToken);

    /// <summary>Observe actual canonical consumer, knowledge and setting inputs
    /// under the same owner/source/selection checks. This grants no final coverage
    /// or save authority. Caller memory is detached before any source query.</summary>
    public WorkspaceContinuationCandidateEvaluation EvaluateCompletionInputsWithPolicyConsumption(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> retainedContinuation, int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (retainedContinuation.Length > maximumBytes)
            return Failure("continuation-wire-invalid");
        return EvaluateCore(expectedOwner, retainedContinuation.ToArray(), maximumBytes,
            CompletionObservation.PolicyConsumption, cancellationToken);
    }

    // Borrow only the actual lease held by the owner-bound private observation.
    // Internal composition, never an authority object accepted from a wire DTO.
    internal WorkspaceContinuationCandidateEvaluation EvaluateCompletionInputsUnderLease(
        OwnerContextStamp expectedOwner, IOwnerContextLease lease, ReadOnlyMemory<byte> bytes,
        int maximumBytes, bool policyConsumption, CancellationToken cancellationToken,
        ICharacterCreationRuleSourceRecovery? recovery = null)
        => EvaluateCore(expectedOwner, bytes, maximumBytes,
            policyConsumption ? CompletionObservation.PolicyConsumption : CompletionObservation.EffectivePolicy,
            cancellationToken, lease, recovery);

    private enum CompletionObservation { None, EffectivePolicy, PolicyConsumption }

    private WorkspaceContinuationCandidateEvaluation EvaluateCore(
        OwnerContextStamp expectedOwner, ReadOnlyMemory<byte> bytes, int maximumBytes,
        CompletionObservation observation, CancellationToken cancellationToken, IOwnerContextLease? admittedOwner = null,
        ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        bool includeCompletionInputs = observation != CompletionObservation.None;
        bool policyConsumption = observation == CompletionObservation.PolicyConsumption;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        IOwnerContextLease? acquired = null;
        if (admittedOwner is null && !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out acquired))
            return Failure("owner-authority-unavailable");
        using (acquired)
        {
            IOwnerContextLease lease = admittedOwner ?? acquired!;
            if (lease.Stamp != expectedOwner) return Failure("owner-authority-unavailable");
            if (!WorkspaceContinuationCodec.TryDecodeCandidate(bytes, maximumBytes, out var candidate))
                return Failure("continuation-wire-invalid");
            if (!string.Equals(candidate!.Snapshot.OwnerId,
                    expectedOwner.Owner.NormalizedValue, StringComparison.Ordinal))
                return Failure("continuation-owner-mismatch");

            var snapshot = candidate.Snapshot.Workspace;
            var workspace = new WorkspaceStoredDocument(snapshot.Id, snapshot.Document,
                snapshot.ContentRevision, snapshot.SavedRevision, snapshot.LastUpdatedUtc);
            bool historyConsistent = WorkspaceContinuationHistoryIntegrity.TryValidate(
                expectedOwner.Owner, candidate.Snapshot, recovery);
            if (!historyConsistent)
                return Failure("continuation-history-inconsistent", candidate);

            WorkspaceDocument document = workspace.Document;
            if (document.Format != WorkspaceDocumentFormat.NativeXml
                || document.SchemaVersion != 1
                || document.PayloadKind is not ("workspace" or "sr5/chum5-xml")
                || !string.Equals(document.RulesetId, "sr5", StringComparison.Ordinal))
                return Failure("continuation-runtime-unsupported", candidate, historyConsistent);

            var checks = new List<WorkspaceContinuationDomainCheck>();
            var boundary = new List<string>();
            string? sourceDigest = null;
            CharacterCreationCompletionReviewInputs? completionInputs = null;
            IReadOnlyList<string> completionReadiness = [];
            try
            {
                // The requested current grammar must precede the first source capture,
                // including the prerequisite/catalogue checks on a private continuation.
                var admitted = sourceResolver.ForCreationSourceExecution(ruleSourceExecutionSchema);
                if (admitted is null)
                    return Failure("continuation-sources-unavailable", candidate, historyConsistent);
                using ICharacterSourceDataResolverOperationScope? scope = includeCompletionInputs
                    ? (admitted as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope() : null;
                // Legacy frozen DTOs have no consumption-source interface. The
                // explicit path uses the real resolver in one scope that freshly
                // admits sources on every context request, never invented policy.
                if (policyConsumption && scope is null)
                    return Failure("continuation-consumption-source-scope-unavailable", candidate, historyConsistent);
                ICharacterSourceDataResolver resolver = scope ?? admitted;
                // Source files are not a route for parsing external XML entities.
                using var reader = XmlReader.Create(new StringReader(document.Content), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
                });
                XElement root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root
                    ?? throw new InvalidDataException("Missing character root.");
                if (root.Name != "character" || root.HasAttributes)
                    return Failure("continuation-character-invalid", candidate, historyConsistent);
                var character = new CharacterDocument(document.Content);
                CharacterValidationResult validation = characterQueries.Validate(character);
                if (!validation.IsValid)
                    return Failure("continuation-character-invalid", candidate, historyConsistent);
                CharacterFileSummary summary = characterQueries.ParseSummary(character);
                // Reject ambiguous lifecycle/container selection before any first-node
                // parser can disagree with another domain about the candidate.
                foreach (string element in new[] { "created", "contacts", "lifestyles" })
                    if (root.Elements(element).Skip(1).Any())
                        return Failure("continuation-character-ambiguous", candidate, historyConsistent);

                ICharacterSourceDataContext? context = resolver.TryCreateContext(document.Content);
                if (context is null || !WorkspaceContinuationSourceCapture.TryCapture(
                        context, document.Content, lifeModules, out var sources,
                        document.AuxiliaryState.CharacterCreationPrerequisiteDraft, workspace, resolver,
                        includeCompletionInputs))
                    return Failure("continuation-sources-unavailable", candidate, historyConsistent);
                sourceDigest = sources.Digest;
                ICharacterSourceDataResolver frozen = sources.CreateResolver();
                var view = new WorkspaceContinuationReadView(expectedOwner.Owner, workspace);
                var prerequisite = new CharacterCreationPrerequisiteService(view, characterQueries, frozen);
                var attributes = new CharacterCreationAttributesService(view, frozen);
                WorkspaceDocumentAuxiliaryState auxiliary = document.AuxiliaryState;
                // Either representation makes bootstrap validation mandatory.
                // Removing its auxiliary binding must not turn a marked pending
                // character into an apparently empty, valid continuation.
                bool hasBootstrapState = CharacterCreationBootstrapAuthority.HasBootstrapState(document);

                if (summary.Created && (hasBootstrapState || HasActiveDrafts(auxiliary)))
                    boundary.Add("active-creation-drafts-on-career-character");

                if (hasBootstrapState)
                    Run("bootstrap", () =>
                    {
                        bool valid = CharacterCreationBootstrapAuthority.TryValidatePending(
                            workspace, frozen, out var blockers);
                        return Check("bootstrap", valid, blockers);
                    });
                if (auxiliary.CharacterCreationKarmaMetatypeDecisions is not null)
                    Run("karma-metatype", () =>
                    {
                        var result = new CharacterCreationKarmaMetatypeService(view, frozen).Load(workspace.Id);
                        return Check("karma-metatype", result.Value?.Selection is not null, result.Blockers);
                    });
                if (auxiliary.CharacterCreationFoundationDraft is not null)
                    Run("foundation", () =>
                    {
                        var foundation = new CharacterCreationFoundationService(view, characterQueries,
                            frozen, sources.LifeModules, new CharacterCreationFoundationDraftApplyAuthority(view));
                        return Check("foundation", true, foundation.ValidateContinuationDraft(workspace));
                    });
                if (auxiliary.CharacterCreationPrerequisiteDraft is not null)
                    Run("prerequisite", () =>
                    {
                        var result = prerequisite.Load(new(workspace.Id));
                        return Check("prerequisite", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationPrerequisiteBlockers.PersistenceAuthorityRequired,
                            CharacterCreationPrerequisiteBlockers.CharacterAlreadyCreated,
                            CharacterCreationPrerequisiteBlockers.DependentAttributesDraftExists);
                    });
                if (auxiliary.CharacterCreationAttributesDraft is not null)
                    Run("attributes", () =>
                    {
                        var result = attributes.Load(new(workspace.Id));
                        return Check("attributes", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationAttributesBlockers.PersistenceAuthorityRequired);
                    });
                // Composite history owns the effective choices, including domains
                // with no ordinary pending draft. Observe the canonical whole graph
                // against this operation's live sources; the frozen legacy context
                // does not implement consumer/source-execution capabilities.
                bool composite = !summary.Created && auxiliary.CharacterCreationCompletionDrafts is not null;
                if (composite)
                    Run("composite", () =>
                    {
                        var result = OwnerBoundCharacterCreationCompletionReader.ObserveComposite(
                            workspace, resolver, characterQueries, cancellationToken);
                        if (result.Value is { } projection)
                        {
                            checks.Add(Check("skills", projection.Skills is not null,
                                projection.Skills?.Blockers ?? []));
                            checks.Add(Check("magic-resonance", projection.UpstreamBudget?.Magic is not null,
                                projection.UpstreamBudget?.Magic.Blockers ?? []));
                            checks.Add(Check("qualities", projection.UpstreamBudget?.Qualities is not null,
                                projection.UpstreamBudget?.Qualities.Blockers ?? []));
                            checks.Add(Check("resources", projection.Lifestyles?.Gear.Resources is not null,
                                projection.Lifestyles?.Gear.Resources.Blockers ?? []));
                            checks.Add(Check("gear", projection.Lifestyles?.Gear is not null,
                                projection.Lifestyles?.Gear.Blockers ?? []));
                            checks.Add(Check("contacts", projection.Contacts is not null,
                                projection.Contacts?.Blockers ?? []));
                            checks.Add(Check("lifestyles", projection.Lifestyles is not null,
                                projection.Lifestyles?.Blockers ?? []));
                            if (projection.Request.Selections.Loadout is not null)
                                checks.Add(Check("loadout", projection.Loadout is not null,
                                    projection.Loadout?.Blockers ?? []));
                        }
                        // Shared wallet, preservation, selected prerequisites and
                        // cross-domain failures remain blocking as well.
                        return Check("composite", result.Value is not null, result.Blockers);
                    });
                if (!composite && auxiliary.CharacterCreationSkillsDraft is not null)
                    Run("skills", () =>
                    {
                        var result = new CharacterCreationSkillsService(view, frozen)
                            .LoadForContinuation(new(workspace.Id));
                        return Check("skills", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationSkillsBlockers.PersistenceAuthorityRequired);
                    });
                if (!composite && auxiliary.CharacterCreationMagicResonanceDraft is not null)
                    Run("magic-resonance", () =>
                    {
                        var result = new CharacterCreationMagicResonanceService(view, frozen)
                            .LoadForContinuation(new(workspace.Id));
                        return Check("magic-resonance", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired);
                    });
                if (!composite && auxiliary.CharacterCreationQualitiesDraft is not null)
                    Run("qualities", () =>
                    {
                        var result = new CharacterCreationQualitiesService(view, frozen, prerequisite, attributes)
                            .Load(new(workspace.Id));
                        string[] readiness = QualitiesCheckpointIsOnlyReadiness(result.Value?.Binding)
                            ? [CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired,
                               CharacterCreationQualitiesBlockers.CreatedCharacter,
                               CharacterCreationQualitiesBlockers.RevisionConflict]
                            : [CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired,
                               CharacterCreationQualitiesBlockers.CreatedCharacter];
                        return Check("qualities", result.Value?.PendingDraft is not null, result.Blockers, readiness);
                    });
                if (!composite && auxiliary.CharacterCreationResourcesDraft is not null)
                    Run("resources", () => Check("resources", true,
                        new CharacterCreationResourcesService(view, frozen).ValidateContinuationDraft(workspace)));
                if (!composite && auxiliary.CharacterCreationGearDraft is not null)
                    Run("gear", () =>
                    {
                        var result = new CharacterCreationGearService(view, frozen).Load(new(workspace.Id));
                        return Check("gear", result.Value?.PendingDraft is not null, result.Blockers,
                            CharacterCreationGearBlockers.PersistenceAuthorityRequired,
                            CharacterCreationGearBlockers.CareerModeRejected);
                    });

                // Legacy decisions live in XML; bootstrap purchases stay in typed
                // auxiliary drafts. Neither is an active draft after finalization.
                if (!composite && !summary.Created && (auxiliary.CharacterCreationContactReceipts is not null
                        || auxiliary.CharacterCreationContactsDraft is not null
                        || root.Elements("contacts").Any(container => container.HasElements)))
                    Run("contacts", () =>
                    {
                        var result = new CharacterCreationContactsService(view, frozen)
                            .Load(expectedOwner.Owner, new(workspace.Id));
                        return Check("contacts", result.Value is not null, result.Blockers,
                            CharacterCreationContactsBlockers.PersistenceAuthorityRequired,
                            CharacterCreationContactsBlockers.CareerModeRejected);
                    });
                if (!composite && !summary.Created && (auxiliary.CharacterCreationLifestyleReceipts is not null
                        || auxiliary.CharacterCreationLifestylesDraft is not null
                        || root.Elements("lifestyles").Any(container => container.HasElements)))
                    Run("lifestyles", () =>
                    {
                        var result = new CharacterCreationLifestylesService(view, frozen).Load(new(workspace.Id));
                        IEnumerable<string> blockers = result.Blockers;
                        // Load projects overspend without adding Preview's funds blocker.
                        if (result.Value?.Budget.Overspend > 0)
                            blockers = blockers.Append(CharacterCreationLifestylesBlockers.InsufficientFunds);
                        return Check("lifestyles", result.Value is not null, blockers,
                            CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired,
                            CharacterCreationLifestylesBlockers.CareerModeRejected);
                    });
                if (summary.Created)
                    Run("career-reputation", () =>
                    {
                        bool valid = CharacterCareerReputationProjector.TryReadForContinuation(
                            workspace, frozen, out var projection, out string error);
                        return Check("career-reputation", valid && projection is not null,
                            valid ? [] : [error]);
                    });

                cancellationToken.ThrowIfCancellationRequested();
                if (includeCompletionInputs && boundary.Count == 0
                    && checks.All(check => check.Evaluated && check.Blockers.Count == 0))
                {
                    if (summary.Created)
                        completionReadiness = ["continuation-completion-creation-required"];
                    else if (auxiliary.CharacterCreationPrerequisiteDraft is null)
                        completionReadiness = ["continuation-completion-prerequisite-required"];
                    else if (workspace.ContentRevision != workspace.SavedRevision)
                        completionReadiness = ["continuation-completion-checkpoint-required"];
                    else
                    {
                        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadFromObservation(
                            view, policyConsumption ? resolver : frozen, characterQueries,
                            new(workspace.Id) { IncludeKarmaPurchasePolicy = true }, cancellationToken,
                            includeLoadout: true, includeEffectivePolicy: true,
                            includeConsumerPolicy: policyConsumption, knowledgeModifiers: policyConsumption,
                            policyConsumption: policyConsumption,
                            includeOriginalCharacter: ruleSourceExecutionSchema is not null,
                            includeRuleSourceExecution: ruleSourceExecutionSchema is not null,
                            ruleSourceExecutionSchema: ruleSourceExecutionSchema ?? CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
                            includeSourcebooks: sourcebookSelection);
                        completionInputs = loaded.Value;
                        completionReadiness = loaded.Blockers.Where(IsCompletionReadiness).ToArray();
                        boundary.AddRange(loaded.Blockers.Where(code => !IsCompletionReadiness(code)));
                        if (completionInputs?.EffectivePolicy is { } policy
                            && !CharacterCreationEffectivePolicyValidation.Satisfies(policy, CharacterCreationPolicyRequirement.SkillLimits))
                            completionReadiness = completionReadiness.Append(
                                CharacterCreationEffectivePolicyContext.SkillLimitsUnsupported).ToArray();
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();

                // Re-admit every source through this synchronous operation's resolver.
                // Its optional scope validates current bytes and complete catalog/file
                // membership before reusing projections; it is not a cached admission.
                // Keep the full capture/digest comparison and the owner lease. The scope
                // ends with this Evaluate call and never crosses a host await.
                ICharacterSourceDataContext? current = resolver.TryCreateContext(document.Content);
                if (current is null || !WorkspaceContinuationSourceCapture.TryCapture(
                        current, document.Content, lifeModules, out var finalSources,
                        document.AuxiliaryState.CharacterCreationPrerequisiteDraft, workspace, resolver,
                        includeCompletionInputs)
                    || !string.Equals(sourceDigest, finalSources.Digest, StringComparison.Ordinal))
                    boundary.Add("continuation-sources-changed");

                // Preserve the full source check above and also bind the additional
                // consumer/knowledge/setting observation to exact canonical inputs.
                if (policyConsumption && boundary.Count == 0 && completionInputs is not null)
                {
                    var recaptured = OwnerBoundCharacterCreationCompletionReader.LoadFromObservation(
                        view, resolver, characterQueries,
                        new(workspace.Id) { IncludeKarmaPurchasePolicy = true }, cancellationToken,
                        includeLoadout: true, includeEffectivePolicy: true, includeConsumerPolicy: true,
                        knowledgeModifiers: true, policyConsumption: true,
                        includeOriginalCharacter: ruleSourceExecutionSchema is not null,
                        includeRuleSourceExecution: ruleSourceExecutionSchema is not null,
                        ruleSourceExecutionSchema: ruleSourceExecutionSchema ?? CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
                        includeSourcebooks: sourcebookSelection);
                    if (recaptured.Value is not { } currentInputs || recaptured.Blockers.Count != 0
                        || !CharacterCreationSkillsDigest.EqualsFixedTime(
                            completionInputs.SnapshotDigest, currentInputs.SnapshotDigest)
                        || !CharacterCreationSkillsDigest.EqualsFixedTime(
                            completionInputs.EffectivePolicy?.PolicyDigest, currentInputs.EffectivePolicy?.PolicyDigest))
                        boundary.Add("continuation-sources-changed");
                }

                // Hash against bounded captured bytes again: an internal consumer must
                // not have mutated the supposedly private candidate during evaluation.
                _ = WorkspaceContinuationCodec.Encode(candidate, maximumBytes);
                if (includeCompletionInputs && (lease.Stamp != expectedOwner
                    || !string.Equals(candidate.Snapshot.OwnerId, expectedOwner.Owner.NormalizedValue, StringComparison.Ordinal)
                    || !string.Equals(candidate.SnapshotDigest,
                        WorkspaceContinuationSnapshotDigest.Compute(candidate.Snapshot), StringComparison.Ordinal)
                    || !WorkspaceContinuationHistoryIntegrity.TryValidate(expectedOwner.Owner, candidate.Snapshot, recovery)))
                    boundary.Add("continuation-observation-changed");
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception) when (IsMalformedOrUnavailable(exception))
            {
                boundary.Add("continuation-evaluation-unavailable");
            }
            return new(candidate, sourceDigest, historyConsistent,
                checks.OrderBy(check => check.Domain, StringComparer.Ordinal).ToArray(),
                boundary.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray())
            {
                CompletionInputs = boundary.Count == 0 ? completionInputs : null,
                CompletionReadinessBlockers = completionReadiness.ToArray()
            };

            void Run(string domain, Func<WorkspaceContinuationDomainCheck> evaluate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { checks.Add(evaluate()); }
                catch (Exception exception) when (IsMalformedOrUnavailable(exception))
                {
                    checks.Add(Check(domain, false, ["continuation-domain-unavailable"]));
                }
            }
        }
    }

    private static bool HasActiveDrafts(WorkspaceDocumentAuxiliaryState state) =>
        state.CharacterCreationBootstrapBinding is not null
        || state.CharacterCreationKarmaMetatypeDecisions is not null
        || state.CharacterCreationFoundationDraft is not null
        || state.CharacterCreationPrerequisiteDraft is not null
        || state.CharacterCreationAttributesDraft is not null
        || state.CharacterCreationSkillsDraft is not null
        || state.CharacterCreationMagicResonanceDraft is not null
        || state.CharacterCreationQualitiesDraft is not null
        || state.CharacterCreationResourcesDraft is not null
        || state.CharacterCreationGearDraft is not null
        || state.CharacterCreationContactsDraft is not null
        || state.CharacterCreationLifestylesDraft is not null;

    private static bool IsCompletionReadiness(string code) => code is
        CharacterCreationAttributesBlockers.PrerequisiteDraftRequired
        or CharacterCreationAttributesBlockers.PersistenceAuthorityRequired
        or CharacterCreationSkillsBlockers.AttributesDraftRequired
        or CharacterCreationSkillsBlockers.PersistenceAuthorityRequired
        or CharacterCreationMagicResonanceBlockers.AttributesDraftRequired
        or CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired
        or CharacterCreationQualitiesBlockers.PrerequisiteDraftRequired
        or CharacterCreationQualitiesBlockers.AttributesDraftRequired
        or CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired
        or CharacterCreationGearBlockers.ResourcesDraftRequired
        or CharacterCreationGearBlockers.PersistenceAuthorityRequired;

    private static bool QualitiesCheckpointIsOnlyReadiness(CharacterCreationQualitiesBinding? binding) =>
        binding is not null && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
        && binding.ContentRevision > 0 && binding.SavedRevision > 0
        && binding.SavedRevision < binding.ContentRevision
        && CharacterCreationQualitiesRules.IsCanonicalDigest(binding.RawCharacterXmlDigest)
        && DelegatedGmCharacterEditLedgerValidator.IsSha256(binding.AuxiliaryStateDigest);

    private static WorkspaceContinuationDomainCheck Check(
        string domain, bool evaluated, IEnumerable<string> blockers, params string[] readiness)
    {
        string[] all = blockers.Distinct(StringComparer.Ordinal).ToArray();
        var blocking = all.Where(code => !readiness.Contains(code, StringComparer.Ordinal)).ToList();
        if (!evaluated && blocking.Count == 0)
            blocking.Add("continuation-domain-unavailable");
        return new(domain, evaluated,
            blocking.OrderBy(code => code, StringComparer.Ordinal).ToArray(),
            all.Where(code => readiness.Contains(code, StringComparer.Ordinal))
                .OrderBy(code => code, StringComparer.Ordinal).ToArray());
    }

    private static bool IsMalformedOrUnavailable(Exception exception) => exception is
        ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException
        or XmlException or JsonException or FormatException or OverflowException
        or NullReferenceException or IndexOutOfRangeException or KeyNotFoundException or NotSupportedException;

    private static WorkspaceContinuationCandidateEvaluation Failure(
        string blocker, WorkspaceContinuationExport? candidate = null, bool historyConsistent = false) =>
        new(candidate, null, historyConsistent, [], [blocker]);
}
