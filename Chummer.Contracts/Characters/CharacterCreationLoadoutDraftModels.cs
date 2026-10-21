using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCreationLoadoutInstance(string OptionId, IReadOnlyList<Guid> InstanceIds);

/// <summary>Confirmed, source-priced purchases awaiting the single finalization boundary.</summary>
public sealed record CharacterCreationLoadoutDraft(
    string Schema, CharacterWorkspaceId WorkspaceId, long DraftRevision, long BaseContentRevision,
    string RawCharacterXmlDigest, long ResourcesDraftRevision, string ResourcesDraftDigest,
    CharacterCreationSelectedLoadoutAuthority Authority, CharacterCreationLoadoutSelections Selections,
    IReadOnlyList<CharacterCreationLoadoutLine> Lines, IReadOnlyList<CharacterCreationLoadoutInstance> Instances,
    decimal PurchaseCost, string DraftDigest)
{
    public const string SchemaV1 = "chummer.creation-loadout-draft.v1";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompositePurchaseBinding? CompositePurchase { get; init; }
}

public sealed record CharacterCreationLoadoutReceipt(
    string Schema, CharacterWorkspaceId WorkspaceId, long PreviousContentRevision, long ContentRevision,
    long PreviousSavedRevision, long SavedRevision, string PreviousAuxiliaryStateDigest,
    string IdempotencyKeyDigest, string CommandDigest, string PreviewDigest, string PreviousReceiptDigest,
    CharacterCreationLoadoutDraft Draft, string ReceiptDigest)
{
    public const string SchemaV1 = "chummer.creation-loadout-receipt.v1";
}

public sealed record CharacterCreationLoadoutState(
    CharacterCreationLoadoutCompletionInputs Inputs, CharacterCreationResourcesCompletionInputs Resources,
    CharacterCreationResourcesBudget Budget)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutSelections? CompositeSelections { get; init; }
}

public sealed record CharacterCreationLoadoutPreviewRequest(
    CharacterCreationLoadoutBinding Binding, string SnapshotDigest, CharacterCreationLoadoutSelections Selections);

public sealed record CharacterCreationLoadoutPreview(
    CharacterCreationLoadoutPreviewRequest Request, CharacterCreationLoadoutCompletionProjection Projection,
    decimal TotalStartingNuyen, decimal TotalPurchaseCost, decimal RemainingNuyen, string PreviewDigest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompositePurchaseBinding? CompositePurchase { get; init; }
}

public sealed record CharacterCreationLoadoutConfirmRequest(
    CharacterCreationLoadoutPreviewRequest Request, string PreviewDigest, string IdempotencyKey, bool ExplicitlyConfirmed);

public static class CharacterCreationLoadoutDraftRules
{
    public static string RootReceiptDigest { get; } = CharacterCreationLoadoutRules.Compute("creation-loadout-receipt-root.v1");
    public static string DraftDigest(CharacterCreationLoadoutDraft value)
        => CharacterCreationLoadoutRules.Compute(value with { DraftDigest = string.Empty });
    public static string ReceiptDigest(CharacterCreationLoadoutReceipt value)
        => CharacterCreationLoadoutRules.Compute(value with { ReceiptDigest = string.Empty });
    public static string PreviewDigest(CharacterCreationLoadoutPreview value)
        => CharacterCreationLoadoutRules.Compute(value with { PreviewDigest = string.Empty });
    public static string KeyDigest(string key) => CharacterCreationLoadoutRules.Compute("creation-loadout-key.v1\0" + key);
}
