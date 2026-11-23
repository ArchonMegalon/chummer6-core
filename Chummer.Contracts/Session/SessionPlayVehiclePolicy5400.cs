using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

/// <summary>Frozen, owner/character/package-bound rules. Never a per-action table choice.</summary>
public sealed record SessionPlayVehiclePolicy5400(int Version, string PolicyId, string OwnerId,
    CharacterVersionReference CharacterVersion, string CharacterSourceDigest, string EnvironmentDigest,
    string SettingsDigest, int JumpedHotSimBonus, SessionPlayVehicleArmorEquality ArmorEquality,
    IReadOnlyList<SessionPlayPilotPolicy5400> Pilots, string Digest);
public sealed record SessionPlayPilotPolicy5400(SessionPlayPilotSkill Skill, string SourceId,
    string Name, bool CanDefault, string SourceDigest);
