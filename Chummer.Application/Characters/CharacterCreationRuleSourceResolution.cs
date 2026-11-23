using System.Text.Json.Serialization;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>One fresh, retained-only resolution. Implemented by the source engine,
/// never deserialized from an assessment. Its accounting observes the existing merger.</summary>
public abstract partial class CharacterCreationRuleSourceResolution
{
    // Independent current-state catalog and canonical consumption; old adapters fail closed.
    public virtual CharacterCurrentRuleCatalogValidation? ValidateCurrentRuleCatalog(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleDomain domain,
        CancellationToken cancellationToken = default) => null;

    public virtual bool HasValidatedCurrentRuleCatalog(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation validation) => false;

    public virtual CharacterCurrentRuleDomainExecution? ExecuteCurrentRuleDomain(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation validation,
        CancellationToken cancellationToken = default) => null;

    public virtual bool HasExecutedCurrentRuleDomain(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation validation, CharacterCurrentRuleDomainExecution execution) => false;

    [JsonIgnore]
    public abstract ICharacterSourceDataResolver Resolver { get; }

    // Separate exact-candidate read capability. Older adapters keep refusing;
    // the original resolver's captured-character admission remains unchanged.
    public virtual ICharacterSourceDataResolver? CreateCompiledGraphRead(CharacterCreationCompiledGraphRead invocation)
        => null;

    // In-process output checks only. Complete invokes this after canonical
    // pricing/effects execution against this resolution's retained full inputs.
    // No public observation or digest is accepted as a replacement for that call.
    public virtual bool EnablePurchaseSourceExecution() => false;

    // Opt-in observation for the current fixed-gear amendment consumer. This
    // grants no current-environment activation or whole-profile authority.
    public virtual bool EnableCurrentGearSourceExecution() => false;

    // Must precede first resolver access. Selects interpretation only, never
    // retroactively upgrades a capture, consumer report or activation permission.
    public virtual bool EnableCurrentEnvironmentSourceSemantics(string profile, string fingerprint) => false;

    // Implementations must repeat canonical pricing and bind its actual result
    // to their private merger observations. A caller-supplied budget is not proof.
    public virtual IReadOnlyDictionary<string, string> ConsumeCurrentGearSource(
        CharacterCreationGearAuthority authority,
        IReadOnlyList<CharacterCreationGearLine> lines,
        CharacterCreationGearBudget budget)
        => new Dictionary<string, string>(StringComparer.Ordinal);

    // Fresh in-process opt-in, before retained source observation, for Complete
    // source scopes or explicit Essence. Historical purchase capture alone must
    // retain its original accounting.
    public virtual bool EnableCompleteSkillCapSourceExecution() => false;

    // Activate only after the original persisted inputs have been admitted.
    // Book filtering must not rewrite historical catalogue/budget authorities.
    public virtual bool EnableCandidateSourcebookSelection() => false;

    // In-process invocation, created only after canonical consumers and their
    // source-effect compiler finish. Reports retain unconsumed operations.
    public virtual IReadOnlyList<CharacterCreationRuleSourceScopeResult> ExecuteRemainingSourceScopes(
        CharacterCreationRuleSourceScopeInvocation invocation) => [];

    // A copied or resealed report is diagnostic data. Only the concrete
    // retained executor can confirm its own exact completed invocation/result.
    // Default-deny keeps existing adapters from acquiring new aggregate authority.
    public virtual bool HasExecutedSourceScope955(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationRuleSourceScopeResult report) => false;

    // Original private invocation/result only; a partial profile report never
    // proves the whole source. Application still selects its exact typed leaf.
    public virtual bool HasExecutedProfileSetting1203(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationRuleSourceScopeResult report, int operationOrder) => false;

    public virtual IReadOnlyDictionary<string, string> ConsumeGearAndQualitySources(
        CharacterCreationGearAuthority gear, CharacterCreationQualitiesAuthority qualities,
        IReadOnlyList<CharacterCreationGearLine> lines, CharacterCreationGearBudget budget,
        CharacterCreationQualitiesCompletionEvaluation evaluatedQualities,
        IReadOnlyList<CharacterCreationTalentGearSource> grantedGear)
        => new Dictionary<string, string>(StringComparer.Ordinal);

    // Invoked only after the retained canonical point/purchase replay equals
    // the actual full or cold result. Public inputs cannot supply catalogue custody.
    public virtual IReadOnlyDictionary<string, string> ConsumeSkillsSource(
        CharacterCreationSkillsAuthority authority, CharacterCreationCompletionSelections selections,
        CharacterCreationAttributeSkillsPreview evaluated)
        => new Dictionary<string, string>(StringComparer.Ordinal);

    // Only completed canonical skill scopes are supplied by Application. The engine
    // must account for EVERY item in each returned contribution, including selectors.
    // Keys are source obligation IDs; values bind the private ordered semantic trace.
    // Reconstruct and execute from private retained bytes. Never deserialize this capability.
    public virtual CharacterCreationSourcebookSelectionProjection.Result? ConsumeSourcebookSelection(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request) => null;

    // Execute only against this resolution's exhaustive private capture. An absent
    // implementation, empty public inventory or request is not an absence proof.
    public virtual string? ExecuteAmendmentAbsence954() => null;

    public abstract IReadOnlyDictionary<string, string> ConsumeSkillLimits(
        IReadOnlyDictionary<string, int> completedLimits);
}

public sealed record CharacterCreationRuleSourcePreparation(
    CharacterCreationCompletionReviewInputs Inputs,
    [property: JsonIgnore] CharacterCreationRuleSourceExecutionContext Execution);

/// <summary>Explicit in-process dependency at the full/replay boundary. The caller
/// restores the private capture in Infrastructure and supplies its ordinary queries.
/// This is neither a live owner lease nor save authority. No ambient resolver exists.</summary>
public sealed class CharacterCreationRuleSourceExecutionContext(
    CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries)
{
    public byte[] ExportPrivateInputs() => capture.ExportPrivateInputs();

    internal bool UsesReputationCapture8562(CharacterCreationRuleSourceCapture expected, ICharacterFileQueries expectedQueries)
        => ReferenceEquals(capture, expected) && ReferenceEquals(queries, expectedQueries);

    internal bool TryEvaluateReputation(WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationCompletionReviewRequest request,
        CharacterCreationOriginalCharacterSource originalSource,
        CharacterCreationCompleteAssessment authority, CharacterCreationSelectedEffectsConsumer effects,
        out CharacterCreationReputationEffectsExecution? execution, out string error,
        CancellationToken cancellationToken, CharacterCreationCampaignExecutionContext? campaign = null,
        CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion? currentReview8562 = null)
        => CharacterCreationReputationEffectsExecution.TryEvaluate(original, bootstrap, request,
            capture, queries, originalSource, authority, effects, out execution, out error, cancellationToken, campaign, currentReview8562);

    internal CharacterCreationCompleteRuleSourceExecution? Resolve(
        WorkspaceStoredDocument original, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken)
    {
        CharacterCreationCompletionCost3340.Log.Checkpoint(20); // cost3340 resolve.begin
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(request)
            || request.RuleSourceExecutionSchema is null
            || request.RuleSourceCaptureDigest != capture.IntegrityDigest
            || request.SourcebookSelection != capture.SourcebookSelection
            || !capture.MatchesOriginalCharacter(original.Document.Content)) return null;
        try
        {
            var resolution = capture.CreateExecutionResolution(request.RuleSourceExecutionSchema);
            if (resolution is null) return null;
            if (request.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                && !resolution.EnablePurchaseSourceExecution()) return null;
            // Schema admission above validates explicit Essence independently of
            // policy coverage. Its retained executor needs this capability too.
            if ((CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                    || request.EssenceResultSchema == CharacterCreationCompletionReviewSchemas.EssenceResultV1)
                && !resolution.EnableCompleteSkillCapSourceExecution()) return null;
            using var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            CharacterCreationCompletionCost3340.Log.Checkpoint(21); // cost3340 resolve.load.begin
            var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForProposal(
                new WorkspaceContinuationReadView(original), scope ?? resolution.Resolver, queries,
                new(original.Id) { IncludeKarmaPurchasePolicy = true }, request, cancellationToken, capture);
            CharacterCreationCompletionCost3340.Log.Checkpoint(22); // cost3340 resolve.load.returned
            if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } inputs
                || !CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out var source, cancellationToken)) return null;
            CharacterCreationCompletionCost3340.Log.Checkpoint(23); // cost3340 resolve.original.returned
            if (request.SourcebookSelection is not null && !resolution.EnableCandidateSourcebookSelection()) return null;
            return new(inputs, request, resolution, source!, capture.EffectivePolicyDigest);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OverflowException)
        { return null; }
    }

    internal CharacterCreationCompleteRuleSourceExecution? Resolve(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken)
    {
        CharacterCreationCompletionCost3340.Log.Checkpoint(19); // cost3340 resolve.inputs.begin
        if (inputs.OriginalCharacter is not { } captured
            || !CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out _, cancellationToken)) return null;
        var document = new WorkspaceDocument(captured.RawCharacterXml, RulesetDefaults.Sr5);
        document = document with { State = document.State with { AuxiliaryState = captured.AuxiliaryState } };
        // Exact original observation, as in OriginalCharacterSource, never a scratch runner.
        var original = new WorkspaceStoredDocument(captured.WorkspaceId, document,
            captured.ContentRevision, captured.SavedRevision, DateTimeOffset.UnixEpoch);
        var result = Resolve(original, request, cancellationToken);
        CharacterCreationCompletionCost3340.Log.Checkpoint(24); // cost3340 resolve.comparison.begin
        var matched = result is not null && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs, result.Inputs)
            ? result : null;
        CharacterCreationCompletionCost3340.Log.Checkpoint(25); // cost3340 resolve.comparison.returned
        return matched;
    }

    public override string ToString() => nameof(CharacterCreationRuleSourceExecutionContext);
}
