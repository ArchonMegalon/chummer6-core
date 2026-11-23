using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Workspaces;

// Display/recovery observations only. A caller cannot turn these into a write permit.
public sealed record WorkspaceRuleUpdatePreview(
    WorkspaceCurrentRuleEnvironmentBinding Current,
    WorkspaceCurrentRuleEnvironmentBinding Incoming,
    IReadOnlyList<string> Effects,
    string Explanation)
{
    // Full calculated observations and coverage for the original current/chosen inputs.
    // Display only: this DTO cannot authorize a restore or a new local revision.
    public CharacterRuleEffectComparisonDisplay? Calculated { get; init; }

    // Display only; the internal preparation binds the exact source and current target.
    public long? NewLocalRevision { get; init; }
    public bool RestoresSavedAlternative { get; init; }
}

public enum WorkspaceRuleUpdateState { NoMutation, PreservedBranchOnly, CommittedUpdate, Unknown }

public sealed record WorkspaceRuleUpdateProgress(Guid OperationId, string AdmissionDigest,
    string Decision, WorkspaceRuleUpdateState State);

public sealed record WorkspaceRuleUpdateAlternative(Guid OperationId,
    WorkspacePrivateRuleSourceClosureBinding Binding, DateTimeOffset PreservedAtUtc);
