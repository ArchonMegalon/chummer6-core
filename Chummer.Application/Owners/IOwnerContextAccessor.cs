using Chummer.Contracts.Owners;

namespace Chummer.Application.Owners;

public interface IOwnerContextAccessor
{
    OwnerScope Current { get; }
}
