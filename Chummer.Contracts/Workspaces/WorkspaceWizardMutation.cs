using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Workspaces;

// Closed domain commands. No document, XML patch, resolver or caller-provided
// mutation delegate crosses the workspace boundary.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]
[JsonDerivedType(typeof(WorkspaceCalendarAdd), "calendar-add")]
[JsonDerivedType(typeof(WorkspaceCalendarEdit), "calendar-edit")]
[JsonDerivedType(typeof(WorkspaceCalendarDelete), "calendar-delete")]
[JsonDerivedType(typeof(WorkspaceEdgeUse), "edge")]
[JsonDerivedType(typeof(WorkspaceWeaponFire), "fire")]
[JsonDerivedType(typeof(WorkspaceAttributeAdvance), "attribute")]
[JsonDerivedType(typeof(WorkspaceActiveSkillAdvance), "active-skill")]
[JsonDerivedType(typeof(WorkspaceAttributeCorrection), "attribute-correction")]
public abstract record WorkspaceWizardAction;
public sealed record WorkspaceCalendarAdd(string CalendarRevision, string SourceAuthorityDigest,
    CharacterCareerCalendarWeekIdentity NewIdentity, int FirstYear, int FirstWeek) : WorkspaceWizardAction;
public sealed record WorkspaceCalendarEdit(string CalendarRevision, string SourceAuthorityDigest,
    CharacterCareerCalendarWeekState ExpectedWeek, string LogicalRevision, string SourceRevision,
    string Notes, string NotesColor) : WorkspaceWizardAction;
public sealed record WorkspaceCalendarDelete(string CalendarRevision, string SourceAuthorityDigest,
    CharacterCareerCalendarWeekState ExpectedWeek, string LogicalRevision, string SourceRevision,
    bool Confirmed) : WorkspaceWizardAction;
public sealed record WorkspaceEdgeUse(CharacterCareerEdgeUseState ExpectedState,
    CharacterCareerEdgeUseAction Action) : WorkspaceWizardAction;
public sealed record WorkspaceWeaponFire(CharacterWeaponFireIdentity Identity, string NodeRevision,
    CharacterWeaponFireMode Mode, bool ConfirmedPartial) : WorkspaceWizardAction;
public sealed record WorkspaceAttributeAdvance(CharacterCareerAttributeAdvanceQuote ExpectedAttribute,
    string LogicalRevision, string SourceRevision, string RuleDigest, bool Confirmed,
    Guid ExpenseId, DateTime ExpenseDateLocal) : WorkspaceWizardAction;
public sealed record WorkspaceAttributeCorrection(CharacterCareerAttributeAdvanceReceipt OriginalReceipt,
    string ReceiptDigest, bool Confirmed, Guid CorrectionId, string Reason) : WorkspaceWizardAction;
public sealed record WorkspaceActiveSkillAdvance(CharacterCareerActiveSkillAdvanceQuote ExpectedSkill,
    string RuleDigest, bool Confirmed, Guid ExpenseId, DateTime ExpenseDateLocal) : WorkspaceWizardAction;
public sealed record WorkspaceWizardRequest(CharacterWorkspaceId WorkspaceId, long ExpectedContentRevision,
    Guid OperationId, WorkspaceWizardAction Action);
public sealed record WorkspaceWizardCommand(WorkspaceWizardRequest Request, string BindingDigest);
public enum WorkspaceWizardOutcome { Available, Legacy, Applied, Replayed, Rejected, Unknown, NotFound }
public sealed record WorkspaceWizardReceipt(CharacterWorkspaceId WorkspaceId, string OwnerId, Guid OperationId,
    string CommandDigest, long PreviousRevision, long CommittedRevision, string PayloadDigest,
    CharacterCareerAttributeAdvanceReceipt? AttributeReceipt, string ReceiptDigest,
    CharacterCareerAttributeCorrectionPlan? AttributeCorrection = null);
public sealed record WorkspaceWizardResult(WorkspaceWizardOutcome Outcome, WorkspaceWizardCommand? Command = null,
    WorkspaceWizardReceipt? Receipt = null, string? Error = null)
{
    public bool Applied => Outcome is WorkspaceWizardOutcome.Applied or WorkspaceWizardOutcome.Replayed;
}
public sealed record WorkspaceWizardAdvancementCatalog(WorkspaceWizardOutcome Outcome, CharacterWorkspaceId WorkspaceId,
    long ContentRevision, IReadOnlyList<CharacterCareerAttributeAdvanceQuote> Attributes,
    IReadOnlyList<CharacterCareerActiveSkillAdvanceQuote> ActiveSkills, int OmittedCount,
    IReadOnlyList<CharacterCareerAttributeAdvanceReceipt> RecoverableReceipts, int OmittedReceiptCount,
    string? Error = null);
