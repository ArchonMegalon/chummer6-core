using Chummer.Contracts.Journal;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Chummer.Contracts.Trackers;

namespace Chummer.Contracts.Campaign;

public static class CampaignParticipantRoles
{
    public const string GameMaster = "game-master";
    public const string Player = "player";
    public const string Observer = "observer";
}

public static class CombatRoundMarkerStates
{
    public const string Planned = "planned";
    public const string Active = "active";
    public const string Completed = "completed";
}

public sealed record CampaignDescriptor(
    OwnerScope Owner,
    string CampaignId,
    string Title,
    string Visibility,
    string? Description = null);

public sealed record PartyRosterEntry(
    string ParticipantId,
    string DisplayName,
    string Role,
    string CharacterId,
    string? OverlayId = null,
    bool IsConnected = false);

public sealed record InitiativeOrderEntry(
    string ParticipantId,
    string Label,
    int Order,
    string? PassLabel = null,
    bool IsCurrentTurn = false);

public sealed record ParticipantSessionTile(
    string ParticipantId,
    string CharacterId,
    string DisplayName,
    string Role,
    IReadOnlyList<TrackerSnapshot> Trackers,
    IReadOnlyList<string> ActiveEffects,
    SessionSyncBanner? SyncBanner = null,
    string? ExplainEntryId = null,
    bool RequiresAttention = false);

public sealed record GmTrackerBoardTile(
    string TileId,
    string ParticipantId,
    string Label,
    IReadOnlyList<TrackerSnapshot> Trackers,
    string? ExplainEntryId = null);

public sealed record CombatRoundMarker(
    string MarkerId,
    int RoundNumber,
    string Label,
    string State,
    DateTimeOffset? StartedAtUtc = null);

public sealed record GmBoardProjection(
    CampaignDescriptor Campaign,
    IReadOnlyList<PartyRosterEntry> Roster,
    IReadOnlyList<InitiativeOrderEntry> InitiativeOrder,
    IReadOnlyList<ParticipantSessionTile> Participants,
    IReadOnlyList<GmTrackerBoardTile> TrackerBoard,
    IReadOnlyList<CombatRoundMarker> RoundMarkers,
    IReadOnlyList<NoteDocument> Notes,
    string? ActiveRoundMarkerId = null);
