namespace Chummer.Contracts.Characters;

/// <summary>Read-only coverage of saved choices, not finalization permission or a legality certificate.</summary>
public sealed record Sr6CreationDraftSummary(
    Sr6CreationFoundationBinding Binding,
    IReadOnlyList<Sr6CreationDraftStep> Steps,
    Sr6CreationDraftBalances? Balances,
    IReadOnlyList<string> SourceAnchorIds)
{
    // The draft ledger does not yet materialize a complete SR6 character/effect graph.
    public bool FinalizationAvailable { get; }

    public Sr6CreationNaturalValues? NaturalValues { get; init; }
    public Sr6CreationPassiveValues? PassiveValues { get; init; }
    public IReadOnlyList<Sr6CreationEquipmentProfile> Equipment { get; init; } = [];
}

public sealed record Sr6CreationDraftStep(string Id, string Status, bool CanOpen,
    IReadOnlyList<Sr6CreationDraftRemainder> Remainders);
public sealed record Sr6CreationDraftRemainder(string Id, decimal Amount);
public sealed record Sr6CreationDraftBalances(decimal ResourcesNuyen, decimal GearSpentNuyen,
    decimal LifestyleSpentNuyen, decimal RemainingNuyen, decimal ProjectedStartingNuyen,
    decimal NuyenAboveCarryOver, int RemainingKarma, int ProjectedStartingKarma, int KarmaAboveCarryOver);

public static class Sr6CreationDraftStepStatuses
{
    public const string Missing = "missing";
    public const string Saved = "saved";
    public const string Unspent = "unspent";
    public const string Waiting = "waiting";
    public const string NoneSelected = "none-selected";
}
