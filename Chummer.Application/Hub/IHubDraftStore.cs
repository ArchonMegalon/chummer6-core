using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubDraftStore
{
    IReadOnlyList<HubDraftRecord> List(OwnerScope owner, string? kind = null, string? rulesetId = null, string? state = null);

    HubDraftRecord? Get(OwnerScope owner, string draftId);

    HubDraftRecord? Get(OwnerScope owner, string kind, string projectId, string rulesetId);

    HubDraftRecord Upsert(OwnerScope owner, HubDraftRecord record);

    bool Delete(OwnerScope owner, string draftId);
}
