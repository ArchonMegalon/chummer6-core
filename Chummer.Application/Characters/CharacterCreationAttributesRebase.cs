using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Trusted host composition. Never selected by request JSON or an ambient resolver.</summary>
public sealed record CharacterCreationAttributesRebaseServices(
    ICharacterFileQueries Queries,
    ICharacterCreationAttributesRebaseCampaignFactory Campaigns);

/// <summary>Facts captured under the actual owner lease and current source observation.
/// The adapter must authenticate campaign authority independently, including absence.
/// It must return a stable binding for identical observations and reread live permission
/// on every Observe call. A retained campaign stamp is not an implementation.</summary>
public interface ICharacterCreationAttributesRebaseCampaignFactory
{
    ICharacterCreationCampaignAuthority Observe(
        CharacterCreationAttributesRebaseObservation observation, CancellationToken cancellationToken);
}

public sealed record CharacterCreationAttributesRebaseObservation(
    OwnerContextStamp Owner, CharacterWorkspaceId WorkspaceId,
    long ContentRevision, long SavedRevision, string RawCharacterXmlDigest,
    string AuxiliaryStateDigest, string InputsDigest, string SourceDigest, string PolicyDigest)
{
    public override string ToString() => nameof(CharacterCreationAttributesRebaseObservation);
}

public interface IOwnerBoundCharacterCreationAttributesRebaseService
{
    CharacterCreationFoundationResult<CharacterCreationAttributesRebasePreview> PreviewRebase(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesPreviewRequest request,
        CancellationToken cancellationToken = default);

    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> ConfirmRebase(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesRebasePreview preview,
        string idempotencyKey, bool explicitlyConfirmed, CancellationToken cancellationToken = default);
}

/// <summary>In-process review handle, minted only after full source/campaign review.
/// No public constructor or writable collections. Persist ordinary Draft receipt lookup
/// data for reconciliation; this object is not a portable authority envelope.</summary>
public sealed class CharacterCreationAttributesRebasePreview
{
    private readonly CharacterCreationCompletionDraftReview review;
    internal OwnerContextStamp Owner { get; }

    internal CharacterCreationAttributesRebasePreview(OwnerContextStamp owner,
        CharacterCreationCompletionDraftReview review)
    {
        Owner = owner;
        this.review = Copy(review);
    }

    public CharacterCreationCompletionDraftReview Review => Copy(review);
    public string PreviewDigest => review.DraftReviewDigest;
    public IReadOnlyList<CharacterCreationAttributeAllocation> Allocations
        => review.Review.Request.Selections.AttributeAllocations.ToArray();

    internal CharacterCreationCompletionDraftApplyRequest Command(string key, bool confirmed)
        => new(CharacterCreationCompletionDraftApplyRequest.SchemaV1, Copy(review), key, confirmed);

    public CharacterCreationCompletionDraftReceiptLookup ReceiptLookup(string idempotencyKey)
        => CharacterCreationCompletionDraftTransaction.LookupRequest(Command(idempotencyKey, true));

    private static CharacterCreationCompletionDraftReview Copy(CharacterCreationCompletionDraftReview value)
        => JsonSerializer.Deserialize<CharacterCreationCompletionDraftReview>(JsonSerializer.Serialize(value))!;

    public override string ToString() => nameof(CharacterCreationAttributesRebasePreview);
}
