using System.Text.Json;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// An isolated candidate view for deterministic continuation evaluation, not a
/// persistence or admission authority. It has no reference to a live store and
/// deliberately advertises no atomic-write, bootstrap, or GM replay capability.
/// The caller must acquire the actual expected-owner lease independently.
/// </summary>
internal sealed class WorkspaceContinuationReadView : IWorkspaceStore
{
    private readonly OwnerScope _owner;
    private readonly WorkspaceStoredDocument _candidate;
    private readonly JsonElement _auxiliaryState;

    public WorkspaceContinuationReadView(OwnerScope owner, WorkspaceStoredDocument candidate)
    {
        if (string.IsNullOrWhiteSpace(owner.Value)
            || (owner.UsesLocalSingleUserValue && !owner.IsLocalSingleUser))
            throw new ArgumentException("A concrete owner scope is required.", nameof(owner));
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.Document);
        ArgumentNullException.ThrowIfNull(candidate.Document.State);
        ArgumentNullException.ThrowIfNull(candidate.Document.State.AuxiliaryState);
        if (string.IsNullOrWhiteSpace(candidate.Id.Value))
            throw new ArgumentException("A concrete candidate workspace ID is required.", nameof(candidate));

        _owner = owner;
        // Freeze the entire auxiliary graph, including archives and histories.
        // Copy only this graph through JSON: reconstructing the document through
        // its constructors could normalize the candidate's envelope fields.
        _auxiliaryState = JsonSerializer.SerializeToElement(candidate.Document.State.AuxiliaryState);
        if (!JsonElement.DeepEquals(_auxiliaryState, JsonSerializer.SerializeToElement(CopyAuxiliaryState())))
            throw new InvalidDataException("Candidate auxiliary state cannot be copied losslessly.");
        _candidate = candidate with
        {
            Document = candidate.Document with
            {
                State = candidate.Document.State with
                {
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            }
        };
    }

    // Legacy rule services use these signatures. They can see only the supplied
    // candidate; this is never a fallback to a trusted-local storage partition.
    public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        => id == _candidate.Id
            ? new(WorkspaceOperationOutcome.Success, CopyCandidate())
            : MissingRead();

    public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
        => owner == _owner ? Get(id) : MissingRead();

    public IReadOnlyList<WorkspaceStoreEntry> List() => [Entry()];

    public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner)
        => owner == _owner ? List() : [];

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(CharacterWorkspaceId id, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(
        OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
        CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
        OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        CharacterWorkspaceId id, long expectedContentRevision,
        string expectedAuxiliaryStateDigest, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision,
        string expectedAuxiliaryStateDigest, WorkspaceDocument document)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult SaveCheckpoint(
        OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision)
        => UnavailableMutation();

    public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
        => UnavailableMutation();

    public DelegatedGmCharacterEditStoreResult LookupDelegatedGmCharacterEdit(
        OwnerScope owner, CharacterWorkspaceId id, string idempotencyKeySha256, string commandSha256)
        => UnavailableGmOperation();

    public DelegatedGmCharacterEditStoreResult ApplyDelegatedGmCharacterEdit(
        OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision,
        WorkspaceDocument document, DelegatedGmCharacterEditLedgerEntry ledgerEntry)
        => UnavailableGmOperation();

    private WorkspaceDocumentAuxiliaryState CopyAuxiliaryState()
        => _auxiliaryState.Deserialize<WorkspaceDocumentAuxiliaryState>()
            ?? throw new InvalidDataException("Candidate auxiliary state is missing.");

    private WorkspaceStoredDocument CopyCandidate() => _candidate with
    {
        Document = _candidate.Document with
        {
            State = _candidate.Document.State with { AuxiliaryState = CopyAuxiliaryState() }
        }
    };

    private WorkspaceStoreEntry Entry() => new(
        _candidate.Id, _candidate.LastUpdatedUtc, _candidate.ContentRevision, _candidate.SavedRevision);

    private static WorkspaceStoreReadResult MissingRead() => new(
        WorkspaceOperationOutcome.Missing,
        Error: "The continuation read view does not contain that owner and workspace.");

    private static WorkspaceStoreMutationResult UnavailableMutation() => new(
        WorkspaceOperationOutcome.Unavailable,
        Error: "Continuation candidate views are read-only.");

    private static DelegatedGmCharacterEditStoreResult UnavailableGmOperation() => new(
        DelegatedGmCharacterEditStoreOutcome.Unavailable,
        Error: "Continuation candidate views do not supply delegated GM persistence or replay authority.");
}
