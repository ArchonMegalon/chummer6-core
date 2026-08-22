using Chummer.Contracts.Api;

namespace Chummer.Application.Tools;

/// <summary>
/// Exact Chummer5 GlobalSettings.ConfirmDelete semantics without registry or character XML.
/// </summary>
public static class ApplicationDeleteConfirmationRules
{
    public const string LegacyIdentity = "confirmdelete";

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
        if (mutation.Identity != ApplicationSettingIdentity.ConfirmDelete)
            throw new ArgumentOutOfRangeException(nameof(mutation), "The confirmdelete setting identity is required.");
        if (mutation.Value == current.ConfirmDelete)
            return current;

        return new ApplicationDeleteConfirmationState(current.Revision + 1, mutation.Value);
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
}
