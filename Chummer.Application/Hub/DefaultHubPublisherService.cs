using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public sealed class DefaultHubPublisherService : IHubPublisherService
{
    private readonly IHubPublisherStore _publisherStore;

    public DefaultHubPublisherService(IHubPublisherStore publisherStore)
    {
        _publisherStore = publisherStore;
    }

    public HubPublicationResult<HubPublisherCatalog> ListPublishers(OwnerScope owner)
    {
        HubPublisherProfile[] items = _publisherStore.List(owner)
            .OrderBy(record => record.DisplayName, StringComparer.Ordinal)
            .Select(ToProfile)
            .ToArray();
        return HubPublicationResult<HubPublisherCatalog>.Implemented(new HubPublisherCatalog(items));
    }

    public HubPublicationResult<HubPublisherProfile?> GetPublisher(OwnerScope owner, string publisherId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);

        HubPublisherRecord? record = _publisherStore.Get(owner, publisherId);
        return HubPublicationResult<HubPublisherProfile?>.Implemented(record is null ? null : ToProfile(record));
    }

    public HubPublicationResult<HubPublisherProfile> UpsertPublisher(OwnerScope owner, string publisherId, HubUpdatePublisherRequest? request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        HubPublisherRecord? existing = _publisherStore.Get(owner, publisherId);
        HubPublisherRecord persisted = _publisherStore.Upsert(
            owner,
            new HubPublisherRecord(
                PublisherId: NormalizeRequired(publisherId),
                OwnerId: owner.NormalizedValue,
                DisplayName: NormalizeDisplayName(request.DisplayName),
                Slug: NormalizeRequired(request.Slug),
                VerificationState: existing?.VerificationState ?? HubPublisherVerificationStates.Unverified,
                CreatedAtUtc: existing?.CreatedAtUtc ?? now,
                UpdatedAtUtc: now,
                Description: NormalizeOptional(request.Description),
                WebsiteUrl: NormalizeOptional(request.WebsiteUrl)));
        return HubPublicationResult<HubPublisherProfile>.Implemented(ToProfile(persisted));
    }

    private static HubPublisherProfile ToProfile(HubPublisherRecord record)
        => new(
            PublisherId: record.PublisherId,
            OwnerId: record.OwnerId,
            DisplayName: record.DisplayName,
            Slug: record.Slug,
            VerificationState: record.VerificationState,
            CreatedAtUtc: record.CreatedAtUtc,
            UpdatedAtUtc: record.UpdatedAtUtc,
            Description: record.Description,
            WebsiteUrl: record.WebsiteUrl);

    private static string NormalizeDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string NormalizeRequired(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
