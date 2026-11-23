using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Only valid on the synchronous reader callback stack. No UI/custody use.</summary>
public sealed class CharacterCreationAdmittedCompletionContext : IDisposable
{
    private readonly ICharacterSourceDataContext source;
    private readonly CancellationToken cancellation;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private bool disposed;
    private readonly List<(Guid? SourceId, string Digest)> cashSources = [];
    internal (Guid? SourceId, string Digest)[] CaptureCashComparisons() => cashSources.ToArray();
    public CharacterCreationCompletionReviewInputs Inputs { get; }
    public WorkspaceStoredDocument Original { get; }
    public CharacterCreationCompletionBootstrapSnapshot Bootstrap { get; }
    public CharacterCreationRuleSourceExecutionContext RuleSources { get; }

    internal CharacterCreationAdmittedCompletionContext(CharacterCreationCompletionReviewInputs inputs,
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationRuleSourceExecutionContext ruleSources, ICharacterSourceDataContext source,
        CancellationToken cancellation)
    {
        Inputs = inputs; Original = original; Bootstrap = bootstrap; RuleSources = ruleSources;
        this.source = source; this.cancellation = cancellation;
    }

    public CharacterCreationFoundationResult<CharacterCreationCompletionCashRequirement> PrepareStartingCash(
        CharacterCreationCompletionBaselineRequest request, CharacterCreationCampaignExecutionContext? campaign = null)
    {
        RequireActive();
        return CharacterCreationCompletionPlanner.PrepareCompleteStartingCash(Inputs, request, Original,
            ResolveCash, RuleSources, cancellation, campaign);
    }

    private CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> ResolveCash(
        CharacterCreationCompletionPlan plan)
    {
        RequireActive();
        if (plan.Review.InputsSnapshotDigest != Inputs.SnapshotDigest || plan.Review.Blockers.Count != 0
            || plan.Review.Lifestyles is not { } lifestyles) return Unavailable();
        Guid? sourceId = null;
        if (lifestyles.StartingLifestyleId is Guid selected)
        {
            var rows = lifestyles.Lifestyles.Where(row => row.Configuration.LifestyleId == selected).Take(2).ToArray();
            if (rows.Length != 1) return Unavailable();
            var options = lifestyles.Authority.LifestyleOptions.Where(row =>
                row.OptionId == rows[0].Configuration.BaseLifestyleOptionId).Take(2).ToArray();
            if (options.Length != 1) return Unavailable();
            sourceId = options[0].SourceId;
        }
        else if (lifestyles.Lifestyles.Count != 0) return Unavailable();
        CharacterCreationStartingNuyenSource? terms;
        bool resolved = sourceId is Guid id ? source.TryResolveCreationPriorityStartingNuyen(id, out terms)
            : source.TryResolveCreationDefaultStartingNuyen(out terms);
        if (!resolved || terms is null || !CharacterCreationFinalizationStartingCashRules.SourcesMatch(terms, lifestyles.Authority))
            return Unavailable();
        RequireActive();
        cashSources.Add((sourceId, terms.AuthorityDigest));
        return new(CharacterCreationFoundationOutcomes.Success, terms, []);
        static CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> Unavailable()
            => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationFinalizationBlockers.StartingCashUnavailable]);
    }
    private void RequireActive()
    {
        cancellation.ThrowIfCancellationRequested();
        if (disposed || thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Completion admission has ended.");
    }
    public void Dispose() => disposed = true;
    public override string ToString() => nameof(CharacterCreationAdmittedCompletionContext);
}
