using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

/// <summary>Exact character and losses shown before the one-way Creation transition.</summary>
public sealed record Sr6CreationFinalizationReview(
    Sr6CreationFoundationBinding Binding,
    CharacterDocument Document,
    string DocumentDigest,
    Sr6CreationDraftBalances Balances,
    IReadOnlyList<Sr6CreationFinalizationLoss> Losses,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    string ReviewDigest)
{
    public bool CanFinalize => Blockers.Count == 0;
}

public sealed record Sr6CreationFinalizationLoss(string DomainId, string PoolId, decimal Amount);

/// <summary>No client XML or rule values are admitted. The store rebuilds the exact review.</summary>
public sealed record Sr6CreationFinalizationRequest(Sr6CreationFoundationBinding Binding,
    string ReviewDigest, Guid OperationId, bool ExplicitlyConfirmed, bool AcceptUnspentLoss);

public sealed record Sr6CreationFinalizationReceipt(Sr6CreationFinalizationRequest Command,
    long ContentRevision, long SavedRevision, string DocumentDigest, string ReceiptDigest);

public sealed record Sr6CreationFinalizationCommit(Sr6CreationFinalizationReceipt Receipt, bool Replayed);

/// <summary>Immutable history, not an active draft or part of a character download.</summary>
public sealed record Sr6CreationFinalizationArchive(CharacterDocument OriginalDocument,
    WorkspaceDocumentAuxiliaryState OriginalState,
    Sr6CreationFinalizationReview Review,
    Sr6CreationFinalizationReceipt Receipt);

public static class Sr6CreationFinalizationBlockers
{
    public const string ConfirmationRequired = "sr6-finalization-confirmation-required";
    public const string LossAcknowledgementRequired = "sr6-finalization-loss-acknowledgement-required";
    public const string AlreadyFinalized = "sr6-finalization-already-completed";
    public const string OperationConflict = "sr6-finalization-operation-conflict";
    public const string NotFinalized = "sr6-finalization-not-completed";
    public const string InvalidArchive = "sr6-finalization-invalid-archive";
}
