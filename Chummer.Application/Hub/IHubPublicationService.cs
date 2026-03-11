using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubPublicationService
{
    HubPublicationResult<HubPublishDraftList> ListDrafts(OwnerScope owner, string? kind = null, string? rulesetId = null, string? state = null);

    HubPublicationResult<HubDraftDetailProjection?> GetDraft(OwnerScope owner, string draftId);

    HubPublicationResult<HubPublishDraftReceipt> CreateDraft(OwnerScope owner, HubPublishDraftRequest? request);

    HubPublicationResult<HubPublishDraftReceipt?> UpdateDraft(OwnerScope owner, string draftId, HubUpdateDraftRequest? request);

    HubPublicationResult<HubPublishDraftReceipt?> ArchiveDraft(OwnerScope owner, string draftId);

    HubPublicationResult<bool> DeleteDraft(OwnerScope owner, string draftId);

    HubPublicationResult<HubProjectSubmissionReceipt> SubmitForReview(OwnerScope owner, string kind, string itemId, string? rulesetId, HubSubmitProjectRequest? request);
}
