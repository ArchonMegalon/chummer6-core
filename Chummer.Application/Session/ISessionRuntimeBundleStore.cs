using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionRuntimeBundleStore
{
    SessionRuntimeBundleRecord? Get(OwnerScope owner, string characterId);

    SessionRuntimeBundleRecord Upsert(OwnerScope owner, SessionRuntimeBundleRecord record);
}
