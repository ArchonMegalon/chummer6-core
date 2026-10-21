namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Local comparison data from an admitted store read. No document, private source
/// carrier, restore capability, current campaign permission or upload admission.
/// </summary>
public sealed record WorkspaceLocalIdentityObservation(
    string OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string ContinuationDigest,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    bool? CompletionDraftIncludesLoadout,
    string? UnimportedLocalHistoryDigest)
{
    // Additive, omitted by default: historical observer JSON remains unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Chummer.Contracts.Characters.CharacterCreationReservation? CreationReservation { get; init; }

    public override string ToString() => nameof(WorkspaceLocalIdentityObservation);
}
