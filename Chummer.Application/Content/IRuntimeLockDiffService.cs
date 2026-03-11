using Chummer.Contracts.Content;

namespace Chummer.Application.Content;

public interface IRuntimeLockDiffService
{
    RuntimeLockDiffProjection Diff(ResolvedRuntimeLock before, ResolvedRuntimeLock after);
}
