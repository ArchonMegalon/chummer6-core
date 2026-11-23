using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerGearSaleOutcome
{
    Available, Pending, Applied, Recorded, Rejected, NotFound, Conflict, Unavailable, Corrupt,
    // Original archive metadata only; never a typed workspace receipt.
    NeverApplied = 9
}

// Sale remains the default so existing source callers and v1 serialized receipts retain their identity.
public enum CharacterCareerGearOperationKind { Sale = 0, Remove = 1 }

public sealed record CharacterCareerGearSaleRequest(CharacterWorkspaceId WorkspaceId, Guid OperationId,
    Guid InstanceId, string ExpectedNodeRevision, decimal ResalePercent, Guid ExpenseId, DateTimeOffset ExpenseDate)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CharacterCareerGearOperationKind OperationKind { get; init; }

    // Removal has no economic inputs or fabricated expense identity/date.
    public static CharacterCareerGearSaleRequest ForRemoval(CharacterWorkspaceId workspaceId, Guid operationId,
        Guid instanceId, string expectedNodeRevision) => new(workspaceId, operationId, instanceId,
            expectedNodeRevision, 0m, Guid.Empty, default) { OperationKind = CharacterCareerGearOperationKind.Remove };
}

public sealed record CharacterCareerGearSaleBinding(string OwnerId, bool TrustedLocalOwner,
    CharacterWorkspaceId WorkspaceId, long ContentRevision, long SavedRevision,
    string SourceDigest, string AuxiliaryDigest, string EnvironmentDigest, string RuntimeDigest,
    string IncarnationId, string SlotGeneration)
{
    public string OwnerAuthorityInstanceId { get; init; } = "";
    public long OwnerTransitionRevision { get; init; }
}

public sealed record CharacterCareerGearSaleCatalog(CharacterCareerGearSaleBinding Binding,
    IReadOnlyList<CharacterCareerGearSaleNode> Nodes);

public sealed record CharacterCareerGearSaleCommand(CharacterCareerGearSaleRequest Request,
    CharacterCareerGearSaleBinding Binding, CharacterCareerGearSaleQuote Quote, bool ExplicitlyConfirmed = false);

/// <summary>A stored original-operation outcome. Pending is never completion evidence.</summary>
public sealed record CharacterCareerGearSaleReceipt(CharacterCareerGearSaleCommand Command,
    string CommandDigest, CharacterCareerGearSaleOutcome Outcome, long RecordedRevision,
    long? CommittedRevision, string? PayloadDigestAfter, string? Rejection, string ReceiptDigest);

public sealed record CharacterCareerGearSaleReadResult(CharacterCareerGearSaleOutcome Outcome,
    CharacterCareerGearSaleCatalog? Catalog = null, string? Error = null);
public sealed record CharacterCareerGearSalePreviewResult(CharacterCareerGearSaleOutcome Outcome,
    CharacterCareerGearSaleCommand? Command = null, string? Error = null);
public enum CharacterCareerGearSaleOriginalLocation { LiveOriginal, Missing, DifferentIncarnation }

/// <summary>Private execution metadata only: no character, Gear, source body or display label.</summary>
public sealed record CharacterCareerGearSaleOriginalOutcome(string OwnerId, bool TrustedLocalOwner,
    CharacterWorkspaceId WorkspaceId, string IncarnationId, string SlotGeneration, Guid OperationId,
    string CommandDigest, long ExpectedRevision, CharacterCareerGearSaleOutcome Outcome,
    long RecordedRevision, long? CommittedRevision, string? ReceiptDigest);
public sealed record CharacterCareerGearSaleResult(CharacterCareerGearSaleOutcome Outcome,
    CharacterCareerGearSaleReceipt? Receipt = null, long? CurrentRevision = null, string? Error = null,
    CharacterCareerGearSaleOriginalOutcome? Original = null,
    CharacterCareerGearSaleOriginalLocation OriginalLocation = CharacterCareerGearSaleOriginalLocation.LiveOriginal);

// InstalledXml is evidence, never permission to dispatch arbitrary XML.
public sealed record CharacterCareerGearSaleNode(Guid? InstanceId, string Name, string NodeRevision,
    string InstalledXml, CharacterGearCostSnapshot? Cost, decimal Nuyen, IReadOnlyList<string> Blockers)
{
    // Null means a v1 sale-only witness, never removal eligibility. Existing Blockers stay sale-specific.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RemovalBlockers { get; init; }
}
public sealed record CharacterCareerGearRemovalItem(Guid InstanceId, string Name, decimal Quantity, string NodeRevision);

public sealed record CharacterCareerGearSaleQuote(CharacterCareerGearSaleNode Item, decimal TotalCost,
    decimal DeletionExtraProceeds, decimal Proceeds, decimal NuyenAfter, string ExpenseXml)
{
    // Deterministic preorder, including the target and each proven ordinary child; null for Sale.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCareerGearRemovalItem>? RemovedItems { get; init; }
}
