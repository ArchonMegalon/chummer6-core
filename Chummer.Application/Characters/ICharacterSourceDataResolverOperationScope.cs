namespace Chummer.Application.Characters;

/// <summary>
/// Optional resolver-owned reuse within one synchronous operation. Implementations
/// must freshly admit the complete source catalog and live inputs on every context
/// request; exact character XML alone is not sufficient authority for reuse.
/// Resolvers without this capability retain their ordinary per-request behavior.
/// </summary>
public interface ICharacterSourceDataResolverOperationScopeFactory
{
    ICharacterSourceDataResolverOperationScope CreateOperationScope();
}

/// <summary>
/// A private operation's resolver, never an owner lease or a cache of domain results.
/// Disposing it releases reuse state. It must not be retained across operations.
/// </summary>
public interface ICharacterSourceDataResolverOperationScope : ICharacterSourceDataResolver, IDisposable
{
}
