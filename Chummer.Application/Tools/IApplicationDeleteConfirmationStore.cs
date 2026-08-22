using Chummer.Contracts.Api;

namespace Chummer.Application.Tools;

public interface IApplicationDeleteConfirmationStore
{
    ApplicationDeleteConfirmationState Load();

    void Save(long expectedRevision, ApplicationDeleteConfirmationState state);
}
