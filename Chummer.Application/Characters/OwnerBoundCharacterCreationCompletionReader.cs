using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Reads the original completion inputs under one exact owner lease, one source
/// operation scope and one actual workspace observation. It never creates drafts
/// or writes a runner; the detached bundle must still pass the combined review.
/// </summary>
public sealed class OwnerBoundCharacterCreationCompletionReader(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver,
    ICharacterFileQueries characterQueries) : IOwnerBoundCharacterCreationCompletionReader
{
    public CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithRuleSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => LoadRuleSourceExecution(expectedOwner, request, includeLoadout, cancellationToken,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1);

    public CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithPurchaseSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => LoadRuleSourceExecution(expectedOwner, request, includeLoadout, cancellationToken,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2);

    public CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithSourcebookSelectionExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        bool includeLoadout = false, CancellationToken cancellationToken = default)
        => LoadRuleSourceExecution(expectedOwner, request, includeLoadout, cancellationToken,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, sourcebooks: true);

    // New requests explicitly opt in. Existing sourcebook and purchase readers
    // retain V2 so historical callers do not acquire different graph output.
    public CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithMovementSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        bool includeLoadout = false, bool includeSourcebooks = false, CancellationToken cancellationToken = default)
        => LoadRuleSourceExecution(expectedOwner, request, includeLoadout, cancellationToken,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, sourcebooks: includeSourcebooks);

    public CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithInitiativeSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        bool includeLoadout = false, bool includeSourcebooks = false, CancellationToken cancellationToken = default)
        => LoadRuleSourceExecution(expectedOwner, request, includeLoadout, cancellationToken,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4, sourcebooks: includeSourcebooks);

    private CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadRuleSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout,
        CancellationToken cancellationToken, string ruleSourceExecutionSchema, bool sourcebooks = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Conflict, null, [CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
        using (lease)
        {
            CharacterCreationRuleSourceCapture? capture = null;
            CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> loaded;
            using (var scope = (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope())
            {
                var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.WorkspaceId, reuseReadObservation: true);
                loaded = LoadFromObservation(view, scope ?? sourceResolver, characterQueries,
                    request with { IncludeKarmaPurchasePolicy = true }, cancellationToken, includeLoadout,
                    includeEffectivePolicy: true, includeConsumerPolicy: true, knowledgeModifiers: true,
                    policyConsumption: true, includeOriginalCharacter: true, includeRuleSourceExecution: true,
                    captured: value => capture = value, ruleSourceExecutionSchema: ruleSourceExecutionSchema, includeSourcebooks: sourcebooks);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.Stamp != expectedOwner || loaded.Value is null || capture is null)
                return new(CharacterCreationFoundationOutcomes.Blocked, null,
                    loaded.Blockers.Count > 0 ? loaded.Blockers : [CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
            return new(CharacterCreationFoundationOutcomes.Success,
                new(loaded.Value, new(capture, characterQueries)), []);
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithOriginalCharacter(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => Load(expectedOwner, request with { IncludeKarmaPurchasePolicy = true }, includeLoadout,
            cancellationToken, includeEffectivePolicy: true, includeConsumerPolicy: true,
            knowledgeModifiers: true, policyConsumption: true, includeOriginalCharacter: true);

    /// <summary>Explicit current setting-accounting read. Historical entry points
    /// retain their exact schemas and omitted fields.</summary>
    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithPolicyConsumption(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => Load(expectedOwner, request with { IncludeKarmaPurchasePolicy = true }, includeLoadout, cancellationToken,
            includeEffectivePolicy: true, includeConsumerPolicy: true, knowledgeModifiers: true, policyConsumption: true);

    /// <summary>Opt-in source-backed category knowledge pricing. Ordinary readers
    /// and already recorded consumer versions keep their original catalogs/bytes.</summary>
    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithKnowledgeModifiers(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => Load(expectedOwner, request with { IncludeKarmaPurchasePolicy = true }, includeLoadout, cancellationToken,
            includeEffectivePolicy: true, includeConsumerPolicy: true, knowledgeModifiers: true);

    /// <summary>Explicit read/review opt-in for typed consumer policy evidence.
    /// V4 selected replay independently verifies its bounded original-input projection.</summary>
    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithConsumerPolicy(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => Load(expectedOwner, request, includeLoadout, cancellationToken,
            includeEffectivePolicy: true, includeConsumerPolicy: true);

    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithEffectivePolicy(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => Load(expectedOwner, request, includeLoadout, cancellationToken, includeEffectivePolicy: true);

    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        CancellationToken cancellationToken = default)
        => Load(expectedOwner, request, false, cancellationToken);

    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithLoadout(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        CancellationToken cancellationToken = default)
        => Load(expectedOwner, request, true, cancellationToken);

    private CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> Load(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        bool includeLoadout, CancellationToken cancellationToken, bool includeEffectivePolicy = false,
        bool includeConsumerPolicy = false, bool knowledgeModifiers = false, bool policyConsumption = false,
        bool includeOriginalCharacter = false)
    {
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict],
                CharacterCreationFoundationOutcomes.Conflict);
        using (lease)
        {
            using ICharacterSourceDataResolverOperationScope? scope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            ICharacterSourceDataResolver resolver = scope ?? sourceResolver;
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner,
                request.WorkspaceId, reuseReadObservation: true);
            var result = LoadFromObservation(view, resolver, characterQueries, request,
                cancellationToken, includeLoadout, includeEffectivePolicy, includeConsumerPolicy, knowledgeModifiers: knowledgeModifiers,
                policyConsumption: policyConsumption, includeOriginalCharacter: includeOriginalCharacter);
            cancellationToken.ThrowIfCancellationRequested();
            return lease.Stamp == expectedOwner ? result
                : Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict],
                    CharacterCreationFoundationOutcomes.Conflict);
        }
    }

    // The proposal can only select an exactly matching server-derived variant. No
    // client version flag, copied digest or portable archive grants live authority.
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadForProposal(
        IWorkspaceStore view, ICharacterSourceDataResolver resolver, ICharacterFileQueries characterQueries,
        CharacterCreationSkillsLoadRequest request, CharacterCreationCompletionReviewRequest proposal,
        CancellationToken cancellationToken, CharacterCreationRuleSourceCapture? retainedCapture = null,
        Action<CharacterCreationRuleSourceCapture>? captured = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCreationCompletionReviewRules.HasValidAdditionalConsumerExecution(proposal)
            || !CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(proposal)
            || !CharacterCreationCompletionReviewRules.HasValidMaximumAttributesDefaultExecution(proposal)
            || !CharacterCreationCompletionReviewRules.HasValidOriginalCharacterExecution(proposal))
            return Reject([CharacterCreationCompleteAssessment.Invalid]);
        return LoadFromObservation(view, resolver, characterQueries, request, cancellationToken,
            includeLoadout: proposal.Selections.Loadout is not null,
            includeEffectivePolicy: proposal.EffectivePolicyDigest is not null || proposal.PolicyRequirement != CharacterCreationPolicyRequirement.None,
            includeConsumerPolicy: proposal.PolicyConsumptionSchema is not null,
            expectedPolicyDigest: proposal.EffectivePolicyDigest, expectedInputsDigest: proposal.InputsSnapshotDigest,
            knowledgeModifiers: proposal.PolicyConsumptionSchema is not null,
            policyConsumption: proposal.PolicyConsumptionSchema is not null,
            includeOriginalCharacter: proposal.OriginalCharacterExecutionSchema is not null,
            includeRuleSourceExecution: proposal.RuleSourceExecutionSchema is not null, retainedCapture: retainedCapture,
            expectedCaptureDigest: proposal.RuleSourceCaptureDigest, captured: captured,
            ruleSourceExecutionSchema: proposal.RuleSourceExecutionSchema ?? CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
            includeSourcebooks: proposal.SourcebookSelection is not null,
            expectedSourcebooks: proposal.SourcebookSelection);
    }

    // The caller owns the exact owner lease, source operation scope and real
    // workspace observation. Reused by the read-only proposed-cash route; it
    // never accepts a constructed intermediate draft or portable save authority.
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadFromObservation(
        IWorkspaceStore view, ICharacterSourceDataResolver resolver, ICharacterFileQueries characterQueries,
        CharacterCreationSkillsLoadRequest request, CancellationToken cancellationToken, bool includeLoadout = false,
        bool includeEffectivePolicy = false, bool includeConsumerPolicy = false,
        string? expectedPolicyDigest = null, string? expectedInputsDigest = null, bool knowledgeModifiers = false, bool policyConsumption = false,
        bool includeOriginalCharacter = false, bool includeRuleSourceExecution = false,
        CharacterCreationRuleSourceCapture? retainedCapture = null, string? expectedCaptureDigest = null,
        Action<CharacterCreationRuleSourceCapture>? captured = null,
        string ruleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
        bool includeSourcebooks = false, CharacterCreationSourcebookSelectionAuthority? expectedSourcebooks = null)
    {
        if (includeRuleSourceExecution && ruleSourceExecutionSchema is not
            (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4))
            return Reject([CharacterCreationCompleteAssessment.Invalid]);
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        cancellationToken.ThrowIfCancellationRequested();
        if (includeEffectivePolicy && view is not WorkspaceContinuationReadView)
        {
            var observation = view.Get(request.WorkspaceId);
            if (!observation.Success || observation.Value is null)
                return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
            // V3 capture and later V3 review reloads have the same pure input
            // identity regardless of the enclosing operation's write capability.
            view = new WorkspaceContinuationReadView(observation.Value);
        }
        // Admit before capture as well as catalogue/prerequisite/quality reads.
        // Sourcebook capture may replace this with its accounting-enabled resolver.
        var versionedResolver = resolver.ForCreationSourceExecution(includeRuleSourceExecution ? ruleSourceExecutionSchema : null);
        if (versionedResolver is null) return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
        resolver = versionedResolver;
        string? sourcebookCharacterXml = null;
        if (includeOriginalCharacter)
        {
            var observed = view.Get(request.WorkspaceId);
            if (!observed.Success || observed.Value is null
                || !CharacterCreationOriginalCharacterSource.TryParse(observed.Value.Document.Content, out _, cancellationToken))
                return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
            sourcebookCharacterXml = observed.Value.Document.Content;
        }
        if (includeSourcebooks)
        {
            bool captureSupplied800 = retainedCapture is not null;
            if (!includeRuleSourceExecution || !includeOriginalCharacter || sourcebookCharacterXml is null
                || ruleSourceExecutionSchema is not (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                || retainedCapture is null && (resolver is not ICharacterCreationRuleSourceCapture producer
                    || !producer.TryCaptureSourcebookRuleSources(sourcebookCharacterXml, out retainedCapture))
                || retainedCapture?.SourcebookSelection is not { } sourcebooks || !sourcebooks.IsValid()
                || !retainedCapture.MatchesOriginalCharacter(sourcebookCharacterXml)
                || expectedSourcebooks is not null && sourcebooks != expectedSourcebooks
                || expectedCaptureDigest is not null && retainedCapture.IntegrityDigest != expectedCaptureDigest)
                return Reject([CharacterCreationSourcebookSelectionAuthority.Unavailable]);
            // Resolve already supplied a fresh accounting-enabled resolution. Retain
            // that exact instance so its source/effect obligations still observe reads.
            if (!captureSupplied800)
            {
                var sourceResolution = retainedCapture.CreateExecutionResolution(ruleSourceExecutionSchema);
                if (sourceResolution is null) return Reject([CharacterCreationSourcebookSelectionAuthority.Invalid]);
                resolver = sourceResolution.Resolver;
            }
        }
        else if (retainedCapture?.SourcebookSelection is not null || expectedSourcebooks is not null)
            return Reject([CharacterCreationSourcebookSelectionAuthority.Invalid]);

        using var versionedScope = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        resolver = versionedScope ?? resolver;
        var skills = new CharacterCreationSkillsService(view, resolver).LoadCompletionInputs(request);
        if (skills.Value is null) return Reject(skills.Blockers);
        cancellationToken.ThrowIfCancellationRequested();
        var magic = new CharacterCreationMagicResonanceService(view, resolver)
            .LoadCompletionInputs(new(request.WorkspaceId));
        if (magic.Value is null) return Reject(magic.Blockers);
        cancellationToken.ThrowIfCancellationRequested();
        var lifestyles = new CharacterCreationLifestylesService(view, resolver, characterQueries)
            .LoadCompletionInputs(new(request.WorkspaceId), knowledgeModifiers);
        if (lifestyles.Value is null) return Reject(lifestyles.Blockers);
        cancellationToken.ThrowIfCancellationRequested();
        var contacts = new CharacterCreationContactsService(view, resolver, characterQueries)
            .LoadCompletionInputs(new(request.WorkspaceId), knowledgeModifiers);
        if (contacts.Value is null) return Reject(contacts.Blockers);
        cancellationToken.ThrowIfCancellationRequested();

        CharacterCreationLoadoutCompletionInputs? loadout = null;
        // A saved typed purchase cannot disappear through the older entry point.
        if (!includeLoadout)
            includeLoadout = lifestyles.Value.Gear.Resources.LoadoutDraft is not null
                || skills.Value.CompositeBaseline?.Selections.Loadout is not null;
        if (includeLoadout)
        {
            var original = view.Get(request.WorkspaceId);
            if (!original.Success || original.Value is null)
                return Reject([CharacterCreationLoadoutCompletionSchemas.SnapshotConflict]);
            var loaded = CharacterCreationLoadoutCompletionService.LoadFromObservation(original.Value, resolver,
                requireAuthority: true);
            if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success) return Reject(loaded.Blockers);
            loadout = loaded.Value;
        }
        cancellationToken.ThrowIfCancellationRequested();

        CharacterCreationEffectivePolicyContext? policy = null;
        CharacterCreationAttributePolicy? attributePolicy = null;
        CharacterCreationLoadoutLegalityPolicy? loadoutLegalityPolicy = null;
        if (includeEffectivePolicy)
        {
            var original = view.Get(request.WorkspaceId);
            var context = original.Success && original.Value is not null
                ? resolver.TryCreateContext(original.Value.Document.Content) : null;
            if (context is null || !TryResolvePolicy(context, policyConsumption, out policy) || policy is null)
                return Reject([CharacterCreationEffectivePolicyContext.Unavailable]);
            if (!CharacterCreationEffectivePolicyValidation.IsValid(policy, skills.Value))
                return Reject([CharacterCreationEffectivePolicyContext.Invalid]);
            // Derive inventory and typed consumer variants from this one real readonly
            // observation. A nonmatching inventory digest only requests derivation;
            // exact policy AND original-input equality below is still mandatory.
            includeConsumerPolicy |= expectedPolicyDigest is not null && expectedPolicyDigest != policy.PolicyDigest;
            if (includeConsumerPolicy)
            {
                // Same admitted source scope and original observation as the domain reads.
                // An old resolver can omit this input; coverage then explicitly stays missing.
                if (!context.TryResolveCreationAttributePolicy(out attributePolicy))
                    attributePolicy = null;
                if (loadout is not null && !context.TryResolveCreationLoadoutLegalityPolicy(out loadoutLegalityPolicy))
                    loadoutLegalityPolicy = null;
                // False can also mean that a supplemental read poisoned the source
                // scope. Re-admit the original observation before treating absence as
                // missing coverage; never replace its inventory with a newer one.
                var recapturedContext = resolver.TryCreateContext(original.Value!.Document.Content);
                if (recapturedContext is null
                    || !TryResolvePolicy(recapturedContext, policyConsumption, out var recapturedPolicy)
                    || recapturedPolicy is null)
                    return Reject([CharacterCreationEffectivePolicyContext.Unavailable]);
                if (!CharacterCreationEffectivePolicyValidation.IsValid(recapturedPolicy, skills.Value)
                    || recapturedPolicy.PolicyDigest != policy.PolicyDigest)
                    return Reject([CharacterCreationEffectivePolicyContext.Invalid]);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        CharacterCreationCompletionOriginalCharacter? originalCharacter = null;
        if (includeOriginalCharacter)
        {
            var original = view.Get(request.WorkspaceId);
            var context = original.Value is null ? null : resolver.TryCreateContext(original.Value.Document.Content);
            if (!original.Success || original.Value is null || context is null
                || !CharacterCreationOriginalCharacterSource.TryCapture(original.Value, context, out originalCharacter, cancellationToken)
                || !TryResolvePolicy(context, policyConsumption, out var recapturedPolicy)
                || recapturedPolicy?.PolicyDigest != policy?.PolicyDigest)
                return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
        }
        var inputs = new CharacterCreationCompletionReviewInputs(
            policy is not null ? CharacterCreationCompletionReviewSchemas.InputsV3
                : loadout is null ? CharacterCreationCompletionReviewSchemas.InputsV1 : CharacterCreationCompletionReviewSchemas.InputsV2,
            skills.Value, magic.Value, lifestyles.Value, contacts.Value, string.Empty)
            { Loadout = loadout, EffectivePolicy = policy, AttributePolicy = attributePolicy, LoadoutLegalityPolicy = loadoutLegalityPolicy,
                OriginalCharacter = originalCharacter };
        if (includeRuleSourceExecution)
        {
            var observed = view.Get(request.WorkspaceId);
            var capture = retainedCapture;
            if (!includeOriginalCharacter || observed.Value is null
                || capture is null && (resolver is not ICharacterCreationRuleSourceCapture sourceCapture
                    || !sourceCapture.TryCaptureRuleSources(observed.Value.Document.Content, out capture))
                || capture is null || !capture.MatchesOriginalCharacter(observed.Value.Document.Content)
                || expectedCaptureDigest is not null && capture.IntegrityDigest != expectedCaptureDigest)
                return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
            // Inventory and consumer policy have distinct digests. Re-resolve the raw
            // inventory here; never compare it with the enriched consumer policy.
            var context = resolver.TryCreateContext(observed.Value.Document.Content);
            if (context is null || !context.TryResolveCreationEffectivePolicy(out var inventory)
                || inventory?.PolicyDigest != capture.EffectivePolicyDigest)
                return Reject([CharacterCreationEffectivePolicyContext.Invalid]);
            inputs = inputs with { RuleSourceExecutionSchema = ruleSourceExecutionSchema,
                RuleSourceCaptureDigest = capture.IntegrityDigest, SourcebookSelection = capture.SourcebookSelection };
            if (ruleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            {
                if (observed.Value.Document.RulesetId != "sr5"
                    || !context.TryResolveCreationPhysicalInitiative886(inputs.Skills.PrerequisiteDraft, out var initiative)
                    || initiative is null) return Reject([CharacterCreationPhysicalInitiativeAuthority.Unavailable]);
                inputs = inputs with { PhysicalInitiative886 = initiative };
            }
            captured?.Invoke(capture);
        }
        if (includeConsumerPolicy)
        {
            if (policy is null) return Reject([CharacterCreationEffectivePolicyContext.Unavailable]);
            // The consumer input digest includes the rule-source identity. Attach
            // only after capture admission has populated every original input.
            policy = CharacterCreationCompletionConsumerPolicyProjection.Attach(policy, inputs, knowledgeModifiers);
            inputs = inputs with { EffectivePolicy = policy };
            if (!CharacterCreationEffectivePolicyValidation.IsValid(policy, inputs))
                return Reject([CharacterCreationEffectivePolicyContext.Invalid]);
        }
        inputs = inputs with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs) };
        if (includeOriginalCharacter && !CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out _, cancellationToken))
            return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
        if (expectedInputsDigest is not null && includeEffectivePolicy
            && (inputs.EffectivePolicy?.PolicyDigest != expectedPolicyDigest || inputs.SnapshotDigest != expectedInputsDigest))
        {
            // Derive a second, explicitly versioned variant from the SAME lease and
            // source observation. It must match both expected digests; no fallback repricing.
            if (!knowledgeModifiers && includeConsumerPolicy && request.IncludeKarmaPurchasePolicy)
                return LoadFromObservation(view, resolver, characterQueries, request, cancellationToken,
                    includeLoadout, true, true, expectedPolicyDigest, expectedInputsDigest, knowledgeModifiers: true,
                    policyConsumption: policyConsumption, includeOriginalCharacter: includeOriginalCharacter,
                    includeRuleSourceExecution: includeRuleSourceExecution, retainedCapture: retainedCapture,
                    expectedCaptureDigest: expectedCaptureDigest, captured: captured, ruleSourceExecutionSchema: ruleSourceExecutionSchema, includeSourcebooks: includeSourcebooks, expectedSourcebooks: expectedSourcebooks);
            if (!policyConsumption && knowledgeModifiers && includeConsumerPolicy && request.IncludeKarmaPurchasePolicy)
                return LoadFromObservation(view, resolver, characterQueries, request, cancellationToken,
                    includeLoadout, true, true, expectedPolicyDigest, expectedInputsDigest, knowledgeModifiers: true,
                    policyConsumption: true, includeOriginalCharacter: includeOriginalCharacter,
                    includeRuleSourceExecution: includeRuleSourceExecution, retainedCapture: retainedCapture,
                    expectedCaptureDigest: expectedCaptureDigest, captured: captured, ruleSourceExecutionSchema: ruleSourceExecutionSchema, includeSourcebooks: includeSourcebooks, expectedSourcebooks: expectedSourcebooks);
            return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict], CharacterCreationFoundationOutcomes.Conflict);
        }
        return new(CharacterCreationFoundationOutcomes.Success,
            JsonSerializer.Deserialize<CharacterCreationCompletionReviewInputs>(JsonSerializer.SerializeToUtf8Bytes(inputs))!, []);
    }

    // Explicit queries and an already owner-bound observation are mandatory.
    // A source resolver is never used to discover an ambient query service.
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadForCompositeEdit(
        WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver, ICharacterFileQueries? queries)
    {
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        if (queries is null || !CharacterCreationCompositeBaselineReader.TryRead(workspace, out var composite)
            || composite is null)
            return Reject([CharacterCreationCompositeBaselineReader.RebaseRequired]);
        var entry = workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1];
        if (!CharacterCreationCompletionReplayCodec.TryDecode(entry.Baseline.SelectedReplay, out _, out var historical))
            return Reject([CharacterCreationCompositeBaselineReader.Invalid]);
        // History chooses the consumer version only. Every authority is read
        // afresh from the explicit live resolver below.
        return LoadFromObservation(new WorkspaceContinuationReadView(workspace), resolver, queries,
            new(workspace.Id) { IncludeKarmaPurchasePolicy = composite.Selections.SkillKarmaPurchases is not null },
            CancellationToken.None, includeLoadout: composite.Selections.Loadout is not null,
            includeEffectivePolicy: historical.EffectivePolicy is not null,
            includeConsumerPolicy: historical.EffectivePolicy?.Consumers is not null,
            knowledgeModifiers: historical.EffectivePolicy?.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5,
            policyConsumption: historical.EffectivePolicy?.SettingObservations is not null);
    }

    // Caller already owns the exact lease and validated history/recovery scope.
    // History selects execution versions and choices, never current sources or
    // campaign authority. This result is consumed only as a continuation check.
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReview> ObserveComposite(
        WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver,
        ICharacterFileQueries queries, CancellationToken cancellationToken)
    {
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCreationCompositeBaselineReader.TryRead(workspace, out var composite)
            || composite is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationCompositeBaselineReader.Invalid]);
        var entry = workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1];
        if (!CharacterCreationCompletionReplayCodec.TryDecode(entry.Baseline.SelectedReplay, out _, out var historical))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationCompositeBaselineReader.Invalid]);
        var previous = entry.Baseline.Review.Review.Request;
        CharacterCreationRuleSourceCapture? capture = null;
        var loaded = LoadFromObservation(new WorkspaceContinuationReadView(workspace), resolver, queries,
            new(workspace.Id) { IncludeKarmaPurchasePolicy = historical.Skills.KarmaPurchasePolicy is not null
                || composite.Selections.SkillKarmaPurchases is not null }, cancellationToken,
            includeLoadout: historical.Loadout is not null || composite.Selections.Loadout is not null,
            includeEffectivePolicy: historical.EffectivePolicy is not null,
            includeConsumerPolicy: historical.EffectivePolicy?.Consumers is not null,
            knowledgeModifiers: historical.EffectivePolicy?.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5,
            policyConsumption: historical.EffectivePolicy?.SettingObservations is not null,
            includeOriginalCharacter: previous.OriginalCharacterExecutionSchema is not null,
            includeRuleSourceExecution: previous.RuleSourceExecutionSchema is not null,
            captured: value => capture = value,
            ruleSourceExecutionSchema: previous.RuleSourceExecutionSchema ?? CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
            includeSourcebooks: previous.SourcebookSelection is not null);
        if (loaded.Value is not { } inputs || loaded.Blockers.Count != 0)
            return new(loaded.Outcome, null, loaded.Blockers);
        var request = previous with
        {
            InputsSnapshotDigest = inputs.SnapshotDigest, Selections = composite.Selections,
            EffectivePolicyDigest = inputs.EffectivePolicy?.PolicyDigest,
            RuleSourceCaptureDigest = inputs.RuleSourceCaptureDigest,
            SourcebookSelection = inputs.SourcebookSelection
        };
        return CharacterCreationCompletionReviewService.ObserveComposite(inputs, request,
            cancellationToken, capture is null ? null : new CharacterCreationRuleSourceExecutionContext(capture, queries));
    }

    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReview> ReviewCompositeEdit(
        WorkspaceStoredDocument workspace, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionSelections choices, CharacterCreationCompositeEditStage stage)
    {
        if (inputs.Skills.CompositeBaseline?.Selections is not { } original)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompositeBaselineReader.Invalid]);
        var expected = stage switch
        {
            CharacterCreationCompositeEditStage.Magic => original with { MagicSelections = choices.MagicSelections },
            CharacterCreationCompositeEditStage.Qualities => original with { QualityOptionIds = choices.QualityOptionIds },
            CharacterCreationCompositeEditStage.Resources => original with { TotalKarmaInvestment = choices.TotalKarmaInvestment },
            CharacterCreationCompositeEditStage.Gear => original with { Gear = choices.Gear },
            CharacterCreationCompositeEditStage.Loadout => original with { Loadout = choices.Loadout },
            CharacterCreationCompositeEditStage.Contacts => original with { Contacts = choices.Contacts },
            CharacterCreationCompositeEditStage.Lifestyles => original with
            {
                Lifestyles = choices.Lifestyles,
                StartingLifestyleId = CharacterCreationCompositeBaselineReader.ResolveStartingLifestyle(
                    original.StartingLifestyleId, choices.Lifestyles)
            },
            _ => original
        };
        if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, choices))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict]);
        var previous = workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1]
            .Baseline.Review.Review.Request;
        return CharacterCreationCompletionReviewService.ReviewCompositeEdit(inputs, previous with
        {
            InputsSnapshotDigest = inputs.SnapshotDigest, Selections = choices,
            EffectivePolicyDigest = inputs.EffectivePolicy?.PolicyDigest
        }, stage);
    }

    private static bool TryResolvePolicy(ICharacterSourceDataContext context, bool consumption,
        out CharacterCreationEffectivePolicyContext? policy)
    {
        if (!consumption) return context.TryResolveCreationEffectivePolicy(out policy);
        policy = null;
        return context is ICharacterCreationPolicyConsumptionSource source && source.TryResolveCreationPolicyConsumption(out policy);
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> Reject(
        IReadOnlyList<string> blockers, string outcome = CharacterCreationFoundationOutcomes.Blocked)
        => new(outcome, null, blockers.Count == 0
            ? [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable] : blockers.ToArray());
}

// Pure rule readiness is separate from editor/save readiness. No semantic,
// source, history or revision blocker is waived, and CanEdit is never granted.
internal static class CharacterCreationCompletionReadiness
{
    internal static bool RulesReady(bool canEdit, IReadOnlyList<string>? blockers, string persistenceBlocker)
        => blockers is not null && (canEdit ? blockers.Count == 0
            : blockers.Count == 1 && blockers[0] == persistenceBlocker);
}
