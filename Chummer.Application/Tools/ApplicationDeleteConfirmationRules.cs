using Chummer.Contracts.Api;

namespace Chummer.Application.Tools;

/// <summary>
/// Exact Chummer5 confirmation Global Settings semantics without registry or character XML.
/// </summary>
public static class ApplicationDeleteConfirmationRules
{
    public const string LegacyIdentity = "confirmdelete";
    public const string LegacyKarmaExpenseIdentity = "confirmkarmaexpense";

    public static ApplicationDeleteConfirmationState Apply(
        ApplicationDeleteConfirmationState current,
        ApplicationDeleteConfirmationMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        Validate(current);
        if (mutation.ExpectedRevision != current.Revision)
        {
            throw new InvalidOperationException(
                $"Application settings changed at revision {current.Revision}; expected {mutation.ExpectedRevision}.");
        }
        if (!Enum.IsDefined(mutation.Identity))
            throw new ArgumentOutOfRangeException(nameof(mutation), "A known application setting identity is required.");

        bool unchanged = mutation.Identity switch
        {
            ApplicationSettingIdentity.ConfirmDelete => mutation.Value == current.ConfirmDelete,
            ApplicationSettingIdentity.ConfirmKarmaExpense => mutation.Value == current.ConfirmKarmaExpense,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), "A known application setting identity is required.")
        };
        if (unchanged)
            return current;

        return mutation.Identity switch
        {
            ApplicationSettingIdentity.ConfirmDelete => current with
            {
                Revision = current.Revision + 1,
                ConfirmDelete = mutation.Value
            },
            ApplicationSettingIdentity.ConfirmKarmaExpense => current with
            {
                Revision = current.Revision + 1,
                ConfirmKarmaExpense = mutation.Value
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), "A known application setting identity is required.")
        };
    }

    public static ApplicationDeleteConfirmationState ApplySnapshot(
        ApplicationDeleteConfirmationState current,
        ApplicationConfirmationSettingsMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        Validate(current);
        if (mutation.ExpectedRevision != current.Revision)
        {
            throw new InvalidOperationException(
                $"Application settings changed at revision {current.Revision}; expected {mutation.ExpectedRevision}.");
        }
        if (mutation.ConfirmDelete == current.ConfirmDelete
            && mutation.ConfirmKarmaExpense == current.ConfirmKarmaExpense)
        {
            return current;
        }

        return new ApplicationDeleteConfirmationState(
            current.Revision + 1,
            mutation.ConfirmDelete,
            mutation.ConfirmKarmaExpense);
    }

    public static ApplicationDeleteConfirmationState Validate(ApplicationDeleteConfirmationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Revision < 0)
            throw new InvalidDataException("Application settings revision cannot be negative.");
        return state;
    }

    public static bool RequiresConfirmation(ApplicationDeleteConfirmationState state)
        => Validate(state).ConfirmDelete;

    public static bool RequiresKarmaExpenseConfirmation(ApplicationDeleteConfirmationState state)
        => Validate(state).ConfirmKarmaExpense;
}
