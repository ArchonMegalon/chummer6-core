using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IBuildIdeaCardCatalogService
{
    IReadOnlyList<BuildIdeaCard> SearchBuildIdeas(OwnerScope owner, string routeType, string queryText, string? rulesetId = null, int maxCount = 5);

    BuildIdeaCard? GetBuildIdea(OwnerScope owner, string ideaId);
}
