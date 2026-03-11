#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Hub;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class HubPublisherServiceTests
{
    [TestMethod]
    public void Default_publisher_service_upserts_and_lists_owner_profiles()
    {
        InMemoryHubPublisherStore store = new();
        DefaultHubPublisherService service = new(store);
        OwnerScope owner = new("alice");

        HubPublisherProfile profile = service.UpsertPublisher(
            owner,
            "shadowops",
            new HubUpdatePublisherRequest(
                DisplayName: "ShadowOps",
                Slug: "shadowops",
                Description: "Campaign runtime publisher",
                WebsiteUrl: "https://example.invalid/shadowops")).Payload!;

        HubPublisherCatalog catalog = service.ListPublishers(owner).Payload!;

        Assert.AreEqual("shadowops", profile.PublisherId);
        Assert.AreEqual("ShadowOps", profile.DisplayName);
        Assert.AreEqual("shadowops", profile.Slug);
        Assert.AreEqual(HubPublisherVerificationStates.Unverified, profile.VerificationState);
        Assert.HasCount(1, catalog.Items);
        Assert.AreEqual("shadowops", catalog.Items[0].PublisherId);
    }

    [TestMethod]
    public void Default_publisher_service_is_owner_scoped()
    {
        InMemoryHubPublisherStore store = new();
        DefaultHubPublisherService service = new(store);

        service.UpsertPublisher(
            new OwnerScope("alice"),
            "shadowops",
            new HubUpdatePublisherRequest("ShadowOps", "shadowops"));

        HubPublisherCatalog bobCatalog = service.ListPublishers(new OwnerScope("bob")).Payload!;
        HubPublisherProfile? bobView = service.GetPublisher(new OwnerScope("bob"), "shadowops").Payload;

        Assert.IsEmpty(bobCatalog.Items);
        Assert.IsNull(bobView);
    }

    private sealed class InMemoryHubPublisherStore : IHubPublisherStore
    {
        private readonly List<HubPublisherRecord> _records = [];

        public IReadOnlyList<HubPublisherRecord> List(OwnerScope owner)
        {
            return _records
                .Where(record => string.Equals(record.OwnerId, owner.NormalizedValue, StringComparison.Ordinal))
                .ToArray();
        }

        public HubPublisherRecord? Get(OwnerScope owner, string publisherId)
        {
            return List(owner).FirstOrDefault(record => string.Equals(record.PublisherId, publisherId, StringComparison.Ordinal));
        }

        public HubPublisherRecord Upsert(OwnerScope owner, HubPublisherRecord record)
        {
            int existingIndex = _records.FindIndex(current =>
                string.Equals(current.OwnerId, owner.NormalizedValue, StringComparison.Ordinal)
                && string.Equals(current.PublisherId, record.PublisherId, StringComparison.Ordinal));
            HubPublisherRecord normalizedRecord = record with { OwnerId = owner.NormalizedValue };
            if (existingIndex >= 0)
            {
                _records[existingIndex] = normalizedRecord;
            }
            else
            {
                _records.Add(normalizedRecord);
            }

            return normalizedRecord;
        }
    }
}
