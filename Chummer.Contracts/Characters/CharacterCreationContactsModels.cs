using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationContactsSchemas
{
    public const string StateV1 = "chummer.character_creation_contacts.state.v1";
    public const string PreviewV1 = "chummer.character_creation_contacts.preview.v1";
    public const string ReceiptV1 = "chummer.character_creation_contacts.receipt.v1";
    public const string WritePlanV1 = "chummer.character_creation_contacts.write_plan.v1";
    public const string RulesV1 = "chummer.character_creation_contacts.sr5_rules.v1";
    public const string RuntimeV1 = "chummer.character_creation_contacts.runtime.v1";
}

public static class CharacterCreationContactFieldIds
{
    public const string Name = "name";
    public const string Role = "role";
    public const string Location = "location";
    public const string Notes = "notes";
    public const string CustomName = "custom-name";
    public const string Metatype = "metatype";
    public const string Gender = "gender";
    public const string Age = "age";
    public const string ContactType = "contact-type";
    public const string PreferredPayment = "preferred-payment";
    public const string HobbiesVice = "hobbies-vice";
    public const string PersonalLife = "personal-life";
    public const string GroupName = "group-name";
    public const string Connection = "connection";
    public const string Loyalty = "loyalty";
    public const string Group = "group";
    public const string Free = "free";
    public const string Family = "family";
    public const string Blackmail = "blackmail";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Name, Role, Location, Notes, CustomName, Metatype, Gender, Age,
        ContactType, PreferredPayment, HobbiesVice, PersonalLife, GroupName,
        Connection, Loyalty, Group, Free, Family, Blackmail
    });
}

public static class CharacterCreationContactValueKinds
{
    public const string Text = "text";
    public const string Integer = "integer";
    public const string Boolean = "boolean";
}

public static class CharacterCreationContactBudgetIds
{
    public const string Contacts = CharacterCreationBudgetIds.Contacts;
    public const string FriendsInHighPlaces = "friends-in-high-places-contacts";
}

public static class CharacterCreationContactsBlockers
{
    public const string AuthorityUnavailable = "creation-contacts-authority-unavailable";
    public const string BudgetAuthorityRequired = "creation-contacts-budget-authority-required";
    public const string BudgetExceeded = "creation-contacts-budget-exceeded";
    public const string CareerModeRejected = "creation-contacts-career-mode-rejected";
    public const string CharacterDocumentInvalid = "creation-contacts-character-document-invalid";
    public const string ContactAmbiguous = "creation-contacts-contact-ambiguous";
    public const string ContactInvalid = "creation-contacts-contact-invalid";
    public const string ContactNotFound = "creation-contacts-contact-not-found";
    public const string FieldNotEditable = "creation-contacts-field-not-editable";
    public const string FriendsInHighPlacesAuthorityRequired = "creation-contacts-friends-in-high-places-authority-required";
    public const string HighPlacesBudgetExceeded = "creation-contacts-high-places-budget-exceeded";
    public const string IdempotencyConflict = "creation-contacts-idempotency-conflict";
    public const string IdempotencyKeyInvalid = "creation-contacts-idempotency-key-invalid";
    public const string MutationEmpty = "creation-contacts-mutation-empty";
    public const string MutationInvalid = "creation-contacts-mutation-invalid";
    public const string NoChange = "creation-contacts-no-change";
    public const string PersistenceAuthorityRequired = "creation-contacts-persistence-authority-required";
    public const string PreviewDigestMismatch = "creation-contacts-preview-digest-mismatch";
    public const string ExplicitConfirmationRequired = "creation-contacts-explicit-confirmation-required";
    public const string ReceiptLedgerCorrupt = "creation-contacts-receipt-ledger-corrupt";
    public const string RulesetSr5Required = "creation-contacts-ruleset-sr5-required";
    public const string StaleAuxiliaryStateDigest = "creation-contacts-stale-auxiliary-state-digest";
    public const string StaleContentDigest = "creation-contacts-stale-content-digest";
    public const string StaleRulesDigest = "creation-contacts-stale-rules-digest";
    public const string StaleRuntimeDigest = "creation-contacts-stale-runtime-digest";
    public const string StaleSourceDigest = "creation-contacts-stale-source-digest";
    public const string StaleWorkspaceRevision = "creation-contacts-stale-workspace-revision";
    public const string UnsupportedContactType = "creation-contacts-unsupported-contact-type";
    public const string WorkspaceUnavailable = "creation-contacts-workspace-unavailable";
}

public static class CharacterCreationContactOutcomes
{
    public const string Available = "available";
    public const string Applied = "applied";
    public const string Replayed = "replayed";
    public const string NotFound = "not-found";
    public const string Blocked = "blocked";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Missing = "missing";
    public const string Corrupt = "corrupt";
    public const string Unavailable = "unavailable";
}

public static class CharacterCreationContactSourceAnchors
{
    public const string Step = "CharacterCreationWizardStepIds.ContactsLifestyles";
    public const string EditSemantics = "Chummer.Contracts/Characters/CharacterContactEditSemantics.cs#CharacterContactEditSemanticsResolver";
    public const string ContactPointCost = "Chummer/Backend/Characters/Contact.cs#ContactPoints";
    public const string ContactPointBudget = "Chummer/Backend/Characters/Character.cs#ContactPointsUsed";
    public const string CreationFreeControl = "Chummer/Controls/Characters/ContactControl.cs#chkFree";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Step, EditSemantics, ContactPointCost, ContactPointBudget, CreationFreeControl
    });
}

public sealed record CharacterCreationContactBinding(
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    long ContentRevision,
    long SavedRevision,
    string ContentDigest,
    string AuxiliaryStateDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest);

public sealed record CharacterCreationContactIdentity(
    string Name,
    string Role,
    string Location,
    string Notes,
    string CustomName,
    string Metatype,
    string Gender,
    string Age,
    string ContactType,
    string PreferredPayment,
    string HobbiesVice,
    string PersonalLife,
    string GroupName);

/// <summary>
/// Strongly typed partial edit. Null means unchanged; Identity, when present,
/// replaces the complete saved identity projection and can therefore clear a value.
/// No XML path or caller-defined field name crosses this boundary.
/// </summary>
public sealed record CharacterCreationContactEdit(
    Guid ContactId,
    CharacterCreationContactIdentity? Identity = null,
    int? Connection = null,
    int? Loyalty = null,
    bool? IsGroup = null,
    bool? Free = null,
    bool? Family = null,
    bool? Blackmail = null);

public sealed record CharacterCreationContactOption(
    string OptionId,
    string Label,
    string SerializedValue,
    bool IsEnabled,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationContactFieldAuthority(
    string FieldId,
    string Label,
    string ValueKind,
    bool IsEditable,
    string SerializedValue,
    int? Minimum,
    int? Maximum,
    IReadOnlyList<CharacterCreationContactOption> LegalOptions,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationContactProjection(
    Guid ContactId,
    CharacterCreationContactIdentity Identity,
    int Connection,
    int Loyalty,
    bool IsGroup,
    bool Free,
    bool Family,
    bool Blackmail,
    int ContactPointCost,
    bool CountsAgainstContactBudget,
    bool CountsAgainstHighPlacesBudget,
    IReadOnlyList<CharacterCreationContactFieldAuthority> Fields,
    IReadOnlyList<string> SourceAnchorIds,
    string ContactDigest);

public sealed record CharacterCreationContactBudget(
    string BudgetId,
    int Total,
    int Used,
    int Remaining,
    int Overspend,
    bool IsExact,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationContactsLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationContactPreviewRequest(
    CharacterCreationContactBinding Binding,
    CharacterCreationContactEdit Edit);

public sealed record CharacterCreationContactConfirmRequest(
    CharacterCreationContactBinding Binding,
    CharacterCreationContactEdit Edit,
    string PreviewDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationContactReceiptLookupRequest(
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKey);

public sealed record CharacterCreationContactWriteOperation(
    int Order,
    string FieldId,
    string BeforeValue,
    string AfterValue,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationContactAtomicWritePlan(
    string Schema,
    string StepId,
    Guid ContactId,
    IReadOnlyList<CharacterCreationContactWriteOperation> Operations,
    string ContentDigestBefore,
    string ContentDigestAfter,
    string UntouchedSiblingDigestBefore,
    string UntouchedSiblingDigestAfter,
    string NestedStateDigestBefore,
    string NestedStateDigestAfter,
    bool PreservesUntouchedSiblingState,
    bool PreservesNestedState,
    string PlanDigest);

public sealed record CharacterCreationContactsState(
    string Schema,
    string StepId,
    CharacterCreationContactBinding Binding,
    bool CharacterCreated,
    IReadOnlyList<CharacterCreationContactProjection> Contacts,
    CharacterCreationContactBudget ContactBudget,
    CharacterCreationContactBudget HighPlacesBudget,
    IReadOnlyList<string> Blockers,
    bool CanEdit,
    string SnapshotDigest);

public sealed record CharacterCreationContactPreview(
    string Schema,
    string StepId,
    CharacterCreationContactBinding Binding,
    CharacterCreationContactProjection ContactBefore,
    CharacterCreationContactProjection ContactAfter,
    CharacterCreationContactBudget ContactBudgetBefore,
    CharacterCreationContactBudget ContactBudgetAfter,
    CharacterCreationContactBudget HighPlacesBudgetBefore,
    CharacterCreationContactBudget HighPlacesBudgetAfter,
    CharacterCreationContactAtomicWritePlan WritePlan,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationContactReceipt(
    string Schema,
    string ReceiptId,
    string StepId,
    CharacterWorkspaceId WorkspaceId,
    Guid ContactId,
    string IdempotencyKeyDigest,
    string CommandDigest,
    long PreviousWorkspaceRevision,
    long WorkspaceRevision,
    long PreviousContentRevision,
    long ContentRevision,
    long PreviousSavedRevision,
    long SavedRevision,
    string ContentDigestBefore,
    string ContentDigestAfter,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    int ContactPointsBefore,
    int ContactPointsAfter,
    int ContactPointsRemaining,
    int HighPlacesPointsBefore,
    int HighPlacesPointsAfter,
    int HighPlacesPointsRemaining,
    CharacterCreationContactAtomicWritePlan WritePlan,
    string ReceiptDigest);

public sealed record CharacterCreationContactReceiptLedgerEntry(
    string IdempotencyKeyDigest,
    string CommandDigest,
    CharacterCreationContactReceipt Receipt);

public sealed record CharacterCreationContactResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class
{
    public bool Success => Outcome is CharacterCreationContactOutcomes.Available
        or CharacterCreationContactOutcomes.Applied
        or CharacterCreationContactOutcomes.Replayed;
}
