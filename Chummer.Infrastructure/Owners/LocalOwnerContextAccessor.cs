using Chummer.Application.Owners;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Owners;

public sealed class LocalOwnerContextAccessor : IOwnerContextAccessor
{
    public OwnerScope Current => OwnerScope.LocalSingleUser;
}
