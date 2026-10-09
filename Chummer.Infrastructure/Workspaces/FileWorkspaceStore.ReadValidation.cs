using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // Ordinary reads repeatedly revisit the same finalized runner during shell
    // bootstrap and selection. Reuse ONLY deterministic historical shape checks
    // for identical bytes in this store instance. This is not source admission,
    // permission to write/restore, or a cache of caller-visible mutable objects.
    private const int MaximumReadValidations = 128;
    private readonly object _readValidationSync = new();
    private readonly HashSet<ReadValidationKey> _readValidations = [];
    private readonly Queue<ReadValidationKey> _readValidationOrder = new();

    internal int CachedReadValidationCount
    {
        get { lock (_readValidationSync) return _readValidations.Count; }
    }

    private readonly record struct ReadValidationKey(
        string OwnerId, CharacterWorkspaceId WorkspaceId, string RecordDigest);

    private static PersistedWorkspaceRecord? ReadRecordWithDigest(Stream stream, out string? digest)
    {
        using var hash = SHA256.Create();
        using var input = new CryptoStream(stream, hash, CryptoStreamMode.Read, leaveOpen: true);
        // Hash the same bytes the decoder receives, in one pass. A second read
        // or file timestamp/length would not bind validation to this snapshot.
        var record = JsonSerializer.Deserialize(input, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord);
        digest = input.HasFlushedFinalBlock ? Convert.ToHexStringLower(hash.Hash!) : null;
        return record;
    }

    private bool HasReadValidation(ReadValidationKey? key)
    {
        if (key is null) return false;
        lock (_readValidationSync) return _readValidations.Contains(key.Value);
    }

    private void RememberReadValidation(ReadValidationKey? key)
    {
        if (key is null) return;
        lock (_readValidationSync)
        {
            if (!_readValidations.Add(key.Value)) return;
            _readValidationOrder.Enqueue(key.Value);
            if (_readValidationOrder.Count > MaximumReadValidations)
                _readValidations.Remove(_readValidationOrder.Dequeue());
        }
    }
}
