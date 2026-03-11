using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

public static class SessionMergeFamilies
{
    public const string Tracker = "tracker";
    public const string Resource = "resource";
    public const string Effect = "effect";
    public const string QuickAction = "quick-action";
    public const string Selection = "selection";
    public const string Notes = "notes";
}

public static class SessionMergePolicyModes
{
    public const string Additive = "additive";
    public const string SetLike = "set-like";
    public const string LastWriterWins = "last-writer-wins";
    public const string AppendOnly = "append-only";
    public const string ConflictMarker = "conflict-marker";
}

public static class SessionRebindOutcomes
{
    public const string Replayed = "replayed";
    public const string ReplayedOnNewBaseVersion = "replayed-on-new-base-version";
    public const string ReboundToNewRuntime = "rebound-to-new-runtime";
    public const string ManualResolutionRequired = "manual-resolution-required";
    public const string Rejected = "rejected";
}

public sealed record SessionMergePolicy(
    string Family,
    string Mode,
    IReadOnlyList<string> EventTypes,
    bool SupportsOfflineReplay = true);

public sealed record SessionRebindDiagnostic(
    string Family,
    string Outcome,
    string Message,
    string? PriorRuntimeFingerprint = null,
    string? NewRuntimeFingerprint = null);

public sealed record SessionRebindReceipt(
    CharacterVersionReference PriorCharacterVersion,
    CharacterVersionReference AppliedCharacterVersion,
    string Outcome,
    IReadOnlyList<SessionRebindDiagnostic> Diagnostics,
    bool RuntimeFingerprintChanged = false,
    bool BaseCharacterChanged = false);
