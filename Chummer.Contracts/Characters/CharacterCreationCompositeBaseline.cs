namespace Chummer.Contracts.Characters;

/// <summary>
/// A read projection of one admitted composite operation, not a stage receipt or
/// a save capability. Allocations and paid purchases retain their separate units.
/// Later confirmed Attributes and point-funded Skills history supersede only
/// their own allocations. Paid purchases remain explicit in Selections.
/// </summary>
public sealed record CharacterCreationCompositeBaseline(
    string BaselineDigest,
    long ContentRevision,
    CharacterCreationCompletionSelections Selections);
