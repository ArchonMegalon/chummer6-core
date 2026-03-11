using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubModerationCaseStore
{
    IReadOnlyList<HubModerationCaseRecord> List(OwnerScope owner, string? kind = null, string? rulesetId = null, string? state = null);

    HubModerationCaseRecord? GetByCaseId(OwnerScope owner, string caseId);

    HubModerationCaseRecord? GetByDraftId(OwnerScope owner, string draftId);

    HubModerationCaseRecord? Get(OwnerScope owner, string kind, string projectId, string rulesetId);

    HubModerationCaseRecord Upsert(OwnerScope owner, HubModerationCaseRecord record);

    bool DeleteByDraftId(OwnerScope owner, string draftId);
}
