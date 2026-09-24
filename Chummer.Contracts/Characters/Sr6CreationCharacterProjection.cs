namespace Chummer.Contracts.Characters;

/// <summary>
/// A deterministic, read-only materialization of saved SR6 choices. Not a write
/// plan or a completed runner: the document remains uncreated and retains all
/// unspent balances. The finalization transaction must separately admit every
/// remaining requirement and bind the exact document it commits.
/// </summary>
public sealed record Sr6CreationCharacterProjection(
    Sr6CreationFoundationBinding Binding,
    CharacterDocument Document,
    string DocumentDigest,
    IReadOnlyList<string> IncompleteDomains,
    IReadOnlyList<string> SourceAnchorIds);
