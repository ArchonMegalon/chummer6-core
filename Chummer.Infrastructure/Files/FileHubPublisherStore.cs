using System.Text.Json;
using Chummer.Application.Hub;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

public sealed class FileHubPublisherStore : IHubPublisherStore
{
    private readonly string _stateDirectory;

    public FileHubPublisherStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<HubPublisherRecord> List(OwnerScope owner)
        => Load(owner);

    public HubPublisherRecord? Get(OwnerScope owner, string publisherId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);

        return Load(owner).FirstOrDefault(record =>
            string.Equals(record.PublisherId, publisherId.Trim().ToLowerInvariant(), StringComparison.Ordinal));
    }

    public HubPublisherRecord Upsert(OwnerScope owner, HubPublisherRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        HubPublisherRecord normalizedRecord = record with
        {
            PublisherId = NormalizeRequired(record.PublisherId),
            OwnerId = owner.NormalizedValue,
            DisplayName = record.DisplayName.Trim(),
            Slug = NormalizeRequired(record.Slug),
            VerificationState = NormalizeRequired(record.VerificationState),
            Description = NormalizeOptional(record.Description),
            WebsiteUrl = NormalizeOptional(record.WebsiteUrl)
        };

        List<HubPublisherRecord> records = Load(owner).ToList();
        int existingIndex = records.FindIndex(current =>
            string.Equals(current.PublisherId, normalizedRecord.PublisherId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            records[existingIndex] = normalizedRecord;
        }
        else
        {
            records.Add(normalizedRecord);
        }

        Save(owner, records);
        return normalizedRecord;
    }

    private IReadOnlyList<HubPublisherRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<HubPublisherRecord>? records = JsonSerializer.Deserialize<List<HubPublisherRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<HubPublisherRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "hub", "publishers.json");
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
