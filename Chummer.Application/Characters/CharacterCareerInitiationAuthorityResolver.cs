using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Retains the resolver's validated synchronous read scope through atomic replacement.</summary>
public sealed class CharacterCareerInitiationAuthorityResolver(ICharacterSourceDataResolver sources)
    : ICharacterCareerInitiationAuthorityResolver
{
    public ICharacterCareerInitiationAdmission? Open(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationSelection selection, string runtimeFingerprint)
    {
        if (sources is not ICharacterSourceDataResolverOperationScopeFactory factory) return null;
        var scope = factory.CreateOperationScope();
        try
        {
            var context = scope.TryCreateContext(saved.Document.Content);
            if (context is not ICharacterCareerInitiationSourceContext initiationContext
                || !initiationContext.TryResolveCareerInitiationSources(out var source) || source is null
                || !CharacterCareerInitiationProjection.TryQuote(owner, saved, selection, runtimeFingerprint, source, context, out var quote))
            { scope.Dispose(); return null; }
            return new Admission(scope, context, owner, saved, quote!);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    // Store-only overload: the retained source observation stays inside this resolver admission.
    internal ICharacterCareerInitiationAdmission? Open(OwnerContextStamp owner,
        WorkspaceStoredDocument saved, CharacterCareerInitiationSelection selection, string runtimeFingerprint,
        ICharacterSourceDataResolver retainedSources, CharacterCurrentRuleEnvironmentContext? current)
    {
        var scope = (retainedSources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var effective = scope ?? retainedSources;
        try
        {
            var context = effective.TryCreateContext(saved.Document.Content);
            if (context is null || !TryCurrentQuote5722(owner, saved, selection, runtimeFingerprint,
                    context, current, out var quote))
            { scope?.Dispose(); return null; }
            return new CurrentAdmission5722(scope, effective, context, current, owner, saved, quote!);
        }
        catch { scope?.Dispose(); throw; }
    }

    private static bool TryCurrentQuote5722(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationSelection selection, string runtime, ICharacterSourceDataContext context,
        CharacterCurrentRuleEnvironmentContext? current, out CharacterCareerInitiationQuote? quote)
    {
        quote = null;
        if (context is not ICharacterCareerInitiationSourceContext initiation
            || !initiation.TryResolveCareerInitiationSources(out var catalog) || catalog is null) return false;
        bool resonance = CharacterCareerPowerGraph.Bool(CharacterCareerPowerGraph.Parse(saved.Document.Content), "resenabled", false);
        var proof = CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(saved, current, context, resonance);
        return CharacterCareerInitiationProjection.TryQuote(owner, saved, selection, runtime, catalog, context, out quote, proof);
    }

    private sealed class CurrentAdmission5722(ICharacterSourceDataResolverOperationScope? scope,
        ICharacterSourceDataResolver resolver, ICharacterSourceDataContext retained,
        CharacterCurrentRuleEnvironmentContext? current, OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationQuote quote) : ICharacterCareerInitiationAdmission
    {
        private bool disposed, invalidated;
        public CharacterCareerInitiationQuote Quote => quote;
        public bool IsCurrent()
        {
            if (disposed || invalidated) return false;
            try
            {
                bool valid = Matches(retained) && resolver.TryCreateContext(saved.Document.Content) is { } fresh
                    && Matches(fresh) && Matches(retained);
                invalidated = !valid;
                return valid;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            { invalidated = true; return false; }
        }
        private bool Matches(ICharacterSourceDataContext source)
            => TryCurrentQuote5722(owner, saved, quote.Selection, quote.Binding.RuntimeFingerprint, source, current, out var candidate)
                && CharacterCareerInitiationRules.Same(quote, candidate);
        public void Dispose() { if (!disposed) { disposed = true; scope?.Dispose(); } }
    }

    private sealed class Admission(ICharacterSourceDataResolverOperationScope scope, ICharacterSourceDataContext retained,
        OwnerContextStamp owner, WorkspaceStoredDocument saved, CharacterCareerInitiationQuote quote)
        : ICharacterCareerInitiationAdmission
    {
        private bool disposed, invalidated;
        public CharacterCareerInitiationQuote Quote => quote;
        public bool IsCurrent()
        {
            if (disposed || invalidated) return false;
            try
            {
                // Rebuild the quote, including Essence authority, from the retained
                // context and a newly admitted catalog. A digest alone is no lease.
                bool valid = Matches(retained)
                    && scope.TryCreateContext(saved.Document.Content) is { } current
                    && Matches(current);
                invalidated = !valid;
                return valid;
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
            { invalidated = true; return false; }
        }
        private bool Matches(ICharacterSourceDataContext context)
            => context is ICharacterCareerInitiationSourceContext initiationContext
                && initiationContext.TryResolveCareerInitiationSources(out var sources) && sources is not null
                && CharacterCareerInitiationProjection.TryQuote(owner, saved, quote.Selection,
                    quote.Binding.RuntimeFingerprint, sources, context, out var current)
                && CharacterCareerInitiationRules.Same(quote, current);
        public void Dispose() { if (!disposed) { disposed = true; scope.Dispose(); } }
    }
}
