namespace Chummer.Contracts.Characters;

/// <summary>Detached decision prompt, never a completed baseline or Apply authority.
/// A response must match a fresh calculation of every field under a new admission.</summary>
public sealed record CharacterCreationCompletionCashRequirement(
    string InputsSnapshotDigest, string RequestDigest, string PlanDigest,
    string OriginalDigest, Guid? StartingLifestyleId, CharacterCreationStartingNuyenSource Source);
