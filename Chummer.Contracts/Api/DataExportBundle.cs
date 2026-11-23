using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Api;

public sealed record DataExportBundle(
    CharacterFileSummary Summary,
    CharacterProfileSection? Profile,
    CharacterProgressSection? Progress,
    CharacterAttributesSection? Attributes,
    CharacterSkillsSection? Skills,
    CharacterInventorySection? Inventory,
    CharacterQualitiesSection? Qualities,
    CharacterContactsSection? Contacts,
    CharacterLifestylesSection? Lifestyles = null)
{
    /// <summary>
    /// Optional canonical character payload for native-codec round trips. The
    /// projections above cannot reconstruct a character. This is not a workspace
    /// continuation: owner authority, auxiliary ledgers and external source files
    /// are not carried or authorized by this envelope.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspacePayloadEnvelope? NativeWorkspace { get; init; }
}
