using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;

namespace Chummer.Application.Tools;

public interface IShellSessionStore
{
    ShellSessionState Load();

    void Save(ShellSessionState session);

    ShellSessionState Load(OwnerScope owner);

    void Save(OwnerScope owner, ShellSessionState session);
}
