namespace Chummer.Contracts.Api;

/// <summary>
/// Stable Chummer5 GlobalSettings identity. The serialized value deliberately matches the
/// legacy registry key without exposing registry persistence to phone clients.
/// </summary>
public enum ApplicationSettingIdentity
{
    ConfirmDelete,
    ConfirmKarmaExpense
}

public sealed record ApplicationDeleteConfirmationState(
    long Revision,
    bool ConfirmDelete,
    bool ConfirmKarmaExpense = true)
{
    public static ApplicationDeleteConfirmationState Default { get; } = new(
        Revision: 0,
        ConfirmDelete: true,
        ConfirmKarmaExpense: true);
}

public sealed record ApplicationDeleteConfirmationMutation(
    ApplicationSettingIdentity Identity,
    bool Value,
    long ExpectedRevision);

public sealed record ApplicationConfirmationSettingsMutation(
    bool ConfirmDelete,
    bool ConfirmKarmaExpense,
    long ExpectedRevision);
