using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCreationCompletionDraftApplyRequest(
    string Schema, CharacterCreationCompletionDraftReview Review,
    string IdempotencyKey, bool ExplicitlyConfirmed)
{
    public const string SchemaV1 = "chummer.character-creation-completion-draft-command.v1";
}

public sealed record CharacterCreationCompletionDraftReceiptLookup(
    string Schema, CharacterWorkspaceId WorkspaceId, string IdempotencyKey, string CommandDigest)
{
    public const string SchemaV1 = "chummer.character-creation-completion-draft-lookup.v1";
}

public sealed record CharacterCreationCompletionDraftReceipt(
    string Schema, string ReceiptId, CharacterWorkspaceId WorkspaceId,
    string IdempotencyKeyDigest, string CommandDigest,
    long PreviousContentRevision, long ContentRevision,
    long PreviousSavedRevision, long SavedRevision,
    string PreviousRawCharacterXmlDigest, string RawCharacterXmlDigest,
    string PreviousAuxiliaryStateDigest, string DraftReviewDigest,
    string BaselineDigest, string ReceiptDigest)
{
    public const string SchemaV1 = "chummer.character-creation-completion-draft-receipt.v1";
}

/// <summary>
/// One real composite operation. Paid purchases belong to Review.Request.Selections,
/// never to the legacy point-only Skills draft. Review contains the exact pending
/// budgets and effects. The source witness is historical evidence, not write authority.
/// </summary>
public sealed record CharacterCreationCompletionDraftBaseline(
    string Schema, WorkspaceDocumentAuxiliaryState OriginalState, string RawCharacterXml,
    CharacterCreationCompletionDraftReview Review,
    CharacterCreationCompletionReplayArchive SelectedReplay,
    string BaselineDigest)
{
    public const string SchemaV1 = "chummer.character-creation-completion-draft-baseline.v1";
}

public sealed record CharacterCreationCompletionDraftEntry(
    CharacterCreationCompletionDraftBaseline Baseline, CharacterCreationCompletionDraftReceipt Receipt);

public static class CharacterCreationCompletionDraftRules
{
    public static string CommandDigest(CharacterCreationCompletionDraftApplyRequest request)
        => CommandDigest(request.Schema, request.Review,
            CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey), request.ExplicitlyConfirmed);

    public static string CommandDigest(string schema, CharacterCreationCompletionDraftReview review, string keyDigest, bool explicitlyConfirmed)
        => CharacterCreationFinalizationDigest.Compute(new
        { Schema = schema, Mode = "editable-creation", Review = review, KeyDigest = keyDigest, ExplicitlyConfirmed = explicitlyConfirmed });

    public static string BaselineDigest(CharacterCreationCompletionDraftBaseline baseline)
        => CharacterCreationFinalizationDigest.Compute(baseline with { BaselineDigest = string.Empty });

    public static string ReceiptDigest(CharacterCreationCompletionDraftReceipt receipt)
        => CharacterCreationFinalizationDigest.Compute(receipt with { ReceiptDigest = string.Empty });
}
