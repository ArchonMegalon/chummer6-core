using System.Text.Json.Serialization;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Store-local incarnation and execution boundary. This is not portable character
/// data, a signature, or a caller's restore permission. Only the durable store may
/// establish it; imported snapshot claims must never supply these values.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceLocalHistory(
    [property: JsonRequired] string IncarnationId,
    [property: JsonRequired] long ImportedThroughRevision,
    [property: JsonRequired] string? ImportedSnapshotDigest)
{
    /// <summary>Structural consistency, never proof that the caller owns the record.</summary>
    public bool IsValid(long currentRevision) => currentRevision > 0
        && Guid.TryParseExact(IncarnationId, "N", out Guid incarnation)
        && incarnation != Guid.Empty && incarnation.ToString("N") == IncarnationId
        && ImportedThroughRevision >= 0 && ImportedThroughRevision <= currentRevision
        && (ImportedThroughRevision == 0
            ? ImportedSnapshotDigest is null
            : DelegatedGmCharacterEditLedgerValidator.IsSha256(ImportedSnapshotDigest));

    /// <summary>Classifies a recorded revision; it does not authenticate its execution.</summary>
    public bool IsImportedRevision(long revision) => revision > 0 && revision <= ImportedThroughRevision;
}
