using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerGearEquipmentOutcome
{
    Available, Pending, Applied, Recorded, Rejected, NotFound, Conflict, Unavailable, Corrupt,
    // Original archive metadata only; never a typed workspace receipt.
    NeverApplied = 9
}

public sealed record CharacterCareerGearEquipmentRequest(CharacterWorkspaceId WorkspaceId, Guid OperationId,
    CharacterGearEquipmentIdentity Identity, string ExpectedNodeRevision, bool Equipped,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCareerLoadoutSelection? Loadout = null);

public sealed record CharacterCareerGearEquipmentBinding(string OwnerId, bool TrustedLocalOwner,
    CharacterWorkspaceId WorkspaceId, long ContentRevision, long SavedRevision,
    string SourceDigest, string AuxiliaryDigest, string EnvironmentDigest, string RuntimeDigest,
    string IncarnationId, string SlotGeneration);

public sealed record CharacterCareerGearEquipmentCatalog(CharacterCareerGearEquipmentBinding Binding, IReadOnlyList<CharacterGearEquipmentState> Nodes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCareerLoadoutCatalog? Loadouts { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LoadoutError { get; init; }
}

public sealed record CharacterCareerGearEquipmentCommand(CharacterCareerGearEquipmentRequest Request,
    CharacterCareerGearEquipmentBinding Binding, CharacterGearEquipmentState? Before, bool ExplicitlyConfirmed = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCareerLoadoutPlan? Loadout = null);

/// <summary>A stored original-operation outcome. Pending is never completion evidence.</summary>
public sealed record CharacterCareerGearEquipmentReceipt(CharacterCareerGearEquipmentCommand Command,
    string CommandDigest, CharacterCareerGearEquipmentOutcome Outcome, long RecordedRevision,
    long? CommittedRevision, string? PayloadDigestAfter, string? Rejection, string ReceiptDigest);

public sealed record CharacterCareerGearEquipmentReadResult(CharacterCareerGearEquipmentOutcome Outcome,
    CharacterCareerGearEquipmentCatalog? Catalog = null, string? Error = null);
public sealed record CharacterCareerGearEquipmentPreviewResult(CharacterCareerGearEquipmentOutcome Outcome,
    CharacterCareerGearEquipmentCommand? Command = null, string? Error = null);
public enum CharacterCareerGearEquipmentOriginalLocation { LiveOriginal, Missing, DifferentIncarnation }

/// <summary>Private execution metadata only: no character, Gear, source body or display label.</summary>
public sealed record CharacterCareerGearEquipmentOriginalOutcome(string OwnerId, bool TrustedLocalOwner,
    CharacterWorkspaceId WorkspaceId, string IncarnationId, string SlotGeneration, Guid OperationId,
    string CommandDigest, long ExpectedRevision, CharacterCareerGearEquipmentOutcome Outcome,
    long RecordedRevision, long? CommittedRevision, string? ReceiptDigest);
public sealed record CharacterCareerGearEquipmentResult(CharacterCareerGearEquipmentOutcome Outcome,
    CharacterCareerGearEquipmentReceipt? Receipt = null, long? CurrentRevision = null, string? Error = null,
    CharacterCareerGearEquipmentOriginalOutcome? Original = null,
    CharacterCareerGearEquipmentOriginalLocation OriginalLocation = CharacterCareerGearEquipmentOriginalLocation.LiveOriginal);
