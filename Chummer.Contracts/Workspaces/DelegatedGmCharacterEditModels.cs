using System.Collections.Immutable;
using Chummer.Contracts.Owners;

namespace Chummer.Contracts.Workspaces;

public static class DelegatedGmCharacterEditContract
{
    public const string Name = "chummer.delegated-gm-character-edit/v1";
    public const string GameMasterRole = "game-master";
    public const string CharacterEditScope = "character-edit";

    public const string ProfileNamePath = "/profile/name";
    public const string ProfileAliasPath = "/profile/alias";
    public const string ProfileNotesPath = "/profile/notes";
}

public enum DelegatedGmCharacterPatchOperationKind
{
    Replace = 0
}

public sealed record DelegatedGmCharacterPatchOperation(
    DelegatedGmCharacterPatchOperationKind Operation,
    string Path,
    string? Value);

public sealed record DelegatedGmCharacterEditCommand(
    string CampaignId,
    string ActorId,
    OwnerScope CharacterOwner,
    CharacterWorkspaceId CharacterId,
    long ExpectedRevision,
    string IdempotencyKey,
    string Reason,
    IReadOnlyList<DelegatedGmCharacterPatchOperation> Operations);

/// <summary>
/// A request for an authority-owner decision. Core never derives campaign roles
/// from caller-controlled labels; a Hub-owned adapter must produce the decision.
/// </summary>
public sealed record CampaignGmCharacterEditAuthorizationRequest(
    string CampaignId,
    string ActorId,
    OwnerScope CharacterOwner,
    CharacterWorkspaceId CharacterId,
    ImmutableArray<string> RequestedPatchPaths);

/// <summary>
/// Immutable, campaign-bound proof returned by the campaign authority adapter.
/// Core validates every binding again before touching owner-scoped state.
/// </summary>
public sealed record CampaignGmCharacterEditAuthorization(
    bool Authorized,
    string CampaignId,
    string ActorId,
    string Role,
    string Scope,
    OwnerScope CharacterOwner,
    CharacterWorkspaceId CharacterId,
    string DelegationId,
    string GrantedByCampaignOwnerId,
    string AuthorityReceiptId,
    long AuthorityRevision,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    ImmutableArray<string> AllowedPatchPaths,
    string? DenialReason = null);

public sealed record DelegatedGmCharacterEditAuditOperation(
    DelegatedGmCharacterPatchOperationKind Operation,
    string Path,
    string ValueSha256,
    int ValueLength);

/// <summary>
/// Append-only receipt persisted atomically with the character revision. Patch
/// values are represented by digests so the audit lane does not duplicate
/// private character text.
/// </summary>
public sealed record DelegatedGmCharacterEditAuditReceipt(
    string Contract,
    string ReceiptId,
    string CampaignId,
    string DelegationId,
    string GrantedByCampaignOwnerId,
    string AuthorityReceiptId,
    long AuthorityRevision,
    string ActorId,
    string ActorRole,
    string CharacterOwnerId,
    CharacterWorkspaceId CharacterId,
    string Reason,
    string IdempotencyKeySha256,
    string CommandSha256,
    long PreviousRevision,
    long NewRevision,
    DateTimeOffset AppliedAtUtc,
    ImmutableArray<DelegatedGmCharacterEditAuditOperation> Operations);

public enum DelegatedGmCharacterEditOutcome
{
    Applied = 0,
    Replayed = 1,
    Denied = 2,
    Forbidden = 3,
    Invalid = 4,
    Missing = 5,
    Conflict = 6,
    Corrupt = 7,
    Unavailable = 8
}

public sealed record DelegatedGmCharacterEditResult(
    DelegatedGmCharacterEditOutcome Outcome,
    DelegatedGmCharacterEditAuditReceipt? Receipt = null,
    string? ErrorCode = null,
    string? Error = null)
{
    public bool Success => Outcome is DelegatedGmCharacterEditOutcome.Applied
        or DelegatedGmCharacterEditOutcome.Replayed;
}
