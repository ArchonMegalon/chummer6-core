using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

/// <summary>An owner-admitted review, not a persisted draft or a transferable write capability.</summary>
public sealed record CharacterCreationCompletionApplyReview(
    CharacterCreationCompletionReviewRequest Proposal,
    CharacterCreationCompletionFinalizationProjection Projection);

/// <summary>Explicit acceptance of one exact independently regenerated proposal. No replacement XML is accepted.</summary>
public sealed record CharacterCreationCompletionApplyRequest(
    CharacterCreationFinalizationBinding Binding,
    CharacterCreationCompletionReviewRequest Proposal,
    string CompletionReviewDigest,
    CharacterCreationStartingCashChoice StartingCash,
    string ProjectionDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed);

/// <summary>
/// Historical reconstruction inputs, never live source or write authority.
/// The original auxiliary history lives once in the enclosing archive.State;
/// evaluated reviews, projections and replacement XML are regenerated, not copied.
/// </summary>
public sealed record CharacterCreationCompletionAuthority(
    string Schema,
    string RawCharacterXml,
    JsonElement? BootstrapSourceSnapshot,
    CharacterCreationCompletionReviewInputs? Inputs,
    CharacterCreationCompletionReviewRequest Proposal,
    CharacterCreationStartingNuyenSource StartingCashSource,
    CharacterCreationStartingCashChoice StartingCash,
    string AuthorityDigest)
{
    public const string SchemaV2 = "chummer.character-creation-completion-authority.v2";
    public const string SchemaV3 = "chummer.character-creation-completion-authority.v3";

    // V3 emits explicit nulls for the two legacy constructor parameters above:
    // strict continuation decoding requires their presence, even when nullable.
    // Separately versioned historical evidence. It is not a pruned full Inputs
    // authority and cannot supply current source or mutation admission.
    // V2 keeps its original JSON shape; V3 requires this object and no Inputs.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompletionReplayArchive? SelectedReplay { get; init; }
}

/// <summary>
/// Bounded lossless storage of separately versioned historical replay inputs.
/// The digest identifies decoded bytes; neither digest grants write authority.
/// </summary>
public sealed record CharacterCreationCompletionReplayArchive(
    string Schema,
    string Encoding,
    int UncompressedBytes,
    string UncompressedDigest,
    string Data)
{
    public const string SchemaV1 = "chummer.creation-completion-replay-archive.v1";
}
