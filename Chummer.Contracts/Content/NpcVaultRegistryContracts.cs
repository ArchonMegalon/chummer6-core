using Chummer.Contracts.Owners;

namespace Chummer.Contracts.Content;

public static class NpcPublicationStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

public sealed record NpcEntryManifest(
    string EntryId,
    string Version,
    string Title,
    string Description,
    string RulesetId,
    string ThreatTier,
    string? Faction = null,
    string? RuntimeFingerprint = null,
    bool SessionReady = false,
    bool GmBoardReady = false,
    string Visibility = ArtifactVisibilityModes.Public,
    string TrustTier = ArtifactTrustTiers.Curated,
    IReadOnlyList<string>? Tags = null);

public sealed record NpcPackMemberReference(
    string EntryId,
    int Quantity = 1);

public sealed record NpcPackManifest(
    string PackId,
    string Version,
    string Title,
    string Description,
    string RulesetId,
    IReadOnlyList<NpcPackMemberReference> Entries,
    bool SessionReady = false,
    bool GmBoardReady = false,
    string Visibility = ArtifactVisibilityModes.Public,
    string TrustTier = ArtifactTrustTiers.Curated,
    IReadOnlyList<string>? Tags = null);

public sealed record EncounterPackParticipantReference(
    string EntryId,
    int Quantity = 1,
    string? Role = null);

public sealed record EncounterPackManifest(
    string EncounterPackId,
    string Version,
    string Title,
    string Description,
    string RulesetId,
    IReadOnlyList<EncounterPackParticipantReference> Participants,
    bool SessionReady = false,
    bool GmBoardReady = false,
    string Visibility = ArtifactVisibilityModes.Public,
    string TrustTier = ArtifactTrustTiers.Curated,
    IReadOnlyList<string>? Tags = null);

public sealed record NpcEntryRegistryEntry(
    NpcEntryManifest Manifest,
    OwnerScope Owner,
    string PublicationStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record NpcPackRegistryEntry(
    NpcPackManifest Manifest,
    OwnerScope Owner,
    string PublicationStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record EncounterPackRegistryEntry(
    EncounterPackManifest Manifest,
    OwnerScope Owner,
    string PublicationStatus,
    DateTimeOffset UpdatedAtUtc);
