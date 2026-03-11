using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Api;

public sealed record DataExportBundle(
    CharacterFileSummary Summary,
    CharacterProfileSection? Profile,
    CharacterProgressSection? Progress,
    CharacterAttributesSection? Attributes,
    CharacterSkillsSection? Skills,
    CharacterInventorySection? Inventory,
    CharacterQualitiesSection? Qualities,
    CharacterContactsSection? Contacts);
