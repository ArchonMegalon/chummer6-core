namespace Chummer.Contracts.LifeModules;

/// <summary>Exact current decision to inspect; never a preview or mutation command.</summary>
public sealed record LifeModuleDecisionAvailabilityRequest(
    string WorkspaceId, long WorkspaceRevision, long SavedRevision, string TurnId, string DecisionDigest);

public static class LifeModuleOptionAvailabilityStates
{
    public const string Available = "available";
    public const string BudgetExcluded = "budget-excluded";
}

/// <summary>
/// Read-only rules explanation. A budget-excluded choice is not a legal command
/// and does not establish an in-world reason, admission decision or consequence.
/// </summary>
public sealed record LifeModuleOptionAvailability(
    string ChoiceId, string Label, string Availability, IReadOnlyList<string> SourceAnchorIds);

/// <summary>
/// Separate from the persisted decision graph so display consumers cannot add
/// blocked choices to that graph or rewrite accepted history.
/// </summary>
public sealed record LifeModuleDecisionAvailabilitySnapshot(
    LifeModuleDecisionAvailabilityRequest Binding,
    string ContentDigest, string SourceDigest, string RulesDigest, string RuntimeDigest,
    IReadOnlyList<LifeModuleOptionAvailability> Options);
