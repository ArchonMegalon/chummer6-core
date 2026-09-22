using Chummer.Application.Owners;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.LifeModules;

/// <summary>Reads retained chapters without reopening Creation or issuing a mutation.</summary>
public interface IOwnerBoundLifeModuleBookService
{
    LifeModuleOriginDossierResult<OriginStoryArcSeed> Load(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId, long contentRevision, long savedRevision);
}
