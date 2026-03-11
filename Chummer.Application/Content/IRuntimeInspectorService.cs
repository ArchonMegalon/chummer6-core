using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuntimeInspectorService
{
    RuntimeInspectorProjection? GetProfileProjection(OwnerScope owner, string profileId, string? rulesetId = null);
}
