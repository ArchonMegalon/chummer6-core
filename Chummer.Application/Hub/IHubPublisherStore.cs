using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubPublisherStore
{
    IReadOnlyList<HubPublisherRecord> List(OwnerScope owner);

    HubPublisherRecord? Get(OwnerScope owner, string publisherId);

    HubPublisherRecord Upsert(OwnerScope owner, HubPublisherRecord record);
}
