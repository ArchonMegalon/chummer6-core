using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;

namespace Chummer.Application.Tools;

public interface IShellPreferencesService
{
    ShellPreferences Load();

    void Save(ShellPreferences preferences);

    ShellPreferences Load(OwnerScope owner);

    void Save(OwnerScope owner, ShellPreferences preferences);
}
