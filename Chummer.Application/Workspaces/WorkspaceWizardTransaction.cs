using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces.TypedWizard;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public static class WorkspaceWizardTransaction
{
    public static WorkspaceWizardAdvancementCatalog ReadAdvancement(WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver sources, bool attributes)
    {
        if (attributes)
        {
            var value = CareerAttributeAdvanceEditorProjector.Project(saved.Document.Content,saved.Id,saved.ContentRevision,null,sources);
            return new(WorkspaceWizardOutcome.Available,saved.Id,saved.ContentRevision,value.Attributes,[],value.OmittedAttributeCount,
                value.RecoverableReceipts,value.OmittedReceiptCount);
        }
        var skills = CareerActiveSkillAdvanceEditorProjector.Project(saved.Document.Content,saved.Id,saved.ContentRevision,null,sources);
        return new(WorkspaceWizardOutcome.Available,saved.Id,saved.ContentRevision,[],skills.Skills,skills.OmittedSkillCount,[],0);
    }
    public static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string RequestDigest(WorkspaceWizardRequest request) => Hash(request);
    public static Guid OperationIdentity(CharacterWorkspaceId id, long revision, WorkspaceWizardAction action)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(Hash(new { id, revision, action })))[..16]);
    public static bool Valid(WorkspaceWizardRequest? request) => request is { ExpectedContentRevision: > 0 and < long.MaxValue }
        && !string.IsNullOrWhiteSpace(request.WorkspaceId.Value) && request.OperationId != Guid.Empty
        && (request.Action switch
        {
            WorkspaceCalendarAdd a => a.NewIdentity is not null,
            WorkspaceCalendarEdit a => a.ExpectedWeek is not null,
            WorkspaceCalendarDelete a => a.ExpectedWeek is not null,
            WorkspaceEdgeUse a => a.ExpectedState is not null,
            WorkspaceWeaponFire a => a.Identity is not null,
            WorkspaceAttributeAdvance a => a.ExpectedAttribute is not null,
            WorkspaceActiveSkillAdvance a => a.ExpectedSkill is not null,
            WorkspaceAttributeCorrection a => a.OriginalReceipt is not null,
            _ => false
        });
    // Detach nested quote collections before binding or executing them. A caller
    // retaining an array must not change a prepared command during publication.
    public static WorkspaceWizardRequest Freeze(WorkspaceWizardRequest request)
        => JsonSerializer.Deserialize<WorkspaceWizardRequest>(JsonSerializer.SerializeToUtf8Bytes(request))
            ?? throw new InvalidDataException("Wizard request is unavailable.");
    public static bool ValidLedger(CharacterWorkspaceId id, long revision, IReadOnlyList<WorkspaceWizardReceipt>? receipts)
        => receipts is null || receipts.All(r => r is not null) && receipts.Select(r => r.OperationId).Distinct().Count() == receipts.Count
           && receipts.All(r => r is not null && r.WorkspaceId == id && !string.IsNullOrWhiteSpace(r.OwnerId) && r.OperationId != Guid.Empty
             && r.PreviousRevision > 0 && r.PreviousRevision < long.MaxValue && r.CommittedRevision == r.PreviousRevision + 1
             && r.CommittedRevision <= revision && Hex(r.CommandDigest) && Hex(r.PayloadDigest)
             && (r.AttributeReceipt is null || CharacterCareerAttributeAdvanceRules.IsCoherent(r.AttributeReceipt))
             && (r.AttributeCorrection is null || CharacterCareerAttributeAdvanceRules.IsCoherent(r.AttributeCorrection))
             && r.ReceiptDigest == Hash(r with { ReceiptDigest = "" }));
    private static bool Hex(string? value) => value is { Length:64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static WorkspaceWizardResult Lookup(WorkspaceStoredDocument saved, string owner, WorkspaceWizardRequest request)
    {
        var rows = saved.Document.AuxiliaryState.WorkspaceWizardReceipts;
        if (!ValidLedger(saved.Id, saved.ContentRevision, rows)) throw new InvalidDataException("Wizard receipt history is invalid.");
        var prior = rows?.SingleOrDefault(r => r.OperationId == request.OperationId);
        if (prior is null) return new(WorkspaceWizardOutcome.NotFound);
        return prior.OwnerId == owner && prior.CommandDigest == RequestDigest(request)
            ? new(WorkspaceWizardOutcome.Replayed, Receipt: prior)
            : new(WorkspaceWizardOutcome.Rejected, Error: "The wizard operation identity was already used for another request.");
    }
    // Uses the existing typed validators and XML adapters. The store supplies
    // retained sources after fresh aggregate admission; this never accepts XML
    // or a calculation delegate from the caller of the workspace capability.
    public static WorkspaceDocument Build(WorkspaceStoredDocument saved, WorkspaceWizardRequest r,
        string owner, ICharacterSourceDataResolver retainedSources, out WorkspaceWizardReceipt receipt)
    {
        if (!Valid(r) || r.WorkspaceId != saved.Id || r.ExpectedContentRevision != saved.ContentRevision
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml || saved.Document.RulesetId != "sr5")
            throw new InvalidOperationException("The typed wizard target changed.");
        var xml = saved.Document.Content;
        CharacterCareerAttributeAdvanceReceipt? attributeReceipt = null;
        CharacterCareerAttributeCorrectionPlan? correction = null;
        string result = r.Action switch
        {
            WorkspaceCalendarAdd a => CareerCalendarMutation.Add(xml, new(r.WorkspaceId,r.ExpectedContentRevision,
                a.CalendarRevision,a.SourceAuthorityDigest,a.NewIdentity,a.FirstYear,a.FirstWeek)),
            WorkspaceCalendarEdit a => CareerCalendarMutation.Edit(xml,new(r.WorkspaceId,r.ExpectedContentRevision,
                a.CalendarRevision,a.SourceAuthorityDigest,a.ExpectedWeek,a.LogicalRevision,a.SourceRevision,a.Notes,a.NotesColor)),
            WorkspaceCalendarDelete a => CareerCalendarMutation.Delete(xml,new(r.WorkspaceId,r.ExpectedContentRevision,
                a.CalendarRevision,a.SourceAuthorityDigest,a.ExpectedWeek,a.LogicalRevision,a.SourceRevision,a.Confirmed)),
            WorkspaceEdgeUse a => TableMutation.ApplyCareerEdgeUseEdit(xml,new(r.WorkspaceId,r.ExpectedContentRevision,a.ExpectedState,a.Action)),
            WorkspaceWeaponFire a => TableMutation.ApplyCareerWeaponFire(xml,new(r.WorkspaceId,r.ExpectedContentRevision,
                a.Identity,a.NodeRevision,a.Mode,a.ConfirmedPartial)),
            WorkspaceActiveSkillAdvance a => CareerActiveSkillAdvanceMutation.Apply(xml,new(r.WorkspaceId,r.ExpectedContentRevision,
                a.ExpectedSkill,a.RuleDigest,a.Confirmed,a.ExpenseId,a.ExpenseDateLocal), null, retainedSources),
            WorkspaceAttributeAdvance a => Attribute(a),
            WorkspaceAttributeCorrection a => Correct(a),
            _ => throw new InvalidOperationException("Unsupported typed wizard operation.")
        };
        var rows = saved.Document.AuxiliaryState.WorkspaceWizardReceipts ?? [];
        if (!ValidLedger(saved.Id,saved.ContentRevision,rows)
            || rows.Any(p => p.OperationId == r.OperationId) || result == xml)
            throw new InvalidOperationException("Wizard receipt identity or mutation is invalid.");
        receipt = new(saved.Id,owner,r.OperationId,RequestDigest(r),saved.ContentRevision,saved.ContentRevision + 1,
            Hash(result),attributeReceipt,"",correction);
        receipt = receipt with { ReceiptDigest = Hash(receipt) };
        return saved.Document with { State = saved.Document.State with { Payload = result,
            AuxiliaryState = saved.Document.AuxiliaryState with { WorkspaceWizardReceipts = [..rows,receipt] } } };
        string Correct(WorkspaceAttributeCorrection a)
        {
            var applied = CareerAttributeAdvanceMutation.Correct(xml,new(r.WorkspaceId,r.ExpectedContentRevision,
                a.OriginalReceipt,a.ReceiptDigest,a.Confirmed,a.CorrectionId,a.Reason),null,retainedSources);
            correction = applied.Correction; return applied.Xml;
        }
        string Attribute(WorkspaceAttributeAdvance a)
        {
            var applied = CareerAttributeAdvanceMutation.Apply(xml,new(r.WorkspaceId,r.ExpectedContentRevision,
                a.ExpectedAttribute,a.LogicalRevision,a.SourceRevision,a.RuleDigest,a.Confirmed,a.ExpenseId,a.ExpenseDateLocal),null,retainedSources);
            attributeReceipt = applied.Receipt; return applied.Xml;
        }
    }
}
