using System.Runtime.CompilerServices;
using Chummer.Contracts.Characters;
using P935 = Chummer.Application.Characters.CharacterCreationSourcebookSelectionProjection;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    internal bool CanExecuteSourcebooks800()
        => inputs.SourcebookSelection is not null
            && resolution.ConsumeSourcebookSelection(inputs, request) is not null;

    // Neither a public projection nor a digest can construct/register this private
    // completed execution. Only the retained consumer call below mints it.
    private sealed record CompletedSourcebooks800(P935.Result Projection,
        CharacterCreationSourcebookSelectionAuthority Catalogue, string ProfileId,
        string PolicyDigest, string RequestDigest);

    private CompletedSourcebooks800? ExecuteSourcebooks800(CharacterCreationCompleteAssessment authority)
    {
        if (request.SourcebookSelection is not { } catalogue || inputs.SourcebookSelection != catalogue
            || inputs.EffectivePolicy is not { } policy
            || authority.InputsSnapshotDigest != inputs.SnapshotDigest
            || authority.RequestDigest != requestDigest || authority.PolicyDigest != policy.PolicyDigest
            || CharacterCreationFinalizationDigest.Compute(request) != requestDigest) return null;
        try
        {
            // Rebuild and execute the retained canonical selection kernel. This is
            // independent of source reports, profile coverage and CompleteErrata.
            var projection = resolution.ConsumeSourcebookSelection(inputs, request);
            if (projection is null || projection.Schema != P935.Schema
                || projection.Binding.InputsSnapshotDigest != inputs.SnapshotDigest
                || projection.Binding.ProfileInputsDigest != policy.ProfileInputsDigest
                || projection.Binding.BooksInputsDigest != policy.BooksInputsDigest
                || projection.Binding.RuleSourceCaptureDigest != inputs.RuleSourceCaptureDigest
                || projection.Binding.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest
                || projection.Binding.SelectionsDigest != CharacterCreationFinalizationDigest.Compute(request.Selections)
                || projection.CatalogueInputsDigest != catalogue.CatalogueInputsDigest
                || projection.CatalogueXmlDigest != catalogue.CatalogueXmlDigest
                || !projection.EnabledBooks.Select(book => book.Code).SequenceEqual(policy.EnabledSourcebooks)) return null;
            // Keep an immutable private snapshot even across the resolver port.
            projection = projection with
            {
                EnabledBooks = Array.AsReadOnly(projection.EnabledBooks.ToArray()),
                Scopes = Array.AsReadOnly(projection.Scopes.Select(scope => scope with
                { Decisions = Array.AsReadOnly(scope.Decisions.ToArray()) }).ToArray())
            };
            return new(projection, catalogue, policy.SettingsProfileId, policy.PolicyDigest, requestDigest);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OverflowException)
        { return null; }
    }

    private static void CompleteSourcebooks800(CharacterCreationCompleteAssessment authority,
        IDictionary<string, string> evaluated, CompletedSourcebooks800? completed)
    {
        if (completed is null) return;
        var projection = completed.Projection;
        foreach (var row in authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Sourcebook
            && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent))
        {
            if (!projection.EnabledBooks.Any(book => row.Id == "book:" + book.Code)) continue;
            evaluated.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                row.AuthorityDigest, authority.InputsSnapshotDigest, authority.RequestDigest,
                projection.SelectionDigest, projection.ProjectionDigest
            }));
        }
    }

    // Object identity is the capability: copying/resealing invocation fields, or
    // serializing any result, cannot register a different invocation. Weak keys
    // keep this evaluation-local without a durable receipt or a replay cache.
    private sealed record ProfileSourcebooks935(CompletedSourcebooks800 Completed,
        string GraphDigest, string ConsumerDigest, string CreationDigest);
    private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, ProfileSourcebooks935>
        profileSourcebooks935 = new();

    private static string CreationBinding935(CharacterCreationRuleSourceScopeInvocation invocation)
        => CharacterCreationFinalizationDigest.Compute(new { invocation.CreationDraft718, invocation.WorkspaceId718 });

    private static void BindProfileSourcebooks935(CharacterCreationRuleSourceScopeInvocation invocation,
        CompletedSourcebooks800 completed)
    {
        if (invocation.PolicyDigest != completed.PolicyDigest || invocation.RequestDigest != completed.RequestDigest
            || invocation.CaptureDigest != completed.Projection.Binding.RuleSourceCaptureDigest
            || invocation.CreationDraft718?.SettingsProfileId != completed.ProfileId
            || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
            || !invocation.TryGetCompletedConsumer("profile", out var consumer)) return;
        profileSourcebooks935.Add(invocation, new(completed, invocation.GraphDigest, consumer, CreationBinding935(invocation)));
    }

    internal static bool TryReadProfileSourcebooks935(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string booksInputsDigest,
        out P935.Result? projection, out CharacterCreationSourcebookSelectionAuthority? catalogue)
    {
        projection = null;
        catalogue = null;
        if (!profileSourcebooks935.TryGetValue(invocation, out var retained)) return false;
        var completed = retained.Completed;
        var binding = completed.Projection.Binding;
        if (profileId != completed.ProfileId || invocation.CreationDraft718?.SettingsProfileId != profileId
            || profileInputsDigest != binding.ProfileInputsDigest || booksInputsDigest != binding.BooksInputsDigest
            || invocation.CaptureDigest != binding.RuleSourceCaptureDigest
            || invocation.PolicyDigest != completed.PolicyDigest || invocation.RequestDigest != completed.RequestDigest
            || invocation.GraphDigest != retained.GraphDigest || CreationBinding935(invocation) != retained.CreationDigest
            || !invocation.TryGetCompletedConsumer("profile", out var consumer) || consumer != retained.ConsumerDigest) return false;
        projection = completed.Projection;
        catalogue = completed.Catalogue;
        return true;
    }
}
