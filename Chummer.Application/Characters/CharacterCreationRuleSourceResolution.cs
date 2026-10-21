using System.Text.Json.Serialization;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>One fresh, retained-only resolution. Implemented by the source engine,
/// never deserialized from an assessment. Its accounting observes the existing merger.</summary>
public abstract class CharacterCreationRuleSourceResolution
{
    [JsonIgnore]
    public abstract ICharacterSourceDataResolver Resolver { get; }

    // In-process output checks only. Complete invokes this after canonical
    // pricing/effects execution against this resolution's retained full inputs.
    // No public observation or digest is accepted as a replacement for that call.
    public virtual bool EnablePurchaseSourceExecution() => false;

    // Fresh in-process Complete/V2 opt-in, before retained source observation.
    // Historical V2 purchase capture alone must retain its original accounting.
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

    internal bool TryEvaluateReputation(WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationCompletionReviewRequest request,
        CharacterCreationOriginalCharacterSource originalSource,
        CharacterCreationCompleteAssessment authority, CharacterCreationSelectedEffectsConsumer effects,
        out CharacterCreationReputationEffectsExecution? execution, out string error,
        CancellationToken cancellationToken, CharacterCreationCampaignExecutionContext? campaign = null)
        => CharacterCreationReputationEffectsExecution.TryEvaluate(original, bootstrap, request,
            capture, queries, originalSource, authority, effects, out execution, out error, cancellationToken, campaign);

    internal CharacterCreationCompleteRuleSourceExecution? Resolve(
        WorkspaceStoredDocument original, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken)
    {
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
            if (CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                && !resolution.EnableCompleteSkillCapSourceExecution()) return null;
            using var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForProposal(
                new WorkspaceContinuationReadView(original), scope ?? resolution.Resolver, queries,
                new(original.Id) { IncludeKarmaPurchasePolicy = true }, request, cancellationToken, capture);
            if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } inputs
                || !CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out var source, cancellationToken)) return null;
            if (request.SourcebookSelection is not null && !resolution.EnableCandidateSourcebookSelection()) return null;
            return new(inputs, request, resolution, source!);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OverflowException)
        { return null; }
    }

    internal CharacterCreationCompleteRuleSourceExecution? Resolve(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (inputs.OriginalCharacter is not { } captured
            || !CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out _, cancellationToken)) return null;
        var document = new WorkspaceDocument(captured.RawCharacterXml, RulesetDefaults.Sr5);
        document = document with { State = document.State with { AuxiliaryState = captured.AuxiliaryState } };
        // Exact original observation, as in OriginalCharacterSource, never a scratch runner.
        var original = new WorkspaceStoredDocument(captured.WorkspaceId, document,
            captured.ContentRevision, captured.SavedRevision, DateTimeOffset.UnixEpoch);
        var result = Resolve(original, request, cancellationToken);
        return result is not null && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs, result.Inputs)
            ? result : null;
    }

    public override string ToString() => nameof(CharacterCreationRuleSourceExecutionContext);
}
