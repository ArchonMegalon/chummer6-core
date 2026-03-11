using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public sealed class DefaultHubModerationService : IHubModerationService
{
    private readonly IHubModerationCaseStore _moderationCaseStore;

    public DefaultHubModerationService(IHubModerationCaseStore moderationCaseStore)
    {
        _moderationCaseStore = moderationCaseStore;
    }

    public HubPublicationResult<HubModerationQueue> ListQueue(OwnerScope owner, string? state = null)
    {
        IReadOnlyList<HubModerationQueueItem> items = _moderationCaseStore
            .List(owner, state: NormalizeStateOptional(state))
            .OrderByDescending(record => record.UpdatedAtUtc)
            .Select(record => new HubModerationQueueItem(
                CaseId: record.CaseId,
                DraftId: record.DraftId,
                ProjectKind: record.ProjectKind,
                ProjectId: record.ProjectId,
                RulesetId: record.RulesetId,
                Title: record.Title,
                OwnerId: record.OwnerId,
                PublisherId: record.PublisherId,
                State: record.State,
                CreatedAtUtc: record.CreatedAtUtc,
                Summary: record.Summary))
            .ToArray();

        return HubPublicationResult<HubModerationQueue>.Implemented(new HubModerationQueue(items));
    }

    public HubPublicationResult<HubModerationDecisionReceipt?> Approve(OwnerScope owner, string caseId, HubModerationDecisionRequest? request)
        => UpdateCaseState(owner, caseId, HubModerationStates.Approved, request);

    public HubPublicationResult<HubModerationDecisionReceipt?> Reject(OwnerScope owner, string caseId, HubModerationDecisionRequest? request)
        => UpdateCaseState(owner, caseId, HubModerationStates.Rejected, request);

    private HubPublicationResult<HubModerationDecisionReceipt?> UpdateCaseState(
        OwnerScope owner,
        string caseId,
        string state,
        HubModerationDecisionRequest? request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        HubModerationCaseRecord? existing = _moderationCaseStore.GetByCaseId(owner, caseId);
        if (existing is null)
        {
            return HubPublicationResult<HubModerationDecisionReceipt?>.Implemented(null);
        }

        HubModerationCaseRecord updated = _moderationCaseStore.Upsert(
            owner,
            existing with
            {
                State = state,
                Summary = NormalizeNotes(request?.Notes),
                Notes = NormalizeNotes(request?.Notes),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

        return HubPublicationResult<HubModerationDecisionReceipt?>.Implemented(
            new HubModerationDecisionReceipt(
                CaseId: updated.CaseId,
                DraftId: updated.DraftId,
                ProjectKind: updated.ProjectKind,
                ProjectId: updated.ProjectId,
                RulesetId: updated.RulesetId,
                OwnerId: updated.OwnerId,
                PublisherId: updated.PublisherId,
                State: updated.State,
                Notes: updated.Notes,
                UpdatedAtUtc: updated.UpdatedAtUtc));
    }

    private static string? NormalizeStateOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private static string? NormalizeNotes(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
