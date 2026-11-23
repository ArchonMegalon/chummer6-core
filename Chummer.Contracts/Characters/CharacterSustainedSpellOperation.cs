using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterSustainedSpellOutcome { Available, Applied, Recorded, NotFound, Conflict, Unavailable, Unknown, Cancelled }

public sealed record CharacterSustainedSpellBinding(
    CharacterSustainedSpellPreparationBinding Preparation, string RuntimeDigest,
    string IncarnationId, string SlotGeneration, string OwnerAuthorityInstanceId, long OwnerTransitionRevision);

public sealed record CharacterSustainedSpellCatalog(CharacterSustainedSpellBinding Binding,
    CharacterSustainedSpellPreparationState Preparation);

public sealed record CharacterSustainedSpellCommand(Guid OperationId, CharacterSustainedSpellBinding Binding,
    CharacterSustainedSpellPreparationReview Review, bool ExplicitlyConfirmed = false);

// The original command must be saved by the caller before dispatch. A matching
// spell row is never evidence that this particular operation was committed.
public sealed record CharacterSustainedSpellReceipt(CharacterSustainedSpellCommand Command,
    string CommandDigest, long CommittedRevision, string PayloadDigestAfter, string ReceiptDigest);

// Private local original-operation evidence, never imported character history.
// Cancelled means this exact command was durably excluded; it does not undo an applied receipt.
public sealed record CharacterSustainedSpellCancellationReceipt(CharacterSustainedSpellCommand Command,
    string CommandDigest, long ObservedRevision, string ReceiptDigest);

public sealed record CharacterSustainedSpellLookup(string OriginalOwnerId, bool TrustedLocalOwner,
    CharacterWorkspaceId WorkspaceId, Guid OperationId, string CommandDigest,
    string IncarnationId, string SlotGeneration);

public sealed record CharacterSustainedSpellResult(CharacterSustainedSpellOutcome Outcome,
    CharacterSustainedSpellCatalog? Catalog = null, CharacterSustainedSpellCommand? Command = null,
    CharacterSustainedSpellReceipt? Receipt = null, long? CurrentRevision = null,
    CharacterSustainedSpellCancellationReceipt? Cancellation = null);
