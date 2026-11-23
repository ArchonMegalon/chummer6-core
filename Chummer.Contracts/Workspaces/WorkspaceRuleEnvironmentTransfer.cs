using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Chummer.Contracts.Workspaces;

/// <summary>Private transport identity only. Execution requires the receiver's Core admission.</summary>
public sealed record WorkspaceRuleEnvironmentTransfer(
    string Schema, WorkspacePrivateRuleSourceClosureBinding Binding, int ByteLength)
{
    public const string SchemaV1 = "chummer.account-rule-environment.v1";
    // Fits one immutable inline Teable row, including the exact manifest.
    public const int ChunkBytes = 16 * 1024;
    private static readonly SearchValues<char> s_archiveHexCharacters = SearchValues.Create("0123456789abcdef");
    [JsonIgnore] public string Identity => ArchiveDigest(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this));
    [JsonIgnore] public int ChunkCount => (ByteLength + ChunkBytes - 1) / ChunkBytes;
    public override string ToString() => nameof(WorkspaceRuleEnvironmentTransfer);

    public bool IsValid(string owner, string workspace, string continuationDigest)
        => Schema == SchemaV1 && Binding is not null
            && ByteLength is > 0 and <= WorkspacePrivateRuleSourceClosureBinding.MaximumBytes
            && Binding.Schema == WorkspacePrivateRuleSourceClosureBinding.SchemaV2
            && owner.Length is > 0 and <= 256 && workspace.Length is > 0 and <= 128
            && Binding.OwnerId == owner && Binding.WorkspaceId.Value == workspace
            && continuationDigest is { Length: 64 } && continuationDigest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            && Binding.ContinuationDigest == continuationDigest
            && Binding.ContentRevision > 0 && Binding.SavedRevision >= 0
            && Binding.SavedRevision <= Binding.ContentRevision
            && Binding.ArchiveCount is > 0 and <= WorkspacePrivateRuleSourceClosureBinding.MaximumArchives
            && Binding.ArchiveBytes > 0 && Binding.ArchiveBytes < ByteLength
            && Binding.CampaignCount is >= 0 and <= WorkspacePrivateRuleSourceClosureBinding.MaximumCampaigns
            && IsArchiveDigest(Binding.ClosureDigest) && IsArchiveDigest(Binding.CurrentEnvironmentDigest);

    public int ChunkLength(int index)
    {
        if (index < 0 || index >= ChunkCount) throw new ArgumentOutOfRangeException(nameof(index));
        return Math.Min(ChunkBytes, ByteLength - index * ChunkBytes);
    }

    public bool MatchesBytes(ReadOnlySpan<byte> bytes)
        => bytes.Length == ByteLength && ArchiveDigest(bytes) == Binding.ClosureDigest;
    public static string ArchiveDigest(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static bool IsArchiveDigest(string? digest) => digest is { Length: 71 }
        && digest.StartsWith("sha256:", StringComparison.Ordinal)
        && digest.AsSpan(7).IndexOfAnyExcept(s_archiveHexCharacters) < 0;
}
