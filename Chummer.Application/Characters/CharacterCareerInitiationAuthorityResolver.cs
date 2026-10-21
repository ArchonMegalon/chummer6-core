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
            if (scope.TryCreateContext(saved.Document.Content) is not ICharacterCareerInitiationSourceContext context
                || !context.TryResolveCareerInitiationSources(out var source) || source is null
                || !CharacterCareerInitiationProjection.TryQuote(owner, saved, selection, runtimeFingerprint, source, out var quote))
            { scope.Dispose(); return null; }
            return new Admission(scope, saved.Document.Content, source, quote!);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private sealed class Admission(ICharacterSourceDataResolverOperationScope scope, string xml,
        CharacterCareerInitiationSources original, CharacterCareerInitiationQuote quote)
        : ICharacterCareerInitiationAdmission
    {
        private bool disposed;
        public CharacterCareerInitiationQuote Quote => quote;
        public bool IsCurrent()
        {
            if (disposed) return false;
            try
            {
                return scope.TryCreateContext(xml) is ICharacterCareerInitiationSourceContext context
                    && context.TryResolveCareerInitiationSources(out var current)
                    && CharacterCareerInitiationRules.Same(original, current);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
            { return false; }
        }
        public void Dispose() { if (!disposed) { disposed = true; scope.Dispose(); } }
    }
}
