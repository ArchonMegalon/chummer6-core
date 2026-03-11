using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubPublisherService
{
    HubPublicationResult<HubPublisherCatalog> ListPublishers(OwnerScope owner);

    HubPublicationResult<HubPublisherProfile?> GetPublisher(OwnerScope owner, string publisherId);

    HubPublicationResult<HubPublisherProfile> UpsertPublisher(OwnerScope owner, string publisherId, HubUpdatePublisherRequest? request);
}
