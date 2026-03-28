using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultNpcVaultRegistryService : INpcVaultRegistryService
{
    private static readonly NpcEntryRegistryEntry[] BuiltInEntries =
    [
        new(
            Manifest: new NpcEntryManifest(
                EntryId: "red-samurai",
                Version: "1.0.0",
                Title: "Red Samurai",
                Description: "Renraku elite trooper packet for checkpoint and breach-response prep.",
                RulesetId: RulesetDefaults.Sr5,
                ThreatTier: "high",
                Faction: "Renraku",
                RuntimeFingerprint: "sha256:core",
                SessionReady: true,
                GmBoardReady: true,
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated,
                Tags: ["elite", "corporate", "checkpoint"]),
            Owner: new OwnerScope("system"),
            PublicationStatus: NpcPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:00:00+00:00")),
        new(
            Manifest: new NpcEntryManifest(
                EntryId: "renraku-spider",
                Version: "1.0.0",
                Title: "Renraku Spider",
                Description: "Matrix overwatch specialist that keeps alarms, locks, and support fire grounded.",
                RulesetId: RulesetDefaults.Sr5,
                ThreatTier: "medium",
                Faction: "Renraku",
                RuntimeFingerprint: "sha256:core",
                SessionReady: true,
                GmBoardReady: true,
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated,
                Tags: ["corporate", "matrix", "support"]),
            Owner: new OwnerScope("system"),
            PublicationStatus: NpcPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:05:00+00:00")),
        new(
            Manifest: new NpcEntryManifest(
                EntryId: "neon-razor-biker",
                Version: "1.0.0",
                Title: "Neon Razor Biker",
                Description: "Ancients outrider packet tuned for fast pressure, chase beats, and smash-and-grab scenes.",
                RulesetId: RulesetDefaults.Sr6,
                ThreatTier: "medium",
                Faction: "Ancients",
                RuntimeFingerprint: "sr6.preview.v1",
                SessionReady: true,
                GmBoardReady: true,
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated,
                Tags: ["gang", "vehicle", "chase"]),
            Owner: new OwnerScope("system"),
            PublicationStatus: NpcPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:10:00+00:00")),
        new(
            Manifest: new NpcEntryManifest(
                EntryId: "hex-lantern-mage",
                Version: "1.0.0",
                Title: "Hex Lantern Mage",
                Description: "Awakened overwatch packet for ritual pressure, astral scouting, and anomaly scenes.",
                RulesetId: RulesetDefaults.Sr6,
                ThreatTier: "high",
                Faction: "Street Coven",
                RuntimeFingerprint: "sr6.preview.v1",
                SessionReady: true,
                GmBoardReady: true,
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated,
                Tags: ["magic", "overwatch", "ritual"]),
            Owner: new OwnerScope("system"),
            PublicationStatus: NpcPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:15:00+00:00"))
    ];

    private static readonly NpcPackRegistryEntry[] BuiltInPacks =
    [
        new(
            Manifest: new NpcPackManifest(
                PackId: "renraku-security",
                Version: "1.0.0",
                Title: "Renraku Security",
                Description: "GM-ready corporate security roster with direct-fire and matrix support lanes.",
                RulesetId: RulesetDefaults.Sr5,
                Entries:
                [
                    new NpcPackMemberReference("red-samurai", 2),
                    new NpcPackMemberReference("renraku-spider", 1)
                ],
                SessionReady: true,
                GmBoardReady: true,
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated,
                Tags: ["security", "checkpoint", "matrix"]),
            Owner: new OwnerScope("system"),
            PublicationStatus: NpcPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:20:00+00:00")),
        new(
            Manifest: new NpcPackManifest(
                PackId: "ancients-hit-squad",
                Version: "1.0.0",
                Title: "Ancients Hit Squad",
                Description: "SR6 gang roster with fast vanguard pressure and awakened overwatch.",
                RulesetId: RulesetDefaults.Sr6,
                Entries:
                [
                    new NpcPackMemberReference("neon-razor-biker", 3),
                    new NpcPackMemberReference("hex-lantern-mage", 1)
                ],
                SessionReady: true,
                GmBoardReady: true,
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated,
                Tags: ["gang", "vehicle", "smash-grab"]),
            Owner: new OwnerScope("system"),
            PublicationStatus: NpcPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:25:00+00:00"))
    ];

    private static readonly EncounterPackRegistryEntry[] BuiltInEncounterPacks =
    [
        new(
            Manifest: new EncounterPackManifest(
                EncounterPackId: "renraku-checkpoint",
                Version: "1.0.0",
                Title: "Renraku Checkpoint",
                Description: "Checkpoint opposition packet with lead pressure and matrix overwatch already wired.",
                RulesetId: RulesetDefaults.Sr5,
                Participants:
                [
                    new EncounterPackParticipantReference("red-samurai", 2, "lead"),
                    new EncounterPackParticipantReference("renraku-spider", 1, "matrix-support")
                ],
                SessionReady: true,
                GmBoardReady: true,
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated,
                Tags: ["checkpoint", "corporate", "breach-response"]),
            Owner: new OwnerScope("system"),
            PublicationStatus: NpcPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:30:00+00:00")),
        new(
            Manifest: new EncounterPackManifest(
                EncounterPackId: "ancients-smash-and-grab",
                Version: "1.0.0",
                Title: "Ancients Smash and Grab",
                Description: "Fast chase-and-pressure encounter packet for SR6 travel and grab scenes.",
                RulesetId: RulesetDefaults.Sr6,
                Participants:
                [
                    new EncounterPackParticipantReference("neon-razor-biker", 3, "vanguard"),
                    new EncounterPackParticipantReference("hex-lantern-mage", 1, "overwatch")
                ],
                SessionReady: true,
                GmBoardReady: true,
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated,
                Tags: ["gang", "chase", "smash-grab"]),
            Owner: new OwnerScope("system"),
            PublicationStatus: NpcPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:35:00+00:00"))
    ];

    public IReadOnlyList<NpcEntryRegistryEntry> ListEntries(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        IEnumerable<NpcEntryRegistryEntry> entries = BuiltInEntries;
        if (normalizedRulesetId is not null)
        {
            entries = entries.Where(entry => string.Equals(entry.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
        }

        return entries
            .OrderBy(static entry => entry.Manifest.RulesetId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Manifest.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public NpcEntryRegistryEntry? GetEntry(OwnerScope owner, string entryId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        return ListEntries(owner, rulesetId)
            .FirstOrDefault(entry => string.Equals(entry.Manifest.EntryId, entryId, StringComparison.Ordinal));
    }

    public IReadOnlyList<NpcPackRegistryEntry> ListPacks(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        IEnumerable<NpcPackRegistryEntry> entries = BuiltInPacks;
        if (normalizedRulesetId is not null)
        {
            entries = entries.Where(entry => string.Equals(entry.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
        }

        return entries
            .OrderBy(static entry => entry.Manifest.RulesetId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Manifest.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public NpcPackRegistryEntry? GetPack(OwnerScope owner, string packId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);

        return ListPacks(owner, rulesetId)
            .FirstOrDefault(entry => string.Equals(entry.Manifest.PackId, packId, StringComparison.Ordinal));
    }

    public IReadOnlyList<EncounterPackRegistryEntry> ListEncounterPacks(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        IEnumerable<EncounterPackRegistryEntry> entries = BuiltInEncounterPacks;
        if (normalizedRulesetId is not null)
        {
            entries = entries.Where(entry => string.Equals(entry.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
        }

        return entries
            .OrderBy(static entry => entry.Manifest.RulesetId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Manifest.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public EncounterPackRegistryEntry? GetEncounterPack(OwnerScope owner, string encounterPackId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encounterPackId);

        return ListEncounterPacks(owner, rulesetId)
            .FirstOrDefault(entry => string.Equals(entry.Manifest.EncounterPackId, encounterPackId, StringComparison.Ordinal));
    }
}
