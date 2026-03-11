using System.Text.Json;
using Chummer.Contracts.Session;
using Microsoft.JSInterop;

namespace Chummer.Infrastructure.Browser.Storage;

public sealed class IndexedDbBrowserStore
{
    private const string JsNamespace = "chummerSessionStorage";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IJSRuntime _jsRuntime;

    public IndexedDbBrowserStore(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<CachedClientPayload<T>?> GetAsync<T>(
        string storeName,
        string cacheArea,
        string cacheKey,
        CancellationToken ct = default)
    {
        string? rawRecord = await _jsRuntime.InvokeAsync<string?>(
            $"{JsNamespace}.getJson",
            ct,
            storeName,
            BuildCompositeKey(cacheArea, cacheKey));
        if (string.IsNullOrWhiteSpace(rawRecord))
        {
            return null;
        }

        StoredBrowserRecord? record = JsonSerializer.Deserialize<StoredBrowserRecord>(rawRecord, JsonOptions);
        if (record is null || string.IsNullOrWhiteSpace(record.Json))
        {
            return null;
        }

        T? payload = JsonSerializer.Deserialize<T>(record.Json, JsonOptions);
        if (payload is null)
        {
            return null;
        }

        return new CachedClientPayload<T>(
            cacheArea,
            cacheKey,
            payload,
            record.StoredAtUtc,
            record.StorageBackend);
    }

    public async Task<CachedClientPayload<T>> PutAsync<T>(
        string storeName,
        string cacheArea,
        string cacheKey,
        T payload,
        CancellationToken ct = default)
    {
        string rawPayload = JsonSerializer.Serialize(payload, JsonOptions);
        string rawRecord = await _jsRuntime.InvokeAsync<string>(
            $"{JsNamespace}.putJson",
            ct,
            storeName,
            BuildCompositeKey(cacheArea, cacheKey),
            rawPayload);

        StoredBrowserRecord? record = JsonSerializer.Deserialize<StoredBrowserRecord>(rawRecord, JsonOptions);
        if (record is null || string.IsNullOrWhiteSpace(record.Json))
        {
            throw new InvalidOperationException($"Browser storage write for '{storeName}/{cacheArea}/{cacheKey}' returned an unreadable receipt.");
        }

        T? typedPayload = JsonSerializer.Deserialize<T>(record.Json, JsonOptions);
        if (typedPayload is null)
        {
            throw new InvalidOperationException($"Browser storage write for '{storeName}/{cacheArea}/{cacheKey}' returned an empty payload.");
        }

        return new CachedClientPayload<T>(
            cacheArea,
            cacheKey,
            typedPayload,
            record.StoredAtUtc,
            record.StorageBackend);
    }

    public ValueTask DeleteAsync(string storeName, string cacheArea, string cacheKey, CancellationToken ct = default)
        => _jsRuntime.InvokeVoidAsync(
            $"{JsNamespace}.deleteJson",
            ct,
            storeName,
            BuildCompositeKey(cacheArea, cacheKey));

    public async Task<ClientStorageQuotaEstimate> GetQuotaEstimateAsync(CancellationToken ct = default)
    {
        string rawEstimate = await _jsRuntime.InvokeAsync<string>($"{JsNamespace}.getQuotaEstimate", ct);
        ClientStorageQuotaEstimate? estimate = JsonSerializer.Deserialize<ClientStorageQuotaEstimate>(rawEstimate, JsonOptions);
        if (estimate is null)
        {
            throw new InvalidOperationException("Browser storage quota request returned an unreadable estimate.");
        }

        return estimate;
    }

    private static string BuildCompositeKey(string cacheArea, string cacheKey)
        => $"{cacheArea}::{cacheKey}";

    private sealed record StoredBrowserRecord(
        string Key,
        string Json,
        DateTimeOffset StoredAtUtc,
        string StorageBackend = SessionClientStorageBackends.IndexedDb);
}
