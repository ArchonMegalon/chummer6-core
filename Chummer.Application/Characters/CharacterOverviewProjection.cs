using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// The complete character projection required by the overview shell.
/// Ruleset implementations may materialize this packet from one immutable
/// document parse while preserving the exact section contracts.
/// </summary>
public sealed record CharacterOverviewProjection(
    CharacterProfileSection Profile,
    CharacterProgressSection Progress,
    CharacterSkillsSection Skills,
    CharacterRulesSection Rules,
    CharacterBuildSection Build,
    CharacterMovementSection Movement,
    CharacterAwakeningSection Awakening);
